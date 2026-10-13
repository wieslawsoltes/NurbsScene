using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed record PanelScrollObservation(int Offset, int Maximum, int Visible, int Total, CanvasBox Track, CanvasBox Grip);

public sealed partial class GraphCanvas
{
    private sealed record ScrollPaint(PanelPaintStamp Source, PanelWindow Window, PanelScrollBar Bar);
    private sealed record ScrollCapture(string Id, ScrollPaint Paint, double Grab, int Original);
    private readonly Dictionary<string, int> _panelOffsets = [];
    private readonly Dictionary<string, ScrollPaint> _panelScrollbars = [];
    private ScrollCapture? _panelScrollCapture;
    private int PanelOffset(string id) => _panelOffsets.GetValueOrDefault(id);
    public long PanelIndexBuilds { get; private set; }
    public long PanelFormattedRecords { get; private set; }
    public int RetainedPanelWindows => _panelWindows.Count;
    public PanelScrollObservation? PanelScrolling(string id)
    {
        if (!_panelScrollbars.TryGetValue(id, out var p)) return null;
        CanvasBox Screen(CanvasBox b) => new(b.X * _zoom + _panX, b.Y * _zoom + _panY, b.Width * _zoom, b.Height * _zoom);
        return new(PanelOffset(id), p.Window.MaximumOffset, p.Window.Visible, p.Window.Total, Screen(p.Bar.Track), Screen(p.Bar.Grip));
    }
    private bool ScrollSourceCurrent(ScrollPaint p) => _session is not null && ReferenceEquals(_session.Graph, p.Source.Owner) &&
        _session.Graph.TryGetNode(p.Source.Node.Id, out var node) && ReferenceEquals(node, p.Source.Node) &&
        ReferenceEquals(_session.Graph.LastResult?.Nodes.GetValueOrDefault(node.Id), p.Source.Result) && p.Source.Zoom == _zoom;
    private void ForgetPanelPresentation(string id)
    {
        _panelWindows.Remove(id); _panelOffsets.Remove(id); _panelScrollbars.Remove(id);
        _panelDisplayLayouts.Remove(id); _panelTypography.Forget(id);
    }
    private void ClearPanelPresentation()
    {
        _panelScrollCapture = null; _panelOffsets.Clear(); _panelScrollbars.Clear();
        _panelWindows.Clear(); _panelDisplayLayouts.Clear(); _panelTypography.Clear();
    }
    private static int MeasuredRowBudget(double height, PanelDisplayLayout layout, PanelFontMetrics metrics)
    {
        var top = Math.Min(height, Math.Max(0, layout.FirstBaseline + metrics.Ascent));
        var bottom = Math.Max(top, height - layout.FooterInset - Math.Max(metrics.Descent, layout.FontSize));
        var ink = metrics.Descent - metrics.Ascent; var step = Math.Max(layout.LineHeight, ink);
        return Math.Clamp(1 + (int)Math.Floor((bottom - top - ink) / step), 1, layout.Rows);
    }
    private void DrawScrollingPanel(SKCanvas canvas, GraphNode node, SKRect rect, NodeResult? result, PanelTextStyle style, PanelDisplayLayout original)
    {
        if (!_panelWindows.TryGetValue(node.Id, out var cache))
        {
            // The renderer already bounds native entries; bound retained source
            // windows as well. FIFO eviction never evicts the active scrollbar.
            if (_panelWindows.Count >= PanelTypographyRenderer.MaximumPanels)
                ForgetPanelPresentation(_panelWindows.Keys.First(id => id != _panelScrollCapture?.Id));
            _panelWindows[node.Id] = cache = new();
        }
        var builds = cache.BuildCount; var indexes = cache.IndexBuilds; var records = cache.FormattedRecords;
        var prior = _panelTypography.Observe(node.Id);
        var reserve = Math.Max(10, 10 / _zoom) + 4;
        var body = rect; var layout = original;
        PanelWindow Window(bool scrolling)
        {
            body = rect; if (scrolling) body.Right -= (float)reserve;
            layout = scrolling ? PanelDisplayLayout.Create(new(0, 0, body.Width, body.Height), _zoom, style.Size) : original;
            if (prior is not null && prior.FontSize == layout.FontSize && prior.Style.FontFamily == style.FontFamily && prior.Style.Bold == style.Bold && prior.Style.Italic == style.Italic)
                layout = layout with { Rows = MeasuredRowBudget(body.Height, layout, prior.Metrics) };
            return cache.Get(node, result, layout.Rows, layout.Columns, PanelOffset(node.Id));
        }
        var hadBar = _panelScrollbars.ContainsKey(node.Id);
        var window = Window(hadBar);
        if (window.CanScroll != hadBar) window = Window(window.CanScroll);
        _panelOffsets[node.Id] = window.First;
        var drawn = _panelTypography.Draw(canvas, node.Id, window.Preview, style, body, layout, TypographySource(node, result));
        // A newly resolved font may fit fewer rows than the approximate budget.
        // Correct once, then reuse these measured metrics on subsequent paints.
        var capacity = MeasuredRowBudget(body.Height, layout, drawn.Metrics);
        if (capacity < layout.Rows)
        {
            layout = layout with { Rows = capacity };
            window = cache.Get(node, result, layout.Rows, layout.Columns, PanelOffset(node.Id));
            _panelOffsets[node.Id] = window.First;
            if (window.CanScroll && !hadBar && body == rect)
            { prior = drawn; window = Window(true); _panelOffsets[node.Id] = window.First; }
            drawn = _panelTypography.Draw(canvas, node.Id, window.Preview, style, body, layout, TypographySource(node, result));
        }
        _panelDisplayLayouts[node.Id] = layout;
        ValuePreviewBuilds += cache.BuildCount - builds; PanelIndexBuilds += cache.IndexBuilds - indexes; PanelFormattedRecords += cache.FormattedRecords - records;
        var font = (float)layout.FontSize;
        Text(canvas, node.Name ?? (node.Component == "Text" ? "Text" : "Panel"), rect.Left + 8, rect.Top + (float)layout.HeadingBaseline, new(94, 92, 54), font);
        Text(canvas, drawn.Placement.Clipped && !window.Preview.Truncated ? window.DisplayFooter + " · …" : window.DisplayFooter,
            rect.Left + 8, rect.Bottom - (float)layout.FooterInset, new(112, 105, 69), font * .9f);
        var clip = drawn.Placement.Clip;
        if (window.CanScroll && clip.Height > 0)
        {
            var track = new CanvasBox(rect.Right - reserve + 2, rect.Top + clip.Y, reserve - 8, clip.Height);
            var bar = PanelScrollBar.Create(track, window, 14 / _zoom);
            _panelScrollbars[node.Id] = new(TypographySource(node, result), window, bar);
            static SKRect Box(CanvasBox b) => new((float)b.X, (float)b.Y, (float)(b.X + b.Width), (float)(b.Y + b.Height));
            Fill(canvas, Box(bar.Track), new(110, 107, 60, 28), 2 / _zoom);
            Fill(canvas, Box(bar.Grip), new(115, 110, 62, 155), 2 / _zoom);
            Stroke(canvas, Box(bar.Grip), new(91, 88, 49), .7f / _zoom, 2 / _zoom);
        }
        else _panelScrollbars.Remove(node.Id);
    }
    private bool TryBeginPanelScroll(GraphNode node, Point point)
    {
        if (!_panelScrollbars.TryGetValue(node.Id, out var p) || !ScrollSourceCurrent(p) || !p.Bar.Track.Contains(new(point.X, point.Y))) return false;
        var original = PanelOffset(node.Id);
        var grip = p.Bar.Grip.Contains(new(point.X, point.Y));
        _panelScrollCapture = new(node.Id, p, point.Y - p.Bar.Grip.Y, original);
        _gesture = grip ? "panel-scroll" : "panel-page";
        if (!grip) _panelOffsets[node.Id] = PanelScrollBar.Move(original, point.Y < p.Bar.Grip.Y ? -p.Window.PageStep : p.Window.PageStep, p.Window.MaximumOffset);
        return true;
    }
    private void MovePanelScroll(double y)
    {
        if (_panelScrollCapture is not { } c) return;
        if (!ScrollSourceCurrent(c.Paint)) { CancelPanelScroll(false); _gesture = ""; ReleasePointerCaptures(); return; }
        _panelOffsets[c.Id] = c.Paint.Bar.Drag(y, c.Grab);
    }
    private void CancelPanelScroll(bool restore)
    {
        var capture = _panelScrollCapture; _panelScrollCapture = null;
        if (restore && capture is not null && ScrollSourceCurrent(capture.Paint)) _panelOffsets[capture.Id] = capture.Original;
    }
    private void CancelStalePanelScroll()
    {
        if (_panelScrollCapture is { } c && !ScrollSourceCurrent(c.Paint))
        { CancelPanelScroll(false); _gesture = ""; ReleasePointerCaptures(); }
    }
    private bool TryPanelWheel(PointerRoutedEventArgs e)
    {
        if (e.KeyModifiers != VirtualKeyModifiers.None) return false;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0) return false;
        var at = World(point.Position); var node = NodeAt(at);
        // The narrow scrollbar owns wheel input; the rest of the canvas retains
        // the original zoom gesture, including over panel text and with Ctrl.
        if (node is null || !_panelScrollbars.TryGetValue(node.Id, out var p) || !ScrollSourceCurrent(p) || !p.Bar.Track.Contains(new(at.X, at.Y))) return false;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Abs((double)point.Properties.MouseWheelDelta) / 120 * 3));
        _panelOffsets[node.Id] = PanelScrollBar.Move(PanelOffset(node.Id), point.Properties.MouseWheelDelta > 0 ? -step : step, p.Window.MaximumOffset);
        Invalidate(); return true;
    }
    private bool TryPanelScrollKey(VirtualKey key)
    {
        if (_gesture != "" || _selection.Count != 1 || key is not (VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)) return false;
        var id = _selection.First();
        if (!_panelScrollbars.TryGetValue(id, out var p) || !ScrollSourceCurrent(p)) return false;
        var delta = key switch { VirtualKey.Home => long.MinValue, VirtualKey.End => long.MaxValue, VirtualKey.PageUp => -p.Window.PageStep, _ => (long)p.Window.PageStep };
        _panelOffsets[id] = PanelScrollBar.Move(PanelOffset(id), delta, p.Window.MaximumOffset);
        Invalidate(); return true;
    }
}
