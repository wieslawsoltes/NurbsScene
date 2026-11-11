# Build and validation

`global.json` pins Uno.Sdk **7.0.5** (Uno Platform **7.0.32**) and a compatible .NET 10 SDK range. The desktop and browser application hosts select the WebGPU backend explicitly. The preserved source import is complete. Temporary transfer and dependency-cache workflows have been removed; their prior commits remain in Git history.

## Real Uno application

```sh
dotnet workload install wasm-tools
dotnet build src/NurbsScene.App -c Release -f net10.0-desktop -p:NurbsSceneDesktopOnly=true
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm
python tools/stage-uno-site.py
python -m http.server 8766 --bind 127.0.0.1 --directory artifacts/site
```

`src/NurbsScene.App` contains the host; reusable controls and modeling/application logic live in separate projects. Desktop compilation does not substitute for running every desktop platform.

Both host composition roots register `FontProvider(BundledFonts.CreateProvider())`
for XAML text and layout. The bundled resolver selects actual regular, bold, oblique
and bold-oblique DejaVu Sans faces for that explicit family and for the browser's
default fallback; other installed families retain Uno's managed resolution.
Application-specific GPU canvases use their own cached font resources. Global
registration is required on WebAssembly, where system fonts cannot be enumerated:
configuring only the custom canvas provider does not supply XAML's default font.
Passing only regular font bytes as the global default also loses requested fallback
weight/slant. See Uno's [font-provider configuration](https://platform.uno/docs/articles/features/using-webgpu-rendering.html#apps-without-skiasharp)
and [`IFontProvider` interface](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.Drawing/IFontProvider.cs).

## Managed numerical, graph and document suites

```sh
python -m pip install 'numpy>=1.26,<3' 'scipy>=1.12,<2'
mkdir -p tests/fixtures
python tools/generate-oracle-fixtures.py
python tools/generate-length-oracle.py
python tools/generate-query-oracle.py
python tools/generate-area-oracle.py
dotnet run --project tests/dotnet/NurbsScene.CoreTests -c Release
dotnet run --project tests/dotnet/NurbsScene.MeasurementTests -c Release
dotnet run --project tests/dotnet/NurbsScene.IntegrationTests -c Release
dotnet run --project tests/dotnet/NurbsScene.WorkbenchTests -c Release
dotnet run --project tests/dotnet/NurbsScene.GraphContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.PresentationContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.EditingContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.PerformanceContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.QueryContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.ReviewContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.SectionContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.WebGpuContracts -c Release
cmake -S native/kernel -B artifacts/native -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/native --parallel
LD_LIBRARY_PATH="$PWD/artifacts/native" NURBSSCENE_NATIVE_TESTS=1 dotnet run --project tests/dotnet/NurbsScene.SurfaceContracts -c Release
LD_LIBRARY_PATH="$PWD/artifacts/native" NURBSSCENE_NATIVE_TESTS=1 dotnet run --project tests/dotnet/NurbsScene.AnalysisContracts -c Release
```

SciPy generates independent test oracles; it is not an application dependency. Arc-length errors are numerical estimates rather than certified bounds. Surface trim commands extract rectangular UV domains, not arbitrary B-rep trim loops.

## Actual GPU browser qualification

```sh
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm -p:NurbsSceneQualification=true
python tools/stage-uno-site.py
python -m pip install playwright
python -m playwright install chromium
# Serve artifacts/site on port 8766 in another terminal.
python tests/browser/test_uno.py
```

The opt-in hook exercises real managed commands, undo/redo, graph changes, exact baking, workspace and typed-path round trips, four viewports and asynchronous differential WebGPU framebuffer readback. Seven additional checks cover curve-plane sections, overlap reporting, the new components, execution-plan/result reuse, definition serialization, and application-controller splitting/undo. No GPU mocks or skip-on-unavailable path exists. It is smoke qualification, not complete UI, hardware or performance coverage.

The viewport derives from Uno 7's `WebGpuCanvasElement`, borrows Uno's WebGPU device and queue, and renders directly into the compositor target. It does not create an independent browser canvas or copy each displayed frame to CPU memory. Readback runs only for explicit capture/qualification requests. A 16 ms dispatcher timer enforces bounded capture lifetime. It requests rendering until the capture frame has been submitted on either target, then continues requesting compositor frames only on native targets, where Uno's device polling completes asynchronous mapping. The browser map progresses from its JavaScript promise without extra frames after submission. The timer stops on completion, failure or unload; its deadline identifies whether the request was awaiting viewport submission or GPU mapping. The old reflected GL readback adapter and pinned Uno 6 packages are removed.

Interactive GPU canvases set a transparent XAML `Background` so the native
visual has a hit-test region. Uno's
[`WebGpuCanvasVisual`](https://github.com/unoplatform/uno/blob/7.0.32/src/AddIns/Uno.WinUI.Graphics3DWebGpu/WebGpuCanvasVisual.cs)
inherits [`BorderVisual`](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition/Composition/BorderVisual.skia.cs);
GPU texture pixels alone do not create its background geometry. The non-null brush permits pointer routing and focus across the canvas,
while non-interactive overlays and icons keep `IsHitTestVisible=false`.

On WebAssembly, readback calls Uno 7's public
[`WebGpuJsInterop.MapReadBase64Async`](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.WebGpu.Init/WebGpuJsInterop.cs)
bridge, the same mechanism used by Uno's
[`WebGpuDrawingFactory.SnapshotAsync`](https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.WebGpu/WebGpuDrawingFactory.cs).
It maps the actual staging buffer from the JavaScript event loop, copies its padded
bytes and unmaps before completing. The host injects this async operation into the
renderer; native targets retain asynchronous WebGPU callbacks and owner-driven
device polling. The renderer validates the returned length, removes row padding
and retains ownership of its capture buffer and texture until completion or
cancellation. This explicit capture path never runs during ordinary presentation.

Ordinary `CaptureRgbaAsync()` requests retain a 10-second deadline. An explicit
timeout must be finite, positive and at most 60 seconds. Browser qualification
requests the 60-second budget with a 65-second outer wait to measure cold software
GPU execution separately from the normal application deadline. This follows
[observed mapping-phase timeouts](https://github.com/wieslawsoltes/NurbsScene/actions/runs/43113642478)
with no device-validation or JavaScript errors; it lets the next qualification
distinguish a slow initial map from one that never completes. Each empty, filled
and rotated capture runs once, and a timeout still fails the campaign.

`captureMeasurements` records each request's budget, completion phase, RGBA byte
count and elapsed milliseconds. These durations include queue waiting, mapping,
transfer and managed scheduling. They are not GPU timestamp-query measurements
or hardware-latency targets. The artifact gate requires all three completed
captures, matching byte counts, finite durations and the documented bounds.

The real-device observer also records the first 64 native
`GPUBuffer.mapAsync` requests, including mode, offset, size, request/settlement
timestamps, status and duration; it reports any additional calls as dropped
diagnostic records. It returns the original native promise, passes through the
original receiver and arguments, and retains rejection details. Rejected or thrown
map operations also enter the mandatory error gate. These passive
observations distinguish a GPU map still pending from a completed JavaScript map
whose managed continuation has not run. Capture deadlines determine acceptance;
the observer adds evidence without submitting GPU work.

The framebuffer qualification also records cumulative geometry upload bytes/count
and per-frame draw, triangle and line counts before and after camera rotation. It
requires an unchanged scene, origin and grid state, an additional rendered frame,
changed pixels and no additional geometry upload. The artifact gate rejects missing
or contradictory samples. `CpuSubmissionMilliseconds` measures CPU command encoding
and queue submission only; it excludes GPU execution and readback. These observations
prove resident-buffer reuse under camera motion, not a physical-GPU frame rate.

The standalone production-shader gate is also mandatory:

```sh
python -m pip install playwright
python -m playwright install chromium
python tests/browser/test_webgpu_pipeline.py --software
```

It compiles and executes the WGSL files embedded by the real renderer, checks
render-pass behavior and readback, and writes `evidence/webgpu-pipeline.json`.
The software adapter verifies API/shader correctness; this smaller scene cannot
replace the application's ten interaction campaigns or a physical-GPU benchmark.
The `webgpu-pipeline` CI job must pass before the publication can qualify.

The artifact gate requires `gpuApi: "WebGPU"`, actual differential framebuffer evidence and an installed observer that saw at least one real GPU device. A wrapper around the native `GPUAdapter.requestDevice` installs listeners on each returned device without replacing it, changing descriptors or suppressing the host's events. Uncaptured validation errors and unexpected device loss fail both the initial modeling report and the final campaign evidence. Only device loss with an explicit `destroyed` reason is ignored because it reports intentional host teardown. Final evidence is sampled after screenshots and retained in `uno-console.json`; a legacy WebGL report or missing observation cannot qualify this migration. Browser qualification preserves all ten mandatory input campaigns. Hosted CI uses Chromium's SwiftShader WebGPU adapter for shader/API/framebuffer correctness. That software adapter is not hardware performance evidence. The launch arguments are retained in `uno-console.json`. For an equivalent local correctness run:

```sh
NURBSSCENE_CHROMIUM_ARGS='["--enable-unsafe-webgpu","--use-angle=swiftshader","--enable-unsafe-swiftshader","--use-vulkan=swiftshader","--enable-features=Vulkan"]' python tests/browser/test_uno.py
```

The software configuration selects SwiftShader consistently for WebGPU, ANGLE and
the Vulkan canvas compositor. Forcing only `--use-webgpu-adapter=swiftshader` can
leave offscreen WebGPU rendering operational while invalidating the instance on
canvas presentation, as recorded in [Chromium issue 575926500](https://issues.chromium.org/issues/575926500).
The first Uno migration run reproduced that exact device-loss message. The
configuration above changes the CI software driver selection and preserves the
actual application, shader, pixel and device-error gates.

Omit `NURBSSCENE_CHROMIUM_ARGS` to test the browser's normal hardware selection. The default is an empty argument list, and no missing-adapter skip is accepted. Serve the browser app over HTTPS or localhost. Native targets require a working WebGPU graphics driver; unsupported targets fail visibly because the application has no secondary raster backend. Uno documents WebGPU as experimental, so successful compilation alone does not certify a device/driver.

Primary migration references: [Uno 7 migration](https://platform.uno/docs/articles/migrating-to-uno-7.html), [WebGPU backend](https://platform.uno/docs/articles/features/using-webgpu-rendering.html), [WebGpuCanvasElement](https://platform.uno/docs/articles/controls/WebGpuCanvasElement.html), [SDK package manifest](https://www.nuget.org/packages/Uno.Sdk/7.0.5), and [Chromium's WebGPU test adapter](https://dawn.googlesource.com/dawn.git/+/refs/heads/main/webgpu-cts/README.md).

Ordinary builds omit the hook. Pages intentionally publishes the exact **qualification-enabled alpha artifact** tested by CI; its checks run only with the explicit `?qualification=1` query. This permits reproducible validation of the deployed alpha without substituting another renderer or application.

## Native/WebAssembly and six JavaScript packages

Install CMake, Clang with its wasm32 linker, Node 22 and the Python test dependencies:

```sh
python -m pip install -r tests/requirements.txt
python -m playwright install chromium
# CHROMIUM_PATH may select a local Chromium executable; otherwise Playwright's bundled browser is used.
bash tools/validate.sh
```

That script builds the native/WASM kernel, runs the native and Node suites, packs six independently installable npm archives, tests a clean consumer, and runs the separate offline harness interaction suite. It does not qualify the Uno application or successful GPU rendering. Generated WASM, fixtures, offline HTML and evidence are reproducible outputs, not source files.

## GitHub Pages

The `pages.yml` workflow runs only after the main branch's Uno qualification succeeds. It checks that the tested commit is still current main, downloads that exact run's qualified artifact, then uses GitHub Pages deployment with its scoped OIDC permissions. It never publishes the JavaScript harness as Uno. Repository Pages must use GitHub Actions as its build source.

The source was reconstructed through Git objects after the local execution environment became unavailable during a browser publication. A successful desktop build of the earlier local checkout is useful API evidence, but does not qualify the reconstructed remote tree. The normal CI workflows must build and execute that exact tree before it can be qualified.

Use per-commit CI artifacts for current evidence. Reports in `docs/history/alpha` describe the original partial alpha and its older environment.

## Section and graph performance evidence

`sections.yml` runs the new numerical/runtime contract suite and then the same
`tests/section-performance` harness sequentially against baseline main
`21ed2c787c26acd533efd385d25b5fd699785e1c` and the candidate. Release builds,
`DOTNET_TieredCompilation=0`, three warm-up calls, and seven measured samples are
used. JSON includes per-operation medians, min/max samples and allocation counts;
CI preserves both JSON files, test output and the tested tree. This is not an FPS,
GPU, cold-start or complete UI benchmark. See `SECTIONS-AND-RUNTIME.md`.

## Workspace UI regressions

The real Uno browser suite also runs `test_workspace_ui.py`, using mouse and keyboard
input against observed native-control and graph-canvas bounds. It retains JSON logs
and right/bottom/narrow screenshots. Presentation benchmarks and allocation contracts
run separately in `presentation.yml`. See `CLASSIC-WORKSPACE.md` for scope and limits.

A deferred native text-input focus handoff can leave the model canvas receiving
keys after a command draft has started. The command console keeps that draft active
during the handoff: Space appends text and Enter submits the pending draft once;
an empty draft retains the repeat-command behavior. The Rhino workspace campaign
types a command and immediately presses Enter without waiting for focus, then
checks ordinary Undo. These two assertions raise its minimum from 56 to 58 and
the ten-campaign UI minimum from 349 to 351.

Graph clipboard/menu/selection scenarios run inside the same actual Uno test via
`test_graph_editing_ui.py`. `editing.yml` separately runs fragment/arrangement
contracts and identical presentation benchmarks against previous main `1d41fc57`.
See `GRAPH-EDITING.md`; performance numbers from the earlier presentation milestone
are not assumed to apply to later source.

## Panel layout and persistent text

```sh
DOTNET_TieredCompilation=0 dotnet run --project tests/dotnet/NurbsScene.PanelLayoutContracts -c Release
DOTNET_TieredCompilation=0 dotnet run --project tests/dotnet/NurbsScene.CanvasInputContracts -c Release
DOTNET_TieredCompilation=0 dotnet run --project tests/dotnet/NurbsScene.TextDraftContracts -c Release
```

`test_panel_layout_ui.py` extends the actual Uno input campaign with resize drafts,
commit/cancel, history, no-op multiline Apply and legacy Text inspector reopening.
Native wire-cache pass counters verify that slider changes skip unchanged wires;
the numerical allocation contracts do not measure full canvas or GPU frame cost.

## Panel layout and canvas review

```sh
dotnet run --project tests/dotnet/NurbsScene.PanelLayoutContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.CanvasReviewContracts -c Release
dotnet run --project tests/panel-review-performance -c Release -- panel-review.json
```

The `panel-layout.yml` workflow compares the same benchmark against PR9 and the
candidate. `test_panel_layout_ui.py` runs in the actual Uno input campaign, not
the standalone JavaScript harness. See `PANEL-LAYOUT-AND-CANVAS-REVIEW.md` for
source recovery, measurement limits and the local browser-policy block.

## Reconciled panel-edge review

```sh
dotnet run --project tests/dotnet/NurbsScene.PanelEdgeContracts -c Release
python -m unittest discover -s tests/browser -p 'test_observation*contracts.py' -v
```

The waiter unit contracts also require Node 22. They execute JS predicates, not
the Uno app. The actual published Uno artifact is qualified separately using
`test_uno.py`: workspace, inputs/sliders, panels and panel-edges are four mandatory
isolated campaigns. None is an optional fallback or a retry of another campaign.
See `RECONCILED-PANEL-REVIEW.md` for local evidence and publication limitations.

## Panel display and bounded Unicode previews

```sh
dotnet run --project tests/dotnet/NurbsScene.PanelDisplayContracts -c Release
DOTNET_TieredCompilation=0 dotnet run --project tests/panel-display-performance -c Release -- panel-display.json
```

`panel-display.yml` retains metadata/history tests, runs the new Unicode and
allocation contracts and compares the identical benchmark with original PR11.
`test_panel_display_ui.py` is the sixth mandatory native-input campaign in the
actual Uno browser runner. See `PANEL-DISPLAY-AND-TEXT-REVIEW.md`.


## Reusable package WebGPU qualification

The npm graphics renderer is WebGPU-only. `auto` selects WebGPU; explicit `webgl2` selection and legacy WebGL2 constructors fail with migration guidance. Shader strings are generated from the Uno renderer's authoritative WGSL files and verified byte for byte by Node contracts. The strict GPU job builds the real NURBS WASM kernel and runs:

```bash
node packages/graphics/generate-shaders.mjs --check
node --test tests/graphics-webgpu.test.mjs
bash tools/build-kernel.sh
python tests/browser/test_gpu.py --software --output evidence/javascript-webgpu.json
```

The package campaign requires real WebGPU frames, differential NURBS surface pixels, camera changes, reverse-Z, shadows, transparency, clipping, rebasing, resize/readback, viewport bloom separation and explicit unsupported-device errors. Shader/API failures cannot fall back to GL. Hosted software rendering verifies correctness; target GPU performance remains a separate measurement. See [the package API guide](../packages/graphics/README.md) for resource ownership and the complete migration contract.
