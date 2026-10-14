namespace NurbsScene.Presentation;

/// <summary>Pure geometry for the panel's vertical item scrollbar. Offsets and
/// grip travel are clamped independently; tiny tracks never divide by zero.</summary>
public readonly record struct PanelScrollBar(CanvasBox Track, CanvasBox Grip, int Offset, int Maximum)
{
    public static PanelScrollBar Create(CanvasBox track, PanelWindow window, double minimumGrip)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!double.IsFinite(track.X) || !double.IsFinite(track.Y) || !double.IsFinite(track.Width) || !double.IsFinite(track.Height) ||
            track.Width <= 0 || track.Height <= 0 || !double.IsFinite(track.X + track.Width) || !double.IsFinite(track.Y + track.Height) ||
            !double.IsFinite(minimumGrip) || minimumGrip <= 0 ||
            window.First < 0 || window.MaximumOffset < window.First || window.Total < 0 || window.Visible < 0 || window.Visible > window.Total)
            throw new ArgumentException("Invalid scrollbar geometry or item window.");
        // Form bounded fractions before scaling: finite extreme tracks must not
        // overflow intermediate products and fabricate a full-height grip.
        var height = Math.Clamp(track.Height * (window.Visible / (double)Math.Max(1, window.Total)), Math.Min(minimumGrip, track.Height), track.Height);
        var y = track.Y + (track.Height - height) * (window.First / (double)Math.Max(1, window.MaximumOffset));
        return new(track, new(track.X, y, track.Width, height), window.First, window.MaximumOffset);
    }
    public int Drag(double pointerY, double grabOffset)
    {
        if (!double.IsFinite(pointerY) || !double.IsFinite(grabOffset)) throw new ArgumentException("Finite pointer coordinates required.");
        var travel = Track.Height - Grip.Height;
        if (Maximum <= 0 || travel <= 0) return Offset;
        var ratio = Math.Clamp((pointerY - Track.Y - Math.Clamp(grabOffset, 0, Grip.Height)) / travel, 0, 1);
        return (int)Math.Round(ratio * Maximum, MidpointRounding.AwayFromZero);
    }
    public static int Move(int offset, long delta, int maximum)
    {
        if (offset < 0 || maximum < offset) throw new ArgumentOutOfRangeException(nameof(offset));
        // Saturation before addition also handles long.MinValue/MaxValue.
        if (delta <= -offset) return 0;
        if (delta >= (long)maximum - offset) return maximum;
        return offset + (int)delta;
    }
}
