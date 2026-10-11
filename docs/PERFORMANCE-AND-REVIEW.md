# Geometry queries, performance and review contracts

## Implemented APIs

`INurbsSpanEvaluator` is additive: existing `INurbsEvaluator` implementations still work. The managed evaluator uses stack scratch for small problems and scoped pooled arrays for larger degree/order. Point evaluation uses the span path when supported, avoiding a temporary derivative array. Derivative order remains bounded at 32. `INurbsGridEvaluator` computes U/V basis tables once per axis and emits U-major first-order surface jets; V varies fastest. Invalid grid axes are rejected before output writes; cancellation and numerical failures may leave a written prefix. Grid and workspace budgets are explicit.

Immutable geometry computes its control-hull bounds once. Display caches distinguish geometry identity from style metadata. Origin rebasing re-encodes floats from retained binary64 positions/connectivity rather than evaluating NURBS again or translating rounded floats.

The closest-point index decomposes supported positive-weight curves into rational Bezier segments. Local Newton steps improve the candidate upper distance; priority-ordered hull subdivision supplies numerical lower estimates. Results include `Converged`, distance gap, subdivisions and evaluations. The gap is not an interval certificate or parameter-error bound. Unsupported unclamped/disconnected decompositions fail explicitly. The API returns unresolved work honestly; graph components reject unresolved results.

Five components expose closest point, curve-control editing, endpoints, plane projection and reflection. Commands retain exact controls and atomic undo. The closest-point command is diagnostic/read-only, unlike Rhino's point-creation command. Managed catalog: 108 definitions; separate JS catalog: 102.

## Review fixes

Curve picking clips to camera near/far limits and the active plane **before** minimizing screen distance. Visible clipped endpoints within pixel tolerance remain selectable. Eye-crossing segments no longer disappear. Triangle queries use the corresponding world-ray interval so invisible near faces cannot hide farther valid faces in the same mesh.

`DisplayMesh` checks vertex/index array identities before reusing an index copied by a record clone. Origin-only rebases retain the index. Warm access allocates no factory delegate. Record equality/hashing explicitly excludes lazy cache state, so a first pick cannot invalidate a caller's hash-table key. Different geometry objects cannot reuse an old mesh solely by presenting its GUID. Invalid colors, clip planes and camera parameters fail explicitly; failed replacements preserve the previous scene.

Surface sampling retains every active knot and an interior point per nonempty span, plus regular samples. Narrow-span features are no longer completely missed, but this is **not** certified adaptive tessellation. Disconnected surfaces fail instead of being bridged with invented triangles. Tiny viewport rectangles remain nonnegative. Grid spacing/work limits and integer overflow are checked.

Document object/layer/selection views are detached read-only snapshots cached between writes. Older views remain unchanged after edits or rollback. This removes repeated full-selection copies during refresh. `Task`, `ValueTask` and `ValueTask<T>` transaction results are rejected; statically known asynchronous result types are rejected before invoking the action. Document/display mutations still belong on the serialized owner/UI thread.

## Tests

```sh
python tools/generate-length-oracle.py
python tools/generate-query-oracle.py
dotnet run --project tests/dotnet/NurbsScene.PerformanceContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.QueryContracts -c Release
dotnet run --project tests/dotnet/NurbsScene.ReviewContracts -c Release
```

The suites contain **17** performance/correctness contracts, **16** query/modeling contracts (96 independent SciPy reference queries), and **19** review regressions. Fourteen review cases were reproduced failing before their corresponding fixes; five more cover related edge/budget/legacy-consumer behavior. All three projects are in the solution and source-layout check. The existing numerical, graph, document, package and Uno suites also run in CI. These checks are not proof of every possible input or complete product compatibility.

## Measured evidence, not a universal speed guarantee

Run [38146096884](https://github.com/wieslawsoltes/NurbsScene/actions/runs/38146096884) tested implementation `20883fc6eda9daf1094b3cb3c1bd21a77a5f7517`, tree `b43aa9c2e4c4c203581435fd92b6d56a5bf24bf4`, before the final record-hash/ValueTask fixes (17 review cases at that revision). Identical benchmark source runs against baseline `f0bd8e33f4a70c52a6865697baae126d2bc9ee9e` and candidate. Environment: .NET 10.0.12, Ubuntu 24.04.5, x64, four reported processors, Release, tiered compilation disabled, seven measured samples after warm-up. Medians per operation:

| Workload | Baseline | Reviewed candidate | Ratio |
|---|---:|---:|---:|
| Curve point | 0.251 microseconds | 0.089 microseconds | 2.83x |
| Surface point | 0.691 microseconds | 0.297 microseconds | 2.33x |
| 64-segment surface mesh | 5.632 ms | 1.110 ms | 5.07x |
| Origin rebase | 5.656 ms | 0.078 ms | 72.11x |
| Warm pick over 115,200 triangles | 2.957 ms | 0.001805 ms | 1638x |
| Unchanged 25-item scene | 6.139 microseconds | 0.597 microseconds | 10.28x |

The large picking ratio compares a warm hierarchy query with exhaustive traversal for this scene/ray, excluding cold index construction. It is not an application-wide or GPU speedup. Point evaluation allocated zero bytes in the measured loops. Meshing allocations fell from 6,047,600 to 566,376 B/op. Raw JSON retains min/max samples and bounds-access timings; a nanosecond-scale bounds ratio is not a meaningful general application result.

The review suite records 56 B/warm ray pick (the result object), versus 152 B reproduced before the cache fix. A 600-selection refresh measured 55,176 B versus 11,157,560 B before view caching locally; the 55,176 B result also repeated in CI. These allocation observations are separate from the baseline timing table.

Artifacts retain `baseline.json`, `candidate.json`, `contracts.log`, `queries.log`, `review.log`, and `tested-revision.txt`. Later heads are remeasured, not assumed to inherit these numbers. Tests do not measure FPS, input latency, physical GPU execution, cold app startup, or every conditioning case. The previously staged, unpublished renderer-uniform/upload-counter integration is not part of the validated source; no result here depends on it.
