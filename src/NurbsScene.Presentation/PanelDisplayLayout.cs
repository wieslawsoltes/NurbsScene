namespace NurbsScene.Presentation;

/// <summary>Bounded panel text layout in canvas units. Large panels remain useful
/// when zoomed out: body text has an eight-pixel floor instead of inheriting the
/// ordinary component-detail cutoff. Small panels still use the cheap overview.</summary>
public readonly record struct PanelDisplayLayout(int Rows, int Columns, double FontSize,
    double LineHeight, double HeadingBaseline, double FirstBaseline, double FooterInset, bool Readable)
{
    public static PanelDisplayLayout Create(CanvasBox bounds, double zoom) => Create(bounds, zoom, 10);
    public static PanelDisplayLayout Create(CanvasBox bounds, double zoom, double requestedSize)
    {
        if (!double.IsFinite(requestedSize) || requestedSize < 6 || requestedSize > 72)
            throw new ArgumentOutOfRangeException(nameof(requestedSize));
        if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(zoom) || zoom < .1 || zoom > 10)
            throw new ArgumentOutOfRangeException(nameof(zoom), "Finite positive bounds and zoom from 0.1 to 10 required.");
        var font = Math.Max(requestedSize, 8 / zoom);
        var scale = font / 10;
        var rowCount = Math.Clamp(Math.Floor((bounds.Height - 48 * scale) / (13 * scale)), 1, PanelPreviewCache.MaximumRows);
        var columnCount = Math.Clamp(Math.Floor((bounds.Width - 16) / (6 * scale)), 1, PanelPreviewCache.MaximumColumns);
        return new((int)rowCount, (int)columnCount, font, 13 * scale, 15 * scale, 32 * scale, 7 * scale,
            bounds.Width * zoom >= 80 && bounds.Height * zoom >= 55);
    }
}
