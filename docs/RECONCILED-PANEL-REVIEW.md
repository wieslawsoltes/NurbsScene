# Reconciled panel-edge and qualification review

## Preserved history

This review merges the saved local head `62ddd94929fac12dfd941b112fcfb2b598b80136`
with the actual PR #10 head `cb3721cdba1168ae406ba570af3f21154e1b36a2`.
The latter was recovered through authorized GitHub reads, with its original
commit IDs and exact tree `4b7b46e966276bbeed793237426fe0491663686e` verified.
The local merge is `74c9ef1`. Neither previous work nor the newer PR's readable
panel text, retained wire-cache passes or isolated campaigns was overwritten.
This is a **local Git merge**, not a GitHub pull-request merge.

A final repository read found late head `a4800b5e13b368ca4e4c231439b0464df4d20f26`.
Its exact tree `9cc7739fec7e78832e4911431788aa56bfe3fb15` and original commit ID
were also recovered, then reconciled in local merge `7313146`. The late commit
changed only Python startup helpers/tests, not the measured runtime libraries.
Its six scenarios are retained alongside the eleven local contracts. The
`wait_ready` name and `READY_EXPRESSION` remain compatible, while all waits now
use the stronger captured-snapshot behavior. No late remote change was discarded.

The previous local fixes remain: bounded screen-space grid iteration rather than
non-advancing large-coordinate float loops; retained ordered connection metadata
in execution plans; stale graph/resize/wire guards; and literal whitespace,
operator-symbol and cancellation handling. Numerical kernel and GL/WebGL2 backend
code are unchanged. Historical reports describe their own tested revisions, not
this review's qualification.

## Four borders and four corners

`PanelResizeEdges` and `PanelLayout.HitTest` provide all eight resize directions.
Corners have wider targets; thin border strips leave the title and body available
for moving and editing. Port hits retain precedence. Selected panels draw original
edge/corner grips; the old lower-right affordance remains. The existing two-argument
`PanelResizeDraft` constructor and lower-right `IsHandle` API remain available.

Left/top resizing adjusts origin as well as dimensions, keeps the opposite edge
anchored, and clamps at minimum width 128, minimum height 76 and maximum dimension
2048 canvas units without flipping. Invalid or unrepresentable coordinates reject
before publishing a draft. Pointer motion changes only a draft. Release records
one position-and-size update; Escape or loss of capture discards it. A stale or
deleted target cannot be resurrected. Independent graph edits remain intact.

`MapPort` keeps input/output port row offsets consistent with the previewed origin.
Both incoming and outgoing wire endpoints, conservative wire bounds and transformed
wire labels follow the draft. Only affected temporary paths are rebuilt/disposed.
A committed resize modifies presentation metadata, not component parameters or
execution topology; solved numerical output objects remain reusable. Position and
size survive exact workspace/fragment interchange and undo/redo together.

The behavioral reference is McNeel's documented
[resizable attributes](https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Kernel_Attributes_GH_ResizableAttributes_1.htm).
These are independently implemented controls, not identical Grasshopper border,
font, scrolling, accessibility or every input-device behavior. There is no new
solid/B-rep, arbitrary trimming, SubD, proprietary format/plugin or direct Uno
WebGPU implementation in this review.

## Input-campaign startup race

The published PR #10 [Uno run 38256495288](https://github.com/wieslawsoltes/NurbsScene/actions/runs/38256495288)
passed its first campaign, then called `.get` on a missing UI observation at the
start of `inputs-sliders`. Modeling qualification is published before the first
UI sample. A predicate using `missing?.status !== "ready"` evaluates true and
prematurely releases a negative-status wait.

The runner now explicitly waits for a concrete ready observation before **each**
fresh campaign. Shared helpers treat missing/running observations as pending,
report failed or prematurely restored state immediately, validate the ready
snapshot structure, and dispose the exact handle returned by the satisfied wait.
They do not make a second global read which could observe a later phase. Input
settling still requires two fresh samples; restoration requires an actual restored
snapshot. Timeouts propagate without retries, availability skips or GPU mocks.
The wait behavior follows the [Playwright contract](https://playwright.dev/python/docs/api/class-page#page-wait-for-function).

Seventeen focused unit contracts execute the production JS predicate in Node's VM
and exercise Python fake-handle adapters. They reproduce the old missing-state
truthiness and verify cleanup, failures, deadlines and sample freshness. These
are waiter **unit tests**, not evidence that Uno or a GPU ran. Existing actual-input
checks remain mandatory. The late-remote scenarios use adapted fake handles and explicit valid snapshot
fixtures, preserving their original ordering/error assertions. A fourth isolated campaign adds real mouse workflows for
every edge/corner and independent undo/cancel; those new workflows still require
actual Uno qualification.

## Verified local scope

Implementation commit `899cad039c40ba25a5340cdc9be16914535ee292` passed the
pure-managed Release build with zero warnings/errors. All **21 managed test
executables** passed: **374 counted cases** plus the integration executable. This
includes 19 retained panel-layout contracts, 15 prior canvas-review contracts and
14 new edge contracts. The latter include 16,000 deterministic randomized motion
steps, opposite anchors, input/output mapping, stale owners, exact interchange and
numerical-result identity. Warm hit testing, draft motion and port mapping across
10,000 iterations allocated zero bytes on the tested runtime.

The native C library was rebuilt, independent SciPy fixtures regenerated, and
12 native Python tests passed. The existing Node/WASM suite passed 60 tests;
six packed npm consumers passed. Twelve existing offline JavaScript browser
harness scenarios also passed. That harness is separate from Uno, includes its
own limited setup hooks, and does not assert GPU success.

Eight managed libraries were packaged as `0.1.0-review.899cad0`. A fresh consumer,
with no project references and an isolated package cache, passed eleven checks
including actual all-edge API use, exact interchange, undo, cache identity,
geometry/mesh interoperability and Workbench construction. These are local package
artifacts, not a NuGet.org release or a compiled Uno Controls package.

## Comparison with the initial PR #10 head

An identical benchmark was built against the recovered PR #10 head and the
candidate. Release builds, tiered compilation disabled, 20 warmups and seven
samples were used, with both sequential execution orders. The 400-node,
399-wire changed-parameter/full-solve workload allocated **2,191,696 -> 2,130,928
bytes per operation**, a reduction of **60,768 bytes (2.77%)**, in both orders.
Renaming a panel preview stayed at 184 bytes and unchanged preview access at zero
bytes on both revisions: that optimization was already in the recovered PR.

Solve medians were 3.8392 -> 3.621195 ms in one order and 3.89631 -> 3.799955 ms in
the reverse order. Timing variation does not establish a general application
speedup. These are CPU solve/preview workloads, not renderer, physical-GPU, input
latency or FPS measurements. Raw samples, environment and harness hashes are kept
in the accompanying validation archive.

## Publication and UI qualification boundary

The available GitHub actions in this session expose reads but no write/merge
operation. A normal push to the new review branch failed DNS resolution for
`github.com`; no alternative transport or policy bypass was used. Source, complete
history, a patch relative to current PR #10, packages and test evidence are
preserved for publishing. Existing main/PR state was not changed by this session.

The exact Uno build attempt failed to resolve pinned `Uno.Sdk` 6.7.30 in the
supplied offline dependency cache. New Uno code and actual-input scenarios are
therefore **not platform-compiled or browser-qualified here**. Pure C# syntax,
Python syntax, managed library tests and an older workflow's success do not replace
that qualification. Do not merge or deploy solely on this document; run the full
final-head workflows and all four real Uno campaigns first. Full product parity
is not claimed.
