"""Armor review uses valid live recipes and never mutates character authoring inputs."""

import copy
from pathlib import Path
import sys
import tempfile
import json
from unittest.mock import patch
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import fittings
from core import export, kit
from core import mesh, rig
from items import solid
from prefabs import outfits
from prefabs.anatomy import bodies
from prefabs.creatures import characters
from prefabs.outfits import armor, belts, costumes


class ArmorFittingTest(unittest.TestCase):
    def test_travel_costumes_have_closed_supported_parts_and_stay_within_budget(self):
        from prefabs.outfits.travel_costumes import _fused
        hosts,_ = bodies.build({"torso":"standard"})
        for style in ("ranger","scout","arcanist","adventurer"):
            parts = costumes.build(hosts["torso"],style)
            self.assertLess(sum(len(p.faces) for p in parts.values()),25000)
            for name,piece in parts.items():
                with self.subTest(style=style,part=name):
                    report = solid.inspect(piece.verts,piece.faces)
                    for field in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                        self.assertEqual(report[field],0)
                    self.assertGreater(report["signed_volume"],0)
            # These unions fail if a mantle root or collar is merely placed near its support.
            solid.Assembly().add(parts["CostumeTunic"]).add(parts["CostumeCape"]).build()
            self.assertNotIn("CostumeCollar",parts)
            self.assertEqual(parts["CostumeCape"].garment_type,"cape")
            self.assertEqual(solid.inspect(parts["CostumeCape"].verts,parts["CostumeCape"].faces)["components"],1)
            belt = _fused(parts["CostumeBelt"])
            solid.Assembly().add(belt).add(parts["CostumePouch"]).build()
            self.assertEqual(parts["CostumePouch"].bone,"hips")
            self.assertEqual(parts["CostumeCape"].bone,"chest")
            self.assertFalse(hasattr(parts["CostumeTunic"],"bone"))

    def test_gorget_is_a_reusable_closed_neck_guard_with_an_open_throat(self):
        from prefabs.outfits import neckwear
        hosts,_ = bodies.build({"torso":"standard"})
        piece = neckwear.gorget(hosts["torso"])
        report = solid.inspect(piece.verts,piece.faces)
        self.assertEqual(report["components"],1)
        self.assertEqual(report["boundary_edges"],0)
        self.assertEqual(report["nonmanifold_edges"],0)
        self.assertEqual(piece.bone,"chest")
        hit = kit.ray_distance(np.array([[0.,1.4,0.]]),np.array([[0.,-1.,0.]]),piece.verts,piece.faces,reach=.5)
        self.assertFalse(np.isfinite(hit[0]))
        self.assertIn("gorget",characters.GARMENTS)
        self.assertTrue({"armor","trim"}<=set(piece.slots))
        # Fitting must account for the breastplate's sculpt, not only the underlying skin.
        from prefabs.outfits.travel_costumes import _fused
        armor_parts = costumes.build(hosts["torso"],"knight")
        fitted = neckwear.gorget(hosts["torso"],pieces=[p for p in armor_parts.values()
            if getattr(p,"bone",None)=="chest"])
        self.assertGreater(fitted.verts[:,1].min(),1.0)
        united = solid.Assembly().add(fitted).add(_fused(armor_parts["CostumeChest"])).build()
        self.assertEqual(solid.inspect(united.verts,united.faces)["components"],1)

    def test_fitted_outline_assigns_rounding_boundary_heights(self):
        from prefabs.outfits.clothes import Fitted
        hosts,_ = bodies.build({"torso":"standard"})
        host = Fitted(hosts["torso"],[])
        heights = np.array([1.10505,1.10515,1.10605,1.10615])
        points = host.outline(heights,np.arange(4)*np.pi/2,.02)
        np.testing.assert_array_equal(points[:,1],heights)
        self.assertTrue(np.all(np.linalg.norm(points[:,[0,2]],axis=1)>.05))

    def test_neckwear_arches_above_the_shoulder_sleeves(self):
        from prefabs.outfits import neckwear
        hosts,_ = bodies.build({"torso":"standard"})
        shoulder = costumes._shoulder_bridge()
        below = np.array([[sign*x,.8,z] for sign in (-1,1) for x in (.18,.24,.30) for z in (-.1,0,.1)])
        up = np.tile([0.,1.,0.],(len(below),1))
        above = below.copy()
        above[:,1] = 1.6
        shoulder_top = 1.6-kit.ray_distance(above,-up,shoulder.verts,shoulder.faces,reach=.7)
        variants = {style:neckwear.collar(hosts["torso"],style) for style in ("scarf","cowl","gorget")}
        armor_parts = costumes.build(hosts["torso"],"knight")
        variants["fitted steel gorget"] = neckwear.gorget(hosts["torso"],pieces=[p for p in armor_parts.values()
            if getattr(p,"bone",None)=="chest"])
        for style,collar in variants.items():
            underside = .8+kit.ray_distance(below,up,collar.verts,collar.faces,reach=.7)
            covered = np.isfinite(underside)&np.isfinite(shoulder_top)
            self.assertGreaterEqual(covered.sum(),12)
            self.assertGreater(np.min((underside-shoulder_top)[covered]),.002,style)

    def test_mail_crusader_and_scale_are_connected_closed_and_within_budget(self):
        hosts,_ = bodies.build({"torso":"standard"})
        for style in ("chain_mail","crusader","scale"):
            parts = costumes.build(hosts["torso"],style)
            self.assertLess(sum(len(part.faces) for part in parts.values()),25000)
            for name,part in parts.items():
                with self.subTest(style=style,part=name):
                    report = solid.inspect(part.verts,part.faces)
                    for field in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                        self.assertEqual(report[field],0)
                    self.assertGreater(report["signed_volume"],0)
                    if "Torso" in name or "Tasset" in name:
                        self.assertEqual(report["components"],1)
            for side in ("Front","Back"):
                # A chest-bound continuous belly would reproduce the belt collision fixed in the other costumes.
                self.assertFalse(hasattr(parts["Costume"+side+"Torso"],"bone"))
                if style=="crusader":
                    self.assertIn("heraldry",parts["CostumeCape"].slots)
                    self.assertFalse(any("Tabard" in name for name in parts))
                    self.assertFalse(hasattr(parts["CostumeCape"],"bone"))
                if style=="scale":
                    self.assertTrue({"steel_scale","steel_scale_light","steel_scale_dark"}<=set(parts["Costume"+side+"Torso"].slots))
                    self.assertFalse(any(str(slot).startswith("steel_scale") for slot in parts["Costume"+side+"Skirt"].slots))
                else:
                    self.assertTrue({"mail","mail_dark"}<=set(parts["Costume"+side+"Torso"].slots))
            for side in ("Left","Right"):
                self.assertEqual(parts["CostumeTasset"+side].bone,parts["CostumeBelt"].bone)
                if style=="scale":
                    self.assertEqual(set(parts["CostumeTasset"+side].slots),{"armor","trim"})

    def test_mail_ring_has_an_open_center_and_scale_armor_uses_silver_steel(self):
        from prefabs.outfits.mail_costumes import _ring
        ring = _ring()
        through_center = kit.ray_distance(np.array([[0.,0.,.1]]),np.array([[0.,0.,-1.]]),ring.verts,ring.faces,reach=.2)
        self.assertFalse(np.isfinite(through_center[0]))
        through_wire = kit.ray_distance(np.array([[.041,0.,.1]]),np.array([[0.,0.,-1.]]),ring.verts,ring.faces,reach=.2)
        self.assertTrue(np.isfinite(through_wire[0]))
        shield = json.loads((fittings.STUDIO/"objects/shields/shield_dragon_scale/item.json").read_text())
        costume = json.loads((fittings.STUDIO/"costumes/scale/costume.json").read_text())
        for slot in ("dragon","dragon_light","dragon_dark"):
            self.assertEqual(export.MATERIALS[slot],shield["materials"][slot])
        for slot in ("steel_scale","steel_scale_light","steel_scale_dark"):
            rgb = costume["colors"][slot]
            self.assertLessEqual(max(rgb)-min(rgb),30)
            self.assertGreaterEqual(min(rgb),140)
            self.assertEqual(export.MATERIALS[slot],export.MATERIALS["armor"])

    def test_original_waist_articulation_keeps_belt_clearance_while_walking(self):
        source = rig.Rig(export.TEMPLATES["knight"])
        variants = (("paladin","standard"),("knight","standard"),("maiden","female"),
                    ("necromancer","female"),("dark_knight","hero"),
                    ("segmented","standard"),("brigandine","standard"))
        animation = source.clips["Walking_A"]
        duration = max(float(source.acc(s["input"])[-1,0]) for s in animation["samplers"])
        for style,preset in variants:
            hosts,_ = bodies.build({"torso":preset})
            parts = costumes.build(hosts["torso"],style)
            belt = parts["CostumeBelt"]
            for side,sign in (("Front",1),("Back",-1)):
                with self.subTest(style=style,side=side):
                    waist = parts["Costume"+side+"Lame0"]
                    self.assertEqual(waist.bone,"spine")
                    self.assertLess(waist.verts[:,1].max(),.970 if side=="Front" and preset=="female" else .755 if side=="Back" else .740)
                    triangles = waist.verts[waist.faces]
                    normals = np.cross(triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0])
                    centers = triangles.mean(1)
                    mask = (normals[:,2]*sign>1e-5)&(centers[:,1]>.605)&(centers[:,1]<.645)
                    points = centers[mask]
                    view = np.array([0.,0.,sign])
                    directions = np.tile(view,(len(points),1))
                    rest = kit.ray_distance(points+.3*view,-directions,belt.verts,belt.faces,reach=.6)
                    hidden = np.isfinite(rest)&(rest<.295)&(rest>.10)
                    points,directions = points[hidden],directions[hidden]
                    self.assertGreater(len(points),5)
                    for t in np.linspace(0,duration,25):
                        transforms = source.deltas("Walking_A",float(t))
                        relative = np.linalg.inv(transforms["hips"])@transforms[waist.bone]
                        moved = points@relative[:3,:3].T+relative[:3,3]
                        hit = kit.ray_distance(moved+.3*view,-directions,belt.verts,belt.faces,reach=.6)
                        self.assertTrue(np.isfinite(hit).all(),f"{style} {side}: waist emerged beyond belt")
                        self.assertLess(hit.max(),.295,f"{style} {side}: waist cut through belt")

    def test_gambeson_skirt_upper_edge_stays_under_its_belt(self):
        hosts,_ = bodies.build({"torso":"standard"})
        for y,height in ((.625,.105),(.585,.085)):
            parts = costumes.build(hosts["torso"],"gambeson",belt_y=y,belt_height=height)
            for side in ("Front","Back"):
                skirt = parts["Costume"+side+"ClothSkirt"]
                self.assertEqual(skirt.bone,parts["CostumeBelt"].bone)
                self.assertLess(skirt.verts[:,1].max(),y+height*.5-.005)
                self.assertGreater(skirt.verts[:,1].max(),.59 if y>.6 else .59-.04)

    def test_field_costumes_are_closed_and_their_real_bands_and_hardware_connect(self):
        hosts,_ = bodies.build({"torso":"standard"})
        for style in ("segmented","brigandine","gambeson"):
            parts = costumes.build(hosts["torso"],style)
            self.assertLess(sum(len(p.faces) for p in parts.values()),25000)
            for name,part in parts.items():
                with self.subTest(style=style,part=name):
                    report = solid.inspect(part.verts,part.faces)
                    for field in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                        self.assertEqual(report[field],0)
                    self.assertGreater(report["signed_volume"],0)
            for side in ("Front","Back"):
                assembly = solid.Assembly()
                for name,part in parts.items():
                    if side+"Segment" not in name and side+"Brigandine" not in name:
                        continue
                    groups = rig._islands(part.faces,len(part.verts))
                    for group in np.unique(groups[part.faces[:,0]]):
                        keep = groups[part.faces[:,0]]==group
                        assembly.add(kit.Piece(part.verts,part.faces[keep],part.slots[keep]))
                if assembly.parts:
                    connected = assembly.build()
                    self.assertEqual(solid.inspect(connected.verts,connected.faces)["components"],1)
            if style=="gambeson":
                for side in ("Front","Back"):
                    torso = parts["Costume"+side+"Gambeson"]
                    self.assertFalse(hasattr(torso,"bone"))
                    self.assertIn("cloth_dark",torso.slots)
                    self.assertFalse(any(str(slot).startswith("armor") for p in parts.values() for slot in p.slots))
                    self.assertNotIn("Costume"+side+"Lame0",parts)
                self.assertIn("iron",parts["CostumeBelt"].slots)
            else:
                torso = parts["CostumeFrontSegment0" if style=="segmented" else "CostumeFrontBrigandine"]
                self.assertEqual(torso.bone,"chest")
            source = rig.Rig(export.TEMPLATES["knight"])
            rigid = {name:p for name,p in parts.items() if hasattr(p,"bone")}
            for clip in ("Walking_A","1H_Melee_Attack_Chop"):
                for t in (0.,.25,.50):
                    posed = rig.skinned(source,rigid,clip,t=t)
                    for side in ("Left","Right"):
                        flap,hanger = "CostumeTasset"+side,"CostumeHangers"+side
                        before = np.linalg.norm(rigid[flap].verts-rigid[hanger].verts[0],axis=1)
                        after = np.linalg.norm(posed[flap].verts-posed[hanger].verts[0],axis=1)
                        np.testing.assert_allclose(before,after,atol=1e-6)

    def test_plate_family_preserves_closed_geometry_and_physically_seats_fittings(self):
        for style,preset in (("knight","standard"),("maiden","female"),("dark_knight","hero")):
            hosts,_ = bodies.build({"torso":preset})
            parts = costumes.build(hosts["torso"],style)
            self.assertLess(sum(len(p.faces) for p in parts.values()),25000)
            for name,part in parts.items():
                with self.subTest(style=style,part=name):
                    report = solid.inspect(part.verts,part.faces)
                    for field in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                        self.assertEqual(report[field],0)
                    self.assertGreater(report["signed_volume"],0)
            for name in ("CostumeChest","CostumeTassetLeft","CostumeTassetRight"):
                # Submit original indexed islands separately: a single merged Manifold does not union itself.
                part = parts[name]
                groups = rig._islands(part.faces,len(part.verts))
                assembly = solid.Assembly()
                for group in np.unique(groups[part.faces[:,0]]):
                    keep = groups[part.faces[:,0]]==group
                    assembly.add(kit.Piece(part.verts,part.faces[keep],part.slots[keep]))
                if name=="CostumeChest" and "CostumeEmblem" in parts:
                    assembly.add(parts["CostumeEmblem"])
                mounted = assembly.build()
                self.assertEqual(solid.inspect(mounted.verts,mounted.faces)["components"],1)
            self.assertEqual("CostumeEmblem" in parts,style!="knight")
            self.assertFalse(hasattr(parts["CostumeShoulderCloth"],"bone"))

    def test_new_plate_family_keeps_hip_supports_aligned_through_motion(self):
        source = rig.Rig(export.TEMPLATES["knight"])
        for style,preset in (("knight","standard"),("maiden","female"),("dark_knight","hero")):
            hosts,_ = bodies.build({"torso":preset})
            parts = costumes.build(hosts["torso"],style)
            rigid = {name:part for name,part in parts.items() if hasattr(part,"bone")}
            for clip in ("Walking_A","1H_Melee_Attack_Chop","Running_A"):
                animation = source.clips[clip]
                duration = max(float(source.acc(s["input"])[-1,0]) for s in animation["samplers"])
                for t in np.linspace(0,duration,5):
                    posed = rig.skinned(source,rigid,clip,t=float(t))
                    for side in ("Left","Right"):
                        flap,hanger = "CostumeTasset"+side,"CostumeHangers"+side
                        before = np.linalg.norm(rigid[flap].verts-rigid[hanger].verts[0],axis=1)
                        after = np.linalg.norm(posed[flap].verts-posed[hanger].verts[0],axis=1)
                        np.testing.assert_allclose(before,after,atol=1e-6)

    def test_paladin_alias_preserves_the_approved_reference_geometry(self):
        hosts,_ = bodies.build({"torso":"standard"})
        original = costumes.build(hosts["torso"],"knight_plate")
        paladin = costumes.build(hosts["torso"],"paladin")
        self.assertEqual(original.keys(),paladin.keys())
        for name in original:
            np.testing.assert_array_equal(original[name].verts,paladin[name].verts)
            np.testing.assert_array_equal(original[name].faces,paladin[name].faces)
            np.testing.assert_array_equal(original[name].slots,paladin[name].slots)

    def test_necromancer_costume_is_closed_and_both_skulls_touch_their_supports(self):
        hosts,_ = bodies.build({"torso":"female"})
        parts = costumes.build(hosts["torso"],"necromancer")
        for name,part in parts.items():
            with self.subTest(part=name):
                report = solid.inspect(part.verts,part.faces)
                for field in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
                    self.assertEqual(report[field],0)
                self.assertGreater(report["signed_volume"],0)
        for support,badge in (("CostumeChest","CostumeEmblem"),("CostumeBelt","CostumeBuckleSkull")):
            part = parts[support]
            groups = rig._islands(part.faces,len(part.verts))
            assembly = solid.Assembly()
            for group in np.unique(groups[part.faces[:,0]]):
                keep = groups[part.faces[:,0]]==group
                assembly.add(kit.Piece(part.verts,part.faces[keep],part.slots[keep]))
            mounted = assembly.add(parts[badge]).build()
            self.assertEqual(solid.inspect(mounted.verts,mounted.faces)["components"],1)
            self.assertEqual(parts[support].bone,parts[badge].bone)
        # The approved hem difference survives the female hip profile.
        skirt = min(p.verts[:,1].min() for name,p in parts.items() if "Lame" in name)
        for side in ("Left","Right"):
            panel = parts["CostumeTasset"+side].verts
            self.assertGreater(skirt-panel[:,1].min(),.03)
            self.assertLess(skirt-panel[:,1].min(),.07)
            self.assertGreater(panel[:,2].max(),.22)
            self.assertLess(panel[:,2].min(),-.22)

    def test_necromancer_breast_shell_hands_the_belly_to_waist_plates(self):
        hosts,_ = bodies.build({"torso":"female"})
        parts = costumes.build(hosts["torso"],"necromancer")
        self.assertGreater(parts["CostumeChest"].verts[:,1].min(),.86)
        self.assertGreater(parts["CostumeFrontLame0"].verts[:,1].max(),.95)
        # There must be no exposed horizontal gap between the short breast shell and the belly.
        y = np.repeat(np.linspace(.84,1.08,13),5)
        angle = np.tile(np.linspace(1.25,1.89,5),13)
        origins = armor._surface(hosts["torso"],y,angle,0)
        cover = kit.Piece.merge(parts["CostumeChest"],parts["CostumeFrontLame0"])
        distance = kit.ray_distance(origins,np.tile([0,0,1],(len(y),1)),cover.verts,cover.faces,reach=.2)
        self.assertTrue(np.isfinite(distance).all())
        skull = parts["CostumeBuckleSkull"].verts
        self.assertGreater(np.ptp(skull[:,0]),.23)
        self.assertLess(np.ptp(skull[:,2]),.14)

    def test_necromancer_uses_female_costume_and_keeps_skulls_seated_during_motion(self):
        recipe = characters.load("mage/necromancer")
        self.assertEqual(recipe["body"]["torso"],"female")
        self.assertIn({"kind":"costume","variant":"necromancer"},recipe["outfit"])
        self.assertFalse(any(g["kind"] in ("belt","cuirass","robe") for g in recipe["outfit"]))
        hosts,_ = bodies.build(recipe["body"])
        parts = costumes.build(hosts["torso"],"necromancer")
        source = rig.Rig(export.TEMPLATES[recipe["template"]])
        for clip in ("Walking_A","Running_A","1H_Melee_Attack_Chop"):
            animation = source.clips[clip]
            duration = max(float(source.acc(s["input"])[-1,0]) for s in animation["samplers"])
            for t in np.linspace(0,duration,9):
                posed = rig.skinned(source,parts,clip,t=float(t))
                for support,badge in (("CostumeChest","CostumeEmblem"),("CostumeBelt","CostumeBuckleSkull"),
                                      ("CostumeTassetLeft","CostumeHangersLeft"),
                                      ("CostumeTassetRight","CostumeHangersRight")):
                    before = np.linalg.norm(parts[badge].verts-parts[support].verts[0],axis=1)
                    after = np.linalg.norm(posed[badge].verts-posed[support].verts[0],axis=1)
                    np.testing.assert_allclose(before,after,atol=1e-6)

    def test_emblem_is_seated_in_the_breastplate_and_uses_its_attachment(self):
        hosts, _ = bodies.build({"torso":"standard"})
        parts = costumes.build(hosts["torso"])
        chest, emblem = parts["CostumeChest"],parts["CostumeEmblem"]
        self.assertEqual(emblem.bone,chest.bone)
        groups = rig._islands(chest.faces,len(chest.verts))
        labels,counts = np.unique(groups[chest.faces[:,0]],return_counts=True)
        keep = groups[chest.faces[:,0]]==labels[counts.argmax()]
        shell = kit.Piece(chest.verts,chest.faces[keep],chest.slots[keep])
        # A real contact union rejects a badge that merely floats near the plate.
        mounted = solid.Assembly().add(shell).add(emblem).build()
        report = solid.inspect(mounted.verts,mounted.faces)
        self.assertEqual(report["components"],1)
        self.assertEqual(report["boundary_edges"],0)

    def test_shared_buckle_is_one_closed_frame_at_multiple_sizes(self):
        for size in (.036,.13,.15):
            buckle = belts.frame_buckle(size,"metal")
            report = solid.inspect(buckle.verts,buckle.faces)
            self.assertEqual(report["components"],1)
            for key in ("boundary_edges","nonmanifold_edges","duplicate_faces","inconsistent_edges"):
                self.assertEqual(report[key],0)

    def test_breastplate_has_small_positive_body_clearance(self):
        hosts, _ = bodies.build({"torso": "standard"})
        plate = costumes.build(hosts["torso"])["CostumeChest"]
        heights = np.repeat(np.linspace(.82,1.08,7),7)
        angles = np.tile(np.linspace(1.18,1.96,7),7)
        origins = armor._surface(hosts["torso"],heights,angles,0)
        distances = kit.ray_distance(origins,np.tile([0,0,1],(len(origins),1)),plate.verts,plate.faces,reach=.5)
        self.assertTrue(np.isfinite(distances).all())
        self.assertGreater(distances.min(),.005)
        self.assertLess(distances.max(),.04)

    def test_original_and_studio_use_the_same_reference_skeleton_and_clip_times(self):
        original = rig.Rig(str(fittings.REFERENCE_KNIGHT))
        template = rig.Rig(export.TEMPLATES["knight"])
        for bone in ("hips","chest","head","upperarm.l","upperarm.r","upperleg.l","upperleg.r"):
            np.testing.assert_allclose(original.rest[bone],template.rest[bone],atol=1e-7)
        for clip in ("T-Pose","Idle","Walking_A","1H_Melee_Attack_Chop"):
            for t in (0,.25,.5):
                reference = original.worlds(clip,t)
                studio = template.worlds(clip,t)
                for bone in ("hips","chest","upperarm.l","upperarm.r","upperleg.l","upperleg.r"):
                    np.testing.assert_allclose(reference[bone],studio[bone],atol=1e-7)

    def test_costume_parts_are_closed_without_overlay_topology_errors(self):
        hosts, _ = bodies.build({"torso": "standard"})
        parts = costumes.build(hosts["torso"])
        for name, part in parts.items():
            with self.subTest(name=name):
                report = solid.inspect(part.verts, part.faces)
                for key in ("boundary_edges", "nonmanifold_edges", "inconsistent_edges", "duplicate_faces"):
                    self.assertEqual(report[key], 0)
                self.assertGreater(report["signed_volume"], 0)
        self.assertIn("CostumeBelt", parts)
        self.assertEqual(parts["CostumeTassetLeft"].bone, "hips")
        self.assertFalse(any("boot" in name.lower() or "glove" in name.lower() for name in parts))

    def test_costume_export_retains_exact_rigid_attachment(self):
        hosts, _ = bodies.build({"torso": "standard"})
        parts = costumes.build(hosts["torso"])
        colors = characters.build(fittings.recipe(fittings.studies()["costume_knight_plate"]))[1]
        with tempfile.TemporaryDirectory() as directory:
            path = str(Path(directory) / "costume.glb")
            export.export(path, "costume", parts, colors, template="knight")
            g, _, acc = mesh.read_glb(path)
            bone_names = [g["nodes"][i]["name"] for i in g["skins"][0]["joints"]]
            for node in g["nodes"]:
                if "mesh" not in node or "skin" not in node:
                    continue
                part = parts[node["name"].removeprefix("costume_")]
                for primitive in g["meshes"][node["mesh"]]["primitives"]:
                    attrs = primitive["attributes"]
                    weights = acc(attrs["WEIGHTS_0"])
                    joints = acc(attrs["JOINTS_0"])
                    np.testing.assert_allclose(weights[:,0],1)
                    np.testing.assert_allclose(weights[:,1:],0)
                    self.assertTrue(all(bone_names[int(j)] == part.bone for j in joints[:,0]))

    def test_software_pose_preserves_plate_shape(self):
        hosts, _ = bodies.build({"torso": "standard"})
        parts = costumes.build(hosts["torso"])
        animation_rig = rig.Rig(export.TEMPLATES["knight"])
        for clip in ("Walking_A", "1H_Melee_Attack_Chop"):
            posed = rig.skinned(animation_rig, parts, clip, t=.3)
            for name, part in parts.items():
                before = np.linalg.norm(part.verts - part.verts[0], axis=1)
                after = np.linalg.norm(posed[name].verts - posed[name].verts[0], axis=1)
                np.testing.assert_allclose(before, after, atol=1e-6)

    def test_flaps_remain_attached_to_their_hangers_through_motion(self):
        hosts, _ = bodies.build({"torso": "standard"})
        parts = costumes.build(hosts["torso"])
        animation_rig = rig.Rig(export.TEMPLATES["knight"])
        for clip in ("Walking_A", "1H_Melee_Attack_Chop", "Running_A"):
            anim = animation_rig.clips[clip]
            duration = max(float(animation_rig.acc(s["input"])[-1,0]) for s in anim["samplers"])
            for t in np.linspace(0, duration, 17):
                posed = rig.skinned(animation_rig, parts, clip, t=float(t))
                for side in ("Left", "Right"):
                    flap, hanger = "CostumeTasset"+side, "CostumeHangers"+side
                    before = np.linalg.norm(parts[flap].verts - parts[hanger].verts[0], axis=1)
                    after = np.linalg.norm(posed[flap].verts - posed[hanger].verts[0], axis=1)
                    np.testing.assert_allclose(before, after, atol=1e-6, err_msg=f"{clip} {t} {side}: suspension separated")

    def test_costume_belt_height_is_lower_and_configurable_with_its_flaps(self):
        hosts, _ = bodies.build({"torso": "standard"})
        normal = costumes.build(hosts["torso"])
        lowered = costumes.build(hosts["torso"], belt_y=.585)
        belt = normal["CostumeBelt"].verts[:,1]
        self.assertAlmostEqual(.5 * (belt.min()+belt.max()), .625, places=5)
        for name in ("CostumeTassetLeft", "CostumeHangersLeft", "CostumeTassetRight", "CostumeHangersRight"):
            # Curved panels refit to the hip section at the new height, including their rivet normals.
            before = normal[name].verts[:,1]
            after = lowered[name].verts[:,1]
            np.testing.assert_allclose([after.min()-before.min(),after.max()-before.max()],-.04,atol=.001)
            self.assertEqual(lowered[name].bone,"hips")
            report = solid.inspect(lowered[name].verts,lowered[name].faces)
            self.assertEqual(report["boundary_edges"],0)
            self.assertEqual(report["nonmanifold_edges"],0)

    def test_humanoid_neck_and_shoulders_are_one_closed_body_surface(self):
        for preset in ("standard","hero","female","brute","skinny"):
            hosts, _ = bodies.build({"torso":preset})
            torso = hosts["torso"]
            shoulder = torso.section([1.09])[1][0]
            neck = torso.section([1.20])[1][0]
            self.assertGreater(shoulder,neck*1.7)
            mesh = torso.mesh()
            report = solid.inspect(mesh.verts,mesh.faces)
            self.assertEqual(report["components"],1)
            self.assertEqual(report["boundary_edges"],0)

    def test_belt_follows_waist_shell_and_short_panels_curve_around_hips(self):
        hosts, _ = bodies.build({"torso":"standard"})
        parts = costumes.build(hosts["torso"])
        belt = parts["CostumeBelt"].verts
        self.assertGreater(np.ptp(belt[:,1]),.10)
        self.assertLess(np.ptp(belt[:,1]),.15)
        self.assertLess(parts["CostumeChest"].verts[:,1].max(),1.20)
        self.assertGreater(parts["CostumeTassetLeft"].verts[:,0].max(),.30)
        self.assertLess(np.ptp(parts["CostumeTassetLeft"].verts[:,1]),.20)
        host = costumes.WaistHost(hosts["torso"])
        panel = costumes._hip_panel(host,.625,0)
        a,m,b = [panel.surface_at(u,.5) for u in (-.8,0,.8)]
        bow = np.linalg.norm(np.cross(m-a,b-a))/np.linalg.norm(b-a)
        self.assertGreater(bow,.01)
        waist = kit.Piece.merge(*[p for name,p in parts.items() if "Lame" in name])
        theta = np.linspace(.12,2*np.pi-.12,48)
        origin = armor._surface(hosts["torso"],np.full(len(theta),.625),theta,0)
        direction = origin-[0,.625,0]
        direction /= np.linalg.norm(direction,axis=1,keepdims=True)
        shell_inner = kit.ray_distance(origin,direction,waist.verts,waist.faces,reach=.5)
        belt_inner = kit.ray_distance(origin,direction,parts["CostumeBelt"].verts,parts["CostumeBelt"].faces,reach=.5)
        self.assertTrue(np.isfinite(shell_inner).all() and np.isfinite(belt_inner).all())
        # Includes the waist plate thickness; a loose padded belt exceeded this by a wide margin.
        self.assertLess(np.max(belt_inner-shell_inner),.03)

    def test_hip_flaps_wrap_front_to_back_and_extend_below_skirt(self):
        hosts, _ = bodies.build({"torso":"standard"})
        parts = costumes.build(hosts["torso"])
        skirt = kit.Piece.merge(*[p for name,p in parts.items() if "Lame" in name])
        for suffix,sign in (("Left",1),("Right",-1)):
            panel = parts["CostumeTasset"+suffix]
            self.assertTrue((panel.verts[:,0]*sign>0).all())
            self.assertGreater(panel.verts[:,2].max(),.25)
            self.assertLess(panel.verts[:,2].min(),-.25)
            visible_drop = skirt.verts[:,1].min()-panel.verts[:,1].min()
            self.assertGreater(visible_drop,.03)
            self.assertLess(visible_drop,.07)

    def test_waist_is_close_at_belly_and_flared_at_lower_hem(self):
        hosts, _ = bodies.build({"torso":"standard"})
        body = hosts["torso"]
        skirt = costumes.WaistHost(body)
        for index in (1,3):
            self.assertLess(skirt.section([.70])[index][0]-body.section([.70])[index][0],.003)
            flare = skirt.section([.42])[index][0]-skirt.section([.68])[index][0]
            self.assertGreater(flare,.02)
            self.assertLess(flare,.045)

    def test_export_bundle_keeps_animated_previews_and_rejects_unrelated_folder(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory)/"costume"
            source.mkdir(parents=True)
            for name in ("lineup.gif","lineup.png","comparison.gif","comparison.png","costume.glb"):
                (source/name).write_bytes(("current "+name).encode())
            with patch.object(fittings,"lineup_path",return_value=source/"lineup.png"):
                bundle = fittings.package_costume("costume_knight_plate")
                self.assertEqual((bundle/"preview.gif").read_bytes(),(source/"lineup.gif").read_bytes())
                self.assertEqual((bundle/"costume.glb").read_bytes(),(source/"costume.glb").read_bytes())
                self.assertIn("preview.gif",json.loads((bundle/"manifest.json").read_text())["files"])
                unrelated = Path(directory)/"unrelated"
                unrelated.mkdir()
                (unrelated/"notes.txt").write_text("keep")
                with self.assertRaisesRegex(ValueError,"unrelated files"):
                    fittings.package_costume("costume_knight_plate",unrelated)
                self.assertEqual((unrelated/"notes.txt").read_text(),"keep")

    def test_dummy_arm_volume_does_not_move_shoulder_joint(self):
        study = fittings.recipe(fittings.studies()["costume_knight_plate"])
        hosts, parts = bodies.build(study["body"])
        original_hosts, original_parts = bodies.build({"torso":"standard", "arms":{"hand":"mitten"}, "legs":{"foot":"bare"}})
        np.testing.assert_allclose(hosts["arm"].frame.origin, original_hosts["arm"].frame.origin)
        self.assertGreater(parts["ArmLeft"].verts[:,1].max(), original_parts["ArmLeft"].verts[:,1].max()+.015)

    def test_unknown_attachment_bone_fails_before_publication(self):
        part = kit.bevel_box((.1,.1,.1),.01,"armor")
        part.bone = "missing_bone"
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError,"unknown attachment bone"):
                export.export(str(Path(directory)/"bad.glb"),"bad",{"Plate":part},{"armor":(120,130,140)},template="knight")

    def test_outfit_catalog_has_a_color_for_every_surface(self):
        for entry in outfits.catalog():
            name, piece = entry[:2]
            palette = {**kit.DEMO_COLORS, **(entry[3] if len(entry) > 3 else {})}
            colors, _ = export.resolve_palette(set(map(str, piece.slots)), palette)
            with self.subTest(name=name):
                self.assertFalse(set(map(str, piece.slots)) - colors.keys())

    def test_every_study_builds_on_its_intended_body(self):
        for name, study in fittings.studies().items():
            with self.subTest(name=name):
                pieces, colors = characters.build(fittings.recipe(study))
                self.assertTrue(all(np.isfinite(p.verts).all() for p in pieces.values()))
                self.assertGreater(sum(len(p.faces) for p in pieces.values()), 0)
                self.assertEqual(colors["skin"], fittings.NEUTRAL["skin"])
                self.assertIn("Head", pieces)

    def test_dummy_recipe_does_not_modify_original_or_prefab(self):
        original = characters.load("heroes/knight")
        study = fittings.studies()["costume_knight"]
        snapshot = copy.deepcopy(study)
        recipe = fittings.recipe(study)
        recipe["outfit"][0]["slot"] = "changed"
        self.assertEqual(study, snapshot)
        self.assertEqual(original, characters.load("heroes/knight"))
        self.assertTrue(any(g["kind"] == "helmet" for g in original["outfit"]))
        self.assertFalse(any(g["kind"] in ("helmet", "cape") for g in study["outfit"]))

    def test_review_covers_distinct_constructions(self):
        available = fittings.studies()
        for key in ("costume_chain_mail", "costume_scale", "costume_gambeson", "costume_brigandine",
                    "costume_segmented", "costume_knight", "costume_necromancer", "costume_crusader"):
            self.assertIn(key, available)


if __name__ == "__main__":
    unittest.main()
