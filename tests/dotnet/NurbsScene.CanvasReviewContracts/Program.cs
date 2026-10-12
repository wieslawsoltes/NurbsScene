using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Parametric;
using NurbsScene.Kernel;
using NurbsScene.Presentation;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Graph Graph() => new(StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
GraphResult Solve(Graph graph) => graph.SolveAsync().GetAwaiter().GetResult();

Test("large pan grid never uses non-advancing float world-coordinate increments", () => {
    float world = 1_000_000_000;
    Check(world + 25 == world, "Regression precondition: the old float loop would not advance.");
    Check(CanvasGridLayout.TryCreate(1600, 1100, .8, -800_000_000, 1_000_000_000, out var grid));
    Check(grid.Columns > 1 && grid.Columns < 100 && grid.Rows > 1 && grid.Spacing == 20);
    for (var i = 1; i < grid.Columns; i++)
        Check((float)(grid.OffsetX + i * grid.Spacing) > (float)(grid.OffsetX + (i - 1) * grid.Spacing));
});
Test("normal screen grid phases agree with world-space grid intersections", () => {
    foreach (var zoom in new[] { .15, .3, .45, .8, 1.0, 3.5 })
    foreach (var pan in new[] { -103.25, -1d, 0d, 1d, 25d, 123.5 }) {
        Check(CanvasGridLayout.TryCreate(1600, 1100, zoom, pan, -pan, out var grid));
        var worldStep = zoom > .45 ? 25 : 100;
        for (var i = 0; i < grid.Columns; i++) {
            var screen = grid.OffsetX + i * grid.Spacing;
            var multiple = (screen - pan) / zoom / worldStep;
            Check(Math.Abs(multiple - Math.Round(multiple)) < 1e-10);
            Check(screen >= 0 && screen <= 1600 + 1e-10);
        }
    }
});
Test("extreme finite pan offsets retain finite bounded grid coordinates", () => {
    foreach (var pan in new[] { double.MaxValue, -double.MaxValue, 1e100, -1e100 }) {
        Check(CanvasGridLayout.TryCreate(1600, 1100, .8, pan, pan, out var grid));
        Check(double.IsFinite(grid.OffsetX) && grid.OffsetX >= 0 && grid.OffsetX < grid.Spacing);
        Check(grid.Columns <= CanvasGridLayout.MaximumLinesPerAxis && grid.Rows <= CanvasGridLayout.MaximumLinesPerAxis);
    }
});
Test("viewport and zoom extremes cannot exceed the grid work budget", () => {
    foreach (var size in new[] { 1e-100, 1d, 1e10, 1e100, double.MaxValue })
    foreach (var zoom in new[] { double.Epsilon, .001, 1d, 1e100 }) {
        Check(CanvasGridLayout.TryCreate(size, size, zoom, 0, 0, out var grid));
        Check(double.IsFinite(grid.Spacing) && grid.Spacing > 0 && grid.Columns <= CanvasGridLayout.MaximumLinesPerAxis && grid.Rows <= CanvasGridLayout.MaximumLinesPerAxis);
    }
});
Test("invalid rendering transforms fail without fabricated grid data", () => {
    foreach (var value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity }) {
        Check(!CanvasGridLayout.TryCreate(value, 100, 1, 0, 0, out _));
        Check(!CanvasGridLayout.TryCreate(100, 100, value, 0, 0, out _));
        Check(!CanvasGridLayout.TryCreate(100, 100, 1, value, 0, out _));
    }
    Check(!CanvasGridLayout.TryCreate(-1, 100, 1, 0, 0, out _));
    Check(!CanvasGridLayout.TryCreate(100, 100, 0, 0, 0, out _));
});
Test("zero-sized viewports produce no lines", () => {
    Check(CanvasGridLayout.TryCreate(0, 100, 1, 0, 0, out var grid) && grid.Columns == 0 && grid.Rows == 0);
    Check(CanvasGridLayout.TryCreate(100, 0, 1, 0, 0, out grid) && grid.Columns == 0 && grid.Rows == 0);
});
Test("warm grid calculations allocate zero bytes", () => {
    for (var i = 0; i < 100; i++) CanvasGridLayout.TryCreate(1600, 1100, .8, -1e9, i, out _);
    var before = GC.GetAllocatedBytesForCurrentThread(); var total = 0;
    for (var i = 0; i < 10000; i++) { CanvasGridLayout.TryCreate(1600, 1100, .8, -1e9, i, out var grid); total += grid.Columns; }
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(bytes == 0 && total > 0); Console.WriteLine($"METRIC canvas-grid-10000={bytes} B");
});

Test("connection metadata follows definition order and is retained across repeated solves", () => {
    var graph = Graph(); var seen = new List<ImmutableArray<string>>();
    graph.Registry.Register(new("Connections", "Test", "",
        [new("A", ValueKind.Any, PortAccess.Tree, Optional:true), new("B", ValueKind.Any, PortAccess.Tree, Optional:true)],
        [new("Count", ValueKind.Integer)], c => {
            seen.Add(c.ConnectedInputs);
            return ValueTask.FromResult<IReadOnlyDictionary<string,object?>>(new Dictionary<string,object?> { ["Count"] = c.ConnectedInputs.Length });
        }));
    var a = graph.Add("Number"); var b = graph.Add("Number"); var probe = graph.Add("Connections");
    graph.Connect(new(b.Id,"N"),new(probe.Id,"B"));
    graph.Connect(new(a.Id,"N"),new(probe.Id,"A"));
    Check(Solve(graph).ErrorCount == 0);
    var original = seen.Single(); var plans = graph.ExecutionPlanBuilds;
    Check(original.SequenceEqual(new[]{"A","B"}));
    for(var i=0;i<20;i++) {
        graph.Update(graph.GetNode(a.Id) with { Parameters = graph.GetNode(a.Id).Parameters.SetItem("value",(double)i) });
        Check(Solve(graph).ErrorCount == 0);
        Check(seen[^1] == original);
    }
    graph.SetWireTransforms(graph.Wires.Select(w=>w.Id).ToArray(),"graft");
    Check(Solve(graph).ErrorCount == 0 && seen[^1] == original && graph.ExecutionPlanBuilds == plans);
});
Test("connection topology changes publish new snapshots without altering old ones", () => {
    var graph = Graph(); var seen = new List<ImmutableArray<string>>();
    graph.Registry.Register(new("Connections", "Test", "",
        [new("D", ValueKind.Any, PortAccess.Tree, Optional:true)], [new("Count",ValueKind.Integer)], c => {
            seen.Add(c.ConnectedInputs);
            return ValueTask.FromResult<IReadOnlyDictionary<string,object?>>(new Dictionary<string,object?> { ["Count"] = c.ConnectedInputs.Length });
        }));
    var empty=graph.Add("Merge"); var probe=graph.Add("Connections");
    Check(Solve(graph).ErrorCount == 0 && seen[^1].IsEmpty && !seen[^1].IsDefault);
    var wire=graph.Connect(new(empty.Id,"R"),new(probe.Id,"D")); Solve(graph);
    var connected=seen[^1]; Check(connected.SequenceEqual(new[]{"D"}));
    graph.Disconnect(wire.Id); Solve(graph);
    Check(seen[^1].IsEmpty && connected.SequenceEqual(new[]{"D"}));
});
Test("in-flight connection snapshots remain unchanged after concurrent graph edits", () => {
    var graph=Graph(); var gate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    ComponentContext? captured=null;
    graph.Registry.Register(new("Pause", "Test", "",
        [new("A", ValueKind.Any, PortAccess.Tree, Optional:true),new("B", ValueKind.Any, PortAccess.Tree, Optional:true)],
        [new("Count",ValueKind.Integer)], async c => {
            captured=c; await gate.Task;
            return new Dictionary<string,object?>{["Count"]=c.ConnectedInputs.Length};
        }));
    var a=graph.Add("Number");var p=graph.Add("Pause");
    graph.Connect(new(a.Id,"N"),new(p.Id,"A"));
    var solve=graph.SolveAsync();Check(captured is not null);
    var first=captured!;
    graph.Connect(new(a.Id,"N"),new(p.Id,"B"));gate.SetResult(true);
    Check(solve.GetAwaiter().GetResult().Stale && first.ConnectedInputs.SequenceEqual(new[]{"A"}) && !first.IsConnected("B"));
    Check(Solve(graph).ErrorCount == 0 && captured!.IsConnected("A") && captured.IsConnected("B"));
    Check(first.ConnectedInputs.SequenceEqual(new[]{"A"}));
});
Test("optional connection queries tolerate default immutable arrays in direct contexts", () => {
    var context=new ComponentContext(new Dictionary<string,object?>(),new Dictionary<string,object?>(),DataPath.Root,default)
        { ConnectedInputs=default };
    Check(!context.IsConnected("D"));
});

Test("deleting a resize target invalidates the draft without resurrecting graph objects", () => {
    var graph=Graph();var panel=graph.Add("Panel");var session=new GraphSession(graph);
    var draft=new PanelResizeDraft(graph,panel.Id);draft.Move(100,50);
    Check(draft.IsCurrent(graph));
    graph.Remove(panel.Id);
    var independent=graph.Add("Number");
    Check(!draft.IsCurrent(graph));
    Throws<InvalidOperationException>(()=>draft.Commit(session));
    Check(graph.NodeCount==1 && graph.GetNode(independent.Id).Component=="Number" && !session.CanUndo);
});
Test("node lookup reports missing identities without fabricated nodes or exceptions", () => {
    var graph=Graph();var node=graph.Add("Number");
    Check(graph.TryGetNode(node.Id,out var found) && ReferenceEquals(found,node));
    Check(!graph.TryGetNode("missing",out var missing) && missing is null);
    Throws<ArgumentNullException>(()=>graph.TryGetNode(null!,out _));
});
Test("literal suggestions preserve trailing spaces and operator symbols shown by the UI", () => {
    var graph=Graph();var index=new CanvasSearchIndex();const string query="//  padded text  ";
    var suggestion=index.Search(graph.Registry,query).Single();
    Check(suggestion==query);
    Check(CanvasCreationParser.TryParse(suggestion,graph.Registry,out var creation,out _) &&
        (string)creation!.Parameters["text"]! == "  padded text  ");
    foreach(var (glyph,component) in new[]{("−","Subtract"),("×","Multiply"),("÷","Divide")})
        Check(CanvasCreationParser.TryParse(glyph,graph.Registry,out creation,out _) && creation!.Component==component);
});
Test("direct unconnected panel evaluation honors cancellation before allocating output", () => {
    using var cancellation=new CancellationTokenSource();cancellation.Cancel();
    var context=new ComponentContext(
        new Dictionary<string,object?>{["D"]=DataTree.Empty},
        new Dictionary<string,object?>{["text"]=new string('x',65536)},
        DataPath.Root,cancellation.Token);
    Throws<OperationCanceledException>(()=>InputComponents.EvaluatePanel(context));
});

var failed = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
Console.WriteLine(JsonSerializer.Serialize(new { suite = "canvas-review", total = tests.Count, passed = tests.Count - failed, failed }));
return failed == 0 ? 0 : 1;
