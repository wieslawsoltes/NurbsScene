# Compatibility inventory

NurbsScene is an independent alpha, not full or pixel-identical Rhino/Grasshopper
parity. Tests establish the recorded contracts, not complete product compatibility.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Exact geometry | Immutable positive-weight rational curves and tensor-product surfaces; expanded knots, analytic derivatives and cached bounds | Zero/negative projective weights, complete periodic/disconnected editing and every extreme-conditioning case |
| Curves | Lines, polylines, arcs/circles, planar ellipses, interpolation, refinement, elevation, split/trim, control/weight edits, bounded closest-point and plane sections | Complete joining, curve/curve and surface intersections, offsets, blending, fitting and original command semantics |
| Surfaces | Extrusion, revolution, compatible section lofting, isocurves, U/V editing, rectangular extraction, Bezier patches | Arbitrary trim loops, robust intersections, sweep/network/patch variants, seam alignment, all loft styles and periodic/closed lofts |
| Solids | No integrated solid-modeling backend | B-rep topology, booleans, fillets/chamfers, healing, sewing and shells; OCCT is not integrated |
| Measurements | Analytic-speed numerical length, inverse/equal-length sampling, surface area/centroid, cancellation/work limits | Certified bounds, arbitrary-trim integration, inertia, second moments and solid volume |
| Transforms | Translation, rotation, scale, plane projection/reflection of exact controls | Complete gumball, CPlane, constraints and original tool behavior |
| Parametric engine | 114 managed/102 JavaScript components, typed trees/paths, indexed DAG, bounded matching, incremental plans/results, cancellation and stale-result rejection | Complete catalog, clusters, scripting/plugins, all conversion and matching rules |
| Documents | Stable object/layer/pick order, immutable views, locking, selection, transactional undo/redo | Full object types, blocks, references, annotations, construction history and large-project qualification |
| Interchange | Exact managed `.nscene`, separate JS dialect, display OBJ/STL | `.3dm`, `.gh`, `.ghx`, STEP/IGES and plugin compatibility; STL is not a solid certificate |
| Uno UI | Modeling-first empty startup, compact toolbars/icons, four viewports, optional definition docking, persistent keyboard splitters, selectable command history/recall/repeat, anchored modifier navigation, atomic camera properties, layer status menus and grid/ortho/end snaps | Identical visual/functional parity, full docking, Osnap/CPlane/gumball, accessibility and input-device matrix |
| Graph UI | Typed wires, shelves/search, clipboard/duplicate, modifier selection, constrained dragging, anchored right-drag zoom, arrangement, numeric range/precision editing, all-edge panel resize, indexed item scrolling, paths/indices/wrap and font/style/alignment metadata | Long-item visual-line scrolling, measured paragraph wrapping, complete paragraph bidi, font fallback and shaping coverage, every original component interaction |
| Rendering | Uno 7 WebGPU compositor and custom canvases; shared-device WGSL HDR passes, multisample antialiasing, reverse-Z depth, physically based shading, filtered shadows, weighted transparency, curve ribbons, bloom/tone mapping, knot-aware meshes, spatial picking and asynchronous capture | Certified adaptive tessellation, full material/texture workflow, global illumination, temporal reconstruction, representative AAA scene qualification and physical-GPU/driver performance matrix |
| Other systems | Not implemented | SubD, complete meshing, rendering plugins, layouts/printing and scripting ecosystem |

## Qualification boundaries

The geometry suites use analytic references, native/managed comparisons and
independent SciPy fixtures. Closest-point and integration errors are numerical
estimates, not proofs of global minima or certified bounds. Rectangular UV trims
are not arbitrary B-rep trimming loops. Exact primitive/editing tests do not qualify
unimplemented solid operations.

The real Uno runner retains managed modeling and differential framebuffer checks
in every isolated input campaign. Workspace, inputs/sliders, panels, panel edges,
ellipse/loft, panel display, typography, pointer selection, panel scrolling and
Rhino-style modeling-workspace interactions are all mandatory. See the
[full workspace review](RHINO-UI-REVIEW.md) for current shell behavior and limits.
See [panel item scrolling](PANEL-SCROLLING.md) for the integrated input and raster scope.
See [current navigation/input scope](CANVAS-NAVIGATION-AND-HIT-TESTING.md) and the
[historical canvas input report](CANVAS-INPUT-REVIEW.md). Repeated modeling
checks across campaigns are not distinct features. Software WebGPU can satisfy these
checks; no physical-GPU performance conclusion follows. A separate mandatory job
compiles and executes the production WGSL passes; it supplements the real Uno
campaigns and cannot replace them. Desktop compilation is not
execution across every desktop OS/device. The separate JavaScript harness and
observer unit tests do not replace the Uno application's acceptance tests.

The application draws retained text through Uno's managed font/shaping interfaces
and WebGPU surfaces. Bundled DejaVu regular, bold, oblique and bold-oblique fonts
provide an offline fallback and carry their own license. Host font availability
can still affect the resolved family. Shaped glyph runs do not establish complete
mixed-direction paragraph layout, per-character fallback or every script's visual
quality. Wrapping retains the existing bounded column approximation.

The standalone typography raster fixture retains a compile-time SkiaSharp branch
for the previous layout/cache contracts. Those CPU pixels and timings do not
qualify the new WebGPU output; actual Uno typography and framebuffer campaigns do.
See the [migration journal](UNO7-WEBGPU-MIGRATION.md) for exact architecture and
validated revisions. The independent npm graphics package now uses the same production
WGSL shader sources and WebGPU rendering passes. Its former WebGL2 entry points only
throw migration guidance; they contain no GL renderer or automatic fallback. The npm
validation harness retains ordinary DOM/SVG controls and remains separate from the Uno app.

Use the exact commit and tree in CI artifacts for qualification status. Historical
reports describe their recorded revisions, including earlier local-only blockers,
not the newest main. Performance harnesses retain raw timing/allocation samples
and their workload scopes; CPU solver/preview results are not FPS, GPU or complete
application speedups. See [typography](PANEL-TYPOGRAPHY.md),
[panel display](PANEL-DISPLAY-AND-TEXT-REVIEW.md), [loft](ELLIPSE-AND-LOFT.md),
[sections](SECTIONS-AND-RUNTIME.md), [surface analysis](SURFACE-ANALYSIS.md),
[data trees](DATA-TREES.md) and [build instructions](BUILD.md).
