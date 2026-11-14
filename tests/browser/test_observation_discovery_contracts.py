"""Failure-mode contracts for the separate native discovery handoff."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

from grasshopper_discovery_observation import (discovery_predicate, require_open_workspace,
                                             require_launcher_echo, require_unchanged_model, verify_startup_discovery)


def workspace(*, opened=False, saved=False):
    preferences = ("3|Float|0|1|0.55|250|1|1|Layers|0.3|0.1|0.6|0.7|1" if saved
                   else "2|Right|0|1|0.55|250|1|0|Properties")
    if opened:
        fields = preferences.split("|")
        fields[2] = "1"
        if saved:
            fields[13] = "0"
        preferences = "|".join(fields)
    controls = {identity: {"x": 20 + i * 130, "y": 0, "width": 120, "height": 20,
                           "enabled": True, "focused": False, "labels": ["Grasshopper"]}
                for i, identity in enumerate(("OpenGrasshopper", "Tool_Definition", "ModelMenu_Grasshopper"))}
    if opened:
        controls.update({identity: {"x": 800, "y": 300 + i * 50, "width": 300, "height": 40 if i else 500,
                                    "enabled": True, "focused": i == 0, "labels": []}
                         for i, identity in enumerate(("ParametricCanvas", "GraphGettingStarted", "GraphStartAddComponent", "GraphStartExample"))})
    return {"controls": controls, "state": {
        "objects": 0, "nodes": 0, "documentRevision": 0, "graphRevision": 0,
        "documentUndo": False, "documentRedo": False, "graphUndo": False, "graphRedo": False,
        "history": "Command: Grasshopper" if opened else "", "preferences": preferences, "definitionVisible": opened,
        "definitionFolded": saved and not opened, "editorFolded": saved and not opened,
        "definitionFloating": saved, "openPending": False, "gettingStartedVisible": opened,
        "graphFocused": opened, "graphFocus": "Programmatic" if opened else "Unfocused",
        "graphFrames": 1 if opened else 0, "graphError": None, "frames": 2, "gpuError": None,
        "workspaceWidth": 1600, "workspaceHeight": 1100}}


def startup(*, saved=False):
    native = workspace(saved=saved)
    return {"status": "ready", "sample": 30, "grasshopper": native,
            "state": {"objects": 0, "nodes": 0, "history": "", "preferences": native["state"]["preferences"]}}


def sample(number, *, opened=False, saved=False):
    native = workspace(opened=opened, saved=saved)
    if opened and number >= 5:
        native["state"]["history"] = "Command: Grasshopper\nCommand: Grasshopper"
    return {"status": "ready", "sample": number, "workspace": native}


class Handle:
    def __init__(self, value): self.value = value
    async def json_value(self): return copy.deepcopy(self.value)
    async def dispose(self): pass


class Mouse:
    def __init__(self, events): self.events = events
    async def click(self, x, y, *, delay): self.events.append(("click", x, y, delay))


class Page:
    def __init__(self, values):
        self.values, self.events, self.waits = iter(values), [], []
        self.mouse = Mouse(self.events)
    async def wait_for_function(self, predicate, timeout):
        self.waits.append((predicate, timeout))
        self.events.append("observe")
        return Handle(next(self.values))
    async def screenshot(self, *, path):
        self.events.append("screenshot")
        Path(path).write_bytes(b"\x89PNG\r\n\x1a\nfixture")
    async def evaluate(self, expression):
        if expression != "globalThis.nurbsSceneGrasshopperDiscoveryContinue=true":
            raise AssertionError("Unexpected application mutation")
        self.events.append("release")


class DiscoveryContracts(unittest.IsolatedAsyncioTestCase):
    async def test_two_native_clicks_preserve_untouched_startup_and_release_after_capture(self):
        original = startup()
        before = copy.deepcopy(original)
        page = Page([sample(1), sample(2, opened=True), sample(4, opened=True), sample(5, opened=True), sample(7, opened=True)])
        with tempfile.TemporaryDirectory() as folder:
            result = await verify_startup_discovery(page, original, Path(folder) / "discovery")
            self.assertEqual(result, json.loads((Path(folder) / "discovery.json").read_text()))
        self.assertEqual(original, before)
        self.assertEqual(len(result["checks"]), 3)
        self.assertEqual(sum(isinstance(event, tuple) and event[0] == "click" for event in page.events), 2)
        self.assertEqual(page.events[-2:], ["screenshot", "release"])
        self.assertTrue(all(timeout == 20000 for _, timeout in page.waits))

    async def test_saved_hidden_folded_state_is_retained_separately_from_native_reopening(self):
        original = startup(saved=True)
        page = Page([sample(1, saved=True), sample(2, opened=True, saved=True), sample(4, opened=True, saved=True),
                     sample(5, opened=True, saved=True), sample(7, opened=True, saved=True)])
        with tempfile.TemporaryDirectory() as folder:
            result = await verify_startup_discovery(page, original, Path(folder) / "discovery",
                                                   saved_preferences=original["state"]["preferences"])
        self.assertTrue(result["startup"]["grasshopper"]["state"]["definitionFolded"])
        self.assertFalse(result["startup"]["grasshopper"]["state"]["definitionVisible"])
        self.assertFalse(result["reopened"]["workspace"]["state"]["definitionFolded"])

    async def test_completed_wrong_focus_fails_first_result_without_a_second_click(self):
        failed = sample(4, opened=True)
        failed["workspace"]["state"]["graphFocused"] = False
        page = Page([sample(1), sample(2, opened=True), failed])
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(AssertionError):
            await verify_startup_discovery(page, startup(), Path(folder) / "discovery")
        self.assertEqual(sum(isinstance(event, tuple) for event in page.events), 1)
        self.assertNotIn("release", page.events)
        self.assertNotIn("screenshot", page.events)

    async def test_expired_observer_fails_without_input_or_release(self):
        page = Page([{"status": "failed", "error": "expired"}])
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(AssertionError):
            await verify_startup_discovery(page, startup(), Path(folder) / "discovery")
        self.assertEqual(page.events, ["observe"])

    def test_completion_predicate_observes_lifetime_and_frames_not_expected_focus_or_visibility(self):
        predicate = discovery_predicate(12, completed_open=True)
        self.assertIn("value.sample >= 12", predicate)
        self.assertIn("openPending === false", predicate)
        self.assertIn("graphFrames > 0", predicate)
        for expected in ("definitionVisible", "definitionFolded", "graphFocused", "gettingStartedVisible"):
            self.assertNotIn(expected, predicate)
        for invalid in (True, -1, float("nan"), 2**53):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                discovery_predicate(invalid)

    def test_native_empty_canvas_cannot_be_accepted_with_missing_focus_controls_or_frames(self):
        require_open_workspace(workspace(opened=True))
        for key, value in (("graphFocused", False), ("graphFrames", 0), ("openPending", True),
                           ("definitionFolded", True), ("graphError", "device lost")):
            native = workspace(opened=True)
            native["state"][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError):
                require_open_workspace(native)
        native = workspace(opened=True)
        del native["controls"]["GraphStartExample"]
        with self.assertRaises(AssertionError):
            require_open_workspace(native)

    def test_opening_must_preserve_each_model_revision_and_undo_history(self):
        native = workspace()["state"]
        for key in ("objects", "nodes", "documentRevision", "graphRevision", "documentUndo", "graphRedo"):
            changed = copy.deepcopy(native)
            changed[key] = "changed"
            with self.subTest(key=key), self.assertRaises(AssertionError):
                require_unchanged_model(native, changed)

    def test_only_the_two_actual_launcher_command_echoes_are_expected_after_clean_startup(self):
        require_launcher_echo({"history": ""}, 0)
        require_launcher_echo({"history": "Command: Grasshopper"}, 1)
        require_launcher_echo({"history": "Command: Grasshopper\nCommand: Grasshopper"}, 2)
        for text in ("", "Command: Grasshopper", "renderer frame submitted", "Command: Grasshopper\nCommand: Grasshopper\nextra"):
            with self.subTest(text=text), self.assertRaises(AssertionError):
                require_launcher_echo({"history": text}, 2)


if __name__ == "__main__":
    unittest.main()
