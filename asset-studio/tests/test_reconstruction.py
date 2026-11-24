"""Concept reconstruction inputs: turnaround splitting and reconstruction.json checks (no Blender, no paid calls)."""
import copy
import json
from pathlib import Path
import sys
import unittest

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import reconstruction

STUDIO = Path(__file__).resolve().parents[1]


class SplitSheetTest(unittest.TestCase):
    def test_figures_come_out_in_view_order_at_one_height(self):
        # Four figures of different heights; the bottom-left one reaches above the sheet's midline.
        sheet = Image.new("RGB", (800, 800), "white")
        draw = ImageDraw.Draw(sheet)
        figures = {"front": (100, 60, 220, 360, (200, 0, 0)), "left": (520, 80, 600, 340, (0, 200, 0)),
                   "back": (110, 392, 210, 700, (0, 0, 200)), "right": (540, 440, 610, 690, (200, 200, 0))}
        for x0, y0, x1, y1, color in figures.values():
            draw.rectangle([x0, y0, x1, y1], fill=color)
        views = reconstruction.split_sheet(sheet, size=256, height=200)
        self.assertEqual(list(views), list(reconstruction.VIEWS))
        for name, (*_, color) in figures.items():
            a = np.asarray(views[name])
            ys, _ = np.nonzero((a < reconstruction.BACKGROUND).any(axis=2))
            self.assertAlmostEqual(ys.max() - ys.min() + 1, 200, delta=2)
            self.assertTrue(np.all(np.abs(a[128, 128].astype(int) - color) < 30), name)


class ConfigCheckTest(unittest.TestCase):
    def setUp(self):
        self.config = json.loads((STUDIO / "characters/nymph/nymph/reconstruction.json").read_text())

    def test_the_authored_configs_are_complete(self):
        for key in ("nymph/nymph", "slime/acid_blob", "bat/giant_bat", "mimic/chest_mimic", "snake/snake"):
            config = json.loads((STUDIO / "characters" / key / "reconstruction.json").read_text())
            self.assertEqual(reconstruction.check_config(config), [], key)

    def test_gaps_are_named_before_blender_runs(self):
        broken = copy.deepcopy(self.config)
        del broken["budgets"]["Hair"]
        broken["slots"]["Head"].append("freckles")
        broken["parts"]["Clasp"].append(0)
        problems = " | ".join(reconstruction.check_config(broken))
        self.assertIn("budgets", problems)
        self.assertIn("freckles", problems)
        self.assertIn("two parts", problems)


if __name__ == "__main__":
    unittest.main()
