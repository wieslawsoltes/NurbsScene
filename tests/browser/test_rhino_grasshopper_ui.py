"""Native chrome, grouped palettes, layers and floating-definition acceptance.

This is an independent, bounded campaign. The ten existing campaigns retain
their scenarios and assertion counts. All changes enter through actual native
pointer/keyboard input; the application exposes only passive state and bounds.
"""
import json
from pathlib import Path
from command_console_observation import capture_startup
from grasshopper_discovery_observation import verify_startup_discovery
from grasshopper_onboarding_ui import verify_grasshopper_onboarding
from ui_observation import (read_ui, require_gpu_observation, settle,
                            solution_condition, text_editor_point,
                            wait_for_ui, wait_ready)

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "evidence"


async def verify_rhino_grasshopper_ui(page):
    checks = []
    gestures = []

    def gesture_snapshot(data):
        return {"sample": data["sample"],
                "controls": {identity: data["controls"].get(identity) for identity in
                             ("GraphFloatFrame", "GraphTitleDragHandle", "GraphFloatResize")},
                "state": {key: data["state"].get(key) for key in
                          ("preferences", "definitionFloating", "definitionFolded", "historyExpanded", "focusedElement")},
                "pointerInput": data["state"].get("workspacePointerObservations", [])[-24:],
                "keyAndFocusInput": data["state"]["modelInputObservations"][-12:]}

    def save_gestures():
        (EVIDENCE / "uno-rhino-grasshopper-gestures.json").write_text(json.dumps({"gestures": gestures}, indent=2))

    def check(condition, label):
        if not condition:
            raise AssertionError(label)
        checks.append(label)
        (EVIDENCE / "uno-rhino-grasshopper-progress.json").write_text(
            json.dumps({"checks": checks}, indent=2))
        print("RHINO_GRASSHOPPER " + str(len(checks)) + " " + label, flush=True)

    async def click(identity, *, double=False):
        data = await wait_for_ui(page, "!!value.controls[" + json.dumps(identity) + "]")
        box = data["controls"][identity]
        if not box["enabled"]:
            raise AssertionError("Disabled native control: " + identity)
        await page.mouse.click(box["x"] + box["width"] / 2,
                               box["y"] + box["height"] / 2,
                               click_count=2 if double else 1, delay=75)
        return await settle(page)

    async def command(text):
        await click("CommandInput")
        await page.keyboard.press("Control+A")
        await page.keyboard.insert_text(text)
        await page.keyboard.press("Enter")
        return await settle(page)

    async def edit(identity, text):
        data = await wait_for_ui(page, "!!value.controls[" + json.dumps(identity) + "]")
        await page.mouse.click(*text_editor_point(data["controls"][identity]))
        data = await settle(page)
        if not data["controls"][identity]["focused"]:
            raise AssertionError("Native editor did not receive focus: " + identity)
        await page.keyboard.press("Control+A")
        await page.keyboard.insert_text(text)
        data = await settle(page)
        if data["controls"][identity]["text"] != text:
            raise AssertionError("Draft landed in the wrong native field: " + identity)
        return data

    async def menu(menu_id, item_id):
        await click(menu_id)
        return await click(item_id)

    async def drag(identity, dx, dy, *, cancel=False, toggle_history=False):
        data = await wait_for_ui(page, "!!value.controls[" + json.dumps(identity) + "]")
        box = data["controls"][identity]
        point = (box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
        record = {"control": identity, "input": {"button": "left", "start": point,
                  "end": (point[0] + dx, point[1] + dy), "steps": 8,
                  "cancel": cancel, "toggleHistory": toggle_history},
                  "before": gesture_snapshot(data), "during": None, "after": None}
        gestures.append(record)
        del gestures[:-16]
        save_gestures()
        await page.mouse.move(*point)
        await page.mouse.down()
        await page.mouse.move(point[0] + dx, point[1] + dy, steps=8)
        during = await settle(page)
        record["during"] = gesture_snapshot(during)
        save_gestures()
        if toggle_history:
            await page.keyboard.press("F2")
            await settle(page)
        if cancel:
            await page.keyboard.press("Escape")
        await page.mouse.up()
        after = await settle(page)
        record["after"] = gesture_snapshot(after)
        save_gestures()
        return during, after

    def layer(data, identity):
        return next(row for row in data["state"]["layerRows"] if row["id"] == identity)

    def same_box(a, b):
        return all(abs(a[key] - b[key]) <= 2 for key in ("x", "y", "width", "height"))

    initial = await read_ui(page)
    baseline = initial["state"]
    try:
        check(initial["controls"]["CommandInput"]["y"] < initial["controls"]["Toolbar_Standard"]["y"],
              "The native command line occupies the Rhino-style band above modeling toolbars")
        data = await click("Toolbar_Curve")
        check(data["state"]["toolbar"] == "Curve"
              and all(identity in data["controls"] for identity in ("Side_Line", "Side_Circle", "Side_SelectAll", "Side_SelectNone")),
              "Curve toolbar selection populates the modeling sidebar and retains common selection tools")
        data = await click("Toolbar_Surface")
        check(data["state"]["toolbar"] == "Surface"
              and all(identity in data["controls"] for identity in ("Side_Extrude", "Side_Loft"))
              and data["state"]["documentRevision"] == baseline["documentRevision"]
              and data["state"]["graphRevision"] == baseline["graphRevision"],
              "Surface toolbar selection changes executable sidebar tools without editing either document")
        await click("Toolbar_Standard")
        await click("PanelLayers")

        # Keep the fixture geometry intact by exercising a new, empty layer.
        data = await click("NewLayer")
        added = [row for row in data["state"]["layerRows"]
                 if row["id"] not in {row["id"] for row in baseline["layerRows"]}]
        check(len(added) == 1 and data["state"]["layers"] == baseline["layers"] + 1
              and data["state"]["layerSelectedId"] == added[0]["id"],
              "The Layers toolbar creates and selects an actual document layer")
        identity = added[0]["id"]
        original = added[0]
        await edit("LayerName", "Facade study")
        data = await click("RenameLayer")
        check(layer(data, identity)["name"] == "Facade study",
              "The compact layer-name editor commits an ordinary layer rename")
        revision = data["state"]["documentRevision"]
        structures = data["state"]["layerStructureChanges"]
        data = await edit("LayerFilter", "fACADe")
        check(data["state"]["layerVisibleIds"] == [identity]
              and data["state"]["layerFilter"] == "fACADe"
              and data["state"]["documentRevision"] == revision,
              "Case-insensitive layer filtering changes visible rows without changing document history")
        data = await click("ClearLayerFilter")
        check(set(data["state"]["layerVisibleIds"]) == {row["id"] for row in data["state"]["layerRows"]}
              and data["state"]["layerFilter"] == "" and data["state"]["documentRevision"] == revision,
              "Clearing the native layer filter restores all rows without a document transaction")
        data = await click("LayerVisible_" + identity)
        check(not layer(data, identity)["visible"] and data["state"]["layerStructureChanges"] == structures,
              "The layer light-bulb control changes visibility without rebuilding layer row structure")
        data = await command("Undo")
        check(layer(data, identity)["visible"], "Layer visibility remains an independently undoable document edit")
        data = await click("LayerLocked_" + identity)
        check(layer(data, identity)["locked"] and data["state"]["layerStructureChanges"] == structures,
              "The layer padlock changes locking without rebuilding layer row structure")
        data = await command("Undo")
        check(not layer(data, identity)["locked"], "Layer locking remains an independently undoable document edit")

        revision = data["state"]["documentRevision"]
        await click("LayerColor_" + identity)
        await click("InspectorColorApply")
        # Dialog unloading can precede the ShowAsync continuation which commits
        # the transaction. Observe that lifetime, not the desired color/revision.
        data = await wait_for_ui(page, "value.state.layerColorEditorPending === false")
        check(data["state"]["documentRevision"] == revision and layer(data, identity)["color"] == original["color"],
              "Applying an unchanged color preserves authoritative float channels and document history")
        await click("LayerColor_" + identity)
        await edit("InspectorColorHex", "#GGGGGG")
        data = await click("InspectorColorApply")
        check(data["state"]["layerColorEditorOpen"] and bool(data["state"]["layerColorValidation"])
              and data["state"]["documentRevision"] == revision and layer(data, identity)["color"] == original["color"],
              "Invalid native color input keeps the dialog open and preserves the document atomically")
        await page.screenshot(path=str(EVIDENCE / "uno-rhino-layer-color-validation.png"))
        await edit("InspectorColorHex", "#3977B8")
        await click("InspectorColorApply")
        data = await wait_for_ui(page, "value.state.layerColorEditorPending === false")
        expected_color = [57 / 255, 119 / 255, 184 / 255, original["color"][3]]
        check(all(abs(a - b) < 1e-6 for a, b in zip(layer(data, identity)["color"], expected_color))
              and data["state"]["documentRevision"] > revision,
              "Applying a valid layer color commits the requested RGB channels")
        data = await command("Undo")
        check(layer(data, identity)["color"] == original["color"],
              "One document undo restores the complete pre-dialog layer color")
        revision = data["state"]["documentRevision"]
        await click("LayerColor_" + identity)
        await edit("InspectorColorHex", "#123456")
        await click("InspectorColorCancel")
        data = await wait_for_ui(page, "value.state.layerColorEditorPending === false")
        check(data["state"]["documentRevision"] == revision and layer(data, identity)["color"] == original["color"],
              "Cancel discards the color draft without changing document state")
        await page.screenshot(path=str(EVIDENCE / "uno-rhino-layers.png"))
        data = await command("Undo")
        check(layer(data, identity)["name"] == original["name"],
              "Color dialog drafts and undo do not consume the preceding layer-name transaction")
        data = await command("Undo")
        check(data["state"]["layerRows"] == baseline["layerRows"] and data["state"]["objects"] == baseline["objects"],
              "The complete Layers workflow restores the original layers and model objects")

        await click("GraphDockBottom")
        await click("GraphFit")
        data = await click("GraphMenu_File")
        check(all(identity in data["controls"] for identity in
                  ("GraphAction_OpenWorkspace", "GraphAction_SaveWorkspace", "GraphAction_SaveWorkspaceAs")),
              "The definition File menu exposes native workspace file actions")
        await page.keyboard.press("Escape")
        await settle(page)
        await click("Category_Params")
        data = await click("PaletteGroup_Params_Util")
        check(data["state"]["paletteGroupOpen"] and data["state"]["paletteOpenedGroup"] == "Util"
              and "PaletteComponent_Panel" in data["controls"],
              "The dark palette group footer opens its native labeled component flyout")
        await page.screenshot(path=str(EVIDENCE / "uno-grasshopper-palette.png"))
        graph_ids = {node["id"] for node in data["nodes"]}
        await click("PaletteComponent_Panel")
        data = await wait_for_ui(page, solution_condition(editor_closed=True))
        created = [node for node in data["nodes"] if node["id"] not in graph_ids]
        check(len(created) == 1 and created[0]["component"] == "Panel" and not data["state"]["paletteGroupOpen"],
              "Choosing a group flyout item inserts and solves the real Panel component")
        await menu("GraphMenu_Edit", "GraphAction_Undo")
        data = await wait_for_ui(page, solution_condition(editor_closed=True))
        check({node["id"] for node in data["nodes"]} == graph_ids,
              "The definition Edit menu undoes the palette insertion as one graph transaction")
        await click("Category_Maths")
        data = await click("Category_Params")
        builds = (data["state"]["shelfPageBuilds"], data["state"]["shelfButtonBuilds"])
        await click("Category_Maths")
        data = await click("Category_Params")
        check((data["state"]["shelfPageBuilds"], data["state"]["shelfButtonBuilds"]) == builds,
              "Returning to visited component categories reuses their retained palette pages and buttons")

        await verify_grasshopper_onboarding(page, click, menu, command, check)
        data = await read_ui(page)

        document_revision = data["state"]["documentRevision"]
        graph_revision = data["state"]["graphRevision"]
        appearance = (("Grid", "graphGridVisible"), ("Icons", "graphComponentIconsVisible"),
                      ("Names", "graphComponentNamesVisible"), ("FancyWires", "graphFancyWires"))
        for suffix, state_name in appearance:
            data = await menu("GraphMenu_Display", "GraphDisplay_" + suffix)
            check(data["state"][state_name] is False and data["state"]["graphRevision"] == graph_revision,
                  "Display / " + suffix + " changes only canvas presentation")
        for suffix, _ in appearance:
            data = await menu("GraphMenu_Display", "GraphDisplay_" + suffix)
        check(all(data["state"][name] for _, name in appearance)
              and data["state"]["documentRevision"] == document_revision
              and data["state"]["graphRevision"] == graph_revision,
              "Restoring all display options leaves both model and definition history unchanged")
        canvas_height = data["controls"]["GraphCanvas"]["height"]
        data = await menu("GraphMenu_View", "GraphView_Palette")
        check(not data["state"]["componentPaletteVisible"] and "Category_Params" not in data["controls"]
              and data["controls"]["GraphCanvas"]["height"] > canvas_height + 90,
              "Hiding component palettes reclaims their actual native canvas area")
        await menu("GraphMenu_View", "GraphView_Palette")
        data = await menu("GraphMenu_View", "GraphView_Toolbar")
        check(data["state"]["componentPaletteVisible"] and not data["state"]["canvasToolbarVisible"]
              and "GraphFit" not in data["controls"] and data["controls"]["GraphCanvas"]["height"] > canvas_height + 25,
              "Hiding only the canvas toolbar preserves palettes and reclaims toolbar space")
        data = await menu("GraphMenu_View", "GraphView_Toolbar")
        check(data["state"]["canvasToolbarVisible"] and "GraphFit" in data["controls"]
              and abs(data["controls"]["GraphCanvas"]["height"] - canvas_height) <= 2
              and data["state"]["graphRevision"] == graph_revision,
              "Restoring editor chrome restores the canvas layout without a graph edit")
        data = await menu("GraphMenu_Help", "GraphAction_Help")
        check(data["state"]["editorHelpOpen"] and "GraphHelpContent" in data["controls"],
              "The definition Help menu opens native guidance for implemented editor interactions")
        await page.keyboard.press("Escape")
        data = await wait_for_ui(page, "value.state.editorHelpOpen === false")
        check(data["state"]["graphRevision"] == graph_revision and data["state"]["documentRevision"] == document_revision,
              "Closing editor help returns to the unchanged native definition")

        data = await click("GraphFloat")
        frame = data["controls"]["GraphFloatFrame"]
        check(data["state"]["definitionFloating"] and not data["state"]["definitionFolded"]
              and frame["width"] >= 560 and frame["height"] >= 320
              and frame["x"] >= 0 and frame["y"] >= 0
              and frame["x"] + frame["width"] <= data["state"]["workspaceWidth"] + 1
              and frame["y"] + frame["height"] <= data["state"]["workspaceHeight"] + 1,
              "The Float title control opens a usable definition window contained in the workspace")
        _, data = await drag("GraphTitleDragHandle", -70, 20)
        moved = data["controls"]["GraphFloatFrame"]
        check(abs(moved["x"] - frame["x"] + 70) <= 2 and abs(moved["y"] - frame["y"] - 20) <= 2,
              "Dragging the native definition title moves the window by the pointer displacement")
        preferences = data["state"]["preferences"]
        during, data = await drag("GraphTitleDragHandle", 35, 20, cancel=True)
        check(not same_box(during["controls"]["GraphFloatFrame"], moved)
              and same_box(data["controls"]["GraphFloatFrame"], moved) and data["state"]["preferences"] == preferences,
              "Escape cancels a title drag and restores the complete saved floating placement")
        _, data = await drag("GraphFloatResize", 50, 30)
        expanded = data["controls"]["GraphFloatFrame"]
        check(abs(expanded["width"] - moved["width"] - 50) <= 2
              and abs(expanded["height"] - moved["height"] - 30) <= 2,
              "The native lower-right grip resizes both dimensions of the floating definition")
        preferences = data["state"]["preferences"]
        during, data = await drag("GraphFloatResize", -30, -20, cancel=True)
        check(not same_box(during["controls"]["GraphFloatFrame"], expanded)
              and same_box(data["controls"]["GraphFloatFrame"], expanded) and data["state"]["preferences"] == preferences,
              "Escape cancels a window resize without persisting the draft dimensions")
        data = await click("GraphFold")
        check(data["state"]["definitionFolded"] and abs(data["controls"]["GraphFloatFrame"]["height"] - 28) <= 1
              and "GraphMenu_Display" not in data["controls"] and "GraphFloatResize" not in data["controls"],
              "Folding collapses the actual definition window to its title and hides body controls")
        data = await click("GraphFold")
        check(not data["state"]["definitionFolded"] and same_box(data["controls"]["GraphFloatFrame"], expanded)
              and data["state"]["componentPaletteVisible"] and data["state"]["canvasToolbarVisible"],
              "Unfolding restores expanded dimensions and the user's editor chrome choices")
        data = await click("GraphTitleDragHandle", double=True)
        check(data["state"]["definitionFolded"], "Double-clicking the actual floating title folds the definition")
        await click("OpenGrasshopper")
        data = await wait_for_ui(page, "value.state.grasshopperOpenPending === false")
        check(data["state"]["definitionVisible"] and not data["state"]["definitionFolded"]
              and same_box(data["controls"]["GraphFloatFrame"], expanded) and data["controls"]["GraphCanvas"]["focused"],
              "The persistent Grasshopper launcher unfolds and focuses an already visible floating editor")
        await page.screenshot(path=str(EVIDENCE / "uno-grasshopper-floating.png"))
        data = await click("GraphHide")
        check(not data["state"]["definitionVisible"] and "GraphFloatFrame" not in data["controls"],
              "The floating close control hides the native definition window")
        data = await command("Grasshopper")
        check(data["state"]["definitionVisible"] and data["state"]["definitionFloating"]
              and same_box(data["controls"]["GraphFloatFrame"], expanded),
              "The Grasshopper command reopens the remembered floating window")
        data = await click("GraphDockBottom")
        check(data["state"]["dock"] == "Bottom" and not data["state"]["definitionFloating"]
              and "GraphFloatFrame" not in data["controls"] and "DefinitionSplitter" in data["controls"],
              "The definition title re-docks the same editor below the model viewports")
        data = await click("GraphFloat")
        check(same_box(data["controls"]["GraphFloatFrame"], expanded)
              and data["state"]["documentRevision"] == document_revision
              and data["state"]["graphRevision"] == graph_revision,
              "Floating again restores remembered placement while every docking gesture preserves both documents")
        data = await click("GraphFold")
        folded = data["controls"]["GraphFloatFrame"]
        saved_fields = data["state"]["preferences"].split("|")
        saved_fields[7] = "1"  # Persist history expansion, never the pending drag.
        saved_preferences = "|".join(saved_fields)
        during, data = await drag("GraphTitleDragHandle", -25, 15, cancel=True, toggle_history=True)
        check(not same_box(during["controls"]["GraphFloatFrame"], folded)
              and data["state"]["historyExpanded"] and data["state"]["preferences"] == saved_preferences,
              "Persisting command history during a title drag preserves the committed placement after Escape")
        data = await click("GraphHide")
        saved_fields[2] = "0"
        saved_preferences = "|".join(saved_fields)
        check(not data["state"]["definitionVisible"] and data["state"]["preferences"] == saved_preferences
              and "OpenGrasshopper" in data["controls"] and "GraphFloatFrame" not in data["controls"],
              "Closing a folded window saves its hidden state while keeping the labelled Grasshopper launcher available")

        # Reload creates a new GPU device. Retain and qualify the first device's
        # complete observation before navigation can discard its error history.
        observation = await page.evaluate("globalThis.nurbsSceneGpuObservation")
        (EVIDENCE / "uno-rhino-grasshopper-pre-reload-gpu.json").write_text(json.dumps(observation, indent=2))
        require_gpu_observation(observation)
        await page.reload(wait_until="domcontentloaded", timeout=60000)
        startup_reload = await capture_startup(page, EVIDENCE / "uno-grasshopper-startup-reload")
        discovery_reload = await verify_startup_discovery(page, startup_reload,
            EVIDENCE / "uno-grasshopper-discovery-reload", saved_preferences=saved_preferences)
        checks.extend(discovery_reload["checks"])
        await page.wait_for_function(
            'globalThis.nurbsSceneQualification && globalThis.nurbsSceneQualification.status !== "running"', timeout=180000)
        report = await page.evaluate("globalThis.nurbsSceneQualification")
        (EVIDENCE / "uno-rhino-grasshopper-reload.json").write_text(json.dumps(report, indent=2))
        check(report.get("application") == "Uno Platform" and report.get("gpuApi") == "WebGPU" and report.get("status") == "passed",
              "Reloading the floating workspace passes the actual Uno modeling and framebuffer qualification again")
        data = await wait_ready(page)
        check(saved_preferences.startswith("3|Float|") and startup_reload["state"]["preferences"] == saved_preferences
              and data["state"]["startupPreferences"] == discovery_reload["reopened"]["workspace"]["state"]["preferences"],
              "Reload restores the exact version-three floating placement and folded preference before the test fixture opens")
        # The fixture opens a consistent docked workspace after observing startup.
        # Capture the complete restored editor with ordinary native input.
        await click("GraphDockBottom")
        await click("GraphFit")
        data = await settle(page)
        check(data["state"]["gpuError"] is None and data["state"]["graphErrors"] == 0,
              "The completed Rhino and Grasshopper chrome workflow has no native graph or GPU errors")
        await page.screenshot(path=str(EVIDENCE / "uno-rhino-grasshopper-final.png"))
        return checks
    finally:
        (EVIDENCE / "uno-rhino-grasshopper-observed.json").write_text(json.dumps(
            await page.evaluate("globalThis.nurbsSceneUi"), indent=2))
