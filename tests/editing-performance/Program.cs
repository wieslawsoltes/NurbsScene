using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

// The identical source is copied to previous main. These workloads measure
// presentation maintenance, not component evaluation, rasterization or FPS.
var graph=new Graph(StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
var first=graph.Add("Number");var previous=first;var port="N";
for(var i=1;i<1000;i++){var next=graph.Add("Add",i%25*185,i/25*90);graph.Connect(new(previous.Id,port),new(next.Id,"A"));previous=next;port="R";}
var cache=new GraphPresentation();cache.Get(graph);var data=new List<object>();double checksum=0;
void Measure(string name,int iterations,Action action)
{
    for(var i=0;i<20;i++)action();var time=new double[7];var bytes=new long[7];
    for(var sample=0;sample<7;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();var allocated=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
        for(var i=0;i<iterations;i++)action();
        time[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/iterations;
        bytes[sample]=(GC.GetAllocatedBytesForCurrentThread()-allocated)/iterations;
    }
    var ordered=time.Order().ToArray();var sizes=bytes.Order().ToArray();
    data.Add(new{name,iterations,medianMilliseconds=ordered[3],allocatedBytes=sizes[3],samplesMilliseconds=time,samplesBytes=bytes});
    Console.WriteLine($"{name}: {ordered[3]:G7} ms/op; {sizes[3]} B/op");
}
Measure("cold-drawing-1000-nodes-999-wires",50,()=>checksum+=new GraphPresentation().Get(graph).Nodes.Length);
Measure("unchanged-drawing",100000,()=>checksum+=cache.Get(graph).Wires.Length);
var position=0d;
Measure("one-node-position-edit",500,()=>{graph.Update(first with{X=++position});checksum+=cache.Get(graph).Wires[0].Start.X;});
var label=0;
Measure("one-node-label-edit",500,()=>{graph.Update(graph.GetNode(first.Id) with{Name="Input "+ ++label});checksum+=cache.Get(graph).Wires[0].Start.X;});
var report=new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),measurements=data,checksum};
File.WriteAllText(args.Length>0?args[0]:"editing-benchmark.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
