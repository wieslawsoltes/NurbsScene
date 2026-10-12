# Exact ellipses and shared loft interpolation factors

## Published baseline and scope

PR #10 is merged as `d768ea5b0eab5b94818c786369307c860cc5d14d`.
All recovered source and panel reviews are published. Its exact tree
`aa67738b13633075df828fbc915a16e69a579a39` passed all 19 workflows,
153 actual pointer/keyboard checks and the same 29 modeling/framebuffer checks
in each of four independent Uno contexts. Historical offline publishing reports
are retained as provenance, not current publication status.

This continuation adds one exact curve primitive and reduces repeated work in
the existing homogeneous loft algorithm. It does not implement full Rhino loft
styles, automatic seams/directions, periodic/closed lofts, arbitrary trimming or
solid B-rep topology. The existing GL/WebGL2 viewport backend remains in use.

## Ellipse primitive and real entry points

`GeometryFactory.Ellipse(center, radiusX, radiusY, axisX?, axisY?)` creates
a closed degree-two rational NURBS curve with nine controls, four quarter spans,
and alternating weights 1 and sqrt(1/2). The last homogeneous control is copied
from the first for an identical seam. The curve is authoritative geometry, not a
polyline or triangulated approximation. Existing derivatives, measurements,
transforms, refinement, splitting, serialization, meshing, graph preview and
exact baking use the same immutable geometry type.

Axes are normalized, checked for orthogonality using the existing arc tolerance,
and finite positive radii are required. Non-finite homogeneous controls or
collapsed axis extrema at an unrepresentable coordinate scale are rejected.
This does not certify every extreme conditioning case. Its rational [0,1]
parameter is not proportional to angle or arc length.

McNeel's [NurbsCurve API](https://developer.rhino3d.com/api/RhinoCommon/html/Methods_T_Rhino_Geometry_NurbsCurve.htm)
similarly distinguishes an ellipse's exact rational representation from angular
parameterization. This code is independently authored, not a RhinoCommon binding.

The managed catalog now contains **114 executable definitions** (the independent
JavaScript catalog remains 102). `Ellipse` has Center, RadiusX, RadiusY, AxisX and
AxisY inputs and a typed Curve output. The command
`Ellipse centerX,centerY,centerZ radiusX radiusY` creates a world-XY ellipse in one
undoable document transaction. Curve toolbar/search entries use an authored
ellipse icon. This is not the complete interactive Rhino Ellipse command.

## Factor once, solve many homogeneous columns

The former loft implementation rebuilt the same matrix factorization for every
U control column. Each column also cloned the entire dense matrix and allocated
another four-column RHS. All of those columns share the same section parameters
and V knots, so their collocation matrix is identical.

The new internal `InterpolationFactors` factors that matrix once with scaled
partial pivoting. The existing scaled singular-pivot threshold (1e-14) is retained.
It records permutations and lower factors and tracks the last nonzero/fill column
after pivoting instead of assuming an unchanged B-spline bandwidth. RHS solves
apply permutations and triangular substitutions with retained factor ranges.
One four-column homogeneous RHS buffer is reused across all U controls.

This is the standard [factorization](https://netlib.org/lapack/explore-html/db/d04/group__getrf.html) /
[repeated solve](https://netlib.org/lapack/explore-html/df/d36/group__getrs.html)
separation, implemented here in managed C#. No LAPACK dependency or native solver
binary is added. Dense matrix allocation remains quadratic in section count;
this is not a complete banded-storage or sparse-direct-solver redesign.

The old two-argument Loft API remains. A CancellationToken overload is added and
the parametric component forwards its evaluation token. Cancellation is checked
during input capture, matrix preparation, factor columns, RHS solves and output
columns. Existing compatibility preparation is checked at entry/exit; cancellation
does not interrupt the middle of an existing knot insertion or degree elevation.
The output is never published until all controls pass immutable-geometry validation.

Loft inputs are bounded to 2–4096 sections, supported V degree at most 128 after
clipping to section count, and one million final controls. V parameters, knot
averaging, positive-weight validation and the existing compatibility/seam rules
are unchanged. The former dense LinearSolver remains for curve interpolation and
is used by tests as a separately executed loft reference.

## Tests and reproducible measurements

`NurbsScene.LoftContracts` contains 17 cases: analytic conic points/curvatures,
arbitrary plane axes, exact seam, invalid scales, refinement/transform operations,
general multiple-RHS pivot permutations, zero-allocation warm solves, section
interpolation across multiple degrees, control/derivative comparison against the
former dense elimination, differing knot/degree compatibility, budgets and
cancellation, typed component execution, command/history/toolbar integration,
workspace persistence/display meshes, and the independent native C evaluator.

A test-only friend assembly reaches the internal factors/reference solver without
making them public package APIs. Native evaluator tests are mandatory in the new
workflow, not skipped when the library is absent.

The same `tests/loft-performance` harness runs against the prior factory and the
candidate, including compatibility preparation and complete output construction.
Release, tiered compilation disabled, two warm-ups, five samples and three lofts
per sample are used. Local runs were repeated in both sequential execution orders:

| Sections × U controls | Prior allocation | Candidate allocation | Reduction |
|---|---:|---:|---:|
| 16 × 32 | 416,160 B | 327,816 B | 21.2% |
| 48 × 96 | 7,258,784 B | 5,297,928 B | 27.0% |
| 96 × 64 | 10,472,992 B | 5,507,784 B | 47.4% |

The 96 × 64 local median was 29.11 -> 3.07 ms in one order and 29.86 -> 2.75 ms
in the other. Smaller-case timings varied more (16 × 32: 0.269 -> 0.172 ms and
0.263 -> 0.240 ms). These are CPU loft workloads, not universal speedups, input
latency, physical GPU throughput or FPS. Local baseline `7beb789` has the same
GeometryFactory/LinearSolver as merged PR #10. CI explicitly uses `0f021547`.
Raw timing/allocation samples and environment are preserved in artifacts.

A fifth mandatory actual Uno input campaign uses the toolbar, command console,
component search and exact baking. Read-only observation samples verify ellipse
equations and recovered loft sections; model changes use native mouse/keyboard
only. The prior four campaigns and differential framebuffer tests remain enabled.
Use final-head workflow evidence for its outcome; merely adding this source does
not establish browser qualification. Software GL can satisfy these tests.
