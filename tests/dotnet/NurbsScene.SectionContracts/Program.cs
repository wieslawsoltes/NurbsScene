using System.Collections.Immutable;
using System.Text.Json;
using System.Reflection;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Workbench;

var evaluator=new ManagedNurbsEvaluator();var factory=new GeometryFactory(evaluator);
var tests=new List<(string Name,Func<Task> Run)>();
void Test(string name,Action action)=>tests.Add((name,()=>{action();return Task.CompletedTask;}));
void AsyncTest(string name,Func<Task> action)=>tests.Add((name,action));
void Check(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
void Near(double a,double b,double tolerance=1e-8)=>Check(double.IsFinite(a)&&Math.Abs(a-b)<=tolerance,$"{a:G17} != {b:G17}");
void Point(Vec3 a,Vec3 b,double tolerance=1e-8)=>Near((a-b).Length,0,tolerance*Math.Max(1,b.Length));
void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
Test("forward extraction matches original rational evaluation and derivatives",()=>{
    var random=new Random(725621);
    for(var degree=1;degree<=12;degree++)for(var trial=0;trial<6;trial++){
        var knots=Enumerable.Repeat(-2d,degree+1).ToList();
        foreach(var knot in new[]{-.71,.032,.033,1.23,2.81})knots.AddRange(Enumerable.Repeat(knot,random.Next(1,degree+1)));
        knots.AddRange(Enumerable.Repeat(4d,degree+1));
        var controls=Enumerable.Range(0,knots.Count-degree-1).Select(_=>HPoint.FromPoint(new(random.NextDouble()*4,random.NextDouble()*4,random.NextDouble()*4),.1+random.NextDouble()*8));
        var curve=new NurbsCurve(evaluator,degree,knots,controls);
        var method=typeof(CurveSections).Assembly.GetType("NurbsScene.Kernel.BezierExtraction")!.GetMethod("Get",BindingFlags.Static|BindingFlags.NonPublic)!;
        var segments=(BezierSegment[])method.Invoke(null,[curve,CancellationToken.None])!;Check(segments.Length==6);
        foreach(var segment in segments){
            var c=new NurbsCurve(evaluator,degree,Enumerable.Repeat(segment.Domain.Min,degree+1).Concat(Enumerable.Repeat(segment.Domain.Max,degree+1)),segment.Controls);
            foreach(var f in new[]{.001,.17,.5,.93,.999}){var t=segment.Domain.At(f);var a=curve.Derivatives(t,2);var b=c.Derivatives(t,2);for(var k=0;k<3;k++)Point(b[k],a[k],2e-6);}
        }
    }
});
Test("public mutable Bezier controls do not alter plane-query cached geometry",()=>{
    var c=factory.Circle(Vec3.Zero,3);var plane=new Plane3(new(1,0,0),Vec3.UnitX);var before=c.IntersectPlane(plane);var a=c.BezierSegments();var correct=a[0].Controls[0];a[0].Controls[0]=HPoint.FromPoint(new(1000,0,0));a[1]=a[0];
    var b=c.BezierSegments();Check(b[0].Controls[0]==correct&&b[1].Domain.Min==.25);Near(c.Tessellate().Positions[0].X,3);
    Parallel.For(0,25,_=>{var section=c.IntersectPlane(plane);Check(section.Complete&&section.Points.SequenceEqual(before.Points));});
});
Test("extraction rejects discontinuities and unclamped knots",()=>{
    var cp=Enumerable.Range(0,4).Select(i=>HPoint.FromPoint(new(i,0,0))).ToArray();
    Throws<NotSupportedException>(()=>new NurbsCurve(evaluator,1,[0,0,.5,.5,1,1],cp).IntersectPlane(new(Vec3.Zero,Vec3.UnitX)));
    Throws<NotSupportedException>(()=>new NurbsCurve(evaluator,2,[-2,-1,0,1,2,3,4],cp).IntersectPlane(new(Vec3.Zero,Vec3.UnitX)));
});
Test("balanced multi-split preserves weighted geometry and domains",()=>{
    var c=factory.Circle(new(2,3,4),5).Reparameterize(new(-3,7));double[] cuts=[6,-1,0,2,.7,5,2];
    var parts=c.SplitMany(cuts);Check(parts.Length==7);Near(parts[0].Domain.Min,-3);Near(parts[^1].Domain.Max,7);
    for(var i=0;i<parts.Length;i++){if(i>0)Near(parts[i-1].Domain.Max,parts[i].Domain.Min);foreach(var f in new[]{0d,.1,.8,1}){var t=parts[i].Domain.At(f);Point(parts[i].PointAt(t),c.PointAt(t));}}
    Check(ReferenceEquals(c.SplitMany([])[0],c));
    Throws<ArgumentOutOfRangeException>(()=>c.SplitMany([double.NaN]));Throws<ArgumentOutOfRangeException>(()=>c.SplitMany([-3]));
    Throws<OperationCanceledException>(()=>c.SplitMany([],new(true)));
});
Test("line-plane crossing and disjoint result",()=>{
    var plane=new Plane3(new(0,0,3),Vec3.UnitZ);var c=factory.Line(new(1,2,-4),new(1,2,10)).Reparameterize(new(-7,13));
    var r=c.IntersectPlane(plane);Check(r.Complete&&r.Points.Length==1&&r.Overlaps.IsEmpty);Near(r.Points[0].Parameter,3);Point(r.Points[0].Point,new(1,2,3));
    Check(factory.Line(Vec3.Zero,Vec3.UnitX).IntersectPlane(plane).Points.IsEmpty);
});
Test("rational circle plane crossings and tangent endpoint",()=>{
    var c=factory.Circle(Vec3.Zero,3);var r=c.IntersectPlane(new(new(1,0,0),Vec3.UnitX));
    Check(r.Complete&&r.Points.Length==2,string.Join(';',r.Points.Select(p=>p.Parameter)));foreach(var hit in r.Points){Near(hit.Point.X,1);Near(Math.Abs(hit.Point.Y),Math.Sqrt(8));}
    var tangent=c.IntersectPlane(new(new(0,3,0),Vec3.UnitY));Check(tangent.Complete&&tangent.Points.Length==1);Point(tangent.Points[0].Point,new(0,3,0));
});
Test("coplanar spans merge into overlap intervals without redundant endpoints",()=>{
    var plane=new Plane3(Vec3.Zero,Vec3.UnitZ);var c=factory.Polyline([new(0,0,1),Vec3.Zero,Vec3.UnitX,new(2,0,0),new(3,0,1)]);
    var r=c.IntersectPlane(plane);Check(r.Complete&&r.Overlaps.Length==1&&r.Points.IsEmpty);Near(r.Overlaps[0].Min,1);Near(r.Overlaps[0].Max,3);
    var circle=factory.Circle(Vec3.Zero,2).IntersectPlane(plane);Check(circle.Complete&&circle.Overlaps.SequenceEqual(new[]{new Interval(0,1)}));
});
Test("non-dyadic tangent candidates retain precision-limited diagnostics",()=>{
    // y=(t-.3)^2 has a double root. Binary64 coefficients can represent a
    // tiny split pair; do not claim certified separation at this conditioning.
    var c=new NurbsCurve(evaluator,2,[0,0,0,1,1,1],[HPoint.FromPoint(new(0,.09,0)),HPoint.FromPoint(new(.5,-.21,0)),HPoint.FromPoint(new(1,.49,0))]);
    var r=c.IntersectPlane(new(Vec3.Zero,Vec3.UnitY));Check(r.Points.Length>=1);Near(r.Points[0].Parameter,.3,1e-7);Check(r.Points.All(p=>p.Residual<1e-7));
});
Test("roots match independently constructed power polynomials through degree eight",()=>{
    double Choose(int n,int k){double b=1;for(var i=1;i<=k;i++)b=b*(n-i+1)/i;return b;}
    for(var n=1;n<=8;n++){
        var expected=Enumerable.Range(1,n).Select(i=>i/(double)(n+1)).ToArray();double[] a=[1];
        foreach(var root in expected){var next=new double[a.Length+1];for(var i=0;i<a.Length;i++){next[i]-=root*a[i];next[i+1]+=a[i];}a=next;}
        var controls=Enumerable.Range(0,n+1).Select(k=>{double b=0;for(var i=0;i<=k;i++)b+=a[i]*Choose(k,i)/Choose(n,i);var w=1+.13*k;return new HPoint(w*k/n,b,0,w);}).ToArray();
        var c=new NurbsCurve(evaluator,n,Enumerable.Repeat(0d,n+1).Concat(Enumerable.Repeat(1d,n+1)),controls);var result=c.IntersectPlane(new(Vec3.Zero,Vec3.UnitY));
        Check(result.Complete&&result.Points.Length==n,$"Degree {n}: {result.Points.Length} roots, {result.Unresolved.Length} unresolved");
        for(var i=0;i<n;i++)Near(result.Points[i].Parameter,expected[i],2e-7);
    }
});
Test("query cancellation, budget exhaustion and finite options are explicit",()=>{
    var c=factory.Circle(Vec3.Zero,3);var plane=new Plane3(new(1,0,0),Vec3.UnitX);
    var r=c.IntersectPlane(plane,new(MaximumEvaluations:1));Check(!r.Complete&&r.Unresolved.Length>0&&r.Evaluations<=1);
    Throws<OperationCanceledException>(()=>c.IntersectPlane(plane,cancellationToken:new(true)));
    Throws<ArgumentException>(()=>c.IntersectPlane(plane,new(DistanceTolerance:double.NaN)));
});
Test("common extreme homogeneous scales do not change section parameters",()=>{
    var source=factory.Circle(Vec3.Zero,3);var plane=new Plane3(new(1,0,0),Vec3.UnitX);var original=source.IntersectPlane(plane);
    foreach(var scale in new[]{1e-150,1e150}){var c=new NurbsCurve(evaluator,source.Degree,source.Knots.ToArray(),source.ControlPoints.ToArray().Select(p=>p*scale));var result=c.IntersectPlane(plane);Check(result.Complete&&result.Points.Length==original.Points.Length);for(var i=0;i<result.Points.Length;i++)Near(result.Points[i].Parameter,original.Points[i].Parameter);}
});
foreach(var component in new[]{"CurvePlaneSection","ShatterCurve"})AsyncTest("executable section component: "+component,async()=>{
    var graph=new Graph(StandardComponents.Create(factory));var c=factory.Circle(Vec3.Zero,3);
    var node=graph.Add(component,parameters:new Dictionary<string,object?>{{"input:C",c},{"input:Origin",new Vec3(1,0,0)},{"input:Normal",Vec3.UnitX}});
    var result=await graph.SolveAsync();Check(result.ErrorCount==0,string.Join(';',result.Nodes[node.Id].Messages));
    var loaded=NurbsScene.IO.WorkspaceFormat.Deserialize(evaluator,graph.Registry,NurbsScene.IO.WorkspaceFormat.Serialize(new(),graph));
    Check((await loaded.Graph.SolveAsync()).ErrorCount==0);
});
Test("section commands preserve identities and undo multi-selection atomically",()=>{
    using var w=new WorkspaceController();var circle=factory.Circle(Vec3.Zero,3);var item=w.Document.Add(circle,"C");w.Document.Select([item.Id]);
    var revision=w.Document.Revision;w.Execute("CurvePlane 1,0,0 1,0,0");Check(w.Document.Revision==revision);
    w.Execute("SplitCurve .25 .5");Check(w.Document.Objects.Count==3&&w.Document.Selection.Count==3);Check(w.Document.GetObject(item.Id).Geometry is NurbsCurve);
    w.Execute("Undo");Check(w.Document.Objects.Count==1&&ReferenceEquals(w.Document.GetObject(item.Id).Geometry,circle));
    w.Execute("Redo");Check(w.Document.Objects.Count==3);w.Execute("Undo");

    w.Execute("SplitCurve .25 .5 .75");Check(w.Document.Objects.Count==4);w.Execute("Undo");
    Throws<ArgumentOutOfRangeException>(()=>w.Execute("SplitCurve 0.25 2"));Check(w.Document.Objects.Count==1&&ReferenceEquals(w.Document.GetObject(item.Id).Geometry,circle));
});
Test("near-endpoint root clusters cannot be reported as completely resolved",()=>{
    // Bernstein coefficients for (t-epsilon)*(t-.5); the endpoint residual is
    // tiny but nonzero. Reporting the endpoint as an exact isolated event can
    // otherwise suppress the actual nearby crossing.
    const double epsilon=5e-16;
    var c=new NurbsCurve(evaluator,2,[0,0,0,1,1,1],new[]{
        HPoint.FromPoint(new(0,.5*epsilon,0)),
        HPoint.FromPoint(new(.5,.5*epsilon-(.5+epsilon)/2,0)),
        HPoint.FromPoint(new(1,.5-.5*epsilon,0))});
    var result=c.IntersectPlane(new(Vec3.Zero,Vec3.UnitY));
    Check(!result.Complete&&result.Unresolved.Length>0,"An uncertain endpoint was treated as resolved");
});
tests.AddRange(RuntimeReview.Tests());
int failed=0;foreach(var(name,run)in tests)try{await run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="curve-sections",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
