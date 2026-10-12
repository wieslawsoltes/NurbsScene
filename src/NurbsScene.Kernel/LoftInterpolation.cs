using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public sealed partial class GeometryFactory
{
    /// <summary>Interpolate compatible homogeneous sections at uniform V
    /// parameters. Factors the shared collocation matrix once, then reuses it for
    /// every U control's four homogeneous coordinates. It does not change the
    /// former loft's parameterization, seam alignment or section matching rules.</summary>
    public NurbsSurface Loft(IReadOnlyList<NurbsCurve> curves, int degreeV, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(curves);
        cancellationToken.ThrowIfCancellationRequested();
        var count = curves.Count;
        if (count is < 2 or > 4096 || degreeV < 1)
            throw new ArgumentException("Loft requires 2 to 4096 sections and positive V degree.");
        degreeV = Math.Min(degreeV, count - 1);
        if (degreeV > 128) throw new ArgumentException("Loft V degree must not exceed 128.");
        var captured = new NurbsCurve[count];
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            captured[i] = curves[i] ?? throw new ArgumentException("Loft sections cannot contain null.");
            if ((long)captured[i].Count * count > 1_000_000)
                throw new ArgumentException("Loft control net exceeds one million points.");
        }
        // Compatibility preparation retains the existing implementation. It is
        // cancellation-checked at entry/exit, not interruptible inside one edit.
        cancellationToken.ThrowIfCancellationRequested();
        var compatible = CurveOperations.MakeCompatible(captured);
        cancellationToken.ThrowIfCancellationRequested();
        var width = compatible[0].Count;
        if ((long)width * count > 1_000_000)
            throw new ArgumentException("Compatible loft control net exceeds one million points.");
        var parameters = new double[count];
        for (var i = 0; i < count; i++) parameters[i] = i / (count - 1d);
        var knots = new double[count + degreeV + 1];
        for (var j = 1; j < count - degreeV; j++)
        {
            var sum = 0d;
            for (var i = j; i < j + degreeV; i++) sum += parameters[i];
            knots[j + degreeV] = sum / degreeV;
        }
        Array.Fill(knots, 1, count, degreeV + 1);
        var matrix = new double[count, count];
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (span, basis) = ManagedNurbsEvaluator.Basis(degreeV, count, knots, parameters[i], 0);
            for (var j = 0; j <= degreeV; j++) matrix[i, span - degreeV + j] = basis[0, j];
        }
        var factors = new InterpolationFactors(matrix, cancellationToken);
        var controls = new HPoint[width * count];
        var rhs = new double[count, 4];
        for (var u = 0; u < width; u++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var v = 0; v < count; v++)
            {
                var point = compatible[v].ControlPoints[u];
                rhs[v, 0] = point.X; rhs[v, 1] = point.Y; rhs[v, 2] = point.Z; rhs[v, 3] = point.W;
            }
            factors.Solve(rhs, cancellationToken);
            for (var v = 0; v < count; v++)
                controls[u * count + v] = new(rhs[v, 0], rhs[v, 1], rhs[v, 2], rhs[v, 3]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // The immutable geometry constructor still rejects nonpositive weights
        // and non-finite controls. No partially prepared surface is published.
        return new(Evaluator, compatible[0].Degree, degreeV, width, count,
            compatible[0].Knots.ToArray(), knots, controls);
    }
}
