#if NURBSSCENE_QUALIFICATION
using NurbsScene.Controls;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.IO;

namespace NurbsScene.App;

public partial class App
{
    private static async Task VerifySectionsAsync(WorkspaceView view,Action<bool,string> check)
    {
        var controller=view.Controller;var kernel=controller.Kernel;
        var circle=kernel.Circle(Vec3.Zero,3);var plane=new Plane3(new(1,0,0),Vec3.UnitX);
        var section=circle.IntersectPlane(plane);
        check(section.Complete&&section.Points.Length==2&&section.Points.All(p=>p.Residual<=1e-7),"Rational curve-plane sections execute on WebAssembly");
        check(circle.IntersectPlane(new(Vec3.Zero,Vec3.UnitZ)).Overlaps.Length==1,"Coplanar overlap is not substituted by isolated roots");
        var g=new Graph(controller.Registry);
        var n=g.Add("CurvePlaneSection",parameters:new Dictionary<string,object?>{{"input:C",circle},{"input:Origin",plane.Origin},{"input:Normal",plane.Normal}});
        var shatter=g.Add("ShatterCurve",parameters:new Dictionary<string,object?>{{"input:C",circle}});
        var result=await g.SolveAsync();
        check(result.ErrorCount==0&&result.Nodes[shatter.Id].Outputs["C"].ItemCount==2,"Section and shatter components execute on WebAssembly");
        var builds=g.ExecutionPlanBuilds;var cached=await g.SolveAsync();
        check(cached.Cached==2&&ReferenceEquals(result.Nodes,cached.Nodes)&&g.ExecutionPlanBuilds==builds,"Unchanged managed graph reuses plan and immutable results");
        var roundtrip=WorkspaceFormat.Deserialize(kernel.Evaluator,controller.Registry,WorkspaceFormat.Serialize(new(),g));
        check((await roundtrip.Graph.SolveAsync()).ErrorCount==0,"Section definition survives exact workspace round trip");

        // Exercise the application's actual controller and undo stack, restoring
        // both geometry and selection before the existing framebuffer checks.
        var count=controller.Document.Objects.Count;var selection=controller.Document.Selection.ToArray();
        string id="";
        controller.Document.Transaction("Section qualification curve",()=>id=controller.Document.Add(circle,"Section test").Id);
        controller.Document.Select([id]);controller.Execute("SplitCurve .25 .5");
        check(controller.Document.Objects.Count==count+3&&controller.Document.Selection.Count==3,"Actual Uno controller splits selected curves at parameters");
        controller.Execute("Undo");controller.Execute("Undo");controller.Document.Select(selection);
        check(controller.Document.Objects.Count==count,"Section editing undo restores the actual application document");
    }
}
#endif
