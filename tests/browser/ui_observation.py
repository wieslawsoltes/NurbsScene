"""Observation-only handoff between Uno qualification and real input tests.

A missing observer is pending, not a failed/finished observer. Return the exact
snapshot which satisfied the wait so a later publication cannot race a second
read. An explicit failure or premature restoration fails immediately. No action
is dispatched, model mutated, timeout extended, or failed campaign retried here.
"""
from typing import Any
import math

# Preserve PR10's published readiness predicate and helper name for callers.
READY_EXPRESSION = """(() => {
    const observation = globalThis.nurbsSceneUi;
    return !!observation && ["ready", "failed", "restored"].includes(observation.status);
})()"""


def observation_predicate(expression: str = "true", *, restored: bool = False) -> str:
    """Build a predicate from a test-authored expression, never application text."""
    if restored:
        return '''() => {
            const value = globalThis.nurbsSceneUi;
            if (value == null || value.status === "running" || value.status === "ready") return false;
            return value;
        }'''
    return '''() => {
        const value = globalThis.nurbsSceneUi;
        if (value == null || value.status === "running") return false;
        if (value.status !== "ready") return value;
        return (''' + expression + ''') ? value : false;
    }'''


def require_snapshot(value: Any, expected: str = "ready") -> dict:
    if not isinstance(value, dict) or value.get("status") != expected:
        raise AssertionError(f"Expected Uno UI state {expected!r}; observed {value!r}")
    if expected == "ready":
        if not isinstance(value.get("controls"), dict) or not isinstance(value.get("state"), dict):
            raise AssertionError(f"Incomplete ready UI snapshot: {value!r}")
        if type(value.get("sample")) is not int or value["sample"] < 1:
            raise AssertionError(f"Invalid workspace observation sample: {value!r}")
    return value


async def _wait(page, predicate: str, expected: str, timeout: int) -> dict:
    handle = await page.wait_for_function(predicate, timeout=timeout)
    try:
        return require_snapshot(await handle.json_value(), expected)
    finally:
        await handle.dispose()


async def wait_for_ui(page, expression: str = "true", *, timeout: int = 20000) -> dict:
    return await _wait(page, observation_predicate(expression), "ready", timeout)


def solution_condition(*, editor_closed: bool = False, after_revision: int | None = None) -> str:
    """Wait for completion, not for expected output values. Dialog visibility
    alone does not prove its ShowAsync continuation has committed, and a graph
    edit schedules evaluation separately. Preserve incorrect completed results
    for the caller's original assertions instead of waiting for them to change.
    """
    if type(editor_closed) is not bool:
        raise ValueError("A Boolean editor-lifetime constraint is required")
    if after_revision is not None and (type(after_revision) is not int or after_revision < 0 or after_revision > 2**53 - 1):
        raise ValueError("A nonnegative exact prior revision is required")
    expression = "!!value.values && Number.isSafeInteger(value.state.graphRevision) && value.values.solvedRevision === value.state.graphRevision"
    if editor_closed:
        expression += " && value.values.editorPending === false"
    if after_revision is not None:
        expression += " && value.state.graphRevision > " + str(after_revision)
    return expression


async def wait_for_solved_ui(page, *, editor_closed: bool = False, after_revision: int | None = None) -> dict:
    return await wait_for_ui(page, solution_condition(editor_closed=editor_closed, after_revision=after_revision))


async def wait_ready(page, timeout: int = 30000) -> dict:
    """Compatible PR10 entry point, using the race-free snapshot handle."""
    try:
        return await wait_for_ui(page, timeout=timeout)
    except AssertionError as error:
        raise AssertionError({"message": "Workspace observer did not become ready", "detail": str(error)}) from error


async def read_ui(page) -> dict:
    return require_snapshot(await page.evaluate("globalThis.nurbsSceneUi"))


async def wait_for_restored(page, *, timeout: int = 15000) -> dict:
    return await _wait(page, observation_predicate(restored=True), "restored", timeout)


async def settle(page) -> dict:
    # Two snapshots after actual input; neither a fixed delay nor pre-input state
    # is treated as proof the native controls have completed layout or evaluation.
    first = await wait_for_ui(page)
    try:
        return await wait_for_ui(page, "globalThis.nurbsSceneUi.sample >= " + str(first["sample"] + 2))
    except AssertionError as error:
        raise AssertionError({"message": "Workspace observer stopped during input", "detail": str(error)}) from error


def text_editor_point(bounds: dict) -> tuple[float, float]:
    """Target the lower editor, not the header/gap included in control bounds.

    Computes coordinates only. Callers must verify the actual focused control
    and captured draft before sending or accepting text; no focus is forced here.
    """
    try:
        x, y, width, height = (bounds[k] for k in ("x", "y", "width", "height"))
        if any(type(v) not in (int, float) or not math.isfinite(v) for v in (x, y, width, height)):
            raise ValueError("Nonfinite or nonnumeric editor bounds")
        if width <= 0 or height <= 0:
            raise ValueError("Editor bounds must have positive dimensions")
        return x + width / 2, y + height - min(12, height / 2)
    except (KeyError, TypeError) as error:
        raise ValueError("Incomplete editor bounds") from error
