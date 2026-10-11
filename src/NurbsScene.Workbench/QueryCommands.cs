using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Workbench;

public sealed partial class WorkspaceController
{
    private bool TryQueryCommand(string command,string[] args)
    {
        if(TrySectionCommand(command,args))return true;
        if(TrySurfaceAnalysisCommand(command,args))return true;
        if(command is not("closestpoint" or "curveclosestpoint" or "curvecontrolpoint" or "projecttoplane" or "mirrorplane"))return false;
        var selected=Selected();if(selected.Length==0)throw new ArgumentException("Select geometry first.");
        if(command is "closestpoint" or "curveclosestpoint")
        {
            if(args.Length is <1 or >2)throw new ArgumentException("ClosestPoint x,y,z [worldDistanceTolerance] (selected curves)");
            var point=Point(args[0]);var options=new CurveQueryOptions(args.Length==2?Number(args[1]):Document.AbsoluteTolerance);
            var results=selected.Select(o=>(o.Name,Result:o.Geometry is NurbsCurve curve?curve.ClosestPoint(point,options):throw new ArgumentException("ClosestPoint currently requires curves."))).ToArray();
            if(results.Any(r=>!r.Result.Converged))throw new ArithmeticException("Closest-point tolerance was not met; no geometry was changed.");
            foreach(var (name,r) in results)Report(FormattableString.Invariant($"{name}: closest point {r.Point.X:G17},{r.Point.Y:G17},{r.Point.Z:G17}; parameter {r.Parameter:G17}; distance {r.Distance:G17} {Document.Units}; numerical gap {r.DistanceGap:G6}."));
            return true;
        }
        if(selected.Any(o=>!Document.CanEdit(o)))throw new ArgumentException("Selected geometry must be editable.");
        Func<IGeometry,IGeometry> operation;
        if(command=="curvecontrolpoint")
        {
            if(args.Length is <2 or >3)throw new ArgumentException("CurveControlPoint index x,y,z [weight]");
            var index=ParseIndex(args[0]);var point=Point(args[1]);double? weight=args.Length==3?Number(args[2]):null;
            operation=g=>g is NurbsCurve curve?curve.WithControlPoint(index,point,weight):throw new ArgumentException("Select curves only.");
        }
        else
        {
            if(args.Length!=2)throw new ArgumentException("ProjectToPlane/MirrorPlane originX,originY,originZ normalX,normalY,normalZ");
            var plane=new Plane3(Point(args[0]),Point(args[1]));var transform=command=="mirrorplane"?plane.Reflection:plane.Projection;
            operation=g=>g.Transform(transform);
        }
        var prepared=selected.Select(o=>o with{Geometry=operation(o.Geometry)}).ToArray();
        Document.Transaction(command,()=>{foreach(var item in prepared)Document.Update(item);});
        return true;
    }
}
