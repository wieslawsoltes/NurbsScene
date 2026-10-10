namespace NurbsScene.Geometry;

/// <summary>Validated expanded knot operations shared by exact curves and surfaces.</summary>
public static class KnotVector
{
    public static void Validate(int degree, int count, ReadOnlySpan<double> knots)
    {
        if (degree is < 1 or > 128 || count <= degree || count > 10_000_000 || knots.Length != count + degree + 1)
            throw new ArgumentException("Invalid NURBS dimensions. Degree must be in 1..128.");
        var repeated = 0;
        var previous = double.NaN;
        foreach (var knot in knots)
        {
            if (!double.IsFinite(knot) || knot < previous)
                throw new ArgumentException("Knots must be finite and nondecreasing.");
            repeated = knot == previous ? repeated + 1 : 1;
            if (repeated > degree + 1) throw new ArgumentException("Knot multiplicity exceeds degree+1.");
            previous = knot;
        }
        if (!(knots[degree] < knots[count])) throw new ArgumentException("NURBS domain has zero length.");
    }

    public static double[] Reverse(int degree, int count, ReadOnlySpan<double> knots)
    {
        Validate(degree, count, knots);
        var a = knots[degree]; var b = knots[count]; var sum = a + b;
        var middle = a / 2 + b / 2;
        var result = new double[knots.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var value = knots[knots.Length - 1 - i];
            // Preserve the active domain, including non-clamped support knots.
            result[i] = value == a ? b : value == b ? a : double.IsFinite(sum) ? sum - value : middle + (middle - value);
        }
        Validate(degree, count, result);
        return result;
    }

    public static double[] Reparameterize(int degree, int count, ReadOnlySpan<double> knots, Interval target)
    {
        Validate(degree, count, knots);
        var source = new Interval(knots[degree], knots[count]);
        if (!(target.Min < target.Max) || !double.IsFinite(target.Min) || !double.IsFinite(target.Max)
            || !double.IsFinite(target.Length) || !double.IsFinite(source.Length))
            throw new ArgumentException("Parameter domains must have finite positive spans.");
        var result = new double[knots.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = knots[i] == source.Min ? target.Min : knots[i] == source.Max ? target.Max
                : target.Min + ((knots[i] - source.Min) / source.Length) * target.Length;
        Validate(degree, count, result);
        return result;
    }
}
