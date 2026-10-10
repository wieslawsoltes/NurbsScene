namespace NurbsScene.Geometry;

public interface INurbsEvaluator
{
    string Implementation { get; }
    Vec3[] Evaluate(NurbsCurve curve, double parameter, int derivativeOrder = 0);
    Vec3[,] Evaluate(NurbsSurface surface, double u, double v, int orderU = 0, int orderV = 0);
}
public interface IGeometry
{
    Guid Id { get; }
    Bounds3 Bounds { get; }
    IGeometry Transform(Transform3 transform);
}
internal static class GeometryValidation
{
    public static void Knots(int degree, int count, ReadOnlySpan<double> knots)
    {
        KnotVector.Validate(degree, count, knots);
    }
}
/// <summary>Immutable exact positive-weight rational B-spline, with expanded knots.</summary>
public sealed class NurbsCurve : IGeometry
{
    private readonly double[] _knots; private readonly HPoint[] _points;
    public Guid Id { get; }=Guid.NewGuid();
    public INurbsEvaluator Evaluator { get; }
    public int Degree { get; }
    public int Count => _points.Length;
    public ReadOnlySpan<double> Knots => _knots;
    public ReadOnlySpan<HPoint> ControlPoints => _points;
    public Interval Domain => new(_knots[Degree],_knots[Count]);
    public Bounds3 Bounds => _points.Aggregate(Bounds3.Empty,(bounds,p)=>bounds.Include(p.Position));
    public NurbsCurve(INurbsEvaluator evaluator,int degree,IEnumerable<double> knots,IEnumerable<HPoint> points)
    {
        Evaluator=evaluator??throw new ArgumentNullException(nameof(evaluator));Degree=degree;_knots=knots.ToArray();_points=points.ToArray();
        GeometryValidation.Knots(degree,Count,_knots);
        if(_points.Any(p=>!p.IsValid))throw new ArgumentException("NURBS controls must be finite and have positive weights.");
    }
    public Vec3 PointAt(double t)=>Evaluator.Evaluate(this,t)[0];
    public Vec3[] Derivatives(double t,int order=2)=>Evaluator.Evaluate(this,t,order);
    public Vec3 TangentAt(double t)=>Derivatives(t,1)[1].Normalized();
    public double CurvatureAt(double t){var d=Derivatives(t,2);var length=d[1].Length;if(length==0)throw new ArithmeticException("Singular tangent.");return Vec3.Cross(d[1],d[2]).Length/(length*length*length);}
    public NurbsCurve Transform(Transform3 transform)=>new(Evaluator,Degree,_knots,_points.Select(transform.Apply));
    IGeometry IGeometry.Transform(Transform3 transform)=>Transform(transform);
    public NurbsCurve Reverse()=>new(Evaluator,Degree,KnotVector.Reverse(Degree,Count,_knots),_points.Reverse());
    public NurbsCurve Reparameterize(Interval domain)
        =>new(Evaluator,Degree,KnotVector.Reparameterize(Degree,Count,_knots,domain),_points);

}
/// <summary>Immutable tensor-product NURBS surface. V varies fastest in the control net. No implicit trimming.</summary>
public sealed class NurbsSurface : IGeometry
{
    private readonly double[] _u,_v;private readonly HPoint[] _points;
    public Guid Id { get; }=Guid.NewGuid();
    public INurbsEvaluator Evaluator { get; }
    public int DegreeU { get; } public int DegreeV { get; } public int CountU { get; } public int CountV { get; }
    public ReadOnlySpan<double> KnotsU=>_u;public ReadOnlySpan<double> KnotsV=>_v;public ReadOnlySpan<HPoint> ControlPoints=>_points;
    public Interval DomainU=>new(_u[DegreeU],_u[CountU]);public Interval DomainV=>new(_v[DegreeV],_v[CountV]);
    public Bounds3 Bounds=>_points.Aggregate(Bounds3.Empty,(bounds,p)=>bounds.Include(p.Position));
    public NurbsSurface(INurbsEvaluator evaluator,int degreeU,int degreeV,int countU,int countV,IEnumerable<double> knotsU,IEnumerable<double> knotsV,IEnumerable<HPoint> points)
    {
        Evaluator=evaluator??throw new ArgumentNullException(nameof(evaluator));DegreeU=degreeU;DegreeV=degreeV;CountU=countU;CountV=countV;_u=knotsU.ToArray();_v=knotsV.ToArray();_points=points.ToArray();
        GeometryValidation.Knots(degreeU,countU,_u);GeometryValidation.Knots(degreeV,countV,_v);
        if((long)countU*countV!=_points.Length||_points.Length>1_000_000||_points.Any(p=>!p.IsValid))throw new ArgumentException("Invalid NURBS surface control net.");
    }
    public Vec3 PointAt(double u,double v)=>Evaluator.Evaluate(this,u,v)[0,0];
    public Vec3[,] Derivatives(double u,double v,int orderU=2,int orderV=2)=>Evaluator.Evaluate(this,u,v,orderU,orderV);
    public Vec3 NormalAt(double u,double v){var d=Derivatives(u,v,1,1);return Vec3.Cross(d[1,0],d[0,1]).Normalized();}
    public (double Gaussian,double Mean) CurvatureAt(double u,double v)
    {
        var d=Derivatives(u,v);var n=Vec3.Cross(d[1,0],d[0,1]).Normalized();var e=Vec3.Dot(d[1,0],d[1,0]);var f=Vec3.Dot(d[1,0],d[0,1]);var g=Vec3.Dot(d[0,1],d[0,1]);var l=Vec3.Dot(n,d[2,0]);var m=Vec3.Dot(n,d[1,1]);var nn=Vec3.Dot(n,d[0,2]);var den=e*g-f*f;if(den<=0)throw new ArithmeticException("Singular surface metric.");return((l*nn-m*m)/den,(e*nn-2*f*m+g*l)/(2*den));
    }
    public NurbsSurface Transform(Transform3 transform)=>new(Evaluator,DegreeU,DegreeV,CountU,CountV,_u,_v,_points.Select(transform.Apply));
    IGeometry IGeometry.Transform(Transform3 transform)=>Transform(transform);
    public NurbsSurface Transpose(){var p=new HPoint[_points.Length];for(var i=0;i<CountU;i++)for(var j=0;j<CountV;j++)p[j*CountU+i]=_points[i*CountV+j];return new(Evaluator,DegreeV,DegreeU,CountV,CountU,_v,_u,p);}
}
