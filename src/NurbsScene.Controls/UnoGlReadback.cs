using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Uno.WinUI.Graphics3DGL;

namespace NurbsScene.Controls;

/// <summary>Compatibility boundary for the pinned Uno 6.7.135 browser package.
/// Native GLES resolution does not implement Uno's JavaScript BGRA translation.
/// Use Uno's existing RGBA readback and explicit channel-conversion path instead.
/// No GPU drawing, shader, or context fallback is substituted.</summary>
internal static class UnoGlReadback
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(GLCanvasElement))]
    public static void Configure(GLCanvasElement canvas)
    {
        if (!OperatingSystem.IsBrowser()) return;
        // Called from Init, after Uno chooses its default and also on reload or
        // deferred initial layout. A Loaded-only hook misses deferred Init.
        var field = typeof(GLCanvasElement).GetField("_readbackAsRgbaWithSwap",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(bool))
            throw new NotSupportedException("Uno's pinned readback contract changed; requalify the RGBA adapter before updating the package.");
        field.SetValue(canvas, true);
        Console.WriteLine("NURBSSCENE_READBACK: RGBA with framework red/blue conversion");
    }
}
