# NurbsScene

Independent NURBS modeling and visual programming for **Uno Platform desktop and
WebAssembly**, split into reusable libraries and controls.

**Working alpha, not a complete Rhino or Grasshopper replacement.** The application
uses exact positive-weight rational curves and untrimmed tensor-product surfaces;
robust solids, arbitrary trims, proprietary formats/plugins and complete UI parity
remain unfinished. See [the compatibility inventory](docs/PARITY.md).

## Reusable packages

| Package | Responsibility |
|---|---|
| `NurbsScene.Geometry` | Immutable binary64 geometry, bounds, planes and evaluator contracts |
| `NurbsScene.Kernel` | Managed/native evaluators, exact editing, ellipse/loft, numerical measurements and bounded queries |
| `NurbsScene.Model` | Ordered documents, layers, selection, immutable views and transactional history |
| `NurbsScene.Parametric` | Typed paths/trees, indexed dependencies, incremental solving and 114 executable components |
| `NurbsScene.Rendering` | Knot-aware tessellation, cameras, hierarchical picking and depth-enabled GL rendering |
| `NurbsScene.IO` | Exact versioned `.nscene` and display-only OBJ/STL export |
| `NurbsScene.Workbench` | Application commands, preview, exact baking and workspace lifecycle |
| `NurbsScene.Presentation` | Platform-independent input, layout, editing and measured text placement |
| `NurbsScene.Controls` | Reusable Uno viewports, graph canvas, native dialogs, inspectors and workspace |

The host is `src/NurbsScene.App`. `native/kernel` supplies a freestanding C/WASM
numerical kernel. Six separate npm packages in `packages/` provide geometry, model,
parametric, graphics, controls and interchange, with a 102-component catalog.
`validation-harness` exercises these JavaScript packages; **it is not the Uno app**.
JavaScript graphics has WebGPU and WebGL2 implementations. Uno currently uses
desktop GL/browser WebGL2, **not direct Uno WebGPU**.

## Build and validate

Use the versions pinned in `global.json`:

```sh
dotnet workload install wasm-tools
dotnet build src/NurbsScene.App -c Release -f net10.0-desktop -p:NurbsSceneDesktopOnly=true
dotnet publish src/NurbsScene.App -c Release -f net10.0-browserwasm
python tools/stage-uno-site.py
python -m http.server 8766 --bind 127.0.0.1 --directory artifacts/site
```

[Build instructions](docs/BUILD.md) describe native, managed, package and browser
qualification. GitHub Pages deploys only the exact Uno artifact that passed the
main-branch browser/framebuffer workflow. Every new revision needs its own tests;
compilation, software-GL browser checks and physical-GPU performance are distinct.

## Modeling and visual programming

The compact modeling-first workspace starts with an empty document, four viewports,
tabbed icon tools, selectable command history, native camera/object/layer properties,
persistent keyboard-accessible splitters and an optional right/bottom-docked definition.
Model-canvas typing starts command entry; short right-click/Space repeat, modifier
navigation, cursor-anchored zoom and camera-draft validation use the actual controls.
See the [Rhino-style UI/UX review](docs/RHINO-UI-REVIEW.md) for the delivered behavior,
qualification requirements and remaining differences.
The graph editor provides executable search, typed wiring, numeric sliders,
copy/paste/duplicate, arrangement, preview, baking and undo/redo. Authored icons and
controls follow familiar conventions without claiming identical original artwork.

Graph-canvas selection uses Shift to add, Control to remove and Control+Shift to toggle.
Shift-drag follows 45-degree guides; Control+right-drag zooms about its starting
point. Escape cancels a selection, move or navigation draft. See
[canvas navigation and input](docs/CANVAS-NAVIGATION-AND-HIT-TESTING.md).

Panels support multiline text, connected data, virtualized item scrolling, all-edge resizing, persistent
path/index/wrap options, and a native typography dialog (context menu or
Ctrl+double-click). Font/style/alignment are display-only metadata; bounded native
text caches reuse glyphs and measurements. See [typography](docs/PANEL-TYPOGRAPHY.md)
[item scrolling](docs/PANEL-SCROLLING.md), and [Unicode/display options](docs/PANEL-DISPLAY-AND-TEXT-REVIEW.md) for limits.

Curves/surfaces retain homogeneous controls and knots; display meshes never replace
the modeling representation. Modeling includes rational ellipses, interpolation,
extrusion, revolution, shared-factor lofting, knot refinement/elevation, rectangular
UV extraction and control editing. Numerical length, closest-point, plane-section,
area and centroid APIs report estimates/work limits, not certified error bounds.

Further contracts: [geometry and loft](docs/ELLIPSE-AND-LOFT.md),
[sections/runtime](docs/SECTIONS-AND-RUNTIME.md), [area/native](docs/SURFACE-ANALYSIS.md),
[data trees](docs/DATA-TREES.md), [document order](docs/DOCUMENT-ORDER.md),
[workspace](docs/CLASSIC-WORKSPACE.md), [graph editing](docs/GRAPH-EDITING.md),
[sliders](docs/SLIDERS-AND-SNAPSHOT-REVIEW.md),
[performance/review](docs/PERFORMANCE-AND-REVIEW.md).

Rhino and Grasshopper are behavioral references. No proprietary source, icons or
binaries are included. NurbsScene-specific source is MIT-licensed. Historical
reports under `docs/` retain the evidence and restrictions of their recorded
revisions; they do not override the current per-commit workflow results.
