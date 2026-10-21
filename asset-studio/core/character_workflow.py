"""Bound art and rigid large creatures share the character CLI, without gameplay authoring."""
import copy
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess

import numpy as np
from PIL import Image, ImageDraw
from core import creature_rigs as cr, export, mesh, publication
from core.paths import work_root

STUDIO=Path(__file__).resolve().parents[1]
FILES=('model.glb','lineup.png','lineup.gif','asset.json')
BUILDERS={'skinned_source','golem','warden'}


def local(folder,value):
    """Source inputs must stay in their authoring folder; game paths are export targets only."""
    p=(Path(folder)/value).resolve()
    if not p.is_relative_to(Path(folder).resolve()) or not p.is_file():
        raise ValueError(f'Missing or nonlocal authoring input: {value}')
    return p


def profile(character):
    name=character.recipe()['rig']
    if name=='large':
        path=STUDIO/'rigs/large/profile.json'
        p=json.loads(path.read_text())
        if p['rig_sha256']!=publication.digest(path.parent/'rig.glb'):
            raise ValueError('Large technical rig changed; verify its provenance')
        return {**p,'clips':character.recipe().get('roles',p['clips'])}
    return cr.profile(name)


def signature(character):
    """Bind review to the actual selected source, palette, profile and implementing code."""
    r=character.recipe();folder=Path(character.dir)
    paths=[Path(character.recipe_path),STUDIO/'core/character_workflow.py',STUDIO/'core/creature_rigs.py',
           STUDIO/'core/export.py',STUDIO/'core/mesh.py',STUDIO/'core/rig.py',STUDIO/'core/kit.py',STUDIO/'godot/render_creature.gd',
           STUDIO/'godot/render_armor_fittings.gd',STUDIO/'godot/item_surface_colors.gd']
    if r['builder']=='skinned_source':paths += [local(folder,r['source']),local(folder,r['surface'])]
    else:paths += [STUDIO/'prefabs/creatures/golems.py',STUDIO/'rigs/large/rig.glb',STUDIO/'core/hinges.py',STUDIO/'core/rigid_export.py']
    if r['builder']=='warden':
        paths += [STUDIO/'rigs/large/attachment_frames.json']
        paths += sorted((STUDIO/'prefabs').rglob('*.py'))
        paths += sorted((STUDIO/'items').glob('*.py'))
    if r['rig']=='large':paths += [STUDIO/'rigs/large/profile.json']
    else:paths += [STUDIO/'rigs/creatures'/r['rig']/'profile.json',STUDIO/'rigs/creatures'/r['rig']/'rig.glb']
    for name in ['provenance.json','source.blend']:
        if (folder/name).exists():paths.append(folder/name)
    values={str(p.relative_to(STUDIO)):publication.digest(p) for p in paths}
    return hashlib.sha256(json.dumps(values,sort_keys=True).encode()).hexdigest()


def _bound(character,target):
    r=character.recipe();source=local(character.dir,r['source']);surface=json.loads(local(character.dir,r['surface']).read_text())
    if surface['source_sha256']!=publication.digest(source):
        raise ValueError('Source changed; refresh named surface regions before building')
    g,b,acc=cr._doc(source);g=copy.deepcopy(g)
    slots=surface['slots'];colors={**surface['colors'],**r.get('colors',{})}
    if set(colors)!=set(slots):raise ValueError('Palette must match the named source surface slots')
    rgb=np.array([colors[s] for s in slots],float)
    if rgb.shape!=(len(slots),3) or not np.isfinite(rgb).all() or np.any((rgb<0)|(rgb>255)):
        raise ValueError('Palette values must be finite RGB colors in 0..255')
    w=export._Writer();w.g=g;w.bin=bytearray(b)
    if any(list(colors[s])!=surface['colors'][s] for s in slots):
        atlas=mesh.Atlas(rgb,looks=surface['materials'],slot_names=slots)
        if not np.allclose(atlas.u_coords,surface['u_coords']):raise ValueError('Source palette layout changed')
        png=io.BytesIO();atlas.image.save(png,'PNG')
        g['images'][surface['image']]['bufferView']=w._view(png.getvalue())
    scale=float(r.get('scale',1))
    if not np.isfinite(scale) or scale<=0:raise ValueError('Scale must be positive and finite')
    if scale!=1:
        roots=g['scenes'][g.get('scene',0)]['nodes'];children=list(roots);roots[:]=[len(g['nodes'])]
        g['nodes'].append({'name':'ArtScale','scale':[scale]*3,'children':children})
    g['asset']['generator']=export.GENERATOR
    # Default build remains byte-for-byte equal to the selected source.
    w.save(str(target))



def validate(path,p):
    """Validate skin and role contracts without assuming a humanoid node hierarchy."""
    g,_,acc=cr._doc(path)
    if not g.get('skins') or not g.get('animations'):raise ValueError('Bound art requires a skeleton and animation clips')
    names={n.get('name') for n in g['nodes']}
    if not set(p['bones'].values())<=names:raise ValueError('Art is missing required profile joints')
    clips={a['name'] for a in g['animations']}
    if not set(p['clips'].values())<=clips:raise ValueError('Art is missing required animation roles')
    tracks=None
    for a in g['animations']:
        keys={(ch['target']['node'],ch['target']['path']) for ch in a['channels']}
        if tracks is None:tracks=keys
        if tracks!=keys:raise ValueError('Animation clips need complete constant reset tracks')
        for sampler in a['samplers']:
            if not np.isfinite(acc(sampler['input'])).all() or not np.isfinite(acc(sampler['output'])).all():
                raise ValueError('Nonfinite animation keys')
    triangles=0
    for n in g['nodes']:
        if 'mesh' not in n:continue
        if 'skin' not in n:raise ValueError('Creature geometry must declare its skin')
        skin=g['skins'][n['skin']]
        if not np.isfinite(acc(skin['inverseBindMatrices'])).all():raise ValueError('Invalid inverse binds')
        for prim in g['meshes'][n['mesh']]['primitives']:
            a=prim['attributes'];v=acc(a['POSITION']);j=acc(a['JOINTS_0']);wt=acc(a['WEIGHTS_0'])
            if not np.isfinite(v).all() or not np.isfinite(wt).all() or np.any(wt<0) or not np.allclose(wt.sum(1),1,atol=1e-5):
                raise ValueError('Invalid mesh or skin weights')
            if j.min()<0 or j.max()>=len(skin['joints']):raise ValueError('Skin joint index outside skeleton')
            indices=acc(prim['indices']).ravel()
            if len(indices)%3 or indices.max()>=len(v):raise ValueError('Invalid triangle indices')
            triangles+=len(indices)//3
    bounds=cr.motion_bounds(path)
    return {'triangles':triangles,'joints':len(g['skins'][0]['joints']),'bounds':bounds,'clips':sorted(clips)}


def build(character):
    folder=work_root()/ 'characters'/character.key;folder.mkdir(parents=True,exist_ok=True)
    r=character.recipe();target=folder/'model.glb'
    if r['builder'] in {'golem','warden'}:
        from core.rigid_export import build_golem,build_warden
        (build_warden if r['builder']=='warden' else build_golem)(character,target)
    else:_bound(character,target)
    p=profile(character);info=validate(target,p)
    if info['triangles']>r.get('triangle_budget',float('inf')):
        raise ValueError('Character exceeds its authored triangle budget')
    provenance=Path(character.dir)/'provenance.json'
    info.update(key=character.key,rig=r['rig'],roles=p['clips'],loops=p['loops'],scale=r.get('scale',1),
                source_inputs=signature(character),gameplay=False,
                provenance=json.loads(provenance.read_text()) if provenance.exists() else {'kind':'procedural_geometry'})
    (folder/'asset.json').write_text(json.dumps(info,indent=2)+'\n')
    publication.stage_stamp(folder,info['source_inputs'])
    return folder


def render(character,folder):
    """GPU review uses only the staged art; the game project supplies the renderer, not art inputs."""
    p=profile(character);roles=list(p['clips'].values());frames=folder/'frames';frames.mkdir(exist_ok=True)
    info=json.loads((folder/'asset.json').read_text());job=folder/'render-job.json'
    job.write_text(json.dumps([[str(folder/'model.glb'),str(frames),roles,info['bounds']]]))
    result=subprocess.run([str(STUDIO/'godot/godot.sh'),'--path',str(STUDIO.parent),'--script',str(STUDIO/'godot/render_creature.gd'),'--',str(job)],capture_output=True,text=True,timeout=300)
    if result.returncode or 'CREATURE_REVIEW_COMPLETE' not in result.stdout:raise ValueError(result.stdout+result.stderr)
    captures=json.loads((frames/'captures.json').read_text());images=[];durations=[]
    for c in captures:
        with Image.open(frames/(c['label']+'.png')) as im:frame=im.convert('RGB')
        ImageDraw.Draw(frame).text((12,12),character.key+' | '+c['clip'],fill='white');images.append(frame)
        durations.append(max(30,round(c['duration']*1000/12)))
    images[0].save(folder/'lineup.gif',save_all=True,append_images=images[1:],duration=durations,loop=0)
    sheet=Image.new('RGB',(256*4,256*((len(roles)+3)//4)),(20,23,31))
    for i,role in enumerate(roles):sheet.paste(images[i*12+(11 if role==p['clips'].get('Death') else 4)].resize((256,256)),(i%4*256,i//4*256))
    sheet.save(folder/'lineup.png');shutil.rmtree(frames);job.unlink()
    publication.render_stamp(folder,signature(character),FILES)
    return folder


def lineup(character):
    return render(character,build(character))


def import_settings(target,p):
    """Write deliberate art import options, retaining an existing Godot UID/dependency header."""
    target=Path(str(target)+'.import')
    head='[remap]\n\nimporter="scene"\nimporter_version=1\ntype="PackedScene"\n\n'
    if target.exists():
        text=target.read_text()
        if '[params]' not in text:raise ValueError('Malformed existing Godot import metadata')
        head=text[:text.index('[params]')]
    g,_,_=cr._doc(target.with_suffix(''))
    loops={a['name']:{'settings/loop_mode':1 if a['name'] in p['loops'] else 0} for a in g['animations']}
    params='[params]\n\nnodes/root_scale=1.0\nnodes/import_as_skeleton_bones=false\nskins/use_named_skins=true\nanimation/import=true\nanimation/fps=30\nanimation/trimming=false\nanimation/remove_immutable_tracks=false\nanimation/import_rest_as_RESET=false\n'
    params+='_subresources='+json.dumps({'animations':loops},indent=2)+'\n'
    target.write_text(head+params)


def inspect_import(character):
    job=work_root()/'characters'/character.key/'import-job.json'
    p=profile(character);g,_,_=cr._doc(character.glb)
    job.write_text(json.dumps({'path':character.res,'required_bones':list(p['bones'].values()),'clips':[a['name'] for a in g['animations']],'loops':p['loops']}))
    result=subprocess.run([str(STUDIO/'godot/godot.sh'),'--headless','--path',str(STUDIO.parent),'--script',str(STUDIO/'godot/inspect_art.gd'),'--',str(job)],capture_output=True,text=True,timeout=180)
    if result.returncode or 'ART_IMPORT_OK' not in result.stdout:raise ValueError('Godot art validation failed:\n'+result.stdout+result.stderr)
    job.unlink()


def export_asset(character,no_import=False):
    """Export only finished art and metadata; failed imports restore previous target files."""
    folder=lineup(character);destination=Path(character.glb);destination.parent.mkdir(parents=True,exist_ok=True)
    targets=[destination,Path(str(destination)+'.import'),destination.with_suffix('.asset.json')]
    prior={p:p.read_bytes() if p.exists() else None for p in targets}
    try:
        temporary=destination.with_suffix('.glb.pending');shutil.copy2(folder/'model.glb',temporary);temporary.replace(destination)
        import_settings(destination,profile(character));shutil.copy2(folder/'asset.json',targets[2])
        if not no_import:
            result=subprocess.run([str(STUDIO/'godot/godot.sh'),'--headless','--path',str(STUDIO.parent),'--import'],capture_output=True,text=True,timeout=1200)
            if result.returncode:raise ValueError('Godot import failed:\n'+result.stdout+result.stderr)
            inspect_import(character)
        publication.publish(folder,STUDIO/'catalogs/characters'/character.key,character.key,signature(character),FILES,
                            {'engine_import_verified':not no_import})
    except Exception:
        for path,data in prior.items():
            if data is None:path.unlink(missing_ok=True)
            else:path.write_bytes(data)
        raise
    finally:
        destination.with_suffix('.glb.pending').unlink(missing_ok=True)
    print(f'{character.key}: {destination}'+(' (Godot validation deferred)' if no_import else ' (Godot verified)'))
    return folder
