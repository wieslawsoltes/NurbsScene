using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

public sealed class GeometryFactory(INurbsEvaluator evaluator)
{
    public INurbsEvaluator Evaluator { get; }=evaluator;
    public NurbsCurve Line(Vec3 a,Vec3 b)=>new(Evaluator,1,[0,0,1,1],[HPoint.FromPoint(a),HPoint.FromPoint(b)]);
    public NurbsCurve Polyline(IEnumerable<Vec3> input)
    {
        var points=input.ToArray();if(points.Length<2)throw new ArgumentException("Polyline needs at least two points.");
        return new(Evaluator,1,new[]{0d}.Concat(Enumerable.Range(0,points.Length).Select(i=>(double)i)).Append(points.Length-1),points.Select(p=>HPoint.FromPoint(p)));
    }
    public NurbsCurve Arc(Vec3 center,double radius,double start=0,double sweep=2*Math.PI,Vec3? axisX=null,Vec3? axisY=null)
    {
        if(!(radius>0)||!double.IsFinite(radius)||!double.IsFinite(start)||!double.IsFinite(sweep)||Math.Abs(sweep)<1e-14||Math.Abs(sweep)>2*Math.PI+1e-12)throw new ArgumentException("Arc requires positive radius and nonzero sweep of at most one turn.");
        var x=(axisX??Vec3.UnitX).Normalized();var y=(axisY??Vec3.UnitY).Normalized();if(Math.Abs(Vec3.Dot(x,y))>1e-10)throw new ArgumentException("Arc axes must be orthogonal.");
        var count=(int)Math.Ceiling(Math.Abs(sweep)/(Math.PI/2));var points=new List<HPoint>();var knots=new List<double>{0,0,0};
        HPoint Point(double a,double divisor=1,double weight=1)=>HPoint.FromPoint(center+(x*Math.Cos(a)+y*Math.Sin(a))*(radius/divisor),weight);
        points.Add(Point(start));for(var i=0;i<count;i++){var a=start+sweep*i/count;var b=start+sweep*(i+1)/count;var w=Math.Cos((b-a)/2);points.Add(Point((a+b)/2,w,w));points.Add(Point(b));if(i<count-1){knots.Add((i+1d)/count);knots.Add((i+1d)/count);}}
        knots.AddRange([1,1,1]);return new(Evaluator,2,knots,points);
    }
    public NurbsCurve Circle(Vec3 center,double radius)=>Arc(center,radius);
    public NurbsSurface Extrude(NurbsCurve curve,Vec3 direction)
    {
        if(!direction.IsFinite||direction.Length==0)throw new ArgumentException("Extrusion direction must be finite and nonzero.");
        var points=new HPoint[curve.Count*2];for(var i=0;i<curve.Count;i++){var p=curve.ControlPoints[i];points[i*2]=p;points[i*2+1]=HPoint.FromPoint(p.Position+direction,p.W);}
        return new(Evaluator,curve.Degree,1,curve.Count,2,curve.Knots.ToArray(),[0,0,1,1],points);
    }
    public NurbsSurface Revolve(NurbsCurve profile,Vec3 origin,Vec3 axis,double sweep=2*Math.PI)
    {
        axis=axis.Normalized();var arc=Arc(Vec3.Zero,1,0,sweep);var points=new HPoint[profile.Count*arc.Count];
        for(var i=0;i<profile.Count;i++){var p=profile.ControlPoints[i];var center=origin+axis*Vec3.Dot(p.Position-origin,axis);var radial=p.Position-center;var tangent=Vec3.Cross(axis,radial);for(var j=0;j<arc.Count;j++){var q=arc.ControlPoints[j];points[i*arc.Count+j]=HPoint.FromPoint(center+radial*q.Position.X+tangent*q.Position.Y,p.W*q.W);}}
        return new(Evaluator,profile.Degree,arc.Degree,profile.Count,arc.Count,profile.Knots.ToArray(),arc.Knots.ToArray(),points);
    }
    public NurbsSurface FromCorners(Vec3 a,Vec3 b,Vec3 c,Vec3 d)=>new(Evaluator,1,1,2,2,[0,0,1,1],[0,0,1,1],new[]{a,d,b,c}.Select(p=>HPoint.FromPoint(p)));
    public NurbsSurface Saddle(double width=60,double depth=42,double height=12)
    {
        if(!(width>0)||!(depth>0)||!double.IsFinite(height))throw new ArgumentException("Invalid surface dimensions.");
        var points=new HPoint[16];for(var i=0;i<4;i++)for(var j=0;j<4;j++)points[i*4+j]=HPoint.FromPoint(new((i/3d-.5)*width,(j/3d-.5)*depth,16+height*((i is 1 or 2?1.25:0)-(j is 1 or 2?.55:0))));
        return new(Evaluator,3,3,4,4,[0,0,0,0,1,1,1,1],[0,0,0,0,1,1,1,1],points);
    }
    public NurbsCurve IsoCurve(NurbsSurface surface,double parameter,bool fixU=true)
    {
        if(!fixU)return IsoCurve(surface.Transpose(),parameter,true);
        var(span,basis)=ManagedNurbsEvaluator.Basis(surface.DegreeU,surface.CountU,surface.KnotsU,parameter,0);var controls=new HPoint[surface.CountV];
        for(var j=0;j<surface.CountV;j++)for(var i=0;i<=surface.DegreeU;i++)controls[j]+=surface.ControlPoints[(span-surface.DegreeU+i)*surface.CountV+j]*basis[0,i];
        return new(Evaluator,surface.DegreeV,surface.KnotsV.ToArray(),controls);
    }
    public NurbsCurve Interpolate(IReadOnlyList<Vec3> points,int degree=3)
    {
        if(points.Count<2||points.Count>4096||degree<1||degree>=points.Count)throw new ArgumentException("Invalid interpolation dimensions.");
        var n=points.Count;var parameters=new double[n];for(var i=1;i<n;i++)parameters[i]=parameters[i-1]+(points[i]-points[i-1]).Length;if(parameters[^1]==0)throw new ArgumentException("Coincident interpolation points.");for(var i=1;i<n;i++)parameters[i]/=parameters[^1];for(var i=1;i<n;i++)if(parameters[i]<=parameters[i-1])throw new ArgumentException("Consecutive interpolation points must be distinct.");
        var knots=new double[n+degree+1];for(var j=1;j<n-degree;j++){double sum=0;for(var i=j;i<j+degree;i++)sum+=parameters[i];knots[j+degree]=sum/degree;}Array.Fill(knots,1,n,degree+1);
        var matrix=new double[n,n];var rhs=new double[n,3];for(var i=0;i<n;i++){var(span,basis)=ManagedNurbsEvaluator.Basis(degree,n,knots,parameters[i],0);for(var j=0;j<=degree;j++)matrix[i,span-degree+j]=basis[0,j];rhs[i,0]=points[i].X;rhs[i,1]=points[i].Y;rhs[i,2]=points[i].Z;}LinearSolver.Solve(matrix,rhs);
        return new(Evaluator,degree,knots,Enumerable.Range(0,n).Select(i=>new HPoint(rhs[i,0],rhs[i,1],rhs[i,2],1)));
    }
    public NurbsSurface Loft(IReadOnlyList<NurbsCurve> curves,int degreeV=3)
    {
        if(curves.Count<2||degreeV<1)throw new ArgumentException("Loft requires at least two curves.");var compatible=CurveOperations.MakeCompatible(curves);var count=compatible.Length;degreeV=Math.Min(degreeV,count-1);var times=Enumerable.Range(0,count).Select(i=>i/(count-1d)).ToArray();var knots=new double[count+degreeV+1];for(var j=1;j<count-degreeV;j++){double sum=0;for(var i=j;i<j+degreeV;i++)sum+=times[i];knots[j+degreeV]=sum/degreeV;}Array.Fill(knots,1,count,degreeV+1);var matrix=new double[count,count];for(var i=0;i<count;i++){var(span,b)=ManagedNurbsEvaluator.Basis(degreeV,count,knots,times[i],0);for(var j=0;j<=degreeV;j++)matrix[i,span-degreeV+j]=b[0,j];}
        var points=new HPoint[compatible[0].Count*count];for(var u=0;u<compatible[0].Count;u++){var rhs=new double[count,4];for(var j=0;j<count;j++){var p=compatible[j].ControlPoints[u];rhs[j,0]=p.X;rhs[j,1]=p.Y;rhs[j,2]=p.Z;rhs[j,3]=p.W;}LinearSolver.Solve((double[,])matrix.Clone(),rhs);for(var j=0;j<count;j++)points[u*count+j]=new(rhs[j,0],rhs[j,1],rhs[j,2],rhs[j,3]);}
        return new(Evaluator,compatible[0].Degree,degreeV,compatible[0].Count,count,compatible[0].Knots.ToArray(),knots,points);
    }
}
internal static class LinearSolver
{
    public static void Solve(double[,] a,double[,] b)
    {
        var n=a.GetLength(0);var m=b.GetLength(1);var scales=new double[n];for(var i=0;i<n;i++)for(var j=0;j<n;j++)scales[i]=Math.Max(scales[i],Math.Abs(a[i,j]));
        for(var k=0;k<n;k++){var pivot=k;double best=-1;for(var i=k;i<n;i++){var score=scales[i]==0?0:Math.Abs(a[i,k])/scales[i];if(score>best){best=score;pivot=i;}}if(best<=1e-14)throw new ArithmeticException("Singular interpolation system.");if(pivot!=k){for(var j=0;j<n;j++)(a[pivot,j],a[k,j])=(a[k,j],a[pivot,j]);for(var j=0;j<m;j++)(b[pivot,j],b[k,j])=(b[k,j],b[pivot,j]);(scales[pivot],scales[k])=(scales[k],scales[pivot]);}for(var i=k+1;i<n;i++){var factor=a[i,k]/a[k,k];for(var j=k+1;j<n;j++)a[i,j]-=factor*a[k,j];for(var j=0;j<m;j++)b[i,j]-=factor*b[k,j];a[i,k]=0;}}
        for(var i=n-1;i>=0;i--)for(var j=0;j<m;j++){for(var k=i+1;k<n;k++)b[i,j]-=a[i,k]*b[k,j];b[i,j]/=a[i,i];if(!double.IsFinite(b[i,j]))throw new ArithmeticException("Non-finite interpolation result.");}
    }
}
