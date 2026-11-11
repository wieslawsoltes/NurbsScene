using System.Numerics;
using Uno.UI.Composition.Drawing;
using Uno.UI.Composition.Managed;
using Windows.UI.Text;

namespace NurbsScene.Controls.Drawing;

public enum VectorFontStyleWeight { Normal, Bold }
public enum VectorFontStyleWidth { Normal }
public enum VectorFontStyleSlant { Upright, Italic }

public sealed class VectorFontStyle(VectorFontStyleWeight weight, VectorFontStyleWidth width, VectorFontStyleSlant slant) : IDisposable
{
    internal VectorFontStyleWidth Width { get; } = width;
    internal bool Bold => weight == VectorFontStyleWeight.Bold;
    internal bool Italic => slant == VectorFontStyleSlant.Italic;
    public void Dispose() { }
}

/// <summary>
/// Managed font resolution and HarfBuzz shaping, followed by WebGPU glyph rendering. Bundled fonts make
/// browser rendering deterministic and offline-capable; installed families remain usable on desktop.
/// </summary>
public sealed class VectorTypeface : IDisposable
{
    private static readonly IFontProvider Provider = ManagedBackend.CreateFontProvider();
    private static readonly Dictionary<(bool Bold, bool Italic), IFontFile> Bundled = [];
    private readonly Dictionary<float, IFont> _fonts = [];
    private readonly string _family;
    private readonly bool _requestedBold, _requestedItalic;
    private (bool Bold, bool Italic)? _resolvedStyle;
    public static VectorTypeface Default { get; } = new("", false, false);
    public bool IsBold => ResolvedStyle.Bold;
    public bool IsItalic => ResolvedStyle.Italic;
    private (bool Bold, bool Italic) ResolvedStyle => _resolvedStyle ??= ReadStyle(Resolve(12));
    public string FamilyName => Resolve(12).FamilyName;
    private VectorTypeface(string family, bool bold, bool italic)
    { _family = family; _requestedBold = bold; _requestedItalic = italic; }
    public static VectorTypeface FromFamilyName(string? family, VectorFontStyle style)
        => new(family ?? "", style.Bold, style.Italic);
    internal IFont Resolve(float size)
    {
        if (!float.IsFinite(size) || size <= 0 || size > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        if (_fonts.TryGetValue(size, out var existing)) return existing;
        var weight = new FontWeight { Weight = (ushort)(_requestedBold ? 700 : 400) };
        var style = _requestedItalic ? FontStyle.Italic : FontStyle.Normal;
        // Request explicit families from the OS when available. Unavailable names resolve to a bundled face
        // with the requested weight and slant, so a missing font never loses text or synthetic styling.
        var font = _family.Length > 0 ? Provider.MatchFamily(_family, weight, FontStretch.Normal, style, size) : null;
        if (font is null)
        {
            if (!Bundled.TryGetValue((_requestedBold, _requestedItalic), out var file))
            {
                var suffix = _requestedBold ? (_requestedItalic ? "-BoldOblique" : "-Bold") : _requestedItalic ? "-Oblique" : "";
                using var stream = typeof(VectorTypeface).Assembly.GetManifestResourceStream($"NurbsScene.Controls.Drawing.Fonts.DejaVuSans{suffix}.ttf")
                    ?? throw new InvalidOperationException("The bundled vector font is missing.");
                Bundled[(_requestedBold, _requestedItalic)] = file = Provider.LoadFontFile(stream);
            }
            font = file.CreateFont("DejaVu Sans", weight, FontStretch.Normal, style, size)
                ?? throw new InvalidOperationException("The bundled vector font is invalid.");
        }
        // Arbitrary panel zoom/size edits must not retain an unbounded collection of sized fonts.
        if (_fonts.Count >= 32) _fonts.Clear();
        _fonts[size] = font;
        return font;
    }
    // MatchFamily chooses the closest installed face, which can be regular even when bold/italic was
    // requested. Report the actual sfnt style so PanelTypographyRenderer can synthesize any missing style.
    // OS/2 and head are copied once per face, never in the warm drawing or measurement paths.
    private static (bool Bold, bool Italic) ReadStyle(IFont font)
    {
        if (font.TryGetTable(0x4F532F32, out var os2) && os2.Length >= 64) // OS/2
        {
            var selection = (os2[62] << 8) | os2[63];
            var weight = (os2[4] << 8) | os2[5];
            return ((selection & 0x20) != 0 || weight >= 700, (selection & 0x201) != 0);
        }
        if (font.TryGetTable(0x68656164, out var head) && head.Length >= 46) // head
        {
            var style = (head[44] << 8) | head[45];
            return ((style & 1) != 0, (style & 2) != 0);
        }
        return (false, false);
    }
    public void Dispose() { if (!ReferenceEquals(this, Default)) _fonts.Clear(); }
}

public readonly record struct VectorFontMetrics(float Ascent, float Descent);

public sealed class VectorFont(VectorTypeface typeface, float size) : IDisposable
{
    private readonly Dictionary<string, float> _widths = new(StringComparer.Ordinal);
    private VectorTypeface _typeface = typeface;
    private float _size = size;
    public VectorTypeface Typeface
    {
        get => _typeface;
        set { if (!ReferenceEquals(value, _typeface)) { _typeface = value; _widths.Clear(); } }
    }
    public float Size
    {
        get => _size;
        set { if (value != _size) { _size = value; _widths.Clear(); } }
    }
    public bool Embolden { get; set; }
    public float SkewX { get; set; }
    internal IFont Font => Typeface.Resolve(Size);
    public VectorFontMetrics Metrics => new(Font.Ascent, Font.Descent);
    public float MeasureText(string text)
    {
        if (_widths.TryGetValue(text, out var width)) return width;
        var run = Font.Shape(text.AsSpan(), out _);
        width = 0; foreach (var advance in run.Advances) width += advance;
        if (_widths.Count >= 256) _widths.Clear();
        _widths[text] = width; return width;
    }
    public void Dispose() => _widths.Clear();
}

/// <summary>Retains shaped glyph IDs and local positions; alignment or movement never reshapes the run.</summary>
public sealed class VectorTextBlob : IDisposable
{
    internal IFont Font { get; }
    internal ushort[] Glyphs { get; }
    internal Vector2[] Positions { get; }
    internal float Size { get; }
    internal bool Embolden { get; }
    internal float SkewX { get; }
    private VectorTextBlob(string text, VectorFont font)
    {
        Font = font.Font; Size = font.Size; Embolden = font.Embolden; SkewX = font.SkewX;
        var run = Font.Shape(text.AsSpan(), out _);
        Glyphs = run.Glyphs; Positions = new Vector2[run.Count];
        float advance = 0;
        for (var i = 0; i < run.Count; i++) { Positions[i] = run.Offsets[i] + new Vector2(advance, 0); advance += run.Advances[i]; }
    }
    public static VectorTextBlob Create(string text, VectorFont font) => new(text, font);
    public void Dispose() { }
}
