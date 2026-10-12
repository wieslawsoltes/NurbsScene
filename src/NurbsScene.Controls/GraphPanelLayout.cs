using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;
using Windows.Foundation;

namespace NurbsScene.Controls;

public sealed partial class GraphCanvas
{
    private PanelResizeDraft? _panelResize;
    private readonly Dictionary<string, (CanvasPoint Start, CanvasPoint End, SKPath Path)> _resizePaths = [];
    public CanvasBox? PanelResizeBounds => _panelResize?.Bounds;
    public long ResizePathBuilds { get; private set; }
    private CanvasBox CanvasBounds(PresentedNode node) => _panelResize?.NodeId == node.Node.Id ? _panelResize.Bounds : node.Bounds;
    private CanvasPoint CanvasPort(PresentedNode node, bool output, int index)
    {
        var point = node.Port(output, index);
        return _panelResize?.NodeId == node.Node.Id ? _panelResize.MapPort(point, output) : point;
    }
    private CanvasPoint WireStart(PresentedWire wire) => _panelResize?.NodeId == wire.Wire.From.Node
        ? _panelResize.MapPort(wire.Start, true) : wire.Start;
    private CanvasPoint WireEnd(PresentedWire wire) => _panelResize?.NodeId == wire.Wire.To.Node
        ? _panelResize.MapPort(wire.End, false) : wire.End;
    private CanvasBox WireCanvasBounds(PresentedWire wire)
    {
        var start = WireStart(wire); var end = WireEnd(wire);
        if (start == wire.Start && end == wire.End) return wire.Bounds;
        var delta = Math.Max(35, Math.Abs(end.X - start.X) * .48);
        var left = Math.Min(start.X, end.X - delta); var right = Math.Max(start.X + delta, end.X);
        return new(left, Math.Min(start.Y, end.Y) - 3, right - left, Math.Abs(start.Y - end.Y) + 6);
    }
    private void DrawWirePath(SKCanvas canvas, PresentedWire wire, SKPath ordinary)
    {
        var start = WireStart(wire); var end = WireEnd(wire);
        if (start == wire.Start && end == wire.End) { canvas.DrawPath(ordinary, _paint); return; }
        if (!_resizePaths.TryGetValue(wire.Wire.Id, out var cached) || cached.Start != start || cached.End != end)
        {
            cached.Path?.Dispose();
            cached = (start, end, MakePath(new(start.X, start.Y), new(end.X, end.Y)));
            _resizePaths[wire.Wire.Id] = cached; ResizePathBuilds++;
        }
        canvas.DrawPath(cached.Path, _paint);
    }
    private PanelResizeEdges ResizeHit(GraphNode node, Point point)
    {
        if (!PanelLayout.CanResize(node)) return PanelResizeEdges.None;
        try { return PanelLayout.HitTest(Layout(node).Bounds, new(point.X, point.Y), Math.Min(128, 10 / _zoom)); }
        catch (ArgumentException error) { Message?.Invoke(error.Message); return PanelResizeEdges.None; }
    }
    private void ClearPanelResize()
    {
        _panelResize = null;
        foreach (var entry in _resizePaths.Values) entry.Path.Dispose();
        _resizePaths.Clear();
    }
    private void DrawResizeHandles(SKCanvas canvas, SKRect bounds, bool selected)
    {
        _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1; _paint.Color = new(126,118,67);
        for (var inset = 4; inset <= 10; inset += 3)
            canvas.DrawLine(bounds.Right - inset - 2, bounds.Bottom - 3, bounds.Right - 3, bounds.Bottom - inset - 2, _paint);
        if (!selected) return;
        var size = Math.Min(6 / _zoom, Math.Min(bounds.Width, bounds.Height) / 6);
        // Authored grips remain inside node bounds; no new geometry objects,
        // per-frame collections, or layout/solve invalidations are introduced.
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                if (row == 1 && col == 1) continue;
                var x = col == 0 ? bounds.Left : col == 2 ? bounds.Right - size : bounds.MidX - size / 2;
                var y = row == 0 ? bounds.Top : row == 2 ? bounds.Bottom - size : bounds.MidY - size / 2;
                var grip = new SKRect(x, y, x + size, y + size);
                Fill(canvas, grip, new(249,245,201)); Stroke(canvas, grip, new(109,105,72), 1 / _zoom);
            }
    }
}
