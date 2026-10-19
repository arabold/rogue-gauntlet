"""Original heroic, royal and monster shields with reusable raised heraldry.

Color quarters partition the existing face; they are never overlaid paint meshes.
Scales and emblems have real thickness and overlap their structural supports.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, polygon, tube, welded
from prefabs.details.heraldry import crown, lily, scale_plate, bound_horn

HEROIC_KINDS=('heroic_quarters','heroic_sun','wood_simple','dragon_long','iron_tower','royal_crown','royal_lily')
COLORS={'azure':[36,111,177],'ivory':[218,211,184],'crimson':[151,42,43],
        'purple':[92,47,123],'dragon':[39,96,75],'dragon_light':[48,111,84],
        'dragon_dark':[30,75,61],'gold':[181,133,51],'pale_wood':[156,111,62],'dragon_frame':[82,65,49],'dragon_rivet':[143,124,94]}
MATERIALS={name:{'metallic':0,'roughness':.88,'crease':52} for name in COLORS}
MATERIALS['dragon_rivet']={'metallic':.45,'roughness':.42,'crease':40}
MATERIALS['dragon_frame']={'metallic':.30,'roughness':.58,'crease':45}
MATERIALS['gold']={'metallic':.50,'roughness':.36,'crease':45}
MATERIALS.update({name:{'metallic':.12,'roughness':.64,'crease':48} for name in ['dragon','dragon_light','dragon_dark']})


def outline_for(kind):
    if kind=='iron_tower':return np.array([(-.30,.88),(.30,.88),(.36,.80),(.36,-.80),(.29,-.88),(-.29,-.88),(-.36,-.80),(-.36,.80)])
    if kind=='dragon_long':return np.array([(0,.66),(.19,.75),(.25,.85),(.32,.66),(.34,.34),(.29,-.25),(.18,-.65),(0,-.97),(-.18,-.65),(-.29,-.25),(-.34,.34),(-.32,.66),(-.25,.85),(-.19,.75)])
    if kind=='wood_simple':return np.array([(-.31,.39),(.32,.44),(.34,-.22),(.22,-.39),(-.02,-.48),(-.26,-.38),(-.34,-.22)])
    if kind=='royal_crown':return np.array([(0,.68),(.38,.53),(.40,.28),(.32,-.18),(.16,-.56),(0,-.77),(-.16,-.56),(-.32,-.18),(-.40,.28),(-.38,.53)])
    if kind=='royal_lily':return np.array([(0,.63),(.24,.59),(.43,.42),(.36,.19),(.43,-.03),(.29,-.42),(0,-.61),(-.29,-.42),(-.43,-.03),(-.36,.19),(-.43,.42),(-.24,.59)])
    return np.array([(-.42,.44),(-.17,.48),(0,.54),(.17,.48),(.42,.44),(.37,.17),(.43,-.02),(.30,-.32),(0,-.62),(-.30,-.32),(-.43,-.02),(-.37,.17)])


def heroic_shield(design):
    from items.shieldcraft import section,extrude,finish,fastener,rear_grip,apply_wear
    from prefabs.details.construction import timber_plank
    kind=design['kind'];outline=outline_for(kind);outer=section(outline);inner=outer.offset(-.048)
    a=Assembly()
    if kind=='heroic_quarters':
        # The four volumes exactly partition one plate, with identical face Z.
        for sx,sy,slot in [(-1,1,'azure'),(1,1,'ivory'),(-1,-1,'ivory'),(1,-1,'azure')]:
            region=outer.offset(-.020) ^ section([(0,0),(sx,0),(sx,sy),(0,sy)])
            if not region.is_empty():a.add(extrude(region,.075),slot)
    elif kind=='wood_simple':
        a.add(extrude(outer,.045,-.014),'wood')
        clip=extrude(outer.offset(-.008),.25)
        for i,x in enumerate([-.27,-.135,0,.135,.27]):
            board=timber_plank(.132,1.1,.055,.004,i*.8);board.verts += [x,0,.022]
            v,f=welded(board.verts,board.faces)
            part=mf.Manifold(mf.Mesh(np.asarray(v,np.float32),np.asarray(f,np.uint32))) ^ clip
            if not part.is_empty():a.add(part,'pale_wood' if i==1 else 'wood')
        a.parts=[apply_wear(mf.Manifold.batch_boolean(a.parts,mf.OpType.Add),a,'timber',True)]
        # The rear cross braces and handle are wood joinery, not metal fasteners.
        for y in [-.23,.22]:a.add(kit.bevel_box((.57,.073,.052),.01,'wood',center=(0,y,-.040)),'wood')
        for x in [-.22,.22]:
            for y in [-.23,.22]:fastener(a,x,y,.057,'wood_dark',.023)
        for x in [-.13,.13]:a.add(tube((x,0,-.025),(x,0,-.15),.032,10),'wood')
        a.add(tube((-.15,0,-.15),(.15,0,-.15),.034,12),'wood_dark')
        return finish(a,mf.Manifold.batch_boolean(a.parts,mf.OpType.Add),'v',outline,design.get('fold_degrees',22))
    elif kind=='dragon_long':
        a.add(extrude(outer,.08),'leather')
        # Seven staggered rows; broad scales overlap both the backing and neighbors.
        clip=extrude(outer.offset(-.025),.3,.055)
        for row,y in enumerate(np.linspace(-.66,.66,7)):
            for col,x in enumerate(np.arange(-.24,.25,.16)+(row%2)*.08):
                scale=scale_plate(.22,.31);scale.verts += [x,y,.028+row*.001]
                v,f=welded(scale.verts,scale.faces)
                part=mf.Manifold(mf.Mesh(np.asarray(v,np.float32),np.asarray(f,np.uint32))) ^ clip
                if not part.is_empty():a.add(part,['dragon','dragon_light','dragon_dark'][(row+col)%3])
        # The approved reference has two substantial shoulder horns and metal
        # mounts. Bindings are real solid collars around the bone, not paint.
        from prefabs.details.construction import rim_clamp,horn_socket
        a.add(extrude(outer-inner,.115),'dragon_frame')
        for sign in [-1,1]:
            part=bound_horn(.108)
            part.verts[:,0]*=sign
            if sign<0:part.faces=part.faces[:,::-1]
            part.verts += [sign*.255,.70,.035]
            a.add(part)
            # A shaped metal boot follows the actual horn-root axis and
            # transfers into a broad C clamp wrapping the shield front/back.
            axis=np.array([sign*.15,.11,.025]);axis/=np.linalg.norm(axis)
            front=np.array([0.,0.,1.]);front-=axis*np.dot(front,axis);front/=np.linalg.norm(front)
            across=np.cross(axis,front)
            socket=horn_socket(.13,.15,'dragon_frame').moved(kit.Frame((sign*.255,.70,.035),across,axis,front))
            a.add(socket,'dragon_frame')
            mount=rim_clamp(.22,.23,.22,'dragon_frame')
            mount.verts[:,0]*=sign
            if sign<0:mount.faces=mount.faces[:,::-1]
            mount.verts += [sign*.285,.66,0]
            a.add(mount,'dragon_frame')
            bolt=kit.stud(.045,.030,'dragon_rivet',6)
            bolt.verts += [sign*.235,.59,.096]
            a.add(bolt,'dragon_rivet')
            for x,y in [(.325,.20),(.235,-.45)]:
                clamp=rim_clamp(.19,.17,.20,'dragon_frame')
                clamp.verts[:,0]*=sign
                if sign<0:clamp.faces=clamp.faces[:,::-1]
                clamp.verts += [sign*x,y,0]
                a.add(clamp,'dragon_frame')
                bolt=kit.stud(.038,.026,'dragon_rivet',6)
                bolt.verts += [sign*(x-.035),y,.088]
                a.add(bolt,'dragon_rivet')
        tip=rim_clamp(.16,.16,.20,'dragon_frame')
        theta=-np.pi/2
        rotation=np.array([[np.cos(theta),-np.sin(theta),0],[np.sin(theta),np.cos(theta),0],[0,0,1]])
        tip.verts=tip.verts @ rotation.T+[0,-.905,0]
        a.add(tip,'dragon_frame')
        bolt=kit.stud(.035,.026,'dragon_rivet',6)
        bolt.verts += [0,-.875,.088]
        a.add(bolt,'dragon_rivet')

    else:
        body_slot={'heroic_sun':'crimson','iron_tower':'wood_dark','royal_crown':'azure','royal_lily':'purple'}[kind]
        a.add(extrude(outer.offset(-.020),.075),body_slot)
    if kind!='dragon_long':
        a.add(extrude(outer-inner,.115),'iron' if kind in ('heroic_quarters','iron_tower') else 'gold')
    if kind=='heroic_sun':
        theta=np.arange(24)*np.pi/12
        radius=np.where(np.arange(24)%2,.135,.215)
        star=np.column_stack([np.cos(theta)*radius,np.sin(theta)*radius])
        a.add(extrude(section(star),.030,.043),'gold')
        a.add(mf.Manifold.sphere(1,16).scale((.094,.094,.034)).translate((0,0,.060)),'gold')
    elif kind=='royal_crown':
        emblem=crown(.42);emblem.verts += [0,.09,.043];a.add(emblem,'gold')
        # A heraldic chevron is a forged appliqué rather than painted strips.
        a.add(polygon([(-.29,-.16),(0,-.39),(.29,-.16),(.27,-.09),(0,-.30),(-.27,-.09)],.025).translate((0,0,.043)),'gold')
    elif kind=='royal_lily':
        emblem=lily(.63);emblem.verts += [0,.03,.043];a.add(emblem,'gold')
    elif kind=='iron_tower':
        for y in [-.53,0,.53]:
            band=polygon([(-.32,y-.07),(.32,y-.07),(.32,y+.06),(-.32,y+.08)],.045).translate((0,0,.047))
            a.add(band,'iron')
            for x in [-.17,.17]:a.add(kit.stud(.060,.061,'steel',4).moved(kit.Frame((x,y,.061),[1,0,0],[0,1,0],[0,0,1])),'steel')
    if kind!='dragon_long':
        for i,(x,y) in enumerate(outline*.94):
            if i%2==0:fastener(a,float(x),float(y),.059,'steel' if kind=='heroic_quarters' else 'gold' if kind!='iron_tower' else 'iron',.021)
    rear_grip(a)
    flat=mf.Manifold.batch_boolean(a.parts,mf.OpType.Add)
    if kind=='dragon_long':
        # Cast boots and clamp jaws can seal tiny air pockets against the rim.
        # Keep the sole exterior shell, filling these unintended enclosed voids
        # without changing its visible surface or joining disconnected bodies.
        exteriors=[part for part in flat.decompose() if part.volume()>0]
        if len(exteriors)!=1:raise ValueError('Dragon fittings must join the shield')
        flat=exteriors[0]
    return finish(a,flat,'rectangle' if kind=='iron_tower' else 'v',outline,design.get('fold_degrees',22))
