"""Gorget-supported rigid shoulder caps must remain seated in both software and exported animation."""

from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from core import export,hinges,rig
from items import solid
from prefabs.anatomy import bodies
from prefabs.outfits import costumes,pauldrons


class ShoulderMountTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        hosts,_ = bodies.build({"torso":"standard"})
        cls.base = costumes.build(hosts["torso"],"knight")
        cls.parts = pauldrons.steel_assembly(hosts["torso"],list(cls.base.values()))
        cls.source = rig.Rig(export.TEMPLATES["knight"])

    def test_closed_caps_and_mounts_have_real_support(self):
        self.assertLess(sum(len(p.faces) for p in {**self.base,**self.parts}.values()),25000)
        guard = self.parts["CostumeGorget"]
        for name,piece in self.parts.items():
            report = solid.inspect(piece.verts,piece.faces)
            self.assertEqual(report["components"],1,name)
            for field in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                self.assertEqual(report[field],0,name)
        for side in ("Left","Right"):
            mount = self.parts["CostumeShoulderMount"+side]
            cap = self.parts["CostumePauldron"+side]
            solid.Assembly().add(guard).add(mount).build()
            solid.Assembly().add(mount).add(cap).build()
            self.assertEqual(mount.bone,guard.bone)

    def test_pivot_stays_on_gorget_and_caps_remain_rigid_in_motion(self):
        specs = hinges.specifications(self.parts)
        for clip in ("Walking_A","Running_A","1H_Melee_Attack_Chop"):
            animation = self.source.clips[clip]
            duration = max(float(self.source.acc(s["input"])[-1,0]) for s in animation["samplers"])
            for t in np.linspace(0,duration,17):
                D = hinges.pose_deltas(self.source.deltas(clip,float(t)),specs)
                for side in ("Left","Right"):
                    cap = self.parts["CostumePauldron"+side]
                    spec = cap.hinge
                    point = np.r_[spec["pivot"],1.]
                    np.testing.assert_allclose(D[cap.bone]@point,D[spec["parent"]]@point,atol=1e-7)
                    moved = cap.verts@D[cap.bone][:3,:3].T+D[cap.bone][:3,3]
                    np.testing.assert_allclose(np.linalg.norm(moved-moved[0],axis=1),
                                               np.linalg.norm(cap.verts-cap.verts[0],axis=1),atol=1e-6)

    def test_mounts_fit_female_and_broader_steel_costumes(self):
        for style,body,size in (("knight_plate","standard",.18),("maiden","female",.165),
                                ("dark_knight","hero",.19),("segmented","standard",.18)):
            hosts,_ = bodies.build({"torso":body})
            base = costumes.build(hosts["torso"],style)
            parts = pauldrons.steel_assembly(hosts["torso"],list(base.values()),size)
            self.assertLess(sum(len(p.faces) for p in {**base,**parts}.values()),25000)
            for side in ("Left","Right"):
                solid.Assembly().add(parts["CostumeGorget"]).add(parts["CostumeShoulderMount"+side]).build()
                solid.Assembly().add(parts["CostumePauldron"+side]).add(parts["CostumeShoulderMount"+side]).build()
            emblems = [p for p in base.values() if "gem" in set(p.slots)]
            if emblems:
                guard = parts["CostumeGorget"]
                front = guard.verts[(np.abs(guard.verts[:,0])<.035)&(guard.verts[:,2]>0)]
                self.assertGreater(front[:,1].min(),max(p.verts[:,1].max() for p in emblems)+.004)

    def test_exported_hinges_match_the_authored_motion(self):
        from prefabs.creatures import characters
        import fittings
        colors = characters.build(fittings.recipe(fittings.studies()["costume_knight"]))[1]
        with tempfile.TemporaryDirectory() as directory:
            path = str(Path(directory)/"shoulders.glb")
            export.export(path,"shoulders",{**self.base,**self.parts},colors,template="knight")
            exported = rig.Rig(path)
            self.assertEqual(len(exported.g["skins"][0]["joints"]),43)
            self.assertEqual(set(exported.clips),set(self.source.clips))
            specs = hinges.specifications(self.parts)
            for clip in ("Walking_A","Running_A","1H_Melee_Attack_Chop"):
                for t in (0.,.125,.37,.6):
                    expected = hinges.pose_deltas(self.source.deltas(clip,t),specs)
                    actual = exported.deltas(clip,t)
                    for name,spec in specs.items():
                        np.testing.assert_allclose(actual[name],expected[name],atol=.002)
                        point = np.r_[spec["pivot"],1.]
                        np.testing.assert_allclose(actual[name]@point,actual[spec["parent"]]@point,atol=2e-6)

    def test_maiden_without_gorget_keeps_supported_pauldrons_and_banded_back(self):
        import fittings
        hosts,_ = bodies.build({"torso":"female"})
        base = costumes.build(hosts["torso"],"maiden")
        parts = pauldrons.steel_assembly(hosts["torso"],list(base.values()),.165,gorget=False,leather_straps=True)
        self.assertNotIn("CostumeGorget",parts)
        self.assertTrue({"armor_lame","armor_joint"}<=set(base["CostumeBack"].slots))
        self.assertEqual(base["CostumeBack"].bone,"chest")
        for side in ("Left","Right"):
            mount = parts["CostumeShoulderMount"+side]
            cap = parts["CostumePauldron"+side]
            solid.Assembly().add(mount).add(solid.fuse_closed(base["CostumeChest"])).add(
                solid.fuse_closed(base["CostumeBack"])).build()
            solid.Assembly().add(mount).add(cap).build()
            report = solid.inspect(mount.verts,mount.faces)
            self.assertEqual(report["components"],1)
            self.assertEqual(report["boundary_edges"],0)
            self.assertEqual(mount.bone,"chest")
            self.assertEqual(set(mount.slots),{"leather"})
            from core import kit
            pivot = cap.hinge["pivot"]
            origin = np.array([[pivot[0],1.6,pivot[2]]])
            direction = np.array([[0.,-1.,0.]])
            surface = kit.Piece.merge(*base.values())
            skin_hit = kit.ray_distance(origin,direction,surface.verts,surface.faces,reach=.7)[0]
            strap_hit = kit.ray_distance(origin,direction,mount.verts,mount.faces,reach=.7)[0]
            self.assertGreater(skin_hit-strap_hit,.002)
            self.assertLess(skin_hit-strap_hit,.030)
        shoulder_recipe = next(g for g in fittings.studies()["costume_maiden"]["outfit"] if g["kind"]=="steel_shoulders")
        self.assertFalse(shoulder_recipe["gorget"])
        self.assertTrue(shoulder_recipe["leather_straps"])

    def test_crusader_cape_and_pauldrons_have_support_over_cloth(self):
        from prefabs.creatures import characters
        import fittings
        parts,_ = characters.build(fittings.recipe(fittings.studies()["costume_crusader"]))
        parts = {name:piece for name,piece in parts.items() if name.startswith("Costume")}
        self.assertLess(sum(len(p.faces) for p in parts.values()),25000)
        self.assertFalse(any("Gorget" in name for name in parts))
        cape = next(p for name,p in parts.items() if "Cape" in name)
        back = next(p for name,p in parts.items() if "BackTorso" in name)
        self.assertFalse(any("Tabard" in name for name in parts))
        self.assertFalse(hasattr(cape,"bone"))
        front=cape.verts[cape.verts[:,2]>.15]
        self.assertLess(front[:,1].min(),.45)
        solid.Assembly().add(cape).add(back).build()
        # The cape must cover the rear cross near its roots, rather than letting the ink poke through.
        from core import kit
        origins = np.array([[0.,y,-1.] for y in (.99,1.055,1.075)])
        directions = np.tile([0.,0.,1.],(len(origins),1))
        cape_hit = kit.ray_distance(origins,directions,cape.verts,cape.faces,reach=1.)
        cloth_hit = kit.ray_distance(origins,directions,back.verts,back.faces,reach=1.)
        self.assertTrue(np.isfinite(cape_hit).all())
        self.assertTrue(np.all(cloth_hit-cape_hit>.002))
        torso = [p for name,p in parts.items() if "Torso" in name or "Tabard" in name]
        for side in ("Left","Right"):
            mount = next(p for name,p in parts.items() if "ShoulderMount"+side in name)
            cap = next(p for name,p in parts.items() if "Pauldron"+side in name)
            self.assertEqual(set(mount.slots),{"leather"})
            solid.Assembly().add(mount).add(cap).build()
            # At least one actual cloth/mail support must meet each end arch.
            touching = 0
            for support in torso:
                try:
                    solid.Assembly().add(mount).add(support).build()
                    touching += 1
                except ValueError:
                    pass
            self.assertGreaterEqual(touching,2)
