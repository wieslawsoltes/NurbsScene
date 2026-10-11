using System.Runtime.CompilerServices;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>One forward knot scan and local homogeneous knot insertion. Never
/// refines and copies the whole curve once for every distinct knot. Cached arrays
/// are internal and read-only; the public API returns detached control arrays.</summary>
internal static class BezierExtraction
{
    private sealed record Entry(BezierSegment[] Segments);
    private static readonly ConditionalWeakTable<NurbsCurve, Entry> Cache = new();

    internal static BezierSegment[] Get(NurbsCurve curve, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        cancellation.ThrowIfCancellationRequested();
        if (Cache.TryGetValue(curve, out var cached)) return cached.Segments;
        var entry = new Entry(Extract(curve, cancellation));
        cancellation.ThrowIfCancellationRequested();
        // Racing readers may compute twice, but publish one immutable result.
        return Cache.GetValue(curve, _ => entry).Segments;
    }

    private static BezierSegment[] Extract(NurbsCurve curve, CancellationToken cancellation)
    {
        var degree = curve.Degree;
        var knots = curve.Knots;
        var controls = curve.ControlPoints;
        var start = curve.Domain.Min;
        var end = curve.Domain.Max;
        for (var i = 0; i <= degree; i++)
            if (knots[i] != start || knots[knots.Length - 1 - i] != end)
                throw new NotSupportedException("Bezier decomposition requires clamped endpoints.");
        var spans = 0;
        var multiplicity = 1;
        for (var i = degree + 1; i <= curve.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (knots[i] > knots[i - 1]) { spans++; multiplicity = 1; }
            else multiplicity++;
            if (knots[i] > start && knots[i] < end && multiplicity > degree)
                throw new NotSupportedException("Disconnected NURBS decomposition is not supported.");
        }
        if ((long)spans * (degree + 1) > 1_000_000)
            throw new NotSupportedException("Bezier decomposition exceeds one million controls.");

        var result = new BezierSegment[spans];
        Span<HPoint> points = stackalloc HPoint[degree + 1];
        Span<HPoint> next = stackalloc HPoint[degree + 1];
        Span<double> alpha = stackalloc double[degree];
        controls[..(degree + 1)].CopyTo(points);
        var a = degree;
        var b = degree + 1;
        var last = knots.Length - 1;
        var output = 0;
        while (b < last)
        {
            cancellation.ThrowIfCancellationRequested();
            var first = b;
            while (b < last && knots[b + 1] == knots[b]) b++;
            var count = b - first + 1;
            var insertions = degree - count;
            if (insertions > 0)
            {
                // Common scaling avoids infinity/infinity when finite knots span
                // a very large interval. The ratio is still binary64 arithmetic.
                for (var j = degree; j > count; j--)
                {
                    var numerator = knots[b] - knots[a];
                    var denominator = knots[a + j] - knots[a];
                    alpha[j - count - 1] = double.IsFinite(denominator)
                        ? numerator / denominator
                        : (knots[b] / 2 - knots[a] / 2) / (knots[a + j] / 2 - knots[a] / 2);
                }
                for (var j = 1; j <= insertions; j++)
                {
                    var s = count + j;
                    for (var k = degree; k >= s; k--)
                        points[k] = HPoint.Lerp(points[k - 1], points[k], alpha[k - s]);
                    next[insertions - j] = points[degree];
                }
            }
            result[output++] = new(degree, new(knots[a], knots[b]), points.ToArray());
            if (b < last)
            {
                if (insertions > 0) next[..insertions].CopyTo(points);
                for (var j = Math.Max(0, insertions); j <= degree; j++)
                    points[j] = controls[b - degree + j];
                a = b;
                b++;
            }
        }
        if (output != spans) throw new ArithmeticException("Inconsistent Bezier span extraction.");
        return result;
    }
}
