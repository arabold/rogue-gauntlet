"""Shared fitting helpers reject floating fur and follow real armor geometry."""
from pathlib import Path
import sys
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from core import kit
from items import solid
from prefabs.details import fur
from prefabs.outfits import belts


class TaperedHost:
    def section(self,y):
        y=np.atleast_1d(np.asarray(y,float))
        return np.zeros_like(y),.30+.16*y,np.zeros_like(y),.22+.12*y


class FittedDetailsTest(unittest.TestCase):
    def setUp(self):
        self.host=TaperedHost()
        self.support=kit.host_band(self.host,0,1,.04,"armor",sides=64)

    def test_belt_follows_selected_armor_at_multiple_heights(self):
        # Deliberately displaced decoration must not determine the waist outline.
        decoration=kit.bevel_box((.1,.1,.1),.01,"strap",center=(.6,.5,0))
        belt=belts.build_fitted(self.host,[self.support,decoration],.5,height=.3,
            thickness=.022,fit_slot="armor",buckle=None,studs=0)
        theta=np.arange(96)*2*np.pi/96
        direction=np.column_stack([np.cos(theta),np.zeros(96),np.sin(theta)])
        for y in (.39,.45,.5,.55,.61):
            origin=np.array([0,y,0])+2*direction
            outer=kit.ray_distance(origin,-direction,belt.verts,belt.faces)
            inner=kit.ray_distance(origin,-direction,self.support.verts,self.support.faces)
            clearance=inner-outer
            self.assertTrue(np.isfinite(clearance).all())
            self.assertGreater(clearance.min(),.018)
            self.assertLess(clearance.max(),.026)
        with self.assertRaisesRegex(ValueError,"supporting armor"):
            belts.build_fitted(self.host,[decoration],.5,fit_slot="missing")

    def test_fur_attaches_to_tilted_rim_and_keeps_bone_and_colors(self):
        theta=np.arange(64)*2*np.pi/64
        outward=np.column_stack([np.cos(theta),np.zeros(64),np.sin(theta)])
        path=kit.ring_points(self.host,.007,.030,64,theta)
        angle=np.radians(38)
        rotation=np.array([[np.cos(angle),-np.sin(angle),0],
                           [np.sin(angle),np.cos(angle),0],[0,0,1]])
        support=kit.Piece(self.support.verts@rotation.T,self.support.faces,self.support.slots)
        support.bone="hips"
        result=fur.border(support,path@rotation.T,outward@rotation.T,
                          fall=np.array([0,-1,0])@rotation.T,length=.06,thickness=.025)
        report=solid.inspect(result.verts,result.faces)
        self.assertEqual(report["components"],1)
        self.assertEqual(report["boundary_edges"],0)
        self.assertEqual(result.bone,"hips")
        self.assertEqual(set(result.slots),{"armor","fur"})

    def test_floating_fur_and_mismatched_attachments_are_rejected(self):
        theta=np.arange(64)*2*np.pi/64
        outward=np.column_stack([np.cos(theta),np.zeros(64),np.sin(theta)])
        path=kit.ring_points(self.host,.007,.030,64,theta)
        with self.assertRaisesRegex(ValueError,"disconnected"):
            fur.border(self.support,path+[0,-1,0],outward)
        edge=fur.edge(path,outward)
        self.support.bone="hips"
        edge.bone="chest"
        with self.assertRaisesRegex(ValueError,"same bone"):
            fur.attach(self.support,edge)

    def test_cape_and_gorget_are_alternative_garments(self):
        from prefabs.creatures import characters
        recipe=dict(head={"form":{}},body={"torso":"standard"},
            outfit=[{"kind":"gorget"},{"kind":"cape"}])
        with self.assertRaisesRegex(ValueError,"cape or a gorget"):
            characters.build(recipe)
