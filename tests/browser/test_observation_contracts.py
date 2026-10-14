"""Pure waiter unit tests; not Uno, GPU or browser-application qualification.
The production JavaScript predicate is evaluated in Node's VM. Python adapters
are tested with explicit fake handles, including delayed and failing observers.
"""
import asyncio
import json
import subprocess
import unittest
from ui_observation import observation_predicate, require_snapshot, wait_for_ui, wait_for_restored, settle, solution_condition, wait_for_solved_ui

READY = {'status': 'ready', 'sample': 3, 'controls': {}, 'state': {}}


def predicates(expressions, values):
    # Each missing observation gets a completely fresh JS context.
    script = '''const vm = require('node:vm'), fs = require('node:fs');
const {expressions, values} = JSON.parse(fs.readFileSync(0, 'utf8'));
console.log(JSON.stringify(expressions.map(expression => values.map(value => {
 const context = value === '__undefined__' ? {} : {nurbsSceneUi: value};
 const result = vm.runInNewContext('(' + expression + ')()', context, {timeout:1000});
 return result === undefined ? null : result;
}))));'''
    result = subprocess.run(['node', '-e', script], input=json.dumps({'expressions': expressions, 'values': values}),
                            text=True, capture_output=True, check=True, timeout=10)
    return json.loads(result.stdout)


class PredicateContracts(unittest.TestCase):
    def test_missing_and_running_observers_remain_pending(self):
        expressions = [observation_predicate('globalThis.nurbsSceneUi.controls.GraphFit'), observation_predicate(restored=True)]
        self.assertEqual(predicates(expressions, ['__undefined__', None, {'status':'running'}]), [[False]*3]*2)

    def test_ready_snapshot_is_returned_only_for_the_requested_condition(self):
        rows = predicates([observation_predicate('value.sample >= 3'), observation_predicate('value.sample > 3')], [READY])
        self.assertEqual(rows, [[READY], [False]])

    def test_failures_and_premature_restoration_are_not_timeouts_or_success(self):
        values = [{'status':'failed','error':'GPU error'}, {'status':'restored'}, {'status':'unexpected'}]
        self.assertEqual(predicates([observation_predicate('false')], values)[0], values)
        for value in values:
            with self.assertRaises(AssertionError):require_snapshot(value)

    def test_restoration_waits_for_a_concrete_terminal_state(self):
        failed, restored = {'status':'failed'}, {'status':'restored'}
        self.assertEqual(predicates([observation_predicate(restored=True)], [READY, failed, restored]), [[False, failed, restored]])
        self.assertEqual(require_snapshot(restored, 'restored'), restored)
        with self.assertRaises(AssertionError):require_snapshot(failed, 'restored')

    def test_incomplete_ready_observations_have_explicit_errors(self):
        for value in [None, {}, {'status':'ready'}, READY | {'sample':None}, READY | {'sample':True}, READY | {'sample':0}, READY | {'controls':None}]:
            with self.assertRaises(AssertionError):require_snapshot(value)

    def test_old_predicate_reproduces_null_startup_race(self):
        old = '() => globalThis.nurbsSceneUi?.status !== "ready" || false'
        self.assertEqual(predicates([old], ['__undefined__', None]), [[True, True]])
        self.assertEqual(predicates([observation_predicate()], ['__undefined__', None]), [[False, False]])


class SolutionPredicateContracts(unittest.TestCase):
    def solved(self, revision=20, **values):
        return READY | {'state': {'graphRevision': revision}, 'values': {'solvedRevision': revision, 'editorPending': False, **values}}

    def test_unloaded_dialog_is_not_a_committed_edit(self):
        pending=self.solved(editorOpen=False,editorPending=True)
        closed=self.solved(editorOpen=False)
        expression=observation_predicate(solution_condition(editor_closed=True))
        self.assertEqual(predicates([expression],[pending,closed]),[[False,closed]])

    def test_committed_graph_with_older_solution_remains_pending(self):
        old=self.solved(solvedRevision=19)
        missing=self.solved(solvedRevision=None)
        current=self.solved(items=['wrong completed value'])
        expression=observation_predicate(solution_condition())
        self.assertEqual(predicates([expression],[old,missing,current]),[[False,False,current]])
        # A completed wrong output must reach the original assertion, not be
        # hidden by waiting until a desired text/value appears.

    def test_apply_requires_an_actual_revision_transition(self):
        before=self.solved(19)
        after=self.solved(20)
        expression=observation_predicate(solution_condition(editor_closed=True,after_revision=19))
        self.assertEqual(predicates([expression],[before,after]),[[False,after]])

    def test_missing_values_and_invalid_revision_constraints_fail_closed(self):
        self.assertEqual(predicates([observation_predicate(solution_condition())],[READY]),[[False]])
        for revision in (True,-1,2**53,'20'):
            with self.subTest(revision=revision),self.assertRaises(ValueError):solution_condition(after_revision=revision)
        with self.assertRaises(ValueError):solution_condition(editor_closed='false')


class Handle:
    def __init__(self, value):self.value=value;self.disposed=False
    async def json_value(self):return self.value
    async def dispose(self):self.disposed=True


class FakePage:
    def __init__(self, values):self.values=iter(values);self.handles=[];self.calls=[]
    async def wait_for_function(self, predicate, *, timeout):
        self.calls.append((predicate,timeout));handle=Handle(next(self.values));self.handles.append(handle);return handle
    async def evaluate(self, expression):raise AssertionError('Must not re-read after the satisfied snapshot')


class AdapterContracts(unittest.IsolatedAsyncioTestCase):
    async def test_wait_uses_snapshot_handle_without_racing_a_second_read(self):
        page=FakePage([READY]);self.assertEqual(await wait_for_ui(page),READY)
        self.assertTrue(page.handles[0].disposed);self.assertEqual(page.calls[0][1],20000)

    async def test_failed_wait_disposes_handle_and_does_not_retry(self):
        page=FakePage([{'status':'failed','error':'Render failed'}])
        with self.assertRaisesRegex(AssertionError,'Render failed'):await wait_for_ui(page)
        self.assertTrue(page.handles[0].disposed);self.assertEqual(len(page.calls),1)

    async def test_settle_requires_two_fresh_samples(self):
        page=FakePage([READY,READY|{'sample':5}]);result=await settle(page)
        self.assertEqual(result['sample'],5);self.assertIn('sample >= 5',page.calls[1][0])
        self.assertTrue(all(h.disposed for h in page.handles))

    async def test_restore_accepts_only_restored_and_preserves_deadline(self):
        page=FakePage([{'status':'restored'}]);self.assertEqual(await wait_for_restored(page),{'status':'restored'})
        self.assertEqual(page.calls[0][1],15000)
        with self.assertRaises(AssertionError):await wait_for_restored(FakePage([{'status':'ready'}]))


    async def test_solved_wait_returns_the_exact_handle_with_the_original_deadline(self):
        snapshot=READY|{'state':{'graphRevision':20},'values':{'solvedRevision':20,'editorPending':False}}
        page=FakePage([snapshot])
        self.assertIs(await wait_for_solved_ui(page,editor_closed=True,after_revision=19),snapshot)
        self.assertEqual(len(page.calls),1);self.assertEqual(page.calls[0][1],20000)
        self.assertIn('editorPending === false',page.calls[0][0]);self.assertIn('graphRevision > 19',page.calls[0][0])
        self.assertTrue(page.handles[0].disposed)

    async def test_solved_wait_propagates_native_failure_without_retry(self):
        page=FakePage([{'status':'failed','error':'Native solve failed'}])
        with self.assertRaisesRegex(AssertionError,'Native solve failed'):await wait_for_solved_ui(page,editor_closed=True)
        self.assertEqual(len(page.calls),1);self.assertTrue(page.handles[0].disposed)

    async def test_timeout_propagates_without_retry(self):
        class TimedOut:
            def __init__(self):self.calls=0
            async def wait_for_function(self,*args,**kwargs):self.calls+=1;raise TimeoutError('Deadline')
        page=TimedOut()
        with self.assertRaisesRegex(TimeoutError,'Deadline'):await wait_for_ui(page)
        self.assertEqual(page.calls,1)

if __name__ == '__main__': unittest.main(verbosity=2)
