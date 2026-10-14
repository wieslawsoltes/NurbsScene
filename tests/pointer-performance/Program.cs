using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

// Identical complete PortAt calls on identical immutable drawing fixtures.
// Layout construction and numerical solving are deliberately outside this CPU
// input-query workload. No GPU, FPS or whole-application claims follow.
GraphDrawing Drawing(int count,int inputs,int outputs)
{
    var definition=new ComponentDefinition("Fixture","Test","Pointer workload",
        Enumerable.Range(0,inputs).Select(i=>new PortDefinition("I"+i)).ToImmutableArray(),
        Enumerable.Range(0,outputs).Select(i=>new PortDefinition("O"+i)).ToImmutableArray(),
        _=>throw new NotSupportedException("No numerical evaluation in pointer workload."));
    var nodes=ImmutableArray.CreateBuilder<PresentedNode>(count);
    for(var i=0;i<count;i++)
    {
        var x=i%25*220d;var y=i/25*(inputs*19d+64);
        var node=new GraphNode("n"+i,"Fixture",x,y,ImmutableDictionary<string,object?>.Empty);
        nodes.Add(new(node,definition,new(x,y,156,inputs*19d+24)));
    }
    return new(nodes.MoveToImmutable(),[]);
}
var data=new List<object>();long checksum=0;
void Measure(string name,int iterations,GraphDrawing drawing,Func<int,CanvasPoint> point,double tolerance)
{
    for(var i=0;i<20;i++)GraphHitTesting.PortAt(drawing,point(i),tolerance);
    var times=new double[7];var sizes=new long[7];
    for(var sample=0;sample<7;sample++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();var allocated=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
        for(var i=0;i<iterations;i++)
        {
            var hit=GraphHitTesting.PortAt(drawing,point(i),tolerance);
            checksum+=hit is null?0:hit.Node.Length+hit.Port.Length+(hit.Output?1:0);
        }
        times[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/iterations;
        sizes[sample]=(GC.GetAllocatedBytesForCurrentThread()-allocated)/iterations;
    }
    var median=times.Order().ElementAt(3);var bytes=sizes.Order().ElementAt(3);
    data.Add(new{name,iterations,nodes=drawing.Nodes.Length,inputPorts=drawing.Nodes[0].Definition.Inputs.Length,
        outputPorts=drawing.Nodes[0].Definition.Outputs.Length,tolerance,medianMilliseconds=median,allocatedBytes=bytes,
        samplesMilliseconds=times,samplesBytes=sizes});
    Console.WriteLine($"{name}: {median:G7} ms/op; {bytes} B/op");
}
var standard=Drawing(1000,8,2);var tall=Drawing(250,128,64);var dense=Drawing(25,1024,256);
Measure("1000-node-off-column-miss",2000,standard,_=>new(-100,75),9);
Measure("1000-node-low-zoom-nearest",2000,standard,i=>new(i%25*220d+3,19+i%8*19d+2),60);
Measure("250-node-128-input-columns",1000,tall,i=>new(i%25*220d+3,19+i%128*19d+2),9);
Measure("25-node-1024-input-columns",1000,dense,i=>new(i%25*220d+3,19+i%1024*19d+2),9);
Measure("frontmost-exact-hit",3000,standard,_=>standard.Nodes[^1].Port(false,0),9);
var report=new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,
    architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,
    tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),measurements=data,checksum};
File.WriteAllText(args.Length>0?args[0]:"pointer-benchmark.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
