using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

// Identical source compiles against baseline and candidate public APIs. It tests
// the actual native ABI adapter, not a substitute evaluator. No timing pass gate.
var factory=new GeometryFactory(new NativeNurbsEvaluator());var measurements=new List<object>();double sink=0;
void Measure(string name,int operations,Action action)
{
    for(var i=0;i<100;i++)action();var times=new double[7];var bytes=new long[7];
    for(var sample=0;sample<7;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();var before=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
        for(var i=0;i<operations;i++)action();
        times[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/operations;bytes[sample]=(GC.GetAllocatedBytesForCurrentThread()-before)/operations;
    }
    Array.Sort(times);Array.Sort(bytes);measurements.Add(new{name,operations,medianMilliseconds=times[3],minMilliseconds=times[0],maxMilliseconds=times[^1],allocatedBytes=bytes[3]});
    Console.WriteLine($"{name}: {times[3]:G8} ms/op; {bytes[3]} B/op");
}
var curve=factory.Circle(Vec3.Zero,3);var surface=factory.Saddle();
Measure("native-curve-point",10000,()=>sink+=curve.PointAt(.317).X);
Measure("native-surface-point",1000,()=>sink+=surface.PointAt(.317,.629).Z);
Measure("native-surface-derivatives-2x2",1000,()=>sink+=surface.Derivatives(.317,.629)[0,0].Z);
static double[] Knots(int count)=>Enumerable.Repeat(0d,4).Concat(Enumerable.Range(1,count-4).Select(i=>i/(double)(count-3))).Concat(Enumerable.Repeat(1d,4)).ToArray();
var points=Enumerable.Range(0,96).SelectMany(i=>Enumerable.Range(0,48).Select(j=>HPoint.FromPoint(new(i,j,Math.Sin(i*.1)*Math.Cos(j*.1)))));
var dense=new NurbsSurface(factory.Evaluator,3,3,96,48,Knots(96),Knots(48),points);
Measure("native-surface-point-4608-controls",100,()=>sink+=dense.PointAt(.317,.629).Z);
var json=JsonSerializer.Serialize(new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),samples=7,measurements,checksum=sink},new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(args.Length>0?args[0]:"analysis-benchmark.json",json);
