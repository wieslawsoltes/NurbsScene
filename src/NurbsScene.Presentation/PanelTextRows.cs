using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace NurbsScene.Presentation;

/// <summary>One bounded preview build. Span-based grapheme traversal creates row
/// strings, not a string for every visible or clipped character. This is display
/// formatting only: authoritative data is neither normalized nor truncated.</summary>
internal sealed class PanelTextRows(int rows, int columns, bool wrap)
{
    private readonly ImmutableArray<string>.Builder _lines = ImmutableArray.CreateBuilder<string>(rows);
    private readonly StringBuilder _line = new(Math.Max(16, columns));
    private int _scanned, _count, _lastElementStart;
    public bool Truncated { get; private set; }
    public bool IsFull => _lines.Count == rows || _scanned == PanelPreviewCache.MaximumScannedCodeUnits;
    public void Truncate() => Truncated = true;
    public ImmutableArray<string> Snapshot() => _lines.ToImmutable();

    public void Append(string text, int itemIndex = -1)
    {
        if (IsFull) { Truncate(); return; }
        var take = Math.Min(text.Length, Math.Min(2048, PanelPreviewCache.MaximumScannedCodeUnits - _scanned));
        _scanned += take;
        var cutoff = take < text.Length;
        Truncated |= cutoff;
        // A bounded stack buffer lets an initial combining mark retain its text
        // boundary with the item prefix, without concatenation/substrings.
        Span<char> buffer = stackalloc char[2064];
        var prefixLength = 0;
        if (itemIndex >= 0)
        {
            itemIndex.TryFormat(buffer, out prefixLength, provider: CultureInfo.InvariantCulture);
            buffer[prefixLength++] = ':'; buffer[prefixLength++] = ' ';
        }
        text.AsSpan(0, take).CopyTo(buffer[prefixLength..]);
        ReadOnlySpan<char> input = buffer[..(prefixLength + take)];
        ResetLine();
        var offset = 0;
        while (offset < input.Length)
        {
            if (_lines.Count == rows) { Truncate(); return; }
            var remaining = input[offset..];
            if (!wrap && _count == columns && remaining[0] is not ('\r' or '\n'))
            {
                MarkClippedLine();
                // Invisible tail characters need no grapheme strings or shaping.
                // Search remains inside the fixed scan window and preserves the
                // next explicit line break, including CRLF as a single element.
                var nextLine = remaining.IndexOfAny('\r', '\n');
                if (nextLine < 0) break;
                offset += nextLine; continue;
            }
            var length = StringInfo.GetNextTextElementLength(remaining);
            if (cutoff && length == remaining.Length)
            {
                // The last bounded cluster might continue beyond our scan
                // window (combining marks, regional pairs, ZWJ emoji, CRLF).
                // Omit that uncertain cluster rather than publish half of it.
                MarkClippedLine(); break;
            }
            var element = remaining[..length]; offset += length;
            if (element[0] is '\r' or '\n') { FlushLine(); continue; }
            if (_count == columns)
            {
                FlushLine();
                if (_lines.Count == rows) { Truncate(); return; }
            }
            _lastElementStart = _line.Length;
            // One grapheme can contain arbitrarily many combining marks. Replace
            // oversize/ill-formed display elements and explicitly mark the loss.
            if (element.Length > 64 || !WellFormed(element)) { _line.Append('�'); Truncate(); }
            else if (element.Length == 1 && element[0] == '\t') _line.Append(' ');
            else _line.Append(element);
            _count++;
        }
        if (_lines.Count < rows) FlushLine();
        else Truncate(); // An omitted trailing empty line still counts as loss.
    }
    private void MarkClippedLine()
    {
        Truncate();
        if (_count == columns) _line.Length = _lastElementStart;
        else { _lastElementStart = _line.Length; _count++; }
        _line.Append('…');
    }
    private void FlushLine() { _lines.Add(_line.ToString()); ResetLine(); }
    private void ResetLine() { _line.Clear(); _count = 0; _lastElementStart = 0; }
    private static bool WellFormed(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i == value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i])) return false;
        return true;
    }
}
