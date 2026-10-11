# Current implementation and compatibility boundaries

This inventory describes the source after completion of the preserved-source import and the typed-data-path work. It does not claim full Rhino/Grasshopper parity.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Exact geometry | Positive-weight rational curves and tensor-product surfaces; expanded knots; analytic curve and mixed surface derivatives | Negative/zero projective weights, full periodic/disconnected editing coverage and every extreme-conditioning case |
| Curve modeling | Lines, polylines, rational arcs/circles, interpolation, insertion, split/trim, elevation, compatible loft inputs | Full joining, intersections, offsets, blending, fitting and Rhino command behavior |
| Surface modeling | Extrusion, revolution, loft, exact isocurves; U/V refinement, elevation, split/reversal; rectangular extraction; control/weight edits; Bezier patches | Arbitrary trimming loops, robust surface intersections, sweep/network/patch variants and full editing semantics |
| Solids | No integrated solid-modeling backend | B-rep topology, booleans, fillets/chamfers, healing, sewing, shells and production robust predicates. OCCT is not integrated |
| Measurements | Analytic-speed adaptive Gauss-Kronrod length, inverse arc length and equal-distance division with cancellation/work limits | Error estimates are not certified bounds; disconnected full-multiplicity length requests fail explicitly |
| Parametric engine | 103 managed / 102 JavaScript executable definitions, typed ports, persistent trees/paths, bounded matching, cycle rejection, incremental cache, cancellation and stale-solve rejection | Complete Grasshopper catalog, clusters, scripting/plugin compatibility and all branch-matching/data-conversion rules |
| Documents | Immutable geometry snapshots, layers, locking, selection, undo/redo, atomic editing and explicit invalid-input errors | Full Rhino object types, blocks, history, reference files, annotations and large-project qualification |
| Interchange | Managed exact `.nscene`; separate JS workspace dialect; OBJ/STL display export | `.3dm`, `.gh`, `.ghx`, STEP/IGES and plugin interchange. Display STL is not a watertight-solid certificate |
| Uno UI | Desktop/browser source, four viewports, graph canvas/search/typed connections, inspectors, commands, baking, workspace file pickers | Identical Rhino/Grasshopper visuals and behavior, complete docking/toolbars/accessibility/input-device coverage |
| Rendering | Uno GL/WebGL2 depth rendering with explicit shared-state restoration and readback tests; JavaScript WebGPU/WebGL2 source | Direct Uno WebGPU, certified surface tessellation, production materials/lighting, transparency, clipping, hardware/performance matrix |
| Other Rhino systems | Not implemented | SubD, full meshing, rendering plugins, layouts/printing, scripting ecosystem and remaining product features |

## Validation scope

Numerical tests compare the native/managed kernel to analytic results and independent SciPy fixtures. The managed graph suites exercise every newly added component, sparse paths, empty branches, serialization, dependency-index changes, cancellation and a 2,001-node incremental graph. These are bounded tests, not a guarantee for arbitrary workloads.

The real Uno browser suite compiles and runs the managed application, exercises commands/undo/redo/graph changes/baking/interchange and compares actual GL framebuffer pixels against an empty scene and after camera motion. Missing contexts or submission errors fail the test. Software GL may satisfy this check; no physical-GPU performance claim follows.

The offline JavaScript suite is separate: it tests the six reusable modules and their browser controls without claiming Uno or GPU qualification. Historical alpha evidence is preserved as history, not rewritten to imply checks that were never run.
