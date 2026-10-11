using NurbsScene.Geometry;
using NurbsScene.Kernel;
using System.Text.Json;

var kernel = new GeometryFactory(new ManagedNurbsEvaluator());
var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Near(double a, double b, double tolerance = 1e-8) { if (!double.IsFinite(a) || Math.Abs(a-b)>tolerance*Math.Max(1,Math.Abs(b))) throw new Exception($"Actual {a:G17}; expected {b:G17}; difference {a-b:G17}; tolerance {tolerance*Math.Max(1,Math.Abs(b)):G17}"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
Test("line world-unit length and inverse parameter", () => {
    var c = kernel.Line(new(1,2,3), new(4,6,3)).Reparameterize(new(10,30));
    Near(c.GetLength().Length,5); Near(c.LengthParameter(2),18); Near(c.Measure(new(14,22)).Length,2);
});
Test("rational circle and negative-sweep arc lengths", () => {
    Near(kernel.Circle(Vec3.Zero,7).GetLength().Length,14*Math.PI);
    Near(kernel.Arc(new(5,2,-1),3,.71,-1.73).GetLength().Length,5.19);
});
Test("equal arc length is not uniform NURBS parameter", () => {
    var c = kernel.Circle(Vec3.Zero,3); var division = c.DivideByCount(13);
    Check(division.Parameters.Length==14); Near(division.Parameters[0],0); Near(division.Parameters[^1],1);
    for(var i=0;i<13;i++) Near(c.Measure(new(division.Parameters[i],division.Parameters[i+1])).Length,6*Math.PI/13,2e-8);
    Check(Math.Abs(division.Parameters[1]-1d/13)>1e-4);
    Check(c.DivideByCount(13,false).Parameters.Length==12);
});
Test("polyline splits integration at kinks", () => {
    var c=kernel.Polyline([new(0,0,0),new(3,0,0),new(3,4,0),new(3,4,12)]);
    Near(c.GetLength().Length,19); var m=c.CreateArcLengthMap();Near(m.ParameterAtLength(3),1);Near(m.ParameterAtLength(7),2);
    var d=m.DivideByCount(19);for(var i=1;i<d.Parameters.Length;i++)Near(c.Measure(new(d.Parameters[i-1],d.Parameters[i])).Length,1);
});
Test("stationary tangent inversion remains bracketed", () => {
    var c=new NurbsCurve(kernel.Evaluator,3,[0,0,0,0,1,1,1,1],[HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.UnitX)]);
    Near(c.GetLength().Length,1);Near(c.LengthParameter(.001),.1,1e-7);
});
Test("quadratic parabola analytic oracle", () => {
    var c=new NurbsCurve(kernel.Evaluator,2,[0,0,0,1,1,1],[HPoint.FromPoint(new(0,0,0)),HPoint.FromPoint(new(.5,0,0)),HPoint.FromPoint(new(1,1,0))]);
    Near(c.GetLength().Length,.5*Math.Sqrt(5)+.25*Math.Asinh(2),1e-10);
});
Test("reversing reparameterizing inserting knots preserves length", () => {
    var c=kernel.Circle(Vec3.Zero,4);var length=c.GetLength().Length;
    Near(c.Reverse().GetLength().Length,length);Near(c.Reparameterize(new(-10,200)).GetLength().Length,length);
    Near(c.InsertKnot(.123,2).ElevateDegree(1).GetLength().Length,length);
});
Test("degenerate curve length and ambiguous division", () => {
    var c=kernel.Line(Vec3.UnitX,Vec3.UnitX);Near(c.GetLength().Length,0);Near(c.LengthParameter(0),0);
    Throws<InvalidOperationException>(()=>c.DivideByCount(3));
});
Test("invalid length domain budgets and cancellation fail explicitly", () => {
    var c=kernel.Circle(Vec3.Zero,3);
    Throws<ArgumentOutOfRangeException>(()=>c.LengthParameter(-1));Throws<ArgumentOutOfRangeException>(()=>c.LengthParameter(100));
    Throws<ArgumentOutOfRangeException>(()=>c.Measure(new(.8,.2)));
    Throws<ArgumentException>(()=>c.GetLength(new(AbsoluteTolerance:double.NaN)));
    Throws<ArithmeticException>(()=>c.GetLength(new(MaximumEvaluations:15)));
    Throws<ArithmeticException>(()=>c.GetLength(new(MaximumIntervals:1)));
    Throws<OperationCanceledException>(()=>c.GetLength(cancellationToken:new CancellationToken(true)));
    Throws<OperationCanceledException>(()=>c.CreateArcLengthMap().ParameterAtLength(1,new CancellationToken(true)));
});
Test("disconnected knots are not silently bridged", () => {
    var c=new NurbsCurve(kernel.Evaluator,1,[0,0,.5,.5,1,1],[HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.UnitX),HPoint.FromPoint(new(50,0,0)),HPoint.FromPoint(new(51,0,0))]);
    Throws<NotSupportedException>(()=>c.GetLength());Near(c.Measure(new(0,.5)).Length,1);
});
Test("length map supports concurrent inverse queries", () => {
    var map=kernel.Circle(Vec3.Zero,2).CreateArcLengthMap();
    Parallel.For(1,100,i=>{var p=map.ParameterAtLength(map.Length*i/100);Check(p>0&&p<1);});
});
Test("large translation and uniform scaling", () => {
    var c=kernel.Circle(new(1000000,-2000000,3000000),7);
    Near(c.GetLength().Length,14*Math.PI,1e-8);
    Near(c.Transform(Transform3.Scale(new(3,3,3))).GetLength().Length,42*Math.PI,1e-8);
});
Test("independent SciPy QUADPACK oracle: 32 rational curves degrees 1..8", () => {
    using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"lengths-scipy.json")));
    foreach(var item in fixture.RootElement.GetProperty("cases").EnumerateArray()) {
        var controls=item.GetProperty("controls").EnumerateArray().Select(p=>{var x=p.EnumerateArray().Select(n=>n.GetDouble()).ToArray();return new HPoint(x[0],x[1],x[2],x[3]);});
        var curve=new NurbsCurve(kernel.Evaluator,item.GetProperty("degree").GetInt32(),item.GetProperty("knots").EnumerateArray().Select(n=>n.GetDouble()),controls);
        var length=curve.GetLength();Near(length.Length,item.GetProperty("length").GetDouble(),3e-9);
        var map=curve.CreateArcLengthMap();
        foreach(var fraction in new[]{.01,.173,.5,.913,.99}) {
            var parameter=map.ParameterAtLength(map.Length*fraction);
            Near(curve.Measure(new(curve.Domain.Min,parameter)).Length,map.Length*fraction,1e-8);
        }
    }
});
int failed=0;
foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="curve-measurements",total=tests.Count,passed=tests.Count-failed,failed}));
return failed==0?0:1;
