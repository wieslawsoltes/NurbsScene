using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Identity of the immutable inputs consumed by a completed panel draw.
/// Equal-looking styles or recycled graph revisions alone do not identify a frame.
/// This value never owns native resources or changes application state.</summary>
public readonly record struct PanelPaintStamp(Graph Owner, GraphNode Node, NodeResult? Result,
    double Zoom, CanvasBox Bounds)
{
    public bool Matches(PanelPaintStamp other) => ReferenceEquals(Owner, other.Owner) &&
        ReferenceEquals(Node, other.Node) && ReferenceEquals(Result, other.Result) &&
        Zoom == other.Zoom && Bounds == other.Bounds;
}
