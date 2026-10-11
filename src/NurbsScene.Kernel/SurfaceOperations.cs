using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public enum SurfaceDirection { U, V }

/// <summary>Exact tensor-product editing in homogeneous coordinates. These
/// operations do not replace geometry with meshes and do not create B-rep trims.</summary>
public static class SurfaceOperations
{
    private const int MaximumControls = 1_000_000;

    private static void Direction(SurfaceDirection direction)
    {
        if (direction is not (SurfaceDirection.U or SurfaceDirection.V))
            throw new ArgumentOutOfRangeException(nameof(direction));
    }

    private static NurbsCurve[] Lanes(NurbsSurface surface, SurfaceDirection direction)
    {
        Direction(direction);
        var alongU = direction == SurfaceDirection.U;
        var count = alongU ? surface.CountU : surface.CountV;
        var lanes = alongU ? surface.CountV : surface.CountU;
        var knots = alongU ? surface.KnotsU.ToArray() : surface.KnotsV.ToArray();
        var degree = alongU ? surface.DegreeU : surface.DegreeV;
        var source = surface.ControlPoints;
        var result = new NurbsCurve[lanes];
        for (var lane = 0; lane < lanes; lane++)
        {
            var points = new HPoint[count];
            for (var i = 0; i < count; i++)
                points[i] = source[alongU ? i * surface.CountV + lane : lane * surface.CountV + i];
            result[lane] = new(surface.Evaluator, degree, knots, points);
        }
        return result;
    }

    private static NurbsSurface Assemble(NurbsSurface source, SurfaceDirection direction, NurbsCurve[] lanes)
    {
        var first = lanes[0];
        if (lanes.Any(c => c.Degree != first.Degree || c.Count != first.Count || !c.Knots.SequenceEqual(first.Knots)))
            throw new InvalidOperationException("Surface lanes have incompatible knot vectors.");
        var alongU = direction == SurfaceDirection.U;
        var countU = alongU ? first.Count : source.CountU;
        var countV = alongU ? source.CountV : first.Count;
        if ((long)countU * countV > MaximumControls) throw new InvalidOperationException("Surface control-point budget exceeded.");
        var points = new HPoint[countU * countV];
        for (var lane = 0; lane < lanes.Length; lane++)
            for (var i = 0; i < first.Count; i++)
                points[alongU ? i * countV + lane : lane * countV + i] = lanes[lane].ControlPoints[i];
        return new(source.Evaluator, alongU ? first.Degree : source.DegreeU, alongU ? source.DegreeV : first.Degree,
            countU, countV, alongU ? first.Knots.ToArray() : source.KnotsU.ToArray(),
            alongU ? source.KnotsV.ToArray() : first.Knots.ToArray(), points);
    }

    public static NurbsSurface InsertKnot(this NurbsSurface surface, SurfaceDirection direction, double parameter, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(surface); Direction(direction);
        if (times is < 0 or > 128) throw new ArgumentOutOfRangeException(nameof(times));
        if (times == 0) return surface;
        var count = direction == SurfaceDirection.U ? surface.CountU : surface.CountV;
        var other = direction == SurfaceDirection.U ? surface.CountV : surface.CountU;
        if ((long)(count + times) * other > MaximumControls) throw new InvalidOperationException("Surface control-point budget exceeded.");
        var lanes = Lanes(surface, direction);
        for (var i = 0; i < lanes.Length; i++) lanes[i] = lanes[i].InsertKnot(parameter, times);
        return Assemble(surface, direction, lanes);
    }

    /// <summary>Insert a multiset of interior knots. Preflight all multiplicities
    /// before allocating any modified control net; inputs remain immutable.</summary>
    public static NurbsSurface RefineKnots(this NurbsSurface surface, SurfaceDirection direction, IEnumerable<double> parameters)
    {
        ArgumentNullException.ThrowIfNull(surface); ArgumentNullException.ThrowIfNull(parameters); Direction(direction);
        var values = parameters.Take(4097).ToArray();
        if (values.Length > 4096) throw new ArgumentException("At most 4096 knot insertions are accepted per operation.");
        if (values.Length == 0) return surface;
        var domain = direction == SurfaceDirection.U ? surface.DomainU : surface.DomainV;
        var degree = direction == SurfaceDirection.U ? surface.DegreeU : surface.DegreeV;
        var knots = direction == SurfaceDirection.U ? surface.KnotsU.ToArray() : surface.KnotsV.ToArray();
        var count = direction == SurfaceDirection.U ? surface.CountU : surface.CountV;
        var other = direction == SurfaceDirection.U ? surface.CountV : surface.CountU;
        if ((long)(count + values.Length) * other > MaximumControls) throw new InvalidOperationException("Surface control-point budget exceeded.");
        foreach (var group in values.GroupBy(t => t))
        {
            if (!double.IsFinite(group.Key) || group.Key <= domain.Min || group.Key >= domain.Max)
                throw new ArgumentOutOfRangeException(nameof(parameters), "Refinement knots must lie strictly inside the active domain.");
            if (group.Count() + knots.Count(t => t == group.Key) > degree)
                throw new ArgumentException("Refinement would exceed the interior knot multiplicity.");
        }
        Array.Sort(values);
        var lanes = Lanes(surface, direction);
        for (var i = 0; i < lanes.Length; i++) foreach (var t in values) lanes[i] = lanes[i].InsertKnot(t);
        return Assemble(surface, direction, lanes);
    }

    public static (NurbsSurface Lower, NurbsSurface Upper) Split(this NurbsSurface surface, SurfaceDirection direction, double parameter)
    {
        ArgumentNullException.ThrowIfNull(surface); Direction(direction);
        var lanes = Lanes(surface, direction); var lower = new NurbsCurve[lanes.Length]; var upper = new NurbsCurve[lanes.Length];
        for (var i = 0; i < lanes.Length; i++) (lower[i], upper[i]) = lanes[i].Split(parameter);
        return (Assemble(surface, direction, lower), Assemble(surface, direction, upper));
    }

    /// <summary>Exact rectangular parameter-domain extraction, not arbitrary trimmed-face modeling.</summary>
    public static NurbsSurface Trim(this NurbsSurface surface, Interval domainU, Interval domainV)
    {
        ArgumentNullException.ThrowIfNull(surface);
        static void Validate(Interval requested, Interval current)
        {
            if (!(requested.Min < requested.Max) || !current.Contains(requested.Min) || !current.Contains(requested.Max))
                throw new ArgumentException("Subsurface intervals must lie inside the active parameter domains.");
        }
        Validate(domainU, surface.DomainU); Validate(domainV, surface.DomainV);
        if (domainU.Min > surface.DomainU.Min) surface = surface.Split(SurfaceDirection.U, domainU.Min).Upper;
        if (domainU.Max < surface.DomainU.Max) surface = surface.Split(SurfaceDirection.U, domainU.Max).Lower;
        if (domainV.Min > surface.DomainV.Min) surface = surface.Split(SurfaceDirection.V, domainV.Min).Upper;
        if (domainV.Max < surface.DomainV.Max) surface = surface.Split(SurfaceDirection.V, domainV.Max).Lower;
        return surface;
    }

    public static NurbsSurface ElevateDegree(this NurbsSurface surface, SurfaceDirection direction, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(surface); Direction(direction);
        var degree = direction == SurfaceDirection.U ? surface.DegreeU : surface.DegreeV;
        if (times < 0 || degree + (long)times > 128) throw new ArgumentOutOfRangeException(nameof(times));
        if (times == 0) return surface;
        var knots = direction == SurfaceDirection.U ? surface.KnotsU.ToArray() : surface.KnotsV.ToArray();
        var other = direction == SurfaceDirection.U ? surface.CountV : surface.CountU;
        var segments = knots.Distinct().Count() - 1;
        if (((long)segments * (degree + times) + 1) * other > MaximumControls)
            throw new InvalidOperationException("Degree elevation would exceed the control-point budget.");
        var lanes = Lanes(surface, direction);
        for (var i = 0; i < lanes.Length; i++) lanes[i] = lanes[i].ElevateDegree(times);
        return Assemble(surface, direction, lanes);
    }

    public static NurbsSurface Reverse(this NurbsSurface surface, SurfaceDirection direction)
    {
        ArgumentNullException.ThrowIfNull(surface); Direction(direction);
        var lanes = Lanes(surface, direction);
        for (var i = 0; i < lanes.Length; i++) lanes[i] = lanes[i].Reverse();
        return Assemble(surface, direction, lanes);
    }

    public static NurbsSurface Reparameterize(this NurbsSurface surface, Interval domainU, Interval domainV)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return new(surface.Evaluator, surface.DegreeU, surface.DegreeV, surface.CountU, surface.CountV,
            KnotVector.Reparameterize(surface.DegreeU, surface.CountU, surface.KnotsU, domainU),
            KnotVector.Reparameterize(surface.DegreeV, surface.CountV, surface.KnotsV, domainV), surface.ControlPoints.ToArray());
    }

    public static NurbsSurface WithControlPoint(this NurbsSurface surface, int uIndex, int vIndex, Vec3 position, double? weight = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if ((uint)uIndex >= surface.CountU || (uint)vIndex >= surface.CountV) throw new ArgumentOutOfRangeException(nameof(uIndex));
        if (!position.IsFinite) throw new ArgumentException("Control point must be finite.");
        var index = uIndex * surface.CountV + vIndex; var controls = surface.ControlPoints.ToArray();
        var updated = HPoint.FromPoint(position, weight ?? controls[index].W);
        if (!updated.IsValid) throw new ArgumentException("Control weight must be finite and positive.");
        controls[index] = updated;
        return new(surface.Evaluator, surface.DegreeU, surface.DegreeV, surface.CountU, surface.CountV,
            surface.KnotsU.ToArray(), surface.KnotsV.ToArray(), controls);
    }

    /// <summary>Counterclockwise boundary in the UV rectangle: bottom, right, top, left.</summary>
    public static NurbsCurve[] BoundaryCurves(this NurbsSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var factory = new GeometryFactory(surface.Evaluator);
        return [factory.IsoCurve(surface, surface.DomainV.Min, false), factory.IsoCurve(surface, surface.DomainU.Max, true),
            factory.IsoCurve(surface, surface.DomainV.Max, false).Reverse(), factory.IsoCurve(surface, surface.DomainU.Min, true).Reverse()];
    }

    /// <summary>Exact rational Bezier patches, retaining source UV domains. Current
    /// decomposition requires clamped endpoints and continuous interior knots.</summary>
    public static NurbsSurface[] BezierPatches(this NurbsSurface surface, int maximumPatches = 10000)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (maximumPatches is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(maximumPatches));
        var prospectiveU = surface.KnotsU.ToArray().Distinct().Count(t => t >= surface.DomainU.Min && t <= surface.DomainU.Max) - 1;
        var prospectiveV = surface.KnotsV.ToArray().Distinct().Count(t => t >= surface.DomainV.Min && t <= surface.DomainV.Max) - 1;
        if ((long)prospectiveU * prospectiveV > maximumPatches || (long)prospectiveU * prospectiveV * (surface.DegreeU + 1) * (surface.DegreeV + 1) > MaximumControls)
            throw new InvalidOperationException("Bezier patch output budget exceeded.");
        var uLanes = Lanes(surface, SurfaceDirection.U);
        var uSegments = uLanes.Select(c => c.BezierSegments()).ToArray();
        var strips = new List<NurbsSurface>();
        for (var segment = 0; segment < uSegments[0].Length; segment++)
        {
            var lanes = uSegments.Select(parts => new NurbsCurve(surface.Evaluator, parts[segment].Degree,
                Enumerable.Repeat(parts[segment].Domain.Min, parts[segment].Degree + 1).Concat(Enumerable.Repeat(parts[segment].Domain.Max, parts[segment].Degree + 1)), parts[segment].Controls)).ToArray();
            strips.Add(Assemble(surface, SurfaceDirection.U, lanes));
        }
        var output = new List<NurbsSurface>();
        foreach (var strip in strips)
        {
            var vSegments = Lanes(strip, SurfaceDirection.V).Select(c => c.BezierSegments()).ToArray();
            if ((long)strips.Count * vSegments[0].Length > maximumPatches
                || (long)strips.Count * vSegments[0].Length * (surface.DegreeU + 1) * (surface.DegreeV + 1) > MaximumControls)
                throw new InvalidOperationException("Bezier patch output budget exceeded.");
            for (var segment = 0; segment < vSegments[0].Length; segment++)
            {
                var lanes = vSegments.Select(parts => new NurbsCurve(surface.Evaluator, parts[segment].Degree,
                    Enumerable.Repeat(parts[segment].Domain.Min, parts[segment].Degree + 1).Concat(Enumerable.Repeat(parts[segment].Domain.Max, parts[segment].Degree + 1)), parts[segment].Controls)).ToArray();
                output.Add(Assemble(strip, SurfaceDirection.V, lanes));
            }
        }
        return output.ToArray();
    }
}
