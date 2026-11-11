# Uno Platform 7 and WebGPU migration

## Scope

This migration starts at `5c14b18` and is developed in
[PR #18](https://github.com/wieslawsoltes/NurbsScene/pull/18).

The production Uno desktop and WebAssembly applications now use Uno Platform 7
and its WebGPU compositor. Their CAD viewport, graph canvas, icons, overlays,
paths and text all draw through WebGPU. The application dependency graph has no
SkiaSharp runtime package, GL canvas, GL framebuffer readback bridge or
Silk.NET.OpenGL reference.

The independent npm graphics packages and their validation harness remain a
separate compatibility distribution with WebGPU/WebGL2 implementations. They are
not used to render the Uno application. This change does not alter exact binary64
NURBS authoring, document history, parametric solving, picking or interchange.

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
   diagnostic workflow exposes the already-captured failing browser console.
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
