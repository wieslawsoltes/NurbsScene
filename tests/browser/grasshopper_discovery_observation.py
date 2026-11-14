"""Real native launcher input after the untouched startup capture is released.

The application only observes the native tree during this separate 20-second
phase. Neither this helper nor its finish flag can dispatch a managed command.
"""
import json
import math


MODEL_FIELDS = ("objects", "nodes", "documentRevision", "graphRevision",
                "documentUndo", "documentRedo", "graphUndo", "graphRedo", "history")


def require_discovery_snapshot(value):
    if (not isinstance(value, dict) or value.get("status") != "ready"
            or type(value.get("sample")) is not int or value["sample"] < 1
            or not isinstance(value.get("workspace"), dict)
            or not isinstance(value["workspace"].get("controls"), dict)
            or not isinstance(value["workspace"].get("state"), dict)):
        raise AssertionError("Native Grasshopper discovery was not observed: " + json.dumps(value))
    return value


def discovery_predicate(after_sample=0, *, completed_open=False):
    if type(after_sample) is not int or not 0 <= after_sample < 2**53:
        raise ValueError("An exact nonnegative observation sample is required")
    if type(completed_open) is not bool:
        raise ValueError("A Boolean opener-lifetime constraint is required")
    completion = (" && value.workspace.state.openPending === false"
                  " && value.workspace.state.graphFrames > 0") if completed_open else ""
    return """() => {
        const value = globalThis.nurbsSceneGrasshopperDiscovery;
        const modeling = globalThis.nurbsSceneQualification;
        if (modeling?.status === 'failed') return modeling;
        if (value == null) return false;
        if (value.status !== 'ready') return value;
        return value.sample >= """ + str(after_sample) + completion + """ ? value : false;
    }"""


async def wait_for_discovery(page, after_sample=0, *, completed_open=False):
    handle = await page.wait_for_function(discovery_predicate(after_sample, completed_open=completed_open), timeout=20000)
    try:
        return require_discovery_snapshot(await handle.json_value())
    finally:
        await handle.dispose()


def require_native_box(workspace, identity):
    box = workspace["controls"].get(identity)
    state = workspace["state"]
    if (not isinstance(box, dict) or box.get("enabled") is not True
            or any(type(box.get(key)) not in (int, float) or not math.isfinite(box[key])
                   for key in ("x", "y", "width", "height"))
            or box["width"] <= 0 or box["height"] <= 0
            or box["x"] < -1 or box["y"] < -1
            or box["x"] + box["width"] > state["workspaceWidth"] + 1
            or box["y"] + box["height"] > state["workspaceHeight"] + 1):
        raise AssertionError("Visible native Grasshopper control is unavailable: " + identity + " " + json.dumps(workspace))
    return box


def require_unchanged_model(before, after):
    if any(before.get(key) != after.get(key) for key in MODEL_FIELDS):
        raise AssertionError("Opening Grasshopper changed ordinary model/history state: " + json.dumps({"before": before, "after": after}))


def require_open_workspace(workspace):
    state = workspace["state"]
    canvas = require_native_box(workspace, "ParametricCanvas")
    for identity in ("GraphGettingStarted", "GraphStartAddComponent", "GraphStartExample"):
        require_native_box(workspace, identity)
    if (state.get("definitionVisible") is not True or state.get("definitionFolded") is not False
            or state.get("editorFolded") is not False or state.get("gettingStartedVisible") is not True
            or state.get("openPending") is not False or state.get("graphFocused") is not True
            or state.get("graphFocus") == "Unfocused" or canvas.get("focused") is not True
            or canvas["width"] <= 120 or canvas["height"] <= 80
            or type(state.get("graphFrames")) is not int or state["graphFrames"] < 1
            or "graphError" not in state or state["graphError"] is not None
            or "gpuError" not in state or state["gpuError"] is not None):
        raise AssertionError("Completed native opener did not expose a focused, usable Grasshopper canvas: " + json.dumps(workspace))


async def verify_startup_discovery(page, startup, destination, *, saved_preferences=None):
    """Three first-result assertions; preserve untouched and interacted states."""
    record = {"status": "running", "startup": startup, "checks": [], "inputs": []}
    label = "Saved hidden/folded startup" if saved_preferences is not None else "Ordinary startup"

    def save():
        destination.with_suffix(".json").write_text(json.dumps(record, indent=2))

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        record["checks"].append(name)
        save()
        print("GRASSHOPPER_DISCOVERY " + name, flush=True)

    async def open_once(before):
        box = require_native_box(before["workspace"], "OpenGrasshopper")
        point = [box["x"] + box["width"] / 2, box["y"] + box["height"] / 2]
        record["inputs"].append({"control": "OpenGrasshopper", "button": "left", "point": point, "beforeSample": before["sample"]})
        save()
        await page.mouse.click(*point, delay=75)
        # Fresh samples plus the opener's real completion flag are independent
        # of the expected visibility/focus asserted below. Never wait for those
        # expected values to turn green or re-issue a failed click.
        first = await wait_for_discovery(page)
        return await wait_for_discovery(page, first["sample"] + 2, completed_open=True)

    save()
    try:
        original = startup.get("grasshopper")
        if not isinstance(original, dict):
            raise AssertionError("Untouched startup omitted native Grasshopper chrome")
        for identity in ("OpenGrasshopper", "Tool_Definition", "ModelMenu_Grasshopper"):
            box = require_native_box(original, identity)
            if "Grasshopper" not in box.get("labels", []):
                raise AssertionError("Grasshopper is not visibly labelled in " + identity + ": " + json.dumps(box))
        state = original["state"]
        check(state.get("definitionVisible") is False and state.get("objects") == state.get("nodes") == 0
              and state.get("history") == startup["state"]["history"] == ""
              and state.get("preferences") == startup["state"]["preferences"]
              and (saved_preferences is None or (state["preferences"] == saved_preferences
                   and state.get("definitionFloating") is True and state.get("definitionFolded") is True)),
              label + " exposes labelled Grasshopper launchers while preserving the empty, hidden document")
        before = record["before"] = await wait_for_discovery(page)
        require_unchanged_model(state, before["workspace"]["state"])
        if before["workspace"]["state"]["preferences"] != state["preferences"]:
            raise AssertionError("Discovery started after an unrecorded layout change")
        opened = record["opened"] = await open_once(before)
        save()
        require_open_workspace(opened["workspace"])
        require_unchanged_model(state, opened["workspace"]["state"])
        check(True, label + " launcher opens an unfolded, focused canvas with real component-search and example actions")
        reopened = record["reopened"] = await open_once(opened)
        save()
        require_open_workspace(reopened["workspace"])
        require_unchanged_model(state, reopened["workspace"]["state"])
        check(reopened["workspace"]["state"]["preferences"] == opened["workspace"]["state"]["preferences"],
              label + " repeated launch stays open without changing model history or the restored layout")
        await page.screenshot(path=str(destination.with_suffix(".png")))
        record["status"] = "passed"
        save()
        await page.evaluate("globalThis.nurbsSceneGrasshopperDiscoveryContinue=true")
        return record
    except Exception as error:
        record["status"] = "failed"
        record["error"] = str(error)
        save()
        raise
