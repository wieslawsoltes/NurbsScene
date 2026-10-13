using System.Collections.Immutable;

namespace NurbsScene.Presentation;

public enum GraphSelectionMode { Replace, Add, Remove, Toggle }
public readonly record struct GraphSelectionChange(bool Changed, bool CanDrag);

/// <summary>Classic canvas selection policy, independent of routed-event types.
/// See David Rutten's canvas shortcut reference (2012-08-11):
/// https://www.grasshopper3d.com/forum/topics/what-hotkeys-and-shortcuts-are-available-in-grasshopper
/// Plain clicks preserve an already selected group for dragging; Shift adds,
/// Control removes, and Control+Shift toggles. No graph/history edits occur.</summary>
public static class GraphSelectionModifiers
{
    public static GraphSelectionMode Mode(bool shift, bool control) => (shift, control) switch
    {
        (true, true) => GraphSelectionMode.Toggle,
        (true, false) => GraphSelectionMode.Add,
        (false, true) => GraphSelectionMode.Remove,
        _ => GraphSelectionMode.Replace
    };

    public static GraphSelectionChange Click(HashSet<string> selection, string id, GraphSelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrEmpty(id);
        Validate(mode);
        var changed = false;
        switch (mode)
        {
            case GraphSelectionMode.Replace:
                if (!selection.Contains(id)) { selection.Clear(); selection.Add(id); changed = true; }
                break;
            case GraphSelectionMode.Add: changed = selection.Add(id); break;
            case GraphSelectionMode.Remove: changed = selection.Remove(id); break;
            case GraphSelectionMode.Toggle:
                if (!selection.Remove(id)) selection.Add(id);
                changed = true;
                break;
        }
        return new(changed, selection.Contains(id));
    }

    /// <summary>Apply distinct hits from GraphSelection.InRectangle. Validation
    /// completes before mutation. Duplicate hits are treated as one hit, including
    /// toggle mode, so a host cannot accidentally cancel a selection operation.</summary>
    public static bool Rectangle(HashSet<string> selection, ImmutableArray<string> hits, GraphSelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Validate(mode);
        if (hits.IsDefault) throw new ArgumentException("Initialized hits required.", nameof(hits));
        foreach (var id in hits) ArgumentException.ThrowIfNullOrEmpty(id);
        if (mode == GraphSelectionMode.Replace)
        {
            var changed = !selection.SetEquals(hits);
            if (changed) { selection.Clear(); selection.UnionWith(hits); }
            return changed;
        }
        var modified = false;
        HashSet<string>? visited = mode == GraphSelectionMode.Toggle ? new(selection.Comparer) : null;
        foreach (var id in hits)
        {
            if (visited is not null && !visited.Add(id)) continue;
            modified |= Click(selection, id, mode).Changed;
        }
        return modified;
    }

    private static void Validate(GraphSelectionMode mode)
    {
        if (mode is not (GraphSelectionMode.Replace or GraphSelectionMode.Add or GraphSelectionMode.Remove or GraphSelectionMode.Toggle))
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
}

/// <summary>Reusable node-position buffer for one drag. The origin snapshot is
/// immutable; pointer updates do not allocate arrays, LINQ iterators or closures.
/// Shift constrains displacement to the nearest horizontal, vertical or 45-degree
/// direction using orthogonal projection. This does not mutate a graph.</summary>
public sealed class GraphDragPositions
{
    private readonly ImmutableArray<NurbsScene.Parametric.NodePosition> _origins;
    private readonly NurbsScene.Parametric.NodePosition[] _positions;
    private readonly IReadOnlyList<NurbsScene.Parametric.NodePosition> _view;
    public GraphDragPositions(IEnumerable<NurbsScene.Parametric.NodePosition> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        _origins = origins.Take(10_001).ToImmutableArray();
        if (_origins.Length > 10_000) throw new ArgumentException("Node move budget exceeded.", nameof(origins));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in _origins)
            if (string.IsNullOrEmpty(p.Id) || !ids.Add(p.Id) || !double.IsFinite(p.X) || !double.IsFinite(p.Y))
                throw new ArgumentException("Distinct identities and finite origins required.", nameof(origins));
        _positions = _origins.ToArray();
        _view = Array.AsReadOnly(_positions);
    }
    /// <summary>Returns the same read-only live view on every call. Its contents
    /// change at the next Update. Consume it synchronously; copy it before
    /// retaining it as a snapshot or passing it to asynchronous work.</summary>
    public IReadOnlyList<NurbsScene.Parametric.NodePosition> Update(CanvasPoint delta, bool constrain)
    {
        delta = Constrain(delta, constrain);
        // Reject overflow before altering the reusable buffer.
        foreach (var p in _origins)
            if (!double.IsFinite(p.X + delta.X) || !double.IsFinite(p.Y + delta.Y))
                throw new ArgumentException("Drag coordinates exceed the finite range.", nameof(delta));
        for (var i = 0; i < _positions.Length; i++)
        {
            var p = _origins[i];
            _positions[i] = new(p.Id, p.X + delta.X, p.Y + delta.Y);
        }
        return _view;
    }
    public static CanvasPoint Constrain(CanvasPoint delta, bool constrain)
    {
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y))
            throw new ArgumentException("Finite drag displacement required.", nameof(delta));
        if (!constrain) return delta;
        var x = Math.Abs(delta.X); var y = Math.Abs(delta.Y);
        const double boundary = 0.4142135623730950488; // tan(pi / 8)
        if (y <= x * boundary) return new(delta.X, 0);
        if (x <= y * boundary) return new(0, delta.Y);
        var projected = x / 2 + y / 2; // avoid overflow from x + y
        return new(Math.CopySign(projected, delta.X), Math.CopySign(projected, delta.Y));
    }
}
