using System.Collections.Immutable;
using System.Globalization;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public sealed record PanelPreview(ImmutableArray<string> Lines, string Footer)
{
    public bool Truncated { get; init; }
}

/// <summary>Bounded, owner-thread preview formatting. Cache keys are immutable
/// solution identity, component kind and visible row/column budgets; moving,
/// renaming or selecting a panel does not reshape identical output text.</summary>
public sealed class PanelPreviewCache
{
    public const int MaximumRows = 64, MaximumColumns = 128, MaximumScannedCodeUnits = 8192;
    private string? _kind;
    private NodeResult? _result;
    private int _rows, _columns;
    private PanelDisplayOptions? _options;
    private PanelPreview _preview = new([], "Waiting for solution");
    public long BuildCount { get; private set; }
    public PanelPreview Current => _preview;
    public PanelPreview Get(GraphNode node, NodeResult? result) => Get(node, result, 8, 29);
    public PanelPreview Get(GraphNode node, NodeResult? result, int rows, int columns)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (rows < 1 || rows > MaximumRows || columns < 1 || columns > MaximumColumns)
            throw new ArgumentOutOfRangeException(nameof(rows), "Preview dimensions exceed the formatting budget.");
        var options = PanelDisplayOptions.For(node);
        if (_kind == node.Component && ReferenceEquals(result, _result) && rows == _rows && columns == _columns && options == _options) return _preview;
        _options = options; _kind = node.Component; _result = result; _rows = rows; _columns = columns; BuildCount++;
        var lines = ImmutableArray.CreateBuilder<string>(); var truncated = false; var scanned = 0;
        void Text(string text, string prefix = "")
        {
            if (lines.Count == rows || scanned == MaximumScannedCodeUnits) { truncated = true; return; }
            var limit = Math.Min(2048, MaximumScannedCodeUnits - scanned);
            if (text.Length > limit)
            {
                // This is a display prefix only; authoritative strings are intact.
                var end = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
                text = text[..end]; truncated = true;
            }
            scanned += text.Length;
            // Only the bounded display prefix is combined with an item label.
            if (prefix.Length > 0) text = prefix + text;
            var elements = StringInfo.GetTextElementEnumerator(text);
            var line = new System.Text.StringBuilder(); var count = 0; var previousCr = false;
            var lastElementStart = 0; var clippedLine = false;
            while (elements.MoveNext())
            {
                if (lines.Count == rows) { truncated = true; return; }
                var element = elements.GetTextElement();
                if (element is "\r\n" or "\n" or "\r")
                {
                    if (!(previousCr && element == "\n")) { lines.Add(line.ToString()); line.Clear(); count = 0; clippedLine = false; lastElementStart = 0; }
                    previousCr = element == "\r"; continue;
                }
                previousCr = false;
                if (count == columns)
                {
                    if (!options.WrapItems)
                    {
                        // Replace a whole final grapheme, never half a surrogate
                        // or combining sequence. Explicit newlines still survive.
                        if (!clippedLine) { line.Length = lastElementStart; line.Append('…'); clippedLine = true; }
                        truncated = true; continue;
                    }
                    lines.Add(line.ToString()); line.Clear(); count = 0;
                    if (lines.Count == rows) { truncated = true; return; }
                }
                // A single grapheme can contain arbitrarily many combining marks.
                lastElementStart = line.Length;
                line.Append(element.Length > 64 ? "�" : element == "\t" ? " " : element); count++;
            }
            if (lines.Count < rows) lines.Add(line.ToString());
            else truncated = true; // includes an omitted trailing empty line
        }
        PanelPreview Finish(string footer) => _preview = new(lines.ToImmutable(), footer + (truncated ? " · …" : "")) { Truncated = truncated };
        if (result is null) return Finish("Waiting for solution");
        if (result.Status != "ok") { Text(result.Messages.FirstOrDefault() ?? result.Status); return Finish(result.Status); }
        if (!result.Outputs.TryGetValue("T", out var tree)) return Finish("No text output");
        // Even a malformed Text result containing many empty branches is bounded.
        var visited = 0;
        foreach (var branch in tree.Branches)
        {
            if (lines.Count == rows || scanned == MaximumScannedCodeUnits || ++visited > MaximumRows)
            { truncated = true; break; }
            if (options.DrawPaths) Text(branch.Path.ToString());
            var itemIndex = 0;
            foreach (var item in branch.Items)
            {
                if (lines.Count == rows || scanned == MaximumScannedCodeUnits) { truncated = true; break; }
                Text(item as string ?? "<non-text>", options.DrawIndices ? (itemIndex++).ToString(CultureInfo.InvariantCulture) + ": " : "");
            }
        }
        return Finish($"{tree.ItemCount} items · {tree.Branches.Length} branches");
    }
}

public static class CanvasValueEditing
{
    public static bool Toggle(GraphSession session, Graph expectedGraph, GraphNode expectedNode)
    {
        ValidateOwner(session, expectedGraph, expectedNode);
        if (expectedNode.Component != "Boolean") throw new ArgumentException("Select a Boolean toggle.");
        var value = !InputComponents.BooleanValue(expectedNode.Parameters);
        session.Edit("Toggle Boolean", g => g.Update(expectedNode with { Parameters = expectedNode.Parameters.SetItem("value", value) }));
        return value;
    }
    public static bool SetText(GraphSession session, Graph expectedGraph, GraphNode expectedNode, string text, bool singleItem)
    {
        ArgumentNullException.ThrowIfNull(text); ValidateOwner(session, expectedGraph, expectedNode);
        if (expectedNode.Component is not ("Text" or "Panel")) throw new ArgumentException("Select a text input or panel.");
        if (text.Length > InputComponents.MaximumTextLength) throw new ArgumentException("Text exceeds 65,536 characters.");
        var single = expectedNode.Component == "Text" || singleItem;
        var oldSingle = expectedNode.Component == "Text" || expectedNode.Parameters.GetValueOrDefault("singleItem", false) is true;
        if (expectedNode.Parameters.GetValueOrDefault("text", expectedNode.Component == "Text" ? "NurbsScene" : "") is string old && old == text && oldSingle == single) return false;
        var parameters = expectedNode.Parameters.SetItem("text", text);
        if (expectedNode.Component == "Panel") parameters = parameters.SetItem("singleItem", single);
        session.Edit("Edit panel text", g => g.Update(expectedNode with { Parameters = parameters })); return true;
    }
    private static void ValidateOwner(GraphSession session, Graph graph, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(session.Graph, graph) || !ReferenceEquals(graph.GetNode(node.Id), node))
            throw new InvalidOperationException("The component changed. Reopen its editor.");
    }
}
