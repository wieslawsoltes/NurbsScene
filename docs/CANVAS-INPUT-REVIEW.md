# Canvas input review — 2026-10-13

## Delivery status

This is a **local, unqualified managed/Uno candidate**, not a published release.
Its source base is the authentic PR #13 head
`9b7b88d0a8314fe5da25f21a770be0d22dce64da` with tree
`341fa878b8f6f0295aaf14097ae6f4d237cffcd4`. During this work, GitHub reported
PR #13 merged as `340281d7e3ec6fce01012ba6aa6769f750479031`, with the identical
source tree. That remote merge was not performed by this session.

The recovered CI source and Git bundle have verified SHA256 checksums. They do
not recover unrelated, previously unuploaded working files from another session.
This session's GitHub actions expose reads only. A direct push of
`feat/canvas-input-review` failed because `github.com` could not be resolved.
No new PR, remote candidate commit or candidate deployment is claimed.

## Changes in this candidate

`GraphSelectionModifiers` defines replace, add, remove and toggle selection
without altering graph data or history. A plain click on an already selected
component preserves the selected group for dragging. Shift adds; Control removes;
Control+Shift toggles. Modifier-aware window/crossing selection uses the same
policy. Repeated hits are deduplicated with the host selection set's identity
comparer. Validation completes before a selection set is changed.

The interaction reference is David Rutten's original
[canvas shortcuts explanation](https://www.grasshopper3d.com/forum/topics/what-hotkeys-and-shortcuts-are-available-in-grasshopper).
The color-coded marquee and active dashed guide are independently authored
feedback, not a claim of pixel-identical original artwork.

`GraphDragPositions` captures node origins once and reuses a read-only live
position buffer. Every pointer displacement is computed from the initial origins,
not accumulated from previous rounded moves. Shift projects to the nearest of
eight horizontal, vertical and 45-degree rays. Finite-coordinate, identity and
10,000-node limits are validated; overflowing updates leave the buffer unchanged.
The returned view is intentionally live: copy it before retaining a snapshot or
using it asynchronously.

The native canvas consumes these policies and adds ownership checks for the
active pointer and initiating mouse button. Secondary pointer presses or mouse
button releases cannot overwrite a primary gesture's rollback snapshot. Final
release coordinates are applied even when the host coalesces the last move.
Wheel zoom is suppressed during active gestures. Escape releases capture and
restores an uncommitted drag or the pre-marquee selection. Delete and zoom extents
cancel the draft before applying their action. Session replacement, unloading and
disposal clear drafts and capture.

Independent graph mutations abandon stale node/slider continuations rather than
restoring an entire earlier graph over the host's change. Positions already
published by that gesture are retained when an independent host change occurs;
this is not a general concurrent-edit transaction system. Touch, pen, focus-loss,
and reentrant-host event matrices still require native acceptance coverage.

Selection feedback distinguishes subtract/toggle operations by color and crossing
rectangles by a dashed border. Shift-constrained drags show a dashed active guide.
Full panel scrolling, measured paragraph wrapping and full font shaping were **not**
implemented in this continuation.

## Performance scope

The reusable drag buffer removes the caller's per-move LINQ projection, closure
and position-array construction. It does **not** remove all allocations in
`Graph.MoveNodes`, presentation rebuilding, native input dispatch or rendering.
A new contract asserts zero warm allocations for the buffer and an already
selected additive click only. That managed contract was written but could not be
executed here. No measured speedup, FPS or whole-application zero-allocation result
is claimed for this candidate.

## Validation actually run

`CHROMIUM_PATH=/usr/bin/chromium bash tools/validate.sh` passed on the candidate
source: native C and WebAssembly builds, 12 Python/native numerical tests, 60
Node/WebAssembly tests, six isolated npm package consumers, 12 actual Chromium
offline-harness tests and structural project/XAML/ESM checks (36 projects).
The harness is the separate JavaScript application, not the changed native Uno
canvas, and does not qualify GPU rendering.

All 21 observation/coordinate contracts passed. Python compilation and
`git diff --check` passed. An initial harness attempt failed because Playwright's
bundled browser executable was absent. Configuring its existing `CHROMIUM_PATH`
option to the installed system Chromium resolved that infrastructure failure;
assertions and application code were not changed to obtain the passes.

The .NET SDK and native Uno build toolchain were not available in this session.
The new **16 managed pointer contracts**, changed Controls assembly and added
native-input campaign have **not** been compiled or executed here. XML/import
validation is not C# compilation.

## Acceptance gate registered in source

The existing editing workflow now runs:

```sh
dotnet run --project tests/dotnet/NurbsScene.PointerContracts -c Release
```

The real Uno runner retains all seven existing campaigns and adds an eighth,
`pointer-selection`, using real pointer/keyboard input and observed native bounds.
It covers modifier clicks and rectangles, cache preservation on selection,
marquee cancellation, constrained movement, live modifiers, undo/redo, drag
cancellation, secondary mouse buttons, wheel suppression, Delete during drag and
zoom extents during drag. The old incorrect Shift-toggle expectation is replaced
with an additive-selection assertion, not removed.

The candidate must pass managed compilation, all contract suites, actual desktop
and WebAssembly compilation, and all eight Uno input/framebuffer campaigns before
merge. No previous-head CI pass qualifies these new changes.

## Previously qualified source, not candidate results

PR #13's final head passed all 22 workflows. Recovered browser evidence contains
232 input assertions across seven campaigns, no page errors, and the same 29
managed/modeling/framebuffer checks repeated in each separate campaign. Repeated
checks are not additional distinct features. The browser evidence archive has
SHA256 `db815153305520f52e6605666fde32dc402c9689c6821da948a8897f40985a84`.

The source evidence archive has SHA256
`62cb92579c600f161fffaeac3b001694ed22c75cf82783357d33baf688bd83aa`.
The downloaded archives, internal bundle/source checksums, exact tree identity
and prior native screenshot were inspected. No newer main/Pages acceptance is
inferred from them.

NurbsScene remains an independent alpha, not full Rhino/Grasshopper parity; the
[compatibility inventory](PARITY.md) retains all numerical, solid-modeling,
interchange, rendering, component-catalog and UI boundaries.
