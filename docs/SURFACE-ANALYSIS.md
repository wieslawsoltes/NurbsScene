# Surface area, centroid and native evaluation

## Published API and application entry points

`NurbsSurface.GetAreaProperties(options, cancellationToken)` integrates unsigned
area and area centroid from analytic NURBS derivatives. It never integrates the
viewport triangles. `SurfaceArea` and `SurfaceCentroid` are executable components
in the existing component search (112 managed definitions; 102 in the separate
JavaScript catalog). The commands `SurfaceArea [absoluteAreaTolerance]` and
`SurfaceCentroid [absoluteAreaTolerance]` report selected surfaces without changing
the document, selection or undo history. Mixed non-surface selections fail before
measurement output is reported.

This implements area and first moments only. It does not implement all Rhino area
mass properties, second/product moments, inertia, volume, or arbitrary B-rep trim
loops. Rhino's broader feature contract is documented in its
[AreaMassProperties API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.areamassproperties/compute).
No Rhino code or binaries are used.

## Numerical contract

The integrand is `|Su cross Sv|`; first moments multiply that density by the
surface position. Each nonempty active U/V knot rectangle is integrated separately.
Disconnected parameter regions are summed without inventing triangles across the
gaps. Reversed parameters do not change unsigned area. Self-overlapping parameter
sheets are counted with multiplicity; this is not the area of their geometric union.

The algorithm compares tensor Gauss 3-point and 5-point rules, subdividing the
rectangle with the largest estimated error into four children. Eight additional
near-edge derivative samples detect excessive boundary contrast: interior-only
rules can both miss a thin rational boundary layer. That heuristic forces further
refinement or a nonconverged result; it is not a mathematical certificate against
all possible missed features. Coordinates and homogeneous weights are normalized
before evaluation, and compensated sums reduce cancellation in first moments.

Absolute tolerance is in **squared model units**. Relative tolerance also controls
first moments normalized by the control-hull radius. The returned area error and
centroid-coordinate error are numerical integration estimates, not interval bounds
or guarantees of input/evaluator floating-point accuracy. In particular, the
centroid error does not include loss already present in large input coordinates.
Defaults are absolute/relative `1e-8`, 250,000 evaluations, 4,096 cells and depth 20.
Each initial rectangle costs 42 samples; splitting costs 168. The initial partition
must fit the budget or the call throws instead of returning a partial-domain area.

After complete initial coverage, exhausted evaluation/cell/depth budgets return a
full-domain estimate with `Converged == false` and an explicit `Termination` value.
Cancellation throws. Unrepresentable quadrature nodes, relative-weight underflow,
overflow and non-finite integrands fail explicitly. A zero-area patch returns
`Centroid == null`; the centroid component diagnoses this instead of inventing an
origin point. The component refuses nonconverged results. No arbitrary-trim or
solid-volume approximation is silently substituted.

## Performance and cache ownership

The existing optional grid evaluator reuses U/V basis tables inside each tensor
rule; legacy evaluators remain supported. Successful measurements are cached by
immutable surface identity and exact options. Only one settings/result entry is
retained per surface in a weak-key cache. A new immutable surface cannot inherit
another surface's result. Failures, cancellation and unresolved measurements are
not cached. Cached hits report `CacheHit == true` and zero new evaluations; `Cells`
still describes the retained partition. Concurrent cold calls may compute twice.
Custom evaluators must remain numerically stable for immutable geometry; callers
using stateful evaluators must set `CacheResult: false`.

`NativeNurbsEvaluator` now implements `INurbsSpanEvaluator`. It pins the existing
read-only homogeneous controls and writes directly into the requested prefix of a
caller-owned derivative span. Small scratch buffers use at most 2 KiB of stack;
larger scratch uses a per-call array-pool lease returned in `finally`. Geometry
vector layouts are explicitly sequential and checked against the C ABI. No
unsafe capacity assumption, shared mutable scratch, or fallback evaluator is used.
Invalid parameters, derivative orders and short buffers are rejected before the
native call. Numerical failures may leave the requested output prefix modified;
entries beyond the requested prefix remain untouched.

The original allocating `Evaluate` APIs remain, but allocate only their returned
array after warm-up. `PointAt` automatically takes the span path for the native
adapter. C-side input validation still scans geometry; this is not constant-time
in the number of controls. The default Uno browser application still uses the
managed evaluator, and its renderer is still GL/WebGL2. Native benchmark gains
must not be described as WebGPU, FPS or application-wide gains.

## Reproduction and review coverage

```sh
python -m pip install 'numpy>=1.26,<3' 'scipy>=1.12,<2'
python tools/generate-area-oracle.py
cmake -S native/kernel -B artifacts/native -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/native --parallel
LD_LIBRARY_PATH="$PWD/artifacts/native" NURBSSCENE_NATIVE_TESTS=1 \
  dotnet run --project tests/dotnet/NurbsScene.AnalysisContracts -c Release
```

The suite contains 26 checks when the native flag is enabled: planar/tilted patches,
cylinders, spheres and cones; singular poles; parameter/degree/knot invariance;
large translations/scaling; disconnected and narrow spans; limits, cache identity,
cancellation and concurrency; native ABI layout, sentinels and allocation checks;
component execution, workspace round trips and read-only commands. Twelve surface
fixtures use an independent SciPy tensor-spline implementation and 32/48-point
Gauss rules that must agree before a reference is accepted. Those references are
not certified global oracles. The CI job requires the native flag and runs all 26.

Seven additional real Uno WebAssembly checks extend the existing 22-check browser
hook: cylinder analysis, cache reuse, components, exact workspace round trip,
zero-area behavior, actual controller commands and restoration. The existing
rendered-surface/empty-scene and camera-motion framebuffer comparisons remain.
This is not complete pointer/menu, desktop-runtime or physical-GPU coverage.

`analysis.yml` compares exactly the same `tests/native-performance` source against
previous main `876f21c675eefaff83b0b48ccb96de06d6a92c7f` and the candidate, sequentially
on one runner with Release builds, tiered compilation disabled, 100 warm-up calls
and seven measured samples. It uses the actual unchanged native C library on both.
Artifacts contain baseline/candidate JSON, min/max/median times, allocations,
contracts, and the tested commit/tree. Timings are observations, not test gates.
The structural test requires zero managed bytes for 2,000 warmed native span calls.
