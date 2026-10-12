# Number-slider editor and retained graph views

## Recovered work

This continuation starts at main `49601b987577ac796609950a917e9ac9c5f18246`
(PR #7). The previous interrupted pass had created core commit `130f7af7` and
controls tree `1f33ba41`, but had not advanced the slider branch. Both were
recovered into PR #8 before completing the canvas/menu integration. No live
uncommitted worktree survived; archived evidence was not mistaken for new source.

## Slider behavior and reusable APIs

`NumberSliderSettings` and `NumberSliderEditing` are UI-independent Presentation
APIs. Settings validate the complete finite range, value and precision together.
Floating sliders retain binary64 values; Integer components retain signed 32-bit
whole values. Automatic precision preserves the former continuous scrub behavior.
Explicit precision supports 0–12 decimal places, rounds interior values using
round-to-even, and preserves exact range endpoints. Opposite-sign extreme bounds
are interpolated without overflowing the range width. Invalid imported metadata
is reported by the canvas rather than throwing inside its draw callback.

The reusable Uno `NumberSliderDialog` edits a draft. The label double-click,
component context menu and inspector button open that same editor. Apply changes
range, value, precision and nickname in one graph history entry; invalid Apply
keeps the dialog open. Cancel and Escape do not alter the graph or clear redo.
Graph and node identity guards reject stale edits after undo, loading or another
host's edits. Closing/disposal cannot apply a pending draft to another session.
The rail remains a direct scrub target. Inspector values are synchronized after
rounding or a rejected entry rather than displaying a number the graph did not
accept. Existing F2 command-console focus is not reassigned.

The visual reference is Grasshopper's number-slider settings and its
[documented slider properties](https://developer.rhino3d.com/api/grasshopper/html/Properties_T_Grasshopper_GUI_Base_GH_SliderBase.htm).
This is not identical Grasshopper behavior: Grasshopper's slider API exposes
[decimal values](https://developer.rhino3d.com/api/grasshopper/html/P_Grasshopper_GUI_GH_Slider_Value.htm),
while NurbsScene uses binary64. Even/odd slider modes, expressions, animation,
remote-control panels, numeric shorthand search and every original formatting
option are not implemented here. Decimal places do not certify exact decimal
arithmetic or change the mathematical kernel's precision. Existing catalog counts
remain 112 managed / 102 JavaScript components.

## Retained views and drawing metadata

Graph.Nodes and Graph.Wires now return retained read-only collection snapshots,
not fresh arrays on every read. Node mutation invalidates only the node view;
connection/transform mutation invalidates the wire view. Published views never
change and cannot expose a mutable array. Graph.Snapshot continues returning
independently mutable arrays for history/interchange. Graph reads remain under
the existing gate. ComponentRegistry.Definitions similarly retains a read-only
view until successful registration; registration retains its existing owner-thread
contract. No registry-removal/replacement or concurrent-registration API is added.

GraphPresentation distinguishes wire-metadata revision from topology changes.
Changing flatten/graft/simplify updates the wire's presentation record without
rebuilding the adjacency map, port coordinates or native Skia path. Mixed node
movement and wire-transform edits are compared with fresh drawing geometry in
regressions. Changed revisions still scan/copy affected snapshot arrays: this is
not constant-time editing, a spatial-index redesign or a direct WebGPU backend.

## Qualification

The slider contracts cover range extremes, subnormal/narrow intervals, rounding,
invalid metadata, stale edits, atomic undo/redo, no-op history, serialization,
wire reuse and zero-allocation warm mapping. The snapshot contracts cover view
identity, immutable publication, all mutation categories, graph replacement,
no-op preservation, drawing adjacency and mixed updates.

```sh
DOTNET_TieredCompilation=0 dotnet run --project tests/dotnet/NurbsScene.SliderContracts -c Release
DOTNET_TieredCompilation=0 dotnet run --project tests/dotnet/NurbsScene.SnapshotContracts -c Release
```

`snapshots.yml` runs identical 1,000-node/999-wire workloads against previous main
`49601b98` and the candidate on one runner. Release, tiered compilation disabled,
20 warm-up calls and seven samples are used. JSON preserves every timing and
allocation sample. Measurements concern collection access and wire metadata
maintenance, not solver execution, frame rendering, physical GPUs or FPS.

`test_sliders_ui.py` extends the real Uno input campaign. It uses mouse/keyboard
input for modal settings, invalid Apply, precision selection, undo/redo, Cancel,
label double-click, actual rounded scrubbing, keyboard context-menu entry and
Escape. The observation hook reports bounds/state and does not invoke actions.
Existing numerical, package, native, modeling and differential GL-framebuffer
checks remain enabled. Per-commit Actions artifacts are the qualification record;
this scope list does not assert full product/browser/desktop/hardware coverage.

Full Rhino/Grasshopper parity, robust B-rep/solid modeling, arbitrary trims, SubD,
complete formats/plugins/commands, identical UI, complete docking/CPlanes/Osnap,
and a direct Uno WebGPU renderer remain unfinished. This pass retains the existing
NURBS kernel and GL/WebGL2 renderer.
