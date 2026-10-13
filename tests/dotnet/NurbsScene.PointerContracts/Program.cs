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
var failed=0;
foreach(var(name,run) in tests) try { run(); Console.WriteLine("PASS "+name); } catch(Exception e) { failed++; Console.WriteLine("FAIL "+name+"\n"+e); }
Console.WriteLine(JsonSerializer.Serialize(new{suite="pointer-selection",total=tests.Count,passed=tests.Count-failed,failed}));
return failed==0?0:1;
