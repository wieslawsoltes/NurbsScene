using System.Reflection;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Rendering;
using NurbsScene.Model;
using NurbsScene.Workbench;

var kernel = new GeometryFactory(new ManagedNurbsEvaluator());
var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Near(Vec3 a, Vec3 b, double tolerance = 1e-7) => Check((a-b).Length <= tolerance, $"{a} != {b}");
void Throws<T>(Action action) where T : Exception { try { action(); } catch(T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
var top = new Camera("Top") {Target=Vec3.Zero, Distance=10, Scale=40};
var view = new ViewportRectangle("Top",0,0,800,800,top);
NurbsSurface Plane(double z) => kernel.FromCorners(new(-5,-5,z),new(5,-5,z),new(5,5,z),new(-5,5,z));
Test("curve crossing the eye plane keeps its visible selectable portion", () => {
    var camera = new Camera {Target=Vec3.Zero,Distance=10,Azimuth=0,Elevation=0};
    var v = new ViewportRectangle("Perspective",0,0,800,800,camera);
    var c = kernel.Line(new(12,-1,0),new(0,1,0));
    var scene = new DisplayScene(); scene.SetItems([new("crossing",c,new(1,0,0))]);
    var p = c.PointAt(.75); var screen = camera.Project(p,800,800);
    var hit = MeshPicker.Pick(scene,v,screen.X,screen.Y);
    Check(hit is not null, "Visible curve was dropped because one endpoint is behind the eye"); Near(hit!.Point,p);
});
Test("clip-plane boundary participates in screen-space curve tolerance", () => {
    var scene = new DisplayScene {ClipPlane=(Vec3.UnitX,0)};
    scene.SetItems([new("line",kernel.Line(new(-10,0,0),new(10,0,0)),new(1,0,0))]);
    var p = top.Project(new(-.2,0,0),800,800);
    var hit = MeshPicker.Pick(scene,view,p.X,p.Y);
    Check(hit is not null, "Visible clipped endpoint lies within seven pixels"); Near(hit!.Point,Vec3.Zero);
});
Test("surface before near plane does not occlude selectable visible surface", () => {
    var scene = new DisplayScene();
    scene.SetItems([new("before-near",Plane(9.99999),new(1,0,0)),new("visible",Plane(0),new(0,1,0))]);
    Check(MeshPicker.Pick(scene,view,401,403)?.ObjectId=="visible");
});
Test("viewport picking excludes surface and curve beyond the far plane", () => {
    var scene = new DisplayScene(); scene.SetItems([new("far-surface",Plane(-20000),new(1,0,0))]);
    Check(MeshPicker.Pick(scene,view,401,403) is null);
    scene.SetItems([new("far-curve",kernel.Line(new(-5,0,-20000),new(5,0,-20000)),new(1,0,0))]);
    Check(MeshPicker.Pick(scene,view,400,400) is null);
});
Test("subpixel viewports never produce negative or out-of-range rectangles", () => {
    var cameras=new[]{"Top","Perspective","Front","Right"}.ToDictionary(n=>n,n=>new Camera(n));
    for(int w=0;w<8;w++) for(int h=0;h<8;h++) foreach(var r in ViewportLayout.Create(w,h,cameras))
        Check(r.X>=0&&r.Y>=0&&r.Width>=0&&r.Height>=0&&r.X+r.Width<=w&&r.Y+r.Height<=h,$"{w}x{h}: {r}");
});
Test("different geometry cannot reuse old meshes by spoofing a previous identity", () => {
    var scene=new DisplayScene();var c=kernel.Circle(Vec3.Zero,2);scene.SetItems([new("old",c,new(1,0,0))]);
    var revision=scene.Revision;
    Throws<NotSupportedException>(()=>scene.SetItems([new("new",new UnsupportedGeometry(c.Id),new(1,0,0))]));
    Check(scene.Revision==revision&&ReferenceEquals(scene.Items[0].Geometry,c));
});
Test("nonfinite render metadata fails without changing a published scene", () => {
    var scene=new DisplayScene();var item=new DisplayItem("s",Plane(0),new(1,0,0));scene.SetItems([item]);var revision=scene.Revision;
    Throws<ArgumentException>(()=>scene.SetItems([item with {Color=new(float.NaN,0,0)}]));
    Check(scene.Revision==revision&&scene.Items[0]==item);
});
Test("surface display samples narrow nonuniform knot spans", () => {
    double[] u=[0,0,0,.123,.123,.124,.124,1,1,1];double[] height=[0,0,0,10,0,0,0];
    var cp=Enumerable.Range(0,7).SelectMany(i=>Enumerable.Range(0,2).Select(j=>HPoint.FromPoint(new(i,j,height[i]))));
    var s=new NurbsSurface(kernel.Evaluator,2,1,7,2,u,[0,0,1,1],cp);
    var mesh=MeshBuilder.Build(s,Vec3.Zero,32);
    Check(mesh.Positions.Max(p=>p.Z)>4.9,"Uniform global sampling completely missed the local hump");
});
Test("display record clones cannot inherit a picking index for other vertex arrays", () => {
    var mesh=MeshBuilder.Build(Plane(0),Vec3.Zero,8);
    var method=typeof(DisplayMesh).GetMethod("GetPickingIndex",BindingFlags.Instance|BindingFlags.NonPublic)!;
    TrianglePickIndex Index(DisplayMesh m) => (TrianglePickIndex)method.Invoke(m,[false])!;
    var original=Index(mesh);
    var changed=mesh with{Positions=mesh.Positions.Select(p=>p+new Vec3(0,0,5)).ToArray()};
    var index=Index(changed);Check(!ReferenceEquals(original,index));
    Near(index.Intersect(new(new(.1,.2,10),-Vec3.UnitZ),out _)!.Value.Point,new(.1,.2,5));
    var rebase=MeshBuilder.Rebase(mesh,new(100,200,300));Check(ReferenceEquals(original,Index(rebase)));
});
Test("cached picking index hits do not allocate delegates", () => {
    // Reflection is deliberately excluded from the measured path: scene picking
    // should allocate only the returned PickResult (56 bytes on x64), not a closure.
    var scene=new DisplayScene();scene.SetItems([new("s",Plane(0),new(1,0,0))]);var ray=new Ray3(new(.1,.2,10),-Vec3.UnitZ);
    for(var i=0;i<100;i++)MeshPicker.PickRay(scene,ray,out _);
    var allocated=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)MeshPicker.PickRay(scene,ray,out _);
    var bytes=(GC.GetAllocatedBytesForCurrentThread()-allocated)/1000;
    System.Console.WriteLine($"METRIC warm-pick-bytes={bytes}");
    Check(bytes<=64,$"Warm picking allocated {bytes} bytes/query");
});
Test("grid validates budgets and cannot loop on invalid or overflowing integer steps", () => {
    Throws<ArgumentOutOfRangeException>(()=>MeshBuilder.Grid(Vec3.Zero,"Top",10,0));
    Throws<ArgumentOutOfRangeException>(()=>MeshBuilder.Grid(Vec3.Zero,"Top",10,-1));
    Throws<ArgumentException>(()=>MeshBuilder.Grid(Vec3.Zero,"Top",int.MaxValue,1));
    Check(MeshBuilder.Grid(Vec3.Zero,"Top",int.MaxValue,int.MaxValue).Length==36);
    Throws<ArithmeticException>(()=>MeshBuilder.Grid(new(1e100,0,0),"Top"));
});
Test("invalid clip planes and camera parameters fail explicitly", () => {
    var scene=new DisplayScene{ClipPlane=(Vec3.UnitX,2)};
    Throws<ArgumentException>(()=>scene.ClipPlane=(Vec3.Zero,1));
    Throws<ArgumentException>(()=>scene.ClipPlane=(Vec3.UnitX,double.NaN));
    Check(scene.ClipPlane==(Vec3.UnitX,2));
    var camera=new Camera{Distance=double.NaN};Throws<ArgumentException>(()=>camera.Ray(1,1,800,800));
    camera.Distance=10;Throws<ArgumentException>(()=>camera.Ray(1,1,0,800));
    camera.FieldOfView=Math.PI;Throws<ArgumentException>(()=>camera.Matrix(800,800,Vec3.Zero));
});
Test("ray interval finds farther visible triangles in the same mesh", () => {
    var c=kernel.Polyline([new(-1,0,9.99999),new(1,0,9.99999),new(1,0,0),new(-1,0,0)]);
    var surface=kernel.Extrude(c,new(0,2,0));var scene=new DisplayScene();scene.SetItems([new("fold",surface,new(1,0,0))]);
    var hit=MeshPicker.Pick(scene,view,400,380);
    Check(hit is not null&&Math.Abs(hit.Point.Z)<1e-9,"Near-clipped face prevented the farther face from being selected");
});
Test("disconnected surface display fails atomically instead of bridging gaps", () => {
    var cp=new[]{0d,1d,50d,51d}.SelectMany(x=>new[]{HPoint.FromPoint(new(x,0,0)),HPoint.FromPoint(new(x,1,0))});
    var s=new NurbsSurface(kernel.Evaluator,1,1,4,2,[0,0,.5,.5,1,1],[0,0,1,1],cp);
    var scene=new DisplayScene();var prior=new DisplayItem("p",Plane(0),new(1,0,0));scene.SetItems([prior]);var revision=scene.Revision;
    Throws<NotSupportedException>(()=>scene.SetItems([new("gap",s,new(1,0,0))]));Check(scene.Revision==revision&&scene.Items[0]==prior);
});
Test("knot-aware batched and legacy evaluation produce identical display meshes", () => {
    var controls=Enumerable.Range(0,10).Select(i=>HPoint.FromPoint(new(i/2,i%2,i==4?8:0),1+i*.1)).ToArray();
    double[] knots=[0,0,0,.12,.19,1,1,1];
    var fast=new NurbsSurface(kernel.Evaluator,2,1,5,2,knots,[0,0,1,1],controls);
    var legacy=new NurbsSurface(new LegacyEvaluator(kernel.Evaluator),2,1,5,2,knots,[0,0,1,1],controls);
    var a=MeshBuilder.Build(fast,Vec3.Zero,16);var b=MeshBuilder.Build(legacy,Vec3.Zero,16);
    Check(a.Indices.SequenceEqual(b.Indices)&&a.LineIndices!.SequenceEqual(b.LineIndices!));
    Check(a.Positions.Length==b.Positions.Length);for(var i=0;i<a.Positions.Length;i++)Near(a.Positions[i],b.Positions[i],1e-10);
});
Test("document views are reused but remain immutable snapshots across changes and rollback", () => {
    var doc=new SceneDocument();var item=doc.Add(Plane(0),"p");doc.Select([item.Id]);
    var objects=doc.Objects;var layers=doc.Layers;var selection=doc.Selection;
    Check(ReferenceEquals(objects,doc.Objects)&&ReferenceEquals(layers,doc.Layers)&&ReferenceEquals(selection,doc.Selection));
    Throws<NotSupportedException>(()=>((IList<SceneObject>)objects).Clear());
    Throws<NotSupportedException>(()=>((ISet<string>)selection).Clear());
    doc.Add(Plane(2),"q");doc.Select([]);Check(objects.Count==1&&selection.Contains(item.Id)&&doc.Objects.Count==2);
    Throws<InvalidOperationException>(()=>doc.Transaction("failure",()=>{doc.Select([item.Id]);_ =doc.Selection;doc.Delete([item.Id]);_ =doc.Objects;throw new InvalidOperationException();}));
    Check(doc.Objects.Count==2&&doc.Selection.Count==0&&objects.Count==1&&selection.Count==1);
});
Test("selected workspace refresh has a linear allocation budget", () => {
    using var workspace=new WorkspaceController();var s=Plane(0);
    workspace.Document.Transaction("populate",()=>{for(var i=0;i<600;i++)workspace.Document.Add(s,"item");});
    workspace.Document.Select(workspace.Document.Objects.Select(o=>o.Id));workspace.Refresh();
    var before=GC.GetAllocatedBytesForCurrentThread();workspace.Refresh();var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
    System.Console.WriteLine($"METRIC refresh-600-selection-bytes={bytes}");
    Check(bytes<512_000,$"Refresh allocated {bytes} bytes for 600 shared-geometry selections");
    Check(workspace.Scene.Items.All(i=>i.Selected));
});
Test("warming a display index does not change record equality or hash keys", () => {
    var mesh=MeshBuilder.Build(Plane(0),Vec3.Zero,8);var copy=mesh with{};
    var hash=mesh.GetHashCode();var keys=new HashSet<DisplayMesh>{mesh};
    var method=typeof(DisplayMesh).GetMethod("GetPickingIndex",BindingFlags.Instance|BindingFlags.NonPublic)!;
    _ =method.Invoke(mesh,[false]);
    Check(mesh.GetHashCode()==hash&&keys.Contains(mesh)&&mesh.Equals(copy),"A private cache altered record value semantics");
});
Test("ValueTask transactions are rejected before invoking their action", () => {
    var document=new SceneDocument();var called=false;
    Throws<ArgumentException>(()=>document.Transaction("generic",()=>{called=true;return new ValueTask<int>(3);}));
    Check(!called);
    Throws<ArgumentException>(()=>document.Transaction("nongeneric",()=>{called=true;return ValueTask.CompletedTask;}));
    Check(!called&&!document.CanUndo);
});
int failed=0;
foreach(var (name,run) in tests)try{run();System.Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;System.Console.WriteLine("FAIL "+name+"\n"+ex);}
System.Console.WriteLine(JsonSerializer.Serialize(new{suite="review-regressions",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
sealed record UnsupportedGeometry(Guid Id):IGeometry
{
    public Bounds3 Bounds=>new(Vec3.Zero,Vec3.UnitX);
    public IGeometry Transform(Transform3 transform)=>this;
}

sealed class LegacyEvaluator(INurbsEvaluator inner):INurbsEvaluator
{
    public string Implementation=>"Legacy review consumer";
    public Vec3[] Evaluate(NurbsCurve curve,double t,int n=0)=>inner.Evaluate(curve,t,n);
    public Vec3[,] Evaluate(NurbsSurface surface,double u,double v,int a=0,int b=0)=>inner.Evaluate(surface,u,v,a,b);
}
