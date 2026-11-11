using NurbsScene.Geometry;
using NurbsScene.Rendering;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool success, string message) { if (!success) throw new Exception(message); }
void Near(double actual, double expected, double tolerance = 1e-8)
    => Check(Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), $"Expected {expected:R}; got {actual:R}.");
void Throws<T>(Action run) where T : Exception
{
    try { run(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

Test("perspective and orthographic reverse-Z preserve camera projection and map near/far exactly", () =>
{
    foreach (var name in new[] { "Perspective", "Top", "Front", "Right" })
    {
        var camera = new Camera(name) { Target = new(14, -9, 31), Distance = 73 };
        var origin = new Vec3(10, -7, 20);
        var matrix = WebGpuRenderMath.ReverseZCameraMatrix(camera, 1231, 759, origin);
        var (eye, forward, right, up) = camera.Basis();
        Near(matrix.Apply(eye + forward * camera.NearClip - origin).Z, 1, 2e-8);
        Near(matrix.Apply(eye + forward * camera.FarClip - origin).Z, 0, 1e-10);
        var point = camera.Target + right * 11 + up * 7;
        var projected = camera.Project(point, 1231, 759);
        var clip = matrix.Apply(point - origin);
        Near((clip.X * .5 + .5) * 1231, projected.X);
        Near((.5 - clip.Y * .5) * 759, projected.Y);
        Check(clip.Z > 0 && clip.Z < 1, "Target must lie in the WebGPU depth range.");
    }
});

Test("double precision rebasing keeps float reverse-Z useful at large world coordinates", () =>
{
    var origin = new Vec3(1e12, -2e12, 3e12);
    var camera = new Camera { Target = origin + new Vec3(10, 7, 3), Distance = 80 };
    var precise = WebGpuRenderMath.ReverseZCameraMatrix(camera, 960, 640, origin);
    Span<float> encoded = stackalloc float[16];
    WebGpuRenderMath.WriteMatrix(precise, encoded);
    var gpu = new Transform3(encoded.ToArray().Select(x => (double)x));
    var point = camera.Target - origin;
    var actual = gpu.Apply(point);
    var expected = precise.Apply(point);
    Near(actual.X, expected.X, 2e-6);
    Near(actual.Y, expected.Y, 2e-6);
    Near(actual.Z, expected.Z, 1e-9);
    Check(actual.Z > 0, "Float encoding lost reverse-Z depth.");
});

Test("frustum culling is conservative on all six homogeneous planes and behind-camera geometry", () =>
{
    var identity = Transform3.Identity;
    Bounds3 Box(Vec3 center, double radius = .1) => new(center - new Vec3(radius, radius, radius), center + new Vec3(radius, radius, radius));
    Check(WebGpuRenderMath.IsVisible(Box(new(0, 0, .5)), identity, Vec3.Zero), "Interior box culled.");
    foreach (var point in new[] { new Vec3(-2, 0, .5), new(2, 0, .5), new(0, -2, .5), new(0, 2, .5), new(0, 0, -1), new(0, 0, 2) })
        Check(!WebGpuRenderMath.IsVisible(Box(point), identity, Vec3.Zero), $"Outside box retained at {point}.");
    Check(WebGpuRenderMath.IsVisible(Box(new(1.1, 0, .5)), identity, Vec3.Zero), "Touching box culled.");
    Check(WebGpuRenderMath.IsVisible(new(new(-10, -10, -10), new(10, 10, 10)), identity, Vec3.Zero), "Containing box culled.");
    Check(!WebGpuRenderMath.IsVisible(Bounds3.Empty, identity, Vec3.Zero), "Empty box retained.");
    var camera = new Camera { Target = Vec3.Zero, Distance = 30 };
    var matrix = WebGpuRenderMath.ReverseZCameraMatrix(camera, 800, 600, Vec3.Zero);
    var (eye, forward, _, _) = camera.Basis();
    Check(!WebGpuRenderMath.IsVisible(Box(eye - forward * 10), matrix, Vec3.Zero), "Behind-camera geometry retained.");
    var origin = new Vec3(1e9, -1e9, 2e9);
    Check(WebGpuRenderMath.IsVisible(Box(origin + new Vec3(0, 0, .5)), identity, origin), "Rebased interior box culled.");
});

Test("directional shadow transform contains every scene corner including rebased and planar scenes", () =>
{
    foreach (var bounds in new[]
    {
        new Bounds3(new(-100, -2, -4), new(30, 40, 90)),
        new Bounds3(new(1e9, -2e9, 3e9), new(1e9 + 40, -2e9 + 10, 3e9)),
        new Bounds3(Vec3.Zero, Vec3.Zero)
    })
    foreach (var light in new[] { new Vec3(-.3, -.5, .85), Vec3.UnitZ, -Vec3.UnitY })
    {
        var origin = bounds.Center;
        var matrix = WebGpuRenderMath.ShadowMatrix(bounds, origin, light);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vec3((i & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            var clip = matrix.Apply(corner - origin);
            Check(Math.Abs(clip.X) <= 1 && Math.Abs(clip.Y) <= 1 && clip.Z is > 0 and < 1,
                $"Shadow clip missed scene corner: {clip}.");
        }
    }
});

Test("copy rows align correctly at odd widths and reject integer overflow", () =>
{
    foreach (var width in new[] { 1, 63, 64, 65, 257, 1023, 1921, 4097 })
    {
        var row = WebGpuRenderMath.AlignedBytesPerRow(width);
        Check(row % 256 == 0 && row >= width * 4 && row < width * 4 + 256, $"Bad row pitch for {width}.");
    }
    Throws<ArgumentOutOfRangeException>(() => WebGpuRenderMath.AlignedBytesPerRow(0));
    Throws<ArgumentOutOfRangeException>(() => WebGpuRenderMath.AlignedBytesPerRow(1, 0));
    Throws<OverflowException>(() => WebGpuRenderMath.AlignedBytesPerRow(int.MaxValue));
    Throws<ArgumentException>(() => WebGpuRenderMath.ReverseZCameraMatrix(new Camera(), 0, 100, Vec3.Zero));
    Throws<ArgumentException>(() => WebGpuRenderMath.ShadowMatrix(Bounds3.Empty, Vec3.Zero, Vec3.Zero));
});

var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"WebGPU contracts: {tests.Count - failed}/{tests.Count} passed.");
return failed == 0 ? 0 : 1;
