# Building and qualifying NurbsScene

The authoritative application is `src/NurbsScene.App`, built with the pinned Uno SDK in `global.json`. The JavaScript workspace under `validation-harness` is a separate consumer of the reusable JavaScript packages, not a substitute Uno application.

## Managed desktop and browser application

Install a .NET 10 SDK compatible with `global.json` and the WebAssembly workload:

```sh
dotnet workload install wasm-tools
dotnet build src/NurbsScene.App -c Release -f net10.0-desktop -p:NurbsSceneDesktopOnly=true
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm
python tools/stage-uno-site.py
python -m http.server 8766 --bind 127.0.0.1 --directory artifacts/site
```

Desktop compilation is not a substitute for running each supported desktop platform. Browser builds use the real managed kernel, graph engine, reusable Uno controls and GL renderer.

## Numerical and document tests

The two independent SciPy fixture generators run only during testing; the application has no SciPy dependency.

```sh
python -m pip install 'numpy>=1.26,<3' 'scipy>=1.12,<2'
mkdir -p tests/fixtures
python tools/generate-oracle-fixtures.py
python tools/generate-length-oracle.py
dotnet run --project tests/dotnet/NurbsScene.CoreTests -c Release
dotnet run --project tests/dotnet/NurbsScene.MeasurementTests -c Release
dotnet run --project tests/dotnet/NurbsScene.IntegrationTests -c Release
dotnet run --project tests/dotnet/NurbsScene.WorkbenchTests -c Release
```

`CurveLength` integrates analytic NURBS speed with adaptive Gauss–Kronrod quadrature. Its reported error is an estimate, not a certified geometric error bound. `DivideCurve` uses inverse arc length; `DivideParameter` deliberately remains uniform parameter sampling. Surface trim commands extract rectangular parameter domains, not arbitrary B-rep trim loops.

## Actual GPU browser qualification

```sh
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm -p:NurbsSceneQualification=true
python tools/stage-uno-site.py
python -m pip install playwright
python -m playwright install chromium
# Serve artifacts/site on port 8766 in a separate terminal.
python tests/browser/test_uno.py
```

The opt-in build hook checks managed commands, undo/redo, parametric edits, exact baking, workspace round-trip, four viewports, and differential readback of actual GL surface pixels. A missing GPU context or failed framebuffer comparison fails the test. The hook is omitted from normal builds. It is a smoke qualification, not proof of full UI, hardware, performance or Rhino/Grasshopper compatibility.

The pinned Uno browser GL package requires its existing RGBA readback/channel-conversion path instead of a BGRA call through the native GLES resolver. `UnoGlReadback` makes that boundary explicit and rejects an unknown framework contract. Browser rendering does not depend on the optional debug-renderer extension. Pre-existing shared-context GL errors are recorded separately from errors introduced by NurbsScene; context loss and application submission errors are still failures.

## Native C / WebAssembly and JavaScript packages

The freestanding C kernel is built with CMake and Clang's wasm32 target. Generated WebAssembly and offline HTML are build outputs, not alternate authoritative geometry.

```sh
npm ci --ignore-scripts
bash tools/build-kernel.sh
python -m unittest discover -s tests/native -v
npm test
node tools/pack-javascript.mjs
node tools/test-npm-consumer.mjs
python tools/build-offline-harness.py
python tests/browser/test_offline.py
```

The source transfer is still being completed on the feature branch. Historical alpha reports are preserved under `docs/history`; use current CI run artifacts for results at the exact commit being evaluated. Full Rhino/Grasshopper parity, solid B-rep modeling, proprietary interchange/plugin compatibility and production qualification remain incomplete.
