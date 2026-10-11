using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Workbench;

public sealed partial class WorkspaceController
{
    private bool TrySectionCommand(string command,string[] args)
    {
        if(command is not("curveplane" or "splitcurve"))return false;
        var source=Selected();
        if(source.Length==0||source.Any(o=>o.Geometry is not NurbsCurve))throw new ArgumentException("Select NURBS curves first.");
        Plane3? plane=null;CurvePlaneOptions? options=null;double[] cuts=[];
        if(command=="splitcurve")
        {
            if(args.Length==0||args.Length>4095)throw new ArgumentException("SplitCurve parameter [parameter ...], at most 4095 cuts.");
            cuts=args.Select(Number).ToArray();
        }
        else
        {
            if(args.Length is <2 or >3)throw new ArgumentException("CurvePlane originX,originY,originZ normalX,normalY,normalZ [tolerance]");
            plane=new(Point(args[0]),Point(args[1]));options=new(args.Length==3?Number(args[2]):Document.AbsoluteTolerance);
        }
        if(command=="curveplane")
        {
            var results=source.Select(o=>(o.Name,Result:((NurbsCurve)o.Geometry).IntersectPlane(plane!,options))).ToArray();
            if(results.Any(r=>!r.Result.Complete))throw new ArithmeticException("Curve-plane search has unresolved intervals; no geometry changed.");
            foreach(var (name,result) in results)
            {
                Report($"{name}: {result.Points.Length} isolated intersections; {result.Overlaps.Length} coplanar overlaps.");
                foreach(var hit in result.Points)Report(FormattableString.Invariant($"t={hit.Parameter:G17}: {hit.Point.X:G17},{hit.Point.Y:G17},{hit.Point.Z:G17}; residual={hit.Residual:G6}"));
            }
            return true;
        }
        if(source.Any(o=>!Document.CanEdit(o)))throw new ArgumentException("Selected curves must be editable.");
        var prepared=source.Select(o=>(Object:o,Pieces:((NurbsCurve)o.Geometry).SplitMany(cuts))).ToArray();
        if(prepared.All(p=>p.Pieces.Length==1))return true;
        if((long)Document.Objects.Count+prepared.Sum(p=>(long)p.Pieces.Length-1)>100_000)throw new InvalidOperationException("Split exceeds the document object budget.");
        Document.Transaction(command,()=>{
            var selected=new List<string>();
            foreach(var (item,pieces) in prepared)
            {
                if(pieces.Length==1){selected.Add(item.Id);continue;}
                // Preserve the original object's identity on the first piece.
                Document.Update(item with{Geometry=pieces[0]});selected.Add(item.Id);
                for(var i=1;i<pieces.Length;i++)selected.Add(Document.Add(pieces[i],$"{item.Name} {i+1}",item.LayerId,color:item.Color,source:item.Source).Id);
            }
            Document.Select(selected);
        });
        return true;
    }
}
