using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>One undoable display-only edit. Data parameters, exact output trees,
/// node bounds and wire identities remain unchanged; stale menu owners fail.</summary>
public static class PanelDisplayEditing
{
    public static bool Apply(GraphSession session, Graph expectedGraph, GraphNode expectedNode, PanelDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(expectedGraph);
        ArgumentNullException.ThrowIfNull(expectedNode); ArgumentNullException.ThrowIfNull(options);
        if (expectedNode.Component is not ("Panel" or "Text")) throw new ArgumentException("Select a text panel.");
        if (!ReferenceEquals(session.Graph, expectedGraph)) throw new InvalidOperationException("The definition changed. Reopen the panel menu.");
        GraphNode current;
        try { current = expectedGraph.GetNode(expectedNode.Id); }
        catch (KeyNotFoundException error) { throw new InvalidOperationException("The panel was removed. Reopen the panel menu.", error); }
        if (!ReferenceEquals(current, expectedNode)) throw new InvalidOperationException("The panel changed. Reopen its menu.");
        if (PanelDisplayOptions.For(current) == options) return false;
        var defaults = current.Component == "Text" ? PanelDisplayOptions.TextDefault : PanelDisplayOptions.PanelDefault;
        session.Edit("Panel display options", g => g.Update(current with { PanelDisplay = options == defaults ? null : options }));
        return true;
    }
}
