# Validation record and interpretation

The delivery environment could execute native C, WebAssembly through Node/Chromium, and the JavaScript controls offline. It could not compile .NET, navigate the browser to localhost, or create a usable WebGL2/WebGPU context. No policies were changed to work around these restrictions.

## Recorded passes

| Suite | Result | What it proves / does not prove |
|---|---|---|
| `tests/native/test_kernel.py` | 12 tests passed | Runs the real compiled C shared library. Includes randomized SciPy comparisons, rational derivatives, mixed surfaces, knot insertion/Bezier operations and rejected capacities/inputs. Does not validate C# source. |
| `tests/*.test.mjs` | 60 tests passed | Real WASM numerical wrapper, geometry edits, graph matching/caching/cancellation, document history/interchange, camera/BVH and display preparation. Does not initialize a GPU. |
| `tests/browser/test_offline.py` | 12 tests passed | Real Chromium DOM, custom controls, WASM and graph execution. Covers pointer/keyboard interactions, wiring/dragging, command history, exact baking, visibility, imports and responsive layout. **Not the Uno app.** |
| `tools/check-source-layout.py` | Passed | Parses project/XAML XML, validates project/import paths and checks ESM syntax. **Not a C# compiler.** |
| Isolated npm package consumption | See `evidence/npm-consumer.log` | Installs six generated tarballs in an independent consumer and executes the packaged WASM/graph. No package registry publication. |

`evidence/verification.json` and the accompanying logs are the machine-readable delivery record. Screenshots are actual offline Chromium output, including the unavailable-GPU overlay. There is no rendered-solid screenshot disguised as a successful GPU test.

## Gates not passed in this delivery

**Managed build and tests.** No `dotnet`, `csc` or Mono compiler was available, and dependency download attempts did not succeed. Eight managed libraries and the Uno host are source, not validated binaries. `tests/dotnet/NurbsScene.CoreTests` has analytic checks plus 72 deterministic SciPy curve fixtures (360 sampled derivative sets); `NurbsScene.IntegrationTests` covers managed graph/interchange/display preparation. Those suites were authored but not executed.

**Actual GPU execution.** Offline Chromium could not create a WebGL2 context and did not expose WebGPU in that context. HTTP navigation failed with `ERR_BLOCKED_BY_ADMINISTRATOR`. The retained shader and GPU implementations have not been runtime-qualified. `tests/browser/test_gpu.py` requires both real requested backends and differential framebuffer output; absence fails rather than skips. That test was not run in the restricted browser context.

**Real Uno browser.** `tests/browser/test_uno.py` runs only a published Uno build with the opt-in native qualification hook. It requires a real GL frame, exact modeling/graph changes and framebuffer differences when modeled surfaces are removed/restored. It has not been executed. It is an integration smoke test, not a replacement for accessibility-driven input tests, screenshot comparisons or a full feature suite.

**Desktop hosts and professional qualification.** No Windows/macOS/Linux Uno execution, hardware performance/power measurements, device-loss stress, huge-model tests, trimmed-solid tolerance corpus, reference file/plugin compatibility or original-UI comparison passed here. There is no claim that the entire functionality was validated.

**Publication.** GitHub reads worked, but the first write was blocked. No remote CI result, commit, PR, release, merge or Pages site exists as a result of this work. Authored workflow files are not execution evidence.

## Reproduction and release order

Run `bash tools/validate.sh` for the available Linux numerical/module/offline subset. Install the recorded Python/Chromium dependencies and set `CHROMIUM_PATH` as needed. This command prints its limited scope even on success.

Run the managed CoreTests and IntegrationTests under .NET 10, then build desktop and publish browser. Do not suppress compiler/restore errors or claim a project-file check is sufficient. Serve the actual `publish/wwwroot` content (or the publish root where applicable), run `test_uno.py`, and retain browser diagnostics plus a screenshot of that Uno application.

For the separate ESM backend implementations, serve the repository and run `test_gpu.py`. Repeat the graphics tests across hardware vendors, software adapters, driver/API versions, display scaling, context-loss/recovery, clipping, transparency and large-scene budgets. Software-adapter success tests API correctness only; it is not a hardware-performance certificate.

The Pages workflow is manual and fail-closed. It stages only real .NET publication output, never substitutes the JavaScript harness, and cannot deploy after failed build or Uno GPU smoke steps. An eventual successful alpha deployment still must not be labeled full Rhino/Grasshopper parity.
