"""Class heroes are bare bodies the game dresses: one shared body and underclothes, so armor fits every class, head
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
SHARED = ("Body", "ArmLeft", "ArmRight", "LegLeft", "LegRight")


class ClassHeroTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.heroes = {name: characters.build(characters.load(f"heroes/{name}"))[0] for name in CLASSES}

    def test_every_class_shares_the_body_and_underclothes(self):
        reference = self.heroes[CLASSES[0]]
        under = [k for k in reference if k.rstrip("0123456789") in ("Shorts", "Tunic", "Boots")]
        self.assertEqual(len(under), 3)
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

    def test_committed_hinges_match_the_knight_costume_on_the_hero_body(self):
        with open(characters.HERO_HINGES, encoding="utf-8") as f:
            self.assertEqual(json.load(f), characters.hero_hinges())

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
        self.assertEqual(skinned, ["Rogue_Arms", "Rogue_Body", "Rogue_Ears", "Rogue_Hair", "Rogue_Scalp"])
        clips = {a["name"]: a for a in g["animations"]}
        cap = next(i for i, n in enumerate(g["nodes"]) if n.get("name") == "shoulder_cap.l")
        self.assertTrue(any(ch["target"]["node"] == cap for ch in clips["Walking_A"]["channels"]))

    def test_chest_armor_binds_to_any_class_hero(self):
        # Armor is exported apart, weighted from the hero body under it; the game moves it onto any class's skeleton,
        # so its joints must be the hero's (shoulder hinges included), in one node, without clips of its own.
        import fittings
        from core import export

        pieces, colors, hero = fittings.hero_fitting(json.loads((Path(fittings.STUDIO) / "costumes/knight/costume.json").read_text()))
        armor = {name: piece for name, piece in pieces.items() if name not in hero}
        with open(characters.HERO_HINGES, encoding="utf-8") as f:
            hinges = json.load(f)
        with tempfile.TemporaryDirectory() as folder:
            path = str(Path(folder) / "armor.glb")
            export.export(path, "Armor", armor, colors, template="knight", fixed_hinges=hinges,
                          groups={name: "Armor" for name in armor}, skin={name: pieces[name] for name in hero},
                          props=False, clips=False)
            g, _, _ = mesh.read_glb(path)
        joints = {g["nodes"][j]["name"] for j in g["skins"][0]["joints"]}
        self.assertLessEqual({"shoulder_cap.l", "shoulder_cap.r", "chest", "upperarm.l"}, joints)
        self.assertEqual([n["name"] for n in g["nodes"] if "mesh" in n], ["Armor_Armor"])
        self.assertFalse(g.get("animations"))
        self.assertEqual(set(hero) & set(armor), set())


if __name__ == "__main__":
    unittest.main()
