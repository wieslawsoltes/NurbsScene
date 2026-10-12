namespace NurbsScene.Parametric;

/// <summary>Presentation dimensions in canvas units. Not a component parameter:
/// resizing changes graph revision/history, but never dirties numerical outputs.
/// The graph validates values on Update and external snapshot restoration.</summary>
public readonly record struct NodeCanvasSize(double Width, double Height)
{
    public void Validate()
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width < 1 || Height < 1 || Width > 4096 || Height > 4096)
            throw new ArgumentOutOfRangeException(nameof(NodeCanvasSize), "Canvas dimensions must be finite and between 1 and 4096 units.");
    }
}

public sealed partial record GraphNode
{
    public NodeCanvasSize? CanvasSize { get; init; }
}
