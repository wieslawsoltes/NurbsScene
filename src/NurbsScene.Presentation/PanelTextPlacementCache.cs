using System.Collections.Immutable;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Font measurements use baseline coordinates: ascent is non-positive,
/// descent non-negative. The same font/size must measure and render each row.</summary>
public readonly record struct PanelFontMetrics(double Ascent, double Descent)
{
    public void Validate()
    {
        if (!double.IsFinite(Ascent) || !double.IsFinite(Descent) || Ascent > 0 || Descent < 0 ||
            Descent - Ascent <= 0 || Descent - Ascent > 1024)
            throw new ArgumentException("Invalid font metrics.");
    }
}
public readonly record struct PanelTextRow(string Text, double X, double Baseline, double Advance);
public sealed record PanelTextPlacement(ImmutableArray<PanelTextRow> Rows, CanvasBox Clip, bool Clipped);

/// <summary>Owner-thread, retained local-coordinate row placement. Translation,
/// selection, panning and unrelated solves reuse placement. Alignment edits reuse
/// measured widths. At most 64 rows are measured; no native font is owned here.</summary>
public sealed class PanelTextPlacementCache
{
    private ImmutableArray<string> _lines;
    private ImmutableArray<double> _advances;
    private long _fontIdentity;
    private double _size;
    private PanelHorizontalAlignment _horizontal;
    private PanelVerticalAlignment _vertical;
    private PanelDisplayLayout _layout;
    private PanelFontMetrics _metrics;
    private double _width, _height;
    private PanelTextPlacement? _placement;
    public long MeasurementBuilds { get; private set; }
    public long LayoutBuilds { get; private set; }
    public long MeasuredRows { get; private set; }
    public PanelTextPlacement? Current => _placement;

    public PanelTextPlacement Get(PanelPreview preview, PanelTextStyle style, double width, double height,
        PanelDisplayLayout layout, PanelFontMetrics metrics, long fontIdentity, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(preview); ArgumentNullException.ThrowIfNull(style); ArgumentNullException.ThrowIfNull(measure);
        // Full family validation is done at graph/import/edit boundaries, not by
        // re-enumerating the immutable family on every paint.
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            !double.IsFinite(layout.FontSize) || layout.FontSize <= 0 || layout.FontSize > 1024 || !double.IsFinite(layout.LineHeight) || layout.LineHeight <= 0 ||
            !double.IsFinite(layout.FirstBaseline) || layout.FirstBaseline < 0 || !double.IsFinite(layout.FooterInset) || layout.FooterInset < 0 ||
            layout.Rows < 1 || layout.Rows > PanelPreviewCache.MaximumRows || layout.Columns < 1 || layout.Columns > PanelPreviewCache.MaximumColumns ||
            !Enum.IsDefined(style.Horizontal) || !Enum.IsDefined(style.Vertical)) throw new ArgumentException("Invalid text placement layout.");
        metrics.Validate();
        if (preview.Lines.IsDefault || preview.Lines.Length > PanelPreviewCache.MaximumRows) throw new ArgumentException("Invalid preview row budget.");
        var remeasure = _advances.IsDefault || _lines != preview.Lines || _fontIdentity != fontIdentity || _size != layout.FontSize;
        if (!remeasure && _placement is not null && _horizontal == style.Horizontal && _vertical == style.Vertical &&
            _width == width && _height == height && _layout == layout && _metrics == metrics) return _placement;
        // Prepare before publishing: a throwing/invalid native measurement cannot
        // poison the prior cache or publish a half-measured preview.
        var advances = _advances;
        if (remeasure)
        {
            var builder = ImmutableArray.CreateBuilder<double>(preview.Lines.Length);
            foreach (var text in preview.Lines)
            {
                if (text is null || text.Length > 16384) throw new ArgumentException("Invalid display row.");
                var advance = measure(text);
                if (!double.IsFinite(advance) || advance < 0 || advance > 1e9) throw new ArgumentException("Invalid measured advance.");
                builder.Add(advance);
            }
            advances = builder.MoveToImmutable();
        }
        var top = Math.Min(height, Math.Max(0, layout.FirstBaseline + metrics.Ascent));
        var bottom = Math.Max(top, height - layout.FooterInset - Math.Max(metrics.Descent, layout.FontSize));
        var clip = new CanvasBox(Math.Min(8, width / 2), top, Math.Max(0, width - 16), bottom - top);
        var step = Math.Max(layout.LineHeight, metrics.Descent - metrics.Ascent);
        var available = clip.Height;
        var count = available < metrics.Descent - metrics.Ascent || clip.Width <= 0 ? 0 :
            Math.Min(preview.Lines.Length, 1 + (int)Math.Min(PanelPreviewCache.MaximumRows, Math.Floor((available - (metrics.Descent - metrics.Ascent)) / step)));
        var block = count == 0 ? 0 : metrics.Descent - metrics.Ascent + (count - 1) * step;
        var start = top + Math.Max(0, available - block) * (int)style.Vertical / 2 - metrics.Ascent;
        var rows = ImmutableArray.CreateBuilder<PanelTextRow>(count);
        var clipped = count < preview.Lines.Length;
        for (var i = 0; i < count; i++)
        {
            // Oversize rows stay left-anchored so their beginning remains visible;
            // the actual body clip, not an ellipsis inserted into the data, clips ink.
            var x = clip.X + Math.Max(0, clip.Width - advances[i]) * (int)style.Horizontal / 2;
            rows.Add(new(preview.Lines[i], x, start + i * step, advances[i]));
            clipped |= advances[i] > clip.Width;
        }
        var result = new PanelTextPlacement(rows.MoveToImmutable(), clip, clipped);
        if (remeasure) { MeasurementBuilds++; MeasuredRows += preview.Lines.Length; }
        _lines = preview.Lines; _advances = advances; _fontIdentity = fontIdentity; _size = layout.FontSize;
        _width = width; _height = height; _layout = layout; _metrics = metrics;
        _horizontal = style.Horizontal; _vertical = style.Vertical; _placement = result; LayoutBuilds++;
        return result;
    }
}
