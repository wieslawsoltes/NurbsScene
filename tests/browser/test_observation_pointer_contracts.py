"""Coordinate-only contracts; not actual Uno pointer or GPU qualification."""
import math
import unittest
from ui_observation import text_editor_point

class TextEditorPointContracts(unittest.TestCase):
    def test_header_bearing_field_targets_editor_instead_of_midpoint(self):
        bounds={'x':805,'y':460,'width':170,'height':53}
        self.assertEqual(text_editor_point(bounds),(890,501))
        # This is the exact failed field's observed bounds. Its midpoint sat
        # at the header/editor seam; the target is 14.5 pixels further inside.
        self.assertGreater(text_editor_point(bounds)[1],bounds['y']+bounds['height']/2)
    def test_small_control_target_stays_strictly_inside(self):
        for height in (1,4,12,20,24,53,200):
            x,y=text_editor_point({'x':-10,'y':-20,'width':8,'height':height})
            self.assertEqual(x,-6);self.assertGreater(y,-20);self.assertLess(y,-20+height)
    def test_invalid_rectangles_are_rejected_without_dispatch(self):
        valid={'x':0,'y':0,'width':10,'height':10}
        for field in valid:
            for value in (None,True,'10',math.nan,math.inf,-math.inf):
                with self.assertRaises(ValueError):text_editor_point(valid|{field:value})
        for field in ('width','height'):
            for value in (0,-1):
                with self.assertRaises(ValueError):text_editor_point(valid|{field:value})
    def test_missing_bounds_are_rejected(self):
        for value in ({},{'x':0},None):
            with self.assertRaises(ValueError):text_editor_point(value)
