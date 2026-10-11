# Geometry queries, performance and review contracts

## Implemented APIs

`INurbsSpanEvaluator` is additive: existing `INurbsEvaluator` implementations still work. The managed evaluator uses stack scratch for small problems and scoped pooled arrays for larger degree/order. Point evaluation uses the span path when supported, avoiding a temporary derivative array. Derivative order remains bounded at 32. `INurbsGridEvaluator` computes U/V basis tables once per axis and emits U-major first-order surface jets; V varies fastest. Invalid grid axes are rejected before output writes; cancellation and numerical failures may leave a written prefix. The grid and workspace budgets are explicit.

Immutable geometry computes its Cartesian control-hull bounds once. Display caches distinguish geometry identity from style metadata. Origin rebasing re-encodes floats from retained binary64 positions and retained connectivity rather than evaluating the NURBS again or translating already-rounded floats.

The reusable closest-point index decomposes supported positive-weight curves into rational Bezier segments. Local Newton steps improve the candidate upper distance; priority-ordered control-hull subdivision supplies numerical lower estimates. Every result includes `Converged`, a distance gap, subdivision count and evaluation count. The gap is not an interval-arithmetic certificate or a parameter-error bound. Unsupported unclamped/disconnected decompositions fail explicitly. The public API returns unresolved work honestly; the graph component rejects an unresolved result rather than treating it as success.

Five managed components expose closest point, curve-control editing, curve endpoints, plane projection and reflection. Modeling commands retain exact control data and atomic undo. The closest-point command is currently diagnostic/read-only, unlike Rhino's point-creation command. The managed catalog has 108 definitions; the independent JS catalog remains at 102.

## Review fixes

Picking now clips curve segments to the camera near/far range and the active clip plane **before** minimizing screen-space distance. A visible endpoint within pixel tolerance can be selected even when the original segment's closest point was clipped. Segments crossing behind the eye no longer disappear from picking. Triangle queries use the corresponding world-ray interval, so an invisible near face cannot hide a farther valid face in the same mesh.

Record cloning can copy a private cached index. `DisplayMesh` therefore validates the index's vertex/index array identities before reuse, while retaining the index across origin-only rebases. Warm access has no cache-factory delegate allocation. Different geometry objects cannot reuse a previous scene's mesh solely by presenting the same GUID. Invalid colors, clip planes and camera parameters fail explicitly, and failed scene replacements preserve the prior snapshot.

Surface display sampling retains every active knot and an interior point of each nonempty knot span, in addition to regular samples. This fixes completely missed narrow-span features, but it is **not** certified adaptive surface tessellation. Disconnected surfaces are rejected rather than bridged with invented triangles. Tiny viewport layouts stay nonnegative. Grid generation rejects nonpositive spacing and excessive work, and uses overflow-safe integer iteration.

Document object/layer/selection views are detached read-only snapshots cached between writes. Older views remain unchanged after edits or rollback. This removes repeated full-selection copies from every object lookup during a workspace refresh. Document and display-scene mutations still belong on the serialized owner/UI thread.

## Reproducible tests

```sh
python tools/generate-length-oracle.py
python tools/generate-query-oracle.py
dotnet run --project tests/dotnet/NurbsScene.PerformanceContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.QueryContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.ReviewContracts -c Release
```

These suites respectively contain 17 performance/correctness contracts, 16 query/modeling contracts (including 96 independent SciPy reference queries), and 17 review regressions. The rest of the existing numerical, graph, document, package and Uno tests also run in CI. All three projects are included in the solution and source-layout check.

## Measured evidence, not a universal speed guarantee

Run [38146096884](https://github.com/wieslawsoltes/NurbsScene/actions/runs/38146096884) tested implementation head `20883fc6eda9daf1094b3cb3c1bd21a77a5f7517`, tree `b43aa9c2e4c4c203581435fd92b6d56a5bf24bf4`. The harness checks out baseline `f0bd8e33f4a70c52a6865697baae126d2bc9ee9e` and runs identical benchmark source on both. Environment: .NET 10.0.12, Ubuntu 24.04.5, x64, four reported processors, Release, tiered compilation disabled; seven measured samples after warm-up. Values below are medians per operation.

| Workload | Baseline | Reviewed candidate | Ratio |
|---|---:|---:|---:|
| Curve point evaluation | 0.251 microseconds | 0.089 microseconds | 2.83x |
| Surface point evaluation | 0.691 microseconds | 0.297 microseconds | 2.33x |
| 64-segment surface mesh | 5.632 ms | 1.110 ms | 5.07x |
| Origin rebase | 5.656 ms | 0.078 ms | 72.11x |
| Warm pick over 115,200 triangles | 2.957 ms | 0.001805 ms | 1638x |
| Unchanged 25-item scene | 6.139 microseconds | 0.597 microseconds | 10.28x |

The large picking ratio compares a warm hierarchy query against exhaustive traversal for this specific scene/ray; it excludes cold index construction and is not an application-wide or GPU speedup. Point evaluation allocated zero bytes in the measured loops. Meshing allocations fell from 6,047,600 to 566,376 bytes per operation. The benchmark JSON also records min/max samples and bounds-access timings, whose nanosecond-scale ratio is not meaningful as a general application result.

The review suite records 56 bytes per warmed ray pick (the result object) versus 152 bytes reproduced before the cache fix. A 600-selected-object refresh measured 55,176 bytes versus 11,157,560 bytes before immutable-view caching in the local reproduction; the final 55,176-byte result was also repeated in CI. These allocation observations are separate from the baseline timing table.

CI retains `baseline.json`, `candidate.json`, `contracts.log`, `queries.log`, `review.log` and `tested-revision.txt`. Later heads are remeasured rather than inheriting these numbers. These tests do not measure FPS, input latency, physical-GPU execution, cold application startup, or all possible geometry conditioning. The previously staged, unpublished renderer-uniform/upload-counter integration is not part of this PR's validated source; no performance claim here depends on it.
