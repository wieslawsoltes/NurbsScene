using System.Numerics;
using Uno.UI.Composition.Drawing;
using Uno.UI.Composition.Managed;
using Windows.Foundation;

namespace NurbsScene.Controls.Drawing;

/// <summary>Small drawing vocabulary for the controls, backed by Uno's geometry and GPU drawing seams.</summary>
public struct VectorRect(float left, float top, float right, float bottom)
{
    public float Left = left, Top = top, Right = right, Bottom = bottom;
    public readonly float Width => Right - Left;
    public readonly float Height => Bottom - Top;
    public readonly float MidX => (Left + Right) * .5f;
    public readonly float MidY => (Top + Bottom) * .5f;
    internal readonly Rect Rect => new(Left, Top, Math.Max(0, Width), Math.Max(0, Height));
}

public readonly record struct VectorColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public VectorColor WithAlpha(byte alpha) => this with { Alpha = alpha };
    internal Windows.UI.Color Color => Windows.UI.Color.FromArgb(Alpha, Red, Green, Blue);
}

public static class VectorColors
{
    public static VectorColor White => new(255, 255, 255);
    public static VectorColor Transparent => new(0, 0, 0, 0);
}

public enum VectorPaintStyle { Fill, Stroke }
public enum VectorStrokeCap { Butt, Round, Square }
public enum VectorStrokeJoin { Miter, Round, Bevel }

public sealed class VectorPaint : IDisposable
{
    public bool IsAntialias { get; set; } = true;
    public VectorColor Color { get; set; }
    public VectorPaintStyle Style { get; set; }
    public float StrokeWidth { get; set; } = 1;
    public VectorStrokeCap StrokeCap { get; set; }
    public VectorStrokeJoin StrokeJoin { get; set; }
    public VectorPathEffect? PathEffect { get; set; }
    internal StrokeStyle Stroke => new()
    {
        Thickness = StrokeWidth,
        StartCap = (StrokeCap)StrokeCap, EndCap = (StrokeCap)StrokeCap,
        DashCap = (StrokeCap)StrokeCap, LineJoin = (StrokeJoin)StrokeJoin,
        MiterLimit = 4, DashArray = PathEffect?.Intervals, DashOffset = PathEffect?.Phase ?? 0,
        // Uno uses (0, 0) to disable trimming. (0, 1) rebuilds closed outlines as open contours.
        TrimStart = 0, TrimEnd = 0
    };
    public void Dispose() { }
}

public sealed class VectorPathEffect(float[] intervals, float phase) : IDisposable
{
    internal float[] Intervals { get; } = (float[])intervals.Clone();
    internal float Phase { get; } = phase;
    public static VectorPathEffect CreateDash(float[] intervals, float phase) => new(intervals, phase);
    public void Dispose() { }
}

/// <summary>Owns a neutral immutable outline once built; the GPU recorder retains its own reference.</summary>
public sealed class VectorPath : IDisposable
{
    internal static readonly IGeometryFactory GeometryFactory = ManagedBackend.CreateGeometryFactory();
    private readonly IPathBuilder _builder = GeometryFactory.CreatePathBuilder();
    private IGeometry? _geometry;
    private bool _built;
    private void Editing()
    {
        if (_built) throw new InvalidOperationException("A drawing path is immutable after first use.");
    }
    public void MoveTo(float x, float y) { Editing(); _builder.MoveTo(new(x, y)); }
    public void LineTo(float x, float y) { Editing(); _builder.LineTo(new(x, y)); }
    public void CubicTo(float x1, float y1, float x2, float y2, float x, float y)
    { Editing(); _builder.CubicTo(new(x1, y1), new(x2, y2), new(x, y)); }
    public void Close() { Editing(); _builder.Close(); }
    internal IGeometry Geometry
    {
        get { _built = true; return _geometry ??= _builder.Build(); }
    }
    public void Dispose() { _geometry?.Dispose(); _geometry = null; }
}

/// <summary>
/// Immediate vector commands recorded by Uno's WebGPU backend. Curves remain geometry and text remains
/// positioned glyphs: this layer never creates a CPU canvas or uploads a rendered bitmap.
/// </summary>
public sealed class VectorCanvas(IDrawingSession session)
{
    internal IDrawingSession Session { get; } = session;
    public IDrawingFactory Factory => Session.Factory;
    public void Save() => Session.Save();
    public void Restore() => Session.Restore();
    public void Translate(float x, float y) => Session.Translate(x, y);
    public void Scale(float value) => Session.Scale(value, value);
    public void Scale(float x, float y) => Session.Scale(x, y);
    public void ClipRect(VectorRect rect) => Session.ClipRect(rect.Rect);
    public void DrawRect(float x, float y, float width, float height, VectorPaint paint)
        => DrawRect(new(x, y, x + width, y + height), paint);
    public void DrawRect(VectorRect rect, VectorPaint paint)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (paint.Style == VectorPaintStyle.Fill) { Session.DrawRect(rect.Rect, paint.Color.Color); return; }
        var builder = VectorPath.GeometryFactory.CreatePrimitiveGeometryBuilder(); builder.AddRectangle(rect.Rect);
        using var geometry = builder.Build(); DrawGeometry(geometry, paint);
    }
    public void DrawRoundRect(VectorRect rect, float radiusX, float radiusY, VectorPaint paint)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (paint.Style == VectorPaintStyle.Fill)
        {
            var r = new Vector2(Math.Clamp(radiusX, 0, rect.Width / 2), Math.Clamp(radiusY, 0, rect.Height / 2));
            Session.DrawRoundedRect(new() { Rect = rect.Rect, TopLeft = r, TopRight = r, BottomRight = r, BottomLeft = r }, paint.Color.Color);
            return;
        }
        var builder = VectorPath.GeometryFactory.CreatePrimitiveGeometryBuilder();
        builder.AddRoundedRectangle(rect.Rect, radiusX, radiusY);
        using var geometry = builder.Build(); DrawGeometry(geometry, paint);
    }
    public void DrawLine(float x, float y, float endX, float endY, VectorPaint paint)
    {
        if (paint.PathEffect is null && paint.StrokeCap == VectorStrokeCap.Butt)
        { Session.DrawLine(new(x, y), new(endX, endY), paint.Color.Color, paint.StrokeWidth); return; }
        var builder = VectorPath.GeometryFactory.CreatePathBuilder();
        builder.MoveTo(new(x, y)); builder.LineTo(new(endX, endY));
        using var geometry = builder.Build(); StrokeGeometry(geometry, paint);
    }
    public void DrawCircle(float x, float y, float radius, VectorPaint paint)
        => DrawOval(new(x - radius, y - radius, x + radius, y + radius), paint);
    public void DrawOval(VectorRect bounds, VectorPaint paint)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var builder = VectorPath.GeometryFactory.CreatePrimitiveGeometryBuilder();
        builder.AddEllipse(new(bounds.MidX, bounds.MidY), bounds.Width / 2, bounds.Height / 2);
        using var geometry = builder.Build(); DrawGeometry(geometry, paint);
    }
    public void DrawPath(VectorPath path, VectorPaint paint) => DrawGeometry(path.Geometry, paint);
    private void DrawGeometry(IGeometry geometry, VectorPaint paint)
    {
        if (paint.Style == VectorPaintStyle.Fill) Session.DrawPath(geometry, paint.Color.Color);
        else StrokeGeometry(geometry, paint);
    }
    private void StrokeGeometry(IGeometry geometry, VectorPaint paint)
    {
        if (paint.StrokeWidth <= 0) return;
        if (paint.PathEffect is null && paint.StrokeCap == VectorStrokeCap.Butt)
        { Session.StrokePath(geometry, paint.Color.Color, paint.StrokeWidth, (StrokeJoin)paint.StrokeJoin); return; }
        using var stroke = geometry.GetStrokeFillGeometry(paint.Stroke, GeometryFlatteningScale.From(Session.TotalMatrix));
        Session.DrawPath(stroke, paint.Color.Color);
    }
    public void DrawText(string text, float x, float y, VectorFont font, VectorPaint paint)
    {
        if (text.Length == 0) return;
        using var run = VectorTextBlob.Create(text, font); DrawText(run, x, y, paint);
    }
    public void DrawText(VectorTextBlob run, float x, float y, VectorPaint paint)
    {
        Session.Save();
        try
        {
            Session.Translate(x, y);
            if (run.SkewX != 0) Session.Concat(new Matrix4x4(1, 0, 0, 0, run.SkewX, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
            Session.DrawGlyphRun(run.Font, run.Glyphs, run.Positions, 0, paint.Color.Color);
            if (run.Embolden) Session.StrokeGlyphRun(run.Font, run.Glyphs, run.Positions, 0, paint.Color.Color, run.Size / 32);
        }
        finally { Session.Restore(); }
    }
    public void DrawPicture(VectorPicture picture) => picture.Record.Replay(Session);
}

public sealed class VectorPicture(IRenderRecord record) : IDisposable
{
    internal IRenderRecord Record { get; } = record;
    public void Dispose() => Record.Dispose();
}

public sealed class VectorPictureRecorder(IDrawingFactory factory) : IDisposable
{
    private ICommandRecorder? _recorder;
    public VectorCanvas BeginRecording(VectorRect bounds)
    {
        _recorder = factory.CreateRecording();
        return new(_recorder);
    }
    public VectorPicture EndRecording()
    {
        var recorder = _recorder ?? throw new InvalidOperationException("No recording is active.");
        _recorder = null; return new(recorder.Finish());
    }
    public void Dispose() { if (_recorder is { } recorder) { _recorder = null; recorder.Finish().Dispose(); } }
}
