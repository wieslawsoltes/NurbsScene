using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

// Identical harness compiles against the earlier factory (no new APIs used).
var factory=new GeometryFactory(new ManagedNurbsEvaluator());
var reports=new List<object>();double checksum=0;
foreach(var (sections,controls) in new[]{(16,32),(48,96),(96,64)})
{
    var curves=Enumerable.Range(0,sections).Select(v=>factory.Polyline(
        Enumerable.Range(0,controls).Select(u=>new Vec3(u,Math.Sin(u*.1)+.02*v,v*.5)))).ToArray();
    NurbsSurface Run()
    {
        var surface=factory.Loft(curves,3);
        if(surface.CountU!=controls||surface.CountV!=sections)throw new Exception("Unexpected benchmark net");
        checksum+=surface.ControlPoints[controls*sections/2].Z;return surface;
    }
    for(var i=0;i<2;i++)Run();
    var times=new double[5];var allocations=new long[5];
    for(var sample=0;sample<5;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();
        var before=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
        Run();Run();Run();
        times[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/3;
        allocations[sample]=(GC.GetAllocatedBytesForCurrentThread()-before)/3;
    }
    var ms=times.Order().ElementAt(2);var bytes=allocations.Order().ElementAt(2);
    Console.WriteLine($"loft {sections} x {controls}: {ms:G9} ms/op, {bytes} B/op");
    reports.Add(new{sections,controls,degreeV=3,iterationsPerSample=3,medianMilliseconds=ms,allocatedBytes=bytes,samplesMilliseconds=times,samplesBytes=allocations});
}
File.WriteAllText(args[0],JsonSerializer.Serialize(new{scope="Complete CPU loft preparation including compatibility; not GPU/FPS/whole app",
    framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,
    architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,
    tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),warmup=2,samples=5,measurements=reports,checksum},
    new JsonSerializerOptions{WriteIndented=true}));
