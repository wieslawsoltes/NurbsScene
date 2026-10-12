using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

// The identical source compiles against PR9 before the panel-layout continuation.
var registry=StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator()));
var graph=new Graph(registry);var root=graph.Add("Number");var previous=root;var port="N";
for(var i=1;i<400;i++){var next=graph.Add("Add",i*180,0);graph.Connect(new(previous.Id,port),new(next.Id,"A"));previous=next;port="R";}
graph.SolveAsync().GetAwaiter().GetResult();
var source=new Graph(registry);
var panel=source.Add("Panel",parameters:new Dictionary<string,object?>{
    ["text"]=string.Join('\n',Enumerable.Range(0,200).Select(i=>$"Row {i}: repeated output for cache measurement"))});
var result=source.SolveAsync().GetAwaiter().GetResult().Nodes[panel.Id];
var preview=new PanelPreviewCache();preview.Get(panel,result);
var reports=new List<object>();long checksum=0;var value=1d;var rename=0;
void Measure(string name,int iterations,Action action)
{
    for(var i=0;i<20;i++)action();
    var times=new double[7];var allocations=new long[7];
    for(var sample=0;sample<7;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();
        var before=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
        for(var i=0;i<iterations;i++)action();
        times[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/iterations;
        allocations[sample]=(GC.GetAllocatedBytesForCurrentThread()-before)/iterations;
    }
    var median=times.Order().ElementAt(3);var allocated=allocations.Order().ElementAt(3);
    Console.WriteLine($"{name}: {median:G9} ms/op, {allocated} B/op");
    reports.Add(new{name,iterations,medianMilliseconds=median,allocatedBytes=allocated,samplesMilliseconds=times,samplesBytes=allocations});
}
Measure("400-node-parameter-change-and-full-solve",20,()=>{
    var node=graph.GetNode(root.Id);graph.Update(node with{Parameters=node.Parameters.SetItem("value",++value)});
    var solved=graph.SolveAsync().GetAwaiter().GetResult();
    if(solved.ErrorCount!=0||solved.Evaluated!=400)throw new InvalidOperationException("Unexpected benchmark solve.");
    checksum+=solved.Evaluated;
});
Measure("panel-preview-after-rename",2000,()=>{
    var node=panel with{Name="Panel "+(++rename)};
    checksum+=preview.Get(node,result).Lines.Length;
});
preview.Get(panel,result);
Measure("unchanged-panel-preview",10000,()=>checksum+=preview.Get(panel,result).Lines.Length);
File.WriteAllText(args[0],JsonSerializer.Serialize(new{
    scope="CPU graph solving and panel text-preview metadata, not rendering/GPU/FPS",
    graphNodes=400,graphWires=399,warmup=20,samples=7,
    framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,
    architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,
    tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),measurements=reports,checksum
},new JsonSerializerOptions{WriteIndented=true}));
