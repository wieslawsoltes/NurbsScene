namespace NurbsScene.Presentation;

/// <summary>Screen-space grid generation with bounded integer loop counts.
/// World-coordinate float stepping can stop advancing once a grid interval is
/// smaller than one representable float unit. Large panning offsets must never
/// turn a paint callback into an infinite loop.</summary>
public readonly record struct CanvasGridLayout(double Spacing, double OffsetX, double OffsetY, int Columns, int Rows)
{
    public const int MaximumLinesPerAxis = 2048;

    /// <summary>Returns false for invalid transforms, without throwing from a
    /// rendering callback. Empty viewports are valid and contain no grid lines.
    /// Very large viewports use a coarser grid to retain a fixed work budget.</summary>
    public static bool TryCreate(double width, double height, double zoom, double panX, double panY, out CanvasGridLayout grid)
    {
        grid = default;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 ||
            !double.IsFinite(zoom) || zoom <= 0 || !double.IsFinite(panX) || !double.IsFinite(panY))
            return false;
        if (width == 0 || height == 0) return true;
        var spacing = (zoom > .45 ? 25 : 100) * zoom;
        if (!double.IsFinite(spacing) || spacing <= 0) return false;
        // Subtraction of huge world coordinates and repeated float addition are
        // both avoided. The loop count depends on the viewport, not world position.
        spacing = Math.Max(spacing, Math.Max(width, height) / (MaximumLinesPerAxis - 1));
        static double Phase(double pan, double step)
        {
            var phase = pan % step;
            if (phase < 0) phase += step;
            return phase >= step ? 0 : phase; // rounding at the negative boundary
        }
        static int Count(double size, double phase, double step) => phase > size ? 0 :
            Math.Min(MaximumLinesPerAxis, 1 + (int)Math.Floor((size - phase) / step));
        var x = Phase(panX, spacing); var y = Phase(panY, spacing);
        grid = new(spacing, x, y, Count(width, x, spacing), Count(height, y, spacing));
        return true;
    }
}
