using NurbsScene.Geometry;

namespace NurbsScene.Rendering;

/// <summary>
/// Camera and transfer contracts shared by the WebGPU renderer and qualification.
/// All world-space arithmetic remains binary64 until origin-relative uniforms are written.
/// </summary>
public static class WebGpuRenderMath
{
    public const int FrameUniformSize = 256;
    public const int ObjectUniformSize = 32;
    public const int UniformAlignment = 256;
    public const int TextureRowAlignment = 256;

    /// <summary>
    /// Normalize an equivalent clipping half-space before origin-relative Float32
    /// encoding. Reject offsets that overflow binary64 normalization or cannot be
    /// represented after rebasing; never send infinity or NaN to a shader.
    /// </summary>
    public static (Vec3 Normal, double Offset) RelativeClipPlane(Vec3 normal, double offset, Vec3 origin)
    {
        var scale = Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z)));
        if (!normal.IsFinite || !(scale > 0) || !double.IsFinite(offset) || !origin.IsFinite)
            throw new ArgumentException("A clipping plane requires a finite nonzero normal, offset and origin.");
        var scaled = normal / scale;
        var length = Math.Sqrt(Vec3.Dot(scaled, scaled));
        var unit = scaled / length;
        // Select the division order so a representable unit offset is not lost
        // merely because an intermediate division by a tiny scale overflows.
        var unitOffset = scale >= 1 ? offset / scale / length : offset / length / scale;
        if (!double.IsFinite(unitOffset)) throw new ArithmeticException("Clipping-plane normalization exceeds binary64 range.");
        var relativeOffset = unitOffset + Vec3.Dot(unit, origin);
        if (!float.IsFinite((float)relativeOffset)) throw new ArithmeticException("Clipping-plane offset exceeds Float32 range after rebasing.");
        return (unit, relativeOffset);
    }

    /// <summary>
    /// Column-major, right-handed WebGPU clip transform. Near maps to one and far
    /// maps to zero, without subtracting a rounded legacy GL depth from clip W.
    /// </summary>
    public static Transform3 ReverseZCameraMatrix(Camera camera, int width, int height, Vec3 origin)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (width <= 0 || height <= 0 || !origin.IsFinite)
            throw new ArgumentException("A finite origin and positive viewport dimensions are required.");
        var (eye, forward, right, up) = camera.Basis(origin);
        var backward = -forward;
        var view = new Transform3([
            right.X, up.X, backward.X, 0,
            right.Y, up.Y, backward.Y, 0,
            right.Z, up.Z, backward.Z, 0,
            -Vec3.Dot(right, eye), -Vec3.Dot(up, eye), -Vec3.Dot(backward, eye), 1]);
        var near = camera.NearClip;
        var far = camera.FarClip;
        var aspect = width / (double)height;
        Transform3 projection;
        if (camera.Perspective)
        {
            var focal = 1 / Math.Tan(camera.FieldOfView / 2);
            projection = new([
                focal / aspect, 0, 0, 0,
                0, focal, 0, 0,
                0, 0, near / (far - near), -1,
                0, 0, near * (far / (far - near)), 0]);
        }
        else
        {
            var half = camera.Scale / 2;
            projection = new([
                1 / (half * aspect), 0, 0, 0,
                0, 1 / half, 0, 0,
                0, 0, 1 / (far - near), 0,
                0, 0, far / (far - near), 1]);
        }
        return projection * view;
    }

    /// <summary>
    /// Stable directional-light orthographic transform, with ordinary WebGPU 0..1
    /// shadow depth. A conservative sphere contains all corners of the scene AABB.
    /// XY is snapped to shadow texels to avoid crawling under small scene changes.
    /// </summary>
    public static Transform3 ShadowMatrix(Bounds3 bounds, Vec3 origin, Vec3 lightDirection, int resolution = 2048)
    {
        if (!origin.IsFinite || !lightDirection.IsFinite || lightDirection.Length == 0 || resolution < 1)
            throw new ArgumentException("Shadow inputs must be finite and the light and resolution nonzero.");
        if (bounds.IsEmpty) return Transform3.Identity;
        if (!bounds.Min.IsFinite || !bounds.Max.IsFinite)
            throw new ArgumentException("Shadow bounds must be finite.", nameof(bounds));
        var backward = lightDirection.Normalized();
        var guide = Math.Abs(Vec3.Dot(backward, Vec3.UnitZ)) > .95 ? Vec3.UnitY : Vec3.UnitZ;
        var right = Vec3.Cross(guide, backward).Normalized();
        var up = Vec3.Cross(backward, right);
        var radius = Math.Max(bounds.Size.Length * .525, .001);
        var texel = 2 * radius / resolution;
        var center = (bounds.Min - origin) / 2 + (bounds.Max - origin) / 2;
        var x = Math.Round(Vec3.Dot(right, center) / texel) * texel;
        var y = Math.Round(Vec3.Dot(up, center) / texel) * texel;
        var z = Vec3.Dot(backward, center) + 2 * radius;
        var view = new Transform3([
            right.X, up.X, backward.X, 0,
            right.Y, up.Y, backward.Y, 0,
            right.Z, up.Z, backward.Z, 0,
            -x, -y, -z, 1]);
        var near = radius * .5;
        var far = radius * 3.5;
        var projection = new Transform3([
            1 / radius, 0, 0, 0,
            0, 1 / radius, 0, 0,
            0, 0, 1 / (near - far), 0,
            0, 0, near / (near - far), 1]);
        return projection * view;
    }

    /// <summary>
    /// Conservative AABB/frustum test for WebGPU homogeneous clip space. Touching
    /// a plane remains visible. No perspective division or corner allocation occurs.
    /// </summary>
    public static bool IsVisible(Bounds3 worldBounds, Transform3 viewProjection, Vec3 origin)
    {
        ArgumentNullException.ThrowIfNull(viewProjection);
        if (worldBounds.IsEmpty) return false;
        if (!worldBounds.Min.IsFinite || !worldBounds.Max.IsFinite || !origin.IsFinite)
            throw new ArgumentException("Visibility inputs must be finite.");
        var center = (worldBounds.Min - origin) / 2 + (worldBounds.Max - origin) / 2;
        var extent = worldBounds.Max / 2 - worldBounds.Min / 2;
        var m = viewProjection.Values;
        bool Outside(double a, double b, double c, double d)
        {
            var distance = a * center.X + b * center.Y + c * center.Z + d;
            var support = Math.Abs(a) * extent.X + Math.Abs(b) * extent.Y + Math.Abs(c) * extent.Z;
            // The relative guard keeps a rounded boundary from removing visible thin curves.
            return distance + support < -1e-12 * Math.Max(1, Math.Abs(distance) + support);
        }
        return !Outside(m[3] + m[0], m[7] + m[4], m[11] + m[8], m[15] + m[12])
            && !Outside(m[3] - m[0], m[7] - m[4], m[11] - m[8], m[15] - m[12])
            && !Outside(m[3] + m[1], m[7] + m[5], m[11] + m[9], m[15] + m[13])
            && !Outside(m[3] - m[1], m[7] - m[5], m[11] - m[9], m[15] - m[13])
            && !Outside(m[2], m[6], m[10], m[14])
            && !Outside(m[3] - m[2], m[7] - m[6], m[11] - m[10], m[15] - m[14]);
    }

    /// <summary>Rows copied from a WebGPU texture must be aligned to 256 bytes.</summary>
    public static int AlignedBytesPerRow(int width, int bytesPerPixel = 4)
    {
        if (width < 1 || bytesPerPixel < 1) throw new ArgumentOutOfRangeException(nameof(width));
        var bytes = checked((long)width * bytesPerPixel);
        return checked((int)((bytes + TextureRowAlignment - 1) / TextureRowAlignment * TextureRowAlignment));
    }

    /// <summary>Encode a column-major transform without lossy world-space subtraction.</summary>
    public static void WriteMatrix(Transform3 transform, Span<float> target)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (target.Length < 16) throw new ArgumentException("A matrix requires sixteen floats.", nameof(target));
        var source = transform.Values;
        for (var i = 0; i < 16; i++)
        {
            var value = (float)source[i];
            if (!float.IsFinite(value)) throw new ArithmeticException("The rebased matrix exceeds Float32 range.");
            target[i] = value;
        }
    }
}
