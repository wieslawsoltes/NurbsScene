using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public sealed partial class GeometryFactory
{
    /// <summary>Closed positive-weight quadratic rational ellipse with four
    /// quarter spans and bit-identical seam controls. Axes are normalized and must
    /// be orthogonal. The [0,1] rational parameter is not proportional to angle or
    /// arc length; use curve evaluation/measurement APIs for those quantities.</summary>
    public NurbsCurve Ellipse(Vec3 center, double radiusX, double radiusY, Vec3? axisX = null, Vec3? axisY = null)
    {
        if (!center.IsFinite || !double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX <= 0 || radiusY <= 0)
            throw new ArgumentException("Ellipse requires a finite center and two finite positive radii.");
        var x = (axisX ?? Vec3.UnitX).Normalized();
        var y = (axisY ?? Vec3.UnitY).Normalized();
        if (Math.Abs(Vec3.Dot(x, y)) > 1e-10) throw new ArgumentException("Ellipse axes must be orthogonal.");
        ReadOnlySpan<int> sx = [1, 1, 0, -1, -1, -1, 0, 1];
        ReadOnlySpan<int> sy = [0, 1, 1, 1, 0, -1, -1, -1];
        var weight = Math.Sqrt(.5);
        var controls = new HPoint[9];
        for (var i = 0; i < 8; i++)
        {
            var p = center + x * (sx[i] * radiusX) + y * (sy[i] * radiusY);
            controls[i] = HPoint.FromPoint(p, (i & 1) == 0 ? 1 : weight);
            if (!controls[i].IsValid) throw new ArgumentException("Ellipse controls exceed the finite homogeneous range.");
        }
        if (controls[0].Position == controls[4].Position || controls[2].Position == controls[6].Position)
            throw new ArgumentException("Ellipse axes collapse at this coordinate scale.");
        controls[8] = controls[0];
        return new(Evaluator, 2, [0, 0, 0, .25, .25, .5, .5, .75, .75, 1, 1, 1], controls);
    }
}
