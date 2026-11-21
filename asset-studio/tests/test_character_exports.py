"""Production bound-source export preserves selected art and rejects stale/failed output."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio import Character
from core import character_workflow as workflow,creature_rigs as cr,mesh,publication
from core.rig import Rig,_sample


def posed(path,clip,t):
    r=Rig(str(path));g=r.g;worlds=r.worlds(clip,t);out=[]
    for n in g['nodes']:
        if 'skin' not in n:continue
        sk=g['skins'][n['skin']];ibm=r.acc(sk['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
        m=np.array([worlds[g['nodes'][j]['name']] for j in sk['joints']])@ibm
        for p in g['meshes'][n['mesh']]['primitives']:
            a=p['attributes'];v=r.acc(a['POSITION']);j=r.acc(a['JOINTS_0']).astype(int);w=r.acc(a['WEIGHTS_0'])
            q=np.zeros_like(v)
            for k in range(4):
                matrix=m[j[:,k]]
                q+=(np.einsum('nij,nj->ni',matrix[:,:3,:3],v)+matrix[:,:3,3])*w[:,k,None]
            out.append(q)
    return np.vstack(out)


class CharacterExportTests(unittest.TestCase):
    def test_selected_sources_survive_build_without_donors(self):
        for name in ['wolf','rat','spider']:
            c=Character('beasts/'+name);source=Path(c.dir)/'source.glb';original=source.read_bytes()
            folder=workflow.build(c)
            self.assertEqual((folder/'model.glb').read_bytes(),original)
            self.assertEqual(source.read_bytes(),original)
            r=Rig(str(source))
            for clip,a in r.clips.items():
                duration=max(float(r.acc(s['input']).max()) for s in a['samplers'])
                for t in np.linspace(0,duration,13):self.assertTrue(np.isfinite(posed(folder/'model.glb',clip,t)).all())

    def test_palette_and_scale_change_art_without_rebinding_or_changing_keys(self):
        c=Character('beasts/rat');recipe=c.recipe();recipe['scale']=2;recipe['colors']['coat']=[30,35,40]
        with tempfile.TemporaryDirectory() as tmp,patch.object(c,'recipe',return_value=recipe):
            target=Path(tmp)/'art.glb';workflow._bound(c,target)
            source=Path(c.dir)/'source.glb';a=Rig(str(source));b=Rig(str(target))
            for clip in a.clips:
                for x,y in zip(a.clips[clip]['samplers'],b.clips[clip]['samplers']):
                    np.testing.assert_array_equal(a.acc(x['output']),b.acc(y['output']))
                np.testing.assert_allclose(posed(target,clip,.23),posed(source,clip,.23)*2,atol=1e-6)
            self.assertNotEqual(target.read_bytes(),source.read_bytes())

    def test_changed_source_surface_and_missing_roles_fail(self):
        c=Character('beasts/rat')
        with tempfile.TemporaryDirectory() as tmp:
            folder=Path(tmp)
            for name in ['source.glb','surface.json']:(folder/name).write_bytes((Path(c.dir)/name).read_bytes())
            surface=json.loads((folder/'surface.json').read_text());surface['source_sha256']='bad';(folder/'surface.json').write_text(json.dumps(surface))
            with patch.object(c,'dir',str(folder)),self.assertRaisesRegex(ValueError,'Source changed'):workflow._bound(c,folder/'out.glb')
            p=workflow.profile(c);p['clips']['Absent']='Absent'
            with self.assertRaisesRegex(ValueError,'animation roles'):workflow.validate(Path(c.dir)/'source.glb',p)

    def test_failed_import_restores_prior_export_and_uid(self):
        c=Character('beasts/rat');folder=workflow.build(c)
        with tempfile.TemporaryDirectory() as tmp:
            destination=Path(tmp)/'rat.glb';destination.write_bytes(b'old model')
            settings=Path(str(destination)+'.import');old='[remap]\nuid="uid://keepme"\n[params]\n';settings.write_text(old)
            with patch.object(c,'glb',str(destination)),patch.object(workflow,'lineup',return_value=folder),patch.object(workflow.subprocess,'run',return_value=type('R',(),{'returncode':0})()),patch.object(workflow,'inspect_import',side_effect=ValueError('failed import')):
                with self.assertRaisesRegex(ValueError,'failed import'):workflow.export_asset(c)
            self.assertEqual(destination.read_bytes(),b'old model');self.assertEqual(settings.read_text(),old)
            self.assertFalse(destination.with_suffix('.asset.json').exists())

    def test_import_settings_preserve_uid_and_reset_tracks(self):
        with tempfile.TemporaryDirectory() as tmp:
            dest=Path(tmp)/'wolf.glb';dest.write_bytes((Path(Character('beasts/wolf').dir)/'source.glb').read_bytes());settings=Path(str(dest)+'.import');settings.write_text('[remap]\nuid="uid://keepme"\n[params]\n')
            workflow.import_settings(dest,workflow.profile(Character('beasts/wolf')))
            text=settings.read_text();self.assertIn('uid://keepme',text);self.assertIn('animation/remove_immutable_tracks=false',text)
            self.assertIn('"Run"',text)

    def test_rigid_golem_and_shoulders_follow_the_large_rig(self):
        from prefabs.creatures import golems
        c=Character('golem/stone_golem');path=workflow.build(c)/'model.glb';g,_,acc=mesh.read_glb(str(path))
        attrs=g['meshes'][0]['primitives'][0]['attributes'];w=acc(attrs['WEIGHTS_0'])
        np.testing.assert_array_equal(w[:,0],1);np.testing.assert_array_equal(w[:,1:],0)
        rig=Rig(str(workflow.STUDIO/'rigs/large/rig.glb'));result=Rig(str(path))
        expected=golems.build(rig=rig)
        # Source and output helper deltas agree through independent poses.
        for clip in ['Idle_A','Walking_A','Melee_Unarmed_Smash']:
            for t in [.0,.17,.39]:
                before=rig.deltas(clip,t,helpers=golems.HELPERS);after=result.deltas(clip,t)
                for bone in golems.HELPERS:np.testing.assert_allclose(after[bone],before[bone],atol=1e-3)

    def test_golem_moss_partitions_the_stone_surface_without_shells(self):
        from prefabs.creatures import golems
        from scipy.spatial import ConvexHull
        base=golems.rock((1.2,1.0,.8),(0,0,0),seed=7)
        result=golems.moss(base)
        def area(piece):
            tri=piece.verts[piece.faces]
            return np.linalg.norm(np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]),axis=1).sum()/2
        self.assertTrue({'stone','moss'}<=set(result.slots))
        self.assertAlmostEqual(area(base),area(result),places=10)
        hull=ConvexHull(base.verts)
        signed=result.verts[result.faces].reshape(-1,3)@hull.equations[:,:3].T+hull.equations[:,3]
        np.testing.assert_allclose(signed.max(1),0,atol=1e-12)
        self.assertTrue(np.isfinite(result.normals).all())
        np.testing.assert_allclose(np.linalg.norm(result.normals,axis=2),1,atol=1e-12)


class TechnicalRigTests(unittest.TestCase):
    def test_profiles_have_no_original_art_and_extraction_keeps_clips(self):
        with tempfile.TemporaryDirectory() as tmp:
            for name,count in [('blob',4),('spider',39),('wolf',51),('rat',31)]:
                info=cr.inspect(name);self.assertEqual(len(info['joints']),count)
                g,_,_=cr._doc(cr.source(name));self.assertNotIn('meshes',g);self.assertNotIn('images',g)
                out=Rig(str(cr.extract(name,Path(tmp)/(name+'.glb'))));original=Rig(str(cr.source(name)))
                for clip in out.clips:
                    for time in [.0,.21,.43]:
                        a,b=out.worlds(clip,time),original.worlds(clip,time)
                        for bone in info['joints']:np.testing.assert_allclose(a[bone],b[bone],atol=1e-5)

    def test_switching_clips_does_not_inherit_old_joint_transforms(self):
        for name in ['wolf','rat','blob','spider']:
            r=Rig(str(cr.source(name)));state=copy.deepcopy(r.g)
            for role in list(r.clips)+list(r.clips)[::-1]:
                a=r.clips[role]
                for ch in a['channels']:
                    s=a['samplers'][ch['sampler']];target=ch['target']
                    state['nodes'][target['node']][target['path']]=_sample(r.acc(s['input'])[:,0],r.acc(s['output']).astype(float),.2,target['path']=='rotation').tolist()
                actual=mesh._node_worlds(state);expected=r.worlds(role,.2)
                for j in r.g['skins'][0]['joints']:np.testing.assert_allclose(actual[j],expected[r.g['nodes'][j]['name']],atol=1e-5)


if __name__=='__main__':unittest.main()
