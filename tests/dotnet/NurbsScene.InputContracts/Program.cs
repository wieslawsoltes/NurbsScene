using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.IO;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

var factory=new GeometryFactory(new ManagedNurbsEvaluator());
var tests=new List<(string Name,Func<Task> Run)>();
void Test(string name,Action action)=>tests.Add((name,()=>{action();return Task.CompletedTask;}));
void Check(bool condition,string message="Assertion failed"){if(!condition)throw new Exception(message);}
void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
GraphResult Solve(Graph g)=>g.SolveAsync().GetAwaiter().GetResult();
(Graph G,GraphNode Source,GraphNode Target,Wire Wire) Example()
{
    var g=new Graph(StandardComponents.Create(factory));var source=g.Add("Series",parameters:new Dictionary<string,object?>{{"input:Count",3d}});
    var target=g.Add("Merge",240,60);var wire=g.Connect(new(source.Id,"S"),new(target.Id,"A"));return(g,source,target,wire);
}
Test("input transforms preserve identity and topology while changing real tree results",()=>{
    var(g,source,target,wire)=Example();g.Add("Number");Solve(g);var topology=g.TopologyRevision;var plans=g.ExecutionPlanBuilds;var wireRevision=g.WireRevision;
    g.SetWireTransforms([wire.Id],"graft");var result=Solve(g);var updated=g.Wires.Single();
    Check(updated.Id==wire.Id&&updated.From==wire.From&&updated.To==wire.To&&updated.Transform=="graft");
    Check(result.Nodes[target.Id].Outputs["R"].Branches.Length==3&&result.Evaluated==1&&result.Cached==2);
    Check(g.TopologyRevision==topology&&g.ExecutionPlanBuilds==plans&&g.WireRevision==wireRevision+1);
});
Test("flatten and simplify retain values and use the captured transformed tree",()=>{
    var(g,source,target,wire)=Example();g.SetWireTransforms([wire.Id],"graft");Solve(g);
    g.SetWireTransforms([wire.Id],"flatten");var flat=Solve(g).Nodes[target.Id].Outputs["R"];
    Check(flat.Branches.Length==1&&flat.ItemCount==3&&flat.Branches[0].Path.Equals(DataPath.Root));
    g.SetWireTransforms([wire.Id],"simplify");var simple=Solve(g).Nodes[target.Id].Outputs["R"];
    Check(simple.AllItems.SequenceEqual(flat.AllItems));
});
Test("batched transforms preserve fan-in order and emit one notification",()=>{
    var(g,source,target,wire)=Example();var second=g.Add("Series",parameters:new Dictionary<string,object?>{{"input:Start",10d},{"input:Count",3d}});
    var other=g.Connect(new(second.Id,"S"),wire.To);var original=g.Incoming(wire.To).Select(w=>w.Id).ToArray();var changed=0;g.Changed+=()=>changed++;
    g.SetWireTransforms([other.Id,wire.Id],"graft");var tree=Solve(g).Nodes[target.Id].Outputs["R"];
    Check(changed==1&&g.Incoming(wire.To).Select(w=>w.Id).SequenceEqual(original));
    Check(tree.Branches[0].Items.SequenceEqual(new object?[]{0d,10d}));
});
Test("no-op transforms preserve history revisions and solved result cache",()=>{
    var(g,source,target,wire)=Example();var session=new GraphSession(g);var before=Solve(g);var revision=g.Revision;var wr=g.WireRevision;var changed=0;g.Changed+=()=>changed++;
    session.Edit("No change",x=>x.SetWireTransforms([wire.Id],"none"));g.SetWireTransforms([],"flatten");
    Check(!session.CanUndo&&g.Revision==revision&&g.WireRevision==wr&&changed==0);
    Check(ReferenceEquals(Solve(g).Nodes,before.Nodes));
});
Test("invalid batches fail before changing any wire",()=>{
    var(g,source,target,wire)=Example();var revision=g.Revision;
    Throws<KeyNotFoundException>(()=>g.SetWireTransforms([wire.Id,"missing"],"graft"));
    Throws<ArgumentException>(()=>g.SetWireTransforms([wire.Id,wire.Id],"graft"));
    Throws<ArgumentException>(()=>g.SetWireTransforms([wire.Id],"unknown"));
    Throws<ArgumentException>(()=>g.SetWireTransforms(new string[100001],"graft"));
    Check(g.Revision==revision&&g.Incoming(wire.To).Single()==wire);
});
Test("incoming wire snapshots are detached and validate the actual input",()=>{
    var(g,source,target,wire)=Example();var snapshot=(Wire[])g.Incoming(wire.To);snapshot[0]=wire with{Transform="graft"};
    Check(g.Incoming(wire.To).Single()==wire&&g.Incoming(new(target.Id,"B")).Count==0);
    Throws<ArgumentException>(()=>g.Incoming(new(target.Id,"R")));Throws<KeyNotFoundException>(()=>g.Incoming(new("missing","A")));
});
Test("disconnect and replacement retain a correct incoming index after transforms",()=>{
    var(g,source,target,wire)=Example();g.SetWireTransforms([wire.Id],"graft");
    g.Disconnect(wire.Id);Check(g.Incoming(wire.To).Count==0);var next=g.Connect(wire.From,wire.To);
    g.SetWireTransforms([next.Id],"flatten");g.Remove(source.Id);Check(g.Incoming(wire.To).Count==0&&g.WireCount==0);
});
Test("transform transaction undoes and redoes one complete edit",()=>{
    var(g,source,target,wire)=Example();var s=new GraphSession(g);
    s.Edit("Graft input",x=>x.SetWireTransforms([wire.Id],"graft"));Check(s.CanUndo&&s.Graph.Wires.Single().Transform=="graft");
    Check(s.Undo()&&s.Graph.Wires.Single()==wire&&!s.CanUndo);
    Check(s.Redo()&&s.Graph.Wires.Single().Transform=="graft"&&s.Graph.Wires.Single().Id==wire.Id);
});
Test("transform metadata survives workspace and clipboard round trips",()=>{
    var(g,source,target,wire)=Example();g.SetWireTransforms([wire.Id],"graft");
    var text=GraphFragmentFormat.Serialize(GraphFragment.Capture(g,[source.Id,target.Id]),g.Registry);
    var fragment=GraphFragmentFormat.Deserialize(factory.Evaluator,g.Registry,text);var s=new GraphSession(new(g.Registry));fragment.Paste(s);
    Check(s.Graph.Wires.Single().Transform=="graft"&&Solve(s.Graph).Nodes.Values.Any(n=>n.Outputs.Values.Any(t=>t.Branches.Length==3)));
});
Test("presentation updates transform labels without rebuilding endpoint geometry",()=>{
    var(g,source,target,wire)=Example();var cache=new GraphPresentation();var before=cache.Get(g);var count=cache.WireBuilds;
    g.SetWireTransforms([wire.Id],"graft");var after=cache.Get(g);
    Check(after.Wires.Single().Wire.Transform=="graft"&&before.Wires.Single().Wire.Transform=="none");
    Check(after.Wires.Single().Start==before.Wires.Single().Start&&after.Wires.Single().Bounds==before.Wires.Single().Bounds&&cache.WireBuilds==count);
    Check(ReferenceEquals(before.Nodes[0],after.Nodes[0]));
});
tests.Add(("in-flight solves keep immutable old transform plans and publish only fresh results",async()=>{
    var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var resume=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var registry=StandardComponents.Create(factory);
    registry.Register(new("Delayed","Test","",[],[new("T",Access:PortAccess.Tree)],async c=>{entered.TrySetResult();await resume.Task;return new Dictionary<string,object?>{{"T",DataTree.List(new object?[]{1d,2d,3d})}};}));
    var g=new Graph(registry);var a=g.Add("Delayed");var b=g.Add("Merge");var w=g.Connect(new(a.Id,"T"),new(b.Id,"A"));
    var old=g.SolveAsync();await entered.Task;g.SetWireTransforms([w.Id],"graft");resume.SetResult();var stale=await old;
    Check(stale.Stale&&g.LastResult is null&&stale.Nodes[b.Id].Outputs["R"].Branches.Length==1);
    var fresh=await g.SolveAsync();Check(!fresh.Stale&&fresh.Nodes[b.Id].Outputs["R"].Branches.Length==3);
}));
Test("indexed input inspection allocations do not grow with unrelated wires",()=>{
    var(g,source,target,wire)=Example();for(var i=0;i<1500;i++){var a=g.Add("Number");var b=g.Add("Add");g.Connect(new(a.Id,"N"),new(b.Id,"A"));}
    g.Incoming(wire.To);var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)g.Incoming(wire.To);
    var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Console.WriteLine($"METRIC 1000 indexed input snapshots: {bytes} bytes");Check(bytes<100000);
});
Test("data preview retains sparse paths explicit nulls and empty branches",()=>{
    var tree=new DataTree([new(new([1,2]),[null,true,"hello"]),new(new([9]),[])]);var preview=TreePreview.Create(tree);
    Check(preview.BranchCount==2&&preview.ItemCount==3&&preview.Branches[0].Path=="{1;2}"&&preview.Branches[0].Items[0]=="<null>");
    Check(preview.Branches[1].Items.IsEmpty&&preview.Branches[1].ItemCount==0);
});
Test("data preview is bounded for large trees and long strings",()=>{
    var text=new string('x',1000000);var tree=new DataTree(Enumerable.Range(0,10000).Select(i=>new DataBranch(new([i]),[text,text,text])));
    TreePreview.Create(tree);var start=GC.GetAllocatedBytesForCurrentThread();var preview=TreePreview.Create(tree,3,2,32);
    Check(preview.OmittedBranches==9997&&preview.Branches.Length==3&&preview.Branches.All(b=>b.Items.Length==2&&b.Items.All(t=>t.Length<=32)));
    Check(GC.GetAllocatedBytesForCurrentThread()-start<16000);
});
Test("data preview does not execute plugin formatting or geometry evaluation",()=>{
    var curve=factory.Circle(Vec3.Zero,2);var preview=TreePreview.Create(DataTree.List(new object?[]{new HostileFormat(),curve}));
    Check(preview.Branches[0].Items[0]=="<HostileFormat>"&&preview.Branches[0].Items[1].Contains("degree 2"));
});
Test("data preview uses invariant numbers and escapes control characters",()=>{
    var original=CultureInfo.CurrentCulture;
    try{CultureInfo.CurrentCulture=new CultureInfo("pl-PL");var preview=TreePreview.Create(DataTree.List(new object?[]{1.5,"a\nb\tc"}));
        Check(preview.Branches[0].Items[0]=="1.5"&&preview.Branches[0].Items[1].Contains("\\n")&&!preview.Branches[0].Items[1].Contains('\n'));}
    finally{CultureInfo.CurrentCulture=original;}
});
Test("data preview validates limits and handles an empty tree",()=>{
    Check(TreePreview.Create(DataTree.Empty).Branches.IsEmpty);
    Throws<ArgumentOutOfRangeException>(()=>TreePreview.Create(DataTree.Empty,0));
    Throws<ArgumentOutOfRangeException>(()=>TreePreview.Create(DataTree.Empty,1,1000));
});
Test("operator symbols are distinct and every registry component has a bounded key",()=>{
    var registry=StandardComponents.Create(factory);
    var operators=new[]{"Add","Subtract","Multiply","Divide","Power","Sine","Cosine","Tangent"};
    Check(operators.Select(id=>ComponentGlyphs.IconKey(id,"Maths")).Distinct().Count()==operators.Length);
    Check(registry.Definitions.All(d=>ComponentGlyphs.IconKey(d.Id,d.Category).Length is >0 and <64));
});
Test("preview truncation never leaves a broken UTF-16 surrogate pair",()=>{
    var text=string.Concat(Enumerable.Repeat("😀",80));
    foreach(var limit in Enumerable.Range(16,24))
    {
        var result=TreePreview.Create(DataTree.Item(text),1,1,limit).Branches[0].Items[0];
        Check(result.Length<=limit);
        for(var i=0;i<result.Length;i++)
            if(char.IsHighSurrogate(result[i]))Check(++i<result.Length&&char.IsLowSurrogate(result[i]));
            else Check(!char.IsLowSurrogate(result[i]));
    }
});
Test("returning a drag to its start does not add an empty undo record",()=>{
    var(g,source,target,wire)=Example();var session=new GraphSession(g);var before=g.Snapshot();
    g.Update(source with{X=12});g.Update(source);session.Record("Move",before);
    Check(!session.CanUndo&&!session.CanRedo);
    session.Edit("Add",x=>x.Add("Number"));session.Undo();var redoBefore=session.Graph.Snapshot();
    session.Record("Unchanged",redoBefore);Check(session.CanRedo);
});
Test("large transform batches invalidate a shared downstream chain once",()=>{
    var g=new Graph(StandardComponents.Create(factory));var source=g.Add("Number");var previous=source;var port="N";
    for(var i=0;i<1200;i++){var node=g.Add("Add");g.Connect(new(previous.Id,port),new(node.Id,"A"));previous=node;port="R";}
    Solve(g);var plans=g.ExecutionPlanBuilds;var topology=g.TopologyRevision;
    var ids=g.Wires.Select(w=>w.Id).ToArray();var before=GC.GetAllocatedBytesForCurrentThread();
    g.SetWireTransforms(ids,"flatten");var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
    Console.WriteLine($"METRIC 1200-wire transform batch: {bytes} bytes");Check(bytes<2_000_000);
    var changed=Solve(g);
    Check(changed.ErrorCount==0&&changed.Evaluated==1200&&changed.Cached==1);
    Check(g.ExecutionPlanBuilds==plans&&g.TopologyRevision==topology&&g.Wires.All(w=>w.Transform=="flatten"));
    Check(Solve(g).Evaluated==0);
});
var failed=0;foreach(var(name,run)in tests)try{await run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="input-contracts",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
sealed class HostileFormat{public override string ToString()=>throw new Exception("Formatting must not execute plugin code.");}
