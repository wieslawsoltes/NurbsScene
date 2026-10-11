using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Parametric;

/// <summary>Exact tensor-surface editing and analytic-speed curve measurement
/// components. Directions are 0=U and 1=V; all parameters use actual knot domains.</summary>
public static class GeometryComponents
{
    private static PortDefinition N(string name, double value = 0) => new(name, ValueKind.Number, Default: value);
    private static PortDefinition Port(string name, ValueKind kind, PortAccess access = PortAccess.Item) => new(name, kind, access);
    private static IReadOnlyDictionary<string, object?> Output(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
    private static int Integer(double value, int minimum = 0, int maximum = 100_000)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum || value != Math.Truncate(value))
            throw new ArgumentOutOfRangeException(nameof(value), "A bounded integer is required.");
        return (int)value;
    }
    private static SurfaceDirection Direction(ComponentContext c) => (SurfaceDirection)Integer(c.Number("D"), 0, 1);
    public static void Register(ComponentRegistry registry)
    {
        void Add(string id, string category, PortDefinition[] inputs, PortDefinition[] outputs,
            Func<ComponentContext, IReadOnlyDictionary<string, object?>> action, string description)
            => registry.Register(new(id, category, description, inputs.ToImmutableArray(), outputs.ToImmutableArray(),
                c => { c.CancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(action(c)); }));
        var curve = Port("C", ValueKind.Curve); var surface = Port("S", ValueKind.Surface);
        Add("CurveLength", "Curve", [curve, N("Tolerance", 1e-9)], [N("L"), N("Error")], c => {
            var r = c.Get<NurbsCurve>("C").GetLength(new(AbsoluteTolerance: c.Number("Tolerance")), c.CancellationToken);
            return Output(("L", r.Length), ("Error", r.EstimatedError));
        }, "Integrate analytic NURBS speed. Error is an estimate, not a certified bound.");
        Add("LengthParameter", "Curve", [curve, N("L")], [N("T"), Port("P", ValueKind.Point)], c => {
            var source = c.Get<NurbsCurve>("C"); var t = source.LengthParameter(c.Number("L"), cancellationToken: c.CancellationToken);
            return Output(("T", t), ("P", source.PointAt(t)));
        }, "Find the actual knot-domain parameter at a world-unit distance from the curve start.");
        Add("DivideCurve", "Curve", [curve, N("N", 10)], [Port("P", ValueKind.Point, PortAccess.List), Port("T", ValueKind.Number, PortAccess.List)], c => {
            var r = c.Get<NurbsCurve>("C").DivideByCount(Integer(c.Number("N"), 1), cancellationToken: c.CancellationToken);
            return Output(("P", r.Points), ("T", r.Parameters));
        }, "Equal arc-length division including endpoints, not uniform-parameter sampling.");
        Add("InsertSurfaceKnot", "Surface", [surface, N("D"), N("T", .5), N("N", 1)], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").InsertKnot(Direction(c), c.Number("T"), Integer(c.Number("N"), 0, 128)))),
            "Exact homogeneous knot insertion. D: 0=U, 1=V.");
        Add("RefineSurfaceKnots", "Surface", [surface, N("D"), Port("Knots", ValueKind.Number, PortAccess.List)], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").RefineKnots(Direction(c), c.Get<ImmutableArray<object?>>("Knots").Select(Convert.ToDouble)))),
            "Prevalidated exact knot refinement, including requested multiplicities.");
        Add("ElevateSurfaceDegree", "Surface", [surface, N("D"), N("N", 1)], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").ElevateDegree(Direction(c), Integer(c.Number("N"), 0, 127)))),
            "Exact homogeneous degree elevation along U or V; clamped continuous knot vectors required.");
        Add("ReverseSurface", "Surface", [surface, N("D")], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").Reverse(Direction(c)))), "Reverse a parameter direction while retaining the active domain.");
        Add("SplitSurface", "Surface", [surface, N("D"), N("T", .5)], [Port("A", ValueKind.Surface), Port("B", ValueKind.Surface)], c => {
            var (left, right) = c.Get<NurbsSurface>("S").Split(Direction(c), c.Number("T"));
            return Output(("A", left), ("B", right));
        }, "Split exact tensor-product geometry, without approximating a trim with triangles.");
        Add("SubSurface", "Surface", [surface, N("U0", .2), N("U1", .8), N("V0", .2), N("V1", .8)], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").Trim(new(c.Number("U0"), c.Number("U1")), new(c.Number("V0"), c.Number("V1"))))),
            "Exact rectangular UV extraction; not arbitrary trim-loop or B-rep support.");
        Add("ReparameterizeSurface", "Surface", [surface, N("U0"), N("U1", 1), N("V0"), N("V1", 1)], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").Reparameterize(new(c.Number("U0"), c.Number("U1")), new(c.Number("V0"), c.Number("V1"))))),
            "Affine knot-domain remapping in both directions.");
        Add("SurfaceControlPoint", "Surface", [surface, N("I"), N("J"), new("P", ValueKind.Point, Default: Vec3.Zero), new("W", ValueKind.Number, Optional: true)], [surface], c =>
            Output(("S", c.Get<NurbsSurface>("S").WithControlPoint(Integer(c.Number("I")), Integer(c.Number("J")), c.Get<Vec3>("P"), c.Inputs["W"] is null ? null : c.Number("W")))),
            "Edit a Cartesian control position and optional positive weight. Unspecified weight is retained.");
        Add("SurfaceBoundaries", "Surface", [surface], [Port("C", ValueKind.Curve, PortAccess.List)], c =>
            Output(("C", c.Get<NurbsSurface>("S").BoundaryCurves())), "Four exact boundaries oriented counterclockwise in the UV rectangle.");
        Add("SurfaceBezierPatches", "Surface", [surface], [Port("S", ValueKind.Surface, PortAccess.List)], c =>
            Output(("S", c.Get<NurbsSurface>("S").BezierPatches())), "Exact rational Bezier decomposition with source parameter domains.");
        Add("SurfaceControlNet", "Surface", [surface], [Port("P", ValueKind.Point, PortAccess.Tree), Port("W", ValueKind.Number, PortAccess.Tree)], c => {
            var s = c.Get<NurbsSurface>("S"); var points = s.ControlPoints.ToArray();
            DataTree Tree(Func<HPoint, object?> value) => new(Enumerable.Range(0, s.CountU).Select(u =>
                new DataBranch(c.Path.Append(u), Enumerable.Range(0, s.CountV).Select(v => value(points[u * s.CountV + v])).ToImmutableArray())));
            return Output(("P", Tree(p => p.Position)), ("W", Tree(p => p.W)));
        }, "Control-net data trees: one branch per U lane, V varying fastest. Weights remain separate.");
        Add("SurfaceInfo", "Surface", [surface], [N("DegreeU"), N("DegreeV"), N("CountU"), N("CountV"), N("U0"), N("U1"), N("V0"), N("V1")], c => {
            var s = c.Get<NurbsSurface>("S");
            return Output(("DegreeU", s.DegreeU), ("DegreeV", s.DegreeV), ("CountU", s.CountU), ("CountV", s.CountV),
                ("U0", s.DomainU.Min), ("U1", s.DomainU.Max), ("V0", s.DomainV.Min), ("V1", s.DomainV.Max));
        }, "Authoritative degrees, control-net dimensions and active parameter intervals.");
        Add("CurveInfo", "Curve", [curve], [N("Degree"), N("Count"), N("T0"), N("T1")], c => {
            var s = c.Get<NurbsCurve>("C"); return Output(("Degree", s.Degree), ("Count", s.Count), ("T0", s.Domain.Min), ("T1", s.Domain.Max));
        }, "Authoritative curve degree, control count and parameter domain.");
    }
}
