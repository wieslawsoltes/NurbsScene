# Grasshopper discovery and first-use workflow

## Problem and implementation scope

The existing visual-programming editor, component registry, incremental solver,
viewport preview and Bake actions are connected to the production workspace.
However, the default layout hides the editor, its toolbar action is an unlabeled
graph glyph called “Definition panel”, and the Grasshopper command toggles rather
than consistently opens it. A saved folded floating editor can also reopen folded.

This change will make the implemented editor directly discoverable and provide an
editable first definition without replacing the user's model or existing graph.

## Planned behavior

- A persistent, text-labeled Grasshopper launcher remains visible when the editor
  is hidden. A top-level Grasshopper menu exposes opening, component search,
  example insertion, workspace files, docking and hiding.
- Grasshopper and Definition commands open, unfold and focus the existing editor.
  Opening twice preserves its graph, history, selection and saved dock/placement.
  Explicit hide actions remain separate. Ctrl+Alt+G opens the editor.
- The empty editor identifies itself as Grasshopper visual programming and offers
  Add a component and Insert extrusion example, alongside concise wiring/Bake help.
- The example inserts five real components and four wires in one undoable graph
  edit: Radius -> Circle, Height -> UnitZ, Circle + UnitZ -> Extrude. The terminal
  surface is selected for Bake; existing document objects and graph nodes survive.
- Opening an .nscene workspace containing a definition reveals its graph editor.
- Existing native component creation, wiring, sliders, panels, typed trees,
  incremental solving, preview, undo/redo, docking and exact workspace storage are
  reused. The managed workspace format remains .nscene; this change does not add
  .gh/.ghx or proprietary plugin compatibility.

## Acceptance

Qualification will exercise the launcher on an untouched startup before the usual
modeling fixture reveals the graph, repeat opening, restore a saved folded/hidden
floating editor, and create/edit/preview/undo the example through native controls.
The existing browser campaigns and source/build gates remain required. Tests will
observe real model, layout and renderer state rather than directly setting editor
visibility or constructing the graph under test.

Implementation and verified results will be recorded with the completed change.
