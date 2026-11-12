using Uno.UI.Composition.Drawing;
using Uno.UI.Composition.Managed;
using Windows.UI.Text;

namespace NurbsScene.Controls.Drawing;

/// <summary>Fonts shared by the XAML text system and the custom WebGPU vector controls.</summary>
public static class BundledFonts
{
    /// <summary>
    /// Creates a managed XAML font provider with installed-family matching and
    /// offline DejaVu Sans regular, bold, oblique and bold-oblique fallback faces.
    /// Register it before the first XAML measure or render.
    /// </summary>
    public static IFontProvider CreateProvider() => new BundledFontProvider();

    /// <summary>
    /// Reads the regular DejaVu Sans face. The caller owns the returned array;
    /// no mutable font data is shared with another host.
    /// Prefer <see cref="CreateProvider"/> when registering the XAML font provider,
    /// so requests for bold or italic also resolve to an actual styled face.
    /// </summary>
    public static byte[] GetDefaultFontBytes() => ReadFontBytes("DejaVuSans.ttf");

    private static byte[] ReadFontBytes(string name)
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(
            "NurbsScene.Controls.Drawing.Fonts." + name)
            ?? throw new InvalidOperationException("The bundled XAML font is missing: " + name);
        using var buffer = new MemoryStream(checked((int)stream.Length));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class BundledFontProvider : IFontProvider
    {
        private const string Family = "DejaVu Sans";
        private const int MaximumCachedFonts = 128;
        private static readonly string[] FaceFiles =
        [
            "DejaVuSans.ttf", "DejaVuSans-Bold.ttf",
            "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"
        ];
        private readonly IFontProvider _system;
        private readonly byte[]?[] _faceBytes = new byte[4][];
        private readonly object _gate = new();
        private readonly Dictionary<(int Face, float Size), IFont> _fonts = [];
        private readonly Queue<(int Face, float Size)> _fontOrder = new();

        public BundledFontProvider()
        {
            _faceBytes[0] = GetDefaultFontBytes();
            // One provider owns installed-family indexing and platform fallback.
            // Its regular bundled default guarantees that synchronous startup succeeds.
            _system = ManagedBackend.CreateFontProvider(_faceBytes[0]);
        }

        public IFont? CreateFont(byte[] data, string? familyNameHint,
            FontWeight weight, FontStretch stretch, FontStyle style, float fontSize)
            => _system.CreateFont(data, familyNameHint, weight, stretch, style, fontSize);

        public IFont? MatchFamily(string familyName, FontWeight weight,
            FontStretch stretch, FontStyle style, float fontSize)
            => IsBundledFamily(familyName)
                ? GetBundledFont(weight, stretch, style, fontSize)
                : _system.MatchFamily(familyName, weight, stretch, style, fontSize);

        public ValueTask<IFont?> MatchFamilyAsync(string familyName, FontWeight weight,
            FontStretch stretch, FontStyle style, float fontSize)
            => IsBundledFamily(familyName)
                ? new ValueTask<IFont?>(GetBundledFont(weight, stretch, style, fontSize))
                : _system.MatchFamilyAsync(familyName, weight, stretch, style, fontSize);

        public ValueTask<IFont?> MatchCharacterAsync(int codepoint, FontWeight weight,
            FontStretch stretch, FontStyle style, float fontSize)
        {
            var bundled = GetBundledFont(weight, stretch, style, fontSize);
            return bundled.ContainsGlyph(codepoint)
                ? new ValueTask<IFont?>(bundled)
                : _system.MatchCharacterAsync(codepoint, weight, stretch, style, fontSize);
        }

        public IFont GetDefaultFont(FontWeight weight, FontStretch stretch, FontStyle style, float fontSize)
        {
            var resolved = _system.GetDefaultFont(weight, stretch, style, fontSize);
            // Uno 7.0.32 caches its bundled default by size alone. Replace that
            // regular-only fallback with the actual requested DejaVu face. Other
            // native default families retain the host's normal matching.
            return IsBundledFamily(resolved.FamilyName)
                ? GetBundledFont(weight, stretch, style, fontSize)
                : resolved;
        }

        private static bool IsBundledFamily(string familyName)
            => string.Equals(familyName, Family, StringComparison.OrdinalIgnoreCase);

        private IFont GetBundledFont(FontWeight weight, FontStretch stretch, FontStyle style, float fontSize)
        {
            // The packaged static family has 400 and 700 weights at normal width.
            // Semibold and heavier requests select the actual bold face; both
            // italic and oblique requests select the authored oblique outlines.
            var face = (weight.Weight > 550 ? 1 : 0) | (style == FontStyle.Normal ? 0 : 2);
            var key = (face, fontSize);
            lock (_gate)
            {
                if (_fonts.TryGetValue(key, out var cached)) return cached;
                var bytes = _faceBytes[face] ??= ReadFontBytes(FaceFiles[face]);
                var font = _system.CreateFont(bytes, Family, weight, stretch, style, fontSize)
                    ?? throw new InvalidOperationException("The bundled XAML font could not be parsed: " + FaceFiles[face]);
                // IFont has no disposal contract; existing XAML layouts keep their
                // handles when this bounded resolution cache evicts an old size.
                if (_fonts.Count == MaximumCachedFonts) _fonts.Remove(_fontOrder.Dequeue());
                _fonts.Add(key, font); _fontOrder.Enqueue(key);
                return font;
            }
        }
    }
}
