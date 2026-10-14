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
| Uno UI | Compact toolbars/icons, four viewports, right/bottom definition docking, command recall, grid/ortho/end snaps, native inspectors and dialogs | Identical visual/functional parity, full docking, Osnap/CPlane/gumball, accessibility and input-device matrix |
| Graph UI | Typed wires, shelves/search, clipboard/duplicate, modifier selection, constrained dragging, anchored right-drag zoom, arrangement, numeric range/precision editing, all-edge panel resize, indexed item scrolling, paths/indices/wrap and font/style/alignment metadata | Long-item visual-line scrolling, measured paragraph wrapping, full script shaping/bidi/glyph fallback, every original component interaction |
| Rendering | GL/WebGL2 depth rendering, shared-state restoration, knot-aware meshes, origin re-encoding, spatial picking, clip-aware selection; separate JS WebGPU | Direct Uno WebGPU, certified adaptive tessellation, production materials/lighting/transparency and full hardware matrix |
| Other systems | Not implemented | SubD, complete meshing, rendering plugins, layouts/printing and scripting ecosystem |

## Qualification boundaries

The geometry suites use analytic references, native/managed comparisons and
independent SciPy fixtures. Closest-point and integration errors are numerical
estimates, not proofs of global minima or certified bounds. Rectangular UV trims
are not arbitrary B-rep trimming loops. Exact primitive/editing tests do not qualify
unimplemented solid operations.

The real Uno runner retains managed modeling and differential framebuffer checks
in every isolated input campaign. Workspace, inputs/sliders, panels, panel edges,
ellipse/loft, panel display, typography, pointer selection and panel scrolling are all mandatory.
See [panel item scrolling](PANEL-SCROLLING.md) for the integrated input and raster scope.
See [current navigation/input scope](CANVAS-NAVIGATION-AND-HIT-TESTING.md) and the
[historical canvas input report](CANVAS-INPUT-REVIEW.md). Repeated modeling
checks across campaigns are not distinct features. Software GL can satisfy these
checks; no physical-GPU performance conclusion follows. Desktop compilation is not
execution across every desktop OS/device. The separate JavaScript harness and
observer unit tests do not replace the Uno application's acceptance tests.

Typography raster tests link the actual renderer source to SkiaSharp 3.119.2 and
exercise real native pixels, clipping and cache eviction. A text blob uses simple
character-to-glyph mapping; complete grapheme segmentation does not imply full
font shaping or fallback. Font selection can be substituted by the host. No font
binaries are embedded or exported by the feature.

Use the exact commit and tree in CI artifacts for qualification status. Historical
reports describe their recorded revisions, including earlier local-only blockers,
not the newest main. Performance harnesses retain raw timing/allocation samples
and their workload scopes; CPU solver/preview results are not FPS, GPU or complete
application speedups. See [typography](PANEL-TYPOGRAPHY.md),
[panel display](PANEL-DISPLAY-AND-TEXT-REVIEW.md), [loft](ELLIPSE-AND-LOFT.md),
[sections](SECTIONS-AND-RUNTIME.md), [surface analysis](SURFACE-ANALYSIS.md),
[data trees](DATA-TREES.md) and [build instructions](BUILD.md).
