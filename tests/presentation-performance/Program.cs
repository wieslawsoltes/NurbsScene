using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

var graph=new Graph(StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
var last=graph.Add("Number");var port="N";
for(var i=1;i<1000;i++){var next=graph.Add("Add",i%25*185,i/25*90);graph.Connect(new(last.Id,port),new(next.Id,"A"));last=next;port="R";}
var cache=new GraphPresentation();cache.Get(graph);var results=new List<object>();long checksum=0;
void Measure(string name,int operations,Action action)
{
    for(var i=0;i<10;i++)action();var times=new double[7];var bytes=new long[7];
    for(var sample=0;sample<7;sample++){
        GC.Collect();GC.WaitForPendingFinalizers();var before=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.GetTimestamp();
        for(var i=0;i<operations;i++)action();
        times[sample]=Stopwatch.GetElapsedTime(clock).TotalMilliseconds/operations;bytes[sample]=(GC.GetAllocatedBytesForCurrentThread()-before)/operations;
    }
    Array.Sort(times);Array.Sort(bytes);results.Add(new{name,operations,medianMilliseconds=times[3],minimumMilliseconds=times[0],maximumMilliseconds=times[^1],allocatedBytes=bytes[3]});
    Console.WriteLine($"{name}: {times[3]:G8} ms; {bytes[3]} B/op");
}
Measure("cold-layout-1000-nodes-999-wires",20,()=>checksum+=new GraphPresentation().Get(graph).Wires.Length);
Measure("unchanged-layout-cache-hit",100000,()=>checksum+=cache.Get(graph).Wires.Length);
var node=graph.Nodes[0];var x=0d;
Measure("one-node-layout-edit",20,()=>{graph.Update(node with{X=++x});checksum+=cache.Get(graph).Wires.Length;});
var report=new{scope="UI-independent drawing metadata only; cold/rebuilt versus cached on same implementation, not an old-version, full frame, GPU or FPS comparison",
    framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),samples=7,measurements=results,checksum};
File.WriteAllText(args.Length>0?args[0]:"presentation-benchmark.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
