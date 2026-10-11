using System.Collections.Immutable;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public sealed record CurvePlaneOptions(double DistanceTolerance=1e-7,
    double ParameterTolerance=1e-11, int MaximumEvaluations=200_000)
{
    internal void Validate()
    {
        if(!double.IsFinite(DistanceTolerance)||DistanceTolerance<=0
            ||!double.IsFinite(ParameterTolerance)||ParameterTolerance<=0||ParameterTolerance>=1
            ||MaximumEvaluations is <1 or >2_000_000)
            throw new ArgumentException("Invalid curve-plane tolerance or evaluation budget.");
    }
}
public sealed record CurvePlanePoint(double Parameter,Vec3 Point,double Residual);
/// <summary>Numerical intersections of authoritative NURBS, not their mesh.
/// ParameterTolerance is relative to each Bezier span. Overlaps mean the whole
/// span's control hull is within DistanceTolerance. Unresolved intervals must
/// never be interpreted as having no intersections. No interval certificate.</summary>
public sealed record CurvePlaneResult(ImmutableArray<CurvePlanePoint> Points,
    ImmutableArray<Interval> Overlaps,ImmutableArray<Interval> Unresolved,int Evaluations)
{
    public bool Complete=>Unresolved.IsEmpty;
}

public static class CurveSections
{
    /// <summary>Plane roots of homogeneous Bernstein numerators. Derivative-root
    /// partitioning finds monotone intervals and also examines tangential roots.
    /// Supports the same clamped continuous positive-weight curves as extraction.</summary>
    public static CurvePlaneResult IntersectPlane(this NurbsCurve curve,Plane3 plane,
        CurvePlaneOptions? options=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(curve);ArgumentNullException.ThrowIfNull(plane);
        var settings=options??new();settings.Validate();cancellationToken.ThrowIfCancellationRequested();
        var points=new List<CurvePlanePoint>();var overlaps=new List<Interval>();var unresolved=new List<Interval>();
        var budget=new RootBudget(settings.MaximumEvaluations,cancellationToken);
        foreach(var span in BezierExtraction.Get(curve,cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(budget.Exhausted){unresolved.Add(span.Domain);continue;}
            var weights=span.Controls.Max(p=>p.W);
            var coefficients=new double[span.Controls.Length];
            var nearPlane=true;
            for(var i=0;i<coefficients.Length;i++)
            {
                var distance=plane.SignedDistance(span.Controls[i].Position);
                nearPlane&=Math.Abs(distance)<=settings.DistanceTolerance;
                var weight=span.Controls[i].W/weights;
                if(weight==0)throw new ArithmeticException("Relative rational weights are below binary64 range.");
                coefficients[i]=distance*weight;
                if(!double.IsFinite(coefficients[i]))throw new ArithmeticException("Curve-plane numerator overflow.");
            }
            if(nearPlane){overlaps.Add(span.Domain);continue;}
            try
            {
                budget.PrecisionLimited=false;
                var roots=Roots(coefficients,settings.ParameterTolerance,budget);
                if(budget.PrecisionLimited)unresolved.Add(span.Domain);
                foreach(var root in roots)
                {
                    var t=Map(span.Domain,root);
                    budget.Tick();var point=curve.PointAt(t);var residual=Math.Abs(plane.SignedDistance(point));
                    if(residual<=settings.DistanceTolerance)points.Add(new(t,point,residual));
                    else unresolved.Add(span.Domain); // Do not silently drop a poorly conditioned root.
                }
            }
            catch(RootBudgetExceededException){unresolved.Add(span.Domain);}
        }
        var joined=MergeIntervals(overlaps);
        // Deduplicate shared knot endpoints only. Different parameters at the same
        // world point (closed seam, self intersection) remain distinct events.
        var distinct=points.OrderBy(p=>p.Parameter).GroupBy(p=>p.Parameter).Select(g=>g.MinBy(p=>p.Residual)!)
            .Where(p=>!joined.Any(i=>i.Contains(p.Parameter))).ToImmutableArray();
        return new(distinct,joined,MergeIntervals(unresolved),budget.Evaluations);
    }

    private static ImmutableArray<Interval> MergeIntervals(IEnumerable<Interval> source)
    {
        var result=new List<Interval>();
        foreach(var interval in source.Distinct().OrderBy(i=>i.Min))
        {
            if(result.Count>0&&result[^1].Max>=interval.Min)result[^1]=new(result[^1].Min,Math.Max(result[^1].Max,interval.Max));
            else result.Add(interval);
        }
        return result.ToImmutableArray();
    }
    private static double Map(Interval domain,double fraction)
        =>fraction==0?domain.Min:fraction==1?domain.Max:domain.Min*(1-fraction)+domain.Max*fraction;
    private sealed class RootBudgetExceededException:Exception;
    private sealed class RootBudget(int maximum,CancellationToken cancellation)
    {
        public int Evaluations {get;private set;}
        public bool Exhausted=>Evaluations>=maximum;
        public bool PrecisionLimited {get;set;}
        public void Tick(){cancellation.ThrowIfCancellationRequested();if(Exhausted)throw new RootBudgetExceededException();Evaluations++;}
    }
    private static double Evaluate(ReadOnlySpan<double> coefficients,double t,RootBudget budget)
    {
        budget.Tick();Span<double> work=stackalloc double[coefficients.Length];coefficients.CopyTo(work);
        for(var n=work.Length-1;n>0;n--)for(var i=0;i<n;i++)work[i]=work[i]*(1-t)+work[i+1]*t;
        return work[0];
    }
    private static List<double> Roots(double[] coefficients,double tolerance,RootBudget budget)
    {
        budget.Tick();
        var scale=coefficients.Max(Math.Abs);
        if(scale==0||coefficients.Length==1)return [];
        var c=coefficients.Select(x=>x/scale).ToArray();
        var degree=c.Length-1;
        // Convex-hull sign exclusion is independent of sampling resolution.
        if(c.All(x=>x>0)||c.All(x=>x<0))return [];
        if(degree==1)
        {
            if(c[0]==c[1])return [];
            var root=c[0]/(c[0]-c[1]);return root>=0&&root<=1?[root]:[];
        }
        // A positive common derivative factor does not affect its roots.
        var derivative=new double[degree];for(var i=0;i<degree;i++)derivative[i]=c[i+1]-c[i];
        var critical=Roots(derivative,Math.Max(8*2.2204460492503131e-16,tolerance/2),budget).Where(t=>t>0&&t<1).Order().ToArray();
        var boundaries=new[]{0d}.Concat(critical).Append(1d).ToArray();
        var values=boundaries.Select(t=>Evaluate(c,t,budget)).ToArray();
        var output=new List<double>();
        // Repeated roots have no sign change. The numerical zero threshold only
        // proposes a candidate; world-distance validation above is mandatory.
        var rounding=32*2.2204460492503131e-16*c.Length;
        for(var i=0;i<boundaries.Length;i++)if(Math.Abs(values[i])<=rounding)
        {
            output.Add(boundaries[i]);
            if(values[i]!=0)budget.PrecisionLimited=true;
        }
        for(var i=1;i<boundaries.Length;i++)
        {
            var left=boundaries[i-1];var right=boundaries[i];var fa=values[i-1];var fb=values[i];
            if(fa==0||fb==0||Math.Sign(fa)==Math.Sign(fb))continue;
            // Near-zero extrema may represent a tangency or a numerically
            // unresolved cluster. Do not manufacture multiple neighboring roots.
            if(Math.Abs(fa)<=rounding||Math.Abs(fb)<=rounding)continue;
            for(var iteration=0;iteration<80&&right-left>tolerance;iteration++)
            {
                var mid=left/2+right/2;if(mid==left||mid==right)break;
                var fm=Evaluate(c,mid,budget);
                if(fm==0){left=right=mid;break;}
                if(Math.Sign(fa)==Math.Sign(fm)){left=mid;fa=fm;}else{right=mid;fb=fm;}
            }
            output.Add(left/2+right/2);
        }
        return output.Distinct().Order().ToList();
    }

    /// <summary>Sort and deduplicate interior parameters before editing. Balanced
    /// splitting limits repeated whole-curve copies to logarithmic partition depth.
    /// Geometry and the original parameter domain are retained in every piece.</summary>
    public static NurbsCurve[] SplitMany(this NurbsCurve curve,IEnumerable<double> parameters,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(curve);ArgumentNullException.ThrowIfNull(parameters);
        var cuts=new SortedSet<double>();var count=0;
        foreach(var t in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(++count>100_000)throw new ArgumentException("Split input enumeration exceeds the item budget.");
            if(!curve.Domain.Contains(t)||t==curve.Domain.Min||t==curve.Domain.Max)
                throw new ArgumentOutOfRangeException(nameof(parameters),"Split parameters must be inside the active domain.");
            cuts.Add(t);if(cuts.Count>4095)throw new ArgumentException("At most 4095 split parameters are supported.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var values=cuts.ToArray();var result=new NurbsCurve[values.Length+1];
        void Divide(NurbsCurve part,int begin,int end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(begin==end){result[begin]=part;return;}
            var mid=begin+(end-begin)/2;var halves=part.Split(values[mid]);
            Divide(halves.Left,begin,mid);Divide(halves.Right,mid+1,end);
        }
        Divide(curve,0,values.Length);return result;
    }
}
