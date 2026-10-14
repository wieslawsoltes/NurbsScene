using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

namespace NurbsScene.Controls;

public readonly record struct PanelWindowRenderPlan(SKRect Body, PanelDisplayLayout Layout,
    PanelWindow Window, double ReservedWidth);

/// <summary>Measured, bounded preparation for a single panel text paint. Resolves
/// metrics before choosing the item window; it never draws a provisional layout.
/// Pure view preparation: graph, history and completed-paint stamps are untouched.</summary>
public static class PanelWindowRenderer
{
    public static PanelWindowRenderPlan Prepare(PanelTypographyRenderer typography, PanelWindowCache cache,
        GraphNode node, NodeResult? result, SKRect bounds, double zoom, PanelTextStyle style,
        int first, bool hadScrollbar)
    {
        ArgumentNullException.ThrowIfNull(typography); ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(style);
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom))
            throw new ArgumentException("Finite native panel bounds required.", nameof(bounds));
        var original = PanelDisplayLayout.Create(new(0, 0, bounds.Width, bounds.Height), zoom, style.Size);
        var metrics = typography.GetFontMetrics(style, original.FontSize);
        var reserve = Math.Max(10, 10 / zoom) + 4;
        var plan = Window(cache, node, result, bounds, zoom, style, metrics, first, hadScrollbar, reserve);
        if (plan.Window.CanScroll != hadScrollbar)
            plan = Window(cache, node, result, bounds, zoom, style, metrics, first, plan.Window.CanScroll, reserve);
        return plan;
    }
    private static PanelWindowRenderPlan Window(PanelWindowCache cache, GraphNode node, NodeResult? result,
        SKRect bounds, double zoom, PanelTextStyle style, PanelFontMetrics metrics, int first, bool scroll, double reserve)
    {
        var body = bounds;
        if (scroll) body.Right = Math.Max(body.Left + 1, body.Right - (float)reserve);
        var layout = PanelDisplayLayout.Create(new(0, 0, body.Width, body.Height), zoom, style.Size);
        layout = layout with { Rows = PanelWindowLayout.RowBudget(body.Height, layout, metrics) };
        var window = cache.Get(node, result, layout.Rows, layout.Columns, first);
        return new(body, layout, window, reserve);
    }
}
