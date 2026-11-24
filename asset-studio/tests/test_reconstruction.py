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
        for key in ("nymph/nymph", "slime/acid_blob", "bat/giant_bat", "mimic/chest_mimic", "snake/snake",
                    "bosses/forsaken_warden", "orc/orc_spearman", "orc/orc_shaman", "orc/orc_captain"):
            config = json.loads((STUDIO / "characters" / key / "reconstruction.json").read_text())
            self.assertEqual(reconstruction.check_config(config), [], key)

    def test_gaps_are_named_before_blender_runs(self):
        broken = copy.deepcopy(self.config)
        del broken["budgets"]["Hair"]
        broken["slots"]["Head"].append("freckles")
        broken["parts"]["Clasp"].append(0)
        broken["props"] = {"Club": {"prefab": "club", "frame": "staff", "slots": {"wood": "oak"}}}
        problems = " | ".join(reconstruction.check_config(broken))
        self.assertIn("budgets", problems)
        self.assertIn("freckles", problems)
        self.assertIn("two parts", problems)
        for gap in ("unknown prefab club", "unknown frame staff", "oak, missing"):
            self.assertIn(gap, problems)


    def test_a_match_tone_snaps_faces_while_the_game_keeps_the_albedo(self):
        socket = {"base": [44, 38, 34], "match": [130, 118, 97]}
        self.assertEqual(reconstruction.base_color(socket), [44, 38, 34])
        self.assertEqual(reconstruction.match_color(socket), [130, 118, 97])
        self.assertEqual(reconstruction.match_color([10, 20, 30]), [10, 20, 30])
        broken = copy.deepcopy(self.config)
        broken["palette"]["skin"] = {"base": [150, 178, 166], "light": [200, 210, 200]}
        self.assertIn("palette skin", " | ".join(reconstruction.check_config(broken)))


class PropTest(unittest.TestCase):
    def test_props_mount_on_the_verified_hand_slot_frames(self):
        from core.rig import Rig
        from items import executioner
        config = {"props": {
            "Sword": {"prefab": "executioner_sword", "frame": "sword",
                      "slots": {s: "steel" for s in ("brass", "edge", "iron", "leather", "steel")}},
            "Shield": {"prefab": "warden_shield", "frame": "shield",
                       "slots": {s: "steel" for s in ("crimson", "gem", "gold", "iron", "leather")}}}}
        placed = reconstruction.props(config, "large")
        rest = Rig.load("large").rest
        # Independent canonical mounts: the blade turned about the slot's Y, the shield on the slot's own axes.
        expected = {"Sword": ("handslot.r", np.diag([-1., 1., -1.]), [0, .03330930517626057, 0], executioner.executioner_sword()),
                    "Shield": ("handslot.l", np.eye(3), [0, .01701178596925984, .15588533878326416], executioner.warden_shield())}
        for name, (bone, rotation, offset, piece) in expected.items():
            self.assertEqual(placed[name]["bone"], bone)
            world = np.asarray(placed[name]["verts"]) @ reconstruction.GLTF_TO_BLENDER  # back to glTF Y-up
            inverse = np.linalg.inv(rest[bone])
            np.testing.assert_allclose(world @ inverse[:3, :3].T + inverse[:3, 3], piece.verts @ rotation.T + offset, atol=1e-5)
            self.assertEqual(len(placed[name]["slots"]), len(placed[name]["faces"]))

    def test_executioner_has_a_broad_squared_blade_and_closed_fittings(self):
        from items import executioner, solid
        sword = executioner.executioner_sword()
        self.assertGreater(np.ptp(sword.verts[sword.verts[:, 1] > 1][:, 0]), .55)
        tip = sword.verts[np.isclose(sword.verts[:, 1], sword.verts[:, 1].max())]
        self.assertGreater(np.ptp(tip[:, 0]), .4)
        report = solid.inspect(sword.verts, sword.faces)
        for key in ("boundary_edges", "nonmanifold_edges", "inconsistent_edges", "duplicate_faces"):
            self.assertEqual(report[key], 0)


if __name__ == "__main__":
    unittest.main()
