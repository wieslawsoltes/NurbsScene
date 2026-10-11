namespace NurbsScene.Geometry;

/// <summary>Immutable normalized plane, constructed from a point and nonzero
/// normal. Projection and reflection are exact affine modeling transforms up to
/// binary64 arithmetic; no tessellation is involved.</summary>
public sealed class Plane3
{
    public Vec3 Origin {get;}
    public Vec3 Normal {get;}
    public Plane3(Vec3 origin,Vec3 normal)
    {
        if(!origin.IsFinite||!normal.IsFinite||normal.Length==0)throw new ArgumentException("A plane needs a finite origin and nonzero finite normal.");
        Origin=origin;Normal=normal.Normalized();
    }
    public double SignedDistance(Vec3 point)
    {
        if(!point.IsFinite)throw new ArgumentException("Non-finite point.");
        var value=Vec3.Dot(point-Origin,Normal);if(!double.IsFinite(value))throw new ArithmeticException("Plane distance overflow.");return value;
    }
    public Vec3 Project(Vec3 point)
    {
        var p=point-Normal*SignedDistance(point);if(!p.IsFinite)throw new ArithmeticException("Plane projection overflow.");return p;
    }
    public Transform3 Projection=>Transform(1);
    public Transform3 Reflection=>Transform(2);
    private Transform3 Transform(double factor)
    {
        var n=Normal;var x=n.X;var y=n.Y;var z=n.Z;var offset=factor*Vec3.Dot(n,Origin);var t=n*offset;
        return new([1-factor*x*x,-factor*y*x,-factor*z*x,0,
            -factor*x*y,1-factor*y*y,-factor*z*y,0,
            -factor*x*z,-factor*y*z,1-factor*z*z,0,t.X,t.Y,t.Z,1]);
    }
}
