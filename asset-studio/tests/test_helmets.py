"""Helmets fit tightly to `heads.Head`, keep manifold boolean cutouts intact, and suppress covered head features."""
from pathlib import Path
import sys
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from items import solid
from prefabs.anatomy import heads
from prefabs.creatures import characters
from prefabs.outfits import helmets


class HelmetsTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plain_recipe = dict(
            form=heads.FORMS["human"],
            eyes=dict(),
            brows=dict(),
            nose=dict(style="button"),
            mouth=dict(curve=0.15),
            ears=dict(style="round"),
        )
        cls.head, cls.head_piece = heads.build(cls.plain_recipe)

    def test_all_styles_build_finite_valid_geometry(self):
        expected = {
            "bear", "horned", "headdress", "wizard",
            "knight", "crusader", "dark_knight", "kettle", "coif", "hood",
        }
        self.assertEqual(set(helmets.STYLES), expected)
        for name, fn in helmets.STYLES.items():
            with self.subTest(style=name):
                piece = fn(self.head)
                self.assertGreater(len(piece.verts), 0)
                self.assertGreater(len(piece.faces), 0)
                self.assertTrue(np.isfinite(piece.verts).all())
                self.assertEqual(len(piece.slots), len(piece.faces))
                self.assertEqual(int(piece.faces.min()), 0)
                self.assertLess(int(piece.faces.max()), len(piece.verts))
                if getattr(piece, "normals", None) is not None:
                    self.assertTrue(np.isfinite(piece.normals).all())
                    self.assertEqual(piece.normals.shape, (len(piece.faces), 3, 3))

    def test_snug_lateral_width_against_cranium(self):
        # Close-fitting caps/helms stay compact around the human cranium (2*A = 0.94); only horns/feathers/brims flare.
        for name, max_width in (("bear", 1.15), ("knight", 1.14), ("crusader", 1.18), ("coif", 1.06), ("hood", 1.10)):
            with self.subTest(style=name):
                piece = helmets.STYLES[name](self.head)
                width = float(piece.verts[:, 0].max() - piece.verts[:, 0].min())
                self.assertLessEqual(width, max_width)
        # Horned without horns sits cleanly on the brow above ears without bloating the head width.
        plain_horned = helmets.horned(self.head, horns=0)
        horned_width = float(plain_horned.verts[:, 0].max() - plain_horned.verts[:, 0].min())
        self.assertLessEqual(horned_width, 1.10)

    def test_wizard_encloses_cranium_without_protrusion(self):
        # Untilt for exact axis-aligned containment above the brim line (y >= 1.88).
        wiz = helmets.wizard(self.head, tilt=0.0)
        head_pts = self.head_piece.verts[self.head_piece.verts[:, 1] >= self.head.y(0.32)]
        self.assertGreater(len(head_pts), 0)
        for y in np.unique(np.round(head_pts[:, 1], 3)):
            sub_h = head_pts[np.abs(head_pts[:, 1] - y) <= 0.025]
            hat_band = wiz.verts[np.abs(wiz.verts[:, 1] - y) <= 0.045]
            self.assertGreater(len(hat_band), 0)
            r_head = np.hypot(sub_h[:, 0], sub_h[:, 2]).max()
            r_hat = np.hypot(hat_band[:, 0], hat_band[:, 2]).max()
            self.assertGreater(r_hat, r_head)

    def test_solid_slit_helms_have_recessed_dark_faces(self):
        for name in ("crusader", "dark_knight"):
            with self.subTest(style=name):
                piece = helmets.STYLES[name](self.head)
                self.assertIn("dark", set(piece.slots))
                # No open boundary edges in solid-cut helm hulls when built standalone via solid checks.
                stats = solid.inspect(piece.verts, piece.faces)
                self.assertEqual(stats["boundary_edges"], 0)
                self.assertEqual(stats["nonmanifold_edges"], 0)

    def test_variants_and_recipe_compat(self):
        compat_calls = [
            helmets.horned(self.head, horns=0, brim=0.26, tilt=-10.0, y_offset=0.05,
                           patina=True, dents=True, guard=0.6, guard_upright=True),
            helmets.headdress(self.head, tilt=-8.0),
            helmets.knight(self.head, bevor=True),
            helmets.coif(self.head, nasal=True),
        ]
        for piece in compat_calls:
            self.assertGreater(len(piece.faces), 0)
        self.assertIn("beard", compat_calls[2].hides)
        self.assertNotIn("beard", helmets.knight(self.head, bevor=False).hides)

    def test_character_build_suppresses_hidden_head_features(self):
        hero_base = characters.load("heroes/barbarian_base")
        _, full_pieces = heads.build(characters.head_recipe(hero_base["head"]))
        full_verts = len(full_pieces.verts)
        for style in helmets.STYLES:
            with self.subTest(style=style):
                recipe = {
                    **hero_base,
                    "outfit": [{"kind": "helmet", "style": style}],
                }
                pieces, _ = characters.build(recipe)
                helm = pieces["Helmet0"]
                head_piece = pieces["Head"]
                # Every style hides at least sculpted hair; cheek/enclosed helms shrink Head vertex count by dropping ears/beard.
                self.assertIn("hair", helm.hides)
                if "ears" in helm.hides or "beard" in helm.hides:
                    self.assertLess(len(head_piece.verts), full_verts)
                # Sculpted beard hangs far below the cranium (y ~ 0.87 vs cranium chin ~ 1.22); full helms remove it.
                if style in ("crusader", "dark_knight"):
                    self.assertGreater(head_piece.verts[:, 1].min(), 1.15)
                else:
                    self.assertLess(head_piece.verts[:, 1].min(), 1.00)


if __name__ == "__main__":
    unittest.main()
