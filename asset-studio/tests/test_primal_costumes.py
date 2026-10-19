"""Attachment, fidelity and rig checks for the approved primitive costume directions."""
import copy
import json
from pathlib import Path
import sys
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from core import kit,hinges,export
from core.rig import Rig
from items import solid
from prefabs.anatomy import bodies
from prefabs.creatures import characters
from prefabs.outfits import primal_costumes


class PrimalCostumeTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.parts={}
        for style,body in (("barbarian_primitive","hero"),("orc_king","brute")):
            hosts,_=bodies.build({"torso":body})
            cls.parts[style]=getattr(primal_costumes,style)(hosts["torso"])

    def test_closed_geometry_and_budget(self):
        for style,parts in self.parts.items():
            self.assertLess(sum(len(p.faces) for p in parts.values()),25000,style)
            for name,piece in parts.items():
                with self.subTest(style=style,part=name):
                    report=solid.inspect(piece.verts,piece.faces)
                    self.assertTrue(np.isfinite(piece.verts).all())
                    for key in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                        self.assertEqual(report[key],0,key)
                    self.assertGreater(report["signed_volume"],0)

    def test_bare_chest_harness_and_fur_are_supported_structures(self):
        parts=self.parts["barbarian_primitive"]
        self.assertNotIn("CostumeChest",parts)
        self.assertEqual([name for name in parts if "Pauldron" in name],["CostumePauldronLeft"])
        for name in ("CostumeHarness","CostumeHideSkirt","CostumePauldronLeft","CostumeBelt"):
            self.assertEqual(solid.inspect(parts[name].verts,parts[name].faces)["components"],1,name)
        cap=parts["CostumePauldronLeft"]
        self.assertTrue({"fur","leather","leather_dark","stitch"}<=set(cap.slots))

    def test_orc_horns_buckle_and_fur_have_actual_connections(self):
        p=self.parts["orc_king"]
        for name in ("CostumeBelt","CostumeFurCollar","CostumePauldronLeft","CostumePauldronRight"):
            self.assertEqual(solid.inspect(p[name].verts,p[name].faces)["components"],1,name)
        self.assertIn("bone",set(p["CostumePauldronLeft"].slots))
        self.assertNotIn("bone",set(p["CostumePauldronRight"].slots))
        torso=kit.Piece.merge(*(p[name] for name in ("CostumeChest","CostumeBack","CostumeFurCollar",
                                                 "CostumeSideStrapsLeft","CostumeSideStrapsRight")))
        joined=solid.fuse_closed(torso)
        self.assertEqual(solid.inspect(joined.verts,joined.faces)["components"],1)

    def test_shoulder_bearings_stay_connected_and_rigid_through_motion(self):
        rig=Rig(export.TEMPLATES["knight"])
        for style,parts in self.parts.items():
            specs=hinges.specifications(parts)
            self.assertEqual(len(specs),1 if style=="barbarian_primitive" else 2)
            for clip in ("Walking_A","Running_A","1H_Melee_Attack_Chop"):
                # Cover the source clip, including its endpoints and raised-arm extremes.
                duration=float(max(np.asarray(rig.acc(s["input"])).ravel()[-1] for s in rig.clips[clip]["samplers"]))
                for time in np.linspace(0,duration,7):
                    deltas=hinges.pose_deltas(rig.deltas(clip,float(time)),specs)
                    for side in ("Left","Right"):
                        name="CostumePauldron"+side
                        if name not in parts:continue
                        cap,mount=parts[name],parts["CostumeShoulderSupport"+side]
                        spec=specs[cap.bone];pivot=np.r_[spec["pivot"],1]
                        np.testing.assert_allclose(deltas[cap.bone]@pivot,deltas["chest"]@pivot,atol=1e-6)
                        posed=cap.verts@deltas[cap.bone][:3,:3].T+deltas[cap.bone][:3,3]
                        self.assertAlmostEqual(np.linalg.norm(posed[0]-posed[-1]),np.linalg.norm(cap.verts[0]-cap.verts[-1]),places=6)
                        # Shared bearing centers are invariant; the same closed round bearing is in each part.
                        self.assertEqual(mount.bone,"chest")

    def test_collapsed_source_bones_keep_hinges_finite_and_follow_the_hidden_parent(self):
        parts=self.parts["orc_king"]
        specs=hinges.specifications(parts)
        source=Rig(export.TEMPLATES["skeleton"])
        deltas=source.deltas("Walking_A",.1)
        for name in ("chest","upperarm.l","upperarm.r"):
            deltas[name]=deltas[name].copy()
            deltas[name][:3,:3]=0
        posed=hinges.pose_deltas(deltas,specs)
        for name in specs:
            self.assertTrue(np.isfinite(posed[name]).all())
            np.testing.assert_allclose(posed[name],deltas["chest"])

    def test_orc_wearable_exports_all_source_skeleton_clips_including_hidden_poses(self):
        import tempfile
        from core import mesh
        studio=Path(__file__).resolve().parents[1]
        spec=json.loads((studio/"costumes/orc_king/costume.json").read_text())
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"orc_king.glb"
            export.export(str(path),"OrcKing",self.parts["orc_king"],spec["colors"],template="skeleton")
            gltf,_,_=mesh.read_glb(str(path))
            source=Rig(export.TEMPLATES["skeleton"])
            self.assertEqual({a["name"] for a in gltf["animations"]},set(source.clips))
            self.assertTrue({"shoulder_cap.l","shoulder_cap.r"}<={n.get("name") for n in gltf["nodes"]})

    def test_character_review_exports_only_to_external_temporary_storage(self):
        import fittings
        from unittest.mock import Mock,patch
        character=Mock(key="orc/orc_king",title="Orc King")
        character.build.return_value=({}, {"Body":kit.bevel_box((.1,.1,.1),.01,"armor")}, {"armor":(80,80,80)})
        with patch("studio.Character",return_value=character), patch.object(fittings.export,"export",side_effect=ValueError("stop before render")) as write:
            with self.assertRaisesRegex(ValueError,"stop before render"):
                fittings.character_review("costume_orc_king",empty_hands=True)
            destination=Path(write.call_args.args[0]).resolve()
            self.assertFalse(destination.is_relative_to(fittings.STUDIO.parent))
            self.assertTrue(destination.is_relative_to(fittings.work_root(fittings.STUDIO)))

    def test_existing_blue_barbarian_keeps_its_garment_geometry(self):
        studio=Path(__file__).resolve().parents[1]
        spec=json.loads((studio/"costumes/barbarian/costume.json").read_text())
        recipe=dict(template="knight",head={"form":{}},body=spec["body"],colors=spec["colors"],hide_covered=True)
        baseline,_=characters.build({**copy.deepcopy(recipe),"outfit":spec["parts"]})
        coordinated,_=characters.build({**copy.deepcopy(recipe),"outfit":spec["outfit"]})
        for name in ("Body","ArmLeft","ArmRight","LegLeft","LegRight"):
            np.testing.assert_array_equal(coordinated[name].verts,baseline[name].verts)
            np.testing.assert_array_equal(coordinated[name].faces,baseline[name].faces)
        for i,entry in enumerate(spec["parts"]):
            old=baseline[entry["kind"].capitalize()+str(i)]
            new=coordinated[entry["kind"].capitalize()+str(i)+"0"]
            np.testing.assert_array_equal(new.verts,old.verts)
            np.testing.assert_array_equal(new.faces,old.faces)
            np.testing.assert_array_equal(new.slots,old.slots)
            from core.rig import transfer_weights
            before=transfer_weights(old.verts,entry["kind"].capitalize()+str(i),faces=old.faces)
            after=transfer_weights(new.verts,entry["kind"].capitalize()+str(i)+"0",faces=new.faces)
            np.testing.assert_array_equal(after[0],before[0])
            np.testing.assert_array_equal(after[1],before[1])

    def test_nested_costume_sets_are_rejected_by_the_recipe_contract(self):
        from unittest.mock import patch
        recursive={"parts":[{"kind":"costume_set","name":"barbarian"}]}
        recipe=dict(template="knight",head={"form":{}},body={"torso":"standard"},
                    outfit=[{"kind":"costume_set","name":"barbarian"}])
        with patch("pathlib.Path.read_text",return_value=json.dumps(recursive)):
            with self.assertRaisesRegex(ValueError,"Nested costume sets"):
                characters.build(recipe)
