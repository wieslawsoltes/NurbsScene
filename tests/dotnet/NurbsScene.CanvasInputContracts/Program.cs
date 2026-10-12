using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.IO;

var tests = new List<(string, Action)>();
var factory = new GeometryFactory(new ManagedNurbsEvaluator());
ComponentRegistry Registry() => StandardComponents.Create(factory);
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
GraphResult Solve(Graph g) => g.SolveAsync().GetAwaiter().GetResult();
CanvasCreation Parse(string query) { Check(CanvasCreationParser.TryParse(query, Registry(), out var c, out _)); return c!; }
DataTree Output(Graph g, string id) { var r = Solve(g); Check(r.ErrorCount == 0); return r.Nodes[id].Outputs["T"]; }
Test("plain and signed numeric shorthand creates exact slider settings", () => {
    foreach (var (text, value, digits) in new[] { ("5", 5d, 0), ("-3.50", -3.5, 2), (".25", .25, 2), ("1e-3", .001, 3) }) {
        var c = Parse(text); Check(c.Component == "Number" && (double)c.Parameters["value"]! == value && (int)c.Parameters["decimalPlaces"]! == digits);
        Check((double)c.Parameters["min"]! <= value && (double)c.Parameters["max"]! >= value);
    }
});
Test("range shorthand retains exact endpoints and selected precision", () => {
    var c = Parse(" -20 < 5.25 < 80 "); Check((double)c.Parameters["min"]! == -20 && (double)c.Parameters["max"]! == 80 && (double)c.Parameters["value"]! == 5.25);
    Check((int)c.Parameters["decimalPlaces"]! == 2);
});
Test("extreme and high precision literals do not round during creation", () => {
    var c = Parse("-1.7e308 < 0 < 1.7e308"); Check(double.IsFinite((double)c.Parameters["max"]!));
    c = Parse("0.123456789012345"); Check(!c.Parameters.ContainsKey("decimalPlaces") && (double)c.Parameters["value"]! == .123456789012345);
});
Test("invalid ranges and expressions fail with explicit diagnostics", () => {
    foreach (var q in new[] { "3<2<1", "0<4<3", "1<2", "0<1<2<3", "1e999", "1+2", "NaN", "Infinity", new string('x', 8193) })
        Check(!CanvasCreationParser.TryParse(q, Registry(), out var c, out var e) && c is null && !string.IsNullOrEmpty(e));
});
Test("operator and friendly-name aliases refer to real definitions", () => {
    foreach (var (q, id) in new[] { ("+", "Add"), ("-", "Subtract"), ("*", "Multiply"), ("/", "Divide"), ("^", "Power"), ("Number Slider", "Number"), ("Boolean Toggle", "Boolean"), ("circle", "Circle") }) Check(Parse(q).Component == id);
});
Test("Boolean and literal text shortcuts preserve values, whitespace and newlines", () => {
    Check((bool)Parse("TRUE").Parameters["value"]! && !(bool)Parse("false").Parameters["value"]!);
    Check(Parse("//a\nb \n").Parameters["text"] as string == "a\nb \n");
    Check(Parse("\"hello\"").Parameters["text"] as string == "hello");
    Check(Parse("//").Component == "Panel");
});
Test("literal creation is invariant across decimal cultures", () => {
    var previous = CultureInfo.CurrentCulture;
    try { CultureInfo.CurrentCulture = new("pl-PL"); Check((double)Parse("1.25").Parameters["value"]! == 1.25); Check(!CanvasCreationParser.TryParse("1,25", Registry(), out _, out _)); }
    finally { CultureInfo.CurrentCulture = previous; }
});
Test("creation is one undoable operation at the supplied canvas point", () => {
    var g = new Graph(Registry()); var s = new GraphSession(g); var changes = 0; g.Changed += () => changes++;
    var id = Parse("0<2.5<5").Apply(s, new(-200, 60)); Check(g.GetNode(id).X == -200 && changes == 1 && s.CanUndo);
    Check(s.Undo() && s.Graph.NodeCount == 0 && !s.CanUndo && s.Redo() && s.Graph.GetNode(id).Y == 60);
});
Test("missing registry implementations and bad coordinates cannot partially create", () => {
    var r = new ComponentRegistry(); Check(!CanvasCreationParser.TryParse("//x", r, out _, out _));
    var g = new Graph(Registry()); var s = new GraphSession(g); Throws<ArgumentException>(() => Parse("2").Apply(s, new(double.NaN, 0))); Check(g.NodeCount == 0 && !s.CanUndo);
});
Test("unconnected panel splits line endings and retains empty rows", () => {
    var g = new Graph(Registry()); var n = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "a\r\nb\rc\n" });
    Check(Output(g, n.Id).AllItems.Cast<string>().SequenceEqual(new[] { "a", "b", "c", "" }));
});
Test("single-item panel preserves its original newline representation", () => {
    var g = new Graph(Registry()); var n = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "a\r\nb", ["singleItem"] = true });
    Check(Output(g, n.Id).AllItems.Single() as string == "a\r\nb");
});
Test("connected empty tree overrides persistent panel text", () => {
    var g = new Graph(Registry()); var empty = g.Add("Merge"); var p = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "must not appear" });
    g.Connect(new(empty.Id, "R"), new(p.Id, "D")); Check(Output(g, p.Id).Branches.Length == 0);
    g.Disconnect(g.Wires.Single().Id); Check(Output(g, p.Id).AllItems.Single() as string == "must not appear");
});
Test("connected values retain paths and null and empty branches when converted", () => {
    var g = new Graph(Registry()); var t = new DataTree([new(new([2, 4]), [1d, true, null]), new(new([8]), [])]);
    var source = g.Add("Preview", parameters: new Dictionary<string, object?> { ["input:G"] = t }); var p = g.Add("Panel");
    g.Connect(new(source.Id, "G"), new(p.Id, "D")); var o = Output(g, p.Id);
    Check(o.Branches[0].Path.Equals(new DataPath([2, 4])) && o.Branches[0].Items.SequenceEqual(new object?[] { "1", "True", "<null>" }) && o.Branches[1].Items.IsEmpty);
});
Test("explicit persistent tree data overrides unconnected panel text", () => {
    var g = new Graph(Registry()); var p = g.Add("Panel", parameters: new Dictionary<string, object?> { ["input:D"] = DataTree.Empty, ["text"] = "not selected" });
    Check(Output(g, p.Id).ItemCount == 0);
});
Test("panel text is a typed source usable by downstream components", () => {
    var g = new Graph(Registry()); var p = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "one\ntwo" }); var length = g.Add("ListLength");
    g.Connect(new(p.Id, "T"), new(length.Id, "L")); var result = Solve(g); Check(result.ErrorCount == 0 && (int)result.Nodes[length.Id].Outputs["N"].AllItems.Single()! == 2);
});
Test("text conversion neither evaluates geometry nor invokes plugin code", () => {
    var curve = factory.Circle(new(2, 3, 4), 5); Check(InputComponents.Format(curve).StartsWith("NURBS curve"));
    Throws<ArgumentException>(() => InputComponents.Format(new HostileValue()));
});
Test("panel output budget fails with a component error", () => {
    var g = new Graph(Registry()); var p = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = new string('a', 65537) });
    Check(Solve(g).Nodes[p.Id].Status == "error");
});
Test("Boolean toggle changes typed outputs in one undo step without rewiring", () => {
    var g = new Graph(Registry()); var n = g.Add("Boolean"); var s = new GraphSession(g); var p = g.Add("Panel"); var w = g.Connect(new(n.Id, "B"), new(p.Id, "D"));
    Check(Output(g, p.Id).AllItems.Single() as string == "False"); var topology = g.TopologyRevision;
    Check(CanvasValueEditing.Toggle(s, g, n) && Output(g, p.Id).AllItems.Single() as string == "True" && g.TopologyRevision == topology && g.Wires.Single().Id == w.Id);
    Check(s.Undo() && !InputComponents.BooleanValue(s.Graph.GetNode(n.Id).Parameters));
});
Test("malformed Boolean metadata is diagnosed instead of silently False", () => {
    var g = new Graph(Registry()); var b = g.Add("Boolean", parameters: new Dictionary<string, object?> { ["value"] = "True" }); Check(Solve(g).Nodes[b.Id].Status == "error");
});
Test("text draft apply and no-op respect undo and stale owners", () => {
    var g = new Graph(Registry()); var n = g.Add("Panel"); var s = new GraphSession(g);
    Check(!CanvasValueEditing.SetText(s, g, n, "", false) && !s.CanUndo);
    Check(CanvasValueEditing.SetText(s, g, n, "a\nb", true)); Throws<InvalidOperationException>(() => CanvasValueEditing.SetText(s, g, n, "stale", false));
    Check(s.Undo() && Output(s.Graph, n.Id).AllItems.Single() as string == "" && s.CanRedo);
});
Test("Boolean and panel definitions survive exact clipboard and workspace interchange", () => {
    var g = new Graph(Registry()); var b = g.Add("Boolean", parameters: new Dictionary<string, object?> { ["value"] = true }); var p = g.Add("Panel"); g.Connect(new(b.Id, "B"), new(p.Id, "D"));
    var f = GraphFragmentFormat.Deserialize(factory.Evaluator, g.Registry, GraphFragmentFormat.Serialize(GraphFragment.Capture(g, g.Nodes.Select(n => n.Id)), g.Registry));
    var s = new GraphSession(new Graph(g.Registry)); var ids = f.Paste(s); Check(Output(s.Graph, ids[1]).AllItems.Single() as string == "True");
    var restored = WorkspaceFormat.Deserialize(factory.Evaluator, g.Registry, WorkspaceFormat.Serialize(new(), g)).Graph; Check(Output(restored, p.Id).AllItems.Single() as string == "True");
});
Test("panel preview is bounded, immutable and reused between frames", () => {
    var g = new Graph(Registry()); var n = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = string.Join('\n', Enumerable.Repeat(new string('x', 300), 100)) }); var r = Solve(g).Nodes[n.Id];
    var cache = new PanelPreviewCache(); var first = cache.Get(n, r); Check(first.Lines.Length <= 8 && first.Footer.StartsWith("100 items") && ReferenceEquals(first, cache.Get(n, r)) && cache.BuildCount == 1);
    g.Update(n with { Name = "Renamed" }); var next = cache.Get(g.GetNode(n.Id), r); Check(cache.BuildCount == 2 && !ReferenceEquals(first, next));
});
Test("preview handles Unicode text without partial surrogate output", () => {
    var g = new Graph(Registry()); var n = g.Add("Text", parameters: new Dictionary<string, object?> { ["text"] = string.Concat(Enumerable.Repeat("🌐é\n", 40)) }); var preview = new PanelPreviewCache().Get(n, Solve(g).Nodes[n.Id]);
    Check(preview.Lines.Length <= 8 && preview.Lines.All(l => !l.EndsWith('\ud83c')));
});
Test("search caches repeated queries and invalidates after registry growth", () => {
    var r = Registry(); var index = new CanvasSearchIndex(); var a = index.Search(r, "Circle"); Check(a.SequenceEqual(new[] { "Circle" }));
    Check(index.Search(r, "Circle") == a && index.BuildCount == 1);
    r.Register(new("CircleExtra", "Test", "", [], [], _ => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>())));
    Check(index.Search(r, "Circle").Length == 2 && index.BuildCount == 2);
});
Test("literal and empty searches are bounded with matching suggestions", () => {
    var r = Registry(); var index = new CanvasSearchIndex(); Check(index.Search(r, "").IsEmpty && index.Search(r, "//a").Single() == "//a" && index.Search(r, "+").Single() == "Add"); Check(index.Search(r, "").IsEmpty && index.Search(r, "Curve").Length <= 20);
});
Test("warmed search and preview reads allocate zero bytes", () => {
    var g = new Graph(Registry()); var n = g.Add("Panel"); var result = Solve(g).Nodes[n.Id]; var cache = new PanelPreviewCache(); var search = new CanvasSearchIndex();
    cache.Get(n, result); search.Search(g.Registry, "Circle"); var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) { cache.Get(n, result); search.Search(g.Registry, "Circle"); }
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before; Check(bytes == 0); Console.WriteLine($"METRIC preview-search-10000={bytes} B");
});
var failed = 0; foreach (var (name, run) in tests) try { run(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + "\n" + e); }
Console.WriteLine(JsonSerializer.Serialize(new { suite = "canvas-inputs", total = tests.Count, passed = tests.Count - failed, failed })); return failed == 0 ? 0 : 1;
sealed class HostileValue : IImmutableGraphValue { public override string ToString() => throw new Exception("Must never be called"); }
