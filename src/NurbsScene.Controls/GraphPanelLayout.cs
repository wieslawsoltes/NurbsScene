using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

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
        return output ? point with { X = CanvasBounds(node).X + CanvasBounds(node).Width } : point;
    }
    private CanvasPoint WireStart(PresentedWire wire) => _panelResize?.NodeId == wire.Wire.From.Node
        ? wire.Start with { X = _panelResize.Bounds.X + _panelResize.Bounds.Width } : wire.Start;
    private CanvasBox WireCanvasBounds(PresentedWire wire)
    {
        var start = WireStart(wire);
        if (start == wire.Start) return wire.Bounds;
        var end = wire.End; var delta = Math.Max(35, Math.Abs(end.X - start.X) * .48);
        var left = Math.Min(start.X, end.X - delta); var right = Math.Max(start.X + delta, end.X);
        return new(left, Math.Min(start.Y, end.Y) - 3, right - left, Math.Abs(start.Y - end.Y) + 6);
    }
    private void DrawWirePath(SKCanvas canvas, PresentedWire wire, SKPath ordinary)
    {
        var start = WireStart(wire);
        if (start == wire.Start) { canvas.DrawPath(ordinary, _paint); return; }
        if (!_resizePaths.TryGetValue(wire.Wire.Id, out var cached) || cached.Start != start || cached.End != wire.End)
        {
            cached.Path?.Dispose();
            cached = (start, wire.End, MakePath(new(start.X, start.Y), new(wire.End.X, wire.End.Y)));
            _resizePaths[wire.Wire.Id] = cached; ResizePathBuilds++;
        }
        canvas.DrawPath(cached.Path, _paint);
    }
    private void ClearPanelResize()
    {
        _panelResize = null;
        foreach (var entry in _resizePaths.Values) entry.Path.Dispose();
        _resizePaths.Clear();
    }
    private void DrawResizeHandle(SKCanvas canvas, SKRect bounds)
    {
        _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1; _paint.Color = new(126,118,67);
        for (var inset = 4; inset <= 10; inset += 3)
            canvas.DrawLine(bounds.Right - inset - 2, bounds.Bottom - 3, bounds.Right - 3, bounds.Bottom - inset - 2, _paint);
    }
}
