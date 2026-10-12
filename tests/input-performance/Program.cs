using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;

// Identical caller source runs on previous main and candidate. On main the
// fallback is exactly the former inspector lookup. Reflection is resolved once,
// outside measured operations; each query returns a detached input-wire snapshot.
var graph=new Graph(StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
PortAddress? selected=null;
for(var i=0;i<1501;i++)
{
    var a=graph.Add("Number");var b=graph.Add("Add");var input=new PortAddress(b.Id,"A");
    graph.Connect(new(a.Id,"N"),input);selected??=input;
}
var method=typeof(Graph).GetMethod("Incoming",[typeof(PortAddress)]);
Func<PortAddress,IReadOnlyList<Wire>> query=method is null
    ? input=>graph.Wires.Where(w=>w.To==input).ToArray()
    : method.CreateDelegate<Func<PortAddress,IReadOnlyList<Wire>>>(graph);
var samples=new List<object>();long checksum=0;
for(var i=0;i<100;i++)checksum+=query(selected!).Count;
for(var sample=0;sample<7;sample++)
{
    GC.Collect();GC.WaitForPendingFinalizers();var before=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
    for(var i=0;i<10000;i++)checksum+=query(selected!).Count;
    samples.Add(new{millisecondsPerQuery=Stopwatch.GetElapsedTime(start).TotalMilliseconds/10000,bytesPerQuery=(GC.GetAllocatedBytesForCurrentThread()-before)/10000});
}
var report=new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),
    wires=graph.Wires.Count,indexed=method is not null,iterationsPerSample=10000,samples,checksum};
var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(args.Length>0?args[0]:"input-benchmark.json",json);Console.WriteLine(json);
