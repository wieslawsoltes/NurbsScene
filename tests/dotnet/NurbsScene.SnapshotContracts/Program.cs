using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

var tests=new List<(string Name,Action Run)>();
var factory=new GeometryFactory(new ManagedNurbsEvaluator());
void Test(string name,Action run)=>tests.Add((name,run));
void Check(bool ok){if(!ok)throw new Exception("Assertion failed");}
void Throws<T>(Action run)where T:Exception{try{run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
Graph Graph()=>new(StandardComponents.Create(factory));
(Graph G,GraphNode A,GraphNode B,Wire W) Example(){var g=Graph();var a=g.Add("Number");var b=g.Add("Add",250,30);return(g,a,b,g.Connect(new(a.Id,"N"),new(b.Id,"A")));}
Test("repeated graph and registry collection reads reuse immutable views",()=>{
    var(g,a,b,w)=Example();var nodes=g.Nodes;var wires=g.Wires;var defs=g.Registry.Definitions;
    Check(ReferenceEquals(nodes,g.Nodes)&&ReferenceEquals(wires,g.Wires)&&ReferenceEquals(defs,g.Registry.Definitions));
    Throws<NotSupportedException>(()=>((IList<GraphNode>)nodes)[0]=b);
    Throws<NotSupportedException>(()=>((IList<Wire>)wires).Clear());
    Throws<NotSupportedException>(()=>((IList<ComponentDefinition>)defs).Clear());
    Check(g.NodeCount==2&&g.WireCount==1);
});
Test("node-only changes preserve wire views and all prior node snapshots",()=>{
    var(g,a,b,w)=Example();var nodes=g.Nodes;var wires=g.Wires;g.Update(a with{X=99});
    Check(!ReferenceEquals(nodes,g.Nodes)&&ReferenceEquals(wires,g.Wires)&&nodes[0].X==0&&g.Nodes[0].X==99);
    var next=g.Nodes;g.Update(g.GetNode(a.Id));Check(ReferenceEquals(next,g.Nodes));
});
Test("wire-only changes preserve node views and previous wire metadata",()=>{
    var(g,a,b,w)=Example();var nodes=g.Nodes;var wires=g.Wires;g.SetWireTransforms([w.Id],"graft");
    Check(ReferenceEquals(nodes,g.Nodes)&&!ReferenceEquals(wires,g.Wires)&&wires[0].Transform=="none"&&g.Wires[0].Transform=="graft");
    var next=g.Wires;g.SetWireTransforms([w.Id],"graft");Check(ReferenceEquals(next,g.Wires));
});
Test("connection replacement and node deletion invalidate the proper views",()=>{
    var(g,a,b,w)=Example();var c=g.Add("Number");var nodes=g.Nodes;var wires=g.Wires;
    var replacement=g.Connect(new(c.Id,"N"),new(b.Id,"A"),replace:true);
    Check(ReferenceEquals(nodes,g.Nodes)&&g.Wires.Single()==replacement&&wires.Single()==w);
    g.Remove(c.Id);Check(g.Nodes.Count==2&&g.Wires.Count==0&&nodes.Count==3);
});
Test("registry growth publishes a new view while failed registration preserves it",()=>{
    var g=Graph();var registry=g.Registry;var before=registry.Definitions;
    var custom=new ComponentDefinition("Test","Testing","Snapshot test",[],[],_=>ValueTask.FromResult<IReadOnlyDictionary<string,object?>>(new Dictionary<string,object?>()));
    registry.Register(custom);var after=registry.Definitions;
    Check(before.Count+1==after.Count&&registry.Count==after.Count&&!before.Any(d=>d.Id=="Test"));
    Throws<ArgumentException>(()=>registry.Register(custom));Check(ReferenceEquals(after,registry.Definitions));
});
Test("history and serialized snapshots remain independently mutable arrays",()=>{
    var(g,a,b,w)=Example();var nodes=g.Nodes;var wires=g.Wires;var snapshot=g.Snapshot();
    snapshot.Nodes[0]=a with{X=500};snapshot.Wires[0]=w with{Transform="graft"};
    Check(g.GetNode(a.Id).X==0&&g.Wires[0].Transform=="none"&&ReferenceEquals(nodes,g.Nodes)&&ReferenceEquals(wires,g.Wires));
});
Test("wire transforms never rebuild drawing adjacency or port geometry",()=>{
    var(g,a,b,w)=Example();var p=new GraphPresentation();var initial=p.Get(g);var builds=p.TopologyBuilds;var paths=p.WireBuilds;
    foreach(var transform in new[]{"graft","flatten","simplify","none"}){
        g.SetWireTransforms([w.Id],transform);var current=p.Get(g);
        Check(p.TopologyBuilds==builds&&p.WireBuilds==paths&&current.Nodes==initial.Nodes&&current.Wires[0].Wire.Transform==transform);
        Check(current.Wires[0].Start==initial.Wires[0].Start&&current.Wires[0].End==initial.Wires[0].End);
    }
    Check(initial.Wires[0].Wire.Transform=="none");
});
Test("mixed pending node and wire edits match a fresh drawing",()=>{
    var(g,a,b,w)=Example();var p=new GraphPresentation();p.Get(g);var builds=p.TopologyBuilds;
    for(var i=1;i<100;i++){
        g.Update(g.GetNode(a.Id) with{X=i*3});g.SetWireTransforms([w.Id],i%2==0?"none":"graft");
        var actual=p.Get(g);var expected=new GraphPresentation().Get(g);
        Check(actual.Nodes.SequenceEqual(expected.Nodes)&&actual.Wires.SequenceEqual(expected.Wires)&&p.TopologyBuilds==builds);
    }
});
Test("undo graph replacement cannot reuse stale snapshots or node identities",()=>{
    var(g,a,b,w)=Example();var session=new GraphSession(g);var p=new GraphPresentation();var before=p.Get(g);var nodes=g.Nodes;
    session.Edit("Move",x=>x.Update(a with{X=20}));p.Get(g);session.Undo();var restored=p.Get(session.Graph);
    Check(!ReferenceEquals(nodes,session.Graph.Nodes)&&!ReferenceEquals(before.Nodes[0],restored.Nodes[0])&&restored.Nodes[0].Node.X==a.X);
});
Test("warm graph and registry view reads allocate zero bytes",()=>{
    var(g,a,b,w)=Example();long sum=0;
    for(var i=0;i<100;i++)sum+=g.Nodes.Count+g.Wires.Count+g.Registry.Definitions.Count;
    var start=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++)sum+=g.Nodes.Count+g.Wires.Count+g.Registry.Definitions.Count;
    var bytes=GC.GetAllocatedBytesForCurrentThread()-start;Console.WriteLine($"METRIC warm-collection-reads-10000={bytes} B");Check(bytes==0&&sum>0);
});
Test("empty views are retained and do not become live collections",()=>{
    var g=Graph();var nodes=g.Nodes;var wires=g.Wires;Check(ReferenceEquals(nodes,g.Nodes)&&ReferenceEquals(wires,g.Wires));
    g.Add("Number");Check(nodes.Count==0&&g.Nodes.Count==1&&ReferenceEquals(wires,g.Wires));
});
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="snapshot-contracts",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
