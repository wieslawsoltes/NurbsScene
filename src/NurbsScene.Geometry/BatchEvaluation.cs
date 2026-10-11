namespace NurbsScene.Geometry;

/// <summary>Optional allocation-free evaluator contract. Existing INurbsEvaluator
/// implementations remain supported. Surface output is U-major, V varying fastest.
/// Buffers must fit the requested derivatives; entries beyond that range are untouched.</summary>
public interface INurbsSpanEvaluator : INurbsEvaluator
{
    void EvaluateInto(NurbsCurve curve, double parameter, int order, Span<Vec3> derivatives);
    void EvaluateInto(NurbsSurface surface, double u, double v, int orderU, int orderV, Span<Vec3> derivatives);
}

/// <summary>First-order surface sample in modeling precision, not render floats.</summary>
public readonly record struct SurfaceJet(Vec3 Point, Vec3 Du, Vec3 Dv);

/// <summary>Optional tensor-grid evaluation. Axes may be unsorted and repeat values;
/// every value must belong to the actual active knot domain. Output is U-major.
/// Cancellation may leave an output prefix written; invalid axes write nothing.</summary>
public interface INurbsGridEvaluator : INurbsEvaluator
{
    void EvaluateGrid(NurbsSurface surface, ReadOnlySpan<double> u, ReadOnlySpan<double> v,
        Span<SurfaceJet> samples, CancellationToken cancellationToken = default);
}
