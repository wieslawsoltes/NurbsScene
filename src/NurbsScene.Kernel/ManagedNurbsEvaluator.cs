using System.Buffers;
using System.Runtime.InteropServices;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>Binary64 rational evaluation with analytic basis derivatives. Small
/// scratch buffers live on the stack; high degree/order uses scoped pooled buffers.
/// No shared mutable numerical state, unsafe stack-size dependence or finite differences.</summary>
public sealed class ManagedNurbsEvaluator : INurbsSpanEvaluator, INurbsGridEvaluator
{
    public string Implementation => "Managed binary64 NURBS";
    public static int FindSpan(int degree, int count, ReadOnlySpan<double> knots, double t)
    {
        KnotVector.Validate(degree, count, knots);
        return FindSpanCore(degree, count, knots, t);
    }
    private static int FindSpanCore(int degree, int count, ReadOnlySpan<double> knots, double t)
    {
        if (!double.IsFinite(t) || t < knots[degree] || t > knots[count]) throw new ArgumentOutOfRangeException(nameof(t));
        if (t == knots[count])
        {
            var span = count - 1;
            while (span > degree && knots[span] >= knots[count]) span--;
            return span;
        }
        var low = degree; var high = count;
        while (low + 1 < high) { var mid = low + (high-low)/2; if (t < knots[mid]) high=mid; else low=mid; }
        return low;
    }
    internal static double Binomial(int n, int k)
    {
        if (k<0 || k>n) return 0;
        k=Math.Min(k,n-k); double c=1; for(var i=1;i<=k;i++) c=c*(n-i+1)/i; return c;
    }
    private static void Order(int order)
    {
        if (order is <0 or >32) throw new ArgumentOutOfRangeException(nameof(order));
    }
    private static double Divide(double a,double b) => b==0 ? 0 : a/b;

    /// <summary>Right-sided at interior knots; final nonempty span at the endpoint.</summary>
    public static (int Span,double[,] Derivatives) Basis(int p,int count,ReadOnlySpan<double> knots,double t,int order)
    {
        KnotVector.Validate(p,count,knots); Order(order);
        var output=new double[order+1,p+1];
        var span=BasisInto(p,count,knots,t,order,MemoryMarshal.CreateSpan(ref output[0,0],output.Length));
        return (span,output);
    }
    // Caller owns validated immutable knot vectors and output length.
    private static int BasisInto(int p,int count,ReadOnlySpan<double> knots,double t,int order,Span<double> ders)
    {
        var span=FindSpanCore(p,count,knots,t); var width=p+1; var n=Math.Min(p,order);
        var size=width*width+4*width;
        double[]? rented=null;
        Span<double> work=size<=128 ? stackalloc double[size] : (rented=ArrayPool<double>.Shared.Rent(size)).AsSpan(0,size);
        try
        {
            work.Clear(); ders.Clear();
            var ndu=work[..(width*width)]; var left=work.Slice(width*width,width);
            var right=work.Slice(width*width+width,width); var a=work.Slice(width*width+2*width,2*width);
            ndu[0]=1;
            for(var j=1;j<=p;j++)
            {
                left[j]=t-knots[span+1-j]; right[j]=knots[span+j]-t; double saved=0;
                for(var r=0;r<j;r++)
                {
                    ndu[j*width+r]=right[r+1]+left[j-r];
                    var temp=Divide(ndu[r*width+j-1],ndu[j*width+r]);
                    ndu[r*width+j]=saved+right[r+1]*temp; saved=left[j-r]*temp;
                }
                ndu[j*width+j]=saved;
            }
            for(var j=0;j<=p;j++) ders[j]=ndu[j*width+p];
            for(var r=0;r<=p;r++)
            {
                a.Clear(); var s1=0; var s2=width; a[0]=1;
                for(var k=1;k<=n;k++)
                {
                    double d=0; var rk=r-k; var pk=p-k;
                    if(r>=k) { a[s2]=Divide(a[s1],ndu[(pk+1)*width+rk]); d=a[s2]*ndu[rk*width+pk]; }
                    var j1=rk>=-1?1:-rk; var j2=r-1<=pk?k-1:p-r;
                    for(var j=j1;j<=j2;j++) { a[s2+j]=Divide(a[s1+j]-a[s1+j-1],ndu[(pk+1)*width+rk+j]); d+=a[s2+j]*ndu[(rk+j)*width+pk]; }
                    if(r<=pk) { a[s2+k]=-Divide(a[s1+k-1],ndu[(pk+1)*width+r]); d+=a[s2+k]*ndu[r*width+pk]; }
                    ders[k*width+r]=d; (s1,s2)=(s2,s1);
                }
            }
            double factor=p;
            for(var k=1;k<=n;k++) { for(var j=0;j<=p;j++) ders[k*width+j]*=factor; factor*=p-k; }
            return span;
        }
        finally { if(rented is not null) ArrayPool<double>.Shared.Return(rented); }
    }
    public Vec3[] Evaluate(NurbsCurve curve,double parameter,int derivativeOrder=0)
    {
        Order(derivativeOrder); var output=new Vec3[derivativeOrder+1];
        EvaluateInto(curve,parameter,derivativeOrder,output); return output;
    }
    public void EvaluateInto(NurbsCurve curve,double parameter,int order,Span<Vec3> derivatives)
    {
        ArgumentNullException.ThrowIfNull(curve); Order(order);
        if(derivatives.Length<order+1) throw new ArgumentException("Derivative buffer is too small.",nameof(derivatives));
        var p=curve.Degree; var size=(p+1)*(order+1); double[]? rented=null;
        Span<double> basis=size<=128 ? stackalloc double[size] : (rented=ArrayPool<double>.Shared.Rent(size)).AsSpan(0,size);
        // Curve orders are limited to 32: this buffer is at most 1056 bytes.
        Span<HPoint> h=stackalloc HPoint[order+1]; h.Clear();
        try
        {
            var span=BasisInto(p,curve.Count,curve.Knots,parameter,order,basis); var controls=curve.ControlPoints; double scale=0;
            for(var i=0;i<=p;i++) scale=Math.Max(scale,controls[span-p+i].W);
            for(var k=0;k<=order;k++) for(var i=0;i<=p;i++) h[k]+=(controls[span-p+i]/scale)*basis[k*(p+1)+i];
            if(!(h[0].W>0)) throw new ArithmeticException("Singular rational denominator.");
            for(var k=0;k<=order;k++)
            {
                var v=new Vec3(h[k].X,h[k].Y,h[k].Z);
                for(var i=1;i<=k;i++) v-=derivatives[k-i]*(Binomial(k,i)*h[i].W);
                derivatives[k]=v/h[0].W;
                if(!derivatives[k].IsFinite) throw new ArithmeticException("Non-finite rational derivative.");
            }
        }
        finally { if(rented is not null) ArrayPool<double>.Shared.Return(rented); }
    }
    public Vec3[,] Evaluate(NurbsSurface surface,double u,double v,int orderU=0,int orderV=0)
    {
        Order(orderU); Order(orderV); var output=new Vec3[orderU+1,orderV+1];
        EvaluateInto(surface,u,v,orderU,orderV,MemoryMarshal.CreateSpan(ref output[0,0],output.Length)); return output;
    }
    public void EvaluateInto(NurbsSurface surface,double u,double v,int orderU,int orderV,Span<Vec3> derivatives)
    {
        ArgumentNullException.ThrowIfNull(surface); Order(orderU); Order(orderV);
        var entries=(orderU+1)*(orderV+1);
        if(derivatives.Length<entries) throw new ArgumentException("Derivative buffer is too small.",nameof(derivatives));
        var p=surface.DegreeU; var q=surface.DegreeV; var us=(p+1)*(orderU+1); var vs=(q+1)*(orderV+1);
        double[]? rentedBasis=null; HPoint[]? rentedH=null;
        Span<double> basis=us+vs<=128 ? stackalloc double[us+vs] : (rentedBasis=ArrayPool<double>.Shared.Rent(us+vs)).AsSpan(0,us+vs);
        Span<HPoint> h=entries<=32 ? stackalloc HPoint[entries] : (rentedH=ArrayPool<HPoint>.Shared.Rent(entries)).AsSpan(0,entries);
        try
        {
            var bu=basis[..us]; var bv=basis[us..];
            var spanU=BasisInto(p,surface.CountU,surface.KnotsU,u,orderU,bu);
            var spanV=BasisInto(q,surface.CountV,surface.KnotsV,v,orderV,bv);
            var cp=surface.ControlPoints; double scale=0; h.Clear();
            for(var i=0;i<=p;i++) for(var j=0;j<=q;j++) scale=Math.Max(scale,cp[(spanU-p+i)*surface.CountV+spanV-q+j].W);
            for(var a=0;a<=orderU;a++) for(var b=0;b<=orderV;b++) for(var i=0;i<=p;i++) for(var j=0;j<=q;j++)
                h[a*(orderV+1)+b]+=(cp[(spanU-p+i)*surface.CountV+spanV-q+j]/scale)*(bu[a*(p+1)+i]*bv[b*(q+1)+j]);
            if(!(h[0].W>0)) throw new ArithmeticException("Singular rational surface denominator.");
            for(var a=0;a<=orderU;a++) for(var b=0;b<=orderV;b++)
            {
                var index=a*(orderV+1)+b; var r=new Vec3(h[index].X,h[index].Y,h[index].Z);
                for(var i=0;i<=a;i++) for(var j=0;j<=b;j++) if(i!=0||j!=0)
                    r-=derivatives[(a-i)*(orderV+1)+b-j]*(Binomial(a,i)*Binomial(b,j)*h[i*(orderV+1)+j].W);
                derivatives[index]=r/h[0].W;
                if(!derivatives[index].IsFinite) throw new ArithmeticException("Non-finite rational surface derivative.");
            }
        }
        finally
        {
            if(rentedBasis is not null) ArrayPool<double>.Shared.Return(rentedBasis);
            if(rentedH is not null) ArrayPool<HPoint>.Shared.Return(rentedH);
        }
    }
    public void EvaluateGrid(NurbsSurface surface,ReadOnlySpan<double> u,ReadOnlySpan<double> v,
        Span<SurfaceJet> samples,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if((long)u.Length*v.Length>1_000_000 || samples.Length<(long)u.Length*v.Length)
            throw new ArgumentException("Grid exceeds the sample budget or output buffer.");
        // Validate all axes before modifying caller-owned output.
        foreach(var t in u) if(!surface.DomainU.Contains(t)) throw new ArgumentOutOfRangeException(nameof(u));
        foreach(var t in v) if(!surface.DomainV.Contains(t)) throw new ArgumentOutOfRangeException(nameof(v));
        cancellationToken.ThrowIfCancellationRequested();
        if(u.IsEmpty || v.IsEmpty) return;
        var uw=surface.DegreeU+1; var vw=surface.DegreeV+1;
        if(2L*u.Length*uw+2L*v.Length*vw>4_000_000)
            throw new ArgumentException("Grid basis tables exceed the 32 MB workspace budget.");
        var bu=new double[checked(u.Length*uw*2)]; var bv=new double[checked(v.Length*vw*2)];
        var su=new int[u.Length]; var sv=new int[v.Length];
        for(var i=0;i<u.Length;i++) { cancellationToken.ThrowIfCancellationRequested(); su[i]=BasisInto(uw-1,surface.CountU,surface.KnotsU,u[i],1,bu.AsSpan(i*uw*2,uw*2)); }
        for(var j=0;j<v.Length;j++) { cancellationToken.ThrowIfCancellationRequested(); sv[j]=BasisInto(vw-1,surface.CountV,surface.KnotsV,v[j],1,bv.AsSpan(j*vw*2,vw*2)); }
        var cp=surface.ControlPoints;
        for(var i=0;i<u.Length;i++) for(var j=0;j<v.Length;j++)
        {
            if((j&63)==0) cancellationToken.ThrowIfCancellationRequested();
            double scale=0;
            for(var a=0;a<uw;a++) for(var b=0;b<vw;b++) scale=Math.Max(scale,cp[(su[i]-uw+1+a)*surface.CountV+sv[j]-vw+1+b].W);
            HPoint h=default,hu=default,hv=default;
            for(var a=0;a<uw;a++) for(var b=0;b<vw;b++)
            {
                var control=cp[(su[i]-uw+1+a)*surface.CountV+sv[j]-vw+1+b]/scale;
                var ub=bu[i*uw*2+a]; var vb=bv[j*vw*2+b];
                h+=control*(ub*vb); hu+=control*(bu[i*uw*2+uw+a]*vb); hv+=control*(ub*bv[j*vw*2+vw+b]);
            }
            if(!(h.W>0)) throw new ArithmeticException("Singular rational surface denominator.");
            var point=h.Position; var du=(new Vec3(hu.X,hu.Y,hu.Z)-point*hu.W)/h.W; var dv=(new Vec3(hv.X,hv.Y,hv.Z)-point*hv.W)/h.W;
            if(!point.IsFinite || !du.IsFinite || !dv.IsFinite) throw new ArithmeticException("Non-finite surface grid sample.");
            samples[i*v.Length+j]=new(point,du,dv);
        }
    }
}
