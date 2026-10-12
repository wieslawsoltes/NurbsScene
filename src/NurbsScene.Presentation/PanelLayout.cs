using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

[Flags]
public enum PanelResizeEdges
{
    None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8
}

/// <summary>Panel dimensions and eight-direction resize hit testing, independent
/// of Uno and Skia. Corners have a larger target; thin edge strips leave the
/// title/body available for moving and double-click editing. Ports take priority
/// in the host's pointer routing.</summary>
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
    /// <summary>Legacy bottom-right handle query retained for existing consumers.</summary>
    public static bool IsHandle(CanvasBox bounds, CanvasPoint point, double tolerance)
    {
        ValidateTolerance(tolerance);
        return bounds.Contains(point) && point.X >= bounds.X + bounds.Width - tolerance && point.Y >= bounds.Y + bounds.Height - tolerance;
    }
    public static PanelResizeEdges HitTest(CanvasBox bounds, CanvasPoint point, double tolerance)
    {
        ValidateTolerance(tolerance);
        ValidateBounds(bounds);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Finite pointer coordinates required.");
        if (!bounds.Contains(point)) return PanelResizeEdges.None;
        // Quarter-extent limits guarantee opposing handles never overlap, even
        // at minimum dimensions and the largest screen-space hit tolerance.
        var tx = Math.Min(tolerance, bounds.Width / 4);
        var ty = Math.Min(tolerance, bounds.Height / 4);
        var x = point.X - bounds.X; var y = point.Y - bounds.Y;
        var horizontal = x <= tx ? PanelResizeEdges.Left : x >= bounds.Width - tx ? PanelResizeEdges.Right : PanelResizeEdges.None;
        var vertical = y <= ty ? PanelResizeEdges.Top : y >= bounds.Height - ty ? PanelResizeEdges.Bottom : PanelResizeEdges.None;
        if (horizontal != PanelResizeEdges.None && vertical != PanelResizeEdges.None) return horizontal | vertical;
        if (x <= tx / 3) return PanelResizeEdges.Left;
        if (x >= bounds.Width - tx / 3) return PanelResizeEdges.Right;
        if (y <= ty / 3) return PanelResizeEdges.Top;
        if (y >= bounds.Height - ty / 3) return PanelResizeEdges.Bottom;
        return PanelResizeEdges.None;
    }
    internal static void ValidateEdges(PanelResizeEdges edges)
    {
        const PanelResizeEdges known = PanelResizeEdges.Left | PanelResizeEdges.Right | PanelResizeEdges.Top | PanelResizeEdges.Bottom;
        if (edges == PanelResizeEdges.None || (edges & ~known) != 0 ||
            (edges & (PanelResizeEdges.Left | PanelResizeEdges.Right)) == (PanelResizeEdges.Left | PanelResizeEdges.Right) ||
            (edges & (PanelResizeEdges.Top | PanelResizeEdges.Bottom)) == (PanelResizeEdges.Top | PanelResizeEdges.Bottom))
            throw new ArgumentOutOfRangeException(nameof(edges), "Select one edge or two adjacent edges.");
    }
    private static void ValidateTolerance(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0 || tolerance > 128) throw new ArgumentOutOfRangeException(nameof(tolerance));
    }
    internal static void ValidateBounds(CanvasBox bounds)
    {
        var right = bounds.X + bounds.Width; var bottom = bounds.Y + bounds.Height;
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(right) || !double.IsFinite(bottom) || right <= bounds.X || bottom <= bounds.Y)
            throw new ArgumentException("Panel bounds cannot be represented at these canvas coordinates.");
    }
}

/// <summary>A resize draft never mutates the graph until Commit. Every edge keeps
/// its opposite edge anchored, clamps before crossing, and updates position and
/// dimensions atomically. Cancellation drops the draft without rolling back any
/// independent host edits. Movement and port mapping allocate no managed memory.</summary>
public sealed class PanelResizeDraft
{
    private readonly Graph _owner;
    private readonly GraphNode _node;
    private readonly CanvasBox _initial;
    public string NodeId => _node.Id;
    public PanelResizeEdges Edges { get; }
    public CanvasBox InitialBounds => _initial;
    public CanvasBox Bounds { get; private set; }
    public bool Changed => Bounds != _initial;
    public bool IsCurrent(Graph graph) => ReferenceEquals(graph, _owner) &&
        graph.TryGetNode(_node.Id, out var current) && ReferenceEquals(current, _node);
    public PanelResizeDraft(Graph owner, string nodeId) : this(owner, nodeId, PanelResizeEdges.Right | PanelResizeEdges.Bottom) { }
    public PanelResizeDraft(Graph owner, string nodeId, PanelResizeEdges edges)
    {
        ArgumentNullException.ThrowIfNull(owner); PanelLayout.ValidateEdges(edges);
        _owner = owner; _node = owner.GetNode(nodeId); Bounds = _initial = PanelLayout.Bounds(_node);
        PanelLayout.ValidateBounds(_initial); Edges = edges;
    }
    public void Move(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Resize motion must be finite.");
        var left = (Edges & PanelResizeEdges.Left) != 0; var top = (Edges & PanelResizeEdges.Top) != 0;
        var width = Math.Clamp(_initial.Width + (left ? -dx : (Edges & PanelResizeEdges.Right) != 0 ? dx : 0), PanelLayout.MinimumWidth, PanelLayout.MaximumDimension);
        var height = Math.Clamp(_initial.Height + (top ? -dy : (Edges & PanelResizeEdges.Bottom) != 0 ? dy : 0), PanelLayout.MinimumHeight, PanelLayout.MaximumDimension);
        var x = left && width != _initial.Width ? _initial.X + _initial.Width - width : _initial.X;
        var y = top && height != _initial.Height ? _initial.Y + _initial.Height - height : _initial.Y;
        var candidate = new CanvasBox(x, y, width, height);
        PanelLayout.ValidateBounds(candidate); // never publish a partial/invalid draft
        Bounds = candidate;
    }
    /// <summary>Map an original panel port through the preview bounds. Port row
    /// offsets stay fixed; input and output sides both follow top/left movement.</summary>
    public CanvasPoint MapPort(CanvasPoint original, bool output)
    {
        if (!double.IsFinite(original.X) || !double.IsFinite(original.Y)) throw new ArgumentException("Finite original port required.");
        return new(Bounds.X + (output ? Bounds.Width : 0), Bounds.Y + (original.Y - _initial.Y));
    }
    public bool Commit(GraphSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!IsCurrent(session.Graph))
            throw new InvalidOperationException("The panel changed during resizing. The resize draft was discarded.");
        if (!Changed) return false;
        session.Edit("Resize text panel", g => g.Update(_node with { X = Bounds.X, Y = Bounds.Y, CanvasSize = new(Bounds.Width, Bounds.Height) }));
        return true;
    }
}
