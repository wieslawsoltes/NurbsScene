> Historical local review. Its later divergence is reconciled in [the current panel-edge review](RECONCILED-PANEL-REVIEW.md); the validation statements below apply to their recorded commits.

# Panel layout, input lifetime and canvas performance

## Implementation

Text and data panels now have a bottom-right resize handle. Dragging changes an
isolated `PanelResizeDraft`; the authoritative graph, its revision and its solved
outputs remain unchanged until release. The preview changes panel bounds and
outgoing wire endpoints while retaining unrelated native Skia paths. Release
commits one graph edit; Escape, capture loss and an unchanged click are history-free.
Independent host edits are not undone by cancelling a draft. A replaced, changed
or deleted target invalidates the draft rather than resurrecting an old snapshot.

`NodeCanvasSize` belongs to node presentation metadata, not numerical component
parameters. Optional dimensions survive graph snapshots, clipboard fragments and
exact `.nscene` workspaces. Old documents without dimensions retain their previous
defaults. The general metadata accepts finite dimensions from 1 to 4096 units;
the panel editor uses a narrower display range of 128 × 76 through 2048 × 2048
canvas units. Dimensions outside that panel range are displayed clamped without
silently rewriting the stored value.

The reusable types are split between `NurbsScene.Parametric` (optional validated
node dimensions and efficient node lookup), `NurbsScene.Presentation` (layout,
drafts and bounded previews), `NurbsScene.IO` (optional dimension serialization),
and `NurbsScene.Controls` (pointer input, the handle and native drawing). No
application-host dependency is introduced in the model or presentation libraries.

The behavioral reference is McNeel's `GH_ResizableAttributes<T>`: independently
positioned bounds, size limits and mouse-driven resizing. This implementation is
not a binary-compatible replacement or an identical implementation of every
Grasshopper resize border, modifier and cursor behavior.

Reference:
https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Kernel_Attributes_GH_ResizableAttributes_1.htm

## Text and preview behavior

Legacy Text now uses the same draft-only editor as Panel instead of an eagerly
initialized multiline field whose focus-loss callback could truncate text.
`PanelTextDraft` preserves an untouched source's exact CR/LF sequence despite
native TextBox normalization; actual logical edits normalize line endings to LF.
The implicit legacy `NurbsScene` value, trailing empty lines, no-op Apply and redo
history are preserved. The earlier PR9 fixes enable `AcceptsReturn` before setting
Text and separate native text from the authoritative draft.

The preview cache keys immutable solved output, component kind and visible
row/column budgets. Renaming, moving or selecting a panel with unchanged output
does not reformat that output. Resizing adjusts wrapping and the visible row
budget. Truncated previews are marked with an ellipsis in the footer.

Formatting is bounded to 64 rows, 128 text elements per row, 8192 scanned UTF-16
code units in total and a 2048-unit prefix per string. Extremely long individual
graphemes are replaced in the display only. The original graph strings and their
serialization are not truncated. These are formatting budgets, not full complex
script shaping, pixel-measured font wrapping, scrolling of every item or complete
Grasshopper panel-display options.

The toolbar now passes the untrimmed query to the shared search index, retaining
trailing spaces when a literal suggestion is chosen. Operator symbols displayed
in the UI (`−`, `×`, `÷`) resolve to the same real components as their ASCII aliases.
Panel evaluation honors cancellation even on the unconnected persistent-text path.

## Grid freeze prevention

The former grid loop stepped a float in world coordinates. At a world coordinate
of 1,000,000,000, adding 25 produces the same float. A loop whose right bound lies
beyond that coordinate can therefore fail to terminate. A regression verifies
this numerical precondition without executing the hanging loop.

`CanvasGridLayout.TryCreate` computes grid spacing and phase in screen coordinates.
The renderer uses integer line indices, bounded to 2048 lines on each axis. Normal
viewport/zoom/pan cases retain the grid's world-aligned phase. Extremely large
viewports use coarser spacing. Invalid transforms are rejected without throwing
inside grid generation, and empty viewports contain no lines.

Zoom extents now checks proposed transform values before replacing the current
view; a nonfinite result reports a diagnostic and retains the old view. This fix
does not make the existing single-precision Skia node/path drawing a complete
large-coordinate renderer. Floating-origin graph drawing and full input-device
qualification remain separate work.

## Connection-plan allocation review

`ComponentContext.ConnectedInputs` must distinguish an unconnected optional input
from a connected empty data tree. Those names were previously rebuilt for every
evaluated component on every solve.

The immutable execution plan now retains ordered connection-name snapshots.
Definition port order is preserved even when wires were connected in another
order. Parameter edits and wire-transform changes reuse these arrays; connection
topology changes publish new snapshots. A captured in-flight solve retains its old
snapshot and becomes stale normally after a graph edit. Transform-only plan
replacement copies its input-wire table without rebuilding connection names.

Regression tests cover array identity across solves, definition-order stability,
graft updates, connected-empty data, disconnects and asynchronous stale solves.
This is an allocation optimization to CPU graph solving, not a GPU implementation,
an asynchronous UI scheduler or a change to numerical component semantics.

## Validation and evidence boundaries

The local verification includes the panel-layout contracts, canvas-review
contracts, all available managed test executables, regenerated independent SciPy
fixtures and the native C kernel. Exact counts, exit codes, source hashes and logs
are retained in the delivered validation evidence rather than inferred from test
names. Existing native/WASM/JavaScript package checks are separate from Uno checks.

`tests/panel-review-performance` is copied unchanged into baseline PR9 commit
`08ea6f481fe44c6f0ae6633139a9751533500662` and run against baseline and candidate
sequentially. It measures a 400-node parameter update/full CPU solve, panel-preview
formatting after a rename and unchanged preview reads. It uses Release builds,
tiered compilation disabled, 20 warm-up calls and seven measured samples.
All timing/allocation samples are retained. None is a physical-GPU, FPS,
rasterization or end-to-end application-speed measurement.

The original PR9 artifact passed GitHub run `38249453651`: 29 managed/framebuffer
checks and 91 real pointer/keyboard checks. Its exact site was also attempted
locally, but Chromium blocked navigation with `ERR_BLOCKED_BY_ADMINISTRATOR`.
The blocking policy was not altered or bypassed. This older artifact does not
qualify the new panel-resize implementation.

The new pointer/keyboard scenarios cover live draft isolation, commit/undo/redo,
Escape, unchanged handles, retained multiline text, legacy Text and preview reuse.
They remain **unexecuted** in this local environment. The pinned Uno SDK is not
available in the local offline cache, so no fresh Uno desktop/browser build is
claimed. The new source requires the ordinary Uno CI workflow before merge or
deployment. No test assertion or deployment gate was weakened.

## Recovery and publishing

The interrupted source tree `de6be7adb6b3f9acc50b7fc3f0a0f73729e6ed26` was recovered
byte-for-byte. Original commits `1012b8a0` and `b75233a6` were reconstructed from
verified Git objects and metadata with their original hashes and parentage. The
formerly uncommitted integration tree is preserved in local commit `7c31d3e5`,
followed by separate grid, execution-plan, lifetime and benchmark commits.

At this pass's remote audit, main remained `e96ecda3`, PR9 remained draft/open at
`08ea6f48`, and the panel branch's published head was `b75233a6`. The active
GitHub connector exposed no write actions; the command-line transport could not
resolve github.com. Local recovery/commits are not represented as a remote push,
PR merge or Pages deployment.

The NURBS kernel and GL/WebGL2 renderer are unchanged. Robust solid/B-rep modeling,
arbitrary trims, SubD, complete Rhino/Grasshopper commands/components/formats/plugins,
identical UI, complete docking/CPlanes/Osnap and direct Uno WebGPU remain unfinished.

## Final local source verification and concurrent repository changes

The complete pure-managed Release build succeeded with zero warnings and errors.
All 20 managed test executables passed: 358 counted cases across 19 suites, plus
the managed rendering/interchange integration executable. The current code also
passed 12 native Python tests, 60 Node/WASM tests, and six isolated npm package
consumers. These results do not imply execution of the new Uno UI controls.

A later repository read showed PR9 merged independently as
`b69880bb71c580734df21f7aade68014e160360d`, with PR10 open at
`cb3721cdba1168ae406ba570af3f21154e1b36a2`. No remote write was performed by this
local continuation. To avoid overwriting those parallel changes, this review is
preserved on `review/panel-layout-offline-20261012`. It contains the exact recovered
tree and subsequent review commits; it does not claim to include PR10's later
campaign-isolation changes.

The attempted normal push of the review source to the original panel branch
failed with exit code 128 because github.com could not be resolved. No force-push
was attempted. The delivered bundle is self-contained and should be reviewed or
cherry-picked alongside the newer remote work, not used to reset the remote branch.
