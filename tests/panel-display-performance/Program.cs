using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

// Public APIs shared by original PR11 and candidate; copy this harness unchanged.
var registry = StandardComponents.Create(new(new ManagedNurbsEvaluator()));
var measurements = new List<object>(); long checksum = 0;
foreach (var (name, value, wrap, rows, columns, changed) in new[] {
    ("clipped-ascii", new string('x',1900)+"\r\nyz", false, 8, 14, true),
    ("wrapped-ascii", new string('x',2048), true, 64, 32, true),
    ("wrapped-unicode", string.Concat(Enumerable.Repeat("🌐e\u0301👩‍💻 ",100)), true, 32, 24, true),
    ("unchanged-preview", "Panel data", true, 8, 29, false) })
{
    var node = new Graph(registry).Add("Panel") with { PanelDisplay = new(false, false, wrap) };
    var result = new NodeResult(ImmutableDictionary<string,DataTree>.Empty.Add("T",DataTree.Item(value)),"ok",[],0);
    var cache = new PanelPreviewCache(); var serial = 0;
    void Run() {
        var p = cache.Get(node,result,rows,columns+(changed ? (++serial & 1) : 0));
        if (p.Lines.IsEmpty) throw new Exception("Missing benchmark output.");
        checksum += p.Lines[0].Length;
    }
    for(var i=0;i<20;i++) Run();
    var times = new double[7]; var allocations = new long[7]; const int iterations = 500;
    for(var sample=0;sample<times.Length;sample++) {
        GC.Collect(); GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        for(var i=0;i<iterations;i++) Run();
        times[sample] = Stopwatch.GetElapsedTime(started).TotalMicroseconds/iterations;
        allocations[sample] = (GC.GetAllocatedBytesForCurrentThread()-before)/iterations;
    }
    var us = times.Order().ElementAt(3); var bytes = allocations.Order().ElementAt(3);
    measurements.Add(new{name,medianMicroseconds=us,allocatedBytes=bytes,samplesMicroseconds=times,samplesBytes=allocations});
    Console.WriteLine($"{name}: {us:G8} us/op; {bytes} B/op");
}
File.WriteAllText(args[0],JsonSerializer.Serialize(new {
    scope="CPU panel-preview formatting/cache only; no font shaping, GPU or complete app frames",
    framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,
    architecture=RuntimeInformation.ProcessArchitecture.ToString(),processors=Environment.ProcessorCount,
    tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    warmups=20,samples=7,iterationsPerSample=500,measurements,checksum
},new JsonSerializerOptions{WriteIndented=true}));
