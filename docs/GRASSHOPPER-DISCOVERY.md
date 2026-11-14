# Grasshopper discovery and first-use workflow

## Problem and implementation scope

The visual-programming editor, component registry, incremental solver, viewport
preview and Bake actions were already connected to the production workspace.
The default layout hid the editor, however, its toolbar action was an unlabeled
graph glyph called “Definition panel”, and the Grasshopper command toggled rather
than consistently opened it. A saved folded floating editor could also reopen folded.

This change makes the implemented editor directly discoverable and provides an
editable first definition without replacing the user's model or existing graph.

## Opening and using the editor

A persistent, text-labeled **Grasshopper** launcher occupies the right side of the
application title strip. It remains visible when the editor is hidden and when
the horizontal toolbar is scrolled. Every modeling toolbar category also retains
its graph action, now displaying the word Grasshopper. A top-level **Grasshopper**
menu exposes opening, component search, example insertion, workspace files,
docking and hiding.

| Entry | Behavior |
|---|---|
| Title launcher, toolbar button, Grasshopper → Open Grasshopper, Ctrl+Alt+G | Reveal, unfold and focus the existing editor |
| `Grasshopper`, `Definition`, `ShowGrasshopper` | The same idempotent open action |
| `GrasshopperSearch`, Grasshopper → Add a component | Open the editor and its executable component search |
| `GrasshopperExample`, Grasshopper → Insert extrusion example | Open the editor and append the five-component example |
| `HideGrasshopper`, `HideDefinition`, Grasshopper → Hide Grasshopper | Explicitly hide the editor |
| `ToggleDefinition` | Toggle visibility for callers that need the old behavior |

Opening twice preserves the graph, graph history, selection, dock and saved
floating placement. Opening a hidden, folded floating editor unfolds it. The
default modeling layout and existing saved visibility preferences remain valid;
the permanent launcher makes discovery independent of those preferences.
Successful loading of an `.nscene` workspace with graph nodes reveals its editor.

The empty editor identifies itself as **Grasshopper · Visual programming** and
offers **Add a component** and **Insert extrusion example**, followed by short
instructions for wiring, slider editing and Bake. This is a retained native Uno
card inside the graph canvas column. Its content wraps and scrolls in small panes;
it disappears as soon as the graph has nodes and returns if graph undo removes
the last component. Canvas navigation remains available around the card.

## Executable extrusion example

The starter uses the existing managed component registry and graph solver:

| Component | Initial value / connection |
|---|---|
| Radius number slider | Value 5, range 1–20 → Circle.Radius |
| Height number slider | Value 10, range 1–30 → UnitZ.Length |
| Circle | Center at the world origin; C → Extrude.C |
| Height vector (UnitZ) | V → Extrude.V |
| Extrusion (Extrude) | S produces an exact rational NURBS surface |

`GrasshopperExamples.InsertExtrusion(GraphSession)` validates component ports
before changing the graph, then inserts all five nodes and four wires in one
`GraphSession.Edit`. Existing nodes, wires, model objects and document history are
preserved. Repeated insertion allocates fresh identities and separate undo entries.
Placement uses actual component bounds, including resized panels, with a bounded
fallback when imported coordinates are extreme.

The terminal extrusion is selected for **Bake**. Both sliders use the normal
incremental solve and viewport preview paths. The host frames the completed
current solution after preview publication and the dock resize; it does not start
a competing manual solve. The graph is fitted after its native layout is usable.
Graph undo removes the inserted definition; baked geometry is a separate model
transaction with its own document undo.

## Implementation and compatibility

All open entry points share `WorkspaceView.OpenGrasshopper`. The existing dock
operation cancels an active floating gesture and preserves committed placement.
Canvas focus follows the coalesced native layout pass, and a hide/fold/dispose
action cancels the pending focus request. Later layout changes do not steal focus.
Component search uses the existing native search popup and respects modal editors.
Existing toolbar and menu automation identifiers are retained where labels change.

The component palettes, search, typed wires and trees, numeric sliders, panels,
incremental solving, preview, undo/redo and exact Bake implementation are reused.
The workspace format is `.nscene`, containing both the CAD document and definition.
This is an independent Grasshopper-style implementation; `.gh`/`.ghx` import,
proprietary plugins and the McNeel scripting/component API are outside this change.

## Acceptance

Seven focused Presentation contracts cover exact rational surface evaluation,
both slider edits, additive placement, undo/redo with existing history, repeated
examples, unchanged CAD document/history, incompatible-registry atomicity and
finite placement around extreme coordinates. The focused suite passes **67/67**.

The browser qualification extends the existing `rhino-grasshopper-ui` campaign
from 50 to 75 checks, raising the eleven-campaign requirement from 414 to 439.
It must exercise the launcher on an untouched startup before the modeling fixture
reveals the graph, repeat opening, restore a saved folded/hidden floating editor,
and create/edit/preview/Bake/undo the example through native controls. The original
startup capture deadline remains separate from the discovery interaction phase.
Native controls, graph/document state, focus and current render observations supply
the evidence; qualification must not set visibility or construct the tested
example through an observer hook.

The observation-handoff contracts pass **76/76** and the artifact-gate contracts
pass **68/68**. These include rejection of missing original/opened screenshots,
lost focus, unpainted canvases, changed document/graph history, overwritten saved
startup preferences, and omitted discovery assertions.

All existing source/build/browser gates remain required for every revision. The
[implementation PR](https://github.com/wieslawsoltes/NurbsScene/pull/22) records the
final tested source, complete workflow results and downloadable native captures.
The hosted desktop and browser WebGPU runs use software adapters to establish
correctness; their results are not physical-GPU performance measurements.
