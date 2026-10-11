using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Workbench;

var factory=new GeometryFactory(new ManagedNurbsEvaluator());
var tests=new List<(string Name,Action Test)>();
void Test(string name,Action test)=>tests.Add((name,test));
void Check(bool value,string why="Assertion failed"){if(!value)throw new Exception(why);}
void Near(double a,double b,double tol=1e-7)=>Check(double.IsFinite(a)&&Math.Abs(a-b)<=tol*Math.Max(1,Math.Abs(b)),$"{a:G17} != {b:G17}");
void Point(Vec3? a,Vec3 b,double tol=1e-7){Check(a.HasValue);Near((a!.Value-b).Length,0,tol);}
void Throws<T>(Action f)where T:Exception{try{f();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
NurbsSurface Rectangle(double w=4,double h=3)=>factory.FromCorners(Vec3.Zero,new(w,0,0),new(w,h,0),new(0,h,0));
SurfaceAreaResult Area(NurbsSurface s,SurfaceAreaOptions? o=null){var r=s.GetAreaProperties(o);Check(r.Converged,$"Not converged: {r.Termination} {r.Cells} {r.EstimatedAreaError}");return r;}
Test("planar rectangle and exact tilted parallelogram",()=>{
    var a=Area(Rectangle());Near(a.Area,12);Point(a.Centroid,new(2,1.5,0));Check(a.EstimatedAreaError>=0&&a.EstimatedCentroidError.HasValue);
    var x=new Vec3(2,1,3);var y=new Vec3(-1,4,2);var s=factory.FromCorners(Vec3.Zero,x,x+y,y);
    var r=Area(s);Near(r.Area,Vec3.Cross(x,y).Length);Point(r.Centroid,(x+y)/2);
});
Test("rational cylinder area and centroid",()=>{
    var s=factory.Extrude(factory.Circle(new(2,3,4),3),new(0,0,7));var r=Area(s);
    Near(r.Area,42*Math.PI);Point(r.Centroid,new(2,3,7.5));
});
Test("sphere area and centroid with singular poles",()=>{
    var profile=factory.Arc(Vec3.Zero,3,-Math.PI/2,Math.PI,Vec3.UnitX,Vec3.UnitZ);
    var sphere=factory.Revolve(profile,Vec3.Zero,Vec3.UnitZ);var r=Area(sphere);
    Near(r.Area,36*Math.PI);Point(r.Centroid,Vec3.Zero);
});
Test("cone area despite zero-area tip",()=>{
    var s=factory.Revolve(factory.Line(new(3,0,0),new(0,0,4)),Vec3.Zero,Vec3.UnitZ);var r=Area(s);
    Near(r.Area,15*Math.PI);Point(r.Centroid,new(0,0,4d/3));
});
Test("area and centroid survive reversal transposition reparameterization and refinement",()=>{
    var s=factory.Saddle(6,4,1.3);var r=Area(s);
    foreach(var edit in new[]{s.Reverse(SurfaceDirection.U),s.Transpose(),s.Reparameterize(new(-17,11),new(2,8)),s.InsertKnot(SurfaceDirection.V,.003,2),s.ElevateDegree(SurfaceDirection.U,2)}){
        var e=Area(edit);Near(e.Area,r.Area);Point(e.Centroid,r.Centroid!.Value);
    }
});
Test("stable translated coordinates and scale covariance",()=>{
    var s=Rectangle();var offset=new Vec3(1e12,-2e12,3e12);var r=Area(s.Transform(Transform3.Translation(offset)));
    Near(r.Area,12);Point(r.Centroid,offset+new Vec3(2,1.5,0),1e-3);
    foreach(var k in new[]{1e-6,1e6}){var a=Area(s.Transform(Transform3.Scale(new(k,k,k))),new(AbsoluteTolerance:1e-24));Near(a.Area,12*k*k,1e-9);Point(a.Centroid,new(2*k,1.5*k,0),1e-8*Math.Max(1,k));}
});
Test("degenerate point and line patches have no fabricated centroid",()=>{
    var point=factory.FromCorners(new(2,3,4),new(2,3,4),new(2,3,4),new(2,3,4));
    var line=factory.FromCorners(Vec3.Zero,Vec3.UnitX,Vec3.UnitX,Vec3.Zero);
    foreach(var s in new[]{point,line}){var r=Area(s);Check(r.Area==0&&r.Centroid is null&&r.EstimatedCentroidError is null);}
});
Test("disconnected knot regions integrate separately without gap area",()=>{
    var cp=new[]{0d,1,50,52}.SelectMany(x=>new[]{HPoint.FromPoint(new(x,0,0)),HPoint.FromPoint(new(x,2,0))});
    var s=new NurbsSurface(factory.Evaluator,1,1,4,2,[0,0,.5,.5,1,1],[0,0,1,1],cp);var r=Area(s);
    Near(r.Area,6);Point(r.Centroid,new((.5*2+51*4)/6,1,0));
});
Test("narrow knot features contribute area",()=>{
    double[] u=[0,0,0,.123,.123,.124,.124,1,1,1];double[] heights=[0,0,0,10,0,0,0];
    var cp=Enumerable.Range(0,7).SelectMany(i=>new[]{HPoint.FromPoint(new(i,0,heights[i])),HPoint.FromPoint(new(i,2,heights[i]))});
    var s=new NurbsSurface(factory.Evaluator,2,1,7,2,u,[0,0,1,1],cp);Check(Area(s).Area>25);
});
Test("limits return an unresolved complete-domain estimate and never cache it",()=>{
    var s=factory.Saddle(6,4,8);var o=new SurfaceAreaOptions(AbsoluteTolerance:1e-16,RelativeTolerance:0,MaximumCells:1);
    var a=s.GetAreaProperties(o);var b=s.GetAreaProperties(o);Check(!a.Converged&&a.Termination==SurfaceAreaTermination.CellLimit&&!b.CacheHit&&b.Evaluations>0);
    Check(!s.GetAreaProperties(o with{MaximumCells=4096,MaximumEvaluations=42}).Converged);
    Check(!s.GetAreaProperties(o with{MaximumCells=4096,MaximumDepth=0}).Converged);
    Throws<ArgumentException>(()=>factory.Extrude(factory.Circle(Vec3.Zero,1),Vec3.UnitZ).GetAreaProperties(new(MaximumCells:1)));
});
Test("thin rational boundary layers cannot falsely pass interior quadrature",()=>{
    var cp=new[]{HPoint.FromPoint(new(0,0,0)),HPoint.FromPoint(new(0,1,0)),HPoint.FromPoint(new(1,0,0),1e-12),HPoint.FromPoint(new(1,1,0),1e-12)};
    var s=new NurbsSurface(factory.Evaluator,1,1,2,2,[0,0,1,1],[0,0,1,1],cp);var r=s.GetAreaProperties(new(MaximumCells:256));
    Check(!r.Converged||Math.Abs(r.Area-1)<1e-6,"Interior rules missed an endpoint boundary layer");
});
Test("cache uses exact geometry identity and settings and obeys cancellation",()=>{
    var s=Rectangle();var r=Area(s);var cached=Area(s);Check(cached.CacheHit&&cached.Evaluations==0&&cached.Area==r.Area&&cached.Centroid==r.Centroid);
    Check(!Area(s,new(RelativeTolerance:1e-6)).CacheHit);Check(!Area(s).CacheHit,"Cache should retain at most one settings entry");
    Check(!Area(s,new(CacheResult:false)).CacheHit);Check(!Area(s.Transform(Transform3.Identity)).CacheHit);
    Throws<OperationCanceledException>(()=>s.GetAreaProperties(cancellationToken:new(true)));
    Throws<ArgumentException>(()=>s.GetAreaProperties(new(RelativeTolerance:double.NaN)));
});
Test("legacy scalar and batch evaluators agree",()=>{
    var f=new GeometryFactory(new LegacyEvaluator(factory.Evaluator));var fast=Area(factory.Saddle(6,4,1.3));var slow=Area(f.Saddle(6,4,1.3));Near(fast.Area,slow.Area,1e-12);Point(fast.Centroid,slow.Centroid!.Value,1e-12);
});
Test("concurrent surface measurements keep scratch and caches isolated",()=>{
    var s=factory.Saddle(6,4,1.3);var expected=Area(s);Parallel.For(0,16,i=>{var a=Area(s,new(CacheResult:i%2==0));Near(a.Area,expected.Area);Point(a.Centroid,expected.Centroid!.Value);});
});
Test("extreme aspect ratio cannot silently collapse a finite area to zero",()=>{
    var thin=Rectangle(1e-200,1e200);
    Throws<ArithmeticException>(()=>thin.GetAreaProperties());
});
Test("twelve independent SciPy surface integral references",()=>{
    using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"areas-scipy.json")));
    foreach(var c in json.RootElement.GetProperty("cases").EnumerateArray()){
        double[] Values(string name)=>c.GetProperty(name).EnumerateArray().Select(x=>x.GetDouble()).ToArray();
        var cp=c.GetProperty("controls").EnumerateArray().Select(row=>{var a=row.EnumerateArray().Select(x=>x.GetDouble()).ToArray();return new HPoint(a[0],a[1],a[2],a[3]);});
        var surface=new NurbsSurface(factory.Evaluator,c.GetProperty("degreeU").GetInt32(),c.GetProperty("degreeV").GetInt32(),c.GetProperty("countU").GetInt32(),c.GetProperty("countV").GetInt32(),Values("knotsU"),Values("knotsV"),cp);
        var result=Area(surface);Near(result.Area,c.GetProperty("area").GetDouble(),3e-8);var center=Values("centroid");Point(result.Centroid,new(center[0],center[1],center[2]),3e-8);
    }
});
foreach(var id in new[]{"SurfaceArea","SurfaceCentroid"})Test("executable surface analysis component and workspace round trip: "+id,()=>{
    var graph=new Graph(StandardComponents.Create(factory));var node=graph.Add(id,parameters:new Dictionary<string,object?>{{"input:S",Rectangle()}});
    var result=graph.SolveAsync().GetAwaiter().GetResult();Check(result.ErrorCount==0);
    if(id=="SurfaceArea")Near((double)result.Nodes[node.Id].Outputs["A"].AllItems.Single()!,12);
    else Point((Vec3)result.Nodes[node.Id].Outputs["C"].AllItems.Single()!,new(2,1.5,0));
    var json=NurbsScene.IO.WorkspaceFormat.Serialize(new(),graph);
    var loaded=NurbsScene.IO.WorkspaceFormat.Deserialize(factory.Evaluator,graph.Registry,json);
    Check(loaded.Graph.SolveAsync().GetAwaiter().GetResult().ErrorCount==0);
});
Test("centroid component diagnoses zero area rather than outputting an invented point",()=>{
    var graph=new Graph(StandardComponents.Create(factory));var s=factory.FromCorners(Vec3.Zero,Vec3.Zero,Vec3.Zero,Vec3.Zero);
    var a=graph.Add("SurfaceArea",parameters:new Dictionary<string,object?>{{"input:S",s}});
    var c=graph.Add("SurfaceCentroid",parameters:new Dictionary<string,object?>{{"input:S",s}});
    var r=graph.SolveAsync().GetAwaiter().GetResult();Check(r.Nodes[a.Id].Status=="ok"&&r.Nodes[c.Id].Status=="error");
});
Test("read-only area commands preserve document revision selection and undo",()=>{
    using var w=new WorkspaceController();var s=Rectangle();var item=w.Document.Add(s,"Panel");w.Document.Select([item.Id]);
    var messages=new List<string>();w.Message+=messages.Add;var revision=w.Document.Revision;
    w.Execute("SurfaceArea");w.Execute("SurfaceCentroid");Check(w.Document.Revision==revision&&!w.Document.CanUndo&&w.Document.Selection.Contains(item.Id));
    Check(messages.Any(m=>m.Contains("area centroid"))&&messages.Any(m=>m.Contains("mm^2")));
    Throws<ArgumentException>(()=>w.Execute("SurfaceArea -1"));Check(w.Document.Revision==revision);
    var curve=w.Document.Add(factory.Circle(Vec3.Zero,3),"Curve");w.Document.Select([item.Id,curve.Id]);
    Throws<ArgumentException>(()=>w.Execute("SurfaceArea"));Check(ReferenceEquals(w.Document.GetObject(item.Id).Geometry,s));
});
Test("mid-integration cancellation cannot publish a cached partial result",()=>{
    using var cancel=new CancellationTokenSource();var calls=0;
    var evaluator=new CountingEvaluator(factory.Evaluator,()=>{if(++calls==8)cancel.Cancel();});
    var s=new GeometryFactory(evaluator).Saddle(6,4,1.3);
    Throws<OperationCanceledException>(()=>s.GetAreaProperties(cancellationToken:cancel.Token));
    var result=Area(s);Check(!result.CacheHit&&result.Evaluations>0);
});
if(Environment.GetEnvironmentVariable("NURBSSCENE_NATIVE_TESTS")=="1") tests.AddRange(NativeBuffers.Tests());
int failed=0;foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="surface-analysis",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
sealed class LegacyEvaluator(INurbsEvaluator inner):INurbsEvaluator
{
    public string Implementation=>"Scalar comparison";
    public Vec3[] Evaluate(NurbsCurve c,double t,int n=0)=>inner.Evaluate(c,t,n);
    public Vec3[,] Evaluate(NurbsSurface s,double u,double v,int a=0,int b=0)=>inner.Evaluate(s,u,v,a,b);
}

sealed class CountingEvaluator(INurbsEvaluator inner,Action tick):INurbsEvaluator
{
    public string Implementation=>"Cancellation test";
    public Vec3[] Evaluate(NurbsCurve c,double t,int n=0){tick();return inner.Evaluate(c,t,n);}
    public Vec3[,] Evaluate(NurbsSurface s,double u,double v,int a=0,int b=0){tick();return inner.Evaluate(s,u,v,a,b);}
}
