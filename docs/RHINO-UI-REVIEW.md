# Rhino-style workspace UI/UX review

Reviewed on 2026-10-15 against the Rhino 8 Windows interface, with NurbsScene
`7d2384d12e44e9c608823cb84904519f6852d803` / tree
`9d65d9b96ed7716de45bdaffe017a60a15052fc6` as the source and screenshot baseline.
The existing UI was examined by region and interaction, not just by recoloring
its chrome. This change improves a working independent application; **it does
not establish full Rhino or Grasshopper equivalence**.

## Reference sources

- [Rhino window](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/rhino_window.htm): placement of command history, toolbars, titles, view tabs, panels and status controls.
- [Toolbar behavior](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/using_toolbars.htm): docking, grouping, overflow and customization reference.
- [Keyboard modifiers](https://docs.mcneel.com/rhino/8/help/en-us/user_interface/keyboard%20modifiers.htm): selection, navigation and command interaction reference.
- [Keyboard options](https://docs.mcneel.com/rhino/8/help/en-us/options/keyboard.htm): shortcut conventions. Browser/OS-reserved keys are subject to the host and are not universally interceptable.

The reference is the Windows modeling workspace, not Rhino for Mac. All existing
NurbsScene vector icons remain independently authored. No Rhino artwork, fonts,
application binaries or proprietary source were copied.

## Region-by-region review

| Region | Baseline finding | Change / retained behavior | Remaining boundary |
|---|---|---|---|
| Startup | Every ordinary workspace loaded the pavilion demonstration and opened the definition alongside the model. | Ordinary startup is an empty modeling document. The four-view modeling layout and Properties are the fresh default. The sample remains an explicit File action; qualification loads it explicitly after checking empty startup. | No original template/new-document chooser or complete Rhino file integration. |
| Frame, menus and toolbars | Compact native menus and category tools already existed. The definition editor took much of the initial modeling area. | Keep the native gray compact chrome, six actual modeling-tool categories, authored vector icons and two-column palette. Add Window layout presets and common shell commands. | Not every Rhino menu, tool icon, toolbar container, flyout, customization screen or command exists. Unsupported tools are not added as decorative buttons. |
| Command area | History was a display-only TextBlock; F2 only focused command input. Shell operations and model commands did not share one route. | Native selectable/scrollable history, copy/clear context actions, F2 expansion, command recall with draft restoration, shell/model suggestions and common dispatch. | No full Rhino option parser, macro/alias editor, command-help panel, scripting console or command-history search. |
| Command initiation and repeat | Model-canvas typing did not start command entry. Blank repeat could use unrelated history entries. | Actual character input starts the command prompt without deriving text from virtual-key names. Empty Enter, focused-canvas Space and short right click repeat the last successful repeatable model command. History/panel/file actions, undo/redo and selection/view commands do not replace it. | Only the existing interactive model commands are implemented; full command-specific option/acceptance semantics remain incomplete. IME/device and reserved-key coverage is bounded by the tested host. |
| Viewport titles and tabs | Title activation and double-click maximization existed, but any canvas double-click also maximized the view. | Keep single-title activation and title-only double-click maximization. Add title context menu, four-view and active-view commands, keyboard max/restore and view shortcuts. Clicking titles preserves selection. | No arbitrary viewport layouts, named-view manager, floating model views, page layouts or projection switching for a named view. |
| Model navigation | Right drag orbited Perspective or panned parallel views, without the documented modifier workflow. Wheel zoom was centered. Captures could finish on the wrong button release. | Shift+right/middle pan; Control+right target-plane anchored zoom; normal right Perspective orbit/parallel pan; cursor-anchored wheel. One initiating pointer/button owns a reversible gesture. Escape, capture loss, cancellation and size changes restore the press snapshot. | No complete touch/pinch/multi-device matrix, view undo/redo stack, constrained orbit, perspective target-depth inference or mouse-preference editor. |
| Model selection and context | Selection supported replacement/Shift addition. A separate model context menu was absent. | Control removes a picked object, Shift adds, empty ordinary clicks clear. Held right click and Shift+F10 open a native context menu with executable shared commands. | No model window/crossing-selection implementation, subobject selection/filter dialog, full cycling or Rhino selection parity. The graph canvas retains its separate richer selection rules. |
| Layout splitters and persistence | Inspector width was set directly and overwritten by later layout updates. Grips lacked keyboard resizing and rollback. | Version-two preferences persist inspector width, panel/sidebar visibility, history expansion, selected inspector tab and the existing definition split/dock. Native focusable splitters support arrows, initiating-button ownership, Escape and capture-loss rollback. Version-one records migrate atomically. | Definition docking remains right/bottom, not arbitrary tab containers, floating native windows or multi-monitor workspaces. |
| Responsive layout | Definition auto-stacking existed; inspector width did not survive it. | Preserve the requested wide-window dock while stacking at narrow sizes. Below 720 DIP, temporarily hide the inspector without changing its stored visibility/width. Reopening definition requests a final-layout fit. | A small screen is a compact adaptation, not a pixel-identical desktop layout. |
| Properties | Empty selection showed document metadata rather than the active viewport. | Empty-selection Properties edits active camera target, distance, parallel height, orbit angles and field of view. An entire draft validates before any camera field changes. Apply/Revert, active-viewport ownership and perspective-only field enablement are explicit. | No full object/material/render-mesh/texture/light/annotation property pages. Display mode is shared by all model views, and the UI labels this scope. |
| Layers and objects | Existing lists were virtualized, but the layer header used text spacing; the status bar had no layer interaction. | Aligned layer columns; retained stable row identity and transactional layer/object editing. Status layer chooses the current layer without selection or assigns selected objects. Actions check document, revision and selection before committing. Right-click opens Layers. | No complete layer hierarchy, drag/drop ordering, per-viewport layer state, Rhino property columns or comprehensive object types. |
| Status and construction aids | Real Grid Snap, Ortho and End controls existed, alongside a verbose backend status string. | Keep working aids and coordinates; show units, current/selected layer, shared display mode, concise selection count and sidebar toggles. Backend diagnostics remain in command history. | Planar, complete Osnap, SmartTrack, AutoCPlane, Gumball, construction history, selection filters and detailed cursor readouts are not implemented. |
| File lifecycle and feedback | `.nscene` open/save, display OBJ/STL export, validation and discard confirmation were already present. | Preserve those safeguards and route toolbar/menu/typed host commands consistently. Empty ordinary startup no longer begins with an unsolicited demonstration. | No `.3dm`/`.gh`/`.ghx`, STEP/IGES, original import/export option dialogs, recent-file UI, complete autosave/recovery or printing/layout system. |
| Definition editor | Previous changes already provided real typed wiring, search, sliders, panel text, resizing, typography, virtualized scrolling and navigation. | Retain those implementations and all nine existing acceptance campaigns. The panel is deliberately optional in the modeling-first workspace. | This is not the complete Grasshopper component catalog, clustering, plugin, scripting or interaction system. |

## Input and state ownership

`ViewportGesture`, `ViewportCameraSettings`, `WorkspacePreferences` and
`WorkspaceShellCommands` are UI-independent presentation contracts. Navigation
never touches geometry or the document undo stack. Movement is evaluated from the
press snapshot, so the endpoint does not depend on event coalescing. Zoom anchors
to the camera target plane, not to an asserted picked surface depth.

`WorkspaceSplitter` owns one native pointer and initiating button. Draft resizing
updates only view state; only successful release or a keyboard step persists it.
Escape/capture loss restores the previous value without writing a new preference.
The saved record is validated as a whole. Invalid versions, fields, numeric values
or limits fall back atomically; old five-field version-one records retain their
existing dock, visible panels and split, with Layers as their prior inspector tab.

`CommandConsole` separates recall from repeat. Recall includes submitted commands;
repeat is updated only after successful dispatch of an eligible model command.
Clearing displayed history does not destroy recall. History storage and individual
log lines are bounded; truncation does not split a UTF-16 surrogate pair.

The camera inspector retains draft text until Apply/Revert or an active-viewport
change. A hidden inspector does not refresh its camera fields. Visible camera
refreshes are coalesced, and camera motion does not enumerate all document objects
for status text or rebuild layer tooltips. This is a structural cost reduction,
not a measured FPS or physical-GPU speedup claim.

## Qualification contract

The presentation suite now contains **45** cases, including **20** new cases for
workspace migration/validation, shell command metadata, camera property atomicity,
modifier resolution, anchored zoom, gesture restoration and event-coalescing
invariance. These are .NET execution tests, not source-string assertions.

The mandatory `rhino-workspace` browser campaign contains **55 native interaction
assertions**, plus the runner's required session-restoration assertion. It uses
real mouse/keyboard input for layout, history copying, canvas typing, command
repeat, editor/model undo scoping, modifier navigation, camera rollback, title and
keyboard maximization, secondary-button ownership, persistent keyboard/pointer
splitters, responsive visibility, camera draft validation, layer menus, context
menus and a fresh-load preference round trip. It captures actual Uno screenshots.

The earlier nine campaigns retain their **293** required input assertions. The
registry now requires ten independent campaigns and at least **349** input
assertions in total. Each context also executes the same **30** managed modeling
and actual differential-GL checks, including ordinary empty startup. Repetition
across contexts is not 300 distinct product features. The aggregate still rejects
missing/failed campaigns, page/GPU errors, insufficient checks, un-restored sessions,
source mismatches or changed publication hashes.

```sh
dotnet run --project tests/dotnet/NurbsScene.PresentationContracts -c Release
python -m unittest discover -s tests/browser -p 'test_qualification_contracts.py' -v
# Against the actual published qualification build, not the JavaScript harness:
python tests/browser/test_uno.py --campaign rhino-workspace
python tests/browser/test_uno.py
```

Counts here describe the authored mandatory contract. **Use the exact revision's
Actions results and artifacts to determine whether it passed.** An older successful
build, Python gate tests or a desktop compile cannot substitute for actual current
Uno browser qualification. The observation hook remains read-only during tests;
its only completion flag restores the disposable session. Ordinary builds omit it.

## Completion boundary

This review covers all currently exposed workspace regions and delivers a broad
interaction/layout improvement. It does **not** claim full visual identity,
complete accessibility/input-device coverage, all original dialogs/shortcuts,
full Rhino command semantics, original file formats or a solid-modeling backend.
See [PARITY.md](PARITY.md) for geometry, graph, rendering and interchange limits.
