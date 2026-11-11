using System.Collections.Immutable;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
#if NURBSSCENE_SKIA_RASTER_TEST
using VectorCanvas = SkiaSharp.SKCanvas;
using VectorFont = SkiaSharp.SKFont;
using VectorFontStyle = SkiaSharp.SKFontStyle;
using VectorFontStyleSlant = SkiaSharp.SKFontStyleSlant;
using VectorFontStyleWeight = SkiaSharp.SKFontStyleWeight;
using VectorFontStyleWidth = SkiaSharp.SKFontStyleWidth;
using VectorPaint = SkiaSharp.SKPaint;
using VectorRect = SkiaSharp.SKRect;
using VectorTextBlob = SkiaSharp.SKTextBlob;
using VectorTypeface = SkiaSharp.SKTypeface;
#else
using NurbsScene.Controls.Drawing;
#endif

namespace NurbsScene.Controls;

public sealed record PanelRenderedTypography(PanelTextStyle Style, string ResolvedFamily, double FontSize,
    PanelFontMetrics Metrics, PanelTextPlacement Placement);

/// <summary>Reusable owner-thread GPU vector panel text renderer. Text blobs retain glyph
/// runs; movement/alignment/selection never rebuild identical runs. Native caches
/// are bounded and disposed on eviction; the caller retains its authoritative tree.</summary>
public sealed class PanelTypographyRenderer : IDisposable
{
    public const int MaximumPanels = 128, MaximumTypefaces = 16;
    private readonly record struct FaceKey(string Family, bool Bold, bool Italic);
    private sealed class Face(VectorTypeface value, long identity, bool owned)
    {
        public VectorTypeface Value { get; } = value;
        public long Identity { get; } = identity;
        public bool Owned { get; } = owned;
        public long Used;
    }
    private sealed class Entry : IDisposable
    {
        public readonly PanelTextPlacementCache Cache = new();
        public ImmutableArray<string> Lines;
        public long FaceIdentity, Used;
        public double FontSize;
        public VectorTextBlob?[] Blobs = [];
        public PanelRenderedTypography? Rendered;
        public PanelPaintStamp? Source;
        public void Dispose() { foreach (var blob in Blobs) blob?.Dispose(); Blobs = []; }
    }
    private readonly Dictionary<FaceKey, Face> _faces = [];
    private readonly Dictionary<string, Entry> _panels = [];
    private readonly VectorFont _font = new(VectorTypeface.Default, 10);
    private readonly VectorPaint _paint = new() { IsAntialias = true, Color = new(62, 60, 40) };
    private readonly Func<string, double> _measure;
    private long _clock, _identity;
    private bool _disposed;
    private PanelTextStyle? _validatedStyle;
    public long GlyphBuilds { get; private set; }
    public long MeasurementBuilds { get; private set; }
    public long PlacementBuilds { get; private set; }
    public int RetainedPanels => _panels.Count;
    public int RetainedTypefaces => _faces.Count;
    public PanelTypographyRenderer() => _measure = text => _font.MeasureText(text);
    public PanelRenderedTypography? Observe(string id) => _panels.GetValueOrDefault(id)?.Rendered;
    /// <summary>Only a successful draw publishes its source stamp. Reading it never
    /// evaluates a graph or paints, and a previous identical-looking frame fails.</summary>
    public bool IsCurrent(string id, PanelPaintStamp source) => _panels.TryGetValue(id, out var entry) &&
        entry.Source is { } painted && painted.Matches(source);
    public void Forget(string id) { if (_panels.Remove(id, out var entry)) entry.Dispose(); }
    public void Clear() { foreach (var e in _panels.Values) e.Dispose(); _panels.Clear(); }

    public PanelRenderedTypography Draw(VectorCanvas canvas, string id, PanelPreview preview, PanelTextStyle style,
        VectorRect bounds, PanelDisplayLayout layout, PanelPaintStamp? source = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(preview); ArgumentNullException.ThrowIfNull(style);
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) ||
            !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(layout.FontSize) || layout.FontSize <= 0 || layout.FontSize > 1024) throw new ArgumentException("Invalid native text bounds or font size.");
        var face = ConfigureFont(style, layout.FontSize);
        var fm = _font.Metrics;
        var metrics = new PanelFontMetrics(fm.Ascent, fm.Descent);
        if (!_panels.TryGetValue(id, out var entry))
        {
            if (_panels.Count == MaximumPanels) Forget(_panels.MinBy(p => p.Value.Used).Key);
            _panels.Add(id, entry = new());
        }
        entry.Used = ++_clock;
        var oldMeasures = entry.Cache.MeasurementBuilds; var oldLayouts = entry.Cache.LayoutBuilds;
        var placement = entry.Cache.Get(preview, style, bounds.Width, bounds.Height, layout, metrics, face.Identity, _measure);
        MeasurementBuilds += entry.Cache.MeasurementBuilds - oldMeasures;
        PlacementBuilds += entry.Cache.LayoutBuilds - oldLayouts;
        var sameLines = entry.Lines == preview.Lines || (!entry.Lines.IsDefault && entry.Lines.AsSpan().SequenceEqual(preview.Lines.AsSpan()));
        if (!sameLines || entry.FaceIdentity != face.Identity || entry.FontSize != layout.FontSize)
        {
            var blobs = new VectorTextBlob?[preview.Lines.Length];
            try
            {
                for (var i = 0; i < blobs.Length; i++)
                    if (preview.Lines[i].Length > 0) blobs[i] = VectorTextBlob.Create(preview.Lines[i], _font) ?? throw new InvalidOperationException("Native panel glyph creation failed.");
            }
            catch { foreach (var blob in blobs) blob?.Dispose(); throw; }
            entry.Dispose(); entry.Blobs = blobs; entry.Lines = preview.Lines;
            entry.FaceIdentity = face.Identity; entry.FontSize = layout.FontSize; GlyphBuilds++;
        }
        else entry.Lines = preview.Lines; // adopt equal rows without repeated content scans
        if (entry.Rendered is null || !ReferenceEquals(entry.Rendered.Placement, placement) || entry.Rendered.Style != style)
            entry.Rendered = new(style, face.Value.FamilyName, layout.FontSize, metrics, placement);
        canvas.Save();
        try
        {
            canvas.Translate(bounds.Left, bounds.Top);
            var clip = placement.Clip;
            canvas.ClipRect(new((float)clip.X, (float)clip.Y, (float)(clip.X + clip.Width), (float)(clip.Y + clip.Height)));
            for (var i = 0; i < placement.Rows.Length; i++)
            {
                var row = placement.Rows[i];
                if (entry.Blobs[i] is { } blob) canvas.DrawText(blob, (float)row.X, (float)row.Baseline, _paint);
            }
        }
        finally { canvas.Restore(); }
        entry.Source = source;
        return entry.Rendered;
    }
    /// <summary>Resolve the same native font used by Draw before selecting the
    /// visible item window. No glyphs, placements or completed-paint stamps are
    /// published by measurement alone. Warm calls reuse the bounded face cache.</summary>
    public PanelFontMetrics GetFontMetrics(PanelTextStyle style, double size)
    {
        ConfigureFont(style, size);
        var fm = _font.Metrics;
        return new(fm.Ascent, fm.Descent);
    }
    private Face ConfigureFont(PanelTextStyle style, double size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(style);
        if (!double.IsFinite(size) || size <= 0 || size > 1024)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!ReferenceEquals(_validatedStyle, style)) { style.Validate(); _validatedStyle = style; }
        var key = new FaceKey(style.FontFamily, style.Bold, style.Italic);
        if (!_faces.TryGetValue(key, out var face))
        {
            if (_faces.Count == MaximumTypefaces)
            {
                var victim = _faces.MinBy(p => p.Value.Used);
                _font.Typeface = VectorTypeface.Default; _faces.Remove(victim.Key);
                if (victim.Value.Owned && !_faces.Values.Any(other => ReferenceEquals(other.Value, victim.Value.Value))) victim.Value.Value.Dispose();
            }
            using var requested = new VectorFontStyle(style.Bold ? VectorFontStyleWeight.Bold : VectorFontStyleWeight.Normal,
                VectorFontStyleWidth.Normal, style.Italic ? VectorFontStyleSlant.Italic : VectorFontStyleSlant.Upright);
            var value = VectorTypeface.FromFamilyName(style.FontFamily.Length == 0 ? null : style.FontFamily, requested) ?? VectorTypeface.Default;
            _faces.Add(key, face = new(value, ++_identity, !ReferenceEquals(value, VectorTypeface.Default)));
        }
        face.Used = ++_clock; _font.Typeface = face.Value; _font.Size = (float)size;
        // Some hosts only have a regular face. Synthetic style is explicit and
        // applied to both native measurements and the retained glyph run.
        _font.Embolden = style.Bold && !face.Value.IsBold;
        _font.SkewX = style.Italic && !face.Value.IsItalic ? -.2f : 0;
        return face;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Clear(); _font.Dispose(); _paint.Dispose();
        foreach (var value in _faces.Values.Where(face => face.Owned).Select(face => face.Value).Distinct()) value.Dispose(); _faces.Clear();
    }
}
