using NurbsScene.Geometry;
using NurbsScene.Kernel;

internal static class KernelParityTests
{
    private static readonly INurbsEvaluator Evaluator=new ManagedNurbsEvaluator();
    private static void Check(bool result,string message="Assertion failed") { if(!result)throw new Exception(message); }
    private static void Near(double value,double expected,double tolerance=3e-9)
        =>Check(double.IsFinite(value)&&Math.Abs(value-expected)<=tolerance*Math.Max(1,Math.Abs(expected)), $"Expected {expected:G17}, got {value:G17}");
    private static void Point(Vec3 value,Vec3 expected,double tolerance=3e-9) {Near(value.X,expected.X,tolerance);Near(value.Y,expected.Y,tolerance);Near(value.Z,expected.Z,tolerance);}
    private static void Throws<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
    private static NurbsSurface Surface(INurbsEvaluator? evaluator=null)
    {
        var points=new List<HPoint>();
        for(var i=0;i<5;i++)for(var j=0;j<4;j++)
            points.Add(HPoint.FromPoint(new(i,j,.4*i*i-.3*j*j),.35+(i*7+j*3)%11/4d));
        return new(evaluator??Evaluator,3,2,5,4,[0,0,0,0,.4,1,1,1,1],[0,0,0,.65,1,1,1],points);
    }
    private static void Same(NurbsSurface expected,NurbsSurface actual)
    {
        foreach(var u in new[]{.03,.17,.29,.51,.79,.97})foreach(var v in new[]{.04,.19,.43,.59,.73,.96})
        {
            var a=expected.Derivatives(u,v,2,2);var b=actual.Derivatives(u,v,2,2);
            for(var i=0;i<3;i++)for(var j=0;j<3;j++)Point(b[i,j],a[i,j],2e-7);
        }
    }
    public static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("nonclamped repeated domain-end uses last nonempty span",()=>{
            var c=new NurbsCurve(Evaluator,2,[0,0,0,.5,1,1,2,2],Enumerable.Range(0,5).Select(i=>HPoint.FromPoint(new(i,i*i,0))));
            Check(ManagedNurbsEvaluator.FindSpan(c.Degree,c.Count,c.Knots,1)==3);
            Point(c.PointAt(1),new(3,9,0));Point(c.PointAt(1-1e-10),c.PointAt(1),1e-8);
            Check(c.Derivatives(1,4).All(p=>p.IsFinite));
        });
        yield return("unclamped curve reversal preserves the active domain and derivative signs",()=>{
            var c=new NurbsCurve(Evaluator,2,[-10,-2,0,.3,.7,1,2,5],Enumerable.Range(0,5).Select(i=>HPoint.FromPoint(new(i,i*i,2-i),1+i*.3)));
            var reversed=c.Reverse();Check(reversed.Domain==c.Domain);
            foreach(var t in new[]{0,.11,.25,.51,.81,1})
            {
                var a=c.Derivatives(1-t,2);var b=reversed.Derivatives(t,2);
                Point(b[0],a[0]);Point(b[1],-a[1]);Point(b[2],a[2]);Point(reversed.Reverse().PointAt(t),c.PointAt(t));
            }
        });
        yield return("large-domain reversal and reparameterization do not overflow midpoint",()=>{
            var c=new NurbsCurve(Evaluator,1,[1e308,1e308,1.5e308,1.5e308],[new(0,0,0,1),new(1,0,0,1)]);
            var r=c.Reverse();Check(r.Domain==c.Domain);Point(r.PointAt(1e308),new(1,0,0));
            var unit=c.Reparameterize(new(0,1));Point(unit.PointAt(.5),new(.5,0,0));
            Throws<ArgumentException>(()=>c.Reparameterize(new(double.NaN,1)));
        });
        yield return("public basis and span APIs validate dimensions, monotonicity and domain",()=>{
            Throws<ArgumentException>(()=>ManagedNurbsEvaluator.FindSpan(2,5,[],.5));
            Throws<ArgumentException>(()=>ManagedNurbsEvaluator.Basis(0,2,[0,0,1],.5,1));
            Throws<ArgumentException>(()=>ManagedNurbsEvaluator.FindSpan(1,3,[0,0,1,.5,1],.5));
            Throws<ArgumentOutOfRangeException>(()=>ManagedNurbsEvaluator.Basis(1,2,[0,0,1,1],2,1));
        });
        yield return("directional rational surface knot insertion preserves mixed derivatives",()=>{
            var s=Surface();var cp=s.ControlPoints.ToArray();
            var r=s.InsertKnot(SurfaceDirection.U,.2,2).InsertKnot(SurfaceDirection.V,.35);
            Same(s,r);Check(r.CountU==7&&r.CountV==5);Check(s.ControlPoints.SequenceEqual(cp));
            Check(ReferenceEquals(s,s.InsertKnot(SurfaceDirection.U,.2,0)));
        });
        yield return("surface refinement accepts an unordered multiset and rejects multiplicity atomically",()=>{
            var s=Surface();var r=s.RefineKnots(SurfaceDirection.U,[.8,.2,.2,.2,.6]);
            Same(s,r);Check(r.CountU==10);
            Throws<ArgumentException>(()=>s.RefineKnots(SurfaceDirection.U,[.2,.2,.2,.2]));
            Throws<ArgumentException>(()=>s.RefineKnots(SurfaceDirection.V,[.2,1]));
            Throws<ArgumentException>(()=>s.RefineKnots(SurfaceDirection.U,[double.NaN]));
            Throws<ArgumentOutOfRangeException>(()=>s.InsertKnot((SurfaceDirection)9,.2));Check(s.CountU==5&&s.CountV==4);
        });
        yield return("surface splitting in both directions keeps exact domains and seams",()=>{
            var s=Surface();foreach(var direction in new[]{SurfaceDirection.U,SurfaceDirection.V})
            {
                var(a,b)=s.Split(direction,.37);
                Check((direction==SurfaceDirection.U?a.DomainU:a.DomainV)==new Interval(0,.37));
                Check((direction==SurfaceDirection.U?b.DomainU:b.DomainV)==new Interval(.37,1));
                for(var i=0;i<=10;i++)for(var j=0;j<=10;j++)
                {
                    var u=i/10d;var v=j/10d;var t=direction==SurfaceDirection.U?u:v;
                    Point((t<=.37?a:b).PointAt(u,v),s.PointAt(u,v));
                    var x=direction==SurfaceDirection.U?.37:u;var y=direction==SurfaceDirection.V?.37:v;
                    Point(a.PointAt(x,y),b.PointAt(x,y));
                }
            }
        });
        yield return("rectangular surface subdomain trim preserves values, not B-rep trim loops",()=>{
            var s=Surface();var r=s.Trim(new(.15,.85),new(.12,.9));
            Check(r.DomainU==new Interval(.15,.85)&&r.DomainV==new Interval(.12,.9));
            for(var i=0;i<=8;i++)for(var j=0;j<=8;j++)
            {var u=r.DomainU.At(i/8d);var v=r.DomainV.At(j/8d);Point(r.PointAt(u,v),s.PointAt(u,v));}
            Throws<ArgumentException>(()=>s.Trim(new(.2,.9),new(.9,.2)));
            Throws<ArgumentException>(()=>s.Trim(new(-.1,.9),new(0,1)));
        });
        yield return("rational surface degree elevation preserves analytic geometry",()=>{
            var s=Surface();var r=s.ElevateDegree(SurfaceDirection.U,2).ElevateDegree(SurfaceDirection.V);
            Check(r.DegreeU==5&&r.DegreeV==3);Same(s,r);
            Throws<ArgumentOutOfRangeException>(()=>s.ElevateDegree(SurfaceDirection.U,126));
        });
        yield return("surface reversal changes parameter orientation and normal consistently",()=>{
            var s=Surface();foreach(var direction in new[]{SurfaceDirection.U,SurfaceDirection.V})
            {
                var r=s.Reverse(direction);Check(r.DomainU==s.DomainU&&r.DomainV==s.DomainV);
                foreach(var u in new[]{.1,.3,.6,.9})foreach(var v in new[]{.1,.4,.8})
                {var x=direction==SurfaceDirection.U?1-u:u;var y=direction==SurfaceDirection.V?1-v:v;Point(r.PointAt(u,v),s.PointAt(x,y));Point(r.NormalAt(u,v),-s.NormalAt(x,y));}
                Same(s,r.Reverse(direction));
            }
        });
        yield return("surface reparameterization scales all mixed derivative orders",()=>{
            var s=Surface();var r=s.Reparameterize(new(-2,7),new(4,10));
            foreach(var u in new[]{.17,.51,.89})foreach(var v in new[]{.2,.41,.82})
            {var a=s.Derivatives(u,v,2,2);var b=r.Derivatives(-2+u*9,4+v*6,2,2);for(var i=0;i<3;i++)for(var j=0;j<3;j++)Point(b[i,j],a[i,j]/(Math.Pow(9,i)*Math.Pow(6,j)),2e-7);}
        });
        yield return("surface control point editing is immutable and preserves unspecified weights",()=>{
            var s=Surface();var old=s.ControlPoints[0];var p=new Vec3(-2,7,3);var r=s.WithControlPoint(0,0,p);
            Point(r.PointAt(0,0),p);Check(r.ControlPoints[0].W==old.W&&s.ControlPoints[0]==old);
            Check(s.WithControlPoint(0,0,p,2.5).ControlPoints[0].W==2.5);
            Throws<ArgumentException>(()=>s.WithControlPoint(0,0,p,0));Throws<ArgumentOutOfRangeException>(()=>s.WithControlPoint(5,0,p));
        });
        yield return("surface boundary curves form an oriented parameter-space cycle",()=>{
            var s=Surface();var curves=s.BoundaryCurves();Check(curves.Length==4);
            for(var i=0;i<4;i++)Point(curves[i].PointAt(curves[i].Domain.Max),curves[(i+1)%4].PointAt(curves[(i+1)%4].Domain.Min));
            Point(curves[0].PointAt(.25),s.PointAt(.25,0));Point(curves[1].PointAt(.25),s.PointAt(1,.25));
            Point(curves[2].PointAt(.25),s.PointAt(.75,1));Point(curves[3].PointAt(.25),s.PointAt(0,.75));
        });
        yield return("Bezier patch extraction keeps rational weights and original parameter rectangles",()=>{
            var s=Surface();var patches=s.BezierPatches();Check(patches.Length==4);
            foreach(var patch in patches)
            {
                Check(patch.CountU==patch.DegreeU+1&&patch.CountV==patch.DegreeV+1);
                for(var i=0;i<=5;i++)for(var j=0;j<=5;j++)
                {var u=patch.DomainU.At(i/5d);var v=patch.DomainV.At(j/5d);Point(patch.PointAt(u,v),s.PointAt(u,v));}
                var x=patch.DomainU.At(.43);var y=patch.DomainV.At(.47);
                var a=s.Derivatives(x,y,2,2);var b=patch.Derivatives(x,y,2,2);
                for(var i=0;i<3;i++)for(var j=0;j<3;j++)Point(b[i,j],a[i,j],2e-7);
            }
            Throws<InvalidOperationException>(()=>s.BezierPatches(3));
        });
        yield return("unclamped Bezier decomposition is explicitly diagnosed",()=>{
            var s=new NurbsSurface(Evaluator,2,1,3,2,[-2,-1,0,1,2,3],[0,0,1,1],Enumerable.Range(0,6).Select(i=>HPoint.FromPoint(new(i/2,i%2,0))));
            Throws<NotSupportedException>(()=>s.BezierPatches());
        });
        if(Environment.GetEnvironmentVariable("NURBSSCENE_NATIVE_TESTS")=="1")
            yield return("managed and native evaluators agree after exact surface editing",()=>{
                var native=new NativeNurbsEvaluator();var a=Surface().InsertKnot(SurfaceDirection.U,.2,2).ElevateDegree(SurfaceDirection.V);
                var b=new NurbsSurface(native,a.DegreeU,a.DegreeV,a.CountU,a.CountV,a.KnotsU.ToArray(),a.KnotsV.ToArray(),a.ControlPoints.ToArray());Same(a,b);
                var c=new NurbsCurve(native,2,[0,0,0,.5,1,1,2,2],Enumerable.Range(0,5).Select(i=>HPoint.FromPoint(new(i,i*i,0))));
                Point(c.PointAt(1),new(3,9,0));
            });
    }
}
