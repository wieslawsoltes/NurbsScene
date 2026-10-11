# NurbsScene delivery verification

**Version:** 0.1.0-alpha.1. **Status: partial local implementation, not full Rhino/Grasshopper parity.**

The source and browser harness are separate products: the self-contained HTML validates JavaScript/WebAssembly modules. It is **not** a compiled Uno application.

## Executed successfully

| Check | Result |
|---|---|
| Native C numerical kernel | 12 tests passed |
| Node + real WASM geometry/graph/model/CPU graphics modules | 60 tests passed |
| Offline Chromium pointer/keyboard/control scenarios | 12 tests passed |
| Independently installed npm archives | All six passed the isolated consumer test |
| Structural XML/XAML/project/ESM checks | Passed; 11 project files checked |

The browser scenarios cover graph sliders and incremental exact geometry, node dragging, port wiring, deletion/selection, undo/redo, commands, baking, layers, imports and responsive layout. Native numerical tests compare against analytic results and independent SciPy BSpline calculations. The C# evaluator is a separate implementation and did not run.

## Not completed or not verified

No .NET SDK/compiler was available, so the eight managed library projects and the Uno desktop/browser host are **uncompiled source**. The supplied C# tests, desktop build, actual Uno browser and GL framebuffer smoke test have not run. Managed NuGet binaries are not included.

Offline Chromium could not create WebGL2 and did not expose WebGPU in the tested context. Localhost browser navigation was blocked by browser policy. GPU source is included, but no successful actual shader/framebuffer qualification is claimed. The screenshot retains the genuine unavailable-GPU banner. There is no fake renderer or mocked adapter result.

B-rep topology/trimmed solids, booleans, fillets, healing, complete modeling commands, all Grasshopper components/semantics/plugins, .3dm/.gh/.ghx compatibility, identical UI and production rendering are not complete. Open CASCADE was investigated but is **not integrated or shipped**.

GitHub repository reads worked; the first write was blocked by the platform. No remote commit, PR, CI run, release, merge or GitHub Pages deployment resulted. Workflow YAML is included as source, not evidence of execution.

## Files and reproduction

- `NurbsScene-source.zip`: all tracked source, tests, documentation and raw validation evidence; also contains the compiled WASM and offline harness.
- `NurbsScene-packages.zip`: six installable npm tarballs, the Linux x86_64 native shared library, C header and WASM kernel. Not precompiled managed packages.
- `NurbsScene-offline.html`: standalone module/control harness; browser GPU capability restrictions still apply.
- `NurbsScene-history.bundle`: local Git history. No commits in this bundle were pushed.
- `NurbsScene-SHA256SUMS.txt`: checksums for delivered archives and evidence files.

Unpack source; from its root run `bash tools/validate.sh` with Node 22+, CMake/C compiler/Clang/WASM linker, Python dependencies from `tests/requirements.txt`, and Chromium installed. This executes only the available validation subset. README documents the separate .NET and real GPU build/test gates.

**Source revision:** `275615d13f19ee511c8a4a52c2f724f40364533d`. Evidence-only files were committed after this source revision; the source manifest in `evidence/` records each tested source file by SHA-256.

Generated: 2026-10-09T22:50:58.869524+00:00. Raw results and detailed remaining scope are in `evidence/verification.json`, `docs/VALIDATION.md` and `docs/PARITY.md`.
