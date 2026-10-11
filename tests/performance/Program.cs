using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Rendering;

// Identical harness builds against baseline and candidate. No pass/fail timing
// gate: host/JIT variation is reported, while structural budgets live in contracts.
var factory=new GeometryFactory(new ManagedNurbsEvaluator());var records=new List<object>();double sink=0;
void Measure(string name,int operations,Action action)
{
    for(var i=0;i<3;i++)action();var times=new double[7];var bytes=new long[7];
    for(var sample=0;sample<7;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();
        var allocated=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.GetTimestamp();
        for(var i=0;i<operations;i++)action();
        times[sample]=Stopwatch.GetElapsedTime(clock).TotalMilliseconds/operations;
        bytes[sample]=(GC.GetAllocatedBytesForCurrentThread()-allocated)/operations;
    }
    Array.Sort(times);Array.Sort(bytes);records.Add(new{name,operations,medianMilliseconds=times[3],minMilliseconds=times[0],maxMilliseconds=times[^1],allocatedBytes=bytes[3]});
    Console.WriteLine($"{name}: {times[3]:F6} ms/op; {bytes[3]} B/op");
}
var circle=factory.Circle(Vec3.Zero,3);var surface=factory.Saddle();
Measure("curve-point",10000,()=>sink+=circle.PointAt(.371).X);
Measure("surface-point",10000,()=>sink+=surface.PointAt(.371,.629).Z);
var dense=surface;for(var i=1;i<25;i++)dense=dense.InsertKnot(SurfaceDirection.U,i/25d).InsertKnot(SurfaceDirection.V,i/25d);
Measure("control-hull-bounds",10000,()=>sink+=dense.Bounds.Max.X);
Measure("surface-mesh-64",8,()=>sink+=MeshBuilder.Build(surface,Vec3.Zero,64).Vertices.Length);
var scene=new DisplayScene{SurfaceSegments=64};scene.SetItems([new("s",surface,new(.3f,.5f,.7f))]);
var offset=0;Measure("scene-rebase",10,()=>{scene.Rebase(new(++offset,1,2));sink+=scene.Origin.X;});
var items=Enumerable.Range(0,25).Select(i=>new DisplayItem("s"+i,surface.Transform(Transform3.Translation(new((i%5-2)*80,(i/5-2)*60,0))),new(.3f,.5f,.7f))).ToArray();
var many=new DisplayScene{SurfaceSegments=48};many.SetItems(items);var camera=new Camera("Top");camera.Fit(many.Bounds);var view=new ViewportRectangle("Top",0,0,1000,1000,camera);
Measure("pick-115200-triangles",20,()=>{var result=MeshPicker.Pick(many,view,503,497);sink+=result?.Distance??0;});
Measure("unchanged-scene-25",100,()=>many.SetItems(items));
var json=JsonSerializer.Serialize(new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),processorCount=Environment.ProcessorCount,configuration="Release",measurements=records,checksum=sink},new JsonSerializerOptions{WriteIndented=true});
var output=args.Length>0?args[0]:"benchmark.json";File.WriteAllText(output,json);Console.WriteLine("Wrote "+output);
