# Compact CAD and definition workspace

## References and scope

The layout follows the Windows-style conventions described in McNeel's
[Rhino window reference](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/rhino_window.htm):
command history and prompt above tabbed toolbars, viewport titles and view tabs,
side tool palettes, panels and construction-status controls. The definition shelf
uses category tabs and grouped component icons, as described in the
[Grasshopper component guide](https://developer.rhino3d.com/guides/grasshopper/your-first-component-windows/).
These are behavior/layout references, not copied application assets. The vector
icons are original NurbsScene artwork; no proprietary icons, binaries or font
files are included. This is closer to those interfaces, not pixel-identical or
complete Rhino/Grasshopper UI parity.

## Implemented interaction

The compact workspace has a document title/dirty marker, command history and
suggestions, six modeling toolbar categories, a two-column icon palette, four
viewports and native view tabs, tabbed properties/layers/objects, and the definition
panel. Toolbar metadata lists only implemented modeling commands; actions that
need arguments populate the command prompt instead of pretending to complete.
Host file actions retain the existing save/open/export and discard safeguards.

Viewport titles activate on a single click and maximize/restore on double-click.
The dropdown exposes view and display options. Zoom extents accounts for both
viewport axes instead of cropping tall/narrow views. Definition docking supports right
and bottom placement, hiding/restoring, and splitter resizing. Narrow windows
stack the definition below modeling without overwriting the chosen wide-window
dock preference. Automatic dock-mode changes refit the definition after layout,
rather than leaving nodes offscreen. A versioned, validated local-settings record stores docking,
panel visibility and split fraction. This is not arbitrary docking, detached
native windows or full multi-monitor layout support.

The command input has bounded history, draft-preserving Up/Down recall,
case-insensitive command suggestions, empty-input repeat and Escape cancellation.
The workspace provides Ctrl+S, F2 command focus, F7 display grid, F8 Ortho and F9
Grid Snap. It does not implement every Rhino shortcut or command-line option.

Grid Snap and Ortho are actual construction constraints, not decorative labels.
They apply to world-aligned XY, XZ and YZ construction planes, with configurable
spacing and 90-degree dominant-axis constraint. Curve-end snaps use exact endpoint
evaluation and screen-space tolerance, excluding locked items and clipped/out-of-
range endpoints. Endpoint snapping takes precedence over grid/ortho. Arbitrary
CPlanes, midpoint/intersection/nearest snaps, visibility-occlusion tests, full
object snaps and gumball behavior remain incomplete. Live axes, snap labels and
line preview are an input-transparent overlay; they are not scene geometry.

The definition editor uses compact number sliders, yellow text panels, rounded
components, typed port connections and result-sensitive wire styling. Component
shelves are built from the actual registry (112 managed components at this
milestone). Search, adding/deleting, graph undo/redo, number scrubbing, selecting,
dragging, zoom/fit, baking and the existing input inspector remain available.
It is not a full Grasshopper component, cluster or scripting implementation.

## Reusable architecture and performance

`NurbsScene.Presentation` is a new packable UI-independent library containing
command metadata/recall, validated layout preferences, construction aids and
immutable graph drawing metadata. It depends on the existing geometry/graph/
rendering libraries but not on Uno controls. The Uno controls remain reusable
independently of the application host. A caller-supplied WorkspaceController is
not overwritten with the pavilion example or disposed by WorkspaceView.

Graph presentation is cached by graph identity and revision. Unchanged draws,
selection changes and solver-result publication reuse layout metadata and cached
wire paths; topology/position changes invalidate the relevant snapshot. Offscreen
nodes/wires are culled before drawing and low zoom reduces detail. The grid uses
horizontal/vertical lines rather than a draw per grid intersection. Culling is
still linear; one node move currently rebuilds the full drawing snapshot. The
runtime's numeric solve cache and exact geometry remain separate.

Layer rows retain their identity on selection-only changes, with virtualized
ListViews for layers/objects. Property controls use owner/generation guards so
callbacks from detached controls cannot mutate a replaced graph or unrelated
selection. Multi-node moves validate all inputs before applying one notification
batch and retain one gesture-level history record. Original model coordinates
and geometry remain authoritative; this milestone does not replace the GL
renderer with a different implementation or introduce direct Uno WebGPU.

The presentation benchmark measures cold layout construction, a warm cache hit,
and rebuilding after one node move for 1,000 nodes and 999 wires. It is a
same-implementation metadata benchmark, **not** an old-version comparison,
end-to-end frame time, input latency, physical-GPU measurement or FPS claim.
A contract separately requires zero managed allocation for 10,000 warm lookups.
Original authored icon pictures are shared in a bounded cache.

## Verification

```sh
dotnet run --project tests/dotnet/NurbsScene.PresentationContracts -c Release
dotnet run --project tests/presentation-performance -c Release -- drawing-cache.json
# Publish/serve the real Uno qualification build as documented in BUILD.md.
python tests/browser/test_uno.py
```

The 25 UI-independent contracts cover command routing/recall, layout parsing,
construction constraints, endpoint filtering, cache invalidation/allocations,
port layout, wire bounds, batched movement and interactive command anchors.
Existing numerical/native/model/graph/package tests remain enabled.

`test_workspace_ui.py` adds real mouse/keyboard workflows after the existing
29 managed/GL browser checks. Its compile-time hook only observes actual Uno
visual bounds and state; tests do not call commands through that hook, replace
DOM content, mock a GPU or skip an unavailable renderer. A final flag restores
the disposable test session. Right, bottom and narrow layouts are captured as
screenshots together with JSON state and logs. The hook itself has observation
overhead and is not used as a timing benchmark. Ordinary builds omit it; the
qualified Pages alpha includes it but does not run it without `qualification=1`.

The browser suite is bounded regression evidence, not exhaustive control,
accessibility, pointer-device, desktop-runtime or visual-identity qualification.
Use the exact commit's Actions artifacts for pass/fail status and screenshots;
a successful older definition-only build does not qualify a newer workspace.
