"""Native scrolling completion predicates; these are not Uno acceptance tests."""
import unittest
from ui_observation import scroll_condition, observation_predicate
from test_observation_contracts import READY, predicates


def state(sequence=10, active=False, current=True, **values):
    return READY | {'state': {'graphRevision': 7},
        'values': {'editorPending': False, 'solvedRevision': 7,
                   'panelScrollSequence': sequence, 'panelScrollActive': active, **values},
        'typography': {'panels': [{'id': 'panel', 'current': current}]}}


class ScrollCompletionContracts(unittest.TestCase):
    def test_old_painted_snapshot_cannot_acknowledge_new_input(self):
        old, completed = state(), state(11)
        p = observation_predicate(scroll_condition('panel', after_sequence=10, active=False))
        self.assertEqual(predicates([p], [old, completed]), [[False, completed]])

    def test_pointer_capture_and_completed_paint_are_independent_requirements(self):
        values = [state(11, True), state(11, False, False), state(11)]
        p = observation_predicate(scroll_condition('panel', after_sequence=10, active=False))
        self.assertEqual(predicates([p], values), [[False, False, values[-1]]])

    def test_wrong_completed_offset_is_returned_for_original_value_assertion(self):
        wrong = state(11, offset=-999, text='incorrect')
        p = observation_predicate(scroll_condition('panel', after_sequence=10))
        self.assertEqual(predicates([p], [wrong]), [[wrong]])

    def test_pending_dialog_or_stale_solution_does_not_qualify_scroll_paint(self):
        p = observation_predicate(scroll_condition('panel'))
        self.assertEqual(predicates([p], [state(editorPending=True), state(solvedRevision=6)]), [[False, False]])

    def test_invalid_constraints_reject_and_panel_id_is_not_javascript(self):
        for seq in [-1, True, 2**53, '10']:
            with self.assertRaises(ValueError): scroll_condition('panel', after_sequence=seq)
        with self.assertRaises(ValueError): scroll_condition('', active=False)
        with self.assertRaises(ValueError): scroll_condition('panel', active=0)
        p = observation_predicate(scroll_condition('panel"||true||"'))
        self.assertEqual(predicates([p], [state()]), [[False]])
