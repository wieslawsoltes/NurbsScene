namespace NurbsScene.Presentation;

/// <summary>Use the same baseline/ink clip as PanelTextPlacementCache, but retain
/// at least one record so tiny views remain bounded and can report truncation.</summary>
public static class PanelWindowLayout
{
    public static int RowBudget(double height, PanelDisplayLayout layout, PanelFontMetrics metrics)
    {
        metrics.Validate();
        if (!double.IsFinite(height) || height <= 0 || !double.IsFinite(layout.FirstBaseline) || layout.FirstBaseline < 0 ||
            !double.IsFinite(layout.FooterInset) || layout.FooterInset < 0 || !double.IsFinite(layout.FontSize) || layout.FontSize <= 0 ||
            !double.IsFinite(layout.LineHeight) || layout.LineHeight <= 0 || layout.Rows < 1 || layout.Rows > PanelPreviewCache.MaximumRows)
            throw new ArgumentException("A finite panel layout is required.");
        var top = Math.Min(height, Math.Max(0, layout.FirstBaseline + metrics.Ascent));
        var bottom = Math.Max(top, height - layout.FooterInset - Math.Max(metrics.Descent, layout.FontSize));
        var ink = metrics.Descent - metrics.Ascent; var step = Math.Max(layout.LineHeight, ink);
        // Clamp in floating point before conversion, including extreme heights.
        return (int)Math.Clamp(1 + Math.Floor((bottom - top - ink) / step), 1, layout.Rows);
    }
}
