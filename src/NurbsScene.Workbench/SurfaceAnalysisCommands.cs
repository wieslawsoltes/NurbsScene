using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Workbench;

public sealed partial class WorkspaceController
{
    private bool TrySurfaceAnalysisCommand(string command,string[] args)
    {
        if(command is not("surfacearea" or "surfacecentroid"))return false;
        if(args.Length>1)throw new ArgumentException("SurfaceArea/SurfaceCentroid [absolute area tolerance in squared model units]");
        var source=Selected();
        if(source.Length==0||source.Any(o=>o.Geometry is not NurbsSurface))throw new ArgumentException("Select NURBS surfaces first.");
        var settings=new SurfaceAreaOptions(AbsoluteTolerance:args.Length==1?Number(args[0]):1e-8);
        var results=source.Select(o=>(o.Name,Result:((NurbsSurface)o.Geometry).GetAreaProperties(settings))).ToArray();
        if(results.Any(r=>!r.Result.Converged))throw new ArithmeticException("Surface integration did not converge; no measurement report or geometry change was applied.");
        if(command=="surfacecentroid"&&results.Any(r=>r.Result.Centroid is null))throw new ArithmeticException("A selected surface has zero area and no centroid.");
        foreach(var (name,r) in results)
        {
            Report(FormattableString.Invariant($"{name}: area {r.Area:G17} {Document.Units}^2; estimated integration error {r.EstimatedAreaError:G6}."));
            if(command=="surfacecentroid"&&r.Centroid is{} p)
                Report(FormattableString.Invariant($"{name}: area centroid {p.X:G17},{p.Y:G17},{p.Z:G17} {Document.Units}."));
        }
        return true;
    }
}
