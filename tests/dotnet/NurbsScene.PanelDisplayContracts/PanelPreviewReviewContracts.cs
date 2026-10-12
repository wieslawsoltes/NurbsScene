using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

internal static class PanelPreviewReviewContracts
{
    public static void Register(List<(string Name, Action Run)> tests, ComponentRegistry registry)
    {
        void Test(string name, Action action) => tests.Add((name, action));
        void Check(bool value, string message = "Preview review assertion failed") { if (!value) throw new Exception(message); }
        GraphNode Node(bool wrap = true, bool indices = false) => new Graph(registry).Add("Panel") with { PanelDisplay = new(false, indices, wrap) };
        NodeResult Result(string value) => new(ImmutableDictionary<string, DataTree>.Empty.Add("T", DataTree.Item(value)), "ok", [], 0);
        foreach (var (label, prefix, suffix) in new[] {
            ("combining character", new string('x', 2047) + "e", "\u0301tail"),
            ("joined emoji", new string('x', 2045) + "👩\u200d", "💻tail"),
            ("regional indicator flag", new string('x', 2046) + "🇵", "🇱tail") })
        {
            Test("scan cutoff never publishes a partial " + label, () => {
                var p = new PanelPreviewCache().Get(Node(), Result(prefix + suffix), 64, 128);
                var joined = string.Concat(p.Lines);
                Check(p.Truncated && joined.EndsWith('…') && joined[..^1].All(c => c == 'x'), joined);
            });
        }
        Test("oversized complete graphemes are marked as display substitutions", () => {
            var source = "a" + new string('\u0301', 100) + "ok";
            var r = Result(source); var p = new PanelPreviewCache().Get(Node(), r);
            Check(p.Lines.Single() == "�ok" && p.Truncated && ReferenceEquals(r.Outputs["T"].AllItems.Single(), source));
        });
        Test("invalid surrogate display does not leak malformed UTF16 or rewrite data", () => {
            var source = "a\ud800b\udc00c"; var r = Result(source);
            var p = new PanelPreviewCache().Get(Node(), r);
            Check(p.Lines.Single() == "a�b�c" && p.Truncated && ReferenceEquals(r.Outputs["T"].AllItems.Single(), source));
        });
        Test("full width rows preserve explicit newlines and trailing empty rows", () => {
            foreach (var wrap in new[] { false, true }) {
                var p = new PanelPreviewCache().Get(Node(wrap), Result("abcd\r\nxy\n"), 3, 4);
                Check(p.Lines.SequenceEqual(new[] { "abcd", "xy", "" }) && !p.Truncated);
                var clipped = new PanelPreviewCache().Get(Node(wrap), Result("abcd\r\nxy\n"), 2, 4);
                Check(clipped.Lines.SequenceEqual(new[] { "abcd", "xy" }) && clipped.Truncated);
            }
        });
        Test("clipped tails still find later CR LF and CRLF content", () => {
            var value = new string('x', 600) + "\r" + new string('y', 600) + "\n" + new string('z', 600) + "\r\nend";
            var p = new PanelPreviewCache().Get(Node(false), Result(value), 8, 4);
            Check(p.Lines.SequenceEqual(new[] { "xxx…", "yyy…", "zzz…", "end" }) && p.Truncated);
        });
        Test("changed unwrapped preview does not allocate a string for invisible characters", () => {
            var n = Node(false); var r = Result(new string('x', 1900) + "\r\nyz"); var c = new PanelPreviewCache();
            for (var i = 0; i < 100; i++) c.Get(n, r, 8, 12 + i % 2);
            var before = GC.GetAllocatedBytesForCurrentThread(); var p = c.Get(n, r, 8, 14);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"METRIC changed-unwrapped-preview={allocated} B");
            Check(allocated < 8192 && p.Lines.SequenceEqual(new[] { "xxxxxxxxxxxxx…", "yz" }), $"Allocated {allocated} B");
        });
        Test("changed wrapped preview allocates row strings rather than character strings", () => {
            var n = Node(); var r = Result(new string('x', 2048)); var c = new PanelPreviewCache();
            for (var i = 0; i < 100; i++) c.Get(n, r, 64, 32 + i % 2);
            var before = GC.GetAllocatedBytesForCurrentThread(); var p = c.Get(n, r, 64, 32);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"METRIC changed-wrapped-preview={allocated} B");
            Check(allocated < 16000 && p.Lines.Length == 64 && !p.Truncated, $"Allocated {allocated} B");
        });
        Test("short Unicode previews agree with an independent text-element oracle", () => {
            string[] alphabet = ["a", "bc", "🌐", "e\u0301", "👩‍💻", "\r", "\n", "\r\n", "\t", "中", "🇵🇱"];
            var random = new Random(204809); var checks = 0;
            for (var i = 0; i < 1000; i++) {
                var text = string.Concat(Enumerable.Range(0, random.Next(25)).Select(_ => alphabet[random.Next(alphabet.Length)]));
                var columns = random.Next(1, 18); var rows = random.Next(1, 18); var wrap = random.Next(2) == 0; var indexed = random.Next(2) == 0;
                var actual = new PanelPreviewCache().Get(Node(wrap, indexed), Result(text), rows, columns);
                var expected = Reference((indexed ? "0: " : "") + text, rows, columns, wrap);
                Check(actual.Lines.SequenceEqual(expected.Lines) && actual.Truncated == expected.Truncated, $"Oracle mismatch at {i}"); checks++;
            }
            Console.WriteLine($"METRIC Unicode-oracle-cases={checks}");
        });
        Test("a cutoff cannot emit an unbounded combining sequence or exceed row budget", () => {
            var r = Result("a" + new string('\u0301', 60000));
            foreach (var wrap in new[] { false, true }) {
                var p = new PanelPreviewCache().Get(Node(wrap), r, 64, 128);
                Check(p.Truncated && p.Lines.Length <= 64 && p.Lines.Sum(s => s.Length) < 100);
            }
        });
        Test("old immutable previews and zero-allocation warm hits survive a rebuild", () => {
            var n = Node(); var first = Result("old text"); var second = Result("new text"); var cache = new PanelPreviewCache();
            var old = cache.Get(n, first); var next = cache.Get(n, second);
            Check(old.Lines.Single() == "old text" && next.Lines.Single() == "new text" && !ReferenceEquals(old, next));
            for (var i = 0; i < 100; i++) cache.Get(n, second);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) cache.Get(n, second);
            Check(GC.GetAllocatedBytesForCurrentThread() - before == 0 && ReferenceEquals(next, cache.Current));
        });
    }

    // Deliberately uses allocating complete-text enumeration, not production's
    // bounded span scanner. Random inputs stay below scan/substitution budgets.
    private static PanelPreview Reference(string source, int rows, int columns, bool wrap)
    {
        var logical = new List<List<string>> { new() };
        var enumerator = StringInfo.GetTextElementEnumerator(source);
        while (enumerator.MoveNext()) {
            var e = enumerator.GetTextElement();
            if (e is "\r" or "\n" or "\r\n") logical.Add([]);
            else logical[^1].Add(e == "\t" ? " " : e);
        }
        var output = new List<string>(); var truncated = false;
        foreach (var line in logical) {
            if (!wrap) {
                truncated |= line.Count > columns;
                output.Add(line.Count > columns ? string.Concat(line.Take(columns - 1)) + "…" : string.Concat(line));
            } else if (line.Count == 0) output.Add("");
            else for (var start = 0; start < line.Count; start += columns) output.Add(string.Concat(line.Skip(start).Take(columns)));
        }
        return new(output.Take(rows).ToImmutableArray(), "") { Truncated = truncated || output.Count > rows };
    }
}
