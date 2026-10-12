namespace NurbsScene.Kernel;

/// <summary>Owned scaled-pivot LU factors for multiple interpolation right-hand
/// sides. The matrix is factored once; the immutable factors can solve independent
/// RHS buffers. This is an internal implementation, not a general LAPACK binding.</summary>
internal sealed class InterpolationFactors
{
    private readonly double[,] _lu;
    private readonly int[] _pivots, _firstLower, _lastUpper;
    private readonly int _count;
    public InterpolationFactors(double[,] matrix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        cancellationToken.ThrowIfCancellationRequested();
        _count = matrix.GetLength(0);
        if (_count < 1 || matrix.GetLength(1) != _count)
            throw new ArgumentException("A nonempty square interpolation matrix is required.", nameof(matrix));
        _lu = matrix; // Caller transfers its private scratch matrix, no public alias.
        _pivots = new int[_count]; _firstLower = new int[_count]; _lastUpper = new int[_count];
        var scales = new double[_count];
        for (var i = 0; i < _count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 0; j < _count; j++)
            {
                var value = matrix[i, j];
                if (!double.IsFinite(value)) throw new ArithmeticException("Non-finite interpolation coefficient.");
                scales[i] = Math.Max(scales[i], Math.Abs(value));
                if (value != 0) _lastUpper[i] = j;
            }
        }
        for (var k = 0; k < _count; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pivot = k; var best = -1d;
            for (var i = k; i < _count; i++)
            {
                var score = scales[i] == 0 ? 0 : Math.Abs(matrix[i, k]) / scales[i];
                if (score > best) { best = score; pivot = i; }
            }
            // Retain the former solver's scaled pivot diagnostic, not a claim
            // of certified conditioning or exact arithmetic.
            if (!double.IsFinite(best) || best <= 1e-14) throw new ArithmeticException("Singular interpolation system.");
            _pivots[k] = pivot;
            if (pivot != k)
            {
                // Include earlier L entries; applying all permutations to RHS
                // before forward substitution requires the corresponding row L.
                for (var j = 0; j < _count; j++) (matrix[pivot, j], matrix[k, j]) = (matrix[k, j], matrix[pivot, j]);
                (scales[pivot], scales[k]) = (scales[k], scales[pivot]);
                (_lastUpper[pivot], _lastUpper[k]) = (_lastUpper[k], _lastUpper[pivot]);
            }
            for (var i = k + 1; i < _count; i++)
            {
                if (matrix[i, k] == 0) continue;
                var multiplier = matrix[i, k] / matrix[k, k];
                if (!double.IsFinite(multiplier)) throw new ArithmeticException("Non-finite interpolation factor.");
                matrix[i, k] = multiplier;
                // B-spline collocation has local support. Track fill after row
                // pivoting rather than assuming a fixed bandwidth is preserved.
                _lastUpper[i] = Math.Max(_lastUpper[i], _lastUpper[k]);
                for (var j = k + 1; j <= _lastUpper[k]; j++)
                {
                    matrix[i, j] -= multiplier * matrix[k, j];
                    if (!double.IsFinite(matrix[i, j])) throw new ArithmeticException("Non-finite interpolation factor.");
                }
            }
        }
        for (var i = 0; i < _count; i++)
        {
            var first = 0;
            while (first < i && matrix[i, first] == 0) first++;
            _firstLower[i] = first;
        }
    }

    public void Solve(double[,] rhs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rhs);
        cancellationToken.ThrowIfCancellationRequested();
        if (rhs.GetLength(0) != _count || rhs.GetLength(1) < 1)
            throw new ArgumentException("RHS row count must match the interpolation system.", nameof(rhs));
        var columns = rhs.GetLength(1);
        for (var i = 0; i < _count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var c = 0; c < columns; c++)
                if (!double.IsFinite(rhs[i, c])) throw new ArithmeticException("Non-finite interpolation RHS.");
            var pivot = _pivots[i];
            if (pivot != i)
                for (var c = 0; c < columns; c++) (rhs[pivot, c], rhs[i, c]) = (rhs[i, c], rhs[pivot, c]);
        }
        for (var i = 0; i < _count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = _firstLower[i]; j < i; j++)
            {
                var factor = _lu[i, j];
                if (factor == 0) continue;
                for (var c = 0; c < columns; c++) rhs[i, c] -= factor * rhs[j, c];
            }
        }
        for (var i = _count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var c = 0; c < columns; c++)
            {
                var value = rhs[i, c];
                for (var j = i + 1; j <= _lastUpper[i]; j++) value -= _lu[i, j] * rhs[j, c];
                value /= _lu[i, i];
                if (!double.IsFinite(value)) throw new ArithmeticException("Non-finite interpolation result.");
                rhs[i, c] = value;
            }
        }
    }
}
