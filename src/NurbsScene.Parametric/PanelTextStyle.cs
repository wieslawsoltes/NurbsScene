namespace NurbsScene.Parametric;

public enum PanelHorizontalAlignment { Left, Center, Right }
public enum PanelVerticalAlignment { Top, Center, Bottom }

/// <summary>Immutable, display-only panel typography. Sizes are canvas units at
/// 100% zoom, not device pixels or points. An empty family selects the host font;
/// unavailable families may be substituted by the renderer. No font is embedded.</summary>
public sealed record PanelTextStyle(string FontFamily = "", double Size = 10, bool Bold = false,
    bool Italic = false, PanelHorizontalAlignment Horizontal = PanelHorizontalAlignment.Left,
    PanelVerticalAlignment Vertical = PanelVerticalAlignment.Top)
{
    public static PanelTextStyle Default { get; } = new();
    public static PanelTextStyle For(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.PanelStyle ?? Default;
    }
    public void Validate()
    {
        if (FontFamily is null || FontFamily.Length > 128 || FontFamily.Any(char.IsControl) || FontFamily != FontFamily.Trim())
            throw new ArgumentException("Font family must be trimmed, at most 128 characters, and contain no control characters.");
        for (var i = 0; i < FontFamily.Length; i++)
            if (char.IsHighSurrogate(FontFamily[i]))
            {
                if (++i == FontFamily.Length || !char.IsLowSurrogate(FontFamily[i])) throw new ArgumentException("Font family contains malformed UTF-16.");
            }
            else if (char.IsLowSurrogate(FontFamily[i])) throw new ArgumentException("Font family contains malformed UTF-16.");
        if (!double.IsFinite(Size) || Size < 6 || Size > 72)
            throw new ArgumentOutOfRangeException(nameof(Size), "Font size must be between 6 and 72 canvas units.");
        if (!Enum.IsDefined(Horizontal) || !Enum.IsDefined(Vertical))
            throw new ArgumentException("Unsupported text alignment.");
    }
}

public sealed partial record GraphNode
{
    public PanelTextStyle? PanelStyle { get; init; }
}
