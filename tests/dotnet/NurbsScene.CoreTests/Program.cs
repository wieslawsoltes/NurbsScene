using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Model;
using NurbsScene.Parametric;

// Dependency-free test executable. Authored, but NOT executed in the delivery environment.
var evaluator = new ManagedNurbsEvaluator();
var factory = new GeometryFactory(evaluator);
var tests = new List<(string Name, Func<Task> Run)>();
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Near(double actual, double expected, double tolerance = 1e-10) => Check(double.IsFinite(actual) && Math.Abs(actual-expected)<=tolerance*Math.Max(1,Math.Abs(expected)), $"Expected {expected:G17}, got {actual:G17}");
void Point(Vec3 a, Vec3 b, double tolerance = 1e-10) { Near(a.X,b.X,tolerance); Near(a.Y,b.Y,tolerance); Near(a.Z,b.Z,tolerance); }
void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
void Test(string name, Action run) => tests.Add((name,()=>{run();return Task.CompletedTask;}));
void AsyncTest(string name, Func<Task> run) => tests.Add((name,run));

Test("analytic circle: position, tangent, curvature",()=>{
    var circle=factory.Circle(new(3,-2,4),7);
    for(var i=0;i<=100;i++){var t=i/100d;var d=circle.Derivatives(t);Near((d[0]-new Vec3(3,-2,4)).Length,7);Near(Vec3.Dot(d[0]-new Vec3(3,-2,4),d[1]),0,1e-8);Near(circle.CurvatureAt(t),1d/7);}
});
Test("native-independent SciPy curve oracle: degrees 1..9",()=>{
    using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"curves-scipy.json")));
    foreach(var item in fixture.RootElement.GetProperty("curves").EnumerateArray()){
        var knots=item.GetProperty("knots").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
        var points=item.GetProperty("controls").EnumerateArray().Select(x=>{var a=x.EnumerateArray().Select(v=>v.GetDouble()).ToArray();return new HPoint(a[0],a[1],a[2],a[3]);}).ToArray();
        var curve=new NurbsCurve(evaluator,item.GetProperty("degree").GetInt32(),knots,points);
        foreach(var sample in item.GetProperty("samples").EnumerateArray()){
            var expected=sample.GetProperty("derivatives").EnumerateArray().ToArray();var actual=curve.Derivatives(sample.GetProperty("t").GetDouble(),expected.Length-1);
            for(var k=0;k<actual.Length;k++){var a=expected[k].EnumerateArray().Select(v=>v.GetDouble()).ToArray();Point(actual[k],new(a[0],a[1],a[2]),2e-7);}
        }
    }
});
Test("common extreme rational weight scaling",()=>{
    var curve=factory.Circle(Vec3.Zero,3);var expected=curve.Derivatives(.372,4);
    foreach(var scale in new[]{1e-290,1e-200,1e200,1e290}){
        var scaled=new NurbsCurve(evaluator,curve.Degree,curve.Knots.ToArray(),curve.ControlPoints.ToArray().Select(p=>p*scale));
        var actual=scaled.Derivatives(.372,4);for(var k=0;k<actual.Length;k++)Point(actual[k],expected[k],1e-8);
    }
});
Test("knot insertion, split, trim and elevation preserve a rational curve",()=>{
    var curve=factory.Circle(Vec3.Zero,4);var inserted=curve.InsertKnot(.371,2);var elevated=curve.ElevateDegree(2);var (left,right)=curve.Split(.371);var trimmed=curve.Trim(new(.16,.83));
    for(var i=0;i<=100;i++){var t=i/100d;Point(curve.PointAt(t),inserted.PointAt(t));Point(curve.PointAt(t),elevated.PointAt(t));Point(curve.PointAt(t),t<=.371?left.PointAt(t):right.PointAt(t));if(trimmed.Domain.Contains(t))Point(curve.PointAt(t),trimmed.PointAt(t));}
});
Test("nonclamped active domain and analytic derivatives",()=>{
    var c=new NurbsCurve(evaluator,3,Enumerable.Range(0,11).Select(i=>(double)i),Enumerable.Range(0,7).Select(i=>HPoint.FromPoint(new(i,i*i,0))));
    Near(c.Domain.Min,3);Near(c.Domain.Max,7);Near(c.PointAt(3).X,1);Near(c.Derivatives(4.5,1)[1].X,1);Throws<ArgumentOutOfRangeException>(()=>c.PointAt(2));
});
Test("tensor surface mixed derivatives and transpose",()=>{
    var s=factory.FromCorners(new(0,0,0),new(2,0,0),new(2,3,1),new(0,3,0));var d=s.Derivatives(.3,.7,2,2);
    Point(d[0,0],new(.6,2.1,.21));Point(d[1,0],new(2,0,.7));Point(d[0,1],new(0,3,.3));Point(d[1,1],new(0,0,1));Point(d[2,0],Vec3.Zero);Point(s.Transpose().PointAt(.7,.3),d[0,0]);
});
Test("exact extrusion, revolution and isocurve",()=>{
    var c=factory.Circle(Vec3.Zero,2);var s=factory.Extrude(c,new(0,0,5));Point(s.PointAt(.13,.7),c.PointAt(.13)+new Vec3(0,0,3.5));
    var r=factory.Revolve(factory.Line(new(2,0,0),new(2,0,5)),Vec3.Zero,Vec3.UnitZ);var p=r.PointAt(.6,.375);Near(Math.Sqrt(p.X*p.X+p.Y*p.Y),2);Near(p.Z,3);
    var iso=factory.IsoCurve(s,.23);for(var i=0;i<=10;i++)Point(iso.PointAt(i/10d),s.PointAt(.23,i/10d));
});
Test("interpolation and compatible loft pass through their inputs",()=>{
    Vec3[] points=[new(0,0,0),new(1,2,0),new(4,1,2),new(5,0,0)];var c=factory.Interpolate(points);Point(c.PointAt(c.Domain.Min),points[0]);Point(c.PointAt(c.Domain.Max),points[^1]);
    var lower=factory.Circle(Vec3.Zero,3);var upper=lower.Transform(Transform3.Translation(new(0,0,5)));var loft=factory.Loft([lower,upper]);
    for(var i=0;i<=10;i++){Point(loft.PointAt(i/10d,loft.DomainV.Min),lower.PointAt(i/10d));Point(loft.PointAt(i/10d,loft.DomainV.Max),upper.PointAt(i/10d));}
});
Test("invalid knots, weights and transforms are rejected",()=>{
    Throws<ArgumentException>(()=>new NurbsCurve(evaluator,2,[0,0,0,1,1,1],[new(0,0,0,1),new(1,1,0,0),new(2,0,0,1)]));
    Throws<ArgumentException>(()=>new NurbsCurve(evaluator,1,[0,0,1,0],[new(0,0,0,1),new(1,1,0,1)]));
    Throws<ArgumentException>(()=>new Transform3([double.NaN]));Throws<ArgumentOutOfRangeException>(()=>factory.Circle(Vec3.Zero,1).Derivatives(.5,33));
});
Test("data paths, graft, flatten, simplify and empty branches",()=>{
    var a=new DataPath([2,10]);var b=new DataPath([2,2]);Check(a.CompareTo(b)>0);var t=new DataTree([new(a,[1d,2d]),new(b,[])]);
    Check(t.ItemCount==2);Check(t.Graft().Branches.Length==3);Check(t.Partition(1).Branches.Length==3);Check(t.Flatten().ItemCount==2);Check(t.Simplify().Branches[0].Path.Equals(new DataPath([2])));
});
AsyncTest("incremental graph cache and downstream invalidation",async()=>{
    var g=new Graph(StandardComponents.Create(factory));var a=g.Add("Number",parameters:new Dictionary<string,object?>{{"value",3d}});var b=g.Add("Number",parameters:new Dictionary<string,object?>{{"value",4d}});var sum=g.Add("Add");
    g.Connect(new(a.Id,"N"),new(sum.Id,"A"));g.Connect(new(b.Id,"N"),new(sum.Id,"B"));var first=await g.SolveAsync();Check(first.ErrorCount==0);Near((double)first.Nodes[sum.Id].Outputs["R"].AllItems.Single()!,7);
    var second=await g.SolveAsync();Check(second.Evaluated==0&&second.Cached==3);
    g.Update(a with{Parameters=a.Parameters.SetItem("value",8d)});var third=await g.SolveAsync();Check(third.Evaluated==2&&third.Cached==1);Near((double)third.Nodes[sum.Id].Outputs["R"].AllItems.Single()!,12);
});
Test("typed connections reject invalid types, cycles and duplicate IDs atomically",()=>{
    var g=new Graph(StandardComponents.Create(factory));var a=g.Add("Add");var b=g.Add("Add");var text=g.Add("Text");g.Connect(new(a.Id,"R"),new(b.Id,"A"),id:"one");
    Throws<InvalidOperationException>(()=>g.Connect(new(b.Id,"R"),new(a.Id,"A")));Throws<ArgumentException>(()=>g.Connect(new(text.Id,"T"),new(a.Id,"A")));
    Throws<ArgumentException>(()=>g.Connect(new(a.Id,"R"),new(b.Id,"B"),replace:true,id:"one"));Check(g.Wires.Count==1);
});
Test("graph parameters detach caller-owned nested values",()=>{
    var list=new List<object?>{1d,2d};var g=new Graph(StandardComponents.Create(factory));var node=g.Add("Number",parameters:new Dictionary<string,object?>{{"value",2d},{"nested",list}});list[0]=99d;
    Check((double)((ImmutableArray<object?>)node.Parameters["nested"]!)[0]! == 1d);Throws<ArgumentException>(()=>g.Add("Number",parameters:new Dictionary<string,object?>{{"value",double.NaN}}));
});
AsyncTest("cancelled solve does not publish a successful snapshot",async()=>{
    var g=new Graph(StandardComponents.Create(factory));g.Add("Number");using var cancel=new CancellationTokenSource();cancel.Cancel();
    try{await g.SolveAsync(cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}Check(g.LastResult is null);
});
Test("graph edit history and rollback",()=>{
    var session=new GraphSession(new Graph(StandardComponents.Create(factory)));session.Edit("Add",g=>g.Add("Number"));Check(session.Graph.Nodes.Count==1);Check(session.Undo());Check(session.Graph.Nodes.Count==0);Check(session.Redo());Check(session.Graph.Nodes.Count==1);
    Throws<NotSupportedException>(()=>session.Edit("Fail",g=>{g.Add("Number");g.Add("MissingComponent");}));Check(session.Graph.Nodes.Count==1);
});
Test("document transactions, undo, redo and metadata",()=>{
    var doc=new SceneDocument();doc.Transaction("Add",()=>{doc.Name="Exact";doc.Add(factory.Circle(Vec3.Zero,1));});Check(doc.Objects.Count==1&&doc.Name=="Exact");Check(doc.Undo());Check(doc.Objects.Count==0&&doc.Name=="Untitled");Check(doc.Redo());Check(doc.Objects.Count==1&&doc.Name=="Exact");
    Throws<InvalidOperationException>(()=>doc.Transaction("Rollback",()=>{doc.Add(factory.Circle(Vec3.Zero,2));throw new InvalidOperationException("expected");}));Check(doc.Objects.Count==1);
});
Test("locking removes selection and rejects edits",()=>{
    var doc=new SceneDocument();var layer=doc.AddLayer("Locked later",ColorRgba.Default);var obj=doc.Add(factory.Circle(Vec3.Zero,1),layerId:layer.Id);doc.Select([obj.Id]);doc.UpdateLayer(layer with{Locked=true});Check(doc.Selection.Count==0);
    Throws<InvalidOperationException>(()=>doc.Delete([obj.Id]));Throws<InvalidOperationException>(()=>doc.Update(obj with{Name="changed"}));Check(doc.Objects.Count==1);
});
Test("async document transactions are rejected before invocation",()=>{
    var doc=new SceneDocument();bool invoked=false;Throws<ArgumentException>(()=>doc.Transaction("Async",async()=>{invoked=true;await Task.Yield();doc.Add(factory.Circle(Vec3.Zero,1));}));Check(!invoked&&doc.Objects.Count==0);
});

var failures=0;
foreach(var (name,run) in tests){try{await run();Console.WriteLine($"PASS {name}");}catch(Exception error){failures++;Console.Error.WriteLine($"FAIL {name}\n{error}");}}
Console.WriteLine(JsonSerializer.Serialize(new{suite="managed-core",total=tests.Count,passed=tests.Count-failures,failed=failures}));
return failures==0?0:1;
