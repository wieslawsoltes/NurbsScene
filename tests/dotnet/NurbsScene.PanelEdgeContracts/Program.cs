using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.IO;
using E = NurbsScene.Presentation.PanelResizeEdges;

var tests = new List<(string Name, Action Run)>();
var factory = new GeometryFactory(new ManagedNurbsEvaluator());
E[] edges = [E.Left, E.Right, E.Top, E.Bottom, E.Left|E.Top, E.Right|E.Top, E.Left|E.Bottom, E.Right|E.Bottom];
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string message="Assertion failed") { if (!value) throw new Exception(message); }
void Near(double a, double b) => Check(Math.Abs(a-b)<1e-9, $"{a:G17} != {b:G17}");
void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
Graph Graph() => new(StandardComponents.Create(factory));
GraphResult Solve(Graph g) => g.SolveAsync().GetAwaiter().GetResult();

Test("all four borders and corners are independently hit-testable",()=>{
    var b=new CanvasBox(-120,40,224,168);
    foreach(var edge in edges){
        var x=(edge&E.Left)!=0?b.X+1:(edge&E.Right)!=0?b.X+b.Width-1:b.X+b.Width/2;
        var y=(edge&E.Top)!=0?b.Y+1:(edge&E.Bottom)!=0?b.Y+b.Height-1:b.Y+b.Height/2;
        Check(PanelLayout.HitTest(b,new(x,y),10)==edge,edge.ToString());
    }
    Check(PanelLayout.HitTest(b,new(b.X+b.Width/2,b.Y+8),10)==E.None,"Header double-click must not be a border resize");
    Check(PanelLayout.HitTest(b,new(b.X+b.Width/2,b.Y+b.Height/2),10)==E.None);
});
Test("large hit tolerances cannot activate opposing edges or consume panel center",()=>{
    var b=new CanvasBox(0,0,128,76);
    Check(PanelLayout.HitTest(b,new(64,38),128)==E.None);
    Check(PanelLayout.HitTest(b,new(1,1),128)==(E.Left|E.Top));
    Check(PanelLayout.HitTest(b,new(127,75),128)==(E.Right|E.Bottom));
    Check(PanelLayout.HitTest(b,new(-1,1),128)==E.None);
    Check(PanelLayout.HitTest(b,new(0,38),0)==E.Left);
});
Test("invalid edges and nonrepresentable coordinates reject before editing",()=>{
    var g=Graph();var p=g.Add("Panel");
    foreach(var e in new[]{E.None,E.Left|E.Right,E.Top|E.Bottom,(E)16,(E)15})
        Throws<ArgumentOutOfRangeException>(()=>new PanelResizeDraft(g,p.Id,e));
    foreach(var x in new[]{double.NaN,double.PositiveInfinity})
        Throws<ArgumentException>(()=>PanelLayout.HitTest(new(0,0,224,168),new(x,0),10));
    Throws<ArgumentOutOfRangeException>(()=>PanelLayout.HitTest(new(0,0,224,168),new(0,0),double.NaN));
    var huge=g.Add("Panel",double.MaxValue,0);Throws<ArgumentException>(()=>new PanelResizeDraft(g,huge.Id,E.Left));
    Throws<ArgumentException>(()=>new PanelResizeDraft(g,g.Add("Number").Id,E.Left));
});
Test("every resize direction preserves its opposite edges and inactive dimensions",()=>{
    var g=Graph();var p=g.Add("Panel",120,-60);var initial=PanelLayout.Bounds(p);
    foreach(var e in edges){
        var d=new PanelResizeDraft(g,p.Id,e);d.Move(-47,29);var b=d.Bounds;
        if((e&E.Left)!=0){Near(b.Width,271);Near(b.X,73);Near(b.X+b.Width,initial.X+initial.Width);}
        else{Near(b.X,initial.X);Near(b.Width,(e&E.Right)!=0?177:224);}
        if((e&E.Top)!=0){Near(b.Height,139);Near(b.Y,-31);Near(b.Y+b.Height,initial.Y+initial.Height);}
        else{Near(b.Y,initial.Y);Near(b.Height,(e&E.Bottom)!=0?197:168);}
        Check(d.Edges==e&&d.Changed&&d.InitialBounds==initial);
    }
});
Test("crossing an opposite edge clamps without flipping or moving the anchor",()=>{
    var g=Graph();var p=g.Add("Panel",-50,90);var initial=PanelLayout.Bounds(p);
    foreach(var e in edges){
        var d=new PanelResizeDraft(g,p.Id,e);
        d.Move((e&E.Left)!=0?1e200:-1e200,(e&E.Top)!=0?1e200:-1e200);
        var b=d.Bounds;
        Check(b.Width>=128&&b.Height>=76);
        if((e&E.Left)!=0)Near(b.X+b.Width,initial.X+initial.Width);
        if((e&E.Top)!=0)Near(b.Y+b.Height,initial.Y+initial.Height);
        d.Move((e&E.Left)!=0?-1e200:1e200,(e&E.Top)!=0?-1e200:1e200);
        Check(d.Bounds.Width<=2048&&d.Bounds.Height<=2048);
    }
});
Test("invalid motion leaves the previous valid draft intact",()=>{
    var g=Graph();var p=g.Add("Panel");var d=new PanelResizeDraft(g,p.Id,E.Left|E.Top);d.Move(-20,-30);var before=d.Bounds;
    Throws<ArgumentException>(()=>d.Move(double.NaN,0));Throws<ArgumentException>(()=>d.Move(1,double.PositiveInfinity));
    Check(d.Bounds==before&&g.GetNode(p.Id).CanvasSize is null);
});
Test("input and output preview ports follow top-left movement with fixed row offsets",()=>{
    var g=Graph();var p=g.Add("Panel",100,50);var d=new PanelResizeDraft(g,p.Id,E.Left|E.Top);d.Move(-40,-30);
    var input=d.MapPort(new(100,69),false);var output=d.MapPort(new(324,69),true);
    Check(input==new CanvasPoint(60,39)&&output==new CanvasPoint(324,39));
    d.Move(0,0);Check(d.MapPort(new(100,69),false)==new CanvasPoint(100,69)&&!d.Changed);
    Throws<ArgumentException>(()=>d.MapPort(new(0,double.NaN),true));
});
Test("left and top commit preserves exact connected numerical results",()=>{
    var g=Graph();var source=g.Add("Number");var p=g.Add("Panel",100,50);var length=g.Add("ListLength",500,50);
    g.Connect(new(source.Id,"N"),new(p.Id,"D"));g.Connect(new(p.Id,"T"),new(length.Id,"L"));
    var old=Solve(g);var topology=g.TopologyRevision;var wireRevision=g.WireRevision;var s=new GraphSession(g);
    var d=new PanelResizeDraft(g,p.Id,E.Left|E.Top);d.Move(-40,-30);Check(d.Commit(s));var solved=Solve(g);
    Check(solved.Evaluated==0&&ReferenceEquals(old.Nodes,solved.Nodes)&&g.TopologyRevision==topology&&g.WireRevision==wireRevision);
    Check(g.GetNode(p.Id).X==60&&g.GetNode(p.Id).Y==20&&g.GetNode(p.Id).CanvasSize==new NodeCanvasSize(264,198));
    Check(s.Undo()&&s.Graph.GetNode(p.Id).X==100&&s.Graph.GetNode(p.Id).CanvasSize is null);
    Check(s.Redo()&&s.Graph.GetNode(p.Id).Y==20);
});
Test("left-top resize changes only incident wire geometry and target metadata",()=>{
    var g=Graph();var source=g.Add("Number",-200,0);var p=g.Add("Panel",100,50);var length=g.Add("ListLength",500,50);var other=g.Add("Add",500,200);
    g.Connect(new(source.Id,"N"),new(p.Id,"D"));g.Connect(new(p.Id,"T"),new(length.Id,"L"));g.Connect(new(source.Id,"N"),new(other.Id,"A"));
    var cache=new GraphPresentation();var old=cache.Get(g);var d=new PanelResizeDraft(g,p.Id,E.Left|E.Top);d.Move(-40,-30);d.Commit(new(g));var current=cache.Get(g);
    Check(current.Wires[0].End==new CanvasPoint(60,39)&&current.Wires[1].Start==new CanvasPoint(324,39));
    Check(ReferenceEquals(old.Wires[2],current.Wires[2])&&ReferenceEquals(old.Nodes[0],current.Nodes[0]));
    Check(old.Wires[0].End==new CanvasPoint(100,69));
});
Test("all-edge undo no-ops preserve redo and absent canvas dimensions",()=>{
    var g=Graph();var p=g.Add("Panel");var s=new GraphSession(g);s.Edit("Name",x=>x.Update(p with{Name="test"}));s.Undo();
    foreach(var e in edges){var d=new PanelResizeDraft(s.Graph,p.Id,e);d.Move(-20,40);d.Move(0,0);Check(!d.Commit(s)&&s.CanRedo&&!s.CanUndo&&s.Graph.GetNode(p.Id).CanvasSize is null);}
});
Test("stale commits reject deleted targets and preserve independent edits",()=>{
    var g=Graph();var p=g.Add("Panel");var s=new GraphSession(g);var d=new PanelResizeDraft(g,p.Id,E.Left);d.Move(-10,0);
    g.Remove(p.Id);Check(!d.IsCurrent(g));Throws<InvalidOperationException>(()=>d.Commit(s));
    p=g.Add("Panel");d=new(g,p.Id,E.Top);d.Move(0,-10);var other=g.Add("Number");Check(d.Commit(s));
    Check(s.Undo()&&s.Graph.TryGetNode(other.Id,out _)&&s.Graph.GetNode(p.Id).Y==0);
});
Test("left-top position and dimensions survive workspace and fragment interchange",()=>{
    var g=Graph();var p=g.Add("Panel",100,50);var d=new PanelResizeDraft(g,p.Id,E.Left|E.Top);d.Move(-40,-30);d.Commit(new(g));
    var restored=WorkspaceFormat.Deserialize(factory.Evaluator,g.Registry,WorkspaceFormat.Serialize(new(),g)).Graph;
    var node=restored.GetNode(p.Id);Check(node.X==60&&node.Y==20&&node.CanvasSize==new NodeCanvasSize(264,198));
    var fragment=GraphFragment.Capture(restored,[p.Id]);var s=new GraphSession(Graph());var ids=fragment.Paste(s,12,14);node=s.Graph.GetNode(ids.Single());
    Check(node.X==72&&node.Y==34&&node.CanvasSize==new NodeCanvasSize(264,198));
});
Test("random resize traces remain bounded with stable opposite anchors",()=>{
    var rng=new Random(8261);var g=Graph();var p=g.Add("Panel",123.25,-456.5);var initial=PanelLayout.Bounds(p);
    foreach(var e in edges){
        var d=new PanelResizeDraft(g,p.Id,e);
        for(var i=0;i<2000;i++){
            d.Move(rng.NextDouble()*8000-4000,rng.NextDouble()*8000-4000);var b=d.Bounds;
            Check(double.IsFinite(b.X)&&double.IsFinite(b.Y)&&b.Width>=128&&b.Width<=2048&&b.Height>=76&&b.Height<=2048);
            if((e&E.Left)!=0)Near(b.X+b.Width,initial.X+initial.Width);else Near(b.X,initial.X);
            if((e&E.Top)!=0)Near(b.Y+b.Height,initial.Y+initial.Height);else Near(b.Y,initial.Y);
        }
    }
});
Test("edge motion hit testing and preview port mapping allocate zero bytes after warmup",()=>{
    var g=Graph();var p=g.Add("Panel",100,50);var d=new PanelResizeDraft(g,p.Id,E.Left|E.Top);var at=new CanvasPoint(100,69);var initial=PanelLayout.Bounds(p);double sum=0;
    for(var i=0;i<100;i++){d.Move(-i,-i);sum+=d.MapPort(at,false).Y;PanelLayout.HitTest(initial,new(101,51),10);}
    var before=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++){d.Move(-i%100,-i%70);sum+=d.MapPort(at,false).Y;PanelLayout.HitTest(initial,new(101,51),10);}
    var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0&&double.IsFinite(sum));Console.WriteLine($"METRIC edge-resize-hit-map-10000={bytes} B");
});
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="panel-edges",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
