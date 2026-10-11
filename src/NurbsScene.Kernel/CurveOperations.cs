using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public sealed record BezierSegment(int Degree,Interval Domain,HPoint[] Controls);
public sealed record CurveTessellation(Vec3[] Positions,double[] Parameters,double MaximumChordalBound,bool ToleranceMet);

public static class CurveOperations
{
    public static NurbsCurve InsertKnot(this NurbsCurve curve,double t,int times=1)
    {
        if(times is <0 or >128)throw new ArgumentOutOfRangeException(nameof(times));
        for(var step=0;step<times;step++)
        {
            if(t<=curve.Domain.Min||t>=curve.Domain.Max)throw new ArgumentOutOfRangeException(nameof(t),"Knot insertion is restricted to the active domain interior.");
            var p=curve.Degree;var u=curve.Knots.ToArray();var cp=curve.ControlPoints.ToArray();var n=curve.Count;var k=ManagedNurbsEvaluator.FindSpan(p,n,u,t);var multiplicity=u.Count(value=>value==t);
            if(multiplicity>=p)throw new ArgumentException("Interior knot multiplicity would exceed degree.");
            var result=new HPoint[n+1];var knots=new double[u.Length+1];Array.Copy(u,knots,k+1);knots[k+1]=t;Array.Copy(u,k+1,knots,k+2,u.Length-k-1);
            for(var i=0;i<=k-p;i++)result[i]=cp[i];for(var i=k-multiplicity;i<n;i++)result[i+1]=cp[i];for(var i=k-p+1;i<=k-multiplicity;i++){var denominator=u[i+p]-u[i];var alpha=denominator==0?0:(t-u[i])/denominator;result[i]=HPoint.Lerp(cp[i-1],cp[i],alpha);}
            curve=new(curve.Evaluator,p,knots,result);
        }
        return curve;
    }
    public static (NurbsCurve Left,NurbsCurve Right) Split(this NurbsCurve curve,double t)
    {
        if(!double.IsFinite(t)||t<=curve.Domain.Min||t>=curve.Domain.Max)throw new ArgumentOutOfRangeException(nameof(t));
        var multiplicity=curve.Knots.ToArray().Count(u=>u==t);if(multiplicity>curve.Degree)throw new NotSupportedException("Splitting a disconnected full-multiplicity curve is not supported by this operation.");
        curve=curve.InsertKnot(t,curve.Degree-multiplicity);var knots=curve.Knots.ToArray();var points=curve.ControlPoints.ToArray();var last=Array.LastIndexOf(knots,t);var shared=last-curve.Degree;
        return(new(curve.Evaluator,curve.Degree,knots.Take(last+1).Append(t),points.Take(shared+1)),new(curve.Evaluator,curve.Degree,new[]{t}.Concat(knots.Skip(last-curve.Degree+1)),points.Skip(shared)));
    }
    public static NurbsCurve Trim(this NurbsCurve curve,Interval interval)
    {
        if(interval.Min<curve.Domain.Min||interval.Max>curve.Domain.Max||!(interval.Min<interval.Max))throw new ArgumentException("Invalid subcurve interval.");if(interval.Min>curve.Domain.Min)curve=curve.Split(interval.Min).Right;if(interval.Max<curve.Domain.Max)curve=curve.Split(interval.Max).Left;return curve;
    }
    public static BezierSegment[] BezierSegments(this NurbsCurve curve)
    {
        var p=curve.Degree;var a=curve.Domain.Min;var b=curve.Domain.Max;var knots=curve.Knots.ToArray();if(knots.Take(p+1).Any(t=>t!=a)||knots.TakeLast(p+1).Any(t=>t!=b))throw new NotSupportedException("Bezier decomposition requires clamped endpoints.");
        var boundaries=knots.Distinct().Where(t=>t>a&&t<b).ToArray();foreach(var t in boundaries){var count=curve.Knots.ToArray().Count(u=>u==t);if(count>p)throw new NotSupportedException("Disconnected NURBS decomposition is not supported.");curve=curve.InsertKnot(t,p-count);}
        var all=new[]{a}.Concat(boundaries).Append(b).ToArray();var points=curve.ControlPoints.ToArray();return Enumerable.Range(0,all.Length-1).Select(i=>new BezierSegment(p,new(all[i],all[i+1]),points.Skip(i*p).Take(p+1).ToArray())).ToArray();
    }
    public static NurbsCurve ElevateDegree(this NurbsCurve curve,int times=1)
    {
        if(times<0||curve.Degree+times>128)throw new ArgumentOutOfRangeException(nameof(times));
        for(var step=0;step<times;step++)
        {
            var segments=curve.BezierSegments();var degree=curve.Degree+1;var knots=Enumerable.Repeat(segments[0].Domain.Min,degree+1).ToList();var points=new List<HPoint>();
            for(var s=0;s<segments.Length;s++){var input=segments[s].Controls;var elevated=new HPoint[degree+1];elevated[0]=input[0];elevated[^1]=input[^1];for(var i=1;i<degree;i++)elevated[i]=HPoint.Lerp(input[i],input[i-1],i/(double)degree);points.AddRange(s==0?elevated:elevated.Skip(1));if(s<segments.Length-1)knots.AddRange(Enumerable.Repeat(segments[s].Domain.Max,degree));}
            knots.AddRange(Enumerable.Repeat(segments[^1].Domain.Max,degree+1));curve=new(curve.Evaluator,degree,knots,points);
        }
        return curve;
    }
    public static NurbsCurve[] MakeCompatible(IReadOnlyList<NurbsCurve> input)
    {
        if(input.Count==0)throw new ArgumentException("At least one curve is required.");var degree=input.Max(c=>c.Degree);var curves=input.Select(c=>c.Reparameterize(new(0,1)).ElevateDegree(degree-c.Degree)).ToArray();var required=new SortedDictionary<double,int>();foreach(var c in curves)foreach(var group in c.Knots.ToArray().Where(t=>t>0&&t<1).GroupBy(t=>t))required[group.Key]=Math.Max(required.GetValueOrDefault(group.Key),group.Count());
        for(var i=0;i<curves.Length;i++)foreach(var(t,count)in required){var current=curves[i].Knots.ToArray().Count(u=>u==t);curves[i]=curves[i].InsertKnot(t,count-current);}return curves;
    }
    public static CurveTessellation Tessellate(this NurbsCurve curve,double tolerance=.02,int maxDepth=24,int maxPoints=200000)
    {
        if(!(tolerance>0)||!double.IsFinite(tolerance)||maxDepth is <1 or >40||maxPoints<2)throw new ArgumentException("Invalid tessellation budget.");
        var output=new List<Vec3>();var parameters=new List<double>();double maximum=0;var met=true;
        static double Distance(Vec3 p,Vec3 a,Vec3 b){var d=b-a;var length=Vec3.Dot(d,d);var t=length==0?0:Math.Clamp(Vec3.Dot(p-a,d)/length,0,1);return(p-a-d*t).Length;}
        void Visit(HPoint[] points,double a,double b,int depth)
        {
            var first=points[0].Position;var last=points[^1].Position;var bound=points.Max(p=>Distance(p.Position,first,last));
            if(bound<=tolerance||depth>=maxDepth){maximum=Math.Max(maximum,bound);met&=bound<=tolerance;output.Add(last);parameters.Add(b);if(output.Count>maxPoints)throw new InvalidOperationException("Curve display point budget exceeded.");return;}
            var scratch=(HPoint[])points.Clone();var left=new HPoint[points.Length];var right=new HPoint[points.Length];left[0]=scratch[0];right[^1]=scratch[^1];for(var level=1;level<points.Length;level++){for(var i=0;i<points.Length-level;i++)scratch[i]=HPoint.Lerp(scratch[i],scratch[i+1],.5);left[level]=scratch[0];right[points.Length-level-1]=scratch[points.Length-level-1];}var middle=(a+b)/2;Visit(left,a,middle,depth+1);Visit(right,middle,b,depth+1);
        }
        foreach(var segment in curve.BezierSegments()){if(output.Count==0){output.Add(segment.Controls[0].Position);parameters.Add(segment.Domain.Min);}Visit(segment.Controls,segment.Domain.Min,segment.Domain.Max,0);}return new(output.ToArray(),parameters.ToArray(),maximum,met);
    }
}
