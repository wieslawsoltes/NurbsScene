#if NURBSSCENE_DESKTOP_QUALIFICATION
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NurbsScene.Controls;
using NurbsScene.Rendering;
using Windows.Graphics;

namespace NurbsScene.App;

// Opt-in real native host qualification. Ordinary builds and browser campaigns
// never include this implementation; the renderer and native APIs are unchanged.
public partial class App
{
    private sealed record DesktopFrameMetrics(long Frames, long GeometryUploadBytes,
        long GeometryUploadCount, int DrawCalls, int TriangleCount, int LineCount);

    partial void ConfigureQualification(WorkspaceView workspace)
    {
        var output = Environment.GetEnvironmentVariable("NURBSSCENE_DESKTOP_QUALIFICATION_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Environment.ExitCode = 1;
        _ = RunDesktopQualificationAsync(workspace, Path.GetFullPath(output));
    }

    private static async Task RunDesktopQualificationAsync(WorkspaceView view, string output)
    {
        var window = MainWindow ?? throw new InvalidOperationException("The desktop window is unavailable.");
        var canvas = view.Viewport.Canvas;
        var checks = new List<string>();
        var captures = new List<Dictionary<string, object?>>();
        const int captureBudgetMilliseconds = 60_000, captureOuterBudgetMilliseconds = 65_000;
        var report = new Dictionary<string, object?>
        {
            ["schema"] = 1,
            ["application"] = "Uno Platform",
            ["status"] = "running",
            ["scope"] = "Native desktop rendering and asynchronous GPU readback correctness; no hardware performance or in-flight cancellation qualification.",
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["platform"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["commit"] = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            ["requestedBackend"] = Environment.GetEnvironmentVariable("UNO_WEBGPU_BACKENDS"),
            ["driverFile"] = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
            ["windowClosed"] = false,
            ["capturePolicy"] = new { captureBudgetMilliseconds, captureOuterBudgetMilliseconds },
            ["captures"] = captures,
            ["checks"] = checks
        };
        var directory = Path.GetDirectoryName(output)!;
        void Publish()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        async Task Wait(Func<bool> ready, string failure)
        {
            var watch = Stopwatch.StartNew();
            while (!ready())
            {
                if (canvas.LastError is { } error) throw new InvalidOperationException(error);
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException(failure);
                await Task.Delay(50);
            }
        }
        async Task<byte[]> Capture(string name)
        {
            var sample = new Dictionary<string, object?> { ["name"] = name, ["status"] = "running" };
            captures.Add(sample);
            var elapsed = Stopwatch.StartNew();
            try
            {
                var pending = canvas.CaptureRgbaAsync(TimeSpan.FromMilliseconds(captureBudgetMilliseconds));
                sample["phase"] = canvas.LastCapturePhase;
                Publish();
                var bytes = await pending.WaitAsync(TimeSpan.FromMilliseconds(captureOuterBudgetMilliseconds));
                sample["status"] = "passed"; sample["phase"] = canvas.LastCapturePhase; sample["byteCount"] = bytes.Length;
                return bytes;
            }
            catch (Exception error)
            {
                sample["status"] = "failed"; sample["phase"] = canvas.LastCapturePhase; sample["error"] = error.Message;
                throw;
            }
            finally { sample["elapsedMilliseconds"] = elapsed.Elapsed.TotalMilliseconds; Publish(); }
        }
        window.Closed += (_, _) => { report["windowClosed"] = true; Publish(); };

        try
        {
            Publish();
            Check(!OperatingSystem.IsBrowser(), "The real native runtime owns capture mapping");
            window.AppWindow.Resize(new SizeInt32(1024, 768));
            var controller = view.Controller;
            Check(controller.Document.Objects.Count == 0 && controller.Graph.Nodes.Count == 0,
                "Ordinary startup remains an empty modeling document");
            controller.LoadPavilion();
            await Wait(() => canvas.IsLoaded && canvas.IsWebGpuInitialized == true && canvas.Frames > 0,
                "The native Uno WebGPU canvas did not initialize and submit a frame.");
            await Wait(() => controller.Graph.LastResult is { Stale: false } result && result.Revision == controller.Graph.Revision,
                "The existing Pavilion definition did not settle.");
            Check(controller.Graph.LastResult!.ErrorCount == 0, "The real Pavilion graph solves without errors");
            Check(canvas.LastError is null && canvas.Backend.StartsWith("WebGPU", StringComparison.Ordinal),
                "Uno presents through its native WebGPU backend");

            canvas.RestoreFourViews();
            canvas.Fit();
            var scene = controller.Scene;
            scene.Mode = DisplayMode.Shaded;
            scene.ShowGrid = false;
            scene.ShowIsocurves = false;
            var items = scene.Items.ToArray();
            Check(items.Length > 0 && scene.Meshes.Values.Any(mesh => mesh.Indices.Length > 0),
                "The existing exact Pavilion geometry supplies surface triangles");

            scene.SetItems([]);
            var empty = await Capture("empty");
            scene.SetItems(items);
            var filled = await Capture("filled");
            var views = canvas.Viewports;
            var width = views.Max(viewport => viewport.X + viewport.Width);
            var height = views.Max(viewport => viewport.Y + viewport.Height);
            var byteCount = checked(width * height * 4);
            Check(views.Length == 4 && views.All(viewport => viewport.Width > 0 && viewport.Height > 0),
                "Four real native viewports have positive framebuffer extents");
            Check(empty.Length == byteCount && filled.Length == byteCount && byteCount > 0,
                "Native asynchronous captures return complete RGBA framebuffers");
            var surfacePixels = ChangedPixels(empty, filled);
            Check(surfacePixels > 100, "Actual modeled surface pixels differ from the empty framebuffer");

            var before = ReadDesktopMetrics(canvas);
            var beforeScene = (scene.Revision, scene.MeshRevision, scene.Origin, scene.ShowGrid, scene.ShowIsocurves);
            var residentMeshes = scene.Meshes.ToDictionary(pair => pair.Key, pair => pair.Value);
            canvas.Cameras["Perspective"].Orbit(60, 25);
            var rotated = await Capture("camera");
            var after = ReadDesktopMetrics(canvas);
            Check(rotated.Length == byteCount, "Camera capture preserves framebuffer dimensions");
            var cameraPixels = ChangedPixels(filled, rotated);
            Check(cameraPixels > 50, "Camera motion changes the native GPU framebuffer");
            Check(beforeScene == (scene.Revision, scene.MeshRevision, scene.Origin, scene.ShowGrid, scene.ShowIsocurves)
                && residentMeshes.Count == scene.Meshes.Count
                && residentMeshes.All(pair => scene.Meshes.TryGetValue(pair.Key, out var mesh) && ReferenceEquals(pair.Value, mesh)),
                "Camera motion preserves scene revisions, origin and exact display mesh identities");
            Check(before.GeometryUploadBytes > 0 && before.GeometryUploadCount > 0
                && before.GeometryUploadBytes == after.GeometryUploadBytes
                && before.GeometryUploadCount == after.GeometryUploadCount,
                "Camera motion performs zero geometry uploads and reuses resident native GPU buffers");
            Check(after.Frames > before.Frames && before.DrawCalls > 0 && after.DrawCalls > 0
                && before.TriangleCount > 0 && after.TriangleCount > 0,
                "Both native camera samples submit real surface draw calls");
            Check(canvas.LastError is null, "Native capture and submission complete without a renderer error");

            var frameFiles = new Dictionary<string, object>();
            foreach (var (name, pixels) in new[] { ("empty", empty), ("filled", filled), ("camera", rotated) })
            {
                Check(Enumerable.Range(0, pixels.Length / 4).All(pixel => pixels[pixel * 4 + 3] == 255),
                    name + " native presentation is opaque");
                var filename = name + ".ppm";
                WriteDesktopPpm(Path.Combine(directory, filename), pixels, width, height);
                frameFiles[name] = new { file = filename, width, height, bytes = pixels.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(pixels)) };
            }
            report["frames"] = frameFiles;
            report["surfacePixels"] = surfacePixels;
            report["cameraPixels"] = cameraPixels;
            report["residency"] = new { before, after };
            report["status"] = "passed";
            Environment.ExitCode = 0;
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Environment.ExitCode = 1;
        }
        finally
        {
            report["gpuBackend"] = canvas.Backend;
            report["viewport"] = new { canvas.IsLoaded, canvas.IsWebGpuInitialized, canvas.ActualWidth,
                canvas.ActualHeight, canvas.Frames, canvas.LastError };
            Publish();
            // The ordinary Window.Closed handler disposes the workspace; Uno's X11
            // host then returns from Run after its last window closes. No forced exit.
            window.Close();
        }
    }

    private static DesktopFrameMetrics ReadDesktopMetrics(CadViewport canvas) =>
        new(canvas.Frames, canvas.GeometryUploadBytes, canvas.GeometryUploadCount,
            canvas.DrawCalls, canvas.TriangleCount, canvas.LineCount);

    private static int ChangedPixels(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) throw new InvalidOperationException("Capture dimensions changed.");
        var changed = 0;
        for (var i = 0; i < first.Length; i += 4)
            if (Math.Abs(first[i] - second[i]) + Math.Abs(first[i + 1] - second[i + 1])
                + Math.Abs(first[i + 2] - second[i + 2]) > 12) changed++;
        return changed;
    }

    private static void WriteDesktopPpm(string path, byte[] rgba, int width, int height)
    {
        using var file = File.Create(path);
        file.Write(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        var rgb = new byte[checked(width * height * 3)];
        for (int source = 0, target = 0; source < rgba.Length; source += 4, target += 3)
        {
            rgb[target] = rgba[source]; rgb[target + 1] = rgba[source + 1]; rgb[target + 2] = rgba[source + 2];
        }
        file.Write(rgb);
    }
}
#endif
