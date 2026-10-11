using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>Managed binary64 rational evaluator for desktop and .NET WebAssembly.
/// Uses analytic basis differentiation; no finite-difference modeling derivatives.</summary>
public sealed class ManagedNurbsEvaluator : INurbsEvaluator
{
    public string Implementation=>"Managed binary64 NURBS";
    /// <summary>Finds the right-sided active span, or the last nonempty span
    /// at the domain end. Public input is validated; immutable geometry uses
    /// the validation-free internal path during repeated evaluation.</summary>
    public static int FindSpan(int degree,int count,ReadOnlySpan<double> knots,double t)
    {
        KnotVector.Validate(degree,count,knots);
        return FindSpanCore(degree,count,knots,t);
    }
    private static int FindSpanCore(int degree,int count,ReadOnlySpan<double> knots,double t)
    {
        if(!double.IsFinite(t)||t<knots[degree]||t>knots[count])throw new ArgumentOutOfRangeException(nameof(t));
        if(t==knots[count])
        {
            var span=count-1;
            while(span>degree&&knots[span]>=knots[count])span--;
            return span;
        }
        var low=degree;var high=count;
        while(low+1<high){var mid=low+(high-low)/2;if(t<knots[mid])high=mid;else low=mid;}
        return low;
    }
    internal static double Binomial(int n,int k){if(k<0||k>n)return 0;k=Math.Min(k,n-k);double c=1;for(var i=1;i<=k;i++)c=c*(n-i+1)/i;return c;}
    private static double Divide(double a,double b)=>b==0?0:a/b;
    /// <summary>Basis derivatives for the p+1 active controls. Right-sided at interior knots.</summary>
    public static (int Span,double[,] Derivatives) Basis(int p,int count,ReadOnlySpan<double> knots,double t,int order)
    {
        KnotVector.Validate(p,count,knots);
        return BasisCore(p,count,knots,t,order);
    }
    private static (int Span,double[,] Derivatives) BasisCore(int p,int count,ReadOnlySpan<double> knots,double t,int order)
    {
        if(order is <0 or >32)throw new ArgumentOutOfRangeException(nameof(order));
        var span=FindSpanCore(p,count,knots,t);var n=Math.Min(p,order);var ndu=new double[p+1,p+1];var left=new double[p+1];var right=new double[p+1];var ders=new double[order+1,p+1];ndu[0,0]=1;
        for(var j=1;j<=p;j++)
        {
            left[j]=t-knots[span+1-j];right[j]=knots[span+j]-t;double saved=0;
            for(var r=0;r<j;r++){ndu[j,r]=right[r+1]+left[j-r];var temp=Divide(ndu[r,j-1],ndu[j,r]);ndu[r,j]=saved+right[r+1]*temp;saved=left[j-r]*temp;}
            ndu[j,j]=saved;
        }
        for(var j=0;j<=p;j++)ders[0,j]=ndu[j,p];
        var a=new double[2,p+1];
        for(var r=0;r<=p;r++)
        {
            Array.Clear(a);var s1=0;var s2=1;a[0,0]=1;
            for(var k=1;k<=n;k++)
            {
                double d=0;var rk=r-k;var pk=p-k;
                if(r>=k){a[s2,0]=Divide(a[s1,0],ndu[pk+1,rk]);d=a[s2,0]*ndu[rk,pk];}
                var j1=rk>=-1?1:-rk;var j2=r-1<=pk?k-1:p-r;
                for(var j=j1;j<=j2;j++){a[s2,j]=Divide(a[s1,j]-a[s1,j-1],ndu[pk+1,rk+j]);d+=a[s2,j]*ndu[rk+j,pk];}
                if(r<=pk){a[s2,k]=-Divide(a[s1,k-1],ndu[pk+1,r]);d+=a[s2,k]*ndu[r,pk];}
                ders[k,r]=d;(s1,s2)=(s2,s1);
            }
        }
        double factor=p;for(var k=1;k<=n;k++){for(var j=0;j<=p;j++)ders[k,j]*=factor;factor*=p-k;}
        return(span,ders);
    }
    public Vec3[] Evaluate(NurbsCurve curve,double parameter,int derivativeOrder=0)
    {
        var p=curve.Degree;var (span,basis)=BasisCore(p,curve.Count,curve.Knots,parameter,derivativeOrder);var h=new HPoint[derivativeOrder+1];var controls=curve.ControlPoints;double scale=0;
        for(var i=0;i<=p;i++)scale=Math.Max(scale,controls[span-p+i].W);
        for(var k=0;k<=derivativeOrder;k++)for(var i=0;i<=p;i++)h[k]+=(controls[span-p+i]/scale)*basis[k,i];
        if(!(h[0].W>0))throw new ArithmeticException("Singular rational denominator.");
        var output=new Vec3[derivativeOrder+1];
        for(var k=0;k<=derivativeOrder;k++){var v=new Vec3(h[k].X,h[k].Y,h[k].Z);for(var i=1;i<=k;i++)v-=output[k-i]*(Binomial(k,i)*h[i].W);output[k]=v/h[0].W;if(!output[k].IsFinite)throw new ArithmeticException("Non-finite rational derivative.");}
        return output;
    }
    public Vec3[,] Evaluate(NurbsSurface surface,double u,double v,int orderU=0,int orderV=0)
    {
        var p=surface.DegreeU;var q=surface.DegreeV;var (spanU,bu)=BasisCore(p,surface.CountU,surface.KnotsU,u,orderU);var (spanV,bv)=BasisCore(q,surface.CountV,surface.KnotsV,v,orderV);var cp=surface.ControlPoints;var h=new HPoint[orderU+1,orderV+1];double scale=0;
        for(var i=0;i<=p;i++)for(var j=0;j<=q;j++)scale=Math.Max(scale,cp[(spanU-p+i)*surface.CountV+spanV-q+j].W);
        for(var a=0;a<=orderU;a++)for(var b=0;b<=orderV;b++)for(var i=0;i<=p;i++)for(var j=0;j<=q;j++)h[a,b]+=(cp[(spanU-p+i)*surface.CountV+spanV-q+j]/scale)*(bu[a,i]*bv[b,j]);
        if(!(h[0,0].W>0))throw new ArithmeticException("Singular rational surface denominator.");
        var result=new Vec3[orderU+1,orderV+1];
        for(var a=0;a<=orderU;a++)for(var b=0;b<=orderV;b++)
        {
            var r=new Vec3(h[a,b].X,h[a,b].Y,h[a,b].Z);
            for(var i=0;i<=a;i++)for(var j=0;j<=b;j++)if(i!=0||j!=0)r-=result[a-i,b-j]*(Binomial(a,i)*Binomial(b,j)*h[i,j].W);
            result[a,b]=r/h[0,0].W;if(!result[a,b].IsFinite)throw new ArithmeticException("Non-finite rational surface derivative.");
        }
        return result;
    }
}
