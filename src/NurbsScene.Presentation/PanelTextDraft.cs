using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Separates authoritative text from native TextBox newline coercion.
/// An unchanged logical draft returns the exact original string, including mixed
/// CR/LF sequences. Actually edited text uses LF consistently across hosts.</summary>
public sealed class PanelTextDraft
{
    private readonly string _original, _logical;
    public PanelTextDraft(string original)
    {
        Validate(original); _original = original; _logical = Normalize(original);
    }
    public string Resolve(string nativeText)
    {
        Validate(nativeText);
        if (nativeText == _original) return _original;
        var logical = Normalize(nativeText);
        return logical == _logical ? _original : logical;
    }
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    private static void Validate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > InputComponents.MaximumTextLength) throw new ArgumentException("Panel text exceeds 65,536 characters.");
    }
}
