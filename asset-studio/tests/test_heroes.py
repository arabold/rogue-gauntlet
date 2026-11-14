"""Class heroes are bare bodies the game dresses: one shared body and tunic, so armor fits every class, head
parts the game can hide under headgear, and the shoulder hinges separately exported armor binds to."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import mesh  # noqa: E402
from prefabs.creatures import characters  # noqa: E402

CLASSES = ("barbarian", "knight", "mage", "rogue")
SHARED = ("Body", "ArmLeft", "ArmRight", "LegLeft", "LegRight", "Hips")


class ClassHeroTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.heroes = {name: characters.build(characters.load(f"heroes/{name}"))[0] for name in CLASSES}

    def test_every_class_shares_the_body_and_tunic(self):
        reference = self.heroes[CLASSES[0]]
        under = [k for k in reference if k.rstrip("0123456789") == "Tunic"]
        self.assertEqual(len(under), 1)  # KayKit's legs bring their own pants and boots
        # KayKit's hips close the body from below, so no skin shows under the tunic; the limbs keep KayKit's shading
        body = reference["Body"]
        self.assertGreater(body.verts[body.faces].mean(1)[:, 1].min(), reference["Hips"].verts[:, 1].max())
        for limb in ("ArmLeft", "LegLeft", "Hips"):
            self.assertIsNotNone(reference[limb].uv_t, limb)
        for name, pieces in self.heroes.items():
            with self.subTest(hero=name):
                for part in (*SHARED, *under):
                    np.testing.assert_allclose(pieces[part].verts, reference[part].verts, err_msg=part)

    def test_kaykit_heads_split_into_parts_without_losing_faces(self):
        from prefabs.anatomy import adventurer_parts

        for name, pieces in self.heroes.items():
            with self.subTest(hero=name):
                tris = adventurer_parts._stored(f"{name}_head")[1]["tris"]
                self.assertEqual(sum(len(pieces[k].faces) for k in ("Head", "Hair", "Ears") if k in pieces), tris)
                self.assertEqual("Hair" in pieces, "Scalp" in pieces)
        self.assertNotIn("Hair", self.heroes["barbarian"])  # bald: the beard stays part of the face

    def test_scalp_seals_the_hairline_under_the_hair(self):
        from prefabs.anatomy import adventurer_parts

        for name in ("knight", "mage", "rogue"):
            with self.subTest(hero=name):
                parts = adventurer_parts.parts(name)
                hair, scalp = parts["Hair"], parts["Scalp"]
                self.assertEqual(len(hair.faces), len(scalp.faces))
                # Where the hair meets the face, the scalp meets it at the same points: no gap opens at the hairline.
                points = lambda piece: {tuple(p) for p in np.round(piece.verts, 4)}  # noqa: E731
                hairline = points(hair) & points(parts["Head"])
                self.assertTrue(hairline)
                self.assertLessEqual(hairline, points(scalp))
                # Everywhere else it lies under the hair: never further from the head's centre.
                centre = (hair.verts.min(0) + hair.verts.max(0)) / 2
                self.assertLessEqual(np.linalg.norm(scalp.verts - centre, axis=1).max(),
                                     np.linalg.norm(hair.verts - centre, axis=1).max() + 1e-6)

    def test_suits_recolor_class_limbs_on_kaykit_shading(self):
        # A suit dresses a class's KayKit limbs in its own colors: the swatch's middle takes the new color and every
        # row keeps KayKit's brightness relative to it.
        from prefabs.anatomy import adventurer_parts

        look = adventurer_parts.limb_colors("knight", "legs")[1]["knight_03"]
        steel, bronze = np.asarray(look["gradient"], float), np.asarray([155, 145, 128], float)
        tinted = np.asarray(adventurer_parts.tinted(look, bronze)["gradient"], float)
        middle = len(steel) // 2
        np.testing.assert_allclose(tinted[middle], bronze, atol=1)
        luma = lambda rows: rows @ (0.299, 0.587, 0.114)  # noqa: E731
        np.testing.assert_allclose(luma(tinted) / luma(tinted)[middle], luma(steel) / luma(steel)[middle], atol=0.02)

    def test_committed_hinges_match_the_knight_costume_on_the_hero_body(self):
        self.assertEqual(characters.shared_hinges(), characters.hero_hinges())

    def test_export_carries_the_shoulder_hinges_and_few_nodes(self):
        import studio

        c = studio.Character("heroes/rogue")
        r, pieces, colors = c.build()
        fixed_hinges, groups = studio.hero_export(r, pieces)
        with tempfile.TemporaryDirectory() as folder:
            from core import export

            path = str(Path(folder) / "rogue.glb")
            export.export(path, c.title, pieces, colors, template=r["template"], fixed_hinges=fixed_hinges,
                          groups=groups)
            g, _, _ = mesh.read_glb(path)
        joints = {g["nodes"][j]["name"] for j in g["skins"][0]["joints"]}
        self.assertLessEqual({"shoulder_cap.l", "shoulder_cap.r", "head", "chest"}, joints)
        skinned = sorted(n["name"] for n in g["nodes"] if "skin" in n)
        self.assertEqual(skinned, ["Rogue_Arms", "Rogue_Body", "Rogue_Ears", "Rogue_Hair", "Rogue_Legs", "Rogue_Scalp"])
        clips = {a["name"]: a for a in g["animations"]}
        cap = next(i for i, n in enumerate(g["nodes"]) if n.get("name") == "shoulder_cap.l")
        self.assertTrue(any(ch["target"]["node"] == cap for ch in clips["Walking_A"]["channels"]))

    def test_body_armor_binds_to_any_class_hero(self):
        # A suit is exported apart, weighted from the hero body under it; the game moves it onto any class's skeleton,
        # so its joints must be the hero's (shoulder hinges included), in one node, without clips of its own. It wears
        # its class's KayKit limbs, which its display model leaves out.
        import fittings

        knight = fittings.suit(json.loads((Path(fittings.STUDIO) / "costumes/knight/costume.json").read_text()))
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "armor.glb"
            fittings.write_worn(path, "Armor", knight)
            g, _, _ = mesh.read_glb(str(path))
        joints = {g["nodes"][j]["name"] for j in g["skins"][0]["joints"]}
        self.assertLessEqual({"shoulder_cap.l", "shoulder_cap.r", "chest", "upperarm.l"}, joints)
        self.assertEqual([n["name"] for n in g["nodes"] if "mesh" in n], ["Armor_Armor"])
        self.assertFalse(g.get("animations"))
        self.assertLessEqual({"ArmLeft", "ArmRight", "LegLeft", "LegRight", "Hips"}, set(knight.worn) - set(knight.torso))
        self.assertEqual(set(knight.skin) & set(knight.torso), set())

if __name__ == "__main__":
    unittest.main()
