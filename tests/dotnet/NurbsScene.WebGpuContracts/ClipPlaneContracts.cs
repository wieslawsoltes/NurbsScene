using NurbsScene.Geometry;
using NurbsScene.Rendering;

internal static class ClipPlaneContracts
{
    internal static void Register(Action<string, Action> test)
    {
        test("equivalent huge and tiny clipping equations retain finite identical GPU coefficients", () =>
        {
            var normal = new Vec3(1, -2, 1); const double offset = -.75;
            var origin = new Vec3(1000, -2000, 3000);
            var expected = WebGpuRenderMath.RelativeClipPlane(normal, offset, origin);
            foreach (var scale in new[] { 1e-100, 1, 1e100, 1e300 })
            {
                var actual = WebGpuRenderMath.RelativeClipPlane(normal * scale, offset * scale, origin);
                Check((float)actual.Normal.X == (float)expected.Normal.X && (float)actual.Normal.Y == (float)expected.Normal.Y
                    && (float)actual.Normal.Z == (float)expected.Normal.Z && (float)actual.Offset == (float)expected.Offset,
                    "Equivalent half-spaces produced different Float32 coefficients.");
                foreach (var point in new[] { new Vec3(-1, -1, 0), new(3, 0, 0), new(0, 2, 0) })
                    Check(Math.Sign(Vec3.Dot(normal, point) + offset) == Math.Sign(Vec3.Dot(actual.Normal, point - origin) + actual.Offset),
                        "Normalization reversed the clipping half-space.");
            }
        });
        test("rendering directions normalize at binary64 extremes without length overflow", () =>
        {
            foreach (var direction in new[] { new Vec3(1e308, 1e308, 1e308), new(double.Epsilon, 0, 0), new(1e-300, -1e-300, 0) })
            {
                new WebGpuRenderSettings { LightDirection = direction }.Validate();
                var plane = WebGpuRenderMath.RelativeClipPlane(direction, 0, Vec3.Zero);
                Check(plane.Normal.IsFinite && Math.Abs(plane.Normal.Length - 1) < 1e-14 && plane.Offset == 0,
                    "Finite nonzero direction did not produce a unit plane.");
            }
        });
        test("unrepresentable clipping planes fail before shader uniform encoding", () =>
        {
            Throws<ArgumentException>(() => WebGpuRenderMath.RelativeClipPlane(Vec3.Zero, 1, Vec3.Zero));
            Throws<ArithmeticException>(() => WebGpuRenderMath.RelativeClipPlane(Vec3.UnitX, 1e100, Vec3.Zero));
            Throws<ArithmeticException>(() => WebGpuRenderMath.RelativeClipPlane(new(1e-300, 0, 0), 1e300, Vec3.Zero));
            Throws<ArithmeticException>(() => WebGpuRenderMath.RelativeClipPlane(Vec3.UnitX, 0, new(1e100, 0, 0)));
            Check(WebGpuRenderMath.RelativeClipPlane(Vec3.UnitX, -1e12, new(1e12, 0, 0)).Offset == 0,
                "Representable large-world offset was not rebased before encoding.");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
