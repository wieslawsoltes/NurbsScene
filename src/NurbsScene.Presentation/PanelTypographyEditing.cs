using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>One atomic appearance edit. A modal draft cannot overwrite a newer
/// target, deleted target, replaced graph or a different history session.</summary>
public static class PanelTypographyEditing
{
    public static bool Apply(GraphSession session, Graph expectedGraph, GraphNode expectedNode, PanelTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(expectedGraph);
        ArgumentNullException.ThrowIfNull(expectedNode); ArgumentNullException.ThrowIfNull(style);
        style.Validate();
        if (expectedNode.Component is not ("Panel" or "Text")) throw new ArgumentException("Select a text panel.");
        if (!ReferenceEquals(session.Graph, expectedGraph) || !expectedGraph.TryGetNode(expectedNode.Id, out var current) ||
            !ReferenceEquals(current, expectedNode)) throw new InvalidOperationException("The panel changed. Reopen its typography editor.");
        if (PanelTextStyle.For(current) == style) return false;
        session.Edit("Panel typography", g => g.Update(current with { PanelStyle = style == PanelTextStyle.Default ? null : style }));
        return true;
    }
}
