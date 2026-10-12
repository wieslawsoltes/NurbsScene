# Current implementation and compatibility boundaries

This inventory includes the preserved-source import, typed data trees, curve-query additions, curve-plane sections, and graph-performance review through PR #10. It does not claim full Rhino/Grasshopper parity.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Exact geometry | Positive-weight rational curves and tensor-product surfaces; expanded knots; analytic derivatives; optional span/grid evaluators and cached bounds | Negative/zero projective weights, full periodic/disconnected editing coverage and every extreme-conditioning case |
| Curve modeling | Lines, polylines, arcs/circles, interpolation, insertion, split/trim, elevation, control/weight editing, closest-point and numerical curve-plane queries, balanced multi-splitting | Full joining, curve/curve and surface intersections, offsets, blending, fitting and Rhino command behavior; query distance gaps are numerical estimates |
| Surface modeling | Extrusion, revolution, loft, exact isocurves; U/V refinement/elevation/split/reversal; rectangular extraction; control/weight edits; Bezier patches | Arbitrary trimming loops, robust surface intersections, sweep/network/patch variants and full editing semantics |
| Transforms | Translation, rotation, scale, affine plane projection and reflection of authoritative controls | Complete transform tools, gumball constraints, construction planes and command semantics |
| Solids | No integrated solid-modeling backend | B-rep topology, booleans, fillets/chamfers, healing, sewing, shells and production robust predicates. OCCT is not integrated |
| Measurements | Analytic surface area/centroid with work limits and numerical estimates; analytic-speed adaptive Gauss-Kronrod length, inverse arc length and equal-distance division with cancellation/work limits | Error estimates are not certified bounds; disconnected full-multiplicity length requests fail explicitly |
| Parametric engine | 113 managed / 102 JavaScript definitions, typed ports, persistent trees/paths, bounded matching, cycle rejection, cached execution plans/results, batched edit notifications, cancellation and stale-solve rejection | Complete Grasshopper catalog, clusters, scripting/plugin compatibility and all matching/conversion rules |
| Documents | Cached immutable snapshot views, layers, locking, selection, undo/redo, atomic editing and explicit invalid-input errors | Full Rhino object types, blocks, history, reference files, annotations and large-project qualification |
| Interchange | Managed exact `.nscene`; separate JS workspace dialect; OBJ/STL display export | `.3dm`, `.gh`, `.ghx`, STEP/IGES and plugin interchange. Display STL is not a watertight-solid certificate |
| Uno UI | Compact tabbed toolbars/panels, authored vector icons, viewport activation/maximization, right/bottom definition docking, component shelves, command recall and world-aligned grid/ortho/endpoint construction; existing graph wiring, baking and file pickers | Identical Rhino/Grasshopper visuals/behavior, complete docking/toolbars/accessibility/input-device coverage |
| Rendering | GL/WebGL2 depth rendering, shared-state restoration, knot-aware surface sampling, origin re-encoding, hierarchical picking and clip-aware selection; separate JS WebGPU source | Direct Uno WebGPU, certified adaptive surface tessellation, production materials/lighting/transparency, full clipping and hardware/performance matrix |
| Other Rhino systems | Not implemented | SubD, full meshing, rendering plugins, layouts/printing, scripting ecosystem and remaining product features |

## Validation scope

Numerical tests compare the native/managed kernel to analytic results and independent SciPy fixtures. Closest-point references minimize many intervals independently; they are not certified global-minimum oracles. Tests compare distances and enforce work budgets. The graph suites include sparse paths, empty branches, serialization, dependency changes, cancellation and a 2,001-node incremental graph.

The performance suite compares one identical harness against baseline `f0bd8e3` and the candidate on one runner. Structural tests separately check allocation and traversal budgets. The 19 review regressions cover clipping, tiny viewports, cache identity/record cloning/hash stability, narrow knot spans, grid input limits, immutable document views, selected-workspace refresh allocations and ValueTask transaction rejection. These are bounded checks, not proof of every possible input or a complete product review. See `PERFORMANCE-AND-REVIEW.md`.

The real Uno browser suite compiles and runs the application and compares actual GL framebuffer pixels against an empty scene and after camera motion. It exercises managed commands, undo/redo, graph edits, baking, workspace and typed-path round trips. The new query APIs and review contracts are additionally tested in managed .NET CI; the browser smoke test does not exercise every new API or command. Software GL may satisfy the browser check; no physical-GPU performance claim follows.

The section suite adds 28 contracts for forward extraction, rational plane roots/overlaps/uncertainty, balanced splitting, two executable components, commands, branch lookup, plan/result reuse, definition serialization, and edit/history isolation. Its independent benchmark harness compares graph/tree operations against the previous main `21ed2c7`, not the older geometry baseline. The new extraction cache is used by plane queries only; public Bezier decomposition and the pre-existing display/measurement paths are unchanged. No automatic plane-splitting wrapper is included.

The Uno browser hook adds seven checks for real managed sections, overlaps, component execution, cached results, serialization and actual controller split/undo, alongside the existing framebuffer tests. These are not exhaustive pointer/menu or physical-GPU tests. See `SECTIONS-AND-RUNTIME.md`.

The offline JavaScript suite is separate. Historical alpha evidence is preserved as history, not rewritten to imply checks that were never run.

The area/native milestone adds 27 managed/native analysis checks and seven Uno browser checks (29 browser checks total). It does not add second/product moments, inertia, solid volume or arbitrary-trim area integration. Native allocation improvements apply to the optional C ABI adapter, not the default browser evaluator or GPU rendering. See `SURFACE-ANALYSIS.md`.

The compact workspace adds the reusable Presentation package, 25 presentation/input
contracts and real pointer/keyboard browser scenarios. Graph metadata/path reuse,
layer-row retention and view/layout behavior have dedicated checks. This is not
pixel-identical UI, full docking/Osnap/CPlane/gumball or desktop-runtime parity.
The existing GL renderer and numerical kernel are retained. See `CLASSIC-WORKSPACE.md`.

The graph-editing milestone adds versioned exact graph fragments, clipboard and
duplicate actions, native editor/context menus, alignment/distribution, selection
windows and cursor search. Incremental drawing retains unchanged nodes and wires.
It does not add Grasshopper clipboard/file compatibility, clusters or plugins;
see `GRAPH-EDITING.md` for contracts and benchmark scope.

PR #8 adds a native number-slider settings dialog, 0–12-place or automatic
precision, binary64 range-safe scrubbing, atomic Apply/Cancel and stale-edit
guards. Graph/registry collection snapshots are retained and wire transforms
reuse drawing adjacency. These do not add full Grasshopper numeric semantics,
slider expressions/animation, all rounding modes or a different GPU backend.
See `SLIDERS-AND-SNAPSHOT-REVIEW.md` for tests and measurement limits.

PR #9 adds bounded numeric/range/operator/Boolean/text canvas creation, a typed
Panel component and draft-only multiline editing. Connected empty trees override
stored text rather than displaying stale data. PR #10 adds resize handles with
isolated drafts, exact optional canvas dimensions, low-zoom panel readability and
preview/wire-cache work avoidance. Native text normalization preserves unchanged
original line endings; edited text uses LF. Only the bottom-right resize handle is
implemented, not every original border/handle, font/alignment option or scrolling
behavior. See `PANEL-LAYOUT-REVIEW.md` for tests and remaining boundaries.
