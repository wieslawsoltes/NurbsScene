# @nurbsscene/graphics

Reusable WebGPU CAD rendering with the same production WGSL as the Uno application.
Modeling, exact geometry, display tessellation and BVH selection remain independent
of the graphics device. Version `0.1.0-alpha.1`; no npm registry publication has occurred.

## Initialize and draw

```js
import { createRenderer, RenderScene, Camera, viewportLayout } from '@nurbsscene/graphics';

const { renderer } = await createRenderer(canvas, {
  backend: 'webgpu',
  onLost: message => showRenderingError(message),
  exposure: 1,
  shadows: true,
  shadowResolution: 2048,
  bloomStrength: .055,
});
const scene = new RenderScene();
scene.setItems(document.objects);
renderer.setScene(scene);
const cameras = Object.fromEntries(['Top', 'Perspective', 'Front', 'Right']
  .map(name => [name, new Camera(name)]));
const views = viewportLayout(canvas.width, canvas.height)
  .map(view => ({ ...view, camera: cameras[view.name] }));

renderer.render(views);
const rgba = await renderer.render(views, { capture: true });
renderer.setOptions({ exposure: 1.2, bloomStrength: .04 });
// On control teardown:
renderer.dispose();
```

`render()` returns submission metrics synchronously. Capture returns a promise for
tightly packed, top-to-bottom RGBA8 bytes. Each capture owns its staging buffer and
retains the width and height at submission, including while the canvas resizes.
The application retains the original canvas element throughout initialization and
capability failure. `dispose()` is idempotent and releases the owned device,
textures, geometry, uniform arenas and pending readbacks.

## Rendering pipeline

| Stage | Implementation |
| --- | --- |
| Precision | Binary64 authoritative geometry and camera arithmetic; origin-relative float32 GPU vertices and uniforms |
| Depth | Native WebGPU reverse-Z projection with `depth32float`, near=1 and far=0 |
| Surfaces | Linear `rgba16float` HDR, 4x MSAA, two-sided GGX, correlated Smith visibility and Schlick Fresnel |
| Lighting | Analytic studio environment, directional key light, two fill lights and a texel-snapped PCF shadow map |
| Transparency | Weighted blended accumulation/revealage with depth-tested 4x MSAA; no object-order sorting dependency |
| Curves and grids | Instanced screen-space ribbons, homogeneous near-plane clipping, analytic coverage and relative depth bias |
| Postprocessing | Half-resolution separable compute bloom, ACES response fit and exactly one sRGB encoding |
| Four views | Shared geometry, per-view frame uniforms/scissors, independent depth pixels and bloom constrained to each quadrant |

Opaque-only scenes bind cleared 1x1 OIT textures. Transparent surfaces allocate
full-size accumulation/revealage targets when needed. The multisampled and resolved
OIT attachments account for approximately 45 bytes per target pixel before driver
overhead, so omitting these full-size allocations matters for large CAD viewports.
The shadow map is reused across camera/selection movement and rebuilt when scene,
clipping, display mode or light direction changes. Immutable geometry buffers remain
allocated across camera, material and selection updates. Scene updates replace only
changed meshes; the previous complete resource set survives a synchronous upload
preparation failure.

`setOptions()` validates exposure, metallic/roughness defaults, ambient/key intensity,
bloom controls, shadows and light direction. Individual render items can supply
`metallic`, `roughness` and an RGBA `color`. Selection preserves material opacity.
`shadowResolution` is an allocation choice made during creation; recreate the renderer
to change it. `Ghosted` uses weighted transparency; `Wireframe` submits curve ribbons
without surface draws. Four-view and maximized layouts include odd-size and one-pixel
targets. Custom viewport rectangles remain supported; bloom is disabled for layouts
that do not match a single full target or a regular two-by-two partition.

These are concrete raster rendering capabilities. The analytic environment is not
image-based lighting, weighted transparency is an approximation, and the ACES curve
fit is not the complete ACES color pipeline. Transparent surfaces do not cast opaque
shadows. The package does not implement ray tracing, screen-space reflections, ambient
occlusion, temporal antialiasing or automatic device-loss reconstruction. Recreate the
renderer and call `setScene()` after device loss; the authoritative scene survives.
`cpuSubmissionMilliseconds` measures CPU preparation/submission, not GPU frame time.

## Migrating the previous graphics API

The factory still returns `{ renderer, canvas, failures }`; successful WebGPU creation
returns an empty `failures` array. Both `backend: 'auto'` and `backend: 'webgpu'` select
WebGPU. An unavailable API/adapter or initialization error rejects the promise with
capability or validation details. There is no canvas replacement or graphics fallback.

Explicit `backend: 'webgl2'`, `WebGl2Renderer.create()` and `new WebGl2Renderer()` reject
with migration instructions. The old import path and export name remain as tiny error
entrypoints so consumers receive a useful explanation. The GL implementation and GLSL
shaders have been removed. Replace those calls with `await createRenderer(canvas)` or
`await WebGpuRenderer.create(canvas)`. WebGPU requires a supported secure context and
asynchronous device initialization.

The public `WGSL` export now contains the shared production scene module and its
256-byte frame / 32-byte object ABI. `BLOOM_WGSL`, `COMPOSITE_WGSL`, `SHADERS` and
`SHADER_SOURCE_SHA256` expose the other modules and source provenance. Applications
that used the old shader/uniform layout directly must update their bindings.
`Camera.matrices()` keeps its existing projection contract for callers; the renderer
uses `reverseZMatrices()` from `@nurbsscene/graphics/render-math`.

## One authoritative shader source

Edit `src/NurbsScene.Rendering/Shaders/{scene,bloom,composite}.wgsl`, then run:

```sh
node packages/graphics/generate-shaders.mjs
node packages/graphics/generate-shaders.mjs --check
node --test tests/graphics-webgpu.test.mjs
```

`shaders.js` is deterministic generated ESM included in the independently installable
package. It needs no runtime fetch, Node dependency or repository-relative asset URL.
The Node contract compares every generated shader byte and SHA-256 with its canonical
source, so shader drift fails the normal JavaScript test suite. Do not edit generated
strings independently.

## Qualification

```sh
npm ci --offline --ignore-scripts
bash tools/build-kernel.sh
python -m pip install -r tests/requirements.txt
python -m playwright install --with-deps chromium
python tests/browser/test_gpu.py --software --output evidence/javascript-webgpu.json
```

The strict browser campaign draws actual NURBS workbench surfaces and requires
differential framebuffer output, orbit changes and all four views. Additional real
package submissions exercise reverse-Z occlusion, OIT ordering, compact/full OIT
equivalence, clipping, geometry reuse, asynchronous capture during resize, large-world
rebasing, visible PCF shadows, shadow caching, bloom, ghost/wireframe modes, near-plane
curve clipping, separators, tiny targets and explicit unavailable-WebGPU failure.
No GPU API is replaced or simulated. Compilation, validation, device-loss and page
errors fail the campaign. `--software` records the selected software adapter and uses
the coherent SwiftShader flags retained in the evidence JSON; this establishes API
and pixel correctness, not physical-GPU performance. Run without `--software` on each
target hardware/driver combination for additional coverage.

The independent offline UI test and Node geometry tests remain separate contracts.
Their success does not establish GPU qualification. Use the current CI artifact for
the exact revision's browser result. The standalone production shader campaign is
`tests/browser/test_webgpu_pipeline.py`; the Uno integration suite qualifies the
separate Uno host. MIT license applies to this package's own source.
