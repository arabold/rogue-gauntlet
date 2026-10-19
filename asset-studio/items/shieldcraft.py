"""Character-ready shields: shallow V bends, flat discs and curved scuta.

Profiles, rims, grooves, bosses and rear grips are real joined solids. Material
colors live on their surfaces. Bone-themed shields have wooden backing, a bone
frame and a carved skull; bone is decoration rather than structural sheet metal.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, polygon, tube, lathe, welded, conform_surface
from prefabs.details.construction import timber_plank, repair_plate, corner_bracket
from prefabs.details.wear import solid_gash

BASE_KINDS = ('knight', 'bone_kite', 'barbarian', 'spiked_buckler', 'guard_scutum', 'paladin')
from items.heroic_shields import HEROIC_KINDS, COLORS as HERO_COLORS, MATERIALS as HERO_MATERIALS
KINDS = BASE_KINDS + HEROIC_KINDS
COLORS = {'steel': [137,155,168], 'iron': [64,75,82], 'wood': [120,77,43],
          'bone': [211,192,151], 'leather': [75,47,31], 'brass': [173,128,49], 'dark': [43,36,31],
          'wood_light': [125,81,46], 'wood_dark': [114,73,42], 'wood_cut': [91,58,33], 'wood_repair': [155,112,65]}
MATERIALS = {name: {'metallic': .55, 'roughness': .40, 'crease': 45}
             for name in ('steel','iron','brass')}
MATERIALS.update({name: {'metallic': 0, 'roughness': .92, 'crease': 52}
                  for name in ('wood','wood_light','wood_dark','wood_cut','wood_repair','bone','leather','dark')})
COLORS.update(HERO_COLORS)
MATERIALS.update(HERO_MATERIALS)


def silhouette(shape):
    if shape == 'round':
        theta=np.arange(24)*2*np.pi/24
        return np.column_stack([np.cos(theta)*.43,np.sin(theta)*.43])
    if shape == 'rectangle':
        return np.array([(-.38,.64),(.38,.64),(.45,.56),(.45,-.56),(.38,-.64),(-.38,-.64),(-.45,-.56),(-.45,.56)])
    if shape == 'kite':
        return np.array([(0,.56),(.38,.44),(.34,-.11),(.20,-.43),(0,-.70),(-.20,-.43),(-.34,-.11),(-.38,.44)])
    return np.array([(0,.52),(.42,.40),(.35,-.17),(.20,-.40),(0,-.58),(-.20,-.40),(-.35,-.17),(-.42,.40)])


def section(points):
    p=np.asarray(points,float)
    if np.sum(p[:,0]*np.roll(p[:,1],-1)-p[:,1]*np.roll(p[:,0],-1))<0:p=p[::-1]
    return mf.CrossSection([p])


def extrude(cross,depth,z=0):
    return mf.Manifold.extrude(cross,depth).translate((0,0,z-depth/2))


def fastener(a,x,y,z=.073,slot='steel',r=.025):
    a.add(mf.Manifold.sphere(r,12).scale((1,1,.58)).translate((x,y,z)),slot)


def skull(a):
    """Carved stylized skull with recessed eyes/nose and separated jaw teeth."""
    head=mf.Manifold.sphere(1,16).scale((.126,.148,.084)).translate((0,.025,.114))
    jaw=mf.Manifold.cube((.162,.090,.105),center=True).translate((0,-.103,.119))
    shape=head+ jaw
    eyes=[mf.Manifold.sphere(1,12).scale((.044,.048,.048)).translate((x,.034,.177)) for x in [-.050,.050]]
    for eye in eyes:shape=shape-eye
    nose=polygon([(-.022,-.071),(.022,-.071),(0,-.021)],.09).translate((0,0,.174))
    shape=shape-nose
    for x in [-.053,0,.053]:
        shape=shape-mf.Manifold.cube((.009,.032,.055),center=True).translate((x,-.137,.165))
    mesh=shape.to_mesh();p=np.array(mesh.vert_properties[:,:3],copy=True);f=np.asarray(mesh.tri_verts)
    c=p[f].mean(1);ink=np.zeros(len(f),bool)
    for x in [-.050,.050]:
        d=(c-[x,.034,.177])/[.044,.048,.048]
        ink |= ((d*d).sum(1)<1.08)&(c[:,2]>.125)
    ink |= (np.abs(c[:,0])<.025)&(c[:,1]>-.078)&(c[:,1]<-.018)&(c[:,2]>.125)
    a.add(kit.Piece(p,f,np.where(ink,'dark','bone').astype(object)))


def rear_grip(a):
    """Two real rear standoffs and a horizontal grip centered at the origin after placement."""
    for x in [-.14,.14]:
        a.add(tube((x,0,-.025),(x,0,-.16),.031,10),'iron')
        a.add(mf.Manifold.sphere(.050,12).scale((1,1,.55)).translate((x,0,-.029)),'iron')
    a.add(tube((-.16,0,-.15),(.16,0,-.15),.032,12),'leather')
    # Back brace joins the grip supports to the structural body.
    a.add(extrude(section([(-.21,-.045),(.21,-.045),(.21,.045),(-.21,.045)]),.025,-.041),'iron')


def apply_wear(shape,assembly,kind,wood):
    """Subtract deliberate damage with material colors on the new cavity faces."""
    def subtract(cutter,slot):
        nonlocal shape
        holder=Assembly()
        # Use the existing assembly's labels so boolean provenance stays aligned.
        holder.slots=assembly.slots
        holder.add(cutter,slot)
        shape=shape-holder.parts[-1]

    def cut(x,y,length,width,depth,angle,z=.042,slot='iron'):
        cutter=solid_gash(length,width,depth)
        t=np.deg2rad(angle)
        rotation=np.array([[np.cos(t),-np.sin(t),0],[np.sin(t),np.cos(t),0],[0,0,1]])
        cutter.verts=cutter.verts @ rotation.T+[x,y,z]
        subtract(cutter,slot)

    if wood:
        # Long splits follow the boards. A few branching cuts and one knot are
        # readable at game scale; this is shaped timber, not high-frequency noise.
        for x,y,length,angle in [(-.205,.26,.30,3),(.085,.38,.21,-4),(.235,-.22,.25,5),(-.075,-.36,.18,-8)]:
            cut(x,y,length,.015,.022,angle,.050,'wood_cut')
        cut(-.205,.31,.14,.011,.018,-18,.050,'wood_cut')
        subtract(mf.Manifold.sphere(1,12).scale((.023,.040,.028)).translate((-.21,-.22,.071)),'wood_cut')
        if kind=='barbarian':cut(-.22,-.12,.23,.033,.023,-42,.050,'wood_cut')
    elif kind!='bone_kite':
        wear={'knight':[(.20,.23,.24,.027,.017,-32),(-.16,-.28,.16,.020,.014,-32)],
              'spiked_buckler':[(.18,.20,.25,.032,.021,-35),(-.22,-.18,.22,.029,.020,28),(.15,-.23,.13,.024,.017,-40)],
              'paladin':[(.19,-.29,.16,.019,.011,-30)]}[kind]
        for x,y,length,width,depth,angle in wear:cut(x,y,length,width,depth,angle)
        for x,y,r in ([(.22,-.14,.067),(-.18,.23,.050)] if kind=='spiked_buckler' else [(-.19,.24,.042)]):
            subtract(mf.Manifold.sphere(1,12).scale((r,r*.80,.033)).translate((x,y,.063)),'iron')
    # Small edge bites expose real rim thickness, never cut through its backing.
    if kind in ('knight','spiked_buckler'):
        x,y=(.36,.29) if kind=='knight' else (.31,.30)
        subtract(mf.Manifold.sphere(1,10).scale((.045,.032,.038)).translate((x,y,.075)),'iron')
    if kind=='bone_kite':
        cut(-.30,.25,.11,.014,.014,-22,.086,'bone')
    return shape


def shieldcraft(design):
    """Build one curated shield combination; materials/shape are explicit design decisions."""
    kind=design.get('kind')
    if kind not in KINDS:raise ValueError(f'Unknown shieldcraft design {kind}')
    if kind in HEROIC_KINDS:
        from items.heroic_shields import heroic_shield
        return heroic_shield(design)
    shape={'knight':'v','bone_kite':'kite','barbarian':'round','spiked_buckler':'round',
           'guard_scutum':'rectangle','paladin':'rectangle'}[kind]
    wood=kind in ('bone_kite','barbarian','guard_scutum')
    bone=kind=='bone_kite'
    outline=silhouette(shape);outer=section(outline);inner=outer.offset(-.055)
    detail=design.get('details',True)
    if wood and detail:
        # A continuous rear backing supports individually shaped, beveled boards.
        a=Assembly().add(extrude(outer.offset(-.020),.045,-.015),'wood')
        clip=extrude(outer.offset(-.026),.30,.025)
        for i,x in enumerate(np.arange(-.42,.43,.14)):
            board=timber_plank(width=.133,length=1.6,thickness=.055,bow=.004,phase=i*1.7)
            board.verts += [x,0,.021+(.003 if i%3==1 else -.002 if i%3==2 else 0)]
            v,f=welded(board.verts,board.faces)
            shape_board=mf.Manifold(mf.Mesh(np.asarray(v,np.float32),np.asarray(f,np.uint32))) ^ clip
            if not shape_board.is_empty():a.add(shape_board,'wood_repair' if kind=='guard_scutum' and i==4 else ['wood','wood_light','wood_dark'][i%3])
    else:
        a=Assembly().add(extrude(outer.offset(-.020),.075),'wood' if wood else 'steel')
    rim_slot='bone' if bone else 'brass' if kind=='paladin' else 'iron'
    if wood and detail:
        # Wear the timber before attaching hardware, so a wood knot cannot
        # accidentally drill through a later repair plate or its rivets.
        a.parts=[apply_wear(mf.Manifold.batch_boolean(a.parts,mf.OpType.Add),a,'timber',True)]
    if shape=='round':
        # Continuous chamfered ring avoids sharp, sliver-prone rim/body seams.
        profile=[(.430,-.046),(.418,-.058),(.377,-.058),(.365,-.046),
                 (.365,.046),(.377,.058),(.418,.058),(.430,.046)]
        a.add(mf.Manifold.revolve(section(profile),24),rim_slot)
    else:
        a.add(extrude(outer-inner,.116),rim_slot)
    if bone:
        # Rounded bone segments stand proud of the wooden backing and overlap
        # at their joints; dark bindings hold the frame to the wood.
        center=outline.mean(0)
        path=outline*.92+center*.08
        for start,end in zip(path,np.roll(path,-1,axis=0)):
            a.add(kit.sweep([(*start,.065),(*(start*.70+end*.30),.065),
                            (*(start*.30+end*.70),.065),(*end,.065)],
                            kit.ring_profile(.032,.032,10),'bone',scale=[1.3,.82,.82,1.3]),'bone')
        for x,y in path:
            a.add(mf.Manifold.sphere(1,12).scale((.043,.045,.036)).translate((x,y,.065)),'bone')
        for i in [1,2,3,5,6,7]:
            x,y=path[i]
            a.add(mf.Manifold.cube((.075,.075,.10),center=True).translate((x,y,.055)),'leather')
            fastener(a,x,y,.113,'iron',.017)
        skull(a)
    else:
        # Low forged boss, solidly seated in a flange.
        flange=lathe([(.13,0),(.145,.014),(.145,.032),(.12,.044)],16).rotate((90,0,0)).translate((0,0,.031))
        a.add(flange,'brass' if kind=='paladin' else 'iron')
        a.add(mf.Manifold.sphere(1,16).scale((.115,.115,.070)).translate((0,0,.080)),'steel')
        if kind in ('knight','spiked_buckler'):
            # Actual short pyramidal spike, buried in its supporting boss.
            spike=lathe([(.073,.10),(.073,.13),(.040,.20),(.002,.30)],6).rotate((90,0,0))
            a.add(spike,'steel')
        if kind=='paladin':
            # Broad structural cross ribs, fused to the face and frame.
            a.add(extrude(section([(-.032,-.59),(.032,-.59),(.032,.59),(-.032,.59)]),.026,.047),'brass')
            a.add(extrude(section([(-.40,-.03),(.40,-.03),(.40,.03),(-.40,.03)]),.026,.047),'brass')
        if kind=='spiked_buckler':
            for theta in np.arange(6)*np.pi/3:
                x,y=np.cos(theta)*.355,np.sin(theta)*.355
                stud=lathe([(.034,.052),(.034,.076),(.002,.14)],6).rotate((90,0,0)).translate((x,y,0))
                a.add(stud,'steel')
        # Rim rivets follow the outline; no fasteners hover outside its body.
        for x,y in outline*.94:
            if shape=='round' and int(round(np.arctan2(y,x)*12/np.pi))%3:continue
            fastener(a,float(x),float(y),.059,'brass' if kind=='paladin' else 'steel',.021)
        for theta in np.arange(4)*np.pi/2+np.pi/4:
            fastener(a,np.cos(theta)*.115,np.sin(theta)*.115,.091,'steel',.018)
    if detail:
        # Forged plates and existing character studs become structural repairs.
        if kind=='barbarian':
            patch=repair_plate(.27,.12,.025,'iron')
            patch.verts += [.13,-.29,.042]
            a.add(patch,'iron')
            for x,y in [(.045,-.29),(.215,-.29)]:
                a.add(kit.stud(.025,.016,'steel').moved(kit.Frame((x,y,.061),[1,0,0],[0,1,0],[0,0,1])),'steel')
        if kind=='paladin':
            for x,y,angle in [(-.36,.55,180),(.36,.55,-90),(-.36,-.55,90),(.36,-.55,0)]:
                bracket=corner_bracket(.14,.028,'brass')
                theta=np.deg2rad(angle)
                rotation=np.array([[np.cos(theta),-np.sin(theta),0],[np.sin(theta),np.cos(theta),0],[0,0,1]])
                bracket.verts=bracket.verts @ rotation.T+[x,y,.057]
                a.add(bracket,'brass')
    rear_grip(a)
    flat=mf.Manifold.batch_boolean(a.parts,mf.OpType.Add)
    if detail and (not wood or bone):
        flat=apply_wear(flat,a,kind,False)
    return finish(a,flat,shape,outline,design.get('fold_degrees',22))


def finish(a,flat,shape,outline,fold_degrees=22):
    """Bend an already joined shield, retaining crease topology and grip origin."""
    half=float(np.abs(outline[:,0]).max())
    if not 5 <= float(fold_degrees) <= 35:
        raise ValueError('Shield panel fold must be between 5 and 35 degrees per half')
    bend=half*np.tan(np.deg2rad(fold_degrees)) if shape in ('v','kite') else .175 if shape=='rectangle' else 0.
    if shape in ('v','kite'):
        # Split at the ridge before bending, retaining an exact shared crease.
        halves=flat.split_by_plane((1,0,0),0)
        # Discard only the temporary plane caps, then join their identical
        # boundary loops directly. Re-unioning the bent caps creates numerical
        # internal slivers along the material seams of the skull and frame.
        pieces=[]
        for part in halves:
            mesh=part.to_mesh();v=np.array(mesh.vert_properties[:,:3],copy=True);f=np.asarray(mesh.tri_verts)
            v[np.abs(v[:,0])<1e-7,0]=0
            keep=~np.all(np.abs(v[f,0])<1e-7,axis=1)
            labels=np.rint(mesh.vert_properties[:,3][f].mean(1)).astype(int)
            v[:,2]-=bend*np.abs(v[:,0])/half
            pieces.append(kit.Piece(v,f[keep],np.array(a.slots,object)[labels[keep]]))
        merged=kit.Piece.merge(*pieces)
        result=flat
    elif shape=='rectangle':
        # Shared refinement provides enough across-width samples for the arc,
        # including both faces, rim and fittings. It preserves welded topology.
        radius=(half*half+bend*bend)/(2*bend)
        result=flat.refine_to_length(.16).warp(lambda p:[p[0],p[1],p[2]+np.sqrt(max(0,radius*radius-p[0]*p[0]))-radius])
    else:result=flat
    if result.status()!=mf.Error.NoError or sum(c.volume()>0 for c in result.decompose())!=1:
        raise ValueError('Shield has invalid or disconnected construction')
    if shape in ('v','kite'):
        p,f=merged.verts,merged.faces
        slots=merged.slots
    else:
        mesh=result.to_mesh();p=np.array(mesh.vert_properties[:,:3],copy=True);f=np.asarray(mesh.tri_verts)
        labels=np.rint(mesh.vert_properties[:,3][f].mean(1)).astype(int)
        slots=np.array(a.slots,object)[labels]
    # Both curvatures are zero at X=0: shift the rear hand grip onto the origin.
    p[:,2]+=.15
    p,f=welded(p,f)
    keep=(f[:,0]!=f[:,1])&(f[:,1]!=f[:,2])&(f[:,0]!=f[:,2])
    piece=conform_surface(kit.Piece(p,f[keep],slots[keep]))
    if shape in ('v','kite'):
        # Preserve the authored ridge without faceting bosses, skulls or grips.
        # Other corners retain the same crease-aware character normals.
        from core.export import corner_normals
        creases={slot:spec.get('crease',52) for slot,spec in MATERIALS.items()}
        normals=corner_normals(piece.verts,piece.faces,slots=piece.slots,slot_crease=creases)
        tri=piece.verts[piece.faces]
        flat_z=tri[:,:,2]-.15+bend*np.abs(tri[:,:,0])/half
        ridge=(np.abs(tri[:,:,0])<1e-6)&(flat_z>-.065)&(flat_z<.065)
        side=tri[:,:,0].mean(1)>=0
        for selected in [side,~side]:
            panel=corner_normals(piece.verts,piece.faces[selected],slots=piece.slots[selected],slot_crease=creases)
            normals[selected]=np.where(ridge[selected,:,None],panel,normals[selected])
        piece.normals=normals
    return piece
