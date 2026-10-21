"""Export rigid bone-bound creatures using a local technical rig and baked hinges."""
import copy
from pathlib import Path
import numpy as np
from core import creature_rigs as cr,export,mesh,kit,hinges
from core.rig import Rig


def build_golem(character,target):
    from prefabs.creatures import golems
    studio=Path(__file__).resolve().parents[1]
    rig_path=studio/'rigs/large/rig.glb';rig=Rig(str(rig_path));parts=golems.build(rig=rig)
    specs={name:{'parent':parent,'follow':follow,'share':share,'pivot':rig.joint(follow).tolist()}
           for name,(parent,follow,share) in golems.HELPERS.items()}
    return build_parts(character,target,rig,parts,specs,golems.COLORS)


def build_warden(character,target):
    from prefabs.creatures import wardens
    rig=Rig.load("large");parts=wardens.build(rig,character.recipe())
    return build_parts(character,target,rig,parts,hinges.specifications(parts),wardens.COLORS)


def build_parts(character,target,rig,parts,specs,palette):
    """Export rigid parts and explicit soft joints, retaining separately removable props."""
    rig_path=Path(__file__).resolve().parents[1]/"rigs/large/rig.glb"
    rw=export._Writer(str(rig_path));hinges.install(rw,rig,specs)
    for anim in rw.g['animations']:
        for s in anim['samplers']:
            s['input']=rw.copy(s['input']);s['output']=rw.copy(s['output'])
    hinges.bake(rw,rig,specs)
    # Rigid art is authored in node-rest world space, not the donor mannequin bind pose.
    worlds=np.array(mesh._node_worlds(rw.g));joints=rw.g['skins'][0]['joints']
    ibm=np.linalg.inv(worlds[joints])
    rw.g['skins'][0]['inverseBindMatrices']=rw.add(ibm.transpose(0,2,1).reshape(-1,16),'MAT4',5126)
    cr._complete_clips(rw)
    bound_rig=target.parent/'bound-rig.glb';rw.save(str(bound_rig))
    names={rw.g['nodes'][j]['name']:i for i,j in enumerate(joints)}
    merged=kit.Piece.merge(*parts.values());surface=target.parent/'surface.glb'
    colors={**palette,**character.recipe().get('colors',{})}
    export.export_static(str(surface),character.title,merged,colors,
                         materials=character.recipe().get("materials"),
                         crease=float(character.recipe().get("crease",50)))
    joint_rows=[];weight_rows=[]
    for bone,piece in parts.items():
        influence=getattr(piece,'skin_weights',None)
        js=np.zeros((len(piece.verts),4),int);wt=np.zeros_like(js,float)
        if influence is None:
            js[:,0]=names[getattr(piece,'bone',bone)];wt[:,0]=1
        else:
            if not 1<=len(influence)<=4:raise ValueError('Parts require one to four skin influences')
            for k,(name,values) in enumerate(influence.items()):
                values=np.asarray(values,float)
                if name not in names or values.shape!=(len(piece.verts),):raise ValueError('Invalid part skin influence')
                js[:,k]=names[name];wt[:,k]=values
            if not np.isfinite(wt).all() or np.any(wt<0) or not np.allclose(wt.sum(1),1):
                raise ValueError('Part skin weights must be finite, nonnegative and sum to one')
        joint_rows.append(js);weight_rows.append(wt)
    js=np.vstack(joint_rows);wt=np.vstack(weight_rows)
    sg,sb,_=cr._doc(surface);rg,rb,_=cr._doc(bound_rig)
    w=export._Writer();w.g=copy.deepcopy(sg);w.bin=bytearray(sb)
    am=cr._data(w,rg,rb);offset=len(w.g['nodes'])
    for n in copy.deepcopy(rg['nodes']):
        if 'children' in n:n['children']=[i+offset for i in n['children']]
        w.g['nodes'].append(n)
    w.g['scenes'][0]['nodes'] += [i+offset for i in rg['scenes'][rg.get('scene',0)]['nodes']]
    w.g['skins']=copy.deepcopy(rg['skins'])
    for s in w.g['skins']:
        s['joints']=[i+offset for i in s['joints']];s['inverseBindMatrices']=am[s['inverseBindMatrices']]
    w.g['animations']=copy.deepcopy(rg['animations']);cr._remap_clips(w.g['animations'],am,offset)
    w.g['nodes'][0]['skin']=0
    ja=w.add(js[merged.faces].reshape(-1,4),'VEC4',5123,34962);wa=w.add(wt[merged.faces].reshape(-1,4),'VEC4',5126,34962)
    for prim in w.g['meshes'][0]['primitives']:
        prim['attributes']['JOINTS_0']=ja;prim['attributes']['WEIGHTS_0']=wa
    face_groups=np.concatenate([np.full(len(p.faces),getattr(p,"mesh_group","Body"),object) for p in parts.values()])
    default_primitives=w.g['meshes'][0]['primitives'];w.g['meshes']=[]
    _,_,surface_acc=cr._doc(surface)
    for i,group in enumerate(dict.fromkeys(face_groups)):
        prims=[]
        for original in default_primitives:
            # Original indices still refer to the static surface accessors.
            indices=surface_acc(original['indices']).reshape(-1,3)
            chosen=indices[face_groups[indices[:,0]//3]==group]
            if not len(chosen):continue
            prim=copy.deepcopy(original);prim['indices']=w.add(chosen.ravel(),'SCALAR',5125,34963);prims.append(prim)
        w.g['meshes'].append({'name':group,'primitives':prims})
        if i==0:w.g['nodes'][0].update(name=group,mesh=i)
        else:
            index=len(w.g['nodes']);w.g['nodes'].append({'name':group,'mesh':i,'skin':0})
            w.g['scenes'][0]['nodes'].append(index)
    scale=float(character.recipe().get('scale',1))
    if not np.isfinite(scale) or scale<=0:raise ValueError('Scale must be positive and finite')
    if scale!=1:
        roots=w.g['scenes'][0]['nodes'];children=list(roots);roots[:]=[len(w.g['nodes'])]
        w.g['nodes'].append({'name':'ArtScale','scale':[scale]*3,'children':children})
    w.save(str(target));surface.unlink();bound_rig.unlink()
