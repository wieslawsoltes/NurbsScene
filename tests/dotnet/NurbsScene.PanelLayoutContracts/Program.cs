using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.IO;

var tests = new List<(string Name, Action Run)>();
var factory = new GeometryFactory(new ManagedNurbsEvaluator());
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Graph Graph() => new(StandardComponents.Create(factory));
GraphResult Solve(Graph graph) => graph.SolveAsync().GetAwaiter().GetResult();
Test("default panel and legacy text bounds remain compatible", () => {
    var g = Graph(); var p = g.Add("Panel", 20, 30); var t = g.Add("Text");
    Check(PanelLayout.Bounds(p) == new CanvasBox(20, 30, 224, 168));
    Check(PanelLayout.Bounds(t) == new CanvasBox(0, 0, 156, 76));
    Check(!PanelLayout.CanResize(g.Add("Number")));
});
Test("resize draft and cancellation never mutate a graph", () => {
    var g = Graph(); var p = g.Add("Panel"); var session = new GraphSession(g); var revision = g.Revision;
    var draft = new PanelResizeDraft(g, p.Id); draft.Move(80, 35);
    Check(draft.Bounds.Width == 304 && draft.Bounds.Height == 203 && g.Revision == revision && ReferenceEquals(g.GetNode(p.Id), p) && !session.CanUndo);
});
Test("resize commit is one revision, notification and undo record", () => {
    var g = Graph(); var p = g.Add("Panel"); var s = new GraphSession(g); var revision = g.Revision; var notifications = 0; g.Changed += () => notifications++;
    var draft = new PanelResizeDraft(g, p.Id); draft.Move(100, 60); Check(draft.Commit(s));
    Check(g.GetNode(p.Id).CanvasSize == new NodeCanvasSize(324, 228) && g.Revision == revision + 1 && notifications == 1 && s.CanUndo);
    Check(s.Undo() && s.Graph.GetNode(p.Id).CanvasSize is null && !s.CanUndo && s.Redo() && s.Graph.GetNode(p.Id).CanvasSize?.Width == 324);
});
Test("unchanged resize preserves absent dimensions and redo history", () => {
    var g = Graph(); var p = g.Add("Panel"); var s = new GraphSession(g); s.Edit("Move", x => x.Update(p with { X = 5 })); s.Undo();
    var draft = new PanelResizeDraft(s.Graph, p.Id); var revision = s.Graph.Revision; draft.Move(0, 0);
    Check(!draft.Commit(s) && s.Graph.Revision == revision && s.CanRedo && s.Graph.GetNode(p.Id).CanvasSize is null);
});
Test("size edits reuse numerical outputs and preserve execution topology", () => {
    var g = Graph(); var p = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "A\nB" }); var n = g.Add("ListLength"); g.Connect(new(p.Id, "T"), new(n.Id, "L"));
    var original = Solve(g); var topology = g.TopologyRevision; var plans = g.ExecutionPlanBuilds;
    var draft = new PanelResizeDraft(g, p.Id); draft.Move(200, 100); draft.Commit(new(g)); var after = Solve(g);
    Check(after.Evaluated == 0 && ReferenceEquals(original.Nodes, after.Nodes) && g.TopologyRevision == topology && g.ExecutionPlanBuilds == plans);
});
Test("stale draft cannot overwrite external target edits or graph replacement", () => {
    var g = Graph(); var p = g.Add("Panel"); var s = new GraphSession(g); var draft = new PanelResizeDraft(g, p.Id); draft.Move(80, 40);
    g.Update(p with { Name = "External edit" }); Throws<InvalidOperationException>(() => draft.Commit(s)); Check(!s.CanUndo && g.GetNode(p.Id).CanvasSize is null);
    draft = new(g, p.Id); draft.Move(80, 40); s.Replace(Graph()); Throws<InvalidOperationException>(() => draft.Commit(s)); Check(s.Graph.NodeCount == 0);
});
Test("independent edits survive resize commit and its undo", () => {
    var g = Graph(); var p = g.Add("Panel"); var s = new GraphSession(g); var draft = new PanelResizeDraft(g, p.Id); draft.Move(80, 40);
    var external = g.Add("Number"); draft.Commit(s); Check(s.Undo() && s.Graph.GetNode(external.Id).Component == "Number" && s.Graph.GetNode(p.Id).CanvasSize is null);
});
Test("finite size bounds and invalid drafts are validated before mutation", () => {
    var g = Graph(); var p = g.Add("Panel"); var revision = g.Revision;
    foreach (var size in new[] { new NodeCanvasSize(0, 50), new(double.NaN, 100), new(5000, 100), new(100, double.PositiveInfinity) })
        Throws<ArgumentOutOfRangeException>(() => g.Update(p with { CanvasSize = size }));
    Check(g.Revision == revision && ReferenceEquals(g.GetNode(p.Id), p));
    var draft = new PanelResizeDraft(g, p.Id); Throws<ArgumentException>(() => draft.Move(double.NaN, 0)); draft.Move(-1e200, 1e200);
    Check(draft.Bounds.Width == PanelLayout.MinimumWidth && draft.Bounds.Height == PanelLayout.MaximumDimension);
});
Test("resize handle is bounded and uses canvas coordinates", () => {
    var b = new CanvasBox(-100, 20, 224, 168);
    Check(PanelLayout.IsHandle(b, new(120, 184), 10) && !PanelLayout.IsHandle(b, new(120, 30), 10) && !PanelLayout.IsHandle(b, new(125, 184), 10));
    Throws<ArgumentOutOfRangeException>(() => PanelLayout.IsHandle(b, new(1, 1), double.PositiveInfinity));
});
Test("resized node equality and immutable snapshots include canvas dimensions", () => {
    var g = Graph(); var p = g.Add("Panel"); var before = g.Snapshot(); g.Update(p with { CanvasSize = new(300, 200) });
    Check(p != g.GetNode(p.Id) && before.Nodes[0].CanvasSize is null && g.Snapshot().Nodes[0].CanvasSize?.Width == 300);
});
Test("size round-trips through exact workspace and clipboard formats", () => {
    var g = Graph(); var p = g.Add("Panel"); g.Update(p with { CanvasSize = new(380, 240) });
    var text = WorkspaceFormat.Serialize(new(), g); var restored = WorkspaceFormat.Deserialize(factory.Evaluator, g.Registry, text).Graph;
    Check(restored.GetNode(p.Id).CanvasSize == new NodeCanvasSize(380, 240));
    var fragment = GraphFragmentFormat.Deserialize(factory.Evaluator, g.Registry, GraphFragmentFormat.Serialize(GraphFragment.Capture(g, [p.Id]), g.Registry));
    var s = new GraphSession(Graph()); var ids = fragment.Paste(s); Check(s.Graph.GetNode(ids.Single()).CanvasSize == new NodeCanvasSize(380, 240));
});
Test("old workspaces omit optional size and invalid serialized size is rejected", () => {
    var g = Graph(); var p = g.Add("Panel"); var text = WorkspaceFormat.Serialize(new(), g); Check(!text.Contains("canvasSize"));
    Check(WorkspaceFormat.Deserialize(factory.Evaluator, g.Registry, text).Graph.GetNode(p.Id).CanvasSize is null);
    var json = JsonNode.Parse(text)!; json["graph"]!["nodes"]![0]!["canvasSize"] = new JsonObject { ["width"] = -1, ["height"] = 100 };
    Throws<ArgumentOutOfRangeException>(() => WorkspaceFormat.Deserialize(factory.Evaluator, g.Registry, json.ToJsonString()));
});
Test("presentation resizes only target records and attached output paths", () => {
    var g = Graph(); var p = g.Add("Panel"); var n = g.Add("ListLength", 400, 0); g.Connect(new(p.Id, "T"), new(n.Id, "L")); var other = g.Add("Number");
    var cache = new GraphPresentation(); var before = cache.Get(g); g.Update(p with { CanvasSize = new(320, 180) }); var after = cache.Get(g);
    Check(after.Nodes[0].Bounds.Width == 320 && after.Wires[0].Start.X == 320 && ReferenceEquals(before.Nodes[1], after.Nodes[1]) && ReferenceEquals(before.Nodes[2], after.Nodes[2]));
    Check(before.Nodes[0].Bounds.Width == 224 && before.Wires[0].Start.X == 224);
});
Test("preview layout follows row and column budgets and marks clipping", () => {
    var g = Graph(); var p = g.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = string.Join('\n', Enumerable.Repeat("abcdefghij", 30)) }); var r = Solve(g).Nodes[p.Id];
    var cache = new PanelPreviewCache(); var small = cache.Get(p, r, 4, 5); var large = cache.Get(p, r, 40, 20);
    Check(small.Lines.Length == 4 && small.Truncated && small.Footer.EndsWith("…") && large.Lines.Length == 31 && !large.Truncated);
    Check(small.Lines.All(l => l.Length <= 5));
});
Test("same output preview survives node rename movement and selection", () => {
    var g = Graph(); var p = g.Add("Panel"); var r = Solve(g).Nodes[p.Id]; var cache = new PanelPreviewCache(); var before = cache.Get(p, r, 8, 29);
    var after = cache.Get(p with { Name = "Panel name", X = 100 }, r, 8, 29); Check(ReferenceEquals(before, after) && cache.BuildCount == 1);
    cache.Get(p, r, 10, 29); Check(cache.BuildCount == 2);
});
Test("preview limits reject invalid dimensions and bound pathological text", () => {
    var g = Graph(); var p = g.Add("Text", parameters: new Dictionary<string, object?> { ["text"] = "a" + new string('\u0301', 60000) }); var r = Solve(g).Nodes[p.Id]; var cache = new PanelPreviewCache();
    Throws<ArgumentOutOfRangeException>(() => cache.Get(p, r, 0, 10)); Throws<ArgumentOutOfRangeException>(() => cache.Get(p, r, 10, 1000));
    var preview = cache.Get(p, r); Check(preview.Truncated && preview.Lines.Sum(l => l.Length) < 100);
});
Test("warmed preview and resize motion allocate zero managed bytes", () => {
    var g = Graph(); var p = g.Add("Panel"); var r = Solve(g).Nodes[p.Id]; var cache = new PanelPreviewCache(); var draft = new PanelResizeDraft(g, p.Id);
    for (var i = 0; i < 100; i++) { cache.Get(p, r, 8, 29); draft.Move(i, i); }
    var start = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) { cache.Get(p, r, 8, 29); draft.Move(i % 100, i % 50); }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - start; Check(allocated == 0); Console.WriteLine($"METRIC preview-and-resize-10000={allocated} B");
});
var failed = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
Console.WriteLine(JsonSerializer.Serialize(new { suite = "panel-layout", total = tests.Count, passed = tests.Count - failed, failed })); return failed == 0 ? 0 : 1;
