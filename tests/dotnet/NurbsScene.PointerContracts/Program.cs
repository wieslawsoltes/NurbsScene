using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Test("all four modifier combinations", () => {
    Check(GraphSelectionModifiers.Mode(false,false) == GraphSelectionMode.Replace);
    Check(GraphSelectionModifiers.Mode(true,false) == GraphSelectionMode.Add);
    Check(GraphSelectionModifiers.Mode(false,true) == GraphSelectionMode.Remove);
    Check(GraphSelectionModifiers.Mode(true,true) == GraphSelectionMode.Toggle);
});
Test("plain click retains an already selected group for dragging", () => {
    HashSet<string> s = ["a", "b"];
    Check(GraphSelectionModifiers.Click(s,"a",GraphSelectionMode.Replace) == new GraphSelectionChange(false,true));
    Check(s.SetEquals(["a","b"]));
});
Test("plain click replaces a different selection", () => {
    HashSet<string> s = ["a", "b"];
    Check(GraphSelectionModifiers.Click(s,"c",GraphSelectionMode.Replace) == new GraphSelectionChange(true,true));
    Check(s.SetEquals(["c"]));
});
Test("Shift adds and does not toggle selected nodes off", () => {
    HashSet<string> s = ["a"];
    Check(GraphSelectionModifiers.Click(s,"a",GraphSelectionMode.Add) == new GraphSelectionChange(false,true));
    Check(GraphSelectionModifiers.Click(s,"b",GraphSelectionMode.Add) == new GraphSelectionChange(true,true));
    Check(s.SetEquals(["a","b"]));
});
Test("Control removes selected nodes and cannot start a node drag", () => {
    HashSet<string> s = ["a","b"];
    Check(GraphSelectionModifiers.Click(s,"a",GraphSelectionMode.Remove) == new GraphSelectionChange(true,false));
    Check(GraphSelectionModifiers.Click(s,"c",GraphSelectionMode.Remove) == new GraphSelectionChange(false,false));
    Check(s.SetEquals(["b"]));
});
Test("Control+Shift toggles once and drag follows final membership", () => {
    HashSet<string> s = ["a"];
    Check(GraphSelectionModifiers.Click(s,"a",GraphSelectionMode.Toggle) == new GraphSelectionChange(true,false));
    Check(GraphSelectionModifiers.Click(s,"b",GraphSelectionMode.Toggle) == new GraphSelectionChange(true,true));
    Check(s.SetEquals(["b"]));
});
Test("marquee add remove replace and toggle preserve set semantics", () => {
    HashSet<string> s = ["a","b"];
    Check(GraphSelectionModifiers.Rectangle(s,["c"],GraphSelectionMode.Add) && s.SetEquals(["a","b","c"]));
    Check(GraphSelectionModifiers.Rectangle(s,["a","missing"],GraphSelectionMode.Remove) && s.SetEquals(["b","c"]));
    Check(GraphSelectionModifiers.Rectangle(s,["b","b","d"],GraphSelectionMode.Toggle) && s.SetEquals(["c","d"]));
    Check(GraphSelectionModifiers.Rectangle(s,["c"],GraphSelectionMode.Replace) && s.SetEquals(["c"]));
    Check(!GraphSelectionModifiers.Rectangle(s,["c","c"],GraphSelectionMode.Replace));
});
Test("toggle deduplicates with the host selection comparer", () => {
    var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
    Check(GraphSelectionModifiers.Rectangle(s,["a","A","b","B"],GraphSelectionMode.Toggle));
    Check(s.SetEquals(["b"]));
});
Test("invalid hits and modes reject atomically", () => {
    HashSet<string> s = ["a"];
    Throws<ArgumentException>(() => GraphSelectionModifiers.Rectangle(s,["b", ""],GraphSelectionMode.Replace));
    Throws<ArgumentException>(() => GraphSelectionModifiers.Rectangle(s,default,GraphSelectionMode.Remove));
    Throws<ArgumentOutOfRangeException>(() => GraphSelectionModifiers.Click(s,"a",(GraphSelectionMode)99));
    Check(s.SetEquals(["a"]));
});
Test("empty rectangles preserve modified selections but replace clears", () => {
    HashSet<string> s = ["a"];
    foreach(var mode in new[]{GraphSelectionMode.Add,GraphSelectionMode.Remove,GraphSelectionMode.Toggle})
        Check(!GraphSelectionModifiers.Rectangle(s,[],mode) && s.SetEquals(["a"]));
    Check(GraphSelectionModifiers.Rectangle(s,[],GraphSelectionMode.Replace) && s.Count==0);
});
Test("eight guide directions and unconstrained displacement", () => {
    foreach(var signX in new[]{-1,1}) foreach(var signY in new[]{-1,1}) {
        Check(GraphDragPositions.Constrain(new(100*signX,10*signY),true)==new CanvasPoint(100*signX,0));
        Check(GraphDragPositions.Constrain(new(10*signX,100*signY),true)==new CanvasPoint(0,100*signY));
        Check(GraphDragPositions.Constrain(new(80*signX,60*signY),true)==new CanvasPoint(70*signX,70*signY));
    }
    Check(GraphDragPositions.Constrain(new(17,29),false)==new CanvasPoint(17,29));
    Check(GraphDragPositions.Constrain(new(0,0),true)==new CanvasPoint(0,0));
});
Test("guide projection is nearest of eight rays for 1000 random displacements", () => {
    var random=new Random(27183);
    for(var i=0;i<1000;i++) {
        var p=new CanvasPoint(random.NextDouble()*2000-1000,random.NextDouble()*2000-1000);
        var q=GraphDragPositions.Constrain(p,true);var error=(p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y);
        for(var j=0;j<8;j++) {
            var x=Math.Cos(j*Math.PI/4);var y=Math.Sin(j*Math.PI/4);var length=Math.Max(0,p.X*x+p.Y*y);
            var dx=p.X-length*x;var dy=p.Y-length*y;
            Check(error<=dx*dx+dy*dy+1e-7);
        }
    }
});
Test("drag positions reuse a read-only buffer without accumulating error", () => {
    var d=new GraphDragPositions([new("a",10,20),new("b",40,50)]);
    var first=d.Update(new(5,9),false);var next=d.Update(new(15,25),false);
    Check(ReferenceEquals(first,next) && next[0]==new NodePosition("a",25,45) && next[1]==new NodePosition("b",55,75));
    Check(d.Update(new(0,0),false)[0]==new NodePosition("a",10,20));
    Throws<NotSupportedException>(() => ((IList<NodePosition>)next)[0]=new("a",0,0));
});
Test("drag origins are detached and oversized input is bounded", () => {
    NodePosition[] input = [new("a",10,20)];
    var d = new GraphDragPositions(input); input[0] = new("a",100,200);
    Check(d.Update(new(0,0),false)[0] == new NodePosition("a",10,20));
    Throws<ArgumentException>(() => new GraphDragPositions(Enumerable.Range(0,10001).Select(i => new NodePosition(i.ToString(),0,0))));
});
Test("invalid origins and overflow do not corrupt existing buffer", () => {
    Throws<ArgumentException>(() => new GraphDragPositions([new("a",0,0),new("a",1,1)]));
    Throws<ArgumentException>(() => new GraphDragPositions([new("a",double.NaN,0)]));
    var d=new GraphDragPositions([new("a",double.MaxValue,0)]);var p=d.Update(new(0,0),false);
    Throws<ArgumentException>(() => d.Update(new(double.MaxValue,1),false));
    Check(p[0]==new NodePosition("a",double.MaxValue,0));
    Throws<ArgumentException>(() => GraphDragPositions.Constrain(new(0,double.PositiveInfinity),true));
    Check(double.IsFinite(GraphDragPositions.Constrain(new(double.MaxValue,double.MaxValue),true).X));
});
Test("10000 warm drag-buffer updates and selected clicks allocate no managed bytes", () => {
    var d=new GraphDragPositions([new("a",10,20),new("b",40,50)]);HashSet<string> s=["a"];
    for(var i=0;i<100;i++){d.Update(new(i,i/2d),true);GraphSelectionModifiers.Click(s,"a",GraphSelectionMode.Add);}
    var before=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++){d.Update(new(i,i/2d),true);GraphSelectionModifiers.Click(s,"a",GraphSelectionMode.Add);}
    var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0);Console.WriteLine($"METRIC warm-drag-selection-10000={bytes} B");
});

GraphPortHit? ReferenceHit(GraphDrawing drawing, CanvasPoint point, double tolerance)
{
    var distance=tolerance*tolerance;GraphPortHit? best=null;
    for(var n=drawing.Nodes.Length-1;n>=0;n--)
    {
        var node=drawing.Nodes[n];
        for(var side=0;side<2;side++)
        {
            var ports=side==1?node.Definition.Outputs:node.Definition.Inputs;
            for(var i=0;i<ports.Length;i++)
            {
                var port=node.Port(side==1,i);var dx=port.X-point.X;var dy=port.Y-point.Y;var squared=dx*dx+dy*dy;
                if(!double.IsFinite(squared)||squared>distance||squared==distance&&best is not null)continue;
                distance=squared;best=new(node.Node.Id,ports[i].Name,side==1);
            }
        }
    }
    return best;
}
PresentedNode Presented(string id, int inputs, int outputs, CanvasBox bounds, bool slider=false)
{
    var component=slider?"Number":"Custom";
    var definition=new ComponentDefinition(component,"Test","Hit testing fixture",
        Enumerable.Range(0,inputs).Select(i=>new PortDefinition("I"+i)).ToImmutableArray(),
        Enumerable.Range(0,outputs).Select(i=>new PortDefinition("O"+i)).ToImmutableArray(),
        _=>throw new NotSupportedException("Presentation fixture is never solved."));
    return new(new GraphNode(id,component,bounds.X,bounds.Y,ImmutableDictionary<string,object?>.Empty),definition,bounds);
}
Test("anchored zoom retains the world point under the pointer",()=>{
    var view=new GraphCanvasView(.8f,20,-15);var anchor=new CanvasPoint(350,120);
    var next=GraphCanvasNavigation.ZoomAt(view,anchor,1.6);
    Check(Math.Abs((anchor.X-next.PanX)/next.Zoom-(anchor.X-view.PanX)/view.Zoom)<1e-4);
    Check(Math.Abs((anchor.Y-next.PanY)/next.Zoom-(anchor.Y-view.PanY)/view.Zoom)<1e-4);
});
Test("vertical drag uses its origin and horizontal movement is a no-op",()=>{
    var view=new GraphCanvasView(.8f,20,30);var anchor=new CanvasPoint(200,200);
    Check(GraphCanvasNavigation.DragZoom(view,anchor,new(600,200))==view);
    var upward=GraphCanvasNavigation.DragZoom(view,anchor,new(200,150));
    Check(upward.Zoom>view.Zoom);
    var downward=GraphCanvasNavigation.DragZoom(view,anchor,new(200,250));
    Check(downward.Zoom<view.Zoom);
    for(var i=0;i<100;i++)GraphCanvasNavigation.DragZoom(view,anchor,new(200,200-i));
    Check(GraphCanvasNavigation.DragZoom(view,anchor,new(200,150))==upward);
    Check(GraphCanvasNavigation.DragZoom(view,anchor,anchor)==view);
});
Test("zoom clamps before exponentiation and clamped no-ops retain exact translations",()=>{
    var view=new GraphCanvasView(.8f,20,30);var anchor=new CanvasPoint(200,200);
    var large=GraphCanvasNavigation.ZoomBy(view,anchor,double.MaxValue);
    var small=GraphCanvasNavigation.ZoomBy(view,anchor,-double.MaxValue);
    Check(large.Zoom==GraphCanvasNavigation.MaximumZoom&&small.Zoom==GraphCanvasNavigation.MinimumZoom);
    Check(GraphCanvasNavigation.ZoomBy(large,anchor,100)==large);
    Check(GraphCanvasNavigation.ZoomBy(small,anchor,-100)==small);
    Check(GraphCanvasNavigation.ZoomAt(view,anchor,double.MaxValue).Zoom==GraphCanvasNavigation.MaximumZoom);
    Check(GraphCanvasNavigation.ZoomAt(view,anchor,-1).Zoom==GraphCanvasNavigation.MinimumZoom);
});
Test("canvas pan is origin-based and preserves zoom",()=>{
    var view=new GraphCanvasView(.8f,20,30);
    Check(GraphCanvasNavigation.PanBy(view,new(15,-10))==new GraphCanvasView(.8f,35,20));
    Check(GraphCanvasNavigation.PanBy(view,default)==view);
    Check(GraphCanvasNavigation.PanBy(view,new(15,-10))==GraphCanvasNavigation.PanBy(view,new(15,-10)));
});
Test("navigation rejects nonfinite state coordinates and unrepresentable translations",()=>{
    var view=new GraphCanvasView(.8f,20,30);
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.ZoomAt(default,default,1));
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.ZoomAt(view,default,double.NaN));
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.ZoomBy(view,default,double.PositiveInfinity));
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.PanBy(view,new(double.MaxValue,0)));
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.ZoomAt(view,new(double.MaxValue,0),2));
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.DragZoom(view,default,new(0,double.NaN)));
    Throws<ArgumentOutOfRangeException>(()=>GraphCanvasNavigation.PanBy(new(.8f,float.NaN,0),default));
    Check(view==new GraphCanvasView(.8f,20,30));
});
Test("1000 randomized anchored zooms preserve bounded native precision",()=>{
    var random=new Random(88213);
    for(var i=0;i<1000;i++)
    {
        var view=new GraphCanvasView((float)(.15+random.NextDouble()*3.35),random.Next(-1000,1001),random.Next(-1000,1001));
        var point=new CanvasPoint(random.Next(0,1601),random.Next(0,1101));
        var next=GraphCanvasNavigation.ZoomAt(view,point,.15+random.NextDouble()*3.35);
        var x=(point.X-view.PanX)/view.Zoom;var y=(point.Y-view.PanY)/view.Zoom;
        Check(Math.Abs(x*next.Zoom+next.PanX-point.X)<.003&&Math.Abs(y*next.Zoom+next.PanY-point.Y)<.003);
    }
});
Test("10000 warmed navigation calculations allocate no managed bytes",()=>{
    var view=new GraphCanvasView(.8f,20,30);var anchor=new CanvasPoint(200,200);
    for(var i=0;i<100;i++)GraphCanvasNavigation.DragZoom(view,anchor,new(200,150));
    var before=GC.GetAllocatedBytesForCurrentThread();
    var last=view;
    for(var i=0;i<10000;i++)last=GraphCanvasNavigation.DragZoom(view,anchor,new(200,150));
    var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
    Check(allocated==0&&last.Zoom>view.Zoom);
    Console.WriteLine($"METRIC navigation-10000={allocated} B");
});
Test("nearest port preserves exact radial boundaries row and side ties",()=>{
    var n=Presented("a",12,12,new(10,20,0,280));var drawing=new GraphDrawing([n],[]);
    foreach(var point in new[]{new CanvasPoint(10,39),new(10,48.5),new(5,39),new(6,42),new(10,248)})
        foreach(var tolerance in new[]{0,5,9.5,60,10000})
            Check(GraphHitTesting.PortAt(drawing,point,tolerance)==ReferenceHit(drawing,point,tolerance));
    Check(GraphHitTesting.PortAt(drawing,new(10,48.5),9.5)==new GraphPortHit("a","I0",false));
    Check(GraphHitTesting.PortAt(drawing,new(6,42),5)==new GraphPortHit("a","I0",false));
});
Test("overlapping nodes keep frontmost identity and slider rows keep first identity",()=>{
    var behind=Presented("behind",64,64,new(0,0,180,1400));
    var front=Presented("front",64,64,new(0,0,180,38),true);
    var drawing=new GraphDrawing([behind,front],[]);
    Check(GraphHitTesting.PortAt(drawing,new(0,19),0)==new GraphPortHit("front","I0",false));
    Check(GraphHitTesting.PortAt(drawing,new(180,19),9)==new GraphPortHit("front","O0",true));
    Check(GraphHitTesting.PortAt(drawing,new(0,38),9)==new GraphPortHit("behind","I1",false));
});
Test("binary64 collapsed port-row plateaus retain the earliest row",()=>{
    foreach(var y in new[]{1e18,-1e18,1e20,-1e20,double.MaxValue,-double.MaxValue})
    {
        var n=Presented("large",1024,0,new(0,y,180,20000));var drawing=new GraphDrawing([n],[]);
        foreach(var offset in new[]{-8192d,-4096d,-1024d,0d,512d,1024d,4096d,8192d,20000d})
        {
            var point=new CanvasPoint(0,y+offset);
            Check(GraphHitTesting.PortAt(drawing,point,10000)==ReferenceHit(drawing,point,10000));
        }
    }
});
Test("10000 randomized nearest-port queries match the exhaustive reference",()=>{
    var random=new Random(40921);var nodes=ImmutableArray.CreateBuilder<PresentedNode>();
    for(var i=0;i<45;i++)nodes.Add(Presented("n"+i,random.Next(0,129),random.Next(0,129),
        new(random.Next(-1000,1001),random.Next(-1000,1001),random.Next(-180,181),2600),i%11==0));
    var drawing=new GraphDrawing(nodes.ToImmutable(),[]);
    for(var i=0;i<10000;i++)
    {
        var point=new CanvasPoint(random.NextDouble()*2500-1250,random.NextDouble()*4000-1250);
        var tolerance=i%3==0?60:random.NextDouble()*20;
        var actual=GraphHitTesting.PortAt(drawing,point,tolerance);var expected=ReferenceHit(drawing,point,tolerance);
        Check(actual==expected);
    }
});
Test("malformed presentation ports do not fabricate NaN or infinite hits",()=>{
    var valid=Presented("valid",1,0,new(0,0,156,56));
    foreach(var bounds in new[]{new CanvasBox(double.NaN,0,156,56),new(0,double.PositiveInfinity,156,56),new(double.PositiveInfinity,0,156,56)})
    {
        var drawing=new GraphDrawing([valid,Presented("bad",2,2,bounds)],[]);
        Check(GraphHitTesting.PortAt(drawing,new(0,19),0)==new GraphPortHit("valid","I0",false));
        Check(GraphHitTesting.PortAt(drawing,new(500,500),5)is null);
    }
});
Test("10000 off-column hit-test misses allocate no managed bytes",()=>{
    var drawing=new GraphDrawing([Presented("a",1024,1024,new(0,0,180,20000))],[]);
    for(var i=0;i<100;i++)GraphHitTesting.PortAt(drawing,new(-100,100),9);
    var before=GC.GetAllocatedBytesForCurrentThread();GraphPortHit? hit=null;
    for(var i=0;i<10000;i++)hit=GraphHitTesting.PortAt(drawing,new(-100,100),9);
    var allocated=GC.GetAllocatedBytesForCurrentThread()-before;Check(hit is null&&allocated==0);
    Console.WriteLine($"METRIC port-miss-10000={allocated} B");
});

var failed=0;
foreach(var(name,run) in tests) try { run(); Console.WriteLine("PASS "+name); } catch(Exception e) { failed++; Console.WriteLine("FAIL "+name+"\n"+e); }
Console.WriteLine(JsonSerializer.Serialize(new{suite="pointer-selection",total=tests.Count,passed=tests.Count-failed,failed}));
return failed==0?0:1;
