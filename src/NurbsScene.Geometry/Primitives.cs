using System.Runtime.InteropServices;

namespace NurbsScene.Geometry;

/// <summary>Binary64 modeling vector. Rendering conversion must happen after origin rebasing.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => default;
    public static Vec3 UnitX => new(1, 0, 0);
    public static Vec3 UnitY => new(0, 1, 0);
    public static Vec3 UnitZ => new(0, 0, 1);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public double Length { get { var scale=Math.Max(Math.Abs(X),Math.Max(Math.Abs(Y),Math.Abs(Z)));return scale==0?0:scale*Math.Sqrt(Dot(this/scale,this/scale)); } }
    public Vec3 Normalized() { var scale=Math.Max(Math.Abs(X),Math.Max(Math.Abs(Y),Math.Abs(Z))); if(!(scale>0)||!IsFinite)throw new ArithmeticException("Cannot normalize a zero or non-finite vector.");var p=this/scale;return p/Math.Sqrt(Dot(p,p)); }
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator *(double b, Vec3 a) => a * b;
    public static Vec3 operator /(Vec3 a, double b) => new(a.X / b, a.Y / b, a.Z / b);
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a * (1 - t) + b * t;
    public double[] ToArray() => [X, Y, Z];
}
[StructLayout(LayoutKind.Sequential)]
public readonly record struct HPoint(double X, double Y, double Z, double W)
{
    public bool IsValid => W > 0 && double.IsFinite(W) && double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) && double.IsFinite(X/W) && double.IsFinite(Y/W) && double.IsFinite(Z/W);
    public Vec3 Position => new(X / W, Y / W, Z / W);
    public static HPoint FromPoint(Vec3 p, double weight = 1) => new(p.X * weight, p.Y * weight, p.Z * weight, weight);
    public static HPoint operator +(HPoint a, HPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static HPoint operator -(HPoint a, HPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static HPoint operator *(HPoint a, double b) => new(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static HPoint operator /(HPoint a, double b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public static HPoint Lerp(HPoint a, HPoint b, double t) => a * (1 - t) + b * t;
}
public readonly record struct Interval(double Min, double Max)
{
    public double Length => Max - Min;
    public bool Contains(double value) => double.IsFinite(value) && value >= Min && value <= Max;
    public double At(double fraction) => Min + Length * fraction;
}
public readonly record struct Bounds3(Vec3 Min, Vec3 Max)
{
    public static Bounds3 Empty => new(new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));
    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vec3 Center => Min / 2 + Max / 2;
    public Vec3 Size => Max - Min;
    public Bounds3 Include(Vec3 p) => new(new(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)), new(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));
    public Bounds3 Union(Bounds3 b) => b.IsEmpty ? this : Include(b.Min).Include(b.Max);
}
/// <summary>Column-major projective transform. Uses double precision for authoring and camera calculations.</summary>
public sealed class Transform3
{
    private readonly double[] _values;
    public ReadOnlySpan<double> Values => _values;
    public Transform3(IEnumerable<double> values)
    {
        _values = values.ToArray();
        if (_values.Length != 16 || _values.Any(x => !double.IsFinite(x))) throw new ArgumentException("A finite 4×4 transform is required.");
    }
    public static Transform3 Identity => new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
    public static Transform3 Translation(Vec3 v) => new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, v.X, v.Y, v.Z, 1]);
    public static Transform3 Scale(Vec3 v) => new([v.X, 0, 0, 0, 0, v.Y, 0, 0, 0, 0, v.Z, 0, 0, 0, 0, 1]);
    public static Transform3 Rotation(Vec3 axis, double angle)
    {
        var n = axis.Normalized(); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1 - c; var x = n.X; var y = n.Y; var z = n.Z;
        return new([t*x*x+c,t*x*y+s*z,t*x*z-s*y,0,t*x*y-s*z,t*y*y+c,t*y*z+s*x,0,t*x*z+s*y,t*y*z-s*x,t*z*z+c,0,0,0,0,1]);
    }
    public HPoint Apply(HPoint p)
    {
        var m = _values;
        return new(m[0]*p.X+m[4]*p.Y+m[8]*p.Z+m[12]*p.W,m[1]*p.X+m[5]*p.Y+m[9]*p.Z+m[13]*p.W,m[2]*p.X+m[6]*p.Y+m[10]*p.Z+m[14]*p.W,m[3]*p.X+m[7]*p.Y+m[11]*p.Z+m[15]*p.W);
    }
    public Vec3 Apply(Vec3 p) => Apply(HPoint.FromPoint(p)).Position;
    public static Transform3 operator *(Transform3 a, Transform3 b)
    {
        var output = new double[16];
        for (var j=0;j<4;j++) for(var i=0;i<4;i++) for(var k=0;k<4;k++) output[j*4+i]+=a._values[k*4+i]*b._values[j*4+k];
        return new(output);
    }
}
