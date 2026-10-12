using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Panel dimensions and resize handles, independent of Uno and Skia.</summary>
public static class PanelLayout
{
    public const double MinimumWidth = 128, MinimumHeight = 76, MaximumDimension = 2048;
    public static bool CanResize(GraphNode node) => node.Component is "Panel" or "Text";
    public static CanvasBox Bounds(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!CanResize(node)) throw new ArgumentException("Only text panels have resizable bounds.");
        var size = node.CanvasSize ?? (node.Component == "Panel" ? new NodeCanvasSize(224, 168) : new NodeCanvasSize(156, 76));
        size.Validate();
        return new(node.X, node.Y, Math.Clamp(size.Width, MinimumWidth, MaximumDimension), Math.Clamp(size.Height, MinimumHeight, MaximumDimension));
    }
    public static bool IsHandle(CanvasBox bounds, CanvasPoint point, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0 || tolerance > 128) throw new ArgumentOutOfRangeException(nameof(tolerance));
        return bounds.Contains(point) && point.X >= bounds.X + bounds.Width - tolerance && point.Y >= bounds.Y + bounds.Height - tolerance;
    }
}

/// <summary>A resize draft never mutates the graph until Commit. Cancellation is
/// dropping this object, not restoring a whole graph snapshot. Independent edits
/// can continue; a changed target or replaced graph rejects a stale commit.</summary>
public sealed class PanelResizeDraft
{
    private readonly Graph _owner;
    private readonly GraphNode _node;
    private readonly CanvasBox _initial;
    public string NodeId => _node.Id;
    public CanvasBox Bounds { get; private set; }
    public bool Changed => Bounds != _initial;
    public PanelResizeDraft(Graph owner, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner; _node = owner.GetNode(nodeId); Bounds = _initial = PanelLayout.Bounds(_node);
    }
    public void Move(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Resize motion must be finite.");
        Bounds = _initial with {
            Width = Math.Clamp(_initial.Width + dx, PanelLayout.MinimumWidth, PanelLayout.MaximumDimension),
            Height = Math.Clamp(_initial.Height + dy, PanelLayout.MinimumHeight, PanelLayout.MaximumDimension)
        };
    }
    public bool Commit(GraphSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(session.Graph, _owner) || !ReferenceEquals(_owner.GetNode(_node.Id), _node))
            throw new InvalidOperationException("The panel changed during resizing. The resize draft was discarded.");
        if (!Changed) return false;
        session.Edit("Resize text panel", g => g.Update(_node with { CanvasSize = new(Bounds.Width, Bounds.Height) }));
        return true;
    }
}
