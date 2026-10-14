namespace NurbsScene.Presentation;

/// <summary>The finite, single-precision transform consumed by the native canvas.
/// Numerical geometry and document coordinates remain binary64 and are never
/// changed by navigation. Default(GraphCanvasView) is not a valid view.</summary>
public readonly record struct GraphCanvasView(float Zoom, float PanX, float PanY);

/// <summary>Anchored canvas navigation, independent of routed pointer events.
/// Every result is validated before a caller changes its live view. A drag is
/// evaluated from its original view/anchor, not accumulated from coalesced moves.
/// Ctrl+right-drag zoom follows the original canvas shortcut described at
/// https://www.grasshopper3d.com/forum/topics/what-hotkeys-and-shortcuts-are-available-in-grasshopper
/// The vertical sensitivity is independently chosen, not an original binary API.</summary>
public static class GraphCanvasNavigation
{
    public const float MinimumZoom = .15f;
    public const float MaximumZoom = 3.5f;

    public static GraphCanvasView ZoomAt(GraphCanvasView origin, CanvasPoint anchor, double zoom)
    {
        Validate(origin); Validate(anchor);
        if (!double.IsFinite(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom), "Finite zoom required.");
        var scale = (float)Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        // Do not round-trip translations when already at a clamp or on a no-op.
        if (scale == origin.Zoom) return origin;
        var worldX = (anchor.X - origin.PanX) / origin.Zoom;
        var worldY = (anchor.Y - origin.PanY) / origin.Zoom;
        return Checked(scale, anchor.X - worldX * scale, anchor.Y - worldY * scale);
    }

    public static GraphCanvasView ZoomBy(GraphCanvasView origin, CanvasPoint anchor, double logarithmicDelta)
    {
        Validate(origin); Validate(anchor);
        if (!double.IsFinite(logarithmicDelta))
            throw new ArgumentOutOfRangeException(nameof(logarithmicDelta), "Finite zoom displacement required.");
        if (logarithmicDelta == 0) return origin;
        // Clamp in log space before exponentiation. Even a finite, enormous
        // wheel/drag delta cannot create Infinity or a zero-scale transform.
        var log = Math.Log(origin.Zoom) + logarithmicDelta;
        var scale = log <= Math.Log(MinimumZoom) ? MinimumZoom
            : log >= Math.Log(MaximumZoom) ? MaximumZoom : Math.Exp(log);
        return ZoomAt(origin, anchor, scale);
    }

    public static GraphCanvasView PanBy(GraphCanvasView origin, CanvasPoint displacement)
    {
        Validate(origin); Validate(displacement);
        if (displacement == default) return origin;
        return Checked(origin.Zoom, origin.PanX + displacement.X, origin.PanY + displacement.Y);
    }

    /// <summary>Upward right-button dragging zooms in around the pointer-down
    /// anchor. Horizontal motion does not change magnification. Releasing Control
    /// during a captured zoom does not change that gesture into panning.</summary>
    public static GraphCanvasView DragZoom(GraphCanvasView origin, CanvasPoint anchor, CanvasPoint current)
    {
        Validate(current);
        return ZoomBy(origin, anchor, (anchor.Y - current.Y) * .01);
    }

    private static GraphCanvasView Checked(float zoom, double panX, double panY)
    {
        var x = (float)panX; var y = (float)panY;
        if (!float.IsFinite(x) || !float.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(panX), "Navigation exceeds the finite canvas rendering range.");
        return new(zoom, x, y);
    }

    private static void Validate(GraphCanvasView view)
    {
        if (!float.IsFinite(view.Zoom) || view.Zoom < MinimumZoom || view.Zoom > MaximumZoom
            || !float.IsFinite(view.PanX) || !float.IsFinite(view.PanY))
            throw new ArgumentOutOfRangeException(nameof(view), "A finite bounded canvas view is required.");
    }

    private static void Validate(CanvasPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point), "Finite canvas coordinates required.");
    }
}
