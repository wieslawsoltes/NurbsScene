# Rhino command area and desktop fidelity refinement

## Motivation

The November 13 user capture shows clipped command-history lines, rendering-backend
messages occupying the command band, a large bold prompt beside a taller input,
and toolbar/tab styling that still differs visibly from Rhino's desktop layout.

This follow-up refines the real Uno workspace, with particular attention to the
command area. It preserves native text editing, selection, completion, recall,
keyboard routing, document operations and the shared WebGPU renderer.

## Reference

- [Rhino 8 window and command area](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/rhino_window.htm)
- [Rhino command appearance](https://docs.mcneel.com/rhino/8/help/en-us/options/appearance.htm)
- [Command history](https://docs.mcneel.com/rhino/8/help/en-us/commands/commandhistory.htm)

The target is the compact Windows desktop arrangement: a continuous white command
surface, aligned ordinary-weight history/prompt/input typography, visible complete
history lines, and restrained neutral surrounding chrome.

## Implementation and verification plan

1. Replace mismatched command-control sizing and styling with coherent line-based
   layout and native editing templates. Preserve F2, history selection/copy,
   command completion, recall, repeat, and canvas-to-command typing.
2. Keep routine rendering diagnostics available through rendering information and
   diagnostic output; command history contains user commands, results and errors.
3. Refine menu, toolbar/tab, sidebar and viewport/status presentation using the
   existing executable controls and retained pages.
4. Capture ordinary startup before loading qualification fixtures. Remove the
   synthetic replacement history that concealed the reported startup defect.
5. Extend relevant existing UI campaigns and passive observations for the actual
   startup and command interactions. Use existing workflows.
6. Inspect actual application captures, document the final decisions and review
   fixes, then qualify and merge the exact revision.

## Progress

Initial scope and reference recorded. Implementation, visual captures and
acceptance results will be added as the change is completed.
