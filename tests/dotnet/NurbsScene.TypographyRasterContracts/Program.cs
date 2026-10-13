using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using NurbsScene.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

var output=args.Length>0?args[0]:"artifacts/typography";Directory.CreateDirectory(output);
var tests=new List<(string Name,Action Run)>();
void Test(string name,Action action)=>tests.Add((name,action));
void Check(bool ok,string message="Raster assertion failed"){if(!ok)throw new Exception(message);}
void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
var preview=new PanelPreview(["WWW iii 0123","Second row","Third row"],"");
var style=PanelTextStyle.Default with{Size=16};
var bounds=new SKRect(10,10,390,250);
PanelDisplayLayout Layout(PanelTextStyle s)=>PanelDisplayLayout.Create(new(0,0,bounds.Width,bounds.Height),1,s.Size);
byte[] Image(PanelTypographyRenderer r,PanelTextStyle s,string id="panel",SKRect? rect=null,PanelPreview? text=null)
{
    using var bitmap=new SKBitmap(420,280);using var c=new SKCanvas(bitmap);c.Clear(SKColors.White);
    r.Draw(c,id,text??preview,s,rect??bounds,Layout(s));c.Flush();return bitmap.Bytes;
}
int Different(byte[] a,byte[] b)=>a.Where((v,i)=>v!=b[i]).Count();
Test("native glyphs produce visible pixels matching immediate text with identical metrics",()=>{
    using var r=new PanelTypographyRenderer();var actual=Image(r,style);var observed=r.Observe("panel")!;
    using var bitmap=new SKBitmap(420,280);using var c=new SKCanvas(bitmap);c.Clear(SKColors.White);
    using var referenceFace=SKTypeface.FromFamilyName(null,SKFontStyle.Normal);
    using var font=new SKFont(referenceFace??SKTypeface.Default,(float)observed.FontSize);
    using var paint=new SKPaint{IsAntialias=true,Color=new(62,60,40)};
    c.Translate(bounds.Left,bounds.Top);var clip=observed.Placement.Clip;
    c.ClipRect(new((float)clip.X,(float)clip.Y,(float)(clip.X+clip.Width),(float)(clip.Y+clip.Height)));
    foreach(var row in observed.Placement.Rows)c.DrawText(row.Text,(float)row.X,(float)row.Baseline,font,paint);c.Flush();
    Check(actual.Any(x=>x!=255));Check(actual.SequenceEqual(bitmap.Bytes),"Retained glyph raster differs from immediate reference");
});
Test("alignment moves pixels without rebuilding glyphs or measurements",()=>{
    using var r=new PanelTypographyRenderer();var a=Image(r,style);var glyphs=r.GlyphBuilds;var measured=r.MeasurementBuilds;
    var b=Image(r,style with{Horizontal=PanelHorizontalAlignment.Right,Vertical=PanelVerticalAlignment.Bottom});
    Check(Different(a,b)>200);Check(r.GlyphBuilds==glyphs&&r.MeasurementBuilds==measured&&r.PlacementBuilds==2);
});
Test("translation reuses local placement and glyphs",()=>{
    using var r=new PanelTypographyRenderer();Image(r,style);var placement=r.Observe("panel")!.Placement;
    var moved=bounds;moved.Offset(4,6);Image(r,style,rect:moved);
    Check(ReferenceEquals(placement,r.Observe("panel")!.Placement)&&r.GlyphBuilds==1&&r.MeasurementBuilds==1);
});
Test("size and synthetic or resolved font styles change actual raster output",()=>{
    using var r=new PanelTypographyRenderer();var normal=Image(r,style);var bold=Image(r,style with{Bold=true});var italic=Image(r,style with{Italic=true});var bigger=Image(r,style with{Size=24});
    Check(Different(normal,bold)>100&&Different(normal,italic)>100&&Different(normal,bigger)>100);
    Check(r.GlyphBuilds==4&&r.MeasurementBuilds==4);
});
Test("oversized text is confined to the reported body clip",()=>{
    using var r=new PanelTypographyRenderer();var bytes=Image(r,style,text:new([new string('W',100)],""));
    var clip=r.Observe("panel")!.Placement.Clip;Check(r.Observe("panel")!.Placement.Clipped);
    for(var y=0;y<280;y++)for(var x=0;x<420;x++)if(bytes[(y*420+x)*4]!=255)
        Check(x>=Math.Floor(bounds.Left+clip.X)&&x<=Math.Ceiling(bounds.Left+clip.X+clip.Width)&&y>=Math.Floor(bounds.Top+clip.Y)&&y<=Math.Ceiling(bounds.Top+clip.Y+clip.Height));
});
Test("panel retention is bounded and explicit forgetting releases observations",()=>{
    using var r=new PanelTypographyRenderer();for(var i=0;i<PanelTypographyRenderer.MaximumPanels+10;i++)Image(r,style,"node"+i);
    Check(r.RetainedPanels==PanelTypographyRenderer.MaximumPanels&&r.Observe("node0") is null);
    r.Forget("node137");Check(r.RetainedPanels==127&&r.Observe("node137") is null);r.Clear();Check(r.RetainedPanels==0);
});
Test("typeface eviction preserves old blob safety and rebuilds evicted faces",()=>{
    using var r=new PanelTypographyRenderer();var old=Image(r,style);
    for(var i=0;i<PanelTypographyRenderer.MaximumTypefaces+10;i++)Image(r,style with{FontFamily="NurbsScene missing family "+i},"face"+i);
    Check(r.RetainedTypefaces<=PanelTypographyRenderer.MaximumTypefaces);
    Check(old.SequenceEqual(Image(r,style)),"Typeface eviction corrupted retained glyphs");
});
Test("invalid inputs cannot poison a previous renderer entry",()=>{
    using var r=new PanelTypographyRenderer();var old=Image(r,style);var prior=r.Observe("panel");
    Throws<ArgumentException>(()=>Image(r,style with{Size=double.NaN}));
    Throws<ArgumentException>(()=>Image(r,style with{FontFamily="https://example/font"}));
    Throws<ArgumentException>(()=>Image(r,style,text:new([new string('a',16385)],"")));
    Check(ReferenceEquals(prior,r.Observe("panel"))&&old.SequenceEqual(Image(r,style)));
});
Test("empty rows are retained without fabricated glyph blobs",()=>{
    using var r=new PanelTypographyRenderer();var bytes=Image(r,style,text:new(["",""],""));Check(bytes.All(x=>x==255));
    Check(r.Observe("panel")!.Placement.Rows.Length==2);Check(r.GlyphBuilds==1);
});
Test("repeated paints retain all cached native artifacts",()=>{
    using var r=new PanelTypographyRenderer();using var b=new SKBitmap(420,280);using var c=new SKCanvas(b);var l=Layout(style);
    for(var i=0;i<100;i++)r.Draw(c,"panel",preview,style,bounds,l);
    var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)r.Draw(c,"panel",preview,style,bounds,l);
    var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
    Console.WriteLine($"METRIC retained-native-text-1000={bytes} B");
    Check(r.GlyphBuilds==1&&r.MeasurementBuilds==1&&r.PlacementBuilds==1);
});
Test("renderer disposal is idempotent and refuses further drawing",()=>{
    var r=new PanelTypographyRenderer();Image(r,style);r.Dispose();r.Dispose();
    Check(r.RetainedPanels==0&&r.RetainedTypefaces==0);Throws<ObjectDisposedException>(()=>Image(r,style));
});
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="native-typography-raster",total=tests.Count,passed=tests.Count-failed,failed}));
if(failed!=0)return 1;
// In-process reference benchmark: identical pixels and placement, differing only
// in immediate strings versus retained native blobs. Not a pre-release app/FPS test.
using(var r=new PanelTypographyRenderer())using(var bitmap=new SKBitmap(420,280))using(var c=new SKCanvas(bitmap))
using(var font=new SKFont(SKTypeface.Default,16))using(var paint=new SKPaint{IsAntialias=true,Color=new(62,60,40)})
{
    var l=Layout(style);r.Draw(c,"panel",preview,style,bounds,l);var placement=r.Observe("panel")!.Placement;var results=new List<object>();
    void Immediate(){c.Save();c.Translate(bounds.Left,bounds.Top);var clip=placement.Clip;c.ClipRect(new((float)clip.X,(float)clip.Y,(float)(clip.X+clip.Width),(float)(clip.Y+clip.Height)));foreach(var row in placement.Rows)c.DrawText(row.Text,(float)row.X,(float)row.Baseline,font,paint);c.Restore();}
    void Retained()=>r.Draw(c,"panel",preview,style,bounds,l);
    foreach(var(name,action) in new (string,Action)[]{("immediate-reference",Immediate),("retained",Retained)})
    {
        for(var i=0;i<100;i++)action();var times=new double[7];var allocations=new long[7];
        for(var sample=0;sample<7;sample++){GC.Collect();var before=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();for(var i=0;i<1000;i++)action();times[sample]=Stopwatch.GetElapsedTime(start).TotalMicroseconds/1000;allocations[sample]=(GC.GetAllocatedBytesForCurrentThread()-before)/1000;}
        results.Add(new{name,samplesMicroseconds=times,samplesBytes=allocations,medianMicroseconds=times.Order().ElementAt(3),allocatedBytes=allocations.Order().ElementAt(3)});
    }
    File.WriteAllText(Path.Combine(output,"native-text-benchmark.json"),JsonSerializer.Serialize(new{scope="Same-process CPU raster, 3 identical text rows, immediate API reference vs retained blobs; not GPU or full application",framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,measurements=results},new JsonSerializerOptions{WriteIndented=true}));
    c.Clear(SKColors.White);r.Draw(c,"panel",preview,style with{Bold=true,Horizontal=PanelHorizontalAlignment.Center},bounds,l);using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(Path.Combine(output,"native-text.png"));data.SaveTo(file);
}
return 0;
