"""Onboarding slice of the existing native Grasshopper campaign, not a new job."""
import json
import math
from pathlib import Path

from ui_observation import read_ui, settle, solution_condition, wait_for_ui

EVIDENCE = Path(__file__).resolve().parents[2] / "evidence"


def extrusion_output(data, identity):
    outputs = [value for value in data["graphGeometry"]["outputs"]
               if value["node"] == identity and value["port"] == "S"]
    if len(outputs) != 1:
        raise AssertionError("The selected extrusion did not publish exactly one authoritative surface: " + json.dumps(outputs))
    return outputs[0]


def cylinder_matches(output, radius, height):
    shape = output.get("shape") or {}
    samples = shape.get("samples", [])
    return (shape.get("kind") == "surface" and len(samples) == 9
            and all(len(point) == 3 and all(math.isfinite(value) for value in point)
                    and abs(math.hypot(point[0], point[1]) - radius) < 1e-8 for point in samples)
            and abs(min(point[2] for point in samples)) < 1e-8
            and abs(max(point[2] for point in samples) - height) < 1e-8)


async def verify_grasshopper_onboarding(page, click, menu, command, check):
    """Use ordinary menus/pointers; restore the preceding definition and CAD."""
    observations = {}

    def remember(stage, data):
        observations[stage] = data
        (EVIDENCE / "uno-grasshopper-onboarding.json").write_text(json.dumps(observations, indent=2))

    async def completed_open():
        return await wait_for_ui(page, "value.state.grasshopperOpenPending === false")

    def open_and_focused(data):
        return (data["state"]["definitionVisible"] and not data["state"]["definitionFolded"]
                and data["controls"]["GraphCanvas"]["focused"]
                and data["state"]["graphFocus"] != "Unfocused")

    async def select_node(identity):
        data = await read_ui(page)
        node = next(node for node in data["nodes"] if node["id"] == identity)
        await page.mouse.click(node["x"] + node["width"] / 2, node["y"] + 5)
        return await settle(page)

    original = await read_ui(page)
    original_ids = {node["id"] for node in original["nodes"]}
    document = original["state"]["documentObjects"]
    document_revision = original["state"]["documentRevision"]
    data = await click("ModelMenu_Grasshopper")
    check(all(identity in data["controls"] for identity in (
        "Menu_Grasshopper_Open", "Menu_Grasshopper_AddComponent", "Menu_Grasshopper_InsertExample", "Menu_Grasshopper_Hide")),
        "The top-level Grasshopper menu exposes opening, component search, example insertion and explicit hiding")
    await click("Menu_Grasshopper_Hide")
    await click("Tool_Definition")
    data = await completed_open()
    check(open_and_focused(data) and data["state"]["documentRevision"] == document_revision,
          "The labelled Grasshopper toolbar button opens and focuses the hidden definition without editing CAD")
    await command("HideGrasshopper")
    await page.keyboard.press("Control+Alt+G")
    await settle(page)
    data = await completed_open()
    check(open_and_focused(data) and data["state"]["graphRevision"] == original["state"]["graphRevision"],
          "Ctrl+Alt+G restores and focuses the actual hidden Grasshopper canvas")
    for name in ("Grasshopper", "Definition", "ShowGrasshopper"):
        await command(name)
        data = await completed_open()
        check(open_and_focused(data) and data["state"]["graphRevision"] == original["state"]["graphRevision"],
              "The " + name + " command is an idempotent opener for the already visible definition")
    await menu("ModelMenu_Grasshopper", "Menu_Grasshopper_Open")
    data = await completed_open()
    check(open_and_focused(data) and data["state"]["documentRevision"] == document_revision,
          "Grasshopper / Open focuses the existing native editor without toggling it closed")

    # Empty only the graph through its real menu. Keep all CAD objects and the
    # prior deletion as an independent undo record for the end of this slice.
    await menu("GraphMenu_Edit", "GraphAction_SelectAll")
    await menu("GraphMenu_Edit", "GraphAction_Delete")
    data = await wait_for_ui(page, solution_condition(editor_closed=True))
    remember("empty", data)
    check(data["state"]["nodeCount"] == data["state"]["wireCount"] == 0
          and data["state"]["gettingStartedVisible"]
          and all(identity in data["controls"] for identity in ("GraphStartAddComponent", "GraphStartExample"))
          and data["state"]["documentObjects"] == document and data["state"]["documentRevision"] == document_revision,
          "Deleting the definition exposes actionable empty-canvas guidance while preserving the entire CAD document")
    await page.screenshot(path=str(EVIDENCE / "uno-grasshopper-onboarding.png"))
    empty_revision = data["state"]["graphRevision"]
    data = await click("GraphStartAddComponent")
    check(data["state"]["searchOpen"] and "CanvasComponentSearch" in data["controls"]
          and data["state"]["graphRevision"] == empty_revision,
          "Getting started / Add a component opens the existing executable native component search")
    await page.keyboard.press("Escape")
    before_example = await settle(page)
    await click("GraphStartExample")
    # Evaluation and the existing dock/fit timer finish independently. Observe
    # both lifetimes before using node coordinates, plus a real CAD submission
    # newer than the input. None of these predicates asks for desired geometry.
    data = await wait_for_ui(page, solution_condition(editor_closed=True)
        + " && value.state.grasshopperOpenPending === false"
        + " && value.state.renderFrames > " + str(before_example["state"]["renderFrames"]))
    remember("inserted", data)
    nodes = {node["name"]: node for node in data["nodes"]}
    terminal = nodes["Extrusion"]["id"]
    expected_wires = {(nodes["Radius"]["id"], "N", nodes["Circle"]["id"], "Radius"),
                      (nodes["Height"]["id"], "N", nodes["Height vector"]["id"], "Length"),
                      (nodes["Circle"]["id"], "C", terminal, "C"),
                      (nodes["Height vector"]["id"], "V", terminal, "V")}
    check(len(nodes) == 5 and data["state"]["nodeCount"] == 5 and data["state"]["wireCount"] == 4
          and nodes["Radius"]["component"] == nodes["Height"]["component"] == "Number"
          and nodes["Radius"]["value"] == 5 and nodes["Height"]["value"] == 10
          and nodes["Circle"]["component"] == "Circle" and nodes["Height vector"]["component"] == "UnitZ"
          and nodes["Extrusion"]["component"] == "Extrude"
          and {(wire["from"], wire["fromPort"], wire["to"], wire["toPort"]) for wire in data["wires"]} == expected_wires
          and data["state"]["graphSelected"] == [terminal] and not data["state"]["gettingStartedVisible"]
          and data["state"]["documentObjects"] == document and data["state"]["documentRevision"] == document_revision,
          "Getting started inserts the five connected example components, selects the extrusion and preserves all CAD objects")
    output = extrusion_output(data, terminal)
    check(cylinder_matches(output, 5, 10) and output["sceneIds"] == ["preview:" + terminal + ":S:0"]
          and output["meshTriangles"] > 0 and data["state"]["graphErrors"] == 0,
          "The starter definition solves an exact radius-five, height-ten surface and publishes its real viewport preview mesh")
    await page.screenshot(path=str(EVIDENCE / "uno-grasshopper-example.png"))

    # Scrub the visible Height rail as taught by the card. Do not invoke a
    # parameter setter or wait for an expected numeric result through the hook.
    height = nodes["Height"]
    await page.mouse.move(height["x"] + height["width"] * .45, height["y"] + height["height"] * .76)
    await page.mouse.down()
    await page.mouse.move(height["x"] + height["width"] * .68, height["y"] + height["height"] * .76, steps=8)
    await page.mouse.up()
    await settle(page)
    await select_node(terminal)
    edited = await wait_for_ui(page, solution_condition(editor_closed=True, after_revision=data["state"]["graphRevision"])
        + " && value.state.renderFrames > " + str(data["state"]["renderFrames"]))
    remember("edited", edited)
    value = next(node["value"] for node in edited["nodes"] if node["id"] == height["id"])
    output = extrusion_output(edited, terminal)
    check(value != 10 and 1 <= value <= 30 and cylinder_matches(output, 5, value)
          and output["sceneIds"] == ["preview:" + terminal + ":S:0"] and output["meshTriangles"] > 0
          and edited["state"]["documentObjects"] == document and edited["state"]["documentRevision"] == document_revision,
          "Dragging the example Height rail updates the exact extrusion and viewport preview without editing CAD")
    await click("GraphUndo")
    data = await wait_for_ui(page, solution_condition(editor_closed=True))
    output = extrusion_output(data, terminal)
    check(next(node["value"] for node in data["nodes"] if node["id"] == height["id"]) == 10
          and cylinder_matches(output, 5, 10),
          "One graph undo restores the complete example slider gesture and its exact surface")
    data = await click("GraphBake")
    remember("baked", data)
    baked_objects = data["state"]["documentObjects"]
    baked_revision = data["state"]["documentRevision"]
    check(data["state"]["objects"] == original["state"]["objects"] + 1
          and data["lastGeometry"]["kind"] == "surface" and data["lastGeometry"]["samples"] == output["shape"]["samples"]
          and baked_objects[-1]["source"] == terminal and data["state"]["documentRevision"] > document_revision,
          "Bake converts the selected example output into one exact, source-linked CAD surface")
    await click("GraphUndo")
    data = await wait_for_ui(page, solution_condition(editor_closed=True))
    remember("exampleUndone", data)
    check(data["state"]["nodeCount"] == data["state"]["wireCount"] == 0 and data["state"]["gettingStartedVisible"]
          and data["state"]["documentObjects"] == baked_objects and data["state"]["documentRevision"] == baked_revision,
          "One graph undo removes the entire inserted example and restores guidance while retaining the baked CAD surface")
    await click("GraphUndo")
    data = await wait_for_ui(page, solution_condition(editor_closed=True))
    check({node["id"] for node in data["nodes"]} == original_ids and data["wires"] == original["wires"]
          and data["state"]["documentObjects"] == baked_objects,
          "The preceding graph undo independently restores the original definition and every wire identity")
    data = await command("Undo")
    check(data["state"]["documentObjects"] == document and {node["id"] for node in data["nodes"]} == original_ids
          and data["wires"] == original["wires"] and data["state"]["graphErrors"] == 0,
          "Document undo removes only the baked surface and leaves the restored original Grasshopper definition intact")
    await click("GraphFit")
    remember("restored", await settle(page))
