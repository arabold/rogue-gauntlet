"""Regressions for the caped barbarian's fitted belt, shoulder path and cape collar."""
from pathlib import Path
import sys
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import fittings
from core import kit
from items import solid
from prefabs.creatures import characters


class BarbarianRefinementTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.parts,_=characters.build(fittings.recipe(fittings.studies()["costume_barbarian"]))

    def part(self,prefix):
        return next(p for name,p in self.parts.items() if name.startswith(prefix))

    def test_belt_stays_fitted_with_front_swell_and_both_fur_borders(self):
        belt=self.part("Belt")
        # Previously 0.805 across and 0.365 tall, including boss and both fur borders.
        extent=np.ptp(belt.verts,axis=0)
        self.assertLess(extent[0],.75)
        self.assertLess(extent[1],.27)
        fur=belt.verts[belt.faces[belt.slots=="fur_trim"]]
        self.assertGreater(fur[:,:,1].max(),.75)
        self.assertLess(fur[:,:,1].min(),.53)
        leather=belt.verts[belt.faces[belt.slots=="leather"]]
        self.assertLess(np.ptp(leather[:,:,1]),.215)
        front=leather[leather[:,:,2].mean(1)>.25]
        back=leather[leather[:,:,2].mean(1)<-.25]
        self.assertGreater(np.ptp(front[:,:,1]),np.ptp(back[:,:,1])+.05)

    def test_leather_and_fur_share_one_closed_exterior(self):
        belt=self.part("Belt")
        keep=np.isin(belt.slots,("leather","fur_trim"))
        body=kit.Piece(belt.verts,belt.faces[keep],belt.slots[keep])
        report=solid.inspect(body.verts,body.faces)
        self.assertEqual(report["components"],1,"Fur must merge into the leather, not remain separate glued triangles")
        for key in ("boundary_edges","nonmanifold_edges","duplicate_faces","inconsistent_edges"):
            self.assertEqual(report[key],0,key)

    def test_band_remains_visible_above_tunic_all_around_the_waist(self):
        belt,shirt=self.part("Belt"),self.part("Tunic")
        band=kit.Piece(belt.verts,belt.faces[belt.slots=="leather"],"leather")
        theta=np.arange(96)*2*np.pi/96
        directions=np.column_stack([np.cos(theta),np.zeros(96),np.sin(theta)])
        for y in (.595,.615,.640,.665,.690):
            origins=np.array([0,y,0])+directions*2
            belt_hit=kit.ray_distance(origins,-directions,band.verts,band.faces,reach=2.5)
            shirt_hit=kit.ray_distance(origins,-directions,shirt.verts,shirt.faces,reach=2.5)
            self.assertTrue(np.isfinite(belt_hit).all(),f"Belt disappears at waist height {y}")
            clearance=shirt_hit-belt_hit
            self.assertGreater(np.nanmin(clearance),.005,
                               f"Tunic hides part of the belt at waist height {y}")
            self.assertLess(np.nanmax(clearance),.030,
                            f"Belt stands away from the tunic at waist height {y}")

    def test_baldric_has_one_smooth_connected_shoulder_curve(self):
        strap=self.part("Baldric")
        report=solid.inspect(strap.verts,strap.faces)
        self.assertEqual(report["components"],1)
        self.assertEqual(report["boundary_edges"],0)
        self.assertEqual(report["nonmanifold_edges"],0)
        width=len(kit.rect_profile(.08,.022,.004))
        center=strap.verts[:80*width].reshape(80,width,3).mean(1)
        directions=np.diff(center,axis=0)
        directions/=np.linalg.norm(directions,axis=1)[:,None]
        turns=np.degrees(np.arccos(np.clip(np.sum(directions[:-1]*directions[1:],axis=1),-1,1)))
        self.assertLess(turns.max(),25,"A shoulder crown must not acquire a right-angle kink")
        self.assertGreater(center[:,1].max(),1.25)
        self.assertLess(center[0,1],.62)
        self.assertLess(center[-1,1],.62)

    def test_cape_has_one_continuous_chest_shoulder_and_back_surface(self):
        cape=self.part("Cape")
        self.assertFalse(any(name.startswith("Neckcloth") for name in self.parts))
        report=solid.inspect(cape.verts,cape.faces)
        self.assertEqual(report["components"],1)
        for key in ("boundary_edges","nonmanifold_edges","duplicate_faces","inconsistent_edges"):
            self.assertEqual(report[key],0,key)
        self.assertEqual(cape.bone,"chest")
        self.assertEqual(cape.garment_type,"cape")
        front=cape.verts[cape.verts[:,2]>.1]
        back=cape.verts[cape.verts[:,2]<-.1]
        self.assertLess(front[:,1].min(),1.02)
        self.assertLess(back[:,1].min(),.25)
        self.assertTrue({"pelt","pelt_dark"}<=set(cape.slots))
