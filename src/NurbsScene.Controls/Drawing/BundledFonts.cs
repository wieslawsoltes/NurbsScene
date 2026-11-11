namespace NurbsScene.Controls.Drawing;

/// <summary>Fonts shared by the XAML text system and the custom WebGPU vector controls.</summary>
public static class BundledFonts
{
    /// <summary>
    /// Reads the regular DejaVu Sans face for Uno's managed font provider. Browser hosts cannot enumerate
    /// installed fonts, so they must register these bytes before the first XAML measure or render.
    /// The caller owns the returned array; no mutable font data is shared with another host.
    /// </summary>
    public static byte[] GetDefaultFontBytes()
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(
            "NurbsScene.Controls.Drawing.Fonts.DejaVuSans.ttf")
            ?? throw new InvalidOperationException("The bundled XAML default font is missing.");
        using var buffer = new MemoryStream(checked((int)stream.Length));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
