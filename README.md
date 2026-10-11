# NurbsScene

Independent, modular NURBS modeling and visual programming for **Uno Platform desktop and WebAssembly**.

**This is a working alpha, not a complete Rhino or Grasshopper replacement.** Exact curves and untrimmed tensor-product surfaces are implemented; solid B-rep modeling, full command/component coverage, proprietary file/plugin compatibility and identical UI remain unfinished. See [the current scope](docs/PARITY.md).

The application lives in `src/NurbsScene.App`. The separate `validation-harness` demonstrates the reusable JavaScript packages; it is **not** the Uno application. GitHub Pages deploys only the actual Uno publication after its browser/framebuffer qualification succeeds.

## Reusable architecture

| Package | Responsibility |
|---|---|
| `NurbsScene.Geometry` | Immutable binary64 geometry, cached control bounds, planes and optional span/grid evaluator contracts |
| `NurbsScene.Kernel` | Managed/native evaluators, exact curve/surface editing, numerical arc length and bounded curve queries |
| `NurbsScene.Model` | Documents, cached immutable snapshot views, layers, selection and atomic undoable transactions |
| `NurbsScene.Parametric` | Typed data trees/paths, indexed dependency graph, incremental solving and 108 executable components |
| `NurbsScene.Rendering` | Knot-aware display tessellation, cameras, two-level spatial picking and a depth-enabled GL renderer |
| `NurbsScene.IO` | Versioned exact `.nscene` workspaces and display-only OBJ/STL export |
| `NurbsScene.Workbench` | Reusable application commands, graph preview and baking |
| `NurbsScene.Controls` | Uno viewports, graph editor, inspectors, command console and workspace |

`native/kernel` supplies the freestanding C/WebAssembly numerical kernel. Six independent npm packages in `packages/` provide geometry, model, parametric, graphics, controls and interchange; their catalog contains 102 components and is not identical to the managed catalog. The JavaScript graphics package has WebGPU and WebGL2 implementations. The Uno application's current renderer uses desktop GL / browser WebGL2, **not a direct Uno WebGPU backend**.

## Build and run

Use the SDK versions pinned in `global.json`:

```sh
dotnet workload install wasm-tools
dotnet build src/NurbsScene.App -c Release -f net10.0-desktop -p:NurbsSceneDesktopOnly=true
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm
python tools/stage-uno-site.py
python -m http.server 8766 --bind 127.0.0.1 --directory artifacts/site
```

See [build and validation instructions](docs/BUILD.md) and [performance/review contracts](docs/PERFORMANCE-AND-REVIEW.md). CI preserves evidence for the exact commit; compilation, browser smoke tests, physical-GPU performance and full compatibility are distinct qualifications.

## Modeling and visual programming

Curves and surfaces retain their weighted control points and expanded knot vectors. Display triangles never become the authoritative modeling representation. Surface editing includes U/V knot insertion/refinement, elevation, reversal, reparameterization, splitting, rectangular UV extraction, control-point/weight editing, boundary curves and rational Bezier patches. Curve measurement integrates analytic speed and reports a **numerical error estimate**, not a certified bound.

The managed catalog also includes `CurveClosestPoint`, `CurveControlPoint`, `CurveEndPoints`, `ProjectToPlane` and `MirrorInPlane`. The command console exposes `ClosestPoint x,y,z [tolerance]`, `CurveControlPoint index x,y,z [weight]`, `ProjectToPlane origin normal` and `MirrorPlane origin normal`. Closest-point commands report a result without creating a point object; this is not full Rhino command compatibility. Plane operations transform authoritative rational controls, not a triangle approximation.

The visual editor supports component search, typed wiring, number scrubbing, data-tree operations, preview, exact baking and undo/redo. Typed path values such as `{0;2;10}` survive `.nscene` round trips. Persistent trees retain explicit null items and empty branches. Matching behavior and remaining boundaries are described in [the data-tree contract](docs/DATA-TREES.md).

Rhino and Grasshopper are behavioral references only. No proprietary application source, icons or binaries are included. NurbsScene-specific source is MIT-licensed. Historical alpha reports are retained in `docs/history/alpha`; they describe an earlier environment and are not the current qualification record.
