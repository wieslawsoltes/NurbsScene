# NurbsScene

Independent NURBS modeling and visual programming for **Uno Platform 7 desktop and
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
| `NurbsScene.Rendering` | Knot-aware tessellation, cameras, hierarchical picking and a shared-device WebGPU HDR renderer |
| `NurbsScene.IO` | Exact versioned `.nscene` and display-only OBJ/STL export |
| `NurbsScene.Workbench` | Application commands, preview, exact baking and workspace lifecycle |
| `NurbsScene.Presentation` | Platform-independent input, layout, editing and measured text placement |
| `NurbsScene.Controls` | Reusable Uno viewports, graph canvas, native dialogs, inspectors and workspace |

The host is `src/NurbsScene.App`. `native/kernel` supplies a freestanding C/WASM
numerical kernel. Six separate npm packages in `packages/` provide geometry, model,
parametric, graphics, controls and interchange, with a 102-component catalog.
`validation-harness` exercises these JavaScript packages; **it is not the Uno app**.
The Uno application explicitly selects the **WebGPU composition backend** on desktop
and WebAssembly. Its 3D viewport, graph canvas, overlays, icons and retained text
share Uno's device and queue; normal presentation stays on the GPU. The separate
JavaScript graphics package also uses WebGPU, with the same production WGSL shaders and
HDR pipeline. WebGL2 rendering and automatic fallback have been removed; unsupported
browsers receive an explicit capability error. See its [API migration guide](packages/graphics/README.md).

## Uno 7 and WebGPU rendering

The migration pins Uno.Sdk 7.0.5 / Uno Platform 7.0.32 and removes the Uno 6 GL
viewport and private readback adapter. WGSL shaders provide a linear HDR pipeline,
multisample antialiasing, reverse-Z depth, physically based surface shading,
shadow filtering, baked HDR image-based lighting, order-independent transparency, pixel-width curves and a
bloom/tone-mapping pass. Renderer-owned resources are reused across frames and
rebuilt when the target changes. Explicit captures use asynchronous staging-buffer
readback; displayed frames do not round-trip through CPU memory.

Use **Tools → Rendering…** to adjust and persist exposure, material roughness/metallic,
ambient/key lighting, shadows and bloom. Both renderers share the same authored
[HDR studio environment](docs/WEBGPU-ENVIRONMENT.md), including GGX-prefiltered
reflections, diffuse spherical harmonics and a preintegrated BRDF lookup.

The graph and UI drawing adapter uses Uno's managed drawing interfaces and WebGPU
surfaces, with retained shaped text and bundled DejaVu fonts for offline fallback.
Read the [migration journal](docs/UNO7-WEBGPU-MIGRATION.md) for architecture,
resource ownership, per-commit evidence and remaining quality work. Production
WGSL execution and all eleven actual Uno input/framebuffer campaigns are required
by CI, with a combined minimum of 414 native UI checks. The original ten campaigns
retain their 357 prior checks and add seven real-startup/command-layout checks;
`rhino-grasshopper-ui` contributes another 50.
Hardware performance and an AAA production quality claim require their own
representative scenes, visual review and device/driver measurements.

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
compilation, software-WebGPU correctness checks and physical-GPU performance are distinct.
The two persistence campaigns retain separate pre-reload GPU checkpoints,
`uno-rhino-pre-reload-gpu.json` and `uno-rhino-grasshopper-pre-reload-gpu.json`;
both must pass so a new device after reload cannot hide earlier interaction errors.
The Rhino/Grasshopper interface implementation passed all 23 workflows and 407
native UI checks. See its [implementation, actual captures and acceptance record](docs/RHINO-GRASSHOPPER-UI.md)
for the exact accepted source, artifact hashes and the documentation-only follow-up.
The subsequent [command-area refinement](docs/RHINO-COMMAND-AREA.md) passed all 23
workflows and 414 native UI checks, including real empty-startup captures and
native command text, selection, clipboard and draft-recall validation.

## Modeling and visual programming

The compact modeling-first workspace starts with an empty document, four viewports,
tabbed icon tools, a category-driven modeling sidebar, selectable command history,
native camera/object/layer properties, and persistent keyboard-accessible splitters.
The definition editor can dock right or below the viewports, or float in a movable,
resizable window with a folding title bar and saved placement. Compact inspector
tabs expose aligned properties and a filterable layer table with actual current,
visibility, lock and display-color controls. The definition editor adds grouped
component palettes, retained canvas appearance settings and native workspace menus.
Model-canvas typing starts command entry; short right-click/Space repeat, modifier
navigation, cursor-anchored zoom and camera-draft validation use the actual controls.
See the [Rhino/Grasshopper interface implementation](docs/RHINO-GRASSHOPPER-UI.md)
and [command-area fidelity refinement](docs/RHINO-COMMAND-AREA.md)
for the current behavior and acceptance requirements, and the earlier
[Rhino-style UI/UX review](docs/RHINO-UI-REVIEW.md) for that milestone's evidence and differences.
The graph editor provides executable search, typed wiring, numeric sliders,
copy/paste/duplicate, arrangement, preview, baking and undo/redo. Authored icons and
controls follow familiar conventions without claiming identical original artwork.

Graph-canvas selection uses Shift to add, Control to remove and Control+Shift to toggle.
Shift-drag follows 45-degree guides; Control+right-drag zooms about its starting
point. Escape cancels a selection, move or navigation draft. See
[canvas navigation and input](docs/CANVAS-NAVIGATION-AND-HIT-TESTING.md).

Panels support multiline text, connected data, virtualized item scrolling, all-edge resizing, persistent
path/index/wrap options, and a native typography dialog (context menu or
Ctrl+double-click). Font/style/alignment are display-only metadata; bounded text
caches reuse shaped glyph runs and measurements. See [typography](docs/PANEL-TYPOGRAPHY.md)
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
binaries are included. NurbsScene-specific source is MIT-licensed; bundled DejaVu
fonts retain their [upstream license](src/NurbsScene.Controls/Drawing/Fonts/LICENSE-DejaVu.txt).
Historical
reports under `docs/` retain the evidence and restrictions of their recorded
revisions; they do not override the current per-commit workflow results.
