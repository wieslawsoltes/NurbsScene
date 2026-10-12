using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.IO;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

var tests=new List<(string Name,Action Run)>();
var factory=new GeometryFactory(new ManagedNurbsEvaluator());
ComponentRegistry Registry()=>StandardComponents.Create(factory);
void Test(string name,Action action)=>tests.Add((name,action));
void Check(bool value,string reason="Assertion failed"){if(!value)throw new Exception(reason);}
void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
GraphResult Solve(Graph g)=>g.SolveAsync().GetAwaiter().GetResult();
(Graph G,GraphNode A,GraphNode B,GraphNode C) Chain()
{
    var g=new Graph(Registry());var a=g.Add("Number",20,40,new Dictionary<string,object?>{{"value",7d}},"Input");
    var b=g.Add("Add",250,70);var c=g.Add("Add",460,170);
    g.Connect(new(a.Id,"N"),new(b.Id,"A"));g.Connect(new(b.Id,"R"),new(c.Id,"A"));return(g,a,b,c);
}
Test("parameter edits retain all wire records and unaffected node records",()=>{
    var(g,a,b,c)=Chain();var p=new GraphPresentation();var before=p.Get(g);var nb=p.NodeBuilds;var wb=p.WireBuilds;
    g.Update(a with{Parameters=a.Parameters.SetItem("value",12d)});var after=p.Get(g);
    Check(ReferenceEquals(before.Nodes[1],after.Nodes[1])&&ReferenceEquals(before.Nodes[2],after.Nodes[2]));
    Check(before.Wires==after.Wires&&p.WireBuilds==wb&&p.NodeBuilds==nb+1);
    Check((double)before.Nodes[0].Node.Parameters["value"]! ==7&&(double)after.Nodes[0].Node.Parameters["value"]! ==12);
});
Test("one node move updates only its incident wire and preserves old snapshots",()=>{
    var(g,a,b,c)=Chain();var p=new GraphPresentation();var before=p.Get(g);var wb=p.WireBuilds;
    g.Update(a with{X=60});var after=p.Get(g);
    Check(!ReferenceEquals(before.Wires[0],after.Wires[0])&&ReferenceEquals(before.Wires[1],after.Wires[1]));
    Check(p.WireBuilds==wb+1&&before.Wires[0].Start.X+40==after.Wires[0].Start.X);
    Check(before.Nodes[0].Bounds.X==20&&after.Nodes[0].Bounds.X==60);
});
Test("joint movement deduplicates incident-wire rebuilding",()=>{
    var(g,a,b,c)=Chain();var p=new GraphPresentation();p.Get(g);var wb=p.WireBuilds;
    g.MoveNodes([new(a.Id,30,50),new(b.Id,260,80)]);p.Get(g);Check(p.WireBuilds==wb+2);
});
Test("topology changes reuse unaffected nodes and wire paths",()=>{
    var(g,a,b,c)=Chain();var p=new GraphPresentation();var before=p.Get(g);var d=g.Add("Number");
    var after=p.Get(g);Check(ReferenceEquals(before.Nodes[0],after.Nodes[0])&&ReferenceEquals(before.Wires[0],after.Wires[0]));
    g.Connect(new(d.Id,"N"),new(c.Id,"B"));var linked=p.Get(g);Check(linked.Wires.Length==3&&ReferenceEquals(after.Wires[1],linked.Wires[1]));
    g.Remove(b.Id);var removed=p.Get(g);Check(removed.Wires.Length==1&&removed.Wires[0].Wire.From.Node==d.Id);
});
Test("topology revision and cheap counts distinguish node-only edits",()=>{
    var(g,a,b,c)=Chain();var topology=g.TopologyRevision;g.Update(a with{Name="Renamed",Preview=false});
    Check(g.TopologyRevision==topology&&g.NodeCount==3&&g.WireCount==2);
    g.Disconnect(g.Wires[0].Id);Check(g.TopologyRevision==topology+1&&g.WireCount==1);
});
Test("random incremental drawings match fresh geometry and all ports",()=>{
    var(g,a,b,c)=Chain();var p=new GraphPresentation();var rng=new Random(5518);
    for(var j=0;j<150;j++){
        var nodes=g.Nodes;var n=nodes[rng.Next(nodes.Count)];g.Update(n with{X=rng.Next(-700,700),Y=rng.Next(-700,700)});
        var actual=p.Get(g);var expected=new GraphPresentation().Get(g);
        Check(actual.Nodes.SequenceEqual(expected.Nodes)&&actual.Wires.SequenceEqual(expected.Wires));
        if(j%10==0){var extra=g.Add("Number");p.Get(g);g.Remove(extra.Id);}
    }
});
Test("sparse edits allocate substantially less than a cold 1000-node drawing",()=>{
    var g=new Graph(Registry());var previous=g.Add("Number");var first=previous;var port="N";
    for(var i=1;i<1000;i++){var next=g.Add("Add",i*200,0);g.Connect(new(previous.Id,port),new(next.Id,"A"));previous=next;port="R";}
    var p=new GraphPresentation();p.Get(g);g.Update(first with{X=1});p.Get(g);
    var before=GC.GetAllocatedBytesForCurrentThread();for(var i=2;i<12;i++){g.Update(first with{X=i});p.Get(g);}
    var edited=(GC.GetAllocatedBytesForCurrentThread()-before)/10;
    before=GC.GetAllocatedBytesForCurrentThread();new GraphPresentation().Get(g);var cold=GC.GetAllocatedBytesForCurrentThread()-before;
    Console.WriteLine($"METRIC sparse-edit={edited} B cold={cold} B");Check(edited<cold/3&&edited<70_000);
});
Test("fragment captures only selected nodes and internal wires",()=>{
    var(g,a,b,c)=Chain();var fragment=GraphFragment.Capture(g,[a.Id,b.Id]);
    Check(fragment.Nodes.Length==2&&fragment.Wires.Length==1&&fragment.Wires[0].To.Node==b.Id);
    Throws<ArgumentException>(()=>GraphFragment.Capture(g,["missing"]));
    var snapshot=fragment.Snapshot();snapshot.Nodes[0]=c;Check(fragment.Nodes[0].Id==a.Id);
});
Test("paste preserves identity-independent data and produces one undo record",()=>{
    var(g,a,b,c)=Chain();g.Update(b with{Enabled=false,Preview=false,Matching="shortest",Name="Target"});
    var fragment=GraphFragment.Capture(g,[a.Id,b.Id]);var session=new GraphSession(g);var changed=0;g.Changed+=()=>changed++;
    var ids=fragment.Paste(session,100,200);Check(ids.Length==2&&ids.All(id=>id!=a.Id&&id!=b.Id));
    Check(g.NodeCount==5&&g.WireCount==3&&changed==1&&session.CanUndo);
    var clone=g.GetNode(ids[1]);Check(clone.Name=="Target"&&!clone.Enabled&&!clone.Preview&&clone.Matching=="shortest");
    Check(g.GetNode(ids[0]).X==a.X+100&&g.GetNode(ids[0]).Y==a.Y+200);
    Check(session.Undo()&&session.Graph.NodeCount==3&&!session.CanUndo&&session.CanRedo);
    Check(session.Redo()&&session.Graph.NodeCount==5&&session.Graph.GetNode(ids[1]).Name=="Target");
});
Test("repeat pastes have distinct identities without mutating the fragment",()=>{
    var(g,a,b,c)=Chain();var f=GraphFragment.Capture(g,[a.Id,b.Id]);var s=new GraphSession(new(Registry()));
    var one=f.Paste(s);var two=f.Paste(s);Check(!one.Intersect(two).Any()&&s.Graph.WireCount==2&&f.Nodes[0].Id==a.Id);
});
Test("paste rejects missing components and overflow without changing destination",()=>{
    var(g,a,b,c)=Chain();var f=GraphFragment.Capture(g,[a.Id]);
    var empty=new GraphSession(new(new ComponentRegistry()));Throws<NotSupportedException>(()=>f.Paste(empty));Check(empty.Graph.NodeCount==0&&!empty.CanUndo);
    g.Update(a with{X=double.MaxValue});f=GraphFragment.Capture(g,[a.Id]);var s=new GraphSession(new(Registry()));
    Throws<ArgumentException>(()=>f.Paste(s,double.MaxValue,0));Check(s.Graph.NodeCount==0&&!s.CanUndo);
    Throws<ArgumentException>(()=>f.Paste(s,double.NaN,0));
});
Test("empty paste does not change revision or history",()=>{
    var g=new Graph(Registry());var s=new GraphSession(g);Check(GraphFragment.Capture(g,[]).Paste(s).IsEmpty&&g.Revision==0&&!s.CanUndo);
});
Test("external fragment arrays and mutable nested values are detached",()=>{
    var registry=Registry();var values=new object?[]{1d,2d};
    var nodes=new[]{new GraphNode("a","Number",0,0,ImmutableDictionary<string,object?>.Empty.Add("nested",values))};
    var f=GraphFragment.FromSnapshot(registry,new(nodes,[]));values[0]=99d;nodes[0]=nodes[0] with{X=999};
    Check(f.Nodes[0].X==0&&(double)((ImmutableArray<object?>)f.Nodes[0].Parameters["nested"]!)[0]! ==1);
});
Test("malformed and cyclic fragment wires fail before live mutation",()=>{
    var(g,a,b,c)=Chain();var snap=g.Snapshot();var wire=new Wire("bad",new(c.Id,"R"),new(b.Id,"B"));
    Throws<InvalidOperationException>(()=>GraphFragment.FromSnapshot(g.Registry,new(snap.Nodes,snap.Wires.Append(wire).ToArray())));
    Throws<ArgumentException>(()=>GraphFragment.FromSnapshot(g.Registry,new(snap.Nodes,[new("x",new(a.Id,"missing"),new(b.Id,"A"))])));
});
Test("clipboard codec round-trips exact rational data trees and null branches",()=>{
    var g=new Graph(Registry());var curve=factory.Circle(new(2,3,4),5);
    var tree=new DataTree([new(new([2,4]),[curve,null,new DataPath([3,7])]),new(new([9]),[])]);
    var node=g.Add("Text",parameters:new Dictionary<string,object?>{{"tree",tree},{"text","abc"}});
    var original=GraphFragment.Capture(g,[node.Id]);var text=GraphFragmentFormat.Serialize(original,g.Registry);
    var fragment=GraphFragmentFormat.Deserialize(factory.Evaluator,g.Registry,text);var restored=(DataTree)fragment.Nodes[0].Parameters["tree"]!;
    Check(restored.Branches.Length==2&&restored.Branches[1].Items.IsEmpty&&restored.Branches[0].Items[1] is null);
    var c=(NurbsCurve)restored.Branches[0].Items[0]!;Check(c.Knots.SequenceEqual(curve.Knots)&&c.ControlPoints.SequenceEqual(curve.ControlPoints));
});
Test("clipboard format rejects foreign payloads and excess bytes",()=>{
    Throws<FormatException>(()=>GraphFragmentFormat.Deserialize(factory.Evaluator,Registry(),"not a clipboard"));
    Throws<FormatException>(()=>GraphFragmentFormat.Deserialize(factory.Evaluator,Registry(),GraphFragmentFormat.Header+new string(' ',GraphFragmentFormat.MaximumBytes)));
});
Test("alignment uses mixed node dimensions and preserves numeric result cache",()=>{
    var(g,a,b,c)=Chain();var session=new GraphSession(g);var before=Solve(g);var topology=g.TopologyRevision;var notifications=0;g.Changed+=()=>notifications++;
    GraphArrangement.Apply(session,[a.Id,b.Id,c.Id],GraphArrangementMode.Right);
    var nodes=new GraphPresentation().Get(g).Nodes;Check(nodes.Select(n=>n.Bounds.X+n.Bounds.Width).Distinct().Count()==1);
    var after=Solve(g);Check(after.Evaluated==0&&g.TopologyRevision==topology&&notifications==1&&ReferenceEquals(before.Nodes,after.Nodes));
    Check(session.Undo()&&session.Graph.GetNode(a.Id).X==a.X);
});
Test("distribution spaces centers and retains outer anchors",()=>{
    var(g,a,b,c)=Chain();var session=new GraphSession(g);var prior=new GraphPresentation().Get(g).Nodes;
    GraphArrangement.Apply(session,[a.Id,b.Id,c.Id],GraphArrangementMode.DistributeHorizontal);
    var nodes=new GraphPresentation().Get(g).Nodes.OrderBy(n=>n.Bounds.X+n.Bounds.Width/2).ToArray();
    double Center(int i)=>nodes[i].Bounds.X+nodes[i].Bounds.Width/2;
    Check(Math.Abs(Center(1)-(Center(0)/2+Center(2)/2))<1e-10);
    Check(Center(0)==prior[0].Bounds.X+prior[0].Bounds.Width/2&&Center(2)==prior[2].Bounds.X+prior[2].Bounds.Width/2);
});
Test("arrangement rejects stale selections and no-ops remain history-free",()=>{
    var(g,a,b,c)=Chain();var session=new GraphSession(g);var revision=g.Revision;
    Throws<ArgumentException>(()=>GraphArrangement.Apply(session,[a.Id,"missing"],GraphArrangementMode.Left));
    GraphArrangement.Apply(session,[a.Id],GraphArrangementMode.Left);GraphArrangement.Apply(session,[a.Id,b.Id],GraphArrangementMode.DistributeVertical);
    Check(!session.CanUndo&&g.Revision==revision);
});
Test("low-zoom port hits choose nearest row rather than first input",()=>{
    var g=new Graph(Registry());g.Add("Circle");var d=new GraphPresentation().Get(g);var circle=d.Nodes.Single();
    for(var i=0;i<circle.Definition.Inputs.Length;i++)
    {
        var point=circle.Port(false,i);
        foreach(var zoom in new[]{.15,.3,.42,.8,2.0})
        {
            var hit=GraphHitTesting.PortAt(d,point,9/zoom);
            Check(hit is not null&&!hit.Output&&hit.Port==circle.Definition.Inputs[i].Name,$"Wrong port at zoom {zoom}: {hit}");
        }
    }
});
Test("equal-distance port hits prefer frontmost node and radial misses remain misses",()=>{
    var g=new Graph(Registry());g.Add("Circle");var front=g.Add("Circle");var d=new GraphPresentation().Get(g);var point=d.Nodes[1].Port(false,0);
    Check(GraphHitTesting.PortAt(d,point,9)?.Node==front.Id);
    Check(GraphHitTesting.PortAt(d,new(point.X-9,point.Y-9),9) is null);
    Check(GraphHitTesting.PortAt(d,point,0)?.Node==front.Id);
});
Test("port hit-test rejects nonfinite inputs without fabricated hits",()=>{
    var d=new GraphPresentation().Get(new Graph(Registry()));
    Throws<ArgumentOutOfRangeException>(()=>GraphHitTesting.PortAt(d,new(double.NaN,0),9));
    Throws<ArgumentOutOfRangeException>(()=>GraphHitTesting.PortAt(d,new(0,0),double.PositiveInfinity));
    Check(GraphHitTesting.PortAt(d,new(0,0),9) is null);
});
Test("selection windows enclose complete nodes in either vertical direction",()=>{
    var(g,a,b,c)=Chain();var drawing=new GraphPresentation().Get(g);
    foreach(var pair in new[]{(new CanvasPoint(-20,-20),new CanvasPoint(900,400)),(new CanvasPoint(-20,400),new CanvasPoint(900,-20))})
        Check(GraphSelection.InRectangle(drawing,pair.Item1,pair.Item2).SequenceEqual(new[]{a.Id,b.Id,c.Id}));
    Check(GraphSelection.InRectangle(drawing,new(15,35),new(201,79)).SequenceEqual(new[]{a.Id}));
});
Test("crossing selection intersects while window selection requires containment",()=>{
    var(g,a,b,c)=Chain();var drawing=new GraphPresentation().Get(g);
    Check(GraphSelection.InRectangle(drawing,new(30,45),new(40,50)).IsEmpty);
    Check(GraphSelection.InRectangle(drawing,new(40,45),new(30,50)).SequenceEqual(new[]{a.Id}));
    Throws<ArgumentException>(()=>GraphSelection.InRectangle(drawing,new(double.NaN,0),new(10,10)));
});
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+"\n"+error);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="editing",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
