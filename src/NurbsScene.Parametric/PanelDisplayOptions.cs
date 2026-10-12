namespace NurbsScene.Parametric;

/// <summary>Immutable panel presentation, independent of component data. Null
/// metadata retains the legacy Panel/Text defaults in older workspaces.</summary>
public sealed record PanelDisplayOptions(bool DrawPaths = true, bool DrawIndices = false, bool WrapItems = true)
{
    public static PanelDisplayOptions PanelDefault { get; } = new();
    public static PanelDisplayOptions TextDefault { get; } = new(false);
    public static PanelDisplayOptions For(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.PanelDisplay ?? (node.Component == "Text" ? TextDefault : PanelDefault);
    }
}

public sealed partial record GraphNode
{
    public PanelDisplayOptions? PanelDisplay { get; init; }
}
