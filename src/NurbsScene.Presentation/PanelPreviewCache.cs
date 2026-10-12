using System.Collections.Immutable;
using System.Globalization;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public sealed record PanelPreview(ImmutableArray<string> Lines, string Footer);

/// <summary>Owner-thread, bounded preview of a panel's string output. Formatting
/// runs only when its immutable node or result changes, never once per frame.
/// Unicode text elements and explicit line breaks are retained within the budget.</summary>
public sealed class PanelPreviewCache
{
    private GraphNode? _node;
    private NodeResult? _result;
    private PanelPreview _preview = new([], "Waiting for solution");
    public long BuildCount { get; private set; }
    public PanelPreview Get(GraphNode node, NodeResult? result)
    {
        if (ReferenceEquals(node, _node) && ReferenceEquals(result, _result)) return _preview;
        _node = node; _result = result; BuildCount++;
        var lines = ImmutableArray.CreateBuilder<string>();
        const int rows = 8, columns = 29;
        void Text(string text)
        {
            // Bound scanning and temporary grapheme strings as well as final rows.
            if (text.Length > 2048) { var end = char.IsHighSurrogate(text[2047]) ? 2047 : 2048; text = text[..end] + "…"; }
            var elements = StringInfo.GetTextElementEnumerator(text); var line = new System.Text.StringBuilder(); var count = 0;
            var previousCr = false;
            while (elements.MoveNext())
            {
                if (lines.Count == rows) return;
                var element = elements.GetTextElement();
                if (element is "\r\n" or "\n" or "\r")
                {
                    if (!(previousCr && element == "\n")) { lines.Add(line.ToString()); line.Clear(); count = 0; }
                    previousCr = element == "\r"; continue;
                }
                previousCr = false;
                if (count == columns) { lines.Add(line.ToString()); line.Clear(); count = 0; if (lines.Count == rows) return; }
                // Limit even adversarial single graphemes with enormous runs of
                // combining marks; canvas shaping is never given unbounded text.
                line.Append(element.Length > 64 ? "�" : element == "\t" ? " " : element); count++;
            }
            if (lines.Count < rows) lines.Add(line.ToString());
        }
        if (result is null) return _preview = new([], "Waiting for solution");
        if (result.Status != "ok") { Text(result.Messages.FirstOrDefault() ?? result.Status); return _preview = new(lines.ToImmutable(), result.Status); }
        if (!result.Outputs.TryGetValue("T", out var tree)) return _preview = new([], "No text output");
        foreach (var branch in tree.Branches)
        {
            if (lines.Count == rows) break;
            if (node.Component == "Panel") Text(branch.Path.ToString());
            foreach (var item in branch.Items)
            {
                if (lines.Count == rows) break;
                Text(item as string ?? "<non-text>");
            }
        }
        return _preview = new(lines.ToImmutable(), $"{tree.ItemCount} items · {tree.Branches.Length} branches");
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
