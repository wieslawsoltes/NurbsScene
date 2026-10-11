using System.Globalization;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Model;

namespace NurbsScene.Workbench;

public sealed partial class WorkspaceController
{
    private static SurfaceDirection ParseDirection(string text) => text.ToLowerInvariant() switch {
        "u" => SurfaceDirection.U, "v" => SurfaceDirection.V,
        _ => throw new ArgumentException("Direction must be U or V.") };
    private static int ParseIndex(string text, int minimum = 0, int maximum = 100_000)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw new ArgumentException($"Expected an integer in [{minimum}, {maximum}].");
        return value;
    }
    private bool TryExtendedCommand(string command, string[] args)
    {
        if (command == "curvelength")
        {
            if (args.Length > 1) throw new ArgumentException("CurveLength [absoluteTolerance]");
            var selected = Selected(); if (selected.Length == 0) throw new ArgumentException("Select at least one curve.");
            var options = new CurveLengthOptions(AbsoluteTolerance: args.Length == 1 ? Number(args[0]) : Document.AbsoluteTolerance);
            // Prepare every result before reporting, just as edits prepare before publication.
            var results = selected.Select(o => (Object: o, Result: o.Geometry is NurbsCurve curve ? curve.GetLength(options) :
                throw new ArgumentException("CurveLength requires selected curves."))).ToArray();
            foreach (var (item, result) in results) Report(FormattableString.Invariant($"{item.Name}: length {result.Length:G17} {Document.Units}; estimated integration error {result.EstimatedError:G6}."));
            return true;
        }
        if (command is not ("insertsurfaceknot" or "elevatesurfacedegree" or "reversesurface" or "splitsurface" or
            "trimsurface" or "reparameterizesurface" or "surfacecontrolpoint" or "surfaceboundaries" or "surfacebezierpatches")) return false;
        var source = Selected(); if (source.Length == 0) throw new ArgumentException("Select at least one surface.");
        if (source.Any(o => o.Geometry is not NurbsSurface || !Document.CanEdit(o))) throw new ArgumentException("Select editable NURBS surfaces only.");
        void Arity(int minimum, int maximum, string usage) { if (args.Length < minimum || args.Length > maximum) throw new ArgumentException(usage); }
        var prepared = new List<(SceneObject Source, IGeometry[] Values, bool Replace)>();
        foreach (var item in source)
        {
            var s = (NurbsSurface)item.Geometry; IGeometry[] values; var replace = true;
            switch (command)
            {
                case "insertsurfaceknot":
                    Arity(2,3,"InsertSurfaceKnot U|V parameter [count]");
                    values=[s.InsertKnot(ParseDirection(args[0]),Number(args[1]),args.Length==3?ParseIndex(args[2],0,128):1)];break;
                case "elevatesurfacedegree":
                    Arity(1,2,"ElevateSurfaceDegree U|V [count]");
                    values=[s.ElevateDegree(ParseDirection(args[0]),args.Length==2?ParseIndex(args[1],0,127):1)];break;
                case "reversesurface":
                    Arity(1,1,"ReverseSurface U|V");values=[s.Reverse(ParseDirection(args[0]))];break;
                case "splitsurface":
                    Arity(2,2,"SplitSurface U|V parameter");var halves=s.Split(ParseDirection(args[0]),Number(args[1]));
                    values=[halves.Lower,halves.Upper];break;
                case "trimsurface":
                    Arity(4,4,"TrimSurface uMin uMax vMin vMax (rectangular UV domain)");
                    values=[s.Trim(new(Number(args[0]),Number(args[1])),new(Number(args[2]),Number(args[3])))];break;
                case "reparameterizesurface":
                    Arity(4,4,"ReparameterizeSurface uMin uMax vMin vMax");
                    values=[s.Reparameterize(new(Number(args[0]),Number(args[1])),new(Number(args[2]),Number(args[3])))];break;
                case "surfacecontrolpoint":
                    Arity(3,4,"SurfaceControlPoint uIndex vIndex x,y,z [weight]");
                    values=[s.WithControlPoint(ParseIndex(args[0]),ParseIndex(args[1]),Point(args[2]),args.Length==4?Number(args[3]):null)];break;
                case "surfaceboundaries":
                    Arity(0,0,"SurfaceBoundaries");values=s.BoundaryCurves();replace=false;break;
                case "surfacebezierpatches":
                    Arity(0,0,"SurfaceBezierPatches");values=s.BezierPatches();break;
                default:throw new InvalidOperationException("Unreachable geometry command.");
            }
            prepared.Add((item,values,replace));
        }
        // A single transaction makes multi-selection failure and all undo/redo atomic.
        Document.Transaction(command, () => {
            var selection = new List<string>();
            foreach (var (item, values, replace) in prepared)
            {
                if (replace && values.Length == 1) { Document.Update(item with { Geometry=values[0] }); selection.Add(item.Id); }
                else
                {
                    if (replace) Document.Delete([item.Id]);
                    for (var i=0;i<values.Length;i++) selection.Add(Document.Add(values[i],$"{item.Name} {i+1}",item.LayerId,color:item.Color,source:item.Source).Id);
                }
            }
            Document.Select(selection);
        });
        return true;
    }
}
