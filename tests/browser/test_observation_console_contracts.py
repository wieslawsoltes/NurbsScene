"""Console observation failure modes; actual Uno campaigns remain mandatory."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

from command_console_observation import capture_startup, require_entry_layout, require_history_layout


def bounds(x, y, width, height):
    return {"x": x, "y": y, "width": width, "height": height,
            "desiredWidth": width, "desiredHeight": height, "loaded": True, "visible": True}


def native_console():
    text = "NurbsScene\nCommand: ZoomExtents\nCommand: ModelingLayout"
    return {
        "entry": bounds(0, 82, 1600, 20), "text": text,
        "prompt": {"bounds": bounds(7, 84, 58, 16), "fontSize": 12, "fontWeight": 600, "baselineOffset": 12, "padding": {"top": 0}},
        "input": {"bounds": bounds(68, 82, 1500, 20), "fontSize": 12, "placeholder": "", "text": "draft",
                  "inner": {"bounds": bounds(68, 82, 1500, 20), "fontSize": 12, "fontWeight": 400, "placeholder": "", "text": "draft"}},
        "renderedInput": {"bounds": bounds(68, 84, 110, 16), "fontSize": 12, "fontWeight": 400, "baselineOffset": 12, "padding": {"top": 0}},
        "historyText": {"bounds": bounds(7, 34, 1580, 48), "text": text, "fontSize": 12, "fontWeight": 400,
                        "lineHeight": 16, "lineStacking": "BlockLineHeight", "wrapping": "Wrap",
                        "selectionEnabled": True, "baselineOffset": 12, "padding": {"top": 0, "bottom": 0}},
        "history": {"bounds": bounds(0, 50, 1600, 32), "viewportHeight": 32, "viewportWidth": 1600,
                    "extentWidth": 1580, "extentHeight": 48, "horizontalScrollBar": "Disabled",
                    "horizontalOffset": 0, "verticalOffset": 16, "scrollableHeight": 16}
    }


class ConsoleGeometryContracts(unittest.TestCase):
    def test_native_complete_rows_and_entry_baseline_are_accepted(self):
        require_entry_layout(native_console())
        require_history_layout(native_console(), 2)

    def test_empty_startup_has_no_invented_input_or_history_glyphs(self):
        value = native_console()
        value['input']['text'] = value['input']['inner']['text'] = ''
        value['renderedInput'] = None
        value['text'] = value['historyText']['text'] = ''
        value['historyText']['baselineOffset'] = 0
        value['historyText']['bounds'] = bounds(7, 50, 1580, 0)
        value['history'].update(extentHeight=0, verticalOffset=0, scrollableHeight=0)
        require_entry_layout(value)
        require_history_layout(value, 2)

    def test_former_38_dip_partial_line_viewport_rejects(self):
        value = native_console()
        value["history"]["bounds"]["height"] = value["history"]["viewportHeight"] = 38
        with self.assertRaises(AssertionError):
            require_history_layout(value, 2)

    def test_fractional_scroll_cannot_hide_a_clipped_first_line(self):
        value = native_console()
        value["history"]["verticalOffset"] = value["history"]["scrollableHeight"] = 15
        value["historyText"]["bounds"]["y"] = 35
        with self.assertRaises(AssertionError):
            require_history_layout(value, 2)

    def test_stale_tail_and_reserved_horizontal_scroll_space_reject(self):
        for update in ({"verticalOffset": 0}, {"horizontalScrollBar": "Auto"}, {"viewportHeight": 20}):
            value = native_console()
            value["history"].update(update)
            with self.subTest(update=update), self.assertRaises(AssertionError):
                require_history_layout(value, 2)

    def test_outer_font_cannot_conceal_oversized_native_editor(self):
        value = native_console()
        value["input"]["inner"]["fontSize"] = 14
        with self.assertRaises(AssertionError):
            require_entry_layout(value)

    def test_native_baseline_offset_and_missing_renderer_reject(self):
        value = native_console()
        value["renderedInput"]["bounds"]["y"] += 3
        with self.assertRaises(AssertionError):
            require_entry_layout(value)
        value["renderedInput"] = None
        with self.assertRaises(AssertionError):
            require_entry_layout(value)


class Handle:
    def __init__(self, value): self.value = value
    async def json_value(self): return copy.deepcopy(self.value)
    async def dispose(self): pass


class Page:
    def __init__(self, values): self.values, self.events = iter(values), []
    async def wait_for_function(self, predicate, timeout):
        self.events.append("observe")
        return Handle(next(self.values))
    async def screenshot(self, *, path):
        self.events.append("screenshot")
        Path(path).write_bytes(b"\x89PNG\r\n\x1a\nfixture")
    async def evaluate(self, expression):
        if expression != "globalThis.nurbsSceneStartupContinue=true":
            raise AssertionError("Unexpected application mutation")
        self.events.append("release")


class StartupCheckpointContracts(unittest.IsolatedAsyncioTestCase):
    async def test_original_snapshot_and_screenshot_are_saved_before_fixture_release(self):
        first = {"status": "ready", "sample": 1, "state": {}, "console": native_console()}
        final = first | {"sample": 3}
        page = Page([first, final])
        with tempfile.TemporaryDirectory() as folder:
            destination = Path(folder) / "startup"
            result = await capture_startup(page, destination)
            self.assertEqual(json.loads(destination.with_suffix(".json").read_text()), final)
            self.assertEqual(result, final)
            self.assertEqual(page.events, ["observe", "observe", "screenshot", "release"])

    async def test_failed_startup_is_not_released_or_retried(self):
        page = Page([{"status": "failed", "error": "native startup failed"}])
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(AssertionError):
            await capture_startup(page, Path(folder) / "startup")
        self.assertEqual(page.events, ["observe"])


if __name__ == "__main__":
    unittest.main()
