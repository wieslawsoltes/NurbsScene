using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class GraphCanvas
{
    private VirtualKeyModifiers _lastPointerModifiers;
    private uint? _activePointerId;
    private int _pointerButton;
    private bool _applyingPointerEdit;
    private GraphDragPositions? _dragPositions;
    private HashSet<string>? _marqueeBefore;
    private GraphSelectionMode _marqueeMode;

    private static bool Shift(VirtualKeyModifiers modifiers) => (modifiers & VirtualKeyModifiers.Shift) != 0;
    private static GraphSelectionMode SelectionMode(VirtualKeyModifiers modifiers)
        => GraphSelectionModifiers.Mode(Shift(modifiers), (modifiers & VirtualKeyModifiers.Control) != 0);
    private void ClearPointerDrafts()
    {
        _activePointerId = null; _pointerButton = 0; _dragPositions = null; _marqueeBefore = null;
    }
    private void InitializePointerInput()
    {
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCanceled += (_, e) =>
        {
            if (_activePointerId != e.Pointer.PointerId) return;
            Observe("Cancelled", _last); CancelGesture(); ReleasePointerCapture(e.Pointer);
        };
        PointerCaptureLost += (_, e) =>
        {
            if (_activePointerId != e.Pointer.PointerId) return;
            Observe("CaptureLost", _last); CancelGesture();
        };
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null) return;
        // A second touch or mouse button must not erase the first gesture's
        // rollback snapshot. Only the pointer that owns capture may update it.
        if (_activePointerId is not null) { e.Handled = true; return; }
        _lastPointerModifiers = e.KeyModifiers;
        ClearPanelResize(); ClearPointerDrafts();
        var point = e.GetCurrentPoint(this);
        _start = _last = point.Position; _worldStart = _worldLast = World(point.Position);
        _moved = false; _before = null; _slider = null; _wire = null;
        Focus(FocusState.Pointer);
        if (!CapturePointer(e.Pointer)) return;
        _activePointerId = e.Pointer.PointerId;
        _pointerButton = point.Properties.IsRightButtonPressed ? 3 : point.Properties.IsMiddleButtonPressed ? 2 : 1;
        try
        {
            var node = NodeAt(_worldStart); var port = PortAt(_worldStart);
            _rightPan = point.Properties.IsRightButtonPressed;
            if (point.Properties.IsMiddleButtonPressed || _rightPan) _gesture = "pan";
            else if (port is not null) { _wire = port; _gesture = "wire"; }
            else if (node is not null)
            {
                var change = GraphSelectionModifiers.Click(_selection, node.Id, SelectionMode(e.KeyModifiers));
                if (change.Changed) SelectionChanged?.Invoke();
                if (!change.CanDrag)
                {
                    _gesture = ""; ClearPointerDrafts(); ReleasePointerCapture(e.Pointer);
                    Invalidate(); e.Handled = true; return;
                }
                var edges = ResizeHit(node, _worldStart);
                if (edges != PanelResizeEdges.None)
                { _gesture = "resize"; _panelResize = new(_session.Graph, node.Id, edges); }
                else if ((node.Component is "Number" or "Integer") && _worldStart.Y > Rect(node).Bottom - 22)
                { _before = _session.Graph.Snapshot(); _gesture = "slider"; _slider = node.Id; Scrub(node, _worldStart.X); }
                else
                {
                    _gesture = "nodes";
                    _dragPositions = new(_session.Graph.Nodes.Where(n => _selection.Contains(n.Id))
                        .Select(n => new NodePosition(n.Id, n.X, n.Y)));
                }
            }
            else
            {
                _marqueeBefore = new(_selection, StringComparer.Ordinal);
                _marqueeMode = SelectionMode(e.KeyModifiers); _gesture = "marquee";
                if (_marqueeMode == GraphSelectionMode.Replace && _selection.Count > 0)
                { _selection.Clear(); SelectionChanged?.Invoke(); }
            }
            Observe("Pressed", point.Position); Invalidate(); e.Handled = true;
        }
        catch (Exception error)
        { CancelGesture(); ReleasePointerCapture(e.Pointer); Message?.Invoke(error.Message); e.Handled = true; }
    }
    private void Scrub(GraphNode node, double x)
    {
        if (!NumberSliderSettings.TryRead(node, out var settings, out var previous, out var error))
        { Message?.Invoke(error!); _gesture = ""; _before = null; return; }
        var fraction = (x - node.X - 13) / 154;
        if (!double.IsFinite(fraction)) return;
        var value = settings.ValueAt(fraction);
        if (value == previous) return;
        _applyingPointerEdit = true;
        try { _session!.Graph.Update(node with { Parameters = node.Parameters.SetItem("value", value) }); }
        finally { _applyingPointerEdit = false; }
    }
    private void MoveSelectedNodes(Point screen, VirtualKeyModifiers modifiers)
    {
        if (_dragPositions is null || _session is null) return;
        var positions = _dragPositions.Update(new((screen.X - _start.X) / _zoom, (screen.Y - _start.Y) / _zoom), Shift(modifiers));
        _before ??= _session.Graph.Snapshot();
        _applyingPointerEdit = true;
        try { _session.Graph.MoveNodes(positions); }
        finally { _applyingPointerEdit = false; }
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId is { } owner && owner != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(this).Position;
        _worldLast = World(point);
        if (_gesture == "" || _session is null) return;
        _lastPointerModifiers = e.KeyModifiers;
        _moved |= Math.Abs(point.X - _start.X) + Math.Abs(point.Y - _start.Y) > 2;
        try
        {
            if (_gesture == "pan") { _panX += (float)(point.X - _last.X); _panY += (float)(point.Y - _last.Y); }
            else if (_gesture == "resize" && _moved) _panelResize?.Move((point.X - _start.X) / _zoom, (point.Y - _start.Y) / _zoom);
            else if (_gesture == "nodes" && _moved) MoveSelectedNodes(point, e.KeyModifiers);
            else if (_gesture == "slider" && _slider is not null) Scrub(_session.Graph.GetNode(_slider), _worldLast.X);
            _last = point; Observe("Moved", point); Invalidate(); e.Handled = true;
        }
        catch (Exception error)
        { CancelGesture(); ReleasePointerCapture(e.Pointer); Message?.Invoke(error.Message); e.Handled = true; }
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null || _activePointerId != e.Pointer.PointerId) return;
        var release = e.GetCurrentPoint(this);
        // Mouse buttons share one pointer ID. Releasing a secondary button must
        // not commit a gesture whose initiating button remains held.
        if ((_pointerButton == 1 && release.Properties.IsLeftButtonPressed)
            || (_pointerButton == 2 && release.Properties.IsMiddleButtonPressed)
            || (_pointerButton == 3 && release.Properties.IsRightButtonPressed))
        { e.Handled = true; return; }
        Point? context = null;
        try
        {
            var screen = release.Position;
            _worldLast = World(screen); _lastPointerModifiers = e.KeyModifiers;
            // Pointer-release coordinates are authoritative, even when the final
            // move event was coalesced or never delivered by the host.
            _moved |= Math.Abs(screen.X - _start.X) + Math.Abs(screen.Y - _start.Y) > 2;
            Observe("Released", screen);
            if (_gesture == "pan")
            {
                _panX += (float)(screen.X - _last.X); _panY += (float)(screen.Y - _last.Y);
                _worldLast = World(screen);
                if (_rightPan && !_moved)
                {
                    var node = NodeAt(_worldLast);
                    if (node is null) SelectNodes([]); else if (!_selection.Contains(node.Id)) Select(node.Id, false);
                    context = screen;
                }
            }
            else if (_gesture == "resize" && _panelResize is { } resize && _moved)
            { resize.Move((screen.X - _start.X) / _zoom, (screen.Y - _start.Y) / _zoom); resize.Commit(_session); }
            else if (_gesture == "nodes" && _moved) MoveSelectedNodes(screen, e.KeyModifiers);
            else if (_gesture == "slider" && _slider is not null) Scrub(_session.Graph.GetNode(_slider), _worldLast.X);
            if (_wire is { } source && PortAt(_worldLast) is { } target && source.Output != target.Output)
            {
                var output = source.Output ? source : target; var input = source.Output ? target : source;
                _session.Edit("Connect ports", graph => graph.Connect(new(output.Node, output.Port), new(input.Node, input.Port), replace: !Shift(e.KeyModifiers)));
            }
            if (_before is not null && (_moved || _gesture == "slider"))
                _session.Record(_gesture == "slider" ? "Change number" : "Move components", _before);
            if (_gesture == "marquee" && _moved)
            {
                GraphSelectionModifiers.Rectangle(_selection,
                    GraphSelection.InRectangle(Drawing(), new(_worldStart.X, _worldStart.Y), new(_worldLast.X, _worldLast.Y)), _marqueeMode);
                Observe("SelectionCommitted", screen);
            }
        }
        catch (Exception error) { CancelGesture(); Message?.Invoke(error.Message); }
        finally
        {
            ClearPanelResize(); ClearPointerDrafts(); _gesture = ""; _wire = null; _before = null; _slider = null;
            ReleasePointerCapture(e.Pointer); Invalidate(); SelectionChanged?.Invoke(); e.Handled = true;
        }
        if (context is { } point) ContextMenuRequested?.Invoke(point);
    }
    private void AbandonStalePointerEdit()
    {
        // An independent host edit may already have removed or replaced nodes.
        // A whole-graph rollback here would erase that edit. Keep the host state
        // and discard only our stale continuation and rollback snapshot.
        Observe("Interrupted", _last);
        _before = null;
        ClearPanelResize(); ClearPointerDrafts(); _gesture = ""; _wire = null; _slider = null;
        ReleasePointerCaptures();
        Message?.Invoke("Pointer edit interrupted by a definition change; the current definition was retained.");
    }
    private void CancelGesture()
    {
        var before = _before; var selection = _marqueeBefore;
        ClearPanelResize(); ClearPointerDrafts(); _before = null; _gesture = ""; _wire = null; _slider = null;
        if (before is not null && _session is not null) _session.RestoreUncommitted(before);
        if (selection is not null)
        {
            _selection.Clear();
            if (_session is not null)
                foreach (var id in selection) if (_session.Graph.TryGetNode(id, out _)) _selection.Add(id);
            SelectionChanged?.Invoke();
        }
        Invalidate();
    }
}
