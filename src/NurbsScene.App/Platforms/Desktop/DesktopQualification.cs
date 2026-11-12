#if NURBSSCENE_DESKTOP_QUALIFICATION
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Controls;
using NurbsScene.Controls.Drawing;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.Rendering;
using Uno.UI.Composition.Managed;
using Uno.WinUI.Graphics3DWebGpu;
using Windows.Graphics;
using Windows.UI.Text;

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
            ["scope"] = "Native desktop rendering, retained caches, font scaling, asynchronous GPU readback, in-flight cancellation/disposal and recovery correctness; no hardware performance claim.",
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

            report["fonts"] = QualifyNativeFonts(Check);
            report["renderCaches"] = await QualifyNativeRenderCachesAsync(canvas, Check, Wait);
            var lifetime = await QualifyNativeCaptureLifetimeAsync(view);
            report["captureLifetime"] = lifetime;
            Check(lifetime.CancelWasPending && (lifetime.CancelException == nameof(OperationCanceledException)
                || (lifetime.CancelException.Length == 0 && lifetime.CancelCompletedBytes == lifetime.AfterCancelBytes
                    && lifetime.CancelCompletedPixelsMatch && lifetime.CancelCompletedSha256 == lifetime.RecoverySha256)),
                "CancelCapture safely settles an in-flight native capture, including a concurrent successful completion");
            Check(lifetime.AfterCancelBytes == lifetime.Width * lifetime.Height * 4,
                "The same native renderer captures a complete framebuffer after cancellation");
            Check(lifetime.DisposeWasPending && (lifetime.DisposeException == nameof(ObjectDisposedException)
                || (lifetime.DisposeException.Length == 0 && lifetime.DisposeCompletedBytes == lifetime.AfterCancelBytes
                    && lifetime.DisposeCompletedPixelsMatch && lifetime.DisposeCompletedSha256 == lifetime.RecoverySha256)),
                "Disposing a renderer safely settles an in-flight native capture, including a concurrent successful completion");
            Check(lifetime.AfterDisposeBytes == lifetime.AfterCancelBytes && lifetime.RendererInstances == 2,
                "A replacement renderer captures successfully on the same borrowed Uno device after disposal");
            Check(lifetime.IdenticalRecoveryPixels,
                "Native cancellation and replacement preserve the exact rendered RGBA pixels");
            Check(lifetime.OriginalTriangles > 0 && lifetime.ReplacementTriangles > 0,
                "Both native lifetime recoveries render actual Pavilion surface triangles");
            Check(lifetime.CanvasUnloaded,
                "The native lifetime canvas unloads and releases its final owned renderer normally");
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

    private static object QualifyNativeFonts(Action<bool, string> check)
    {
        const string text = "AV office ffi";
        const int zoomSamples = 512;
        float[] sizes = [7.25f, 12, 19.5f, 64, 127.75f];
        var provider = ManagedBackend.CreateFontProvider();
        using var panels = new PanelTypographyRenderer();
        var faceEvidence = new List<object>();
        var stylesMatch = true;
        var metricsMatch = true;
        var resolutionsMatch = true;
        static bool Near(double actual, double expected) =>
            double.IsFinite(actual) && Math.Abs(actual - expected) <= Math.Max(1e-4, Math.Abs(expected) * 2e-6);

        foreach (var (bold, italic, suffix) in new[]
        {
            (false, false, ""), (true, false, "-Bold"),
            (false, true, "-Oblique"), (true, true, "-BoldOblique")
        })
        {
            using var requested = new VectorFontStyle(bold ? VectorFontStyleWeight.Bold : VectorFontStyleWeight.Normal,
                VectorFontStyleWidth.Normal, italic ? VectorFontStyleSlant.Italic : VectorFontStyleSlant.Upright);
            // An empty family selects the exact packaged face, independently of installed font versions.
            using var typeface = VectorTypeface.FromFamilyName(null, requested);
            using var vector = new VectorFont(typeface, sizes[0]);
            using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(
                "NurbsScene.Controls.Drawing.Fonts.DejaVuSans" + suffix + ".ttf")
                ?? throw new InvalidOperationException("The native font reference resource is unavailable.");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var fontBytes = bytes.ToArray();
            var samples = new List<object>();
            foreach (var size in sizes)
            {
                vector.Size = size;
                var reference = provider.CreateFont(fontBytes, "DejaVu Sans",
                    new FontWeight { Weight = (ushort)(bold ? 700 : 400) }, FontStretch.Normal,
                    italic ? FontStyle.Italic : FontStyle.Normal, size)
                    ?? throw new InvalidOperationException("The native managed reference font could not be created.");
                var run = reference.Shape(text.AsSpan(), out _);
                float referenceWidth = 0;
                foreach (var advance in run.Advances) referenceWidth += advance;
                var width = vector.MeasureText(text);
                var metrics = vector.Metrics;
                var panelMetrics = panels.GetFontMetrics(new PanelTextStyle(Bold: bold, Italic: italic), size);
                // Uno shapes at fixed design scale 512, independently of pixel size.
                // Only floating-point accumulation, not pixel hinting, separates these two paths.
                metricsMatch &= run.Count < text.Length && Near(width, referenceWidth)
                    && Near(metrics.Ascent, reference.Ascent) && Near(metrics.Descent, reference.Descent)
                    && Near(panelMetrics.Ascent, reference.Ascent) && Near(panelMetrics.Descent, reference.Descent);
                samples.Add(new { size, width, referenceWidth, metrics.Ascent, metrics.Descent,
                    referenceAscent = reference.Ascent, referenceDescent = reference.Descent, glyphs = run.Count });
            }
            vector.Size = 64;
            var canonicalWidth = vector.MeasureText(text);
            for (var sample = 0; sample < zoomSamples; sample++)
            {
                vector.Size = 5.25f + sample * .25f;
                resolutionsMatch &= Near(vector.MeasureText(text), canonicalWidth * (vector.Size / 64f));
            }
            stylesMatch &= typeface.IsBold == bold && typeface.IsItalic == italic;
            resolutionsMatch &= typeface.ResolutionCount == 1;
            faceEvidence.Add(new { bold, italic, typeface.FamilyName, typeface.IsBold, typeface.IsItalic,
                typeface.ResolutionCount, zoomSamples, samples });
        }

        // Exercise the installed-family path that previously retained a sized font
        // in Uno's provider for every zoom value, as well as the bundled fallback above.
        var nativeDefault = provider.GetDefaultFont(new FontWeight { Weight = 400 }, FontStretch.Normal, FontStyle.Normal, 64);
        var installedReference = provider.MatchFamily(nativeDefault.FamilyName,
            new FontWeight { Weight = 400 }, FontStretch.Normal, FontStyle.Normal, 64)
            ?? throw new InvalidOperationException("The native default family could not be resolved as an installed font.");
        using var normal = new VectorFontStyle(VectorFontStyleWeight.Normal, VectorFontStyleWidth.Normal, VectorFontStyleSlant.Upright);
        using var installedTypeface = VectorTypeface.FromFamilyName(installedReference.FamilyName, normal);
        using var installed = new VectorFont(installedTypeface, 64);
        var installedRun = installedReference.Shape(text.AsSpan(), out _);
        float installedWidth = 0;
        foreach (var advance in installedRun.Advances) installedWidth += advance;
        for (var sample = 0; sample < zoomSamples; sample++)
        {
            installed.Size = 5.25f + sample * .25f;
            resolutionsMatch &= Near(installed.MeasureText(text), installedWidth * (installed.Size / 64f))
                && Near(installed.Metrics.Ascent, installedReference.Ascent * (installed.Size / 64f))
                && Near(installed.Metrics.Descent, installedReference.Descent * (installed.Size / 64f));
        }
        resolutionsMatch &= installedTypeface.ResolutionCount == 1
            && installedTypeface.FamilyName == installedReference.FamilyName;
        check(stylesMatch, "Native canonical font resolution preserves all four actual bundled weight and slant combinations");
        check(metricsMatch, "Native vector and panel metrics at fractional sizes match independently sized managed fonts and ligature shaping");
        check(resolutionsMatch, "Hundreds of native font size changes reuse one physical face per bundled or installed typeface");
        return new { text, zoomSamples, bundled = faceEvidence,
            installed = new { installedTypeface.FamilyName, installedTypeface.ResolutionCount, referenceWidth = installedWidth } };
    }

    private sealed record NativeRenderCacheSample(string Stage, long Frames, long ShadowPassCount,
        long ShadowCasterDrawCount, long MaterialUploadCount, long GeometryUploadBytes, long GeometryUploadCount);

    private static async Task<IReadOnlyList<NativeRenderCacheSample>> QualifyNativeRenderCachesAsync(
        CadViewport canvas, Action<bool, string> check, Func<Func<bool>, string, Task> wait)
    {
        var scene = canvas.Scene;
        var originalItems = scene.Items.ToArray();
        var originalMode = scene.Mode;
        var originalSettings = canvas.RenderSettings;
        var originalGeometry = (scene.MeshRevision, scene.Origin, canvas.GeometryUploadBytes, canvas.GeometryUploadCount);
        var originalMeshes = scene.Meshes.ToDictionary(pair => pair.Key, pair => pair.Value);
        var originalCameras = canvas.Cameras.ToDictionary(pair => pair.Key,
            pair => (pair.Value.Target, pair.Value.Distance, pair.Value.Scale,
                pair.Value.Azimuth, pair.Value.Elevation, pair.Value.FieldOfView));
        var samples = new List<NativeRenderCacheSample>();

        async Task<NativeRenderCacheSample> Frame(string stage, Action change)
        {
            var previousFrame = canvas.Frames;
            change();
            canvas.Invalidate();
            await wait(() => canvas.Frames > previousFrame, "No native frame completed for render cache stage " + stage + ".");
            if (canvas.LastError is { } error) throw new InvalidOperationException(error);
            var sample = new NativeRenderCacheSample(stage, canvas.Frames, canvas.ShadowPassCount,
                canvas.ShadowCasterDrawCount, canvas.MaterialUploadCount, canvas.GeometryUploadBytes, canvas.GeometryUploadCount);
            samples.Add(sample);
            return sample;
        }

        bool GeometryUnchanged(NativeRenderCacheSample sample) =>
            originalGeometry == (scene.MeshRevision, scene.Origin, sample.GeometryUploadBytes, sample.GeometryUploadCount)
            && originalMeshes.Count == scene.Meshes.Count
            && originalMeshes.All(pair => scene.Meshes.TryGetValue(pair.Key, out var mesh) && ReferenceEquals(pair.Value, mesh));
        static bool SameShadow(NativeRenderCacheSample a, NativeRenderCacheSample b) =>
            a.ShadowPassCount == b.ShadowPassCount && a.ShadowCasterDrawCount == b.ShadowCasterDrawCount;
        static bool SameMaterial(NativeRenderCacheSample a, NativeRenderCacheSample b) =>
            a.MaterialUploadCount == b.MaterialUploadCount;

        try
        {
            var baseline = await Frame("shaded-baseline", () =>
            {
                scene.Mode = DisplayMode.Shaded;
                canvas.RenderSettings = originalSettings with
                {
                    Shadows = true, Exposure = 1, BloomStrength = .055f, Metallic = .12f, Roughness = .36f,
                    LightDirection = new(-.45, -.6, 1)
                };
            });
            check(baseline.ShadowPassCount > 0 && baseline.ShadowCasterDrawCount > 0
                && baseline.MaterialUploadCount > 0 && GeometryUnchanged(baseline),
                "Native cache baseline contains real opaque shadow casters and resident material buffers");

            var unchanged = await Frame("unchanged-shaded", () => { });
            check(SameShadow(baseline, unchanged) && SameMaterial(baseline, unchanged) && GeometryUnchanged(unchanged),
                "An unchanged native frame reuses shadow, material and geometry buffers");

            var selection = await Frame("selection-only", () => scene.SetItems(originalItems.Select((item, index) =>
                index == 0 ? item with { Selected = !item.Selected } : item)));
            check(SameShadow(unchanged, selection) && selection.MaterialUploadCount == unchanged.MaterialUploadCount + 1
                && GeometryUnchanged(selection),
                "Selection updates native highlight material without redrawing shadows or uploading geometry");

            var post = await Frame("exposure-bloom-only", () =>
                canvas.RenderSettings = canvas.RenderSettings with { Exposure = 1.4f, BloomStrength = .1f });
            check(SameShadow(selection, post) && SameMaterial(selection, post) && GeometryUnchanged(post),
                "Exposure and bloom edits reuse native material, shadow and geometry buffers");

            var material = await Frame("metallic-roughness-only", () =>
                canvas.RenderSettings = canvas.RenderSettings with { Metallic = .72f, Roughness = .23f });
            check(SameShadow(post, material) && material.MaterialUploadCount == post.MaterialUploadCount + 1
                && GeometryUnchanged(material),
                "Metallic and roughness edits update only the native material buffer");

            var light = await Frame("active-light-direction", () =>
                canvas.RenderSettings = canvas.RenderSettings with { LightDirection = new(.6, -.2, 1) });
            check(light.ShadowPassCount == material.ShadowPassCount + 1
                && light.ShadowCasterDrawCount > material.ShadowCasterDrawCount
                && SameMaterial(material, light) && GeometryUnchanged(light),
                "An active light direction edit refreshes native shadow casters without material or geometry uploads");

            var ghosted = await Frame("ghosted-transition", () => scene.Mode = DisplayMode.Ghosted);
            check(ghosted.ShadowPassCount == light.ShadowPassCount + 1
                && ghosted.ShadowCasterDrawCount == light.ShadowCasterDrawCount
                && ghosted.MaterialUploadCount == light.MaterialUploadCount + 1 && GeometryUnchanged(ghosted),
                "Ghosted mode clears the old native shadow map once and draws zero opaque shadow casters");

            var ghostedUnchanged = await Frame("unchanged-ghosted", () => { });
            check(SameShadow(ghosted, ghostedUnchanged) && SameMaterial(ghosted, ghostedUnchanged)
                && GeometryUnchanged(ghostedUnchanged),
                "Unchanged Ghosted frames reuse the neutral native shadow map");

            var ghostedOff = await Frame("ghosted-shadows-disabled", () =>
                canvas.RenderSettings = canvas.RenderSettings with { Shadows = false });
            check(SameShadow(ghostedUnchanged, ghostedOff) && SameMaterial(ghostedUnchanged, ghostedOff)
                && GeometryUnchanged(ghostedOff),
                "Disabling shadows in Ghosted mode performs no native shadow, material or geometry work");

            var ghostedLight = await Frame("ghosted-inactive-light-direction", () =>
                canvas.RenderSettings = canvas.RenderSettings with { LightDirection = new(-.2, .7, 1) });
            check(SameShadow(ghostedOff, ghostedLight) && SameMaterial(ghostedOff, ghostedLight)
                && GeometryUnchanged(ghostedLight),
                "Inactive light edits preserve the cached neutral native shadow map");

            var ghostedOn = await Frame("ghosted-shadows-enabled", () =>
                canvas.RenderSettings = canvas.RenderSettings with { Shadows = true });
            check(SameShadow(ghostedLight, ghostedOn) && SameMaterial(ghostedLight, ghostedOn)
                && GeometryUnchanged(ghostedOn),
                "Enabling shadows in Ghosted mode still draws zero opaque native shadow casters");

            var shaded = await Frame("shaded-restored", () => scene.Mode = DisplayMode.Shaded);
            check(shaded.ShadowPassCount == ghostedOn.ShadowPassCount + 1
                && shaded.ShadowCasterDrawCount > ghostedOn.ShadowCasterDrawCount
                && shaded.MaterialUploadCount == ghostedOn.MaterialUploadCount + 1 && GeometryUnchanged(shaded),
                "Returning to Shaded mode refreshes native shadow casters and surface materials once");
        }
        finally
        {
            // Restore through the same public retained state used by application controls.
            scene.SetItems(originalItems);
            scene.Mode = originalMode;
            canvas.RenderSettings = originalSettings;
            canvas.Invalidate();
        }

        var restored = await Frame("original-state-restored", () => { });
        check(scene.Items.SequenceEqual(originalItems) && scene.Mode == originalMode && canvas.RenderSettings == originalSettings
            && GeometryUnchanged(restored) && originalCameras.All(pair => canvas.Cameras.TryGetValue(pair.Key, out var camera)
                && pair.Value == (camera.Target, camera.Distance, camera.Scale, camera.Azimuth, camera.Elevation, camera.FieldOfView)),
            "Native cache qualification restores display settings and selection while preserving cameras and exact meshes");
        return samples;
    }

    private static async Task<NativeCaptureLifetimeReport> QualifyNativeCaptureLifetimeAsync(WorkspaceView view)
    {
        var root = view.Content as Grid ?? throw new InvalidOperationException("The native workspace root is unavailable.");
        var probe = new NativeCaptureLifetimeCanvas(view.Controller.Scene)
        {
            Width = 160, Height = 128, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRowSpan(probe, Math.Max(1, root.RowDefinitions.Count));
        root.Children.Add(probe);
        try
        {
            // The probe advances only from the real compositor. This deadline
            // bounds a failed backend; it does not manufacture map timing.
            var result = await probe.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            root.Children.Remove(probe);
            await probe.Destroyed.WaitAsync(TimeSpan.FromSeconds(10));
            return result with { CanvasUnloaded = true };
        }
        finally { root.Children.Remove(probe); }
    }

    private sealed record NativeCaptureLifetimeReport(bool CancelWasPending, string CancelException,
        int CancelCompletedBytes, bool CancelCompletedPixelsMatch, string CancelCompletedSha256,
        bool DisposeWasPending, string DisposeException, int DisposeCompletedBytes, bool DisposeCompletedPixelsMatch,
        string DisposeCompletedSha256, int Width, int Height,
        int AfterCancelBytes, int AfterDisposeBytes, bool IdenticalRecoveryPixels,
        int OriginalTriangles, int ReplacementTriangles, int RendererInstances,
        string RecoverySha256, double ElapsedMilliseconds, bool CanvasUnloaded = false)
    {
        public string CancelOutcome => CancelException.Length == 0 ? "completed-before-cancellation" : "canceled-by-request";
        public string DisposeOutcome => DisposeException.Length == 0 ? "completed-before-cancellation" : "canceled-by-disposal";
    }

    // A real, qualification-only canvas supplies the public borrowed context.
    // Cancellation runs in RenderOverride with pending state asserted instead of
    // a sleep or renderer testing hook. Native spontaneous callbacks can finish
    // between observing a pending task and either cancellation call, so validate
    // both legal terminal outcomes and report which branch was actually observed.
    private sealed class NativeCaptureLifetimeCanvas(DisplayScene scene) : WebGpuCanvasElement
    {
        private readonly TaskCompletionSource<NativeCaptureLifetimeReport> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly Camera _camera = new("Perspective");
        private WebGpuRenderer? _renderer;
        private Task<byte[]>? _recovery;
        private byte[]? _afterCancel, _cancelCompleted;
        private int _stage, _width, _height, _originalTriangles, _replacementTriangles, _disposeCompletedBytes;
        private bool _cancelWasPending, _cancelCompletedPixelsMatch, _disposeWasPending, _disposeCompletedPixelsMatch;
        private string _cancelException = "", _cancelCompletedSha256 = "", _disposeException = "", _disposeCompletedSha256 = "";
        public Task<NativeCaptureLifetimeReport> Completion => _completion.Task;
        public Task Destroyed => _destroyed.Task;

        protected override unsafe void Init(WebGpuCanvasContext context)
        {
            try { _renderer = new(context.Api, context.Device, context.Queue, context.TargetFormat); }
            catch (Exception error) { _completion.TrySetException(error); throw; }
        }

        protected override unsafe void RenderOverride(WebGpuCanvasContext context)
        {
            if (_completion.Task.IsCompleted) return;
            try
            {
                var renderer = _renderer ?? throw new InvalidOperationException("The native lifetime renderer is unavailable.");
                if (_stage == 0)
                {
                    _width = context.PixelWidth; _height = context.PixelHeight;
                    ViewportNavigation.Fit(_camera, scene.Bounds, _width, _height);
                    RenderScene(context, renderer);
                    _originalTriangles = renderer.TriangleCount;
                    var pending = renderer.CaptureRgbaAsync();
                    _cancelWasPending = !pending.IsCompleted;
                    if (!_cancelWasPending) throw new InvalidOperationException("Cancellation qualification requires an actually pending native map.");
                    renderer.CancelCapture();
                    renderer.CancelCapture(); // Public cancellation must also be idempotent.
                    _cancelCompleted = ReadTerminal<OperationCanceledException>(pending, out _cancelException);
                    _recovery = renderer.CaptureRgbaAsync();
                    _stage = 1;
                }
                else if (_stage == 1 && _recovery!.IsCompleted)
                {
                    _afterCancel = ReadRecovery(_recovery);
                    if (_cancelCompleted is { } completedCancel)
                    {
                        _cancelCompletedPixelsMatch = completedCancel.AsSpan().SequenceEqual(_afterCancel);
                        _cancelCompletedSha256 = Convert.ToHexString(SHA256.HashData(completedCancel));
                        if (!_cancelCompletedPixelsMatch)
                            throw new InvalidOperationException("A capture completed concurrently with cancellation returned different pixels.");
                    }
                    var pending = renderer.CaptureRgbaAsync();
                    _disposeWasPending = !pending.IsCompleted;
                    if (!_disposeWasPending) throw new InvalidOperationException("Disposal qualification requires an actually pending native map.");
                    renderer.Dispose();
                    renderer.Dispose(); // Releasing one renderer must not release Uno's device.
                    _renderer = null;
                    var completed = ReadTerminal<ObjectDisposedException>(pending, out _disposeException);
                    if (completed is not null)
                    {
                        // The native callback may win before Dispose acquires the
                        // request lock. Its successfully completed pixels remain valid.
                        _disposeCompletedBytes = completed.Length;
                        _disposeCompletedPixelsMatch = _afterCancel.AsSpan().SequenceEqual(completed);
                        _disposeCompletedSha256 = Convert.ToHexString(SHA256.HashData(completed));
                        if (!_disposeCompletedPixelsMatch)
                            throw new InvalidOperationException("A capture completed concurrently with disposal returned different pixels.");
                    }
                    _renderer = new(context.Api, context.Device, context.Queue, context.TargetFormat);
                    RenderScene(context, _renderer);
                    _replacementTriangles = _renderer.TriangleCount;
                    _recovery = _renderer.CaptureRgbaAsync();
                    _stage = 2;
                }
                else if (_stage == 2 && _recovery!.IsCompleted)
                {
                    var afterDispose = ReadRecovery(_recovery);
                    _completion.TrySetResult(new(_cancelWasPending, _cancelException,
                        _cancelCompleted?.Length ?? 0, _cancelCompletedPixelsMatch, _cancelCompletedSha256,
                        _disposeWasPending, _disposeException, _disposeCompletedBytes, _disposeCompletedPixelsMatch,
                        _disposeCompletedSha256, _width, _height,
                        _afterCancel!.Length, afterDispose.Length, _afterCancel.AsSpan().SequenceEqual(afterDispose),
                        _originalTriangles, _replacementTriangles, 2,
                        Convert.ToHexString(SHA256.HashData(afterDispose)), _elapsed.Elapsed.TotalMilliseconds));
                    return;
                }
                // Native callbacks progress through the compositor's normal poll.
                // This bounded qualification request does not call DevicePoll itself.
                Invalidate();
            }
            catch (Exception error) { _completion.TrySetException(error); throw; }
        }

        private unsafe void RenderScene(WebGpuCanvasContext context, WebGpuRenderer renderer)
        {
            if (context.PixelWidth != _width || context.PixelHeight != _height)
                throw new InvalidOperationException("The fixed native lifetime canvas changed framebuffer dimensions.");
            renderer.Render(scene, [new("Perspective", 0, 0, _width, _height, _camera)], context.TargetView, _width, _height);
        }

        private byte[] ReadRecovery(Task<byte[]> completed)
        {
            var bytes = completed.GetAwaiter().GetResult(); // Only called after IsCompleted.
            if (bytes.Length != checked(_width * _height * 4)
                || Enumerable.Range(0, bytes.Length / 4).Any(pixel => bytes[pixel * 4 + 3] != 255))
                throw new InvalidOperationException("Native lifetime recovery did not return a complete opaque RGBA framebuffer.");
            return bytes;
        }

        private byte[]? ReadTerminal<TException>(Task<byte[]> pending, out string exception) where TException : Exception
        {
            if (!pending.IsCompleted) throw new InvalidOperationException("Native cancellation did not settle its public task.");
            exception = "";
            try { return ReadRecovery(pending); }
            catch (TException error) { exception = error.GetType().Name; return null; }
        }

        protected override void OnWebGpuUnavailable() =>
            _completion.TrySetException(new InvalidOperationException("The native lifetime canvas requires the actual WebGPU backend."));

        protected override void OnDestroy(WebGpuCanvasContext context)
        {
            try
            {
                _renderer?.Dispose(); _renderer = null;
                _completion.TrySetException(new InvalidOperationException("The native lifetime canvas unloaded before completing qualification."));
                _destroyed.TrySetResult();
            }
            catch (Exception error)
            {
                _completion.TrySetException(error);
                _destroyed.TrySetException(error);
                throw;
            }
        }
    }

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
