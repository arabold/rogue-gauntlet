"""Report disposable authoring files and make remaining game-art inputs explicit."""
import json
from pathlib import Path
from core import mesh

STUDIO=Path(__file__).resolve().parents[1]


def audit(studio=STUDIO):
    studio=Path(studio);issues=[];legacy=[]
    for p in studio.rglob('*'):
        if p.is_file() and (p.suffix in {'.pyc','.tmp','.bak'} or '.blend' in p.name and p.name.split('.blend')[-1].isdigit()):
            issues.append(str(p.relative_to(studio)))
        if p.is_dir() and p.name in {'frames','__pycache__'}:issues.append(str(p.relative_to(studio)))
    technical=[studio/'rigs/rig_medium.glb',studio/'rigs/large/rig.glb',*(studio/'rigs/creatures').glob('*/rig.glb')]
    for p in technical:
        if not p.exists():continue
        g,_,_=mesh.read_glb(str(p))
        if g.get('meshes') or g.get('images'):issues.append(str(p.relative_to(studio))+': technical rig contains appearance data')
    for p in (studio/'rigs/templates').glob('*.glb'):
        g,_,acc=mesh.read_glb(str(p))
        if g.get('images') or any(len(acc(prim['indices']))>3 for m in g.get('meshes',[]) for prim in m['primitives']):
            issues.append(str(p.relative_to(studio))+': template contains more than attachment markers')
    for p in (studio/'characters').glob('*/*/character.json'):
        r=json.loads(p.read_text())
        if any(isinstance(v,str) and v.startswith('res://scenes/') for v in r.get('weapons',{}).values()):
            legacy.append(str(p.relative_to(studio)))
    return {'issues':sorted(set(issues)),'legacy_weapon_inputs':sorted(legacy)}
