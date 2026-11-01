"""The boss must retain rigid armor, removable gear and its large-rig motion contract."""
import json,sys,tempfile,unittest
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio import Character
from core import character_workflow as workflow,creature_rigs as cr,hinges
from core.rig import Rig
from prefabs.creatures import wardens
from items import executioner,solid


class WardenTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.character=Character('bosses/forsaken_warden')
        cls.folder=workflow.build(cls.character)
        cls.result=Rig(str(cls.folder/'model.glb'))
        cls.parts=wardens.build(recipe=cls.character.recipe())

    def test_large_motion_and_detachable_geometry(self):
        rig=self.result;g=rig.g
        self.assertEqual({m['name'] for m in g['meshes']},{'Body','ExecutionerSword','WardenShield','WarHorn'})
        profile=workflow.profile(self.character)
        self.assertEqual(profile['clips']['Attack'],'Melee_1H_Slash')
        self.assertTrue(set(profile['clips'].values())<=set(rig.clips))
        self.assertLessEqual(json.loads((self.folder/'asset.json').read_text())['triangles'],30000)
        self.assertEqual(len(g['skins'][0]['joints']),25)
        for node in g['nodes']:
            if 'mesh' not in node:continue
            self.assertIn('skin',node)
            for p in g['meshes'][node['mesh']]['primitives']:
                weights=rig.acc(p['attributes']['WEIGHTS_0'])
                np.testing.assert_allclose(weights.sum(1),1,atol=1e-6)
                offset=0
                for piece in self.parts.values():
                    count=len(piece.faces)*3;part_weights=weights[offset:offset+count]
                    if not hasattr(piece,'skin_weights'):
                        np.testing.assert_array_equal(part_weights[:,0],1)
                        np.testing.assert_array_equal(part_weights[:,1:],0)
                    else:self.assertTrue(np.any(part_weights[:,1]>0))
                    offset+=count

    def test_exported_parts_follow_their_declared_bones_in_motion(self):
        rig=self.result;g=rig.g;native=Rig.load('large')
        specs=hinges.specifications(self.parts)
        first=g['meshes'][0]['primitives'][0]['attributes']
        v=rig.acc(first['POSITION']);j=rig.acc(first['JOINTS_0']).astype(int);weights=rig.acc(first['WEIGHTS_0'])
        sk=g['skins'][0];inverse=rig.acc(sk['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
        scale=self.character.recipe()['scale']
        for clip in ['Idle_A','Walking_A','Melee_1H_Slash','Melee_2H_Slam','Melee_Block','Death_A']:
            for time in [.0,.27,.53]:
                pose=rig.worlds(clip,time)
                matrices=np.array([pose[g['nodes'][x]['name']] for x in sk['joints']])@inverse
                expected=hinges.pose_deltas(native.deltas(clip,time),specs)
                offset=0
                for piece in self.parts.values():
                    corners=piece.verts[piece.faces].reshape(-1,3)
                    ids=offset+np.array([0,len(corners)//2,len(corners)-1])
                    actual=np.zeros((len(ids),3))
                    for k in range(4):
                        m=matrices[j[ids,k]]
                        actual+=(np.einsum('nij,nj->ni',m[:,:3,:3],v[ids])+m[:,:3,3])*weights[ids,k,None]
                    influence=getattr(piece,'skin_weights',{piece.bone:np.ones(len(piece.verts))})
                    want=np.zeros_like(actual);source_ids=piece.faces.ravel()[ids-offset]
                    for bone,w in influence.items():
                        transform=expected[bone]
                        want+=(corners[ids-offset]@transform[:3,:3].T+transform[:3,3])*w[source_ids,None]*scale
                    np.testing.assert_allclose(actual,want,atol=2e-4)
                    offset+=len(corners)

    def test_hand_attachment_axes_and_offsets_match_the_verified_source(self):
        from core import kit
        native=Rig.load('large')
        probe=kit.Piece(np.array([[0,0,0],[1,0,0],[0,1,0],[0,0,1]],float),
                        np.array([[0,1,2],[0,2,3]]),'steel')
        # Independent canonical expectations: no swizzled shield axes or display-only sword tilt.
        expected={'sword':('handslot.r',np.diag([-1.,1.,-1.]),[0,.03330930517626057,0]),
                  'shield':('handslot.l',np.eye(3),[0,.01701178596925984,.15588533878326416])}
        for role,(bone,rotation,offset) in expected.items():
            mounted=wardens.attach(probe,native,role,'TestGear')
            inverse=np.linalg.inv(native.rest[bone])
            local=mounted.verts@inverse[:3,:3].T+inverse[:3,3]
            np.testing.assert_allclose(local,probe.verts@rotation.T+offset,atol=4e-6)
            self.assertEqual(mounted.bone,bone)

    def test_smaller_head_and_visible_limb_proportions_keep_the_rig(self):
        # Keep the helmet seated at its original lower edge while shrinking its volume.
        head=self.parts['Helmet'];original=wardens.helmet()
        scale=self.character.recipe()['head_scale']
        np.testing.assert_allclose(np.ptp(head.verts,axis=0),np.ptp(original.verts,axis=0)*scale)
        self.assertAlmostEqual(head.verts[:,1].min(),original.verts[:,1].min())
        self.assertEqual(head.bone,'head')
        self.assertTrue({'UpperArm.l','Forearm.l','Thigh.l','Greave.l'}<=set(self.parts))

    @staticmethod
    def manifold(piece):
        import manifold3d as mf
        from core.rig import _islands
        from core import kit
        labels=_islands(piece.faces,len(piece.verts))[piece.faces[:,0]]
        assembly=solid.Assembly()
        for label in np.unique(labels):
            keep=labels==label
            assembly.add(kit.Piece(piece.verts,piece.faces[keep],piece.slots[keep]))
        result=mf.Manifold.batch_boolean(assembly.parts,mf.OpType.Add)
        if result.status()!=mf.Error.NoError:raise AssertionError(result.status())
        return result

    def test_shield_clears_forearm_cuffs_through_shielded_motion(self):
        from core import kit
        r=Rig.load('large')
        cuff=self.manifold(kit.Piece.merge(self.parts['Forearm.l'],self.parts['JointGuardForearm.l']))
        shield=self.manifold(self.parts['Shield'])
        for clip in ['Idle_A','Walking_A','Running_A','Melee_1H_Slash','Melee_Block',
                     'Melee_Block_Attack','Melee_Block_Hit','Hit_A','Death_A']:
            a=r.clips[clip];duration=max(float(r.acc(s['input']).max()) for s in a['samplers'])
            for t in np.linspace(0,duration,21):
                d=r.deltas(clip,t)
                overlap=cuff.transform(d['lowerarm.l'][:3]) ^ shield.transform(d['handslot.l'][:3])
                self.assertLess(overlap.volume(),1e-7,(clip,t,overlap.volume()))

    def test_limb_underlayers_fill_the_cuffs(self):
        from core import kit
        r=Rig.load('large')
        for start,end,radius in [('upperarm.l','lowerarm.l',.19),('lowerarm.l','wrist.l',.235),
                                 ('upperleg.l','lowerleg.l',.205),('lowerleg.l','foot.l',.24)]:
            piece,limb=wardens.segment(r,start,end,radius)
            local=(piece.verts-limb.frame.origin)@limb.frame.R
            body_faces=piece.faces[piece.slots=='cloth']
            for fraction in [.15,.85]:
                origin=np.array([[0,limb.length*fraction,radius*2]])
                distance=kit.ray_distance(origin,np.array([[0,0,-1.]]),local,body_faces,reach=radius*3)[0]
                actual=radius*2-distance
                self.assertGreater(actual,float(limb.section([limb.length*fraction])[3][0])*.93)

    def test_flexible_neck_stays_seated_in_chest_and_helmet(self):
        from core import kit
        neck=self.parts['Neck'];r=Rig.load('large')
        chest=self.manifold(self.parts['ChestLining']);helmet=self.manifold(self.parts['Helmet'])
        self.assertEqual(set(neck.skin_weights),{'head','chest'})
        for clip in ['Idle_A','Walking_A','Melee_1H_Slash','Hit_A','Death_A']:
            for t in [0,.2,.4,.6]:
                d=r.deltas(clip,t);v=np.zeros_like(neck.verts)
                for bone,w in neck.skin_weights.items():
                    m=d[bone];v+=(neck.verts@m[:3,:3].T+m[:3,3])*w[:,None]
                posed=self.manifold(kit.Piece(v,neck.faces,neck.slots))
                self.assertGreater((posed ^ chest.transform(d['chest'][:3])).volume(),1e-5,(clip,t,'chest'))
                self.assertGreater((posed ^ helmet.transform(d['head'][:3])).volume(),1e-5,(clip,t,'helmet'))

    def test_executioner_has_a_broad_squared_blade_and_closed_fittings(self):
        sword=executioner.executioner_sword()
        upper=sword.verts[sword.verts[:,1]>1]
        self.assertGreater(np.ptp(upper[:,0]),.55)
        tip=sword.verts[np.isclose(sword.verts[:,1],sword.verts[:,1].max())]
        self.assertGreater(np.ptp(tip[:,0]),.4)
        report=solid.inspect(sword.verts,sword.faces)
        for key in ['boundary_edges','nonmanifold_edges','inconsistent_edges','duplicate_faces']:
            self.assertEqual(report[key],0)

    def test_shield_can_be_removed_without_remodeling_the_body(self):
        without=wardens.build(recipe={'shield':False})
        self.assertNotIn('Shield',without)
        for name,piece in without.items():
            np.testing.assert_array_equal(piece.verts,self.parts[name].verts)
        helmet=self.parts['Helmet'];self.assertIn('eyes',helmet.slots)
        self.assertTrue(wardens.MATERIALS['eyes']['emissive'])


if __name__=='__main__':unittest.main()
