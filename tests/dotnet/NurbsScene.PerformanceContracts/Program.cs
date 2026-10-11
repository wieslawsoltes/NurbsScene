using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Rendering;

var evaluator=new ManagedNurbsEvaluator();var factory=new GeometryFactory(evaluator);
var tests=new List<(string,Action)>();
void Test(string name,Action action)=>tests.Add((name,action));
void Check(bool ok,string message="Assertion failed"){if(!ok)throw new Exception(message);}
void Near(Vec3 a,Vec3 b,double tolerance=1e-9)=>Check((a-b).Length<=tolerance*Math.Max(1,b.Length),$"{a} != {b}");
void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
Test("allocation-free point and span APIs preserve rational curve derivatives",()=>{
    var curve=factory.Circle(new(3,-2,7),4);
    Span<Vec3> into=stackalloc Vec3[8];
    for(var i=0;i<=100;i++){
        var t=i/100d;var old=evaluator.Evaluate(curve,t,6);into[7]=new(9,9,9);
        evaluator.EvaluateInto(curve,t,6,into);
        for(var k=0;k<old.Length;k++)Near(into[k],old[k]);
        Check(into[7]==new Vec3(9,9,9));Near(curve.PointAt(t),old[0]);
    }
    for(var i=0;i<1000;i++)evaluator.EvaluateInto(curve,.371,2,into);
    var before=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++)evaluator.EvaluateInto(curve,.371,2,into);
    Check(GC.GetAllocatedBytesForCurrentThread()-before==0,"Small span evaluation allocated memory.");
});
Test("tensor grid matches scalar jets on asymmetric rational surfaces and reversed domains",()=>{
    var random=new Random(72031);
    for(var p=1;p<=8;p++){
        var q=9-p;var cp=Enumerable.Range(0,(p+1)*(q+1)).Select(_=>HPoint.FromPoint(new(random.NextDouble()*7,random.NextDouble()*5,random.NextDouble()*3),.01+random.NextDouble()*30)).ToArray();
        var s=new NurbsSurface(evaluator,p,q,p+1,q+1,Enumerable.Repeat(-2d,p+1).Concat(Enumerable.Repeat(5d,p+1)),Enumerable.Repeat(10d,q+1).Concat(Enumerable.Repeat(20d,q+1)),cp);
        double[] u=[5,-2,.123,2.7,.123],v=[20,13.73,10];var grid=new SurfaceJet[u.Length*v.Length];evaluator.EvaluateGrid(s,u,v,grid);
        for(var i=0;i<u.Length;i++)for(var j=0;j<v.Length;j++){
            var d=s.Derivatives(u[i],v[j],1,1);var jet=grid[i*v.Length+j];Near(jet.Point,d[0,0]);Near(jet.Du,d[1,0]);Near(jet.Dv,d[0,1]);
        }
    }
});
Test("grid repeated knots and pooled high-degree scratch match scalar derivatives",()=>{
    var s=factory.Saddle().InsertKnot(SurfaceDirection.U,.5,3).ElevateDegree(SurfaceDirection.V,10);
    double[] u=[0,.5,Math.BitDecrement(.5),Math.BitIncrement(.5),1],v=[0,.37,1];var grid=new SurfaceJet[15];evaluator.EvaluateGrid(s,u,v,grid);
    for(var i=0;i<u.Length;i++)for(var j=0;j<v.Length;j++){
        var d=s.Derivatives(u[i],v[j],2,2);var jet=grid[i*v.Length+j];Near(jet.Point,d[0,0]);Near(jet.Du,d[1,0]);Near(jet.Dv,d[0,1]);
    }
});
Test("grid invalid input is atomic and cancellation is observed",()=>{
    var s=factory.Saddle();var sentinel=new SurfaceJet(new(9,9,9),Vec3.Zero,Vec3.Zero);var output=Enumerable.Repeat(sentinel,4).ToArray();
    Throws<ArgumentOutOfRangeException>(()=>evaluator.EvaluateGrid(s,[0,2],[0,1],output));Check(output.All(j=>j==sentinel));
    Throws<ArgumentException>(()=>evaluator.EvaluateGrid(s,[0,1],[0,1],new SurfaceJet[3]));
    Throws<OperationCanceledException>(()=>evaluator.EvaluateGrid(s,[0,1],[0,1],output,new(true)));Check(output.All(j=>j==sentinel));
    Throws<ArgumentOutOfRangeException>(()=>evaluator.Evaluate(s,0,0,33));
    Throws<ArgumentOutOfRangeException>(()=>evaluator.Evaluate(factory.Circle(Vec3.Zero,1),0,-1));
});
Test("legacy evaluator consumer remains supported without optional interfaces",()=>{
    var fallback=new ScalarOnlyEvaluator(evaluator);var f=new GeometryFactory(fallback);var s=f.Saddle();var c=f.Circle(Vec3.Zero,2);
    Near(s.PointAt(.3,.7),factory.Saddle().PointAt(.3,.7));Near(c.PointAt(.3),factory.Circle(Vec3.Zero,2).PointAt(.3));
    var mesh=MeshBuilder.Build(s,Vec3.Zero,12);Check(mesh.Indices.Length==12*12*6);Check(fallback.Calls>100);
});
Test("immutable bounds are cached and transformed copies recompute them",()=>{
    var s=factory.Saddle();var expected=s.ControlPoints.ToArray().Aggregate(Bounds3.Empty,(b,p)=>b.Include(p.Position));Check(s.Bounds==expected);
    var moved=s.Transform(Transform3.Translation(new(100,200,300)));Near(moved.Bounds.Min,expected.Min+new Vec3(100,200,300));
    var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)_ =s.Bounds;Check(GC.GetAllocatedBytesForCurrentThread()==before);
});
Test("display no-op and style edits preserve CPU meshes and mesh revision",()=>{
    var scene=new DisplayScene();var item=new DisplayItem("s",factory.Saddle(),new(.3f,.5f,.7f));scene.SetItems([item]);var revision=scene.Revision;var mr=scene.MeshRevision;var mesh=scene.Meshes[item.Geometry.Id];
    scene.SetItems([item with{}]);Check(scene.Revision==revision);
    scene.SetItems([item with{Selected=true,Color=new(1,0,0)}]);Check(scene.Revision>revision&&scene.MeshRevision==mr&&ReferenceEquals(mesh,scene.Meshes[item.Geometry.Id]));
    Check(scene.MeshBuildCount==1);
});
Test("quality edits invalidate meshes and invalid updates retain old settings",()=>{
    var scene=new DisplayScene();var item=new DisplayItem("s",factory.Saddle(),new(.3f,.5f,.7f));scene.SetItems([item]);scene.SurfaceSegments=64;
    Check(scene.Meshes[item.Geometry.Id].Indices.Length==64*64*6&&scene.MeshBuildCount==2);
    var revision=scene.Revision;Throws<ArgumentOutOfRangeException>(()=>scene.SurfaceSegments=1000);Check(scene.SurfaceSegments==64&&scene.Revision==revision);
    Throws<ArgumentOutOfRangeException>(()=>scene.CurveTolerance=double.NaN);Check(scene.CurveTolerance==.02);
    Throws<ArgumentException>(()=>scene.SetItems([item,item]));Check(scene.Items.Count==1&&scene.Revision==revision);
});
Test("origin rebasing retains doubles and indices without re-evaluation or drift",()=>{
    var scalar=new ScalarOnlyEvaluator(evaluator);var s=new GeometryFactory(scalar).Saddle().Transform(Transform3.Translation(new(1e9,-2e9,3e9)));var scene=new DisplayScene();scene.SetItems([new("s",s,new(1,0,0))]);var calls=scalar.Calls;var source=scene.Meshes[s.Id];
    for(var i=0;i<10;i++){scene.Rebase(new(1e9+i,-2e9,3e9));scene.Rebase(Vec3.Zero);}
    Check(scalar.Calls==calls&&scene.MeshBuildCount==1&&scene.MeshRebaseCount==20);
    var result=scene.Meshes[s.Id];Check(ReferenceEquals(source.Positions,result.Positions)&&ReferenceEquals(source.Indices,result.Indices));
    Check(source.Vertices.SequenceEqual(result.Vertices)&&source.Lines.SequenceEqual(result.Lines));
});
Test("span and grid evaluators are concurrently reusable",()=>{
    var s=factory.Saddle();Parallel.For(0,50,i=>{
        var uv=(i+1)/51d;Span<Vec3> d=stackalloc Vec3[9];evaluator.EvaluateInto(s,uv,.41,2,2,d);Near(d[0],s.PointAt(uv,.41));
        var jets=new SurfaceJet[4];evaluator.EvaluateGrid(s,[0,uv],[.41,1],jets);Near(jets[2].Point,d[0]);
    });
});
Test("two-level picking agrees with independent exhaustive plane/barycentric tests",()=>{
    var scene=new DisplayScene{SurfaceSegments=20};
    scene.SetItems(Enumerable.Range(0,16).Select(i=>new DisplayItem("s"+i,factory.Saddle().Transform(Transform3.Translation(new(i%4*70,i/4*55,i%3))),new(1,0,0))));
    var random=new Random(2831);
    for(var sample=0;sample<200;sample++){
        var ray=new Ray3(new(random.NextDouble()*300-40,random.NextDouble()*220-30,150),-Vec3.UnitZ);
        var indexed=MeshPicker.PickRay(scene,ray,out _);double best=double.PositiveInfinity;string? id=null;
        foreach(var item in scene.Items){var mesh=scene.Meshes[item.Geometry.Id];for(var t=0;t<mesh.Indices.Length;t+=3){
            var a=mesh.Positions[mesh.Indices[t]];var b=mesh.Positions[mesh.Indices[t+1]];var c=mesh.Positions[mesh.Indices[t+2]];
            var u=b-a;var v=c-a;var normal=Vec3.Cross(u,v);var denominator=Vec3.Dot(normal,ray.Direction);if(denominator==0)continue;
            var distance=Vec3.Dot(normal,a-ray.Origin)/denominator;if(distance<0||distance>=best)continue;
            var w=ray.Origin+ray.Direction*distance-a;var uu=Vec3.Dot(u,u);var vv=Vec3.Dot(v,v);var uv=Vec3.Dot(u,v);var wu=Vec3.Dot(w,u);var wv=Vec3.Dot(w,v);var d=uu*vv-uv*uv;
            var s=(vv*wu-uv*wv)/d;var r=(uu*wv-uv*wu)/d;
            if(s>=0&&r>=0&&s+r<=1){best=distance;id=item.Id;}
        }}
        Check(indexed?.ObjectId==id);if(indexed is not null)Check(Math.Abs(indexed.Distance-best)<1e-8);
    }
});
Test("picking has bounded work on 115200 triangles and reuses warm indices",()=>{
    var scene=new DisplayScene{SurfaceSegments=48};scene.SetItems(Enumerable.Range(0,25).Select(i=>new DisplayItem("s"+i,factory.Saddle().Transform(Transform3.Translation(new((i%5-2)*80,(i/5-2)*60,0))),new(1,0,0))));
    var ray=new Ray3(new(.731,.293,200),-Vec3.UnitZ);var hit=MeshPicker.PickRay(scene,ray,out var cold);Check(hit is not null&&cold.BuiltTriangleIndices==1);
    MeshPicker.PickRay(scene,ray,out var warm);Check(warm.BuiltTriangleIndices==0&&warm.TriangleTests<128&&warm.BoxTests<150,$"{warm}");
    scene.Rebase(new(1e6,2e6,3e6));var after=MeshPicker.PickRay(scene,ray,out var rebase);Check(rebase.BuiltTriangleIndices==0);Near(after!.Point,hit!.Point);
    MeshPicker.PickRay(scene,new(new(1e5,1e5,200),-Vec3.UnitZ),out var miss);Check(miss.TriangleTests==0);
});
Test("shared geometry tie-breaking and lock edits are deterministic",()=>{
    var s=factory.Saddle();var items=Enumerable.Range(0,20).Select(i=>new DisplayItem("s"+i,s,new(1,0,0))).ToArray();var scene=new DisplayScene();scene.SetItems(items);var ray=new Ray3(new(.371,.127,200),-Vec3.UnitZ);
    Check(MeshPicker.PickRay(scene,ray,out _)?.ObjectId=="s0");items[0]=items[0] with{Locked=true};scene.SetItems(items);Check(MeshPicker.PickRay(scene,ray,out _)?.ObjectId=="s1");
    scene.ClipPlane=(Vec3.UnitZ,-1000);Check(MeshPicker.PickRay(scene,ray,out _) is null);
});
Test("curve picking respects clipping and perspective-correct segment position",()=>{
    var scene=new DisplayScene();var c=factory.Line(new(5,-2,0),new(-10,8,0));scene.SetItems([new("line",c,new(1,0,0))]);
    var camera=new Camera{Target=Vec3.Zero,Distance=10,Azimuth=0,Elevation=0};var view=new ViewportRectangle("Perspective",0,0,800,600,camera);
    var a=camera.Project(c.PointAt(0),800,600);var b=camera.Project(c.PointAt(1),800,600);var x=(a.X+b.X)/2;var y=(a.Y+b.Y)/2;
    var hit=MeshPicker.Pick(scene,view,x,y,out var stats);Check(hit is not null&&stats.CurveSegmentTests==1);Near(hit!.Point,new(2,0,0));
    var p=camera.Project(hit.Point,800,600);Check(Math.Abs(p.X-x)<1e-8&&Math.Abs(p.Y-y)<1e-8);
    scene.ClipPlane=(Vec3.UnitZ,-1);Check(MeshPicker.Pick(scene,view,x,y) is null);
});
Test("triangle index handles tiny primitives, planar bounds and invalid rays",()=>{
    var index=new TrianglePickIndex([Vec3.Zero,new(1e-8,0,0),new(0,1e-8,0)],[0,1,2]);
    Check(index.Intersect(new(new(2e-9,2e-9,1),new(0,-0d,-2)),out _) is not null);
    Check(index.Intersect(new(new(2e-9,2e-9,1),Vec3.UnitX),out _) is null);
    Throws<ArgumentException>(()=>index.Intersect(new(Vec3.Zero,Vec3.Zero),out _));
    Throws<ArgumentException>(()=>new TrianglePickIndex([Vec3.Zero],[0,1,2]));
    Throws<ArgumentException>(()=>new TrianglePickIndex([Vec3.Zero],[0,0]));
});
Test("quality rebuild changes index but metadata edits do not",()=>{
    var scene=new DisplayScene();var s=factory.Saddle();var item=new DisplayItem("s",s,new(1,0,0));scene.SetItems([item]);var ray=new Ray3(new(.37,.23,200),-Vec3.UnitZ);
    MeshPicker.PickRay(scene,ray,out _);scene.SetItems([item with{Selected=true}]);MeshPicker.PickRay(scene,ray,out var unchanged);Check(unchanged.BuiltTriangleIndices==0);
    scene.SurfaceSegments=40;MeshPicker.PickRay(scene,ray,out var rebuilt);Check(rebuilt.BuiltTriangleIndices==1);
});
Test("analytic rational derivatives above polynomial degree through order 32",()=>{
    var c=new NurbsCurve(evaluator,1,[0,0,1,1],[HPoint.FromPoint(Vec3.Zero),HPoint.FromPoint(Vec3.UnitX,2)]);
    var derivatives=c.Derivatives(.3,32);double factorial=1;
    for(var k=1;k<=32;k++){factorial*=k;var expected=(k%2==0?-1:1)*2*factorial/Math.Pow(1.3,k+1);Check(Math.Abs(derivatives[k].X-expected)<=1e-10*Math.Abs(expected));}
    var cp=new[]{HPoint.FromPoint(new(0,0,0),1),HPoint.FromPoint(new(0,1,0),3),HPoint.FromPoint(new(1,0,0),2),HPoint.FromPoint(new(1,1,1),6)};
    var surface=new NurbsSurface(evaluator,1,1,2,2,[0,0,1,1],[0,0,1,1],cp);var d=surface.Derivatives(.3,.4,6,6);
    double D(double t,int k,double weight){if(k==0)return weight*t/(1+(weight-1)*t);double f=1;for(var j=2;j<=k;j++)f*=j;return (k%2==0?-1:1)*weight*Math.Pow(weight-1,k-1)*f/Math.Pow(1+(weight-1)*t,k+1);}
    for(var a=0;a<=6;a++)for(var b=0;b<=6;b++)Near(d[a,b],new(b==0?D(.3,a,2):0,a==0?D(.4,b,3):0,D(.3,a,2)*D(.4,b,3)),1e-9);
});
int failed=0;foreach(var(name,action)in tests)try{action();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="performance-contracts",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;

sealed class ScalarOnlyEvaluator(INurbsEvaluator inner):INurbsEvaluator
{
    public int Calls {get;private set;}
    public string Implementation=>"Legacy contract test";
    public Vec3[] Evaluate(NurbsCurve c,double t,int n=0){Calls++;return inner.Evaluate(c,t,n);}
    public Vec3[,] Evaluate(NurbsSurface s,double u,double v,int a=0,int b=0){Calls++;return inner.Evaluate(s,u,v,a,b);}
}
