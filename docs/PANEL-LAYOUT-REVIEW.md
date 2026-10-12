# Panel layout, persistence and review

## Source recovery and behavioral reference

PR #9 was qualified and merged as `b69880bb71c580734df21f7aade68014e160360d`.
The interrupted resize pass had published the core and text-draft merge commits,
and uploaded the native-control tree without committing it. That exact tree,
`de6be7adb6b3f9acc50b7fc3f0a0f73729e6ed26`, was recovered into PR #10 before
further edits. A source-evidence bundle restored the complete working history;
no live uncommitted working directory survived in the available environment.

The behavioral reference is McNeel's [resizable attributes API](https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Kernel_Attributes_GH_ResizableAttributes_1.htm),
which separates bounds, input/output grips and sizing limits. This implementation
uses independently authored Uno/Skia controls and does not implement or copy the
proprietary class. It is not full or pixel-identical Rhino/Grasshopper parity.

## Working resizing and text editing

Panel and legacy Text nodes expose a bottom-right grip. A `PanelResizeDraft`
retains the target graph/node identity and initial bounds. Pointer motion updates
only the draft; the graph revision, execution plan and numerical result remain
unchanged until release. The canvas draws the draft bounds, output port and
attached outgoing wire positions, using temporary paths only for affected wires.
Release creates one history entry. Escape/capture cancellation discards the draft
without restoring an old graph snapshot or removing unrelated edits. A changed
node or replaced graph causes a stale commit to fail explicitly. A stationary
handle click does not create history or erase redo.

`NodeCanvasSize` is optional immutable presentation metadata, not a numerical
component parameter. It participates in node equality, snapshot/history and exact
workspace/clipboard serialization. Missing dimensions preserve earlier defaults;
invalid external dimensions are rejected. The panel renderer applies its narrower
128 x 76 to 2048 x 2048 size range. Layout edits invalidate presentation but do not
dirty component results. This extends managed workspace version 1 with an optional
field; older readers may ignore the dimensions. No Grasshopper or Rhino file
compatibility is implied.

The legacy Text inspector now uses the same draft-only multiline dialog as Panel.
Opening text first enables multiline and then assigns the original content. Native
TextBox line-ending coercion cannot truncate reopened text or manufacture an edit:
`PanelTextDraft` returns the exact original CR/LF sequence when logically unchanged,
and normalizes actually edited text to LF. Cancel and unchanged Apply preserve
revision/history. Stored text is still ignored while a Panel has connected data,
including an explicitly empty connected tree.

## Rendering and performance

`PanelPreviewCache` keys text formatting on immutable result identity, component
kind and bounded visible row/column budgets, not the whole node identity. Rename,
move and selection reuse the formatted output. Layout changes reformat only when
the visible text budget changes. Formatting limits are 64 rows, 128 text elements
per row, 8,192 scanned UTF-16 units and 2,048 units per input item. Huge combining
sequences are bounded; an ellipsis in the footer explicitly marks omitted content.
These are display prefixes, never truncated authoritative text or geometry.

`PanelDisplayLayout` uses an eight-screen-pixel body-text floor for sufficiently
large panels. A resized panel therefore remains useful below the ordinary
component-detail zoom threshold; small panels keep an inexpensive overview.
Columns use a conservative width estimate and the canvas still clips to bounds;
this is not full typography, font fallback, bidirectional shaping or pixel parity.

The native wire cache now checks immutable wire-array identity before reconciling
paths. A parameter-only slider edit retains the array and skips the complete wire
walk and its live-ID set, rather than merely avoiding new paths after that walk.
Position, size, topology and wire-transform updates still reconcile as needed.
Node snapshot scanning and visible-candidate traversal remain linear.

The resize/preview contract requires zero managed allocation for 10,000 warmed
preview lookups plus draft moves. The browser slider scenario checks that the
native wire-cache pass counter does not advance for its parameter change. These
are bounded structural/allocation checks, not old-version timing comparisons,
whole-frame cost, input latency, solver speed, GPU performance or FPS claims.

## Verification and remaining scope

The panel-layout executable contains 19 cases, including stale-target rejection,
independent edits, numerical-cache identity, exact workspace/clipboard round trips,
old optional fields, draft cancellation, attached ports, Unicode/budget handling,
low-zoom readable layout and allocation limits. Existing 26 canvas-input and six
text-draft contracts remain enabled, alongside the numerical/model/graph suites.

The real Uno campaign uses actual mouse/keyboard events for handles, dialog text,
invalid inputs, undo/redo and legacy inspector opening. Observation hooks expose
bounds/counters and do not dispatch model commands. Screenshots supplement those
assertions. Use the exact commit's workflow artifacts for pass/fail evidence.
Software GL may satisfy the framebuffer test; successful desktop compilation does
not establish a Windows/macOS/Linux runtime or physical-GPU performance matrix.

Only bottom-right panel resizing is covered here. All-edge resizing, typography
options, full panel scrolling, complete docking/accessibility/input-device support,
remaining Grasshopper components and Rhino commands, B-rep/solid modeling, arbitrary
trim loops, SubD, proprietary formats/plugins and a direct Uno WebGPU backend
remain unfinished. The exact NURBS kernel and GL/WebGL2 renderer are unchanged.

## Browser campaign lifetime review

The recovered resize implementation's first full browser run exhausted the
qualification observer's ten-minute lifetime during the preceding canvas-value
campaign, before resize tests started. This was a test-session lifecycle failure,
not a passed resize qualification. The suite now loads the same compiled artifact
in three independent browser contexts: workspace/editing, inputs/sliders, and
canvas values/panel layout. Each context reruns the managed/framebuffer checks and
must restore its disposable session. All original input assertions remain; no
failure is skipped or retried into a passing report. The per-session ten-minute
observer limit is unchanged. The CI job has a thirty-minute overall compile and
campaign budget. Per-campaign JSON and screenshots retain the exact failure state,
and only completion of all campaigns permits a qualified deployment artifact.
