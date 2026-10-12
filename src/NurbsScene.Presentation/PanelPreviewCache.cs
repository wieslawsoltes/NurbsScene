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
        var text = new PanelTextRows(rows, columns, options.WrapItems);
        PanelPreview Finish(string footer) => _preview = new(text.Snapshot(), footer + (text.Truncated ? " · …" : "")) { Truncated = text.Truncated };
        if (result is null) return Finish("Waiting for solution");
        if (result.Status != "ok") { text.Append(result.Messages.FirstOrDefault() ?? result.Status); return Finish(result.Status); }
        if (!result.Outputs.TryGetValue("T", out var tree)) return Finish("No text output");
        // Even a malformed Text result containing many empty branches is bounded.
        var visited = 0;
        foreach (var branch in tree.Branches)
        {
            if (text.IsFull || ++visited > MaximumRows) { text.Truncate(); break; }
            if (options.DrawPaths) text.Append(branch.Path.ToString());
            var itemIndex = 0;
            foreach (var item in branch.Items)
            {
                if (text.IsFull) { text.Truncate(); break; }
                text.Append(item as string ?? "<non-text>", options.DrawIndices ? itemIndex++ : -1);
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
