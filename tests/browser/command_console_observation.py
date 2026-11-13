"""Read the actual native console layout and preserve ordinary startup evidence.

The checkpoint only delays the existing modeling fixture while its untouched
startup is captured. It cannot dispatch a command or change native input state.
"""
import json
import math


def require_startup(value):
    if (not isinstance(value, dict) or value.get("status") != "ready"
            or type(value.get("sample")) is not int or value["sample"] < 1
            or not isinstance(value.get("state"), dict)
            or not isinstance(value.get("console"), dict)):
        raise AssertionError("Ordinary native startup was not observed: " + json.dumps(value))
    return value


async def capture_startup(page, destination):
    """Capture before allowing the pre-existing qualification fixture to load."""
    async def wait(after_sample=0):
        predicate = """() => {
            const startup = globalThis.nurbsSceneStartup;
            const modeling = globalThis.nurbsSceneQualification;
            if (startup && startup.status !== 'ready') return startup;
            if (modeling && modeling.status === 'failed') return modeling;
            return startup && startup.sample >= """ + str(after_sample) + """ ? startup : false;
        }"""
        # The ordinary first frame uses the same bounded application startup
        # envelope as the existing managed qualification. Once the checkpoint
        # appears, its application-side lifetime is independently only 20s.
        handle = await page.wait_for_function(predicate, timeout=180000)
        try:
            return require_startup(await handle.json_value())
        finally:
            await handle.dispose()

    first = await wait()
    snapshot = await wait(first["sample"] + 2)
    destination.with_suffix(".json").write_text(json.dumps(snapshot, indent=2))
    await page.screenshot(path=str(destination.with_suffix(".png")))
    await page.evaluate("globalThis.nurbsSceneStartupContinue=true")
    return snapshot


def _near(actual, expected, tolerance=0.5):
    return isinstance(actual, (int, float)) and math.isfinite(actual) and abs(actual - expected) <= tolerance


def require_entry_layout(console):
    """Validate visible native text, not only the outer AutoSuggestBox style."""
    try:
        entry, prompt, input_control = console["entry"], console["prompt"], console["input"]
        inner, rendered = input_control["inner"], console["renderedInput"]
        prompt_box, input_box = prompt["bounds"], input_control["bounds"]
        prompt_baseline = prompt_box["y"] + prompt["padding"]["top"] + prompt["baselineOffset"]
        gap = input_box["x"] - prompt_box["x"] - prompt_box["width"]
        valid = (
            _near(entry["height"], 20)
            and prompt_box["loaded"] and inner["bounds"]["loaded"]
            and all(_near(item["fontSize"], 12, 0.01) for item in (prompt, input_control, inner))
            and prompt["fontWeight"] == 600 and inner["fontWeight"] == 400
            and input_control["placeholder"] == inner["placeholder"] == ""
            and input_control["text"] == inner["text"]
            and -0.5 <= gap <= 5
            and prompt["baselineOffset"] > 0
            and entry["y"] - 0.5 <= prompt_box["y"]
            and prompt_box["y"] + prompt_box["height"] <= entry["y"] + entry["height"] + 0.5
            and entry["y"] - 0.5 <= inner["bounds"]["y"]
            and inner["bounds"]["y"] + inner["bounds"]["height"] <= entry["y"] + entry["height"] + 0.5
        )
        # A genuinely empty, placeholder-free input has no visible glyphs to
        # measure. Require real renderer geometry as soon as native typing or
        # recall creates text; never manufacture an empty-input baseline.
        if inner["text"]:
            input_baseline = rendered["bounds"]["y"] + rendered["padding"]["top"] + rendered["baselineOffset"]
            valid = (valid and rendered["bounds"]["loaded"] and rendered["baselineOffset"] > 0
                     and _near(rendered["fontSize"], 12, 0.01) and rendered["fontWeight"] == 400
                     and _near(prompt_baseline, input_baseline, 1)
                     and entry["y"] - 0.5 <= rendered["bounds"]["y"]
                     and rendered["bounds"]["y"] + rendered["bounds"]["height"] <= entry["y"] + entry["height"] + 0.5)
    except (KeyError, TypeError) as error:
        raise AssertionError("Native command-entry metrics are incomplete: " + json.dumps(console)) from error
    if not valid:
        raise AssertionError("Native command prompt/input are not compact and baseline aligned: " + json.dumps(console))


def require_history_layout(console, visible_lines, *, tail=True):
    """Require integral native line boxes inside the real scroll viewport."""
    try:
        history, text = console["history"], console["historyText"]
        viewport, bounds = history["bounds"], text["bounds"]
        pitch = text["lineHeight"]
        valid = (
            viewport["loaded"] and bounds["loaded"] and text["selectionEnabled"]
            and _near(text["fontSize"], 12, 0.01) and text["fontWeight"] == 400
            and _near(pitch, 16, 0.01) and text["lineStacking"] == "BlockLineHeight"
            and text["wrapping"] == "Wrap" and text["text"] == console["text"]
            and _near(history["viewportHeight"], visible_lines * pitch)
            and _near(viewport["height"], history["viewportHeight"])
            and history["horizontalScrollBar"] == "Disabled"
            and _near(history["horizontalOffset"], 0)
            and history["extentWidth"] <= history["viewportWidth"] + 0.5
            and _near(text["padding"]["top"], 0) and _near(text["padding"]["bottom"], 0)
            and (not text["text"] or 0 < text["baselineOffset"] <= pitch)
            and (not text["text"] or _near(bounds["desiredHeight"], round(bounds["desiredHeight"] / pitch) * pitch))
            and _near(history["verticalOffset"], round(history["verticalOffset"] / pitch) * pitch)
            and _near(bounds["y"] + history["verticalOffset"], viewport["y"])
            and (not tail or _near(history["verticalOffset"], history["scrollableHeight"]))
        )
    except (KeyError, TypeError, ZeroDivisionError, ValueError) as error:
        raise AssertionError("Native command-history metrics are incomplete: " + json.dumps(console)) from error
    if not valid:
        raise AssertionError("Native command history clips a line, is not at its tail, or reserves horizontal-scroll space: " + json.dumps(console))
