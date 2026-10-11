using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>World-unit absolute and dimensionless relative error targets.
/// Error estimates are numerical quadrature estimates, not certified bounds.</summary>
public sealed record CurveLengthOptions(
    double AbsoluteTolerance = 1e-9,
    double RelativeTolerance = 1e-9,
    int MaximumEvaluations = 200_000,
    int MaximumIntervals = 4096,
    int MaximumInverseIterations = 80)
{
    internal void Validate()
    {
        if (!double.IsFinite(AbsoluteTolerance) || AbsoluteTolerance <= 0 ||
            !double.IsFinite(RelativeTolerance) || RelativeTolerance < 0 || RelativeTolerance >= 1 ||
            MaximumEvaluations < 15 || MaximumIntervals < 1 || MaximumInverseIterations < 1)
            throw new ArgumentException("Invalid curve-length tolerance or work budget.");
    }
}

public sealed record CurveLengthResult(double Length, double EstimatedError, int Evaluations, int Intervals);
public sealed record CurveDivision(double[] Parameters, Vec3[] Points, CurveLengthResult Measurement);

/// <summary>Arc length of exact NURBS geometry, integrating analytic speed rather
/// than display chords. Integration splits at every active knot; an adaptive
/// Gauss-Kronrod 7/15 rule refines the largest estimated-error interval first.
/// Disconnected full-multiplicity curves are rejected rather than counting an
/// undocumented straight-line jump. No mutable shared evaluator state is kept.</summary>
public static class CurveMeasurements
{
    public static CurveLengthResult GetLength(this NurbsCurve curve, CurveLengthOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return Measure(curve, curve.Domain, options, cancellationToken);
    }

    public static CurveLengthResult Measure(this NurbsCurve curve, Interval domain,
        CurveLengthOptions? options = null, CancellationToken cancellationToken = default)
    {
        var settings = options ?? new(); settings.Validate();
        ValidateDomain(curve, domain);
        if (domain.Min == domain.Max) { cancellationToken.ThrowIfCancellationRequested(); return new(0, 0, 0, 0); }
        var budget = new Budget(settings.MaximumEvaluations, cancellationToken);
        var panels = Integrate(curve, domain, settings.AbsoluteTolerance, settings.RelativeTolerance,
            settings.MaximumIntervals, budget, true);
        return new(Sum(panels.Select(p => p.Value)), Sum(panels.Select(p => p.Error)), budget.Evaluations, panels.Count);
    }

    public static CurveArcLengthMap CreateArcLengthMap(this NurbsCurve curve,
        CurveLengthOptions? options = null, CancellationToken cancellationToken = default)
        => new(curve, options ?? new(), cancellationToken);

    public static double LengthParameter(this NurbsCurve curve, double length,
        CurveLengthOptions? options = null, CancellationToken cancellationToken = default)
        => curve.CreateArcLengthMap(options, cancellationToken).ParameterAtLength(length, cancellationToken);

    public static CurveDivision DivideByCount(this NurbsCurve curve, int count, bool includeEnds = true,
        CurveLengthOptions? options = null, CancellationToken cancellationToken = default)
        => curve.CreateArcLengthMap(options, cancellationToken).DivideByCount(count, includeEnds, cancellationToken);

    internal static void ValidateDomain(NurbsCurve curve, Interval domain)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (!curve.Domain.Contains(domain.Min) || !curve.Domain.Contains(domain.Max) || domain.Min > domain.Max ||
            !double.IsFinite(domain.Length)) throw new ArgumentOutOfRangeException(nameof(domain));
        var run = 1; var knots = curve.Knots;
        for (var i = 1; i < knots.Length; i++)
        {
            run = knots[i] == knots[i - 1] ? run + 1 : 1;
            if (run > curve.Degree && knots[i] > domain.Min && knots[i] < domain.Max)
                throw new NotSupportedException("Arc length across disconnected full-multiplicity knots is not supported; split the curve first.");
        }
    }

    internal sealed class Budget(int maximum, CancellationToken cancellation)
    {
        public int Evaluations { get; private set; }
        public double Speed(NurbsCurve curve, double t)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Evaluations >= maximum) throw new ArithmeticException("Curve-length evaluation budget exceeded.");
            Evaluations++;
            var speed = curve.Derivatives(t, 1)[1].Length;
            if (!double.IsFinite(speed)) throw new ArithmeticException("Non-finite curve speed.");
            return speed;
        }
        public void Check() => cancellation.ThrowIfCancellationRequested();
    }
    internal sealed record Panel(double A, double B, double Value, double Error);
    // Standard Gauss-Kronrod 7/15 abscissae and weights (mathematical constants).
    private static ReadOnlySpan<double> Nodes => [.9914553711208126392, .9491079123427585245,
        .8648644233597690728, .7415311855993944399, .5860872354676911303,
        .4058451513773971669, .2077849550078984676, 0];
    private static ReadOnlySpan<double> Kronrod => [.02293532201052922496, .06309209262997855329,
        .1047900103222501838, .1406532597155259187, .1690047266392679028,
        .1903505780647854099, .2044329400752988924, .2094821410847278280];
    private static ReadOnlySpan<double> Gauss => [.1294849661688696933, .2797053914892766679,
        .3818300505051189449, .4179591836734693878];

    private static Panel Evaluate(NurbsCurve curve, double a, double b, Budget budget)
    {
        var half = (b - a) / 2; var middle = a / 2 + b / 2;
        if (!(middle > a && middle < b)) throw new ArithmeticException("Curve-length interval cannot be resolved in binary64.");
        var center = budget.Speed(curve, middle); var k = Kronrod[7] * center; var g = Gauss[3] * center;
        Span<double> left = stackalloc double[7]; Span<double> right = stackalloc double[7];
        for (var i = 0; i < 7; i++)
        {
            var offset = half * Nodes[i];
            left[i] = budget.Speed(curve, middle - offset); right[i] = budget.Speed(curve, middle + offset);
            var pair = left[i] + right[i]; k += Kronrod[i] * pair;
            if ((i & 1) == 1) g += Gauss[i / 2] * pair;
        }
        var mean = k / 2; var asc = Kronrod[7] * Math.Abs(center - mean);
        for (var i = 0; i < 7; i++) asc += Kronrod[i] * (Math.Abs(left[i] - mean) + Math.Abs(right[i] - mean));
        var value = k * half; asc *= half; var error = Math.Abs((k - g) * half);
        if (asc > 0 && error > 0) error = asc * Math.Min(1, Math.Pow(200 * error / asc, 1.5));
        error = Math.Max(error, 50 * 2.2204460492503131e-16 * value);
        if (!double.IsFinite(value) || !double.IsFinite(error)) throw new ArithmeticException("Curve-length accumulation overflow.");
        return new(a, b, value, error);
    }

    internal static List<Panel> Integrate(NurbsCurve curve, Interval domain, double absolute, double relative,
        int maximumIntervals, Budget budget, bool splitKnots)
    {
        budget.Check();
        if (domain.Min == domain.Max) return [];
        var boundaries = splitKnots ? curve.Knots.ToArray().Where(k => k > domain.Min && k < domain.Max)
            .Prepend(domain.Min).Append(domain.Max).Distinct().ToArray() : [domain.Min, domain.Max];
        var queue = new PriorityQueue<Panel, double>();
        for (var i = 1; i < boundaries.Length; i++)
        {
            // Two panels per nonempty knot span reduce false convergence from
            // symmetric speed profiles; still an estimate, never a proof.
            var mid = boundaries[i - 1] / 2 + boundaries[i] / 2;
            if (!(mid > boundaries[i - 1] && mid < boundaries[i])) throw new ArithmeticException("Unresolvable parameter span.");
            Add(Evaluate(curve, boundaries[i - 1], mid, budget)); Add(Evaluate(curve, mid, boundaries[i], budget));
        }
        void Add(Panel panel)
        {
            if (queue.Count >= maximumIntervals) throw new ArithmeticException("Curve-length interval budget exceeded.");
            queue.Enqueue(panel, -panel.Error);
        }
        while (true)
        {
            budget.Check();
            var value = Sum(queue.UnorderedItems.Select(x => x.Element.Value));
            var error = Sum(queue.UnorderedItems.Select(x => x.Element.Error));
            if (error <= Math.Max(absolute, relative * value)) return queue.UnorderedItems.Select(x => x.Element).OrderBy(p => p.A).ToList();
            if (queue.Count >= maximumIntervals) throw new ArithmeticException("Curve-length tolerance was not met within the interval budget.");
            var worst = queue.Dequeue(); var mid = worst.A / 2 + worst.B / 2;
            if (!(mid > worst.A && mid < worst.B)) throw new ArithmeticException("Curve-length accuracy is limited by parameter precision.");
            Add(Evaluate(curve, worst.A, mid, budget)); Add(Evaluate(curve, mid, worst.B, budget));
        }
    }

    internal static double Sum(IEnumerable<double> numbers)
    {
        double sum = 0, correction = 0;
        foreach (var value in numbers) { var adjusted = value - correction; var next = sum + adjusted; correction = (next - sum) - adjusted; sum = next; }
        if (!double.IsFinite(sum)) throw new ArithmeticException("Curve-length sum overflow.");
        return sum;
    }
}

/// <summary>Reusable immutable cumulative-length table. Each inverse query uses
/// its own budget, analytic-speed Newton steps and a safeguarded bracket.</summary>
public sealed class CurveArcLengthMap
{
    private readonly NurbsCurve _curve;
    private readonly CurveLengthOptions _options;
    private readonly CurveMeasurements.Panel[] _panels;
    private readonly double[] _prefix;
    private readonly double[] _prefixErrors;
    public CurveLengthResult Measurement { get; }
    public double Length => Measurement.Length;
    internal CurveArcLengthMap(NurbsCurve curve, CurveLengthOptions options, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(curve);
        options.Validate(); CurveMeasurements.ValidateDomain(curve, curve.Domain);
        _curve = curve; _options = options;
        var budget = new CurveMeasurements.Budget(options.MaximumEvaluations, cancellation);
        _panels = CurveMeasurements.Integrate(curve, curve.Domain, options.AbsoluteTolerance / 8,
            options.RelativeTolerance / 8, options.MaximumIntervals, budget, true).ToArray();
        _prefix = new double[_panels.Length + 1]; _prefixErrors = new double[_panels.Length + 1]; double correction = 0;
        for (var i = 0; i < _panels.Length; i++)
        {
            var value = _panels[i].Value - correction; var next = _prefix[i] + value;
            correction = (next - _prefix[i]) - value; _prefix[i + 1] = next;
            _prefixErrors[i + 1] = _prefixErrors[i] + _panels[i].Error;
        }
        if (!double.IsFinite(_prefix[^1])) throw new ArithmeticException("Curve-length prefix overflow.");
        Measurement = new(_prefix[^1], CurveMeasurements.Sum(_panels.Select(p => p.Error)), budget.Evaluations, _panels.Length);
    }
    /// <summary>Relative inverse precision is measured against the requested
    /// world-unit length, not the length of the entire curve.</summary>
    public double ParameterAtLength(double length, CancellationToken cancellationToken = default)
        => Invert(length, Math.Max(_options.AbsoluteTolerance, _options.RelativeTolerance * length), cancellationToken);

    private double Invert(double length, double tolerance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(length) || length < 0 || length > Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return _curve.Domain.Min;
        if (length == Length) return _curve.Domain.Max;
        var lo = 0; var hi = _panels.Length;
        while (lo < hi) { var middle = lo + (hi - lo) / 2; if (_prefix[middle + 1] < length) lo = middle + 1; else hi = middle; }
        var panel = _panels[lo];
        // A whole-curve relative integration target may not be sufficient for
        // a much shorter requested length. Use the table only when its prefix
        // error meets this query's target; otherwise re-integrate from the
        // actual curve start with a stricter absolute target.
        var local = _prefixErrors[lo + 1] <= tolerance / 8;
        var target = local ? length - _prefix[lo] : length;
        var origin = local ? panel.A : _curve.Domain.Min;
        var baseError = local ? _prefixErrors[lo] : 0;
        if (local && target + baseError <= tolerance) return panel.A;
        if (local && Math.Abs(panel.Value - target) + _prefixErrors[lo + 1] <= tolerance) return panel.B;
        var a = origin; var b = local ? panel.B : _curve.Domain.Max;
        var t = panel.A + (panel.B - panel.A) * ((length - _prefix[lo]) / panel.Value);
        var budget = new CurveMeasurements.Budget(_options.MaximumEvaluations, cancellationToken);
        for (var iteration = 0; iteration < _options.MaximumInverseIterations; iteration++)
        {
            budget.Check();
            if (!(t > a && t < b)) t = a / 2 + b / 2;
            if (!(t > a && t < b)) throw new ArithmeticException("Inverse arc length is limited by parameter precision.");
            var partial = CurveMeasurements.Integrate(_curve, new(origin, t), tolerance / 16, 0,
                _options.MaximumIntervals, budget, !local);
            var residual = CurveMeasurements.Sum(partial.Select(p => p.Value)) - target;
            var error = CurveMeasurements.Sum(partial.Select(p => p.Error)) + baseError;
            if (Math.Abs(residual) + error <= tolerance) return t;
            if (residual > 0) b = t; else a = t;
            var speed = budget.Speed(_curve, t); var next = speed > 0 ? t - residual / speed : double.NaN;
            // A central bracket guard guarantees progress near stationary tangents.
            var margin = (b - a) * .05;
            t = double.IsFinite(next) && next > a + margin && next < b - margin ? next : a / 2 + b / 2;
        }
        throw new ArithmeticException("Inverse arc-length iteration budget exceeded.");
    }
    public CurveDivision DivideByCount(int count, bool includeEnds = true, CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 100_000) throw new ArgumentOutOfRangeException(nameof(count));
        if (!(Length > 0)) throw new InvalidOperationException("A zero-length curve has no unique equal-length division.");
        var inverseTolerance = Math.Max(_options.AbsoluteTolerance, _options.RelativeTolerance * (Length / count)) / 4;
        var size = includeEnds ? count + 1 : count - 1; var parameters = new double[size]; var points = new Vec3[size];
        for (var i = 0; i < size; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = includeEnds ? i : i + 1;
            parameters[i] = step == count ? _curve.Domain.Max : Invert(Length * (step / (double)count), inverseTolerance, cancellationToken);
            points[i] = _curve.PointAt(parameters[i]);
        }
        return new(parameters, points, Measurement);
    }
}
