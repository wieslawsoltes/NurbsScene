using System.Collections.Immutable;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.Workbench;
using NurbsScene.Model;
using NurbsScene.IO;
using NurbsScene.Rendering;

var kernel = new GeometryFactory(new ManagedNurbsEvaluator());
var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double a, double b, double tolerance = 2e-10) => Check(double.IsFinite(a) && Math.Abs(a-b) <= tolerance*Math.Max(1,Math.Abs(b)), $"{a:G17} != {b:G17}");
void Point(Vec3 a, Vec3 b, double tolerance = 2e-9) => Check((a-b).Length <= tolerance*Math.Max(1,b.Length), $"{a} != {b}");
void Throws<T>(Action run) where T:Exception { try { run(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }

Test("ellipse is a closed nine-control positive-weight quadratic NURBS", () => {
    var c=kernel.Ellipse(new(2,3,4),10,5);
    Check(c.Degree==2 && c.Count==9 && c.Knots.Length==12 && c.Domain==new Interval(0,1));
    Check(c.ControlPoints[0]==c.ControlPoints[8] && c.ControlPoints.ToArray().All(p=>p.IsValid));
    for(var i=0;i<9;i++)Near(c.ControlPoints[i].W,i%2==0?1:Math.Sqrt(.5));
    Point(c.PointAt(0),new(12,3,4));Point(c.PointAt(.25),new(2,8,4));
    Point(c.PointAt(.5),new(-8,3,4));Point(c.PointAt(.75),new(2,-2,4));Point(c.PointAt(1),c.PointAt(0));
    Point(c.Derivatives(0,1)[1],c.Derivatives(1,1)[1]);
});
Test("ellipse satisfies the analytic conic in an arbitrary orthogonal plane", () => {
    var x=new Vec3(1,1,0).Normalized();var y=new Vec3(-1,1,2).Normalized();var center=new Vec3(3,-8,4);
    var c=kernel.Ellipse(center,17,2.5,x*4,y*7);var normal=Vec3.Cross(x,y);
    for(var i=0;i<=1000;i++){
        var q=c.PointAt(i/1000d)-center;
        Near(Math.Pow(Vec3.Dot(q,x)/17,2)+Math.Pow(Vec3.Dot(q,y)/2.5,2),1,3e-12);
        Near(Vec3.Dot(q,normal),0,5e-12);
    }
});
Test("ellipse rational parameter is not misrepresented as angle", () => {
    var c=kernel.Ellipse(Vec3.Zero,3,2);var t=.037;var q=c.PointAt(t);
    Check(Math.Abs(Math.Atan2(q.Y/2,q.X/3)-t*2*Math.PI)>1e-4);
    Near(c.CurvatureAt(0),3d/4);Near(c.CurvatureAt(.25),2d/9);
    Near(kernel.Ellipse(Vec3.Zero,3,3).GetLength().Length,6*Math.PI,2e-8);
});
Test("ellipse transforms, splitting and elevation retain authoritative geometry", () => {
    var original=kernel.Ellipse(new(1,2,3),8,2);
    var elevated=original.ElevateDegree(2);var split=original.Split(.43);
    var shifted=original.Transform(Transform3.Translation(new(5,6,7)));
    for(var i=0;i<=100;i++){
        var t=i/100d;Point(elevated.PointAt(t),original.PointAt(t));
        Point(shifted.PointAt(t),original.PointAt(t)+new Vec3(5,6,7));
        Point((t<=.43?split.Left:split.Right).PointAt(t),original.PointAt(t));
    }
});
Test("ellipse rejects invalid radii axes and collapsed floating-point scales", () => {
    foreach(var r in new[]{0d,-1,double.NaN,double.PositiveInfinity})
        Throws<ArgumentException>(()=>kernel.Ellipse(Vec3.Zero,r,2));
    Throws<ArgumentException>(()=>kernel.Ellipse(new(double.NaN,0,0),1,2));
    Throws<ArithmeticException>(()=>kernel.Ellipse(Vec3.Zero,1,2,Vec3.Zero,Vec3.UnitY));
    Throws<ArgumentException>(()=>kernel.Ellipse(Vec3.Zero,1,2,Vec3.UnitX,Vec3.UnitX));
    Throws<ArgumentException>(()=>kernel.Ellipse(new(1e300,1e300,0),1,1));
    Throws<ArgumentException>(()=>kernel.Ellipse(new(double.MaxValue,0,0),double.MaxValue,2));
});
Test("scaled pivot factor reuse solves multiple RHS and late row swaps", () => {
    var rng=new Random(55130);
    for(var n=2;n<=24;n++){
        var a=new double[n,n];var expected=new double[n,4];
        // Reverse rows of a strongly diagonal matrix to require permutations,
        // including previous L entries. Residual is checked independently.
        for(var i=0;i<n;i++)for(var j=0;j<n;j++)a[n-1-i,j]=i==j?10+n:rng.NextDouble()-.5;
        for(var i=0;i<n;i++)for(var c=0;c<4;c++)expected[i,c]=rng.NextDouble()*20-10;
        var factors=new InterpolationFactors((double[,])a.Clone(),default);
        for(var pass=0;pass<4;pass++){
            var b=new double[n,4];
            for(var i=0;i<n;i++)for(var c=0;c<4;c++)for(var j=0;j<n;j++)b[i,c]+=a[i,j]*expected[j,c];
            factors.Solve(b,default);
            for(var i=0;i<n;i++)for(var c=0;c<4;c++)Near(b[i,c],expected[i,c]);
        }
    }
});
Test("factor diagnostics preserve singular nonfinite dimension and cancellation checks", () => {
    Throws<ArithmeticException>(()=>new InterpolationFactors(new double[,]{{1,1},{2,2}},default));
    Throws<ArithmeticException>(()=>new InterpolationFactors(new double[,]{{double.NaN}},default));
    Throws<ArgumentException>(()=>new InterpolationFactors(new double[2,3],default));
    using var cancel=new CancellationTokenSource();cancel.Cancel();
    Throws<OperationCanceledException>(()=>new InterpolationFactors(new double[,]{{1}},cancel.Token));
    var f=new InterpolationFactors(new double[,]{{1,0},{0,1}},default);
    Throws<ArgumentException>(()=>f.Solve(new double[3,4],default));
    Throws<ArithmeticException>(()=>f.Solve(new double[,]{{1},{double.PositiveInfinity}},default));
    Throws<OperationCanceledException>(()=>f.Solve(new double[2,4],cancel.Token));
});
Test("warm factor solves allocate no managed bytes", () => {
    var f=new InterpolationFactors(new double[,]{{2,1,0},{0,3,1},{1,0,4}},default);var rhs=new double[3,4];
    void Run(){for(var i=0;i<3;i++)for(var c=0;c<4;c++)rhs[i,c]=i+c;f.Solve(rhs,default);}
    for(var i=0;i<100;i++)Run();var before=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++)Run();var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
    Check(allocated==0);Console.WriteLine($"METRIC repeated-factor-solve-10000={allocated} B");
});
Test("loft interpolates every rational section across many V degrees", () => {
    var sections=Enumerable.Range(0,14).Select(i=>kernel.Ellipse(new(Math.Sin(i*.2),0,i*2),5+i*.07,3+i*.03)).ToArray();
    foreach(var degree in new[]{1,2,3,5,8}){
        var s=kernel.Loft(sections,degree);
        for(var v=0;v<sections.Length;v++)for(var i=0;i<=20;i++)
            Point(s.PointAt(i/20d,v/(sections.Length-1d)),sections[v].PointAt(i/20d),1e-8);
    }
});
Test("factor-once loft agrees with the former independent dense elimination", () => {
    var rng=new Random(951);
    for(var trial=0;trial<12;trial++){
        var count=4+trial;var sections=new NurbsCurve[count];
        for(var i=0;i<count;i++){
            var baseCurve=kernel.Circle(new(0,0,i),5+i*.1);
            // Section-dependent weight scales preserve each section geometry,
            // while exercising actual homogeneous-coordinate interpolation.
            var scale=1+.03*i;
            sections[i]=new(kernel.Evaluator,baseCurve.Degree,baseCurve.Knots.ToArray(),
                baseCurve.ControlPoints.ToArray().Select(p=>p*scale));
        }
        var degree=Math.Min(3,count-1);var actual=kernel.Loft(sections,degree);
        var expected=ReferenceLoft(sections,degree);
        Check(actual.CountU==expected.CountU&&actual.CountV==expected.CountV);
        for(var i=0;i<actual.ControlPoints.Length;i++){
            var a=actual.ControlPoints[i];var b=expected.ControlPoints[i];
            Near(a.X,b.X);Near(a.Y,b.Y);Near(a.Z,b.Z);Near(a.W,b.W);
        }
        for(var i=0;i<30;i++){
            var u=rng.NextDouble();var v=rng.NextDouble();
            var a=actual.Derivatives(u,v,2,2);var b=expected.Derivatives(u,v,2,2);
            for(var du=0;du<=2;du++)for(var dv=0;dv<=2;dv++)Point(a[du,dv],b[du,dv],2e-7);
        }
    }
});
Test("loft compatibility still elevates degrees and inserts differing knots", () => {
    var c=kernel.Circle(Vec3.Zero,4);
    NurbsCurve[] sections=[c,c.Transform(Transform3.Translation(new(0,0,3))).ElevateDegree(),
        c.Transform(Transform3.Translation(new(0,0,6))).InsertKnot(.3)];
    var surface=kernel.Loft(sections);
    Check(surface.DegreeU==3&&surface.DegreeV==2);
    for(var v=0;v<3;v++)for(var i=0;i<=30;i++)Point(surface.PointAt(i/30d,v/2d),sections[v].PointAt(i/30d));
});
Test("two-section loft remains an exact ruled interpolation", () => {
    var a=kernel.Ellipse(Vec3.Zero,7,3);var b=a.Transform(Transform3.Translation(new(0,0,10)));
    var s=kernel.Loft([a,b],20);Check(s.DegreeV==1);
    for(var i=0;i<50;i++){var u=i/49d;Point(s.PointAt(u,.3),a.PointAt(u)+new Vec3(0,0,3));}
});
Test("loft refuses partial invalid nets and observes entry cancellation", () => {
    Throws<ArgumentNullException>(()=>kernel.Loft(null!));
    Throws<ArgumentException>(()=>kernel.Loft([]));
    var c=kernel.Circle(Vec3.Zero,3);
    Throws<ArgumentException>(()=>kernel.Loft([c,null!]));
    Throws<ArgumentException>(()=>kernel.Loft([c,c],0));
    Throws<ArgumentException>(()=>kernel.Loft(Enumerable.Repeat(c,4097).ToArray()));
    Throws<ArgumentException>(()=>kernel.Loft(Enumerable.Repeat(c,130).ToArray(),129));
    using var cancel=new CancellationTokenSource();cancel.Cancel();
    Throws<OperationCanceledException>(()=>kernel.Loft([c,c],3,cancel.Token));
    // Positive input section weights need not produce positive interpolated
    // weights. Keep the existing exact-geometry constructor's rejection.
    var tiny=new NurbsCurve(kernel.Evaluator,1,[0,0,1,1],[new(0,0,0,.01),new(.01,0,0,.01)]);
    var big=new NurbsCurve(kernel.Evaluator,1,[0,0,1,1],[new(0,0,2,1),new(1,0,2,1)]);
    Throws<ArgumentException>(()=>kernel.Loft([tiny,tiny,big],2));
});
Test("ellipse component is typed and solves into existing geometry operations", () => {
    var graph=new Graph(StandardComponents.Create(kernel));
    var e=graph.Add("Ellipse",parameters:new Dictionary<string,object?>{["input:RadiusX"]=7d,["input:RadiusY"]=2d});
    var extrude=graph.Add("Extrude");graph.Connect(new(e.Id,"C"),new(extrude.Id,"C"));
    var solved=graph.SolveAsync().GetAwaiter().GetResult();Check(solved.ErrorCount==0);
    var curve=(NurbsCurve)solved.Nodes[e.Id].Outputs["C"].AllItems.Single()!;
    Point(curve.PointAt(0),new(7,0,0));
    Check(solved.Nodes[extrude.Id].Outputs["S"].AllItems.Single() is NurbsSurface);
    var result=graph.SolveAsync().GetAwaiter().GetResult();Check(result.Evaluated==0);
});
Test("ellipse command, toolbar metadata and history are connected", () => {
    using var controller=new WorkspaceController();
    var before=controller.Document.Objects.Count;controller.Execute("Ellipse 1,2,3 8 4");
    Check(controller.Document.Objects.Count==before+1&&controller.Document.Objects.Last().Name=="Ellipse");
    var c=(NurbsCurve)controller.Document.Objects.Last().Geometry;Point(c.PointAt(0),new(9,2,3));
    controller.Execute("Undo");Check(controller.Document.Objects.Count==before);
    controller.Execute("Redo");Check(controller.Document.Objects.Count==before+1);
    var revision=controller.Document.Revision;
    Throws<ArgumentException>(()=>controller.Execute("Ellipse 0,0,0 -2 4"));Check(controller.Document.Objects.Count==before+1);
    Check(WorkspaceCommands.Search("Ellipse").Any(t=>t.Id=="Ellipse"&&t.Command.StartsWith("Ellipse "))&&ComponentGlyphs.IconKey("Ellipse","Curve")=="Ellipse");
});
Test("ellipse and loft survive exact workspace serialization and display meshing", () => {
    var sections=Enumerable.Range(0,5).Select(i=>kernel.Ellipse(new(0,0,i*2),5+i*.2,3)).ToArray();
    var doc=new SceneDocument();doc.Transaction("Geometry",()=>{doc.Add(sections[0],"Ellipse");doc.Add(kernel.Loft(sections),"Loft");});
    var registry=StandardComponents.Create(kernel);var graph=new Graph(registry);
    var restored=WorkspaceFormat.Deserialize(kernel.Evaluator,registry,WorkspaceFormat.Serialize(doc,graph));
    var curve=(NurbsCurve)restored.Document.Objects.First().Geometry;var surface=(NurbsSurface)restored.Document.Objects.Last().Geometry;
    Point(curve.PointAt(.13),sections[0].PointAt(.13));
    Check(MeshBuilder.Build(surface,Vec3.Zero,24).Indices.Length>0);
    Point(surface.PointAt(.42,.5),sections[2].PointAt(.42));
});
Test("native derivative evaluator independently agrees on the new ellipse and loft", () => {
    var native=new NativeNurbsEvaluator();var nk=new GeometryFactory(native);
    var managed=kernel.Ellipse(new(3,2,1),8,3);var n=nk.Ellipse(new(3,2,1),8,3);
    for(var i=0;i<=100;i++){var t=i/100d;var a=managed.Derivatives(t,2);var b=n.Derivatives(t,2);for(var j=0;j<3;j++)Point(a[j],b[j],5e-10);}
    var sections=Enumerable.Range(0,12).Select(i=>kernel.Ellipse(new(0,0,i),5+i*.1,3)).ToArray();
    var s=kernel.Loft(sections);var ns=new NurbsSurface(native,s.DegreeU,s.DegreeV,s.CountU,s.CountV,s.KnotsU.ToArray(),s.KnotsV.ToArray(),s.ControlPoints.ToArray());
    for(var i=0;i<40;i++){var u=(i+.13)/40;var v=(i+.37)/40;var a=s.Derivatives(u,v,1,1);var b=ns.Derivatives(u,v,1,1);for(var du=0;du<2;du++)for(var dv=0;dv<2;dv++)Point(a[du,dv],b[du,dv],5e-9);}
});

NurbsSurface ReferenceLoft(NurbsCurve[] sections,int degree)
{
    var compatible=CurveOperations.MakeCompatible(sections);var count=compatible.Length;
    var time=Enumerable.Range(0,count).Select(i=>i/(count-1d)).ToArray();var knots=new double[count+degree+1];
    for(var j=1;j<count-degree;j++){double sum=0;for(var i=j;i<j+degree;i++)sum+=time[i];knots[j+degree]=sum/degree;}
    Array.Fill(knots,1,count,degree+1);var matrix=new double[count,count];
    for(var i=0;i<count;i++){var(span,basis)=ManagedNurbsEvaluator.Basis(degree,count,knots,time[i],0);for(var j=0;j<=degree;j++)matrix[i,span-degree+j]=basis[0,j];}
    var points=new HPoint[compatible[0].Count*count];
    for(var u=0;u<compatible[0].Count;u++){
        var rhs=new double[count,4];
        for(var j=0;j<count;j++){var p=compatible[j].ControlPoints[u];rhs[j,0]=p.X;rhs[j,1]=p.Y;rhs[j,2]=p.Z;rhs[j,3]=p.W;}
        // Existing dense elimination independently performs all row operations
        // on each RHS. It does not reuse the new factors or sparse solve ranges.
        LinearSolver.Solve((double[,])matrix.Clone(),rhs);
        for(var j=0;j<count;j++)points[u*count+j]=new(rhs[j,0],rhs[j,1],rhs[j,2],rhs[j,3]);
    }
    return new(kernel.Evaluator,compatible[0].Degree,degree,compatible[0].Count,count,compatible[0].Knots.ToArray(),knots,points);
}
DocumentOrderContracts.Register(tests,kernel);
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+"\n"+error);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="loft-ellipse",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
