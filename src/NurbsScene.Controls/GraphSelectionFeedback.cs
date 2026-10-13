using NurbsScene.Presentation;
using SkiaSharp;

namespace NurbsScene.Controls;

public sealed partial class GraphCanvas
{
    private void DrawSelectionFeedback(SKCanvas canvas)
    {
        if (_gesture == "marquee")
        {
            // Crossing rectangles are dashed; window rectangles are solid.
            // Color additionally distinguishes subtract/toggle from add/replace.
            var color = _marqueeMode switch
            {
                GraphSelectionMode.Remove => new SKColor(183, 67, 48),
                GraphSelectionMode.Toggle => new SKColor(157, 116, 36),
                _ => new SKColor(100, 155, 35)
            };
            var rect = new SKRect((float)Math.Min(_worldStart.X, _worldLast.X),
                (float)Math.Min(_worldStart.Y, _worldLast.Y), (float)Math.Max(_worldStart.X, _worldLast.X),
                (float)Math.Max(_worldStart.Y, _worldLast.Y));
            Fill(canvas, rect, color.WithAlpha(30));
            _paint.PathEffect = _worldLast.X < _worldStart.X ? _treeDash : null;
            try { Stroke(canvas, rect, color, 1 / _zoom); }
            finally { _paint.PathEffect = null; }
        }
        else if (_gesture == "nodes" && _moved && Shift(_lastPointerModifiers))
        {
            var delta = GraphDragPositions.Constrain(new((_last.X - _start.X) / _zoom,
                (_last.Y - _start.Y) / _zoom), true);
            _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1 / _zoom;
            _paint.Color = new(64, 95, 19); _paint.PathEffect = _treeDash;
            try
            {
                canvas.DrawLine((float)_worldStart.X, (float)_worldStart.Y,
                    (float)(_worldStart.X + delta.X), (float)(_worldStart.Y + delta.Y), _paint);
            }
            finally { _paint.PathEffect = null; }
        }
    }
}
