using System.Collections.Immutable;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

internal static class PanelScrollingContracts
{
    public static void Register(List<(string Name, Action Run)> tests, ComponentRegistry registry)
    {
        void Test(string name, Action run) => tests.Add(("scroll: " + name, run));
        static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
        static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        var node = new Graph(registry).Add("Panel") with { PanelDisplay = new(false, false, false) };
        static NodeResult Result(DataTree tree) => new(ImmutableDictionary<string, DataTree>.Empty.Add("T", tree), "ok", [], 0);
        static DataTree Items(int count) => DataTree.List(Enumerable.Range(0, count).Select(i => (object?)$"item-{i:D6}"));
        Test("empty branches and per-branch indices are mapped exactly", () => {
            var tree = new DataTree([new(new([0]), []), new(new([2]), ["a", "b"]), new(new([5]), []), new(new([8]), ["c"]), new(new([9]), [])]);
            var index = new PanelRecordIndex(tree);
            Check(index.Count(false) == 3 && index.Count(true) == 8);
            Check(index.Locate(0, false) == new PanelRecord(1, 0) && index.Locate(2, false) == new PanelRecord(3, 0));
            Check(index.Locate(0, true).IsPath && index.Locate(2, true) == new PanelRecord(1, 0) && index.Locate(7, true) == new PanelRecord(4, -1));
            Throws<ArgumentOutOfRangeException>(() => index.Locate(-1, true)); Throws<ArgumentOutOfRangeException>(() => index.Locate(8, true));
        });
        Test("random branch index agrees with independent flattening", () => {
            var random = new Random(1729);
            for (var trial = 0; trial < 200; trial++) {
                var branches = Enumerable.Range(0, random.Next(1, 50)).Select(i => new DataBranch(new([i]), Enumerable.Range(0, random.Next(0, 12)).Select(v => (object?)v).ToImmutableArray())).ToArray();
                var index = new PanelRecordIndex(new(branches));
                foreach (var paths in new[] { false, true }) {
                    var expected = new List<PanelRecord>();
                    for (var b = 0; b < branches.Length; b++) { if (paths) expected.Add(new(b, -1)); for (var i = 0; i < branches[b].Items.Length; i++) expected.Add(new(b, i)); }
                    Check(expected.Count == index.Count(paths)); for (var i = 0; i < expected.Count; i++) Check(index.Locate(i, paths) == expected[i]);
                }
            }
        });
        Test("window reaches the final item without blank overscroll", () => {
            var c = new PanelWindowCache(); var r = Result(Items(100)); var first = c.Get(node, r, 8, 24);
            Check(first.First == 0 && first.Visible == 8 && first.Total == 100 && first.MaximumOffset == 92);
            var last = c.Get(node, r, 8, 24, int.MaxValue);
            Check(last.First == 92 && last.Preview.Lines[0] == "item-000092" && last.Preview.Lines[^1] == "item-000099" && last.Preview.Truncated);
            Check(c.Get(node, r, 8, 24, int.MinValue).First == 0 && c.IndexBuilds == 1);
        });
        Test("final page accounts for scan units as well as rows", () => {
            var c = new PanelWindowCache(); var r = Result(DataTree.List(Enumerable.Range(0, 20).Select(i => (object?)(i + new string('x', 3000)))));
            var last = c.Get(node, r, 64, 10, int.MaxValue);
            Check(last.First == 16 && last.Visible == 4 && last.Preview.Lines[^1].StartsWith("19"));
        });
        Test("wrapped items remain reachable and report bounded truncation", () => {
            var n = node with { PanelDisplay = new(false, true, true) };
            var c = new PanelWindowCache(); var r = Result(DataTree.List(["a", new string('x', 60000), "last"]));
            var middle = c.Get(n, r, 3, 8, 1); Check(middle.First == 1 && middle.Preview.Truncated && middle.Preview.Lines.Length == 3);
            var last = c.Get(n, r, 3, 8, int.MaxValue); Check(last.Preview.Lines[^1].Contains("last") && last.First == 2);
            Check(((string)r.Outputs["T"].Branches[0].Items[1]!).Length == 60000);
        });
        Test("indices identify source items after a distant seek", () => {
            var c = new PanelWindowCache(); var n = node with { PanelDisplay = new(false, true, false) };
            var p = c.Get(n, Result(Items(1000)), 4, 28, 723);
            Check(p.Preview.Lines[0] == "723: item-000723" && p.First == 723);
        });
        Test("hidden empty branches do not consume the visible window", () => {
            var tree = new DataTree(Enumerable.Range(0, 1000).Select(i => new DataBranch(new([i]), i == 999 ? ["last"] : [])));
            var c = new PanelWindowCache(); var r = Result(tree); var p = c.Get(node, r, 4, 24);
            Check(p.Preview.Lines.SequenceEqual(new[] { "last" }) && !p.CanScroll && p.Total == 1);
            var paths = c.Get(node with { PanelDisplay = new(true, false, false) }, r, 4, 24, int.MaxValue);
            Check(paths.Preview.Lines[^1] == "last" && paths.Total == 1001 && c.IndexBuilds == 1);
        });
        Test("font alignment name and position do not invalidate item windows", () => {
            var c = new PanelWindowCache(); var r = Result(Items(100)); var p = c.Get(node, r, 8, 24, 12);
            Check(ReferenceEquals(p, c.Get(node with { X = 50, Name = "new", PanelStyle = new() { Bold = true } }, r, 8, 24, 12)) && c.BuildCount == 1);
        });
        Test("changed outputs clamp offsets and invalidate the prefix index", () => {
            var c = new PanelWindowCache(); c.Get(node, Result(Items(100)), 8, 24, 90);
            var p = c.Get(node, Result(Items(3)), 8, 24, 90);
            Check(p.First == 0 && p.Visible == 3 && !p.CanScroll && c.IndexBuilds == 2);
        });
        Test("error and recovery cannot reuse a stale fallback", () => {
            var c = new PanelWindowCache(); var r = Result(DataTree.Empty); c.Get(node, r, 8, 24);
            var error = new NodeResult(ImmutableDictionary<string, DataTree>.Empty, "error", ["bad"], 0);
            Check(c.Get(node, error, 8, 24).Preview.Lines.Single() == "bad");
            Check(c.Get(node, r, 8, 24).Preview.Lines.IsEmpty && c.Get(node, null, 8, 24).Preview.Footer == "Waiting for solution");
        });
        Test("large branch seeks format bounded records, not skipped items", () => {
            var r = Result(new(Enumerable.Range(0, 100000).Select(i => new DataBranch(new([i]), [(object?)$"v{i}"]))));
            var c = new PanelWindowCache(); c.Get(node, r, 8, 24); var count = c.FormattedRecords;
            var p = c.Get(node, r, 8, 24, 98765);
            Check(p.Preview.Lines[0] == "v98765" && c.FormattedRecords - count == 8 && c.IndexBuilds == 1);
        });
        Test("warm window lookup allocates zero managed bytes", () => {
            var c = new PanelWindowCache(); var r = Result(Items(100)); c.Get(node, r, 8, 24, 20);
            for (var i = 0; i < 100; i++) c.Get(node, r, 8, 24, 20);
            var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 10000; i++) c.Get(node, r, 8, 24, 20);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before; Check(allocated == 0); Console.WriteLine($"METRIC panel-window-10000={allocated} B");
        });
        Test("scrollbar maps endpoints and retains grab offset", () => {
            var w = new PanelWindow(new([], ""), 40, 10, 100, 90); var b = PanelScrollBar.Create(new(20, 100, 10, 200), w, 12);
            Check(Math.Abs(b.Grip.Height - 20) < 1e-9 && b.Drag(b.Grip.Y + 7, 7) == 40 && b.Drag(-1000, 7) == 0 && b.Drag(1000, 7) == 90);
            var tiny = PanelScrollBar.Create(new(0, 0, 3, 4), w, 12); Check(tiny.Grip.Height == 4 && tiny.Drag(9, 1) == 40);
        });
        Test("scroll movement saturates without integer overflow", () => {
            Check(PanelScrollBar.Move(5, long.MinValue, 10) == 0 && PanelScrollBar.Move(5, long.MaxValue, 10) == 10 && PanelScrollBar.Move(5, -2, 10) == 3);
            Throws<ArgumentOutOfRangeException>(() => PanelScrollBar.Move(-1, 1, 10));
            Throws<ArgumentException>(() => PanelScrollBar.Create(new(0, 0, 0, 10), new(new([], ""), 0, 0, 0, 0), 8));
            Throws<ArgumentOutOfRangeException>(() => new PanelWindowCache().Get(node, null, 0, 8));
        });
        Test("completed paint identity includes the displayed offset", () => {
            var g = new Graph(registry); var n = g.Add("Panel"); var r = Result(Items(100));
            var stamp = new PanelPaintStamp(g, n, r, 1, new(0, 0, 100, 100)) { ScrollOffset = 12 };
            Check(stamp.Matches(stamp) && !stamp.Matches(stamp with { ScrollOffset = 13 }));
            Check(!stamp.Matches(stamp with { Result = Result(Items(100)) }));
        });
        Test("scroll operations never change graph history or solved identity", () => {
            var g = new Graph(registry); var n = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "a\nb\nc\nd" });
            var s = new GraphSession(g); var r = g.SolveAsync().GetAwaiter().GetResult(); var revision = g.Revision;
            var c = new PanelWindowCache(); c.Get(n, r.Nodes[n.Id], 2, 20, 0); c.Get(n, r.Nodes[n.Id], 2, 20, int.MaxValue);
            Check(g.Revision == revision && ReferenceEquals(g.LastResult, r) && !s.CanUndo && !s.CanRedo);
        });
    }
}
