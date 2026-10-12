"""Deterministic harness lifecycle regressions, independent of browser/GPU.
The JavaScript predicate is checked in Node; async ordering uses a bounded fake
page. Fake handles model snapshot capture, not a second global read.
Actual Uno input campaigns remain the application acceptance tests.
"""
import asyncio
import json
import subprocess
import unittest
from ui_observation import READY_EXPRESSION, wait_ready, settle

class SnapshotHandle:
    def __init__(self, page): self.page = page
    async def json_value(self):
        self.page.calls.append(("read", "snapshot handle"))
        return next(self.page.observations)
    async def dispose(self): pass

class Page:
    def __init__(self, observations):
        self.observations = iter(observations)
        self.calls = []
    async def wait_for_function(self, expression, **options):
        self.calls.append(("wait", expression, options))
        return SnapshotHandle(self)
    async def evaluate(self, expression):
        raise AssertionError("Do not re-read a global after capturing the satisfied snapshot")

class ObservationTests(unittest.IsolatedAsyncioTestCase):
    def test_predicate_does_not_treat_missing_state_as_ready(self):
        script = "const predicate = () => " + READY_EXPRESSION + ";" + """
        const cases = [undefined, null, {}, {status:'running'},
            {status:'ready'}, {status:'failed'}, {status:'restored'}];
        console.log(JSON.stringify(cases.map(state => {
            globalThis.nurbsSceneUi = state; return predicate();
        })));
        """
        result = subprocess.run(["node", "-e", script], text=True, capture_output=True, check=True)
        self.assertEqual(json.loads(result.stdout), [False, False, False, False, True, True, True])
    async def test_ready_wait_precedes_state_read(self):
        page = Page([{"status":"ready", "controls":{}, "state":{}, "sample":1}])
        self.assertEqual((await wait_ready(page))["sample"], 1)
        self.assertEqual([c[0] for c in page.calls], ["wait", "read"])
    async def test_terminal_and_invalid_snapshots_are_diagnosed(self):
        for value in [None, {}, {"status":"failed", "error":"actual failure"}, {"status":"restored"}]:
            with self.subTest(value=value), self.assertRaisesRegex(AssertionError, "did not become ready"):
                await wait_ready(Page([value]))
    async def test_settle_requires_two_new_samples(self):
        page = Page([{"status":"ready", "controls":{}, "state":{}, "sample":8}, {"status":"ready", "controls":{}, "state":{}, "sample":10}])
        self.assertEqual((await settle(page))["sample"], 10)
        self.assertIn("sample >= 10", page.calls[2][1])
    async def test_stopping_during_input_is_not_silently_accepted(self):
        page = Page([{"status":"ready", "controls":{}, "state":{}, "sample":8}, {"status":"failed", "error":"rendering"}])
        with self.assertRaisesRegex(AssertionError, "stopped during input"):
            await settle(page)
    async def test_invalid_sample_is_rejected_before_input_wait(self):
        for sample in [None, True, -1, "2"]:
            page = Page([{"status":"ready", "controls":{}, "state":{}, "sample":sample}])
            with self.subTest(sample=sample), self.assertRaisesRegex(AssertionError, "Invalid workspace observation sample"):
                await settle(page)
            self.assertEqual(len(page.calls), 2)

if __name__ == "__main__":
    unittest.main()
