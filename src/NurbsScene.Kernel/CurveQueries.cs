using System.Runtime.CompilerServices;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public sealed record CurveQueryOptions(double DistanceTolerance=1e-7,int MaximumSubdivisions=100_000,int MaximumDepth=48)
{
    internal void Validate()
    {
        if(!double.IsFinite(DistanceTolerance)||DistanceTolerance<=0||MaximumSubdivisions is <0 or >1_000_000||MaximumDepth is <1 or >60)
            throw new ArgumentException("Invalid closest-point tolerance or subdivision budget.");
    }
}
/// <summary>DistanceGap is the remaining numerical upper/lower distance gap, not
/// a parameter error or an interval-arithmetic certificate. Never ignore Converged.</summary>
public sealed record CurveClosestPointResult(double Parameter,Vec3 Point,double Distance,double LowerDistance,
    bool Converged,int Subdivisions,int Evaluations)
{
    public double DistanceGap=>Math.Max(0,Distance-LowerDistance);
}

/// <summary>Reusable immutable rational Bezier decomposition for globally bounded
/// closest-point searches. Only clamped continuous positive-weight curves are
/// accepted by the decomposition. Each query owns its priority queue and scratch.</summary>
public sealed class CurveQueryIndex
{
    private readonly NurbsCurve _curve;
    private readonly BezierSegment[] _segments;
    public Guid GeometryId=>_curve.Id;
    public int SegmentCount=>_segments.Length;
    internal CurveQueryIndex(NurbsCurve curve)
    {
        _curve=curve;
        var spans=0;var knots=curve.Knots;
        for(var i=curve.Degree;i<curve.Count;i++)if(knots[i+1]>knots[i])spans++;
        if(spans>100_000||(long)spans*(curve.Degree+1)>1_000_000)
            throw new NotSupportedException("Curve query decomposition exceeds the control budget.");
        _segments=curve.BezierSegments();
        if(_segments.Length>100_000)throw new NotSupportedException("Curve query index exceeds the segment budget.");
    }
    private sealed record Candidate(HPoint[] Controls,Interval Domain,int Depth);
    public CurveClosestPointResult ClosestPoint(Vec3 point,CurveQueryOptions? options=null,CancellationToken cancellationToken=default)
    {
        if(!point.IsFinite)throw new ArgumentException("A finite query point is required.",nameof(point));
        var settings=options??new();settings.Validate();cancellationToken.ThrowIfCancellationRequested();
        var best=double.PositiveInfinity;var parameter=_curve.Domain.Min;var position=Vec3.Zero;
        var evaluations=0;var subdivisions=0;var retired=double.PositiveInfinity;
        var queue=new PriorityQueue<Candidate,double>();
        void Consider(double t)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p=_curve.PointAt(t);evaluations++;var distance=(p-point).Length;
            if(!double.IsFinite(distance))throw new ArithmeticException("Closest-point distance exceeds binary64 range.");
            if(distance<best||distance==best&&t<parameter){best=distance;parameter=t;position=p;}
        }
        void Enqueue(Candidate candidate)
        {
            var lower=LowerBound(candidate.Controls,point);
            if(lower>=best-settings.DistanceTolerance)retired=Math.Min(retired,lower);
            else queue.Enqueue(candidate,lower);
        }
        foreach(var segment in _segments)
        {
            Consider(segment.Domain.Min);Consider(segment.Domain.Max);
            var t=segment.Domain.Min/2+segment.Domain.Max/2;Consider(t);
            // Local Newton steps improve the upper distance estimate only. They
            // never replace the global hull-subdivision test or certify a minimum.
            for(var iteration=0;iteration<8;iteration++)
            {
                var next=NewtonStep(t,point);evaluations++;
                if(!double.IsFinite(next)||next<=segment.Domain.Min||next>=segment.Domain.Max||next==t)break;
                t=next;Consider(t);
            }
            Enqueue(new(segment.Controls,segment.Domain,0));
        }
        while(queue.TryPeek(out var candidate,out var lower))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(best-Math.Min(retired,lower)<=settings.DistanceTolerance)break;
            if(subdivisions>=settings.MaximumSubdivisions)break;
            queue.Dequeue();
            var mid=candidate.Domain.Min/2+candidate.Domain.Max/2;
            if(candidate.Depth>=settings.MaximumDepth||!(mid>candidate.Domain.Min&&mid<candidate.Domain.Max))
            {retired=Math.Min(retired,lower);continue;}
            var (left,right)=Bisect(candidate.Controls);subdivisions++;Consider(mid);
            Enqueue(new(left,new(candidate.Domain.Min,mid),candidate.Depth+1));
            Enqueue(new(right,new(mid,candidate.Domain.Max),candidate.Depth+1));
        }
        var remaining=queue.TryPeek(out _,out var nextBound)?nextBound:double.PositiveInfinity;
        var minimum=Math.Min(best,Math.Min(retired,remaining));
        return new(parameter,position,best,minimum,best-minimum<=settings.DistanceTolerance,subdivisions,evaluations);
    }
    private double NewtonStep(double t,Vec3 point)
    {
        Span<Vec3> d=stackalloc Vec3[3];
        if(_curve.Evaluator is INurbsSpanEvaluator fast)fast.EvaluateInto(_curve,t,2,d);
        else _curve.Derivatives(t,2).AsSpan().CopyTo(d);
        var r=d[0]-point;var denominator=Vec3.Dot(d[1],d[1])+Vec3.Dot(r,d[2]);
        return denominator==0?double.NaN:t-Vec3.Dot(r,d[1])/denominator;
    }
    private static double SegmentDistance(Vec3 p,Vec3 a,Vec3 b)
    {
        var delta=b-a;var length=delta.Length;if(length==0)return(p-a).Length;
        var direction=delta/length;var t=Math.Clamp(Vec3.Dot(p-a,direction),0,length);
        return(p-a-direction*t).Length;
    }
    private static double LowerBound(HPoint[] controls,Vec3 point)
    {
        var box=Bounds3.Empty;var first=controls[0].Position;var last=controls[^1].Position;double deviation=0,scale=point.Length;
        foreach(var control in controls)
        {
            var p=control.Position;box=box.Include(p);deviation=Math.Max(deviation,SegmentDistance(p,first,last));scale=Math.Max(scale,p.Length);
        }
        var nearest=new Vec3(Math.Clamp(point.X,box.Min.X,box.Max.X),Math.Clamp(point.Y,box.Min.Y,box.Max.Y),Math.Clamp(point.Z,box.Min.Z,box.Max.Z));
        // Positive-weight rational Beziers lie inside their Cartesian control hull.
        // Both the box and the chord capsule contain that hull. Use the tighter
        // distance lower estimate, padded for rounding (not certified intervals).
        var lower=Math.Max((point-nearest).Length,SegmentDistance(point,first,last)-deviation);
        var guard=64*2.2204460492503131e-16*Math.Max(1,scale);
        if(!double.IsFinite(lower)||!double.IsFinite(guard))throw new ArithmeticException("Closest-point bounds exceed binary64 range.");
        return Math.Max(0,lower-guard);
    }
    private static (HPoint[] Left,HPoint[] Right) Bisect(HPoint[] points)
    {
        // Degree <= 128: at most 4128 bytes, with no recursion and no stackalloc in a loop.
        Span<HPoint> scratch=stackalloc HPoint[points.Length];points.AsSpan().CopyTo(scratch);
        var left=new HPoint[points.Length];var right=new HPoint[points.Length];left[0]=scratch[0];right[^1]=scratch[^1];
        for(var level=1;level<points.Length;level++)
        {
            for(var i=0;i<points.Length-level;i++)scratch[i]=HPoint.Lerp(scratch[i],scratch[i+1],.5);
            left[level]=scratch[0];right[points.Length-level-1]=scratch[points.Length-level-1];
        }
        return(left,right);
    }
}
public static class CurveQueries
{
    // Ephemeron cache: an index may reference its curve without keeping otherwise
    // unreachable documents alive. New immutable geometry gets a distinct entry.
    private static readonly ConditionalWeakTable<NurbsCurve,CurveQueryIndex> Indices=new();
    public static CurveQueryIndex CreateQueryIndex(this NurbsCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);return Indices.GetValue(curve,c=>new(c));
    }
    public static CurveClosestPointResult ClosestPoint(this NurbsCurve curve,Vec3 point,
        CurveQueryOptions? options=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if(!point.IsFinite)throw new ArgumentException("A finite query point is required.",nameof(point));
        (options??new()).Validate();cancellationToken.ThrowIfCancellationRequested();
        return curve.CreateQueryIndex().ClosestPoint(point,options,cancellationToken);
    }
    /// <summary>Edit a Cartesian control and optional positive weight, preserving
    /// all other exact controls, the degree and the active parameter domain.</summary>
    public static NurbsCurve WithControlPoint(this NurbsCurve curve,int index,Vec3 point,double? weight=null)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if(index<0||index>=curve.Count)throw new ArgumentOutOfRangeException(nameof(index));
        var controls=curve.ControlPoints.ToArray();var value=HPoint.FromPoint(point,weight??controls[index].W);
        if(!value.IsValid)throw new ArgumentException("A finite Cartesian control with a positive finite weight is required.");
        controls[index]=value;return new(curve.Evaluator,curve.Degree,curve.Knots.ToArray(),controls);
    }
}
