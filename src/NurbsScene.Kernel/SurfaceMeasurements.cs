using System.Runtime.CompilerServices;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>Absolute tolerance is in squared model units. Relative tolerance also
/// controls first moments after normalization by the control-hull radius. Error
/// estimates are numerical, not certified bounds. One cached setting per surface.</summary>
public sealed record SurfaceAreaOptions(double AbsoluteTolerance = 1e-8, double RelativeTolerance = 1e-8,
    int MaximumEvaluations = 250_000, int MaximumCells = 4096, int MaximumDepth = 20, bool CacheResult = true)
{
    internal void Validate()
    {
        if (!double.IsFinite(AbsoluteTolerance) || AbsoluteTolerance <= 0 ||
            !double.IsFinite(RelativeTolerance) || RelativeTolerance < 0 || RelativeTolerance >= 1 ||
            MaximumEvaluations is < 42 or > 10_000_000 || MaximumCells is < 1 or > 100_000 || MaximumDepth is < 0 or > 30)
            throw new ArgumentException("Invalid surface-area tolerances or work limits.");
    }
}
public enum SurfaceAreaTermination { Converged, EvaluationLimit, CellLimit, DepthLimit, ParameterResolution }
/// <summary>Unsigned area of the parameterized surface, counting self-overlap with
/// multiplicity. Centroid is null for zero area. Cached hits have zero evaluations;
/// Cells still describes the retained partition. CentroidError is null when the
/// numerical area error does not permit a meaningful quotient estimate.</summary>
public sealed record SurfaceAreaResult(double Area, double EstimatedAreaError, Vec3? Centroid,
    Vec3? EstimatedCentroidError, int Evaluations, int Cells, SurfaceAreaTermination Termination, bool CacheHit = false)
{
    public bool Converged => Termination == SurfaceAreaTermination.Converged;
}

/// <summary>Adaptive tensor Gauss 3/5 integration of analytic NURBS surface jets.
/// Each active knot rectangle is treated separately, never bridged by triangles.
/// Coordinates are rebased/scaled before evaluation to stabilize first moments.
/// Supports untrimmed tensor surfaces, not solid volume or arbitrary trim loops.</summary>
public static class SurfaceMeasurements
{
    private const double Epsilon = 2.2204460492503131e-16;
    private static readonly double[] Nodes3 = [-.77459666924148337704, 0, .77459666924148337704];
    private static readonly double[] Weights3 = [5d/9, 8d/9, 5d/9];
    private static readonly double[] Nodes5 = [-.9061798459386639928, -.5384693101056830910, 0, .5384693101056830910, .9061798459386639928];
    private static readonly double[] Weights5 = [.2369268850561890875, .4786286704993664680, .5688888888888888889, .4786286704993664680, .2369268850561890875];
    private sealed record Cached(SurfaceAreaOptions Options, SurfaceAreaResult Result);
    private sealed class Cache { public Cached? Last; }
    private static readonly ConditionalWeakTable<NurbsSurface, Cache> Results = new();
    private readonly record struct Cell(Interval U, Interval V, int Depth, double Area, Vec3 Moment, double AreaError, Vec3 MomentError)
    {
        public double Error => Math.Max(AreaError, Max(MomentError));
    }
    private struct Sum
    {
        private double _sum, _correction;
        public void Add(double x) { var y = x - _correction; var next = _sum + y; _correction = (next - _sum) - y; _sum = next; }
        public readonly double Value => _sum;
    }
    private sealed class Totals
    {
        private Sum _area, _x, _y, _z, _error, _ex, _ey, _ez;
        public void Add(Cell c, double sign = 1)
        {
            _area.Add(sign*c.Area); _x.Add(sign*c.Moment.X); _y.Add(sign*c.Moment.Y); _z.Add(sign*c.Moment.Z);
            _error.Add(sign*c.AreaError); _ex.Add(sign*c.MomentError.X); _ey.Add(sign*c.MomentError.Y); _ez.Add(sign*c.MomentError.Z);
        }
        public double Area => Math.Max(0, _area.Value);
        public Vec3 Moment => new(_x.Value, _y.Value, _z.Value);
        public double AreaError => Math.Max(0, _error.Value);
        public Vec3 MomentError => new(Math.Max(0, _ex.Value), Math.Max(0, _ey.Value), Math.Max(0, _ez.Value));
        public double Error => Math.Max(AreaError, Max(MomentError));
    }
    private static double Max(Vec3 v) => Math.Max(v.X, Math.Max(v.Y, v.Z));
    private static Vec3 Abs(Vec3 v) => new(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));

    public static SurfaceAreaResult GetAreaProperties(this NurbsSurface surface, SurfaceAreaOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var settings = options ?? new(); settings.Validate(); cancellationToken.ThrowIfCancellationRequested();
        Cache? cache = null;
        if (settings.CacheResult)
        {
            cache = Results.GetValue(surface, static _ => new());
            if (Volatile.Read(ref cache.Last) is { } saved && saved.Options == settings)
                return saved.Result with { Evaluations = 0, CacheHit = true };
        }
        var result = Integrate(surface, settings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (cache is not null && result.Converged) Volatile.Write(ref cache.Last, new(settings, result));
        return result;
    }
    private static SurfaceAreaResult Integrate(NurbsSurface source, SurfaceAreaOptions settings, CancellationToken cancellation)
    {
        var us = Spans(source.DegreeU, source.CountU, source.KnotsU);
        var vs = Spans(source.DegreeV, source.CountV, source.KnotsV);
        var initial = (long)us.Length * vs.Length;
        if (initial > settings.MaximumCells || initial * 42 > settings.MaximumEvaluations)
            throw new ArgumentException("Work budget cannot cover the initial active knot rectangles; no partial-area result is returned.");
        var origin = source.Bounds.Center;
        var radius = Max(Abs(source.Bounds.Max - origin));
        if (!double.IsFinite(radius)) throw new ArithmeticException("Surface control-hull extent exceeds binary64 range.");
        if (radius == 0) { cancellation.ThrowIfCancellationRequested(); return new(0, 0, null, null, 0, (int)initial, SurfaceAreaTermination.Converged); }
        var weight = 0d;
        foreach (var p in source.ControlPoints) weight = Math.Max(weight, p.W);
        var controls = new HPoint[source.ControlPoints.Length];
        for (var i = 0; i < controls.Length; i++)
        {
            if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
            var p = source.ControlPoints[i]; var w = p.W / weight;
            if (w == 0) throw new ArithmeticException("Relative surface weight underflow.");
            var offset = p.Position - origin;
            var normalized = HPoint.FromPoint(offset / radius, w);
            if ((offset.X != 0 && normalized.X == 0) || (offset.Y != 0 && normalized.Y == 0) ||
                (offset.Z != 0 && normalized.Z == 0))
                throw new ArithmeticException("Surface normalization would lose a nonzero coordinate to underflow.");
            controls[i] = normalized;
        }
        var surface = new NurbsSurface(source.Evaluator, source.DegreeU, source.DegreeV, source.CountU, source.CountV,
            source.KnotsU.ToArray(), source.KnotsV.ToArray(), controls);
        var totals = new Totals(); var queue = new PriorityQueue<Cell, double>(); var evaluations = 0;
        Cell Make(Interval u, Interval v, int depth)
        {
            var high = Rule(surface, u, v, Nodes5, Weights5, cancellation);
            var low = Rule(surface, u, v, Nodes3, Weights3, cancellation); evaluations += 42;
            var floor = 64 * Epsilon * high.Area;
            if (high.Area > 0 && floor == 0)
                throw new ArithmeticException("Surface integrand is too small for a representable error estimate.");
            // Interior Gauss rules alone can both miss a thin rational boundary
            // layer. Sample just inside all edges; excessive contrast forces
            // refinement (or an explicit unresolved work-limit result).
            var edge = BoundaryDensity(surface,u,v,cancellation);
            var boundaryError = edge > 32 * high.Area ? edge : 0;
            return new(u, v, depth, high.Area, high.Moment,
                Math.Max(Math.Max(Math.Abs(high.Area - low.Area), floor), boundaryError),
                new(Math.Max(Math.Abs(high.Moment.X-low.Moment.X), floor), Math.Max(Math.Abs(high.Moment.Y-low.Moment.Y), floor), Math.Max(Math.Abs(high.Moment.Z-low.Moment.Z), floor)));
        }
        foreach (var u in us) foreach (var v in vs) { var cell = Make(u, v, 0); totals.Add(cell); queue.Enqueue(cell, -cell.Error); }
        var termination = SurfaceAreaTermination.Converged;
        var absolute = settings.AbsoluteTolerance / radius / radius;
        while (totals.Error > Math.Max(absolute, settings.RelativeTolerance * totals.Area))
        {
            cancellation.ThrowIfCancellationRequested();
            if (evaluations > settings.MaximumEvaluations - 168) { termination = SurfaceAreaTermination.EvaluationLimit; break; }
            if (queue.Count > settings.MaximumCells - 3) { termination = SurfaceAreaTermination.CellLimit; break; }
            var cell = queue.Peek();
            if (cell.Depth >= settings.MaximumDepth) { termination = SurfaceAreaTermination.DepthLimit; break; }
            var um = cell.U.Min/2 + cell.U.Max/2; var vm = cell.V.Min/2 + cell.V.Max/2;
            if (!(um > cell.U.Min && um < cell.U.Max && vm > cell.V.Min && vm < cell.V.Max))
            { termination = SurfaceAreaTermination.ParameterResolution; break; }
            // Prepare all four children before replacing their fully covered parent.
            var a = Make(new(cell.U.Min,um), new(cell.V.Min,vm), cell.Depth+1);
            var b = Make(new(um,cell.U.Max), new(cell.V.Min,vm), cell.Depth+1);
            var c = Make(new(cell.U.Min,um), new(vm,cell.V.Max), cell.Depth+1);
            var d = Make(new(um,cell.U.Max), new(vm,cell.V.Max), cell.Depth+1);
            queue.Dequeue(); totals.Add(cell,-1);
            void Add(Cell next) { totals.Add(next); queue.Enqueue(next,-next.Error); }
            Add(a); Add(b); Add(c); Add(d);
        }
        var area = totals.Area * radius * radius;
        var error = totals.AreaError * radius * radius;
        Vec3? centroid = null, centroidError = null;
        if (totals.Area > 0)
        {
            var relative = totals.Moment / totals.Area; centroid = origin + relative * radius;
            if (totals.Area > totals.AreaError)
                centroidError = (totals.MomentError + Abs(relative) * totals.AreaError) / (totals.Area - totals.AreaError) * radius;
        }
        if (!double.IsFinite(area) || !double.IsFinite(error) || (totals.Area > 0 && area == 0) ||
            centroid is { IsFinite:false } || centroidError is { IsFinite:false })
            throw new ArithmeticException("Surface area or centroid exceeds binary64 range.");
        return new(area, error, centroid, centroidError, evaluations, queue.Count, termination);
    }
    private static Interval[] Spans(int degree, int count, ReadOnlySpan<double> knots)
    {
        var spans = new List<Interval>();
        for (var i = degree; i < count; i++) if (knots[i] < knots[i+1])
        {
            var span = new Interval(knots[i], knots[i+1]);
            if (!double.IsFinite(span.Length)) throw new ArithmeticException("Surface knot interval exceeds binary64 range.");
            spans.Add(span);
            if (spans.Count > 100_000) throw new ArgumentException("Surface measurement axis exceeds its span budget.");
        }
        return spans.ToArray();
    }
    private static double BoundaryDensity(NurbsSurface surface, Interval u, Interval v, CancellationToken cancellation)
    {
        Span<double> us = stackalloc double[] { Math.BitIncrement(u.Min), u.Min/2+u.Max/2, Math.BitDecrement(u.Max) };
        Span<double> vs = stackalloc double[] { Math.BitIncrement(v.Min), v.Min/2+v.Max/2, Math.BitDecrement(v.Max) };
        Span<Vec3> jet = stackalloc Vec3[4]; double maximum=0;
        for (var i=0;i<3;i++) for (var j=0;j<3;j++) if(i!=1||j!=1)
        {
            cancellation.ThrowIfCancellationRequested();
            if (surface.Evaluator is INurbsSpanEvaluator fast) fast.EvaluateInto(surface,us[i],vs[j],1,1,jet);
            else { var d=surface.Derivatives(us[i],vs[j],1,1); jet[1]=d[0,1]; jet[2]=d[1,0]; }
            var density=Vec3.Cross(jet[2]*(u.Length/2),jet[1]*(v.Length/2)).Length*4;
            if (!double.IsFinite(density)) throw new ArithmeticException("Non-finite surface boundary density.");
            maximum=Math.Max(maximum,density);
        }
        return maximum;
    }
    private static (double Area, Vec3 Moment) Rule(NurbsSurface surface, Interval u, Interval v,
        double[] nodes, double[] weights, CancellationToken cancellation)
    {
        Span<double> us = stackalloc double[5]; Span<double> vs = stackalloc double[5];
        Span<SurfaceJet> jets = stackalloc SurfaceJet[25]; Span<Vec3> derivatives = stackalloc Vec3[4];
        var n = nodes.Length;
        for (var i = 0; i < n; i++)
        {
            var fraction = (nodes[i]+1)/2;
            us[i] = u.Min*(1-fraction)+u.Max*fraction; vs[i] = v.Min*(1-fraction)+v.Max*fraction;
            if (!(us[i] > u.Min && us[i] < u.Max && vs[i] > v.Min && vs[i] < v.Max))
                throw new ArithmeticException("Surface quadrature nodes cannot be represented inside their knot rectangle.");
        }
        if (surface.Evaluator is INurbsGridEvaluator grid) grid.EvaluateGrid(surface, us[..n], vs[..n], jets[..(n*n)], cancellation);
        else for (var i = 0; i < n; i++) for (var j = 0; j < n; j++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (surface.Evaluator is INurbsSpanEvaluator fast) fast.EvaluateInto(surface,us[i],vs[j],1,1,derivatives);
            else { var d = surface.Derivatives(us[i],vs[j],1,1); derivatives[0]=d[0,0]; derivatives[1]=d[0,1]; derivatives[2]=d[1,0]; }
            jets[i*n+j] = new(derivatives[0], derivatives[2], derivatives[1]);
        }
        Sum area = default, x = default, y = default, z = default;
        for (var i = 0; i < n; i++) for (var j = 0; j < n; j++)
        {
            var jet = jets[i*n+j];
            var density = Vec3.Cross(jet.Du*(u.Length/2),jet.Dv*(v.Length/2)).Length * weights[i] * weights[j];
            if (!jet.Point.IsFinite || !double.IsFinite(density)) throw new ArithmeticException("Non-finite surface area integrand.");
            area.Add(density); x.Add(jet.Point.X*density); y.Add(jet.Point.Y*density); z.Add(jet.Point.Z*density);
        }
        return (area.Value,new(x.Value,y.Value,z.Value));
    }
}
