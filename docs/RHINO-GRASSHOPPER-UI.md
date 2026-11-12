# Modeling and definition workspace refinement

This follow-up to the Uno 7 / WebGPU migration implements the compact desktop
organization of Rhino 8 and the visual language of the classic Grasshopper
editor using NurbsScene's own controls, vector artwork and executable commands.

## Reference and scope

The references are McNeel's [Rhino window guide](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/rhino_window.htm),
[toolbar/container guide](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/using_toolbars.htm),
and [Grasshopper interface description](https://www.grasshopper3d.com/profiles/blogs/interface-explained).
They inform layout, density, grouping, selection feedback and discoverability.
Rhino's proprietary artwork, application binaries and plugin APIs are not used.

The implementation covers the modeling shell, command area, categorized toolbars
and linked sidebar, viewport controls, status strip, Properties/Layers/Objects
container, and definition window, palettes, menus, toolbar and canvas styling.
Actions continue to operate on the real document/graph and its transaction history.
The exact modeling engine and shared WebGPU renderer remain the production paths.

## Implementation plan

- Establish shared compact chrome with consistent typography, control states,
  separators, section headers, neutral surfaces and selected-tab feedback.
- Reorganize modeling tools into retained category groups and link the sidebar
  to the active category without rebuilding native buttons on every switch.
- Refine command history, viewport framing and status controls for a coherent
  desktop modeling workspace.
- Add a movable/resizable floating definition window with fold, dock and restore
  behavior, preserving graph identity, focus, GPU resources and saved placement.
- Refine the inspector container, layer grid/filter/actions, object metadata and
  aligned viewport properties while retaining draft validation and undo behavior.
- Build grouped definition palettes, compact menus/tools, recognizable capsule
  states and ports, and usable display controls around the retained GPU canvas.
- Keep all ten existing browser campaigns and add a separate compact real-input
  campaign for the new workspace behavior. Bind accepted screenshots and results
  to the exact published source tree.

## Acceptance record

Implementation and qualification are in progress. The final record will identify
the implemented controls, interaction checks, actual application captures and any
remaining compatibility boundaries. Visual/workflow similarity does not imply
Rhino's full modeling-command, file-format or third-party plugin compatibility.
