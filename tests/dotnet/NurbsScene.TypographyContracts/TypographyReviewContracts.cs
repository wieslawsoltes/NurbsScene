using System.Collections.Immutable;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

internal static class TypographyReviewContracts
{
    public static void Register(List<(string Name, Action Run)> tests, ComponentRegistry registry)
    {
        void Test(string name, Action run) => tests.Add((name, run));
        void Check(bool value) { if (!value) throw new Exception("Typography review failed."); }
        var bounds = new CanvasBox(0, 0, 300, 220);
        var layout = PanelDisplayLayout.Create(bounds, 1);
        var metrics = new PanelFontMetrics(-8, 2);
        var style = PanelTextStyle.Default;
        PanelPreview Text() => new(ImmutableArray.Create(new string("same row".AsSpan()), "second"), "");
        double Measure(string text) => text.Length * 6;
        Test("equal text from a new immutable solution reuses measured placement", () => {
            var cache = new PanelTextPlacementCache(); var first = Text(); var second = Text();
            Check(first.Lines != second.Lines && !ReferenceEquals(first.Lines[0], second.Lines[0]));
            var before = cache.Get(first, style, 300, 220, layout, metrics, 1, Measure);
            var after = cache.Get(second, style, 300, 220, layout, metrics, 1, _ => throw new Exception("Unnecessary measurement"));
            Check(ReferenceEquals(before, after) && cache.MeasurementBuilds == 1 && cache.LayoutBuilds == 1);
        });
        Test("equal rebuilt rows can change alignment without remeasuring", () => {
            var cache = new PanelTextPlacementCache();
            var before = cache.Get(Text(), style, 300, 220, layout, metrics, 1, Measure);
            var after = cache.Get(Text(), style with { Horizontal = PanelHorizontalAlignment.Right }, 300, 220, layout, metrics, 1, _ => throw new Exception("Unnecessary measurement"));
            Check(before.Rows[0].X < after.Rows[0].X && cache.MeasurementBuilds == 1 && cache.LayoutBuilds == 2);
        });
        Test("changed content of equal length still invalidates widths", () => {
            var cache = new PanelTextPlacementCache();
            cache.Get(new(["WWW"], ""), style, 300, 220, layout, metrics, 1, _ => 42);
            var after = cache.Get(new(["iii"], ""), style, 300, 220, layout, metrics, 1, _ => 9);
            Check(after.Rows[0].Advance == 9 && cache.MeasurementBuilds == 2);
        });
        Test("equal text does not mask a changed font identity or metrics", () => {
            var cache = new PanelTextPlacementCache();
            cache.Get(Text(), style, 300, 220, layout, metrics, 1, Measure);
            cache.Get(Text(), style, 300, 220, layout, metrics, 2, Measure);
            Check(cache.MeasurementBuilds == 2);
            cache.Get(Text(), style, 300, 220, layout, new(-10, 4), 2, _ => throw new Exception("Unnecessary measurement"));
            Check(cache.MeasurementBuilds == 2 && cache.LayoutBuilds == 3);
        });
        Test("equal solution arrays retain zero allocation after warmup", () => {
            var cache = new PanelTextPlacementCache(); var a = Text(); var b = Text(); Func<string, double> measure = Measure;
            for (var i = 0; i < 100; i++) cache.Get((i & 1) == 0 ? a : b, style, 300, 220, layout, metrics, 1, measure);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) cache.Get((i & 1) == 0 ? a : b, style, 300, 220, layout, metrics, 1, measure);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(bytes == 0 && cache.MeasurementBuilds == 1); Console.WriteLine($"METRIC equal-solution-placement-10000={bytes} B");
        });
        Test("paint identity distinguishes solved data from a prior equal-looking frame", () => {
            var graph = new Graph(registry); var node = graph.Add("Panel");
            var waiting = new PanelPaintStamp(graph, node, null, 1, bounds);
            var result = graph.SolveAsync().GetAwaiter().GetResult().Nodes[node.Id];
            var solved = new PanelPaintStamp(graph, node, result, 1, bounds);
            Check(!waiting.Matches(solved) && solved.Matches(solved));
            var clone = result with { }; Check(clone == result && !solved.Matches(solved with { Result = clone }));
        });
        Test("undo graph replacement with reused revisions cannot satisfy an old frame stamp", () => {
            var graph = new Graph(registry); var node = graph.Add("Panel"); var session = new GraphSession(graph);
            var result = graph.SolveAsync().GetAwaiter().GetResult().Nodes[node.Id];
            var stamp = new PanelPaintStamp(graph, node, result, 1, bounds);
            session.Edit("Move", g => g.Update(node with { X = 1 })); session.Undo();
            var restored = session.Graph; var live = restored.GetNode(node.Id);
            Check(!ReferenceEquals(graph, restored) && live == node);
            Check(!stamp.Matches(new(restored, live, restored.SolveAsync().GetAwaiter().GetResult().Nodes[node.Id], 1, bounds)));
        });
        Test("node identity zoom and resize bounds independently invalidate paint witnesses", () => {
            var graph = new Graph(registry); var node = graph.Add("Panel"); var stamp = new PanelPaintStamp(graph, node, null, 1, bounds);
            Check(!stamp.Matches(stamp with { Node = node with { } }));
            Check(!stamp.Matches(stamp with { Zoom = 1.2 }));
            Check(!stamp.Matches(stamp with { Bounds = bounds with { Width = 301 } }));
        });
    }
}
