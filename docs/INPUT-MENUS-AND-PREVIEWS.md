# Input menus, bounded data previews and retained shelves

## Editor behavior

Component context menus now expose nested **Data matching** and **Inputs** menus.
Longest/shortest/cross matching updates the actual component. Each connected input
can set its current incoming wires to as-wired, flatten, graft or simplify, or
disconnect them. Changes are one undoable batch; wire identities, endpoints and
fan-in ordering survive. The canvas displays F/G/S badges for transformed wires.
Unconnected-input transform actions are disabled. This is the existing per-wire
NurbsScene transform model, not persistent Grasshopper parameter flags, arbitrary
stacked transforms or complete Grasshopper input-conversion compatibility.

Menus use compact 11-point labels and 24-pixel minimum rows. Authored arithmetic,
trigonometric, tree and geometry-family symbols replace repeated category icons
where implemented. The component shelf and canvas share the same symbol mapping.
These improve identification and density, not pixel-identical proprietary artwork.
The reference menu structure is documented in McNeel's GH_Component menu API:
https://developer.rhino3d.com/api/grasshopper/html/M_Grasshopper_Kernel_GH_Component_AppendAdditionalMenuItems.htm

The component inspector previews output paths, item counts, explicit nulls, empty
branches and bounded item samples. Geometry previews show exact type/degree/control
metadata rather than evaluating or triangulating geometry. Additional branches and
items are reported as omitted, never silently presented as the whole result.

## Reusable contracts and performance

`Graph.Incoming` returns a detached ordered snapshot through the maintained input
index. The inspector no longer copies and scans every unrelated wire for each
port. `SetWireTransforms` validates an entire batch before mutation, updates each
affected fan-in in one pass, and retains the topological execution order. Its
copy-on-write input plan leaves in-flight solve snapshots intact; destinations and
descendants are invalidated, and stale solves cannot publish over newer edits.
There is still dictionary/snapshot copying; this is not a constant-time solve or
an application-wide speedup.

`TreePreview.Create` in Presentation has explicit branch/item/text budgets. Work
is bounded by those preview limits and path depth, not the complete tree size.
It never calls unknown plugin ToString methods. Numeric strings are invariant;
control characters are escaped, and UTF-16 pairs remain intact on truncation.

Native shelf pages/buttons are retained after first use and reused when revisiting
categories. Replacing the registry invalidates those cached pages. This trades
bounded per-registry retained controls for less construction/GC on tab switching;
it is not zero-allocation layout or frame rendering. Native wire paths are retained
when only metadata transforms change, because their endpoints remain unchanged.

## Review fixes

Node presses no longer capture a full undo snapshot until movement crosses the
existing drag threshold. Sub-threshold pointer jitter cannot mutate model positions
without an undo record. A drag returning to its original snapshot adds no empty
history entry and does not discard redo. Escape restores an active drag. Replacing
a graph clears obsolete gesture state before releasing capture, preventing a later
capture-lost callback from restoring an old graph over the new one.

## Qualification

`NurbsScene.InputContracts` adds twenty contracts for all transform modes, fan-in
ordering, immutable snapshots and in-flight plans, invalid atomic batches, indexed
allocation, clipboard/undo/redo, bounded previews, exact metadata, culture/escaping,
UTF-16 handling and no-op history. Existing suites remain enabled.

`inputs.yml` runs identical input-query benchmark source on baseline PR #6 main
`37fd94a29c5e4c681c81e6cf3a5349f8c9d46c6e` and candidate on one runner. The baseline
path is the former inspector's full wire scan; the candidate calls Incoming through
a delegate resolved before measurement. Each query returns the same detached
one-wire snapshot amid 1,501 total wires. Release, tiering disabled, 100 warmups,
seven samples and 10,000 queries per sample are recorded with raw timings/allocated
bytes and exact source revision. These are input lookup measurements, not FPS,
GPU timing, mesh generation or complete menu-opening latency.

The actual Uno browser suite retains all earlier assertions and adds real-pointer
nested-menu selection, matching/transform changes and undo, native-path retention,
scrolled data previews, jitter/cancel behavior, shelf revisit allocation counters
and executable cached buttons. Its compile-time hook only reports observed visual
bounds/state; it does not dispatch commands or replace the real renderer. Per-head
Actions artifacts establish pass/fail results. Physical-GPU/desktop runtime,
accessibility, full pointer-device and full Rhino/Grasshopper parity are not implied.
