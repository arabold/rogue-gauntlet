"""Helmets fit tightly to `heads.Head`, keep manifold boolean cutouts intact, and suppress covered head features."""
from pathlib import Path
import sys
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import kit
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
        cls.helms = {name: fn(cls.head) for name, fn in helmets.STYLES.items()}  # each style once, at its defaults

    def test_all_styles_build_finite_valid_geometry(self):
        for name, piece in self.helms.items():
            with self.subTest(style=name):
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
        # Close-fitting procedural helms stay compact around the human cranium (2*A = 0.94).
        for name, max_width in (("crusader", 1.18), ("coif", 1.06)):
            with self.subTest(style=name):
                piece = self.helms[name]
                width = float(piece.verts[:, 0].max() - piece.verts[:, 0].min())
                self.assertLessEqual(width, max_width)

    def test_kaykit_originals_cover_the_cranium_of_every_head(self):
        # Refitted from KayKit's head to a human's and an orc's (narrower, taller, flatter at the back), no original
        # lets the cranium poke through where it covers it. The underside of the jaw, which the neck hides, is exempt.
        orc = dict(self.plain_recipe, form=dict(width=0.86, height=1.08, depth=1.0, back=0.92))
        for label, spec in (("human", self.plain_recipe), ("orc", orc)):
            head, _ = heads.build(spec)
            _, cranium = heads.build(spec, suppress=("ears", "nose", "eyes", "brows", "mouth", "hair", "beard"))
            points = cranium.verts[cranium.verts[:, 1] >= head.y(-0.5)]
            offset = points - head.centre
            reach = np.linalg.norm(offset, axis=1)
            direction = offset / reach[:, None]
            for name in (name for name, style in helmets.STYLES.items() if hasattr(style, "prefab")):
                with self.subTest(head=label, style=name):
                    helm = helmets.STYLES[name](head)
                    covered = np.nan_to_num(kit.ray_distance(head.centre, direction, helm.verts, helm.faces),
                                            nan=np.inf) < reach - 0.004
                    outside = np.isnan(kit.ray_distance(points + 1e-4 * direction, direction, helm.verts, helm.faces))
                    self.assertEqual(int((covered & outside).sum()), 0)

    def test_solid_slit_helms_have_recessed_dark_faces(self):
        for name in ("crusader", "dark_knight"):
            with self.subTest(style=name):
                piece = self.helms[name]
                self.assertIn("dark", set(piece.slots))
                # No open boundary edges in solid-cut helm hulls when built standalone via solid checks.
                stats = solid.inspect(piece.verts, piece.faces)
                self.assertEqual(stats["boundary_edges"], 0)
                self.assertEqual(stats["nonmanifold_edges"], 0)

    def test_variants_and_recipe_compat(self):
        compat_calls = [helmets.headdress(self.head, tilt=-8.0), helmets.coif(self.head, nasal=True)]
        for piece in compat_calls:
            self.assertGreater(len(piece.faces), 0)

    def test_kaykit_originals_recolor_regions_and_reject_detail_they_lack(self):
        default = self.helms["wizard"]
        recolored = helmets.STYLES["wizard"](self.head, slot="robe", band="trim")
        self.assertIn("hat", set(default.slots))
        self.assertEqual(set(recolored.slots), {"robe", "trim", "metal"})
        np.testing.assert_allclose(recolored.verts, default.verts)
        with self.assertRaises(TypeError):
            helmets.STYLES["wizard"](self.head, runes="glow")

    def test_headwear_trims_hair_to_the_locks_showing_outside_it(self):
        # A haired head: an open headdress keeps its hair; a kettle helm keeps the locks below its rim, none on the dome.
        spec = heads.ROSTER["necromancer"]
        head, full = heads.build(spec)
        _, bald = heads.build(spec, suppress=("hair",))
        hair = len(full.faces) - len(bald.faces)
        dress = helmets.headdress(head)
        _, under_dress = heads.build(spec, suppress=dress.hides, cover=dress)
        self.assertGreater(len(under_dress.faces) - len(bald.faces), 0.9 * hair)
        kettle = helmets.kettle(head)
        _, under_kettle = heads.build(spec, suppress=kettle.hides, cover=kettle)
        self.assertGreater(len(under_kettle.faces), len(bald.faces))
        top = lambda piece: piece.verts[piece.faces][..., 1].max()  # noqa: E731
        self.assertLess(top(under_kettle), top(full))

    def test_character_build_suppresses_hidden_head_features(self):
        barbarian = characters.load("heroes/barbarian_base")
        _, full_pieces = heads.build(characters.head_recipe(barbarian["head"]))
        full_verts = len(full_pieces.verts)
        # Every style sits over sculpted hair. This head has none, so its worn head depends only on the hide set:
        # one character per distinct set covers every style.
        by_hides = {}
        for style, helm in self.helms.items():
            self.assertIn("hair", helm.hides)
            by_hides.setdefault(frozenset(helm.hides), style)
        for hides, style in by_hides.items():
            with self.subTest(style=style):
                pieces, _ = characters.build({**barbarian, "outfit": [{"kind": "helmet", "style": style}]})
                head_piece = pieces["Head"]
                # Cheek-guarded and enclosed helms shrink the head by dropping ears or beard.
                if hides & {"ears", "beard"}:
                    self.assertLess(len(head_piece.verts), full_verts)
                # Sculpted beard hangs far below the cranium (y ~ 0.87 vs cranium chin ~ 1.22); full helms remove it.
                if "beard" in hides:
                    self.assertGreater(head_piece.verts[:, 1].min(), 1.15)
                else:
                    self.assertLess(head_piece.verts[:, 1].min(), 1.00)


if __name__ == "__main__":
    unittest.main()
