# Current implementation and compatibility boundaries

This inventory includes the preserved-source import, typed data trees, curve-query additions, curve-plane sections, and graph-performance review through PR #3. It does not claim full Rhino/Grasshopper parity.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Exact geometry | Positive-weight rational curves and tensor-product surfaces; expanded knots; analytic derivatives; optional span/grid evaluators and cached bounds | Negative/zero projective weights, full periodic/disconnected editing coverage and every extreme-conditioning case |
| Curve modeling | Lines, polylines, arcs/circles, interpolation, insertion, split/trim, elevation, control/weight editing, closest-point and numerical curve-plane queries, balanced multi-splitting | Full joining, curve/curve and surface intersections, offsets, blending, fitting and Rhino command behavior; query distance gaps are numerical estimates |
| Surface modeling | Extrusion, revolution, loft, exact isocurves; U/V refinement/elevation/split/reversal; rectangular extraction; control/weight edits; Bezier patches | Arbitrary trimming loops, robust surface intersections, sweep/network/patch variants and full editing semantics |
| Transforms | Translation, rotation, scale, affine plane projection and reflection of authoritative controls | Complete transform tools, gumball constraints, construction planes and command semantics |
| Solids | No integrated solid-modeling backend | B-rep topology, booleans, fillets/chamfers, healing, sewing, shells and production robust predicates. OCCT is not integrated |
| Measurements | Analytic-speed adaptive Gauss-Kronrod length, inverse arc length and equal-distance division with cancellation/work limits | Error estimates are not certified bounds; disconnected full-multiplicity length requests fail explicitly |
| Parametric engine | 110 managed / 102 JavaScript definitions, typed ports, persistent trees/paths, bounded matching, cycle rejection, cached execution plans/results, batched edit notifications, cancellation and stale-solve rejection | Complete Grasshopper catalog, clusters, scripting/plugin compatibility and all matching/conversion rules |
| Documents | Cached immutable snapshot views, layers, locking, selection, undo/redo, atomic editing and explicit invalid-input errors | Full Rhino object types, blocks, history, reference files, annotations and large-project qualification |
| Interchange | Managed exact `.nscene`; separate JS workspace dialect; OBJ/STL display export | `.3dm`, `.gh`, `.ghx`, STEP/IGES and plugin interchange. Display STL is not a watertight-solid certificate |
| Uno UI | Desktop/browser source, four viewports, graph search/typed wiring, inspectors, commands, baking and workspace pickers | Identical Rhino/Grasshopper visuals/behavior, complete docking/toolbars/accessibility/input-device coverage |
| Rendering | GL/WebGL2 depth rendering, shared-state restoration, knot-aware surface sampling, origin re-encoding, hierarchical picking and clip-aware selection; separate JS WebGPU source | Direct Uno WebGPU, certified adaptive surface tessellation, production materials/lighting/transparency, full clipping and hardware/performance matrix |
| Other Rhino systems | Not implemented | SubD, full meshing, rendering plugins, layouts/printing, scripting ecosystem and remaining product features |

## Validation scope

Numerical tests compare the native/managed kernel to analytic results and independent SciPy fixtures. Closest-point references minimize many intervals independently; they are not certified global-minimum oracles. Tests compare distances and enforce work budgets. The graph suites include sparse paths, empty branches, serialization, dependency changes, cancellation and a 2,001-node incremental graph.

The performance suite compares one identical harness against baseline `f0bd8e3` and the candidate on one runner. Structural tests separately check allocation and traversal budgets. The 19 review regressions cover clipping, tiny viewports, cache identity/record cloning/hash stability, narrow knot spans, grid input limits, immutable document views, selected-workspace refresh allocations and ValueTask transaction rejection. These are bounded checks, not proof of every possible input or a complete product review. See `PERFORMANCE-AND-REVIEW.md`.

The real Uno browser suite compiles and runs the application and compares actual GL framebuffer pixels against an empty scene and after camera motion. It exercises managed commands, undo/redo, graph edits, baking, workspace and typed-path round trips. The new query APIs and review contracts are additionally tested in managed .NET CI; the browser smoke test does not exercise every new API or command. Software GL may satisfy the browser check; no physical-GPU performance claim follows.

The section suite adds 28 contracts for forward extraction, rational plane roots/overlaps/uncertainty, balanced splitting, two executable components, commands, branch lookup, plan/result reuse, fan-in ordering, and edit/history isolation. Its independent benchmark harness compares graph/tree operations against the previous main `21ed2c7`, not the older geometry baseline. The new extraction cache is used by plane queries only; public Bezier decomposition and the pre-existing display/measurement paths are unchanged. No automatic plane-splitting wrapper is included.

The Uno browser hook adds seven checks for real managed sections, overlaps, component execution, cached results, serialization and actual controller split/undo, alongside the existing framebuffer tests. These are not exhaustive pointer/menu or physical-GPU tests. See `SECTIONS-AND-RUNTIME.md`.

The offline JavaScript suite is separate. Historical alpha evidence is preserved as history, not rewritten to imply checks that were never run.
