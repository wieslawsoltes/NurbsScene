# Uno Platform 7 and WebGPU migration

## Scope

This migration starts at `5c14b18` and is developed in
[PR #18](https://github.com/wieslawsoltes/NurbsScene/pull/18).

The production Uno desktop and WebAssembly applications now use Uno Platform 7
and its WebGPU compositor. Their CAD viewport, graph canvas, icons, overlays,
paths and text all draw through WebGPU. The application dependency graph has no
SkiaSharp runtime package, GL canvas, GL framebuffer readback bridge or
Silk.NET.OpenGL reference.

The independent npm graphics package now also uses WebGPU and the same production
WGSL shader sources. Its WebGL2 implementation and automatic fallback are removed;
legacy entry points report migration guidance. Its validation harness uses ordinary
DOM/SVG controls and remains separate from the Uno application. Exact binary64 NURBS
authoring, document history, parametric solving, picking and interchange are preserved.

## Pinned platform and supported heads

| Component | Version / selection |
|---|---|
| Uno SDK | 7.0.5 in `global.json` |
| Uno UI, compositor and WebGPU canvas | 7.0.32, resolved by the SDK |
| Silk.NET.WebGPU | 2.23.0 |
| Target frameworks | `net10.0-desktop`, `net10.0-browserwasm` |
| App and Controls Uno features | `WebGpu` |
| Native host selection | WebGPU provider before Win32, X11 and macOS hosts |
| Browser host selection | WebGPU provider before WebAssembly host |
| Custom 2D fonts | Bundled DejaVu Sans 2.37 regular, bold, oblique and bold oblique |

WebGPU support is a real runtime requirement. An unsupported browser/driver must
report unavailability; this application does not silently substitute the former
renderer. The project retains its existing desktop and browser heads; it does not
introduce Android, iOS or WinAppSDK targets.

Uno currently documents its WebGPU backend as experimental. The SDK and runtime
versions are pinned together to keep the canvas, compositor and native API shim
compatible.

## Render architecture

```mermaid
flowchart TD
    Scene["Scene and camera state"] --> Geometry["Resident geometry and uniforms"]
    Geometry --> Shadow["Cached directional shadow map"]
    Geometry --> Opaque["HDR opaque surfaces and curves"]
    Geometry --> OIT["Weighted transparency"]
    Shadow --> Opaque
    Shadow --> OIT
    Opaque -- "Reverse-Z depth" --> OIT
    Opaque --> Bloom["Compute bloom by viewport"]
    Opaque --> Composite["HDR composition, ACES fit and sRGB"]
    OIT --> Composite
    Bloom --> Composite
    Composite --> Target["Presentation target"]
```

The Uno path borrows the presentation target and device from the compositor; the
reusable browser package owns its WebGPU device and canvas context. Capture is an
explicit asynchronous copy request, outside normal presentation.

`CadViewport` derives from `WebGpuCanvasElement`. It passes the context's borrowed
API, device, queue and target view to the reusable `WebGpuRenderer`. The renderer
does not create a second device or request an independent browser context. It
never releases Uno-owned handles and never retains the target view after a frame.

| Pass | GPU resources and behavior |
|---|---|
| Shadow | 2048 × 2048 depth32float map; directional light; caster bias; clip plane; refreshed after scene/light changes |
| Opaque | RGBA16F HDR, four samples; depth32float reverse-Z, near = 1 / far = 0; depth clear = 0 |
| Curves and isocurves | Six-vertex instanced ribbons; pixel-width expansion; analytic coverage; depth test; homogeneous near clipping |
| Ghosted and alpha surfaces | Four-sample RGBA16F weighted accumulation plus R8 revealage; opaque depth read-only; independent blending |
| Bloom | Half-resolution soft-threshold extraction and two separable compute blur passes |
| Presentation | Linear HDR transparency composition, bloom, ACES curve fit and one output transfer conversion |
| Requested capture | Separate composite target, GPU-to-staging copy, asynchronous mapping, padded-row removal and RGBA normalization |

Both rendering and postprocessing respect the four CAD viewport partitions.
Bloom filters are clamped within their viewport, including odd dimensions and
two-pixel separators; maximizing a viewport selects the single-view path.

### Materials and lighting

The surface shader evaluates GGX/Trowbridge-Reitz normal distribution, correlated
Smith visibility and Schlick Fresnel with metallic and roughness parameters.
Three directional studio lights and analytic hemispherical illumination provide
form and reflection cues. Surface color is decoded to linear light before shading.

The directional shadow uses comparison sampling with a weighted nine-position
kernel and a slope-aware receiver bias. Clipped surfaces also clip in the shadow
pass. Two-sided CAD surfaces are supported; derivative normals cover degenerate
display normals without propagating invalid values.

`WebGpuRenderSettings` exposes validated exposure, metallic, roughness, ambient
intensity, key-light intensity, bloom strength, shadow enablement and light
direction. These settings affect display only.

The environment is analytic studio lighting, not HDR image-based lighting. The
tone mapper is the common ACES response fit, not the complete ACES color-management
pipeline. Weighted blended transparency is order-independent in its accumulation,
but remains an approximation to exact sorted transmission.

### Geometry, precision and performance

Exact geometry and CPU display tessellation remain distinct. The renderer accepts
the existing knot-aware display meshes rather than replacing exact model geometry
with float shader state.

- GPU vertex/index/line buffers are cached by immutable display mesh identity.
  Camera movement, selection and color edits do not upload the geometry again.
- Mesh revisions control GPU synchronization. Selection and color changes update
  packed, adapter-aligned object uniforms.
- World coordinates and camera matrices are prepared in binary64. Scene-relative
  positions are converted to float only for GPU consumption.
- Reverse-Z uses a native zero-to-one projection rather than subtracting a
  float OpenGL depth value near its precision limit.
- Conservative AABB tests reject objects outside all six homogeneous clip planes.
- Four viewports share geometry buffers, pipelines, a shadow map, uniform arenas,
  one command encoder and one ordinary frame submission.
- The regular render path has no CPU framebuffer readback, synchronous queue wait
  or continuously running animation loop. Invalidation drives rendering.
- Exposed diagnostics report frame count, draw calls, triangles, lines, culled
  items, cumulative geometry upload bytes/count, and CPU submission time.
  CPU submission time is not a GPU frame-time measurement.

### Lifetime and capture

Initialization catches failures and releases partial resources because Uno does
not call `OnDestroy` when `Init` fails. Resize replaces owned targets, and
unload disposes every owned buffer, view, texture, bind group, sampler and pipeline.
A reloaded element starts with new device resources.

Capture is explicit and limited to one request in flight. Normal frames never map
a buffer. Copies use 256-byte row alignment and normalize BGRA source channels to
the existing RGBA capture contract.

Native map callbacks need the device owner to make progress. While capture is
pending, a 16 ms dispatcher timer invalidates the viewport so Uno can perform its
normal nonblocking device poll. The timer stops on completion, failure or unload;
a 10-second timeout cancels the map and requests one final retirement frame.
There is no busy wait or application-owned native polling loop. Completion is
marshaled to the UI dispatcher, and a retired request cannot change a reloaded
viewport's diagnostics or stop a newer request.

## Custom 2D vector drawing

`GraphCanvas`, `ViewportOverlay` and `WorkbenchIcon` derive from
`WebGpuVectorCanvasElement`. Its adapter maps existing path, shape, clip,
transform, dash and glyph operations to Uno's public drawing interfaces.

The adapter borrows Uno's shared factory and uses its nested `RenderOffscreen`
path. It then resolves the resulting GPU texture into the canvas target with a
shared `textureLoad` pipeline. This explicit GPU pass handles native BGRA window
formats and RGBA canvas formats without resetting the compositor's outer drawing
session. It performs no CPU raster-canvas fallback or CPU pixel upload.

Retained text measurement, glyph and panel layout caches remain bounded.
Bundled licensed fonts make the browser's default text shaping deterministic and
offline-capable. Font provenance, exact Git blob hashes and license are recorded
under `src/NurbsScene.Controls/Drawing/Fonts`. Font coverage/fallback is documented
in the typography guide; complete coverage of every writing system is not claimed.

The older Skia raster fixtures use an explicit test-only alias symbol. They
continue to qualify the retained-layout algorithms against their original
baseline; they are not the new application's renderer or proof of WebGPU text
visual parity.

## Reproducing qualification

The normal commands use the stock Uno build tasks:

```sh
dotnet workload install wasm-tools
dotnet build src/NurbsScene.App -c Release -f net10.0-desktop -p:NurbsSceneDesktopOnly=true
dotnet run --project tests/dotnet/NurbsScene.WebGpuContracts -c Release
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm -p:NurbsSceneQualification=true
python tools/stage-uno-site.py
```

Production shader execution, independently of the Uno host:

```sh
python -m pip install playwright
python -m playwright install --with-deps chromium
python tests/browser/test_webgpu_pipeline.py --software

python -m pip install -r tests/gpu/requirements.txt
python tests/gpu/qualify_native_pipeline.py
```

The Uno workflow keeps all ten existing UI campaigns mandatory. They consume the
same built artifact, record its exact commit/tree and publication hashes, and
must identify a real WebGPU device/backend. The dedicated WGSL job creates and
executes the production shader pipelines and checks actual pixels, clipping,
depth, transparency order, shadows, bloom, resizing and viewport isolation.
Software-adapter results are labeled separately from physical-GPU performance.

The separate npm compatibility workflow pins Ubuntu 24.04 because its retained
Playwright 1.57 dependency does not support the current Ubuntu 26.04 runner.

## Evidence and migration journal

1. The migration branch and draft PR were opened from `5c14b18`.
2. Uno 7 hosts and the canvas lifecycle were committed first. Both desktop and
   browser dependency restores resolved the pinned versions with no SkiaSharp
   dependency in the Uno app graph.
3. The renderer, vector UI and shader work were built in stages. The rendering
   library compiled with zero warnings/errors, the complete desktop app built,
   five render-math contracts passed, 28 artifact gate contracts passed and
   22 retained-typography baseline contracts passed.
4. Fifteen native shader/pipeline/readback checks passed on llvmpipe, a CPU
   adapter using an OpenGL backend through wgpu-native. These results established
   shader correctness; they are not hardware-performance or Uno-host evidence.
5. The execution workspace became unavailable before all implementation commits
   were uploaded. Source was recovered from the editing history into GitHub blobs
   and published as new commits; font binaries were recovered from the licensed
   upstream 2.37 distribution with recorded hashes.
6. The reconstructed revision `fc7f2f18` passed the real desktop build, the five
   math contracts and the Chromium production-WGSL pipeline job, including the
   added viewport-partition checks. All ten full Uno interaction campaigns then stopped at the same canvas
   initialization timeout. Their captured console is being recovered to diagnose
   the integration failure; these campaigns are not qualified.
7. The unrelated npm workflow runner incompatibility was identified from its
   failure log and corrected without changing its test set or dependency pins.
   Browser device-error observation was strengthened so uncaptured GPU validation
   errors cannot be mistaken for a passing visual campaign. A temporary read-only
   diagnostic workflow exposed the already-captured failing browser console.
8. Review found that a constant clip-space line bias could pull distant curves
   through opaque surfaces under reverse-Z. The bias is now relative, with new
   actual-pixel tests for hidden distant and visible coplanar curves.

CI results belong to the exact revision they test. Earlier local results do not
qualify subsequently reconstructed source. The PR's current checks and attached
evidence are the authority for the final revision.

## Quality boundary

This is a substantial replacement of the rendering architecture and presentation
pipeline. It is not a measured claim of AAA game-engine visual equivalence.
Physical-device GPU timings, before/after performance comparisons, representative
complex-scene screenshots and a cross-device visual review remain required.
The pipeline does not currently implement ray tracing, image-based environment
lighting, temporal antialiasing, screen-space ambient occlusion or GPU NURBS
tessellation. Existing geometric/parity limitations remain in the compatibility
inventory.

## Primary implementation references

- [Uno 7 migration guide](https://platform.uno/docs/articles/migrating-to-uno-7.html)
- [Uno WebGPU backend](https://platform.uno/docs/articles/features/using-webgpu-rendering.html)
- [WebGpuCanvasElement](https://platform.uno/docs/articles/controls/WebGpuCanvasElement.html)
- [Pinned Uno SDK](https://www.nuget.org/packages/Uno.Sdk/7.0.5)
- [Stable Uno canvas source](https://github.com/unoplatform/uno/tree/7.0.32/src/AddIns/Uno.WinUI.Graphics3DWebGpu)
- [Stable drawing backend](https://github.com/unoplatform/uno/tree/7.0.32/src/Uno.UI.Composition.WebGpu)
- [WebGPU specification](https://www.w3.org/TR/webgpu/)
- [DejaVu 2.37 release](https://github.com/dejavu-fonts/dejavu-fonts/releases/tag/version_2_37)


## Follow-up integration journal

### Browser integration diagnosis

The failing full-app console showed a device loss immediately after Uno imported
its WebGPU device and initialized the compositor: "A valid external Instance
reference no longer exists." Standalone offscreen WGSL execution on the same
software adapter passed. The behavior and launch arguments match
[Chromium issue 575926500](https://issues.chromium.org/issues/575926500), whose paired
reproduction distinguishes failing forced-adapter canvas interop from a coherent
SwiftShader Vulkan configuration.

The Uno CI campaigns now use the report's working software launch configuration:
`--enable-unsafe-webgpu --use-angle=swiftshader --enable-unsafe-swiftshader --use-vulkan=swiftshader --enable-features=Vulkan`.
Only test-process setup changes; application device ownership, timeouts,
assertions and GPU error observation remain intact. This diagnosis requires the
next complete Uno campaign run to confirm startup and interaction behavior.

At revision `47ae1c89`, the standalone Chromium shader job executed 16 frames
and passed all 21 checks, including both new reverse-Z occlusion regressions.
All three production WGSL modules compiled without messages or validation errors.


### XAML startup font registration

The coherent software driver configuration removed the observed Chromium device loss on `e580c23`, but the shared app still did not reach its first canvas frame. The captured surface was blank across the XAML chrome as well as the canvases. Source review against Uno 7.0.32 identified a separate necessary host fix: the managed browser font provider cannot discover a system default font. Registering fonts only inside the custom vector adapter does not configure XAML `TextBlock` measurement. Both hosts now register `ManagedBackend.CreateFontProvider(BundledFonts.GetDefaultFontBytes())` before application startup, using the same licensed embedded DejaVu Sans font. Release unhandled exceptions are written to stderr, and failed browser qualification reports public loaded/size/initialization state without suppressing exceptions or relaxing the first-frame assertion. Actual resolved font style is read from the font tables so synthetic bold/italic fallback remains correct when a named family returns a regular face. Full browser runtime qualification is rerun after this fix.


### Opaque-scene attachment cost

Opaque and wireframe scenes now use cleared 1×1 accumulation/revealage targets. Full-resolution 4× MSAA OIT targets are created only while transparent surfaces are present; scene or display-mode transitions rebuild and rebind the correct targets without invalidating geometry buffers. The neutral clear pass omits depth, because WebGPU requires matching render-attachment dimensions. The compositor clamps OIT texture loads to each texture's dimensions. This reduces nominal OIT attachment texel storage from 45 bytes per viewport pixel to 45 bytes for opaque-only scenes (about 356 MiB less at 3840×2160, before driver overhead or compression), and removes the corresponding full-size clears/resolves. It is a resource accounting improvement, not an FPS claim.

Two added actual-GPU regressions compare full-size and 1×1 OIT output byte for byte, including opaque geometry and clipped four-view bloom. Native WebGPU qualification passed 21/21 checks on llvmpipe after this change; the browser suite now executes 18 frames and 23 checks. The modified C# renderer also compiled locally; integrated application qualification remains required on the resulting branch head.


### Browser readback and resident-geometry evidence

The font-corrected app initialized its real WebGPU canvas, rendered XAML/vector controls and passed 26 modeling checks in all ten campaigns. It then exposed a browser-only capture failure: a native map callback reported `null function` and the request timed out. The renderer now accepts an optional host readback delegate, and the Uno browser host uses the public `WebGpuJsInterop.MapReadBase64Async` API already used by Uno 7.0.32's `WebGpuDrawingFactory.SnapshotAsync`. This maps the submitted staging buffer through the JavaScript event loop, without reflection or private handle-table access. Native hosts retain the asynchronous C callback path. Both paths validate padded byte counts, remove row padding, handle RGBA/BGRA, cancel outstanding maps and release their owned buffer/texture after completion. Extra native polling frames run only during requested native captures; browser captures progress without those extra frames. Normal presentation still performs no GPU-to-CPU pixel transfers.

Qualification now records cumulative geometry upload counts/bytes and actual draw/triangle/line work before and after camera-only motion. The artifact gate requires a new frame with changed image pixels, unchanged scene/origin/grid state, and exactly unchanged upload counters. CPU submission time is explicitly separate from GPU execution and readback. All 42 artifact-gate contracts passed locally before integration. Source references: [public browser map API](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.WebGpu.Init/WebGpuJsInterop.cs) and [Uno snapshot implementation](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.WebGpu/WebGpuDrawingFactory.cs).


### First complete pixel and buffer-reuse evidence; pointer input

The `e82b853` typography modeling report passed all 33 assertions on the actual compiled Uno browser app. It recorded 384,655 model-versus-empty pixels and a different camera-orbit image. Cumulative geometry uploads remained exactly 657,264 bytes across 34 upload operations before and after orbit, while frames advanced from 4 to 5 and the renderer submitted 40,960 triangles. The observed browser device reported no errors. These are correctness/residency observations; CPU submission timing is not GPU timing. Evidence is retained in the [e82 browser workflow](https://github.com/wieslawsoltes/NurbsScene/actions/runs/43112186112).

The subsequent native input campaigns exposed a new canvas hit-test requirement. Uno 7's `WebGpuCanvasVisual` inherits `BorderVisual`: `IsViewHit` allows traversal, but hit containment needs a background or border shape. The CAD and vector canvas constructors now set a transparent background so their real visual bounds can receive pointer input. This uses ordinary public XAML properties. Captures also re-invalidate a browser canvas only until its requested copy is encoded; no extra browser frames are requested while mapping is pending. The unchanged 10-second capture deadline now reports whether it was waiting for viewport submission or buffer mapping, making any remaining failure diagnosable. Qualification saves an initial real app screenshot after the modeling/readback gate, before input campaigns change the workspace.


### Complete reusable graphics migration

The separate `@nurbsscene/graphics` renderer has been rewritten around the same three authoritative WGSL modules. It now owns the corresponding reverse-Z, HDR/MSAA, shadow, curve, weighted-transparency, compute-bloom and tone-mapping resources through the browser WebGPU API. Geometry remains resident across camera changes, opaque scenes use compact neutral OIT targets, and asynchronous captures retain their own dimensions and padded buffers across resize. Initialization failures and device loss are explicit. The former WebGL2 module is a throwing migration shim; no GL rendering or GLSL remains in it.

`packages/graphics/generate-shaders.mjs` emits distributable JavaScript strings and SHA-256 provenance directly from `src/NurbsScene.Rendering/Shaders/*.wgsl`. The new Node contract suite verifies every generated byte and mathematical/API migration contracts (8/8 local checks passed). The mandatory GPU CI job additionally builds the real kernel, preserves the existing package NURBS framebuffer/orbit/four-view assertions, and exercises package depth, OIT, shadows, clipping, rebasing, resizing, near-plane curves, partitioned bloom and explicit capability failure on real browser GPU objects. Local Chromium process creation was blocked by the execution environment, so integrated browser results must come from the hosted CI run; no mock result is substituted. The [package guide](../packages/graphics/README.md) documents the API changes and rendering limits.


### Verified reusable GPU campaign and remaining integration diagnostics

At `d0c2a935`, both the mandatory GPU job and the existing JavaScript workflow passed. The reusable renderer executed 27 frames and 32 distinct assertions on Chromium/Google SwiftShader, with no shader messages or GPU/page errors. Its real NURBS workbench changed 30,887 pixels versus empty output and 11,491 pixels after orbit. Reversed OIT submission differed by at most one channel byte; visible shadow and near-plane ribbon checks passed. The existing workflow retained 12 native, 68 Node and 12 offline UI tests, plus isolated consumption of all six tarballs. [GPU artifact](https://github.com/wieslawsoltes/NurbsScene/actions/runs/43114369425/artifacts/13896939998).

Six full Uno input campaigns passed at `66f96ad`: workspace, panels, typography, panel display, panel edges and panel scrolling. Three campaigns reached their first submitted map but exceeded the 10-second capture deadline, without a GPU or page error. Rhino-style command entry still lost its prefix. These are recorded failures, not qualified passes. The next diagnostic batch uses a separate explicit 60-second capture budget only for qualification, retains the normal 10-second default, and records each capture's phase, end-to-end duration and byte count. Passive native `GPUBuffer.mapAsync` timing preserves the returned promise and exposes whether the GPU promise or managed continuation is delayed; failed maps remain mandatory error evidence. The input observer records a bounded passive key/character/focus/text timeline. Functional assertions, all ten campaigns, and exact source/publication checks remain required.

Visual review also found that XAML's typography-dialog preview used the regular fallback while the custom panel used the actual bold/italic face. The global public `IFontProvider` now chooses among the four licensed embedded DejaVu faces, retains normal installed-family matching, and bounds its exact-size fallback cache. This corrects the host font source rather than changing the requested font style or hiding the preview. Runtime visual acceptance is rerun with this provider.


### Native map cancellation lifetime review

Independent review against Uno 7.0.32's pinned wgpu-native implementation found that destroying a pending native buffer map can invoke its abort callback synchronously. Cleanup now records successful mapping ownership, skips unmap for failed/aborted maps, marks destruction before entering native code and defers handle release until the outer destroy wrapper returns. This avoids both an invalid second unmap and premature release during Uno's map-state bookkeeping. Browser mapping remains owned by the public async bridge. This correction is supported by the pinned [wgpu-core map-state implementation](https://github.com/gfx-rs/wgpu/blob/v29.0.3/wgpu-core/src/resource.rs) and [Uno's Silk compatibility wrapper](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.WebGpu.Init/SilkCompat/SilkCompat.Hand.cs); native hardware cancellation qualification remains separate from browser correctness.


### Command entry under delayed focus

The passive `c93a131` input trace isolated the remaining Rhino-style shell failure. Characters accumulated correctly in a canvas draft, but the queued text focus took approximately 830 ms to run in the software-rendered session. Each Space was still routed as a canvas repeat action, clearing the draft; the actual character event then discarded its whitespace. No delayed query-clear callback appeared in that failing interval.

Space now preserves an active transfer draft and its text comes from the actual layout-aware `CharacterReceived` event. Enter submits that pending draft through the normal command path. Space/Enter with no pending draft retain the existing repeat behavior. The regression campaign keeps its original exact-prefix test and adds fast native typing followed immediately by Enter, then an ordinary Undo; it does not force focus, slow typing, retry input or invoke a command test hook. The Rhino minimum rises from 56 to 58 checks and the ten-campaign UI minimum from 349 to 351, retaining every prior assertion.

The same `c93a131` run passed the other nine complete Uno input campaigns. Its styled XAML typography preview was visually checked and now uses the real bold-oblique face. Measured completed captures show native software-GPU mapping occupying most of the elapsed readback time; no evidence justified changing managed continuation dispatch.
