# Build and validation

`global.json` pins the Uno SDK and a compatible .NET 10 SDK range. The preserved source import is complete. Temporary transfer and dependency-cache workflows have been removed; their prior commits remain in Git history.

## Real Uno application

```sh
dotnet workload install wasm-tools
dotnet build src/NurbsScene.App -c Release -f net10.0-desktop -p:NurbsSceneDesktopOnly=true
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm
python tools/stage-uno-site.py
python -m http.server 8766 --bind 127.0.0.1 --directory artifacts/site
```

`src/NurbsScene.App` contains the host; reusable controls and modeling/application logic live in separate projects. Desktop compilation does not substitute for running every desktop platform.

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

The opt-in hook exercises real managed commands, undo/redo, graph changes, exact baking, workspace and typed-path round trips, four viewports and differential GL framebuffer readback. Seven additional checks cover curve-plane sections, overlap reporting, the new components, execution-plan/result reuse, definition serialization, and application-controller splitting/undo. No GPU mocks or skip-on-unavailable path exists. It is smoke qualification, not complete UI, hardware or performance coverage.

The pinned Uno 6.7.135 browser GL adapter selects the framework's existing RGBA readback/channel-conversion path and rejects an unknown framework contract. Browser drawing does not require the optional debug-renderer extension. Inherited shared-context GL errors are recorded separately; context loss and NurbsScene submission errors still fail.

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
input against observed native-control and Skia-canvas bounds. It retains JSON logs
and right/bottom/narrow screenshots. Presentation benchmarks and allocation contracts
run separately in `presentation.yml`. See `CLASSIC-WORKSPACE.md` for scope and limits.

Graph clipboard/menu/selection scenarios run inside the same actual Uno test via
`test_graph_editing_ui.py`. `editing.yml` separately runs fragment/arrangement
contracts and identical presentation benchmarks against previous main `1d41fc57`.
See `GRAPH-EDITING.md`; performance numbers from the earlier presentation milestone
are not assumed to apply to later source.
