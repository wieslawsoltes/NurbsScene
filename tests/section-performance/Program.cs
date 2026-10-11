using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;

var records=new List<object>();double sink=0;
void Measure(string name,int operations,Action action)
{
    for(var i=0;i<3;i++)action();var times=new double[7];var bytes=new long[7];
    for(var sample=0;sample<7;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();var allocated=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.GetTimestamp();
        for(var i=0;i<operations;i++)action();
        times[sample]=Stopwatch.GetElapsedTime(clock).TotalMilliseconds/operations;
        bytes[sample]=(GC.GetAllocatedBytesForCurrentThread()-allocated)/operations;
    }
    Array.Sort(times);Array.Sort(bytes);records.Add(new{name,operations,medianMilliseconds=times[3],minMilliseconds=times[0],maxMilliseconds=times[^1],allocatedBytes=bytes[3]});
    Console.WriteLine($"{name}: {times[3]:G6} ms/op; {bytes[3]} B/op");
}
var evaluator=new ManagedNurbsEvaluator();var factory=new GeometryFactory(evaluator);var registry=StandardComponents.Create(factory);
var tree=new DataTree(Enumerable.Range(0,10000).Select(i=>new DataBranch(new([3,i*2]),[i])));var path=new DataPath([3,19998]);
Measure("lookup-10000-branches",1000,()=>sink+=(int)tree.ItemsAt(path)[0]!);
var graph=new Graph(registry);GraphNode? first=null;
for(var i=0;i<1000;i++){var n=graph.Add("Number",parameters:new Dictionary<string,object?>{{"value",(double)i}});first??=n;}
var sum=graph.Add("Add");graph.Connect(new(first!.Id,"N"),new(sum.Id,"A"));graph.SolveAsync().GetAwaiter().GetResult();
Measure("cached-solve-1001-nodes",20,()=>sink+=graph.SolveAsync().GetAwaiter().GetResult().Cached);
var value=0;
Measure("two-dirty-nodes-of-1001",10,()=>{graph.Update(first with{Parameters=first.Parameters.SetItem("value",(double)++value)});sink+=graph.SolveAsync().GetAwaiter().GetResult().Evaluated;});
Measure("connect-chain-1000-nodes",1,()=>{
    var g=new Graph(registry);var current=g.Add("Number");var port="N";
    for(var i=1;i<1000;i++){var next=g.Add("Add");g.Connect(new(current.Id,port),new(next.Id,"A"));current=next;port="R";}sink+=g.Wires.Count;
});
var json=JsonSerializer.Serialize(new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),samples=7,measurements=records,checksum=sink},new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(args.Length==0?"section-benchmark.json":args[0],json);
