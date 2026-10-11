using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Workbench;

var kernel=new GeometryFactory(new ManagedNurbsEvaluator());
var registry=StandardComponents.Create(kernel);
var surface=kernel.Saddle();var circle=kernel.Circle(Vec3.Zero,3);
var tests=new List<(string,Func<Task>)>();
void Check(bool ok,string message="Assertion failed"){if(!ok)throw new Exception(message);}
void Near(double a,double b,double tolerance=1e-8)=>Check(Math.Abs(a-b)<=tolerance*Math.Max(1,Math.Abs(b)), $"{a:G17} != {b:G17}");
void Point(Vec3 a,Vec3 b)=>Check((a-b).Length<1e-8);
void Test(string name,Action action)=>tests.Add((name,()=>{action();return Task.CompletedTask;}));
void AsyncTest(string name,Func<Task> action)=>tests.Add((name,action));
void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
async Task<NodeResult> Evaluate(string component,Dictionary<string,object?>? extra=null)
{
    var graph=new Graph(registry);var parameters=new Dictionary<string,object?>();
    foreach(var p in registry.Get(component).Inputs)
    {
        if(p.Kind==ValueKind.Curve)parameters["input:"+p.Name]=circle;
        if(p.Kind==ValueKind.Surface)parameters["input:"+p.Name]=surface;
    }
    if(component=="RefineSurfaceKnots")parameters["input:Knots"]=new object?[]{.2,.3,.3,.8};
    if(extra is not null)foreach(var item in extra)parameters[item.Key]=item.Value;
    var node=graph.Add(component,parameters:parameters);var result=await graph.SolveAsync();
    Check(result.ErrorCount==0,string.Join(';',result.Nodes[node.Id].Messages));
    return result.Nodes[node.Id];
}
string[] added=["CurveLength","LengthParameter","DivideCurve","InsertSurfaceKnot","RefineSurfaceKnots","ElevateSurfaceDegree","ReverseSurface","SplitSurface","SubSurface","ReparameterizeSurface","SurfaceControlPoint","SurfaceBoundaries","SurfaceBezierPatches","SurfaceControlNet","SurfaceInfo","CurveInfo"];
foreach(var name in added)AsyncTest("Executable component: "+name,async()=>{var result=await Evaluate(name);Check(result.Outputs.Values.All(t=>t.ItemCount>0));});
AsyncTest("graph arc length and equal-distance division",async()=>{
    var measured=await Evaluate("CurveLength");Near(Convert.ToDouble(measured.Outputs["L"].AllItems.Single()),6*Math.PI);
    var divided=await Evaluate("DivideCurve",new(){{"input:N",13d}});var times=divided.Outputs["T"].AllItems.Cast<double>().ToArray();
    for(var i=1;i<times.Length;i++)Near(circle.Measure(new(times[i-1],times[i])).Length,6*Math.PI/13);
});
AsyncTest("surface graph preserves geometry and data-tree lanes",async()=>{
    var inserted=(NurbsSurface)(await Evaluate("InsertSurfaceKnot")).Outputs["S"].AllItems.Single()!;
    Point(inserted.PointAt(.123,.678),surface.PointAt(.123,.678));Check(inserted.CountU==surface.CountU+1);
    var net=await Evaluate("SurfaceControlNet");Check(net.Outputs["P"].Branches.Length==surface.CountU);Check(net.Outputs["W"].ItemCount==surface.CountU*surface.CountV);
    var edit=(NurbsSurface)(await Evaluate("SurfaceControlPoint",new(){{"input:I",2d},{"input:J",1d},{"input:P",new Vec3(2,4,6)},{"input:W",2d}})).Outputs["S"].AllItems.Single()!;
    Point(edit.ControlPoints[2*surface.CountV+1].Position,new(2,4,6));Near(edit.ControlPoints[2*surface.CountV+1].W,2);
});
AsyncTest("surface direction errors block dependent components",async()=>{
    var g=new Graph(registry);var a=g.Add("ReverseSurface",parameters:new Dictionary<string,object?>{{"input:S",surface},{"input:D",2d}});var b=g.Add("SurfaceBoundaries");g.Connect(new(a.Id,"S"),new(b.Id,"S"));
    var r=await g.SolveAsync();Check(r.Nodes[a.Id].Status=="error");Check(r.Nodes[b.Id].Status=="blocked");
});
Test("surface commands preserve exact geometry and undo",()=>{
    using var workspace=new WorkspaceController();var item=workspace.Document.Add(surface,"S");workspace.Document.Select([item.Id]);
    workspace.Execute("InsertSurfaceKnot U 0.37 2");var inserted=(NurbsSurface)workspace.Document.GetObject(item.Id).Geometry;
    Check(inserted.CountU==surface.CountU+2);Point(inserted.PointAt(.13,.77),surface.PointAt(.13,.77));
    workspace.Execute("Undo");Check(ReferenceEquals(workspace.Document.GetObject(item.Id).Geometry,surface));workspace.Execute("Redo");
    workspace.Execute("ElevateSurfaceDegree V 1");Check(((NurbsSurface)workspace.Document.GetObject(item.Id).Geometry).DegreeV==surface.DegreeV+1);
});
Test("surface split is one undoable multi-object edit",()=>{
    using var workspace=new WorkspaceController();var item=workspace.Document.Add(surface,"S");workspace.Document.Select([item.Id]);
    workspace.Execute("SplitSurface U .4");Check(workspace.Document.Objects.Count==2);Check(workspace.Document.Selection.Count==2);
    var lower=(NurbsSurface)workspace.Document.Objects[0].Geometry;var upper=(NurbsSurface)workspace.Document.Objects[1].Geometry;
    Near(lower.DomainU.Max,.4);Near(upper.DomainU.Min,.4);Point(lower.PointAt(.4,.73),upper.PointAt(.4,.73));
    workspace.Execute("Undo");Check(workspace.Document.Objects.Count==1&&workspace.Document.Objects[0].Id==item.Id);
});
Test("rectangular trim control-point edit and boundary extraction",()=>{
    using var workspace=new WorkspaceController();var item=workspace.Document.Add(surface,"S");workspace.Document.Select([item.Id]);
    workspace.Execute("TrimSurface .2 .8 .3 .7");var trimmed=(NurbsSurface)workspace.Document.GetObject(item.Id).Geometry;
    Point(trimmed.PointAt(.34,.56),surface.PointAt(.34,.56));workspace.Execute("Undo");
    workspace.Execute("SurfaceControlPoint 1 2 3,4,5 2");var edited=(NurbsSurface)workspace.Document.GetObject(item.Id).Geometry;
    Point(edited.ControlPoints[6].Position,new(3,4,5));Near(edited.ControlPoints[6].W,2);workspace.Execute("Undo");
    workspace.Execute("SurfaceBoundaries");Check(workspace.Document.Objects.Count==5);Check(workspace.Document.Objects.Count(o=>o.Geometry is NurbsCurve)==4);
});
Test("invalid multi-selection commands do not partially mutate the document",()=>{
    using var workspace=new WorkspaceController();var a=workspace.Document.Add(surface,"A");var b=workspace.Document.Add(surface.Reparameterize(new(5,8),new(0,1)),"B");
    workspace.Document.Select([a.Id,b.Id]);var revision=workspace.Document.Revision;
    Throws<ArgumentOutOfRangeException>(()=>workspace.Execute("InsertSurfaceKnot U .5"));
    Check(workspace.Document.Revision==revision);Check(ReferenceEquals(workspace.Document.GetObject(a.Id).Geometry,surface));Check(!workspace.Document.CanUndo);
});
Test("curve-length command reports without creating history",()=>{
    using var workspace=new WorkspaceController();var item=workspace.Document.Add(circle,"Circle");workspace.Document.Select([item.Id]);var messages=new List<string>();workspace.Message+=messages.Add;
    var revision=workspace.Document.Revision;workspace.Execute("CurveLength");Check(messages.Any(m=>m.Contains("estimated integration error")));Check(revision==workspace.Document.Revision);Check(!workspace.Document.CanUndo);
});
int failed=0;foreach(var(name,action)in tests)try{await action();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="geometry-components-and-workbench",total=tests.Count,passed=tests.Count-failed,failed,definitions=registry.Definitions.Count}));
return failed==0?0:1;
