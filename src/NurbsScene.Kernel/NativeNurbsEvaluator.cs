using System.Runtime.InteropServices;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>Optional native C ABI adapter. Does not silently switch to managed evaluation on load/ABI failure.
/// Build native/kernel and place the platform library on the native search path.</summary>
public sealed unsafe class NativeNurbsEvaluator : INurbsEvaluator
{
    private const string Library="nurbsscene_kernel";
    public string Implementation=>"NurbsScene native C ABI 1";
    public NativeNurbsEvaluator(){if(ns_abi_version()!=1)throw new InvalidOperationException("Kernel ABI mismatch.");}
    private static void Check(int status){if(status!=0)throw new ArithmeticException($"Native NURBS kernel status {status}.");}
    private static double[] Flatten(ReadOnlySpan<HPoint> points){var flat=new double[points.Length*4];for(var i=0;i<points.Length;i++){flat[i*4]=points[i].X;flat[i*4+1]=points[i].Y;flat[i*4+2]=points[i].Z;flat[i*4+3]=points[i].W;}return flat;}
    public Vec3[] Evaluate(NurbsCurve curve,double parameter,int derivativeOrder=0)
    {
        if(derivativeOrder is <0 or >32)throw new ArgumentOutOfRangeException(nameof(derivativeOrder));
        var h=Flatten(curve.ControlPoints);var output=new double[(derivativeOrder+1)*3];var workspace=new double[ns_curve_workspace(curve.Degree,derivativeOrder)];
        fixed(double* knots=curve.Knots,points=h,result=output,work=workspace)Check(ns_curve_eval(curve.Degree,curve.Count,knots,points,parameter,derivativeOrder,result,output.Length,work,workspace.Length));
        var derivatives=new Vec3[derivativeOrder+1];for(var i=0;i<derivatives.Length;i++)derivatives[i]=new(output[i*3],output[i*3+1],output[i*3+2]);return derivatives;
    }
    public Vec3[,] Evaluate(NurbsSurface surface,double u,double v,int orderU=0,int orderV=0)
    {
        if(orderU is <0 or >32||orderV is <0 or >32)throw new ArgumentOutOfRangeException(nameof(orderU));
        var h=Flatten(surface.ControlPoints);var output=new double[(orderU+1)*(orderV+1)*3];var workspace=new double[ns_surface_workspace(surface.DegreeU,surface.DegreeV,orderU,orderV)];
        fixed(double* ku=surface.KnotsU,kv=surface.KnotsV,points=h,result=output,work=workspace)Check(ns_surface_eval(surface.DegreeU,surface.DegreeV,surface.CountU,surface.CountV,ku,kv,points,u,v,orderU,orderV,result,output.Length,work,workspace.Length));
        var derivatives=new Vec3[orderU+1,orderV+1];for(var i=0;i<=orderU;i++)for(var j=0;j<=orderV;j++){var index=(i*(orderV+1)+j)*3;derivatives[i,j]=new(output[index],output[index+1],output[index+2]);}return derivatives;
    }
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_abi_version();
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_curve_workspace(int degree,int order);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_surface_workspace(int p,int q,int du,int dv);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_curve_eval(int p,int n,double* u,double* h,double t,int order,double* result,int capacity,double* work,int workCapacity);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_surface_eval(int p,int q,int nu,int nv,double* u,double* v,double* h,double tu,double tv,int du,int dv,double* result,int capacity,double* work,int workCapacity);
}
