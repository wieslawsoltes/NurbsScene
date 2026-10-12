using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.IO;

var tests = new List<(string Name, Action Run)>();
var factory = new GeometryFactory(new ManagedNurbsEvaluator());
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Graph Graph() => new(StandardComponents.Create(factory));
GraphResult Solve(Graph g) => g.SolveAsync().GetAwaiter().GetResult();
NodeResult Result(params DataBranch[] branches) => new(ImmutableDictionary<string, DataTree>.Empty.Add("T",new(branches)),"ok",[],0);
(Graph G, GraphNode N, NodeResult R) Example(string text="one\ntwo")
{
    var g=Graph();var n=g.Add("Panel",parameters:new Dictionary<string,object?>{{"text",text}});
    return(g,n,Solve(g).Nodes[n.Id]);
}
Test("missing metadata preserves historical Panel and Text display defaults",()=>{
    var(g,n,r)=Example();var p=new PanelPreviewCache().Get(n,r);
    Check(p.Lines.SequenceEqual(new[]{"{0}","one","two"}));
    var t=g.Add("Text");Check(!PanelDisplayOptions.For(t).DrawPaths&&PanelDisplayOptions.For(t).WrapItems);
    Check(n.PanelDisplay is null&&!PanelDisplayOptions.For(n).DrawIndices);
});
Test("paths can be hidden without changing the output tree",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);var output=r.Outputs["T"];
    PanelDisplayEditing.Apply(s,g,n,new(DrawPaths:false));var next=g.GetNode(n.Id);
    Check(new PanelPreviewCache().Get(next,r).Lines.SequenceEqual(new[]{"one","two"}));
    Check(ReferenceEquals(Solve(g).Nodes[n.Id].Outputs["T"],output));
});
Test("indices restart at zero for each branch and identify items not wrapped rows",()=>{
    var g=Graph();var n=g.Add("Panel") with{PanelDisplay=new(false,true,true)};
    var r=Result(new DataBranch(new([1]),["abcdef","second"]),new(new([4]),["last"]));
    var p=new PanelPreviewCache().Get(n,r,12,5);
    Check(p.Lines.SequenceEqual(new[]{"0: ab","cdef","1: se","cond","0: la","st"}));
});
Test("nonwrapping items clip visibly but retain explicit line breaks",()=>{
    var g=Graph();var n=g.Add("Panel") with{PanelDisplay=new(false,false,false)};
    var r=Result(new DataBranch(DataPath.Root,["abcdef\r\nxy\nz"]));var p=new PanelPreviewCache().Get(n,r,8,4);
    Check(p.Lines.SequenceEqual(new[]{"abc…","xy","z"})&&p.Truncated&&p.Footer.EndsWith("…"));
    Check(r.Outputs["T"].AllItems.Single() as string=="abcdef\r\nxy\nz");
});
Test("unwrapped grapheme clipping does not split emoji or combining marks",()=>{
    var n=Graph().Add("Panel") with{PanelDisplay=new(false,false,false)};
    var p=new PanelPreviewCache().Get(n,Result(new DataBranch(DataPath.Root,["a🌐e\u0301z"])),8,3);
    Check(p.Lines.Single()=="a🌐…"&&p.Truncated);
    Check(new PanelPreviewCache().Get(n,Result(new DataBranch(DataPath.Root,["🌐abc"])),8,1).Lines.Single()=="…");
});
Test("small wrapping budgets remain bounded with indices enabled",()=>{
    var n=Graph().Add("Panel") with{PanelDisplay=new(false,true,true)};
    var p=new PanelPreviewCache().Get(n,Result(new DataBranch(DataPath.Root,["long value"])),3,1);
    Check(p.Lines.Length==3&&p.Truncated&&p.Lines.All(l=>l.Length<=1));
});
Test("display options are one undoable revision without component evaluation",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);var before=Solve(g);var revision=g.Revision;var topology=g.TopologyRevision;var events=0;g.Changed+=()=>events++;
    Check(PanelDisplayEditing.Apply(s,g,n,new(false,true,false)));var after=Solve(g);
    Check(events==1&&g.Revision==revision+1&&g.TopologyRevision==topology&&after.Evaluated==0&&ReferenceEquals(after.Nodes,before.Nodes));
    Check(s.Undo()&&s.Graph.GetNode(n.Id).PanelDisplay is null&&!s.CanUndo&&s.Redo()&&s.Graph.GetNode(n.Id).PanelDisplay==new PanelDisplayOptions(false,true,false));
});
Test("display changes preserve size, parameters and every wire path",()=>{
    var(g,n,r)=Example();g.Update(n with{CanvasSize=new(400,220),Name="Inspector",Preview=false});n=g.GetNode(n.Id);
    var target=g.Add("ListLength");g.Connect(new(n.Id,"T"),new(target.Id,"L"));var p=new GraphPresentation();var before=p.Get(g);var wb=p.WireBuilds;
    PanelDisplayEditing.Apply(new(g),g,n,new(false,true,false));var next=g.GetNode(n.Id);var after=p.Get(g);
    Check(ReferenceEquals(n.Parameters,next.Parameters)&&next.CanvasSize==n.CanvasSize&&next.Name==n.Name&&next.Preview==n.Preview);
    Check(before.Wires==after.Wires&&p.WireBuilds==wb&&before.Nodes[0].Bounds==after.Nodes[0].Bounds);
});
Test("no-op options preserve redo and absent metadata",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);s.Edit("Move",x=>x.Update(n with{X=40}));s.Undo();g=s.Graph;n=g.GetNode(n.Id);var revision=g.Revision;
    Check(!PanelDisplayEditing.Apply(s,g,n,new())&&s.CanRedo&&g.Revision==revision&&n.PanelDisplay is null);
});
Test("restoring defaults removes redundant metadata",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);PanelDisplayEditing.Apply(s,g,n,new(false,true,false));
    Check(PanelDisplayEditing.Apply(s,g,g.GetNode(n.Id),new())&&g.GetNode(n.Id).PanelDisplay is null);
    var t=g.Add("Text");PanelDisplayEditing.Apply(s,g,t,new(true,true,false));
    PanelDisplayEditing.Apply(s,g,g.GetNode(t.Id),PanelDisplayOptions.TextDefault);Check(g.GetNode(t.Id).PanelDisplay is null);
});
Test("stale and removed menu owners cannot overwrite newer edits",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);g.Update(n with{Name="external"});
    Throws<InvalidOperationException>(()=>PanelDisplayEditing.Apply(s,g,n,new(false)));
    n=g.GetNode(n.Id);g.Remove(n.Id);Throws<InvalidOperationException>(()=>PanelDisplayEditing.Apply(s,g,n,new(false)));Check(!s.CanUndo);
});
Test("replaced graph and unsupported component are rejected",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);s.Replace(Graph());
    Throws<InvalidOperationException>(()=>PanelDisplayEditing.Apply(s,g,n,new(false)));
    g=Graph();n=g.Add("Number");Throws<ArgumentException>(()=>PanelDisplayEditing.Apply(new(g),g,n,new()));
});
Test("preview cache invalidates only for effective display or output changes",()=>{
    var(g,n,r)=Example();var cache=new PanelPreviewCache();var first=cache.Get(n,r);
    Check(ReferenceEquals(first,cache.Get(n with{Name="renamed",X=20,PanelDisplay=new()},r))&&cache.BuildCount==1);
    var changed=cache.Get(n with{PanelDisplay=new(false)},r);Check(cache.BuildCount==2&&!ReferenceEquals(first,changed));
    Check(ReferenceEquals(changed,cache.Current));
});
Test("display options round-trip through workspace and exact fragment interchange",()=>{
    var(g,n,r)=Example();PanelDisplayEditing.Apply(new(g),g,n,new(false,true,false));
    var json=WorkspaceFormat.Serialize(new(),g);var restored=WorkspaceFormat.Deserialize(factory.Evaluator,g.Registry,json).Graph;
    Check(restored.GetNode(n.Id).PanelDisplay==new PanelDisplayOptions(false,true,false));
    var f=GraphFragmentFormat.Deserialize(factory.Evaluator,g.Registry,GraphFragmentFormat.Serialize(GraphFragment.Capture(g,[n.Id]),g.Registry));
    var s=new GraphSession(Graph());var id=f.Paste(s).Single();Check(s.Graph.GetNode(id).PanelDisplay==new PanelDisplayOptions(false,true,false));
});
Test("old documents omit new metadata and retain original output",()=>{
    var(g,n,r)=Example();var json=WorkspaceFormat.Serialize(new(),g);Check(!json.Contains("panelDisplay"));
    var restored=WorkspaceFormat.Deserialize(factory.Evaluator,g.Registry,json).Graph;Check(restored.GetNode(n.Id).PanelDisplay is null);
    Check(Solve(restored).Nodes[n.Id].Outputs["T"].AllItems.SequenceEqual(r.Outputs["T"].AllItems));
});
Test("all eight display combinations preserve numerical and data-tree identity",()=>{
    var(g,n,r)=Example();var s=new GraphSession(g);var original=Solve(g).Nodes;
    for(var mask=0;mask<8;mask++){
        var options=new PanelDisplayOptions((mask&1)!=0,(mask&2)!=0,(mask&4)!=0);
        PanelDisplayEditing.Apply(s,g,g.GetNode(n.Id),options);
        Check(ReferenceEquals(Solve(g).Nodes,original));
    }
});
Test("hidden empty branches and huge text retain bounded display work",()=>{
    var n=Graph().Add("Panel") with{PanelDisplay=new(false,true,false)};
    var r=Result(new DataBranch(DataPath.Root,[new string('x',60000)]));var p=new PanelPreviewCache().Get(n,r,64,128);
    Check(p.Truncated&&p.Lines.Length==1&&p.Lines[0].Length<=128);
    r=Result(new DataBranch(new([4]),[]));Check(new PanelPreviewCache().Get(n,r).Lines.IsEmpty);
});
Test("warm option-aware preview reads allocate zero managed bytes",()=>{
    var(g,n,r)=Example();n=n with{PanelDisplay=new(false,true,false)};var c=new PanelPreviewCache();c.Get(n,r);
    for(var i=0;i<100;i++)c.Get(n,r);
    var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)c.Get(n,r);
    var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0);Console.WriteLine($"METRIC option-preview-10000={bytes} B");
});
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="panel-display",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
