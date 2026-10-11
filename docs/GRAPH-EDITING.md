# Definition editing and incremental drawing

## User-facing editing

The definition panel adds native Edit, Arrange, View and Solution menus, plus
component/background context menus and a search flyout at the canvas cursor.
This follows Grasshopper's menu/selection conventions without claiming identical
visuals or its complete editor behavior. Reference contracts include McNeel's
[component context-menu guide](https://developer.rhino3d.com/guides/grasshopper/custom-component-options/)
and [GH_DocumentIO](https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Kernel_GH_DocumentIO.htm).
No application source or proprietary assets are used.

When the graph canvas has focus, Ctrl+C/X/V copy, cut and paste selected components;
Ctrl+D duplicates, Ctrl+A selects all, and Ctrl+Z/Y undo/redo graph history. Native
text inputs retain their own text-editing shortcuts. Right-click opens a context
menu, while right/middle dragging pans. Left dragging performs a selection window:
left-to-right selects enclosed node bounds, right-to-left selects intersecting
bounds. Shift-click toggles a node out of or into the selection. Shift+F10 opens a
keyboard-accessible context menu. This is not the complete Grasshopper shortcut,
pointer, accessibility or pen/touch contract.

Menus operate on real components: enable/disable, preview on/off, duplicate,
clipboard actions, delete, baking and property editing. Input submenus disconnect
actual wires. Alignment uses node bounds, including different slider/component
sizes. Distribution spaces centers equally while retaining the outer centers;
insufficient space can still cause overlap. Alignment and distribution are one
undoable graph edit and preserve numerical results and topology.

Double-clicking empty canvas or pressing Space opens search at the canvas cursor.
Only executable registry components are suggested. Enter adds the selected
component; subsequent search openings do not retain an active suggestion popup.
The component inspector is available in narrower definition panels and updates
result messages after a solve without rebuilding its editing controls.

## Reusable fragments and exact interchange

`GraphFragment` in `NurbsScene.Parametric` is an immutable selection of nodes and
internal wires. Capture retains selection order as it appeared in the source
snapshot. External connections are intentionally omitted. Paste assigns new node
and wire identities, preserves parameters, matching, enabled/preview flags and
wire transforms, validates the destination registry and final graph budgets before
editing, then applies one `GraphSession.Edit`. It never evaluates components while
copying, deserializing or validating. Empty paste is a no-op. The public snapshot
method returns detached arrays, and input parameter values are frozen.

`GraphFragmentFormat` in `NurbsScene.IO` uses the existing exact workspace value
codec with an empty document and the header `NurbsScene.GraphFragment/1`. Its limit
is 8 MiB of UTF-8 text. Typed trees, paths, explicit nulls/empty branches and weighted
NURBS controls/knots survive round trips. This is **not** Grasshopper's clipboard,
`.gh` or `.ghx` format, and it does not copy plugins or executable implementations.
Unsupported payloads, document objects or missing component definitions fail.

Copy attempts the native/system clipboard and also retains one application-local
payload. When the platform clipboard is unavailable, an explicit message explains
the application-only fallback. Paste guards the graph identity and revision across
asynchronous clipboard reads, so a changed definition is not silently overwritten.
A foreign text clipboard is not interpreted as code. Cut serializes successfully
before removing selected nodes. Insertion offsets use canvas coordinates rather
than display pixels, independent of zoom.

## Incremental rendering metadata

The graph exposes cheap `NodeCount`/`WireCount` and a `TopologyRevision` separate
from ordinary revision changes. Add/remove/connect/disconnect changes topology;
parameters, positions and presentation flags do not. Existing execution plans,
solver cache and stale-result handling retain their own semantics.

`GraphPresentation` now retains unchanged `PresentedNode` and `PresentedWire`
objects across edits. A node-only update scans a node snapshot, updates changed
node records and recomputes only incident wires when node bounds actually move.
An adjacency index avoids recomputing unrelated endpoints. Parameter and label
changes retain the entire immutable wire array. Topology changes rebuild the index
but still reuse unchanged records where safe. A replaced graph cannot accidentally
reuse old records merely because its node identities are the same.

The Uno canvas caches native Skia paths by presented-wire identity and disposes
only replaced or removed paths. A number-slider parameter change can update the
solution and displayed value without rebuilding a single wire path. Status updates
use cheap counts rather than copying whole graph collections. Drawing still scans
visible candidates, and node-only changes still scan/copy snapshot arrays: this is
not constant-time graph editing, a new spatial index, or a direct WebGPU renderer.
The kernel and depth-tested GL/WebGL2 renderer are unchanged.

## Tests and performance qualification

The editing contract executable has 24 cases covering incremental-versus-cold presentation,
random move traces, incident-wire reuse, topology changes, immutable snapshots,
allocation limits, window/crossing selection, low-zoom port ambiguity/tie handling, fragment identity/remapping, invalid/cyclic data, exact tree
and NURBS serialization, alignment, distribution, and atomic undo/no-op behavior.
Existing graph, model, kernel, native and package suites remain enabled.

`editing.yml` copies **identical** benchmark source into previous main `1d41fc57`
and runs that baseline and candidate sequentially on one runner. Workloads contain
1,000 nodes and 999 wires and measure cold metadata construction, unchanged access,
a single position edit and a single label edit. Release, tiered compilation off,
20 warm-up calls and seven measured samples are used. Artifacts preserve all raw
samples, allocation counts, environment and tested commit/tree. These are metadata
maintenance measurements, not rasterization, input latency, component execution,
GPU pass time or application FPS. A structural regression also bounds sparse-edit
allocation independently of timing.

The expanded real Uno browser suite exercises keyboard fragments, history,
context menus, alignment, selection windows, Shift selection, panning, slider-path
reuse, cursor search/reopening and text-focus isolation. It sends real mouse/key
input to observed native-control/Skia bounds. The observation hook does not dispatch
modeling commands, replace DOM content or mock rendering. Clipboard permissions
are granted to the test browser; denied-permission behavior is a separate platform
boundary. Existing modeling and differential framebuffer checks remain. Use each
commit's artifacts for actual pass/fail evidence, not this list as a claim of full
browser, desktop, hardware or product qualification.

## Low-zoom port-selection review

The expanded browser run reproduced a pre-existing ambiguity after the added menu
row reduced the available graph height: at approximately 42% zoom, adjacent ports
were separated by less than the 9-pixel hit tolerance. The old first-row matching
algorithm selected Circle's Center input when the pointer was exactly on Radius,
rejecting the intended numeric wire as incompatible. `GraphHitTesting.PortAt`
now chooses the nearest port by squared distance, with frontmost-node tie-breaking.
The Uno canvas uses that shared implementation; tests cover five zoom levels,
radial misses, zero tolerance, nonfinite input and overlapping node ties. The
original actual-port dragging regression is retained unchanged.

## Input routing and diagnostics

Graph keyboard accelerators are registered on the editor ancestor, with a focused-
canvas guard. This avoids the framework's scope-owner lookup excluding the owner
itself, while native text inputs keep their own Ctrl+A/C/X/V/Z behavior. Browser
checks exercise those shortcuts through actual key input rather than directly
calling the editor actions. `GraphSelection` defines window/crossing containment
independently of the graphics library and is covered by pure geometry contracts.

Pointer diagnostics are opt-in and disabled for ordinary use. The observer retains
at most 64 routed event records, containing coordinates, gesture name and selection
count; it does not inject input, dispatch commands or change gesture behavior.
The qualification build enables it for the disposable interaction test so input
routing failures can be inspected rather than hidden with skipped assertions.

## Reviewed implementation evidence

Implementation head `86a69bccd947f0bb0f46c214548df5af9efa1ec5`, tree
`63202053218f34033d9d789dfb27dbdddaf561fc`, passed the real Uno desktop/browser job
[38178649096](https://github.com/wieslawsoltes/NurbsScene/actions/runs/38178649096).
The downloaded report contains 29 managed/framebuffer checks and 43 actual-input
checks, zero page errors and 161,740 modeled-surface pixels different from the
empty framebuffer. Software GL may satisfy the test; this is not a physical-GPU
or FPS qualification. Later heads must pass their own complete workflow before
merge, and Pages deploys only the exact successful main artifact.
