using System.Collections.Immutable;
using NurbsScene.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

internal static class PanelWindowRasterContracts
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        static void Check(bool condition) { if (!condition) throw new Exception("Measured panel window contract failed."); }
        static NodeResult Result(int count) => new(ImmutableDictionary<string,DataTree>.Empty.Add("T",
            DataTree.List(Enumerable.Range(0,count).Select(i=>(object?)$"item-{i:D6}"))),"ok",[],0);
        var node = new GraphNode("panel", "Panel", 10, 10, ImmutableDictionary<string,object?>.Empty);
        var bounds = new SKRect(10,10,260,250);
        void Test(string name, Action run) => tests.Add(("measured scrolling: " + name, run));
        Test("metrics are available without publishing a paint or allocating glyph entries", () => {
            using var renderer = new PanelTypographyRenderer();
            var metrics = renderer.GetFontMetrics(PanelTextStyle.Default, 10); metrics.Validate();
            Check(renderer.Observe("panel") is null && renderer.RetainedPanels==0 && renderer.GlyphBuilds==0 && renderer.PlacementBuilds==0);
        });
        Test("cold and warm scroll frames have identical pixels for every alignment", () => {
            foreach(var horizontal in Enum.GetValues<PanelHorizontalAlignment>())
            foreach(var vertical in Enum.GetValues<PanelVerticalAlignment>()) {
                using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(280,270); using var canvas = new SKCanvas(bitmap);
                var style = PanelTextStyle.Default with { Size=17, Bold=true, Italic=true, Horizontal=horizontal, Vertical=vertical };
                var cache = new PanelWindowCache(); var result=Result(60);
                var cold = PanelWindowRenderer.Prepare(renderer,cache,node,result,bounds,1,style,0,false);
                Check(renderer.Observe(node.Id) is null && cold.Window.CanScroll);
                canvas.Clear(SKColors.White); renderer.Draw(canvas,node.Id,cold.Window.Preview,style,cold.Body,cold.Layout); canvas.Flush(); var bytes=bitmap.Bytes;
                var warm = PanelWindowRenderer.Prepare(renderer,cache,node,result,bounds,1,style,0,true);
                canvas.Clear(SKColors.White); renderer.Draw(canvas,node.Id,warm.Window.Preview,style,warm.Body,warm.Layout); canvas.Flush();
                Check(bytes.SequenceEqual(bitmap.Bytes) && bytes.Any(x=>x!=255));
                Check(cold==warm && renderer.GlyphBuilds==1 && renderer.MeasurementBuilds==1 && renderer.PlacementBuilds==1);
            }
        });
        Test("End selects and renders the last item on the first cold frame", () => {
            using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(280,270); using var canvas = new SKCanvas(bitmap);
            var style=PanelTextStyle.Default with { Size=32, Vertical=PanelVerticalAlignment.Bottom };
            var plan=PanelWindowRenderer.Prepare(renderer,new(),node,Result(60),bounds,1,style,int.MaxValue,false);
            renderer.Draw(canvas,node.Id,plan.Window.Preview,style,plan.Body,plan.Layout);
            Check(plan.Window.First==plan.Window.MaximumOffset && plan.Window.Preview.Lines[^1]=="item-000059");
            Check(renderer.Observe(node.Id)!.Placement.Rows[^1].Text=="item-000059");
        });
        Test("short output releases scrollbar width on its first new frame", () => {
            using var renderer = new PanelTypographyRenderer(); var cache=new PanelWindowCache();
            PanelWindowRenderer.Prepare(renderer,cache,node,Result(60),bounds,1,PanelTextStyle.Default,40,false);
            var plan=PanelWindowRenderer.Prepare(renderer,cache,node,Result(1),bounds,1,PanelTextStyle.Default,40,true);
            Check(!plan.Window.CanScroll && plan.Body==bounds && plan.Window.First==0);
        });
        Test("warm measured preparation allocates zero managed bytes", () => {
            using var renderer = new PanelTypographyRenderer(); var cache=new PanelWindowCache(); var result=Result(60);
            for(var i=0;i<100;i++) PanelWindowRenderer.Prepare(renderer,cache,node,result,bounds,1,PanelTextStyle.Default,12,true);
            var before=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<10000;i++) PanelWindowRenderer.Prepare(renderer,cache,node,result,bounds,1,PanelTextStyle.Default,12,true);
            var bytes=GC.GetAllocatedBytesForCurrentThread()-before; Check(bytes==0);
            Console.WriteLine($"METRIC measured-panel-prepare-10000={bytes} B");
        });
        Test("measuring another style cannot corrupt existing native glyph pixels", () => {
            using var renderer = new PanelTypographyRenderer(); using var bitmap = new SKBitmap(280,270); using var canvas = new SKCanvas(bitmap);
            var style=PanelTextStyle.Default;var plan=PanelWindowRenderer.Prepare(renderer,new(),node,Result(60),bounds,1,style,0,false);
            canvas.Clear(SKColors.White);renderer.Draw(canvas,node.Id,plan.Window.Preview,style,plan.Body,plan.Layout);canvas.Flush();var bytes=bitmap.Bytes;
            renderer.GetFontMetrics(style with { FontFamily="serif",Bold=true },30);
            canvas.Clear(SKColors.White);renderer.Draw(canvas,node.Id,plan.Window.Preview,style,plan.Body,plan.Layout);canvas.Flush();
            Check(bytes.SequenceEqual(bitmap.Bytes) && renderer.GlyphBuilds==1);
        });
        Test("metric lookup rejects use after deterministic disposal", () => {
            var renderer = new PanelTypographyRenderer();renderer.Dispose();
            try { renderer.GetFontMetrics(PanelTextStyle.Default,10); }
            catch(ObjectDisposedException) { return; }
            throw new Exception("Disposed metric access was accepted.");
        });
    }
}
