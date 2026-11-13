"""Panel draw completion contracts; these do not replace native UI acceptance."""
import unittest
from ui_observation import panel_render_condition, observation_predicate, wait_for_ui
from test_observation_contracts import READY, predicates, FakePage


def snapshot(*, current=True, revision=22, sample=203, **values):
    return READY | {'sample': sample, 'state': {'graphRevision': revision},
        'values': {'solvedRevision': revision, 'editorPending': False,
                   'panels': [{'id': 'panel', 'wrapItems': False,
                               'drawnTruncated': False, 'drawnLines': ['old wrapped row']}],
                   **values},
        'typography': {'panels': [{'id': 'panel', 'current': current}]}}


class PanelRenderContracts(unittest.TestCase):
    def test_solved_but_unpainted_display_edit_remains_pending(self):
        # Main run 43415245539 observed samples 203 and 204 with solved revision
        # 22 and new options, but the renderer still reported current=false.
        values = [snapshot(current=False), snapshot(current=False, sample=204), snapshot(sample=205)]
        p = observation_predicate(panel_render_condition('panel', after_revision=21))
        self.assertEqual(predicates([p], values), [[False, False, values[-1]]])

    def test_completed_wrong_drawing_reaches_the_original_assertion(self):
        wrong = snapshot()  # Still lacks the requested ellipsis: do not mask it.
        p = observation_predicate(panel_render_condition('panel', after_revision=21))
        self.assertEqual(predicates([p], [wrong]), [[wrong]])

    def test_in_place_edit_requires_a_revision_transition(self):
        before, after = snapshot(revision=21), snapshot()
        p = observation_predicate(panel_render_condition('panel', after_revision=21))
        self.assertEqual(predicates([p], [before, after]), [[False, after]])

    def test_replacement_graph_does_not_require_monotonic_revision(self):
        old_paint, restored = snapshot(revision=14, current=False), snapshot(revision=14)
        p = observation_predicate(panel_render_condition('panel'))
        self.assertEqual(predicates([p], [old_paint, restored]), [[False, restored]])

    def test_dialog_continuation_and_solution_must_complete_before_paint(self):
        values = [snapshot(editorPending=True), snapshot(solvedRevision=21), snapshot()]
        p = observation_predicate(panel_render_condition('panel'))
        self.assertEqual(predicates([p], values), [[False, False, values[-1]]])

    def test_missing_or_other_panel_cannot_satisfy_completion(self):
        missing = snapshot() | {'typography': None}
        other = snapshot() | {'typography': {'panels': [{'id': 'other', 'current': True}]}}
        p = observation_predicate(panel_render_condition('panel'))
        self.assertEqual(predicates([p], [missing, other, snapshot(current=1)]), [[False]*3])

    def test_identity_is_quoted_and_revision_constraint_is_validated(self):
        p = observation_predicate(panel_render_condition('panel"||true||"'))
        self.assertEqual(predicates([p], [snapshot()]), [[False]])
        for identity in ('', None, True):
            with self.assertRaises(ValueError): panel_render_condition(identity)
        for revision in (-1, True, 2**53, '21'):
            with self.assertRaises(ValueError): panel_render_condition('panel', after_revision=revision)


class PanelRenderAdapterContracts(unittest.IsolatedAsyncioTestCase):
    async def test_returns_exact_painted_snapshot_without_second_read(self):
        value = snapshot()
        page = FakePage([value])
        self.assertIs(await wait_for_ui(page, panel_render_condition('panel')), value)
        self.assertEqual(len(page.calls), 1)
        self.assertEqual(page.calls[0][1], 20000)
        self.assertTrue(page.handles[0].disposed)

    async def test_native_failure_is_propagated_without_retry(self):
        page = FakePage([{'status': 'failed', 'error': 'Native paint failed'}])
        with self.assertRaisesRegex(AssertionError, 'Native paint failed'):
            await wait_for_ui(page, panel_render_condition('panel'))
        self.assertEqual(len(page.calls), 1)
        self.assertTrue(page.handles[0].disposed)


if __name__ == '__main__': unittest.main(verbosity=2)
