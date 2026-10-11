# Curve sections and graph runtime

## New modeling APIs and commands

`curve.IntersectPlane(plane, options, cancellationToken)` operates on the
homogeneous Bernstein numerator of each exact rational Bezier span, not on its
display polyline. An internal forward extractor scans the knot vector once and
performs local homogeneous insertion instead of repeatedly copying/refining a
whole curve. The immutable result is cached by curve identity for subsequent
plane queries. Public Bezier decomposition, tessellation and existing measurement
APIs are unchanged by this milestone.

The search partitions the numerator using derivative roots, bisects monotone
sign-changing intervals, and tests repeated-root candidates. Positive rational
weights permit control-hull sign exclusion. Candidates must pass a world-distance
residual test. A span whose entire control hull is within distance tolerance is
reported as an **overlap**, not as a collection of fabricated point intersections.
Adjacent overlaps merge; the two parameter values at a closed seam remain distinct
when they represent isolated events.

`CurvePlaneResult` contains immutable `Points`, `Overlaps`, `Unresolved`, an
`Evaluations` work count and `Complete`. Always inspect `Complete`. Work-budget
exhaustion, poorly resolved residuals and numerically ambiguous near-zero critical
points/endpoints leave unresolved intervals. `Complete` means the numerical
algorithm finished under its stated tests, **not** an interval-arithmetic proof
that every root of an arbitrarily conditioned curve has been isolated.

Supported extraction requires clamped endpoints, continuous interior knots and
positive finite weights. Defaults are a `1e-7` world-distance tolerance, a `1e-11`
parameter tolerance relative to each Bezier span, and 200,000 counted root-work
operations. Decomposition has a separate one-million-control budget. Cancellation
is checked during extraction and search. Inputs whose weight ratios underflow or
whose plane numerator overflows fail explicitly. General curve/curve,
curve/surface and surface/surface intersections remain incomplete.

`curve.SplitMany(parameters, cancellationToken)` sorts and deduplicates explicit
interior parameters before changing anything. Recursive median partitioning gives
a balanced split tree rather than a left-to-right sequence of increasingly
expensive whole-curve copies. Every piece retains its exact degree, weighted
controls and original parameter interval. At most 4,095 distinct cuts and 100,000
enumerated input items are accepted. Empty input returns the original curve.

| Entry point | Behavior |
|---|---|
| `CurvePlaneSection` component | Curve, plane origin/normal and distance tolerance to point/parameter lists and overlap curves; unresolved intervals are component errors. |
| `ShatterCurve` component | Curve plus an explicit parameter list to exact curve pieces. |
| `CurvePlane origin normal [tolerance]` | Read-only diagnostic report for selected curves. Coordinates are comma-separated triples. |
| `SplitCurve parameter [parameter ...]` | Prepare all selected curves, preserve the first piece's object identity, add the other pieces, and select them in one undoable transaction. |

Both components appear through existing component search; commands use the
existing Uno console. This is 110 managed definitions versus 102 in the independent
JavaScript catalog. No automatic split-at-plane wrapper, new dedicated toolbar or
claim of identical Rhino command semantics is included.

## Graph and data-tree performance

`DataTree.ItemsAt` uses binary search over immutable, numeric-lexicographically
ordered paths. It allocates nothing for existing hits or misses; extracting a new
branch tree still allocates that returned tree. Input enumeration, branch/item
counts, default immutable arrays and partition-count arithmetic are bounded.

The graph maintains an incoming-port index in addition to downstream dependencies.
A cached execution plan stores topological order and incoming wire arrays.
Structural changes invalidate it; parameter, enabled-state and presentation edits
do not. Unchanged solves, including layout-only changes, reuse the immutable result
map. Partial dirty solves still snapshot the graph and publish a fresh result map;
they are not constant-time. `ExecutionPlanBuilds` makes reuse testable.

Multi-wire fan-in merges all branches in one tree construction rather than growing
and copying an accumulated tree once per wire. Wire insertion order, sparse paths,
null items and explicit empty branches are retained. Output budgets now also bound
component invocations and emitted branch work, including components that emit no
items. Cancellation, generation checks and stale-result rejection remain enabled.

## Edit isolation and review fixes

Graph-session edits are synchronous and owner-thread serialized. Listeners receive
one `Changed` event after a successful batch, not intermediate partially wired
states. A failed edit restores the pre-edit graph and emits the session replacement
event. Nested edits, undo/redo, replacing the graph or solving it during the edit
are rejected. An unchanged node update does not invalidate caches or advance the
revision. A post-commit subscriber exception cannot roll back a successful edit
while leaving contradictory history.

Tests additionally guard deletion of a clean isolated node, replacement of one of
several parallel dependencies, cancelled cached solves, root uncertainty at a knot
endpoint, and exact fan-in item ordering including empty branches. These are
bounded regression cases, not proof that all application paths have been reviewed.

## Qualification and measurements

`NurbsScene.SectionContracts` contains 28 tests. The extraction test directly
exercises the internal forward implementation across degrees 1 through 12 and
compares original rational positions/derivatives. Plane tests cover analytic
lines/circles, overlaps, tangencies, extreme common weight scaling and independent
power-polynomial constructions through degree eight. The existing SciPy, native,
managed, JavaScript and package suites remain unchanged and continue in CI.

The actual Uno WebAssembly hook adds seven checks: rational sections, coplanar
overlaps, both components, execution-plan/result reuse, workspace round trip,
actual controller multi-splitting and undo. The existing 15 startup/modeling/GL
framebuffer checks remain. This does not establish exhaustive menu/pointer
interaction parity, desktop runtime coverage or physical-GPU performance.

`tests/section-performance` is copied unchanged into baseline main `21ed2c7` and the
candidate. `sections.yml` runs them sequentially on the same runner, Release with
tiered compilation disabled, three warm-up calls and seven samples. Workloads are
lookup in 10,000 branches, cached solving of 1,001 nodes, solving two dirty nodes
among 1,001, and building a 1,000-node chain. Artifacts retain `baseline.json`,
`candidate.json`, `contracts.log` and `tested-revision.txt`. Allocation assertions
are separate from timings. This benchmark is not application-wide speed, GPU/FPS,
cold compilation, or a promise of identical results on other hardware.

Publication of separate local public-extraction integration and an automatic
plane-splitting wrapper was blocked by the platform. Those changes and their local
benchmark numbers are excluded from this published milestone. Only evidence for
the actual published source should be used.

## Recorded implementation run

Run [38155400644](https://github.com/wieslawsoltes/NurbsScene/actions/runs/38155400644)
qualified implementation head `88c1f0b68b64000a62617077cca4b2acd41b4510`, tested tree
`f57d969878be55d688a0ec4c217288c45b53ed9a`, against baseline `21ed2c7`. Environment:
.NET 10.0.12, Ubuntu 24.04.5 LTS, x64, four reported processors, tiered compilation
disabled. The 28 contracts passed. Per-operation medians:

| Workload | Baseline | Candidate | Time ratio | Allocations, baseline to candidate |
|---|---:|---:|---:|---:|
| Lookup in 10,000 branches | 76.556 microseconds | 0.082 microseconds | 934x | 88 to 0 bytes |
| Unchanged 1,001-node solve | 824.475 microseconds | 0.215 microseconds | 3,835x | 301,704 to 200 bytes |
| Two dirty nodes among 1,001 | 773.070 microseconds | 604.640 microseconds | 1.28x | 311,776 to 166,616 bytes |
| Build/connect a 1,000-node chain | 10.769 ms | 3.001 ms | 3.59x | 2,042,800 to 2,129,104 bytes |

The unchanged-solve improvement is specifically reuse of an already-computed
immutable result, not accelerated component execution. The wiring index trades
about 4.2% more allocation in this construction workload for faster connection
validation. Partial solves retain graph snapshots and are deliberately not
reported as thousands of times faster. Later heads are remeasured in CI; use their
artifacts rather than assuming the exact same timing samples.
