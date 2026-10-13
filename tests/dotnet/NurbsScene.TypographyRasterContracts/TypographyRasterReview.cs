using System.Collections.Immutable;
using NurbsScene.Controls;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

internal static class TypographyRasterReview
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Check(bool value) { if (!value) throw new Exception("Native typography review failed."); }
        var bounds = new SKRect(10, 10, 390, 250); var style = PanelTextStyle.Default;
        var box = new CanvasBox(10, 10, 380, 240); var layout = PanelDisplayLayout.Create(box, 1);
        PanelPreview Rows() => new(ImmutableArray.Create(new string("WWW iii".AsSpan()), "second"), "");
        tests.Add(("equal rebuilt outputs retain actual native pixels glyphs and measurements", () => {
            using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(420, 280); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White); renderer.Draw(canvas, "panel", Rows(), style, bounds, layout); canvas.Flush(); var expected = bitmap.Bytes;
            var first = renderer.Observe("panel");
            canvas.Clear(SKColors.White); renderer.Draw(canvas, "panel", Rows(), style, bounds, layout); canvas.Flush();
            Check(expected.SequenceEqual(bitmap.Bytes) && ReferenceEquals(first, renderer.Observe("panel")));
            Check(renderer.GlyphBuilds == 1 && renderer.MeasurementBuilds == 1 && renderer.PlacementBuilds == 1);
        }));
        tests.Add(("render publication identifies the exact solved input even for equal text", () => {
            var registry = StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator()));
            var graph = new Graph(registry); var node = graph.Add("Panel");
            var result = graph.SolveAsync().GetAwaiter().GetResult().Nodes[node.Id];
            var first = new PanelPaintStamp(graph, node, null, 1, box); var solved = first with { Result = result };
            using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(420, 280); using var canvas = new SKCanvas(bitmap);
            Check(!renderer.IsCurrent("panel", solved));
            renderer.Draw(canvas, "panel", Rows(), style, bounds, layout, first);
            Check(renderer.IsCurrent("panel", first) && !renderer.IsCurrent("panel", solved));
            renderer.Draw(canvas, "panel", Rows(), style, bounds, layout, solved);
            Check(renderer.IsCurrent("panel", solved) && !renderer.IsCurrent("panel", first));
            Check(renderer.GlyphBuilds == 1); renderer.Clear(); Check(!renderer.IsCurrent("panel", solved));
        }));
        tests.Add(("changed same-length strings rebuild native glyphs rather than reuse wrong text", () => {
            using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(420, 280); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White); renderer.Draw(canvas, "panel", new(["WWW"], ""), style, bounds, layout); canvas.Flush(); var a = bitmap.Bytes;
            canvas.Clear(SKColors.White); renderer.Draw(canvas, "panel", new(["iii"], ""), style, bounds, layout); canvas.Flush();
            Check(!a.SequenceEqual(bitmap.Bytes) && renderer.GlyphBuilds == 2 && renderer.MeasurementBuilds == 2);
        }));
        tests.Add(("native repeated equal-result publication allocates no managed objects", () => {
            using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(420, 280); using var canvas = new SKCanvas(bitmap);
            var a = Rows(); var b = Rows();
            for (var i = 0; i < 100; i++) renderer.Draw(canvas, "panel", (i & 1) == 0 ? a : b, style, bounds, layout);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) renderer.Draw(canvas, "panel", (i & 1) == 0 ? a : b, style, bounds, layout);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(bytes == 0 && renderer.GlyphBuilds == 1 && renderer.MeasurementBuilds == 1);
            Console.WriteLine($"METRIC equal-output-native-draw-1000={bytes} B");
        }));
    }
}
