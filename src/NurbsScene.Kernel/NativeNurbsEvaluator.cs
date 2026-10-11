using System.Buffers;
using System.Runtime.InteropServices;
using NurbsScene.Geometry;

namespace NurbsScene.Kernel;

/// <summary>Native C ABI adapter with pinned read-only geometry and caller-owned
/// derivative spans. Does not silently switch to managed evaluation on ABI/load
/// failure. Scratch storage is per call; large workspaces use a scoped pool.</summary>
public sealed unsafe class NativeNurbsEvaluator : INurbsSpanEvaluator
{
    private const string Library = "nurbsscene_kernel";
    public string Implementation => "NurbsScene native C ABI 1";
    public NativeNurbsEvaluator()
    {
        if (sizeof(HPoint) != 4 * sizeof(double) || sizeof(Vec3) != 3 * sizeof(double))
            throw new PlatformNotSupportedException("Native geometry requires sequential binary64 vector layouts.");
        if (ns_abi_version() != 1) throw new InvalidOperationException("Kernel ABI mismatch.");
    }
    private static void Check(int status)
    {
        if (status != 0) throw new ArithmeticException($"Native NURBS kernel status {status}.");
    }
    private static void Order(int order)
    {
        if (order is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(order));
    }
    private static int Workspace(int size)
        => size is > 0 and <= 100_000 ? size : throw new InvalidOperationException("Invalid native workspace size.");

    public Vec3[] Evaluate(NurbsCurve curve, double parameter, int derivativeOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(curve); Order(derivativeOrder);
        var result = new Vec3[derivativeOrder + 1];
        EvaluateInto(curve, parameter, derivativeOrder, result);
        return result;
    }
    public void EvaluateInto(NurbsCurve curve, double parameter, int order, Span<Vec3> derivatives)
    {
        ArgumentNullException.ThrowIfNull(curve); Order(order);
        if (!curve.Domain.Contains(parameter)) throw new ArgumentOutOfRangeException(nameof(parameter));
        if (derivatives.Length < order + 1) throw new ArgumentException("Derivative buffer is too small.", nameof(derivatives));
        var size = Workspace(ns_curve_workspace(curve.Degree, order));
        double[]? rented = null;
        Span<double> scratch = size <= 256 ? stackalloc double[size] : (rented = ArrayPool<double>.Shared.Rent(size)).AsSpan(0, size);
        try
        {
            var controls = MemoryMarshal.Cast<HPoint, double>(curve.ControlPoints);
            var output = MemoryMarshal.Cast<Vec3, double>(derivatives[..(order + 1)]);
            fixed (double* knots = curve.Knots, points = controls, result = output, work = scratch)
                Check(ns_curve_eval(curve.Degree, curve.Count, knots, points, parameter, order, result, output.Length, work, size));
        }
        finally { if (rented is not null) ArrayPool<double>.Shared.Return(rented); }
    }
    public Vec3[,] Evaluate(NurbsSurface surface, double u, double v, int orderU = 0, int orderV = 0)
    {
        ArgumentNullException.ThrowIfNull(surface); Order(orderU); Order(orderV);
        var result = new Vec3[orderU + 1, orderV + 1];
        EvaluateInto(surface, u, v, orderU, orderV, MemoryMarshal.CreateSpan(ref result[0, 0], result.Length));
        return result;
    }
    public void EvaluateInto(NurbsSurface surface, double u, double v, int orderU, int orderV, Span<Vec3> derivatives)
    {
        ArgumentNullException.ThrowIfNull(surface); Order(orderU); Order(orderV);
        if (!surface.DomainU.Contains(u)) throw new ArgumentOutOfRangeException(nameof(u));
        if (!surface.DomainV.Contains(v)) throw new ArgumentOutOfRangeException(nameof(v));
        var count = (orderU + 1) * (orderV + 1);
        if (derivatives.Length < count) throw new ArgumentException("Derivative buffer is too small.", nameof(derivatives));
        var size = Workspace(ns_surface_workspace(surface.DegreeU, surface.DegreeV, orderU, orderV));
        double[]? rented = null;
        Span<double> scratch = size <= 256 ? stackalloc double[size] : (rented = ArrayPool<double>.Shared.Rent(size)).AsSpan(0, size);
        try
        {
            var controls = MemoryMarshal.Cast<HPoint, double>(surface.ControlPoints);
            var output = MemoryMarshal.Cast<Vec3, double>(derivatives[..count]);
            fixed (double* ku = surface.KnotsU, kv = surface.KnotsV, points = controls, result = output, work = scratch)
                Check(ns_surface_eval(surface.DegreeU, surface.DegreeV, surface.CountU, surface.CountV,
                    ku, kv, points, u, v, orderU, orderV, result, output.Length, work, size));
        }
        finally { if (rented is not null) ArrayPool<double>.Shared.Return(rented); }
    }
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_abi_version();
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_curve_workspace(int degree,int order);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_surface_workspace(int p,int q,int du,int dv);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_curve_eval(int p,int n,double* u,double* h,double t,int order,double* result,int capacity,double* work,int workCapacity);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]private static extern int ns_surface_eval(int p,int q,int nu,int nv,double* u,double* v,double* h,double tu,double tv,int du,int dv,double* result,int capacity,double* work,int workCapacity);
}
