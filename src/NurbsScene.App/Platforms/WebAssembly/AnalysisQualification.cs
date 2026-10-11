#if NURBSSCENE_QUALIFICATION
using NurbsScene.Controls;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.IO;

namespace NurbsScene.App;

public partial class App
{
    private static async Task VerifySurfaceAnalysisAsync(WorkspaceView view,Action<bool,string> check)
    {
        var controller=view.Controller;var f=controller.Kernel;
        var s=f.Extrude(f.Circle(new(2,3,4),3),new(0,0,7));var r=s.GetAreaProperties();
        check(r.Converged&&Math.Abs(r.Area-42*Math.PI)<1e-6&&r.Centroid is{} p&&(p-new Vec3(2,3,7.5)).Length<1e-6,
            "Analytic NURBS area and centroid execute in the real managed browser");
        var cached=s.GetAreaProperties();
        check(cached.CacheHit&&cached.Evaluations==0&&cached.Area==r.Area,"Repeated surface analysis reuses a complete cached measurement");
        var graph=new Graph(controller.Registry);
        graph.Add("SurfaceArea",parameters:new Dictionary<string,object?>{{"input:S",s}});
        graph.Add("SurfaceCentroid",parameters:new Dictionary<string,object?>{{"input:S",s}});
        check((await graph.SolveAsync()).ErrorCount==0,"Area and centroid components execute in managed WebAssembly");
        var loaded=WorkspaceFormat.Deserialize(f.Evaluator,controller.Registry,WorkspaceFormat.Serialize(new(),graph));
        check((await loaded.Graph.SolveAsync()).ErrorCount==0,"Area definition preserves exact geometry through workspace serialization");
        var zero=f.FromCorners(Vec3.Zero,Vec3.Zero,Vec3.Zero,Vec3.Zero).GetAreaProperties();
        check(zero.Converged&&zero.Area==0&&zero.Centroid is null,"Zero-area surface does not invent a centroid");
        var currentSelection=controller.Document.Selection.ToArray();var count=controller.Document.Objects.Count;string id="";
        controller.Document.Transaction("Analysis qualification surface",()=>id=controller.Document.Add(s,"Area test").Id);
        controller.Document.Select([id]);var revision=controller.Document.Revision;
        controller.Execute("SurfaceArea");controller.Execute("SurfaceCentroid");
        check(controller.Document.Revision==revision&&controller.Document.Objects.Count==count+1,"Actual Uno measurement commands are read-only");
        controller.Execute("Undo");controller.Document.Select(currentSelection);
        check(controller.Document.Objects.Count==count,"Analysis qualification restores the application document");
    }
}
#endif
