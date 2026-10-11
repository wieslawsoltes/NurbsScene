using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Workbench;

var factory=new GeometryFactory(new ManagedNurbsEvaluator());var tests=new List<(string,Func<Task>)>();
void Test(string name,Action action)=>tests.Add((name,()=>{action();return Task.CompletedTask;}));
void AsyncTest(string name,Func<Task> action)=>tests.Add((name,action));
void Check(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
void Near(double actual,double expected,double tolerance=1e-7)=>Check(double.IsFinite(actual)&&Math.Abs(actual-expected)<=tolerance,$"{actual:G17} != {expected:G17}");
void Point(Vec3 a,Vec3 b,double tolerance=1e-7)=>Check((a-b).Length<=tolerance,$"{a} != {b}");
void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
CurveClosestPointResult Query(NurbsCurve c,Vec3 p,double tolerance=1e-7)
{
    var r=c.ClosestPoint(p,new(tolerance));Check(r.Converged,$"Gap {r.DistanceGap}; subdivisions {r.Subdivisions}");
    Check(r.DistanceGap<=tolerance);Point(r.Point,c.PointAt(r.Parameter));Near(r.Distance,(r.Point-p).Length);return r;
}
Test("line projection, endpoint minima and nonunit domains",()=>{
    var c=factory.Line(new(1,2,3),new(11,2,3)).Reparameterize(new(-7,13));var r=Query(c,new(4,6,3));Near(r.Parameter,-1);Point(r.Point,new(4,2,3));Near(r.Distance,4);
    Near(Query(c,new(-10,2,3)).Parameter,-7);Near(Query(c,new(99,2,3)).Parameter,13);
});
Test("rational circle global closest point and nonunique center query",()=>{
    var c=factory.Circle(new(1,2,3),4);var r=Query(c,new(7,10,8));Point(r.Point,new(3.4,5.2,3),1e-5);Near(r.Distance,Math.Sqrt(61));
    var center=Query(c,new(1,2,3),1e-6);Near(center.Distance,4);Check(center.Parameter>=0&&center.Parameter<=1);
});
Test("stationary derivatives and zero-length curves do not require Newton convergence",()=>{
    var c=new NurbsCurve(factory.Evaluator,3,[0,0,0,0,1,1,1,1],[HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.UnitX)]);
    Point(Query(c,new(.001,1,0)).Point,new(.001,0,0),5e-4);
    var zero=Query(factory.Line(new(2,3,4),new(2,3,4)),new(7,3,4));Near(zero.Distance,5);Near(zero.Parameter,0);
});
Test("polyline kinks, reversal, knot insertion and elevated curves",()=>{
    var c=factory.Polyline([Vec3.Zero,new(3,0,0),new(3,4,0)]);Point(Query(c,new(5,-2,0)).Point,new(3,0,0));
    var circle=factory.Circle(Vec3.Zero,3);var q=new Vec3(5,7,2);var r=Query(circle,q);
    foreach(var edited in new[]{circle.Reverse(),circle.InsertKnot(.37,2),circle.ElevateDegree(2),circle.Reparameterize(new(11,73))})Near(Query(edited,q).Distance,r.Distance);
});
Test("query cache is reused by immutable identity and supports concurrency",()=>{
    var c=factory.Circle(Vec3.Zero,3);Check(ReferenceEquals(c.CreateQueryIndex(),c.CreateQueryIndex()));
    Check(!ReferenceEquals(c.CreateQueryIndex(),c.Reverse().CreateQueryIndex()));
    Parallel.For(0,30,i=>{var angle=i*.317;var r=Query(c,new(5*Math.Cos(angle),5*Math.Sin(angle),2));Near(r.Distance,Math.Sqrt(8));});
});
Test("query budgets and unsupported continuity are explicit",()=>{
    var c=factory.Circle(Vec3.Zero,3);var r=c.ClosestPoint(new(8,2,1),new(MaximumSubdivisions:0));Check(!r.Converged&&r.DistanceGap>1e-7);
    Throws<ArgumentException>(()=>c.ClosestPoint(new(double.NaN,0,0)));
    Throws<ArgumentException>(()=>c.ClosestPoint(Vec3.Zero,new(DistanceTolerance:0)));
    Throws<OperationCanceledException>(()=>c.ClosestPoint(Vec3.Zero,cancellationToken:new(true)));
    var disconnected=new NurbsCurve(factory.Evaluator,1,[0,0,.5,.5,1,1],[HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.UnitX),HPoint.FromPoint(new(50,0,0)),HPoint.FromPoint(new(51,0,0))]);
    Throws<NotSupportedException>(()=>disconnected.ClosestPoint(Vec3.Zero));
});
Test("large translated curves preserve world-coordinate query precision",()=>{
    var c=factory.Circle(new(1e5,-2e5,3e5),5);var r=Query(c,new(1e5+10,-2e5,3e5+3),1e-6);Near(r.Distance,Math.Sqrt(34),1e-6);
});
Test("independent SciPy references for 96 rational-curve queries",()=>{
    using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"queries-scipy.json")));
    foreach(var item in source.RootElement.GetProperty("cases").EnumerateArray()){
        var controls=item.GetProperty("controls").EnumerateArray().Select(a=>{var v=a.EnumerateArray().Select(x=>x.GetDouble()).ToArray();return new HPoint(v[0],v[1],v[2],v[3]);});
        var c=new NurbsCurve(factory.Evaluator,item.GetProperty("degree").GetInt32(),item.GetProperty("knots").EnumerateArray().Select(x=>x.GetDouble()),controls);
        foreach(var query in item.GetProperty("queries").EnumerateArray()){
            var v=query.GetProperty("point").EnumerateArray().Select(x=>x.GetDouble()).ToArray();var r=Query(c,new(v[0],v[1],v[2]));var expected=query.GetProperty("distance").GetDouble();
            Near(r.Distance,expected,2e-7);Check(r.LowerDistance<=expected+2e-8,"Hull estimate excluded independent minimum.");
        }
    }
});
Test("rational control editing retains weight and original geometry",()=>{
    var c=factory.Circle(Vec3.Zero,4);var before=c.ControlPoints[1];var edited=c.WithControlPoint(1,new(2,3,4));
    Point(edited.ControlPoints[1].Position,new(2,3,4));Near(edited.ControlPoints[1].W,before.W);Check(c.ControlPoints[1]==before);
    Near(c.WithControlPoint(1,new(2,3,4),2).ControlPoints[1].W,2);
    Throws<ArgumentOutOfRangeException>(()=>c.WithControlPoint(c.Count,Vec3.Zero));Throws<ArgumentException>(()=>c.WithControlPoint(0,Vec3.Zero,0));
});
Test("affine plane projection and reflection preserve exact rational representations",()=>{
    var plane=new Plane3(new(4,5,6),new(1,2,3));var curve=factory.Circle(new(7,8,9),3);var projected=curve.Transform(plane.Projection);var mirrored=curve.Transform(plane.Reflection);
    for(var i=0;i<=20;i++){
        var t=i/20d;var p=curve.PointAt(t);Point(projected.PointAt(t),plane.Project(p));Near(plane.SignedDistance(projected.PointAt(t)),0);
        Near(plane.SignedDistance(mirrored.PointAt(t)),-plane.SignedDistance(p));Point(mirrored.Transform(plane.Reflection).PointAt(t),p);
    }
    var s=factory.Saddle().Transform(plane.Projection);Near(plane.SignedDistance(s.PointAt(.31,.79)),0);
    Throws<ArgumentException>(()=>new Plane3(Vec3.Zero,Vec3.Zero));
});
foreach(var name in new[]{"CurveClosestPoint","CurveControlPoint","CurveEndPoints","ProjectToPlane","MirrorInPlane"})AsyncTest("executable geometry component: "+name,async()=>{
    var registry=StandardComponents.Create(factory);var graph=new Graph(registry);var values=new Dictionary<string,object?>{{"input:C",factory.Circle(Vec3.Zero,3)},{"input:G",factory.Saddle()},{"input:P",new Vec3(5,0,0)}};
    var node=graph.Add(name,parameters:values);var r=await graph.SolveAsync();Check(r.ErrorCount==0,string.Join(';',r.Nodes[node.Id].Messages));Check(r.Nodes[node.Id].Outputs.Values.All(t=>t.ItemCount>0));
});
Test("query commands are read-only; editing and plane transforms undo atomically",()=>{
    using var w=new WorkspaceController();var curve=factory.Circle(Vec3.Zero,3);var item=w.Document.Add(curve,"C");w.Document.Select([item.Id]);var revision=w.Document.Revision;var messages=new List<string>();w.Message+=messages.Add;
    w.Execute("ClosestPoint 5,0,0");Check(w.Document.Revision==revision&&!w.Document.CanUndo);Check(messages.Any(m=>m.Contains("numerical gap")));
    w.Execute("CurveControlPoint 1 2,3,4 2");Near(((NurbsCurve)w.Document.GetObject(item.Id).Geometry).ControlPoints[1].W,2);w.Execute("Undo");Check(ReferenceEquals(w.Document.GetObject(item.Id).Geometry,curve));
    w.Execute("ProjectToPlane 0,0,2 0,0,1");Near(((NurbsCurve)w.Document.GetObject(item.Id).Geometry).PointAt(.37).Z,2);w.Execute("Undo");
    w.Execute("MirrorPlane 0,0,0 1,0,0");Point(((NurbsCurve)w.Document.GetObject(item.Id).Geometry).PointAt(0),new(-3,0,0));w.Execute("Undo");
    var surface=w.Document.Add(factory.Saddle(),"S");w.Document.Select([item.Id,surface.Id]);revision=w.Document.Revision;
    Throws<ArgumentException>(()=>w.Execute("CurveControlPoint 1 3,4,5"));Check(w.Document.Revision==revision&&ReferenceEquals(w.Document.GetObject(item.Id).Geometry,curve));
});
int failed=0;foreach(var(name,action)in tests)try{await action();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="curve-queries-and-affine-modeling",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
