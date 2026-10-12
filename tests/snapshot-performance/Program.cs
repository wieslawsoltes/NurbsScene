using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
// This identical harness also compiles against main before the cache changes.
var graph=new Graph(StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
var previous=graph.Add("Number");var port="N";Wire? last=null;
for(var i=1;i<1000;i++){var next=graph.Add("Add",i%25*185,i/25*90);last=graph.Connect(new(previous.Id,port),new(next.Id,"A"));previous=next;port="R";}
var presentation=new GraphPresentation();presentation.Get(graph);long checksum=0;var reports=new List<object>();
void Measure(string name,int iterations,Action action)
{
    for(var i=0;i<20;i++)action();var times=new double[7];var bytes=new long[7];
    for(var sample=0;sample<7;sample++){
        GC.Collect();GC.WaitForPendingFinalizers();var allocation=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
        for(var i=0;i<iterations;i++)action();
        times[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/iterations;bytes[sample]=(GC.GetAllocatedBytesForCurrentThread()-allocation)/iterations;
    }
    var median=times.Order().ElementAt(3);var size=bytes.Order().ElementAt(3);
    Console.WriteLine($"{name}: {median:G7} ms/op; {size} B/op");reports.Add(new{name,iterations,medianMilliseconds=median,allocatedBytes=size,samplesMilliseconds=times,samplesBytes=bytes});
}
Measure("graph-collection-reads",10000,()=>checksum+=graph.Nodes.Count+graph.Wires.Count);
Measure("registry-collection-read",10000,()=>checksum+=graph.Registry.Definitions.Count);
var toggle=false;string[] target=[last!.Id];
Measure("wire-transform-and-presentation",1000,()=>{toggle=!toggle;graph.SetWireTransforms(target,toggle?"graft":"none");checksum+=presentation.Get(graph).Wires.Length;});
var report=new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),measurements=reports,checksum};
File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
