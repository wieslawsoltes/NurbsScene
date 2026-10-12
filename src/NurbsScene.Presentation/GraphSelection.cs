using System.Collections.Immutable;

namespace NurbsScene.Presentation;

/// <summary>Window (left-to-right) and crossing (right-to-left) selection in
/// authoritative canvas coordinates. Vertical drag direction is unrestricted.
/// Does not mutate the graph or depend on the graphics library's rectangle API.</summary>
public static class GraphSelection
{
    public static ImmutableArray<string> InRectangle(GraphDrawing drawing,CanvasPoint start,CanvasPoint end)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if(!double.IsFinite(start.X)||!double.IsFinite(start.Y)||!double.IsFinite(end.X)||!double.IsFinite(end.Y))
            throw new ArgumentException("Finite selection coordinates required.");
        var left=Math.Min(start.X,end.X);var right=Math.Max(start.X,end.X);
        var top=Math.Min(start.Y,end.Y);var bottom=Math.Max(start.Y,end.Y);
        var enclosed=end.X>=start.X;
        var result=ImmutableArray.CreateBuilder<string>();
        foreach(var node in drawing.Nodes)
        {
            var b=node.Bounds;
            var selected=enclosed
                ? b.X>=left&&b.Y>=top&&b.X+b.Width<=right&&b.Y+b.Height<=bottom
                : b.X<=right&&b.X+b.Width>=left&&b.Y<=bottom&&b.Y+b.Height>=top;
            if(selected)result.Add(node.Node.Id);
        }
        return result.ToImmutable();
    }
}
