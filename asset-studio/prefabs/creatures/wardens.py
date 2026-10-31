"""Large armored revenant assembled from the Studio's approved plate vocabulary."""
import json
from pathlib import Path
import numpy as np
from core import kit,hinges
from core.rig import Rig,_quat_matrix
from prefabs.anatomy import bodies
from prefabs.anatomy.bodies import Limb
from prefabs.outfits import costumes,pauldrons,armor,crowns
from items import solid,forge,executioner,shieldcraft

COLORS={'armor':[94,103,119],'armor_dark':[36,41,51],'trim':[178,142,77],
        'rivet':[189,156,93],'gem':[139,41,43],'cloth':[109,43,44],
        'leather':[76,50,36],'dark':[17,21,28],'eyes':[255,181,55],
        'steel':[132,145,163],'edge':[184,194,207],'iron':[73,82,98],
        'brass':[178,142,77],'horn':[195,170,123],'sole':[44,37,33],'mail':[69,75,83]}
MATERIALS={s:{'metallic':.42,'roughness':.48} for s in ('armor','trim','rivet','steel','edge','iron','brass')}
MATERIALS.update({s:{'metallic':0,'roughness':.9} for s in ('cloth','leather','dark','horn','sole')})
MATERIALS['mail']={'metallic':.18,'roughness':.75}
MATERIALS.update(eyes={'emissive':True},gem={'metallic':.08,'roughness':.25},armor_dark={'metallic':.2,'roughness':.65})
TORSO_SCALE=np.array([2.1,2.,1.8])
TORSO_OFFSET=np.array([0.,.229,0.])


def bound(piece,bone,group='Body'):
    piece.bone=bone;piece.mesh_group=group
    return piece


def transformed(piece,scale=(1,1,1),offset=(0,0,0)):
    scale=np.broadcast_to(scale,(3,))
    normals=None if piece.normals is None else piece.normals/scale
    if normals is not None:normals/=np.linalg.norm(normals,axis=2,keepdims=True)
    return kit.Piece(piece.verts*scale+offset,piece.faces,piece.slots,normals=normals)


def attachment_frames():
    """Studio-owned numeric mount frames verified against the original equipped rig."""
    path=Path(__file__).resolve().parents[2]/'rigs/large/attachment_frames.json'
    data=json.loads(path.read_text())
    if data.get('version')!=1:raise ValueError('Unsupported attachment frame version')
    return data['frames']


def attach(piece,rig,role,group):
    """Use the unmodified carrier axes/offsets, without preview-specific rotations."""
    spec=attachment_frames()[role]
    local=np.eye(4);local[:3,:3]=_quat_matrix(np.asarray(spec['rotation'],float))
    local[:3,3]=spec['translation']
    frame=rig.rest[spec['bone']]@local
    return bound(piece.moved(kit.Frame(frame[:3,3],*frame[:3,:3].T)),spec['bone'],group)


class HelmetHost:
    p=2.0
    def section(self,y):
        y=np.atleast_1d(y);return np.zeros(len(y)),np.full(len(y),.68),np.zeros(len(y)),np.full(len(y),.58)


def helmet():
    """Closed beveled helm shell with actual visor/vent apertures and backed glowing eyes."""
    def shell(rx,rz,lo,hi):
        angles=np.arange(12)*2*np.pi/12
        return forge.loft([np.column_stack([rx*np.cos(angles),np.full(12,y),rz*np.sin(angles)])
                             for y in (lo,hi)],'armor')
    a=solid.Assembly().add(shell(.68,.58,2.94,4.03))
    a.add(shell(.60,.50,2.91,3.95),'armor_dark');a.parts=[a.parts[0]-a.parts[1]]
    for cutter in [kit.bevel_box((.98,.14,.40),.025,'dark',(0,3.64,.57))]+[
            kit.bevel_box((.045,.24,.30),.014,'dark',(x,3.29,.57)) for x in (-.23,0,.23)]:
        a.add(cutter,'armor_dark');a.parts=[a.parts[0]-a.parts[1]]
    shell_piece=a.build()
    crown=crowns.build(HelmetHost(),3.98,4.10,.05,variant='royal',band='trim',point='trim',points=5,
                       point_scale=.95,finials=None,gems=None,front_gem=None,rivets=5,rims=False)
    backing=shell(.585,.485,2.98,3.95)
    backing.slots[:]='dark'
    eyes=[kit.bevel_box((.15,.085,.06),.01,'eyes',(x,3.64,.46)) for x in (-.23,.23)]
    return kit.Piece.merge(shell_piece,crown,backing,*eyes)


def tabard():
    """Thick faceted waistcloth behind the hip plates, with a few broad torn hems."""
    pieces=[]
    for sign in (-1,1):
        p=kit.prism([(-.27,1.40),(.27,1.40),(.28,.68),(.12,.72),(.04,.57),(-.05,.68),(-.22,.62)],
                    .045,'cloth',bevel=.014)
        p.verts[:,2]+=sign*.48
        pieces.append(p)
    return kit.Piece.merge(*pieces)


def segment(rig,start,end,radius,kind='armor',thickness=.055):
    a,b=rig.joint(start),rig.joint(end);direction=b-a;length=np.linalg.norm(direction)
    limb=Limb([(0,radius,radius),(.5,radius*.94,radius*.94),(1,radius*.84,radius*.84)],
              length,a,direction,p=2.7)
    cloth=limb.place(limb.local_mesh('cloth',sides=12,rings=9))
    plate=armor.limb_plate(limb,.12,.91,thickness,slot=kind,trim='trim',straps=1)
    return kit.Piece.merge(cloth,plate),limb


def fit_shield(piece,yaw=-12.,standoff=.27):
    """Open the board slightly at its real hand grip and lengthen its rear supports.

    Keep the leather grip vertices fixed while extending the existing metal posts
    to the board. This is an authored equipment fit; the shared hand socket frame
    stays exactly as verified against the source rig.
    """
    v=piece.verts.copy()
    blend=np.clip((v[:,2]-.065)/.11,0,1)
    blend=blend*blend*(3-2*blend)
    board=(np.abs(v[:,0])>.42)|(np.abs(v[:,1])>.11)
    blend[board]=1
    grip=np.unique(piece.faces[piece.slots=='leather'])
    blend[grip]=0
    v[:,2]+=standoff*blend
    return kit.Piece(v@kit.rotation((0,1,0),yaw).T,piece.faces,piece.slots)


def neck():
    """An actual flexible neck seated inside both cuirass and helmet.

    Blend between the chest and head carriers so turning/nodding cannot open a
    gap at either attachment. Keep the underlying volume, not a decorative ring.
    """
    angles=np.arange(12)*2*np.pi/12
    rings=[np.column_stack([rx*np.cos(angles),np.full(12,y),rz*np.sin(angles)])
           for y,rx,rz in [(2.48,.32,.29),(2.70,.29,.27),(2.92,.25,.24),(3.18,.245,.235)]]
    piece=forge.loft(rings,'mail')
    head=np.clip((piece.verts[:,1]-2.65)/.42,0,1)
    piece.skin_weights={'chest':1-head,'head':head}
    return bound(piece,'head')


def build(rig=None,recipe=None):
    rig=rig or Rig.load('large');recipe=recipe or {};parts={}
    hosts,_=bodies.build({'torso':'hero'})
    plate=costumes.knight_plate(hosts['torso'])
    plate.update(pauldrons.steel_assembly(hosts['torso'],list(plate.values()),size=.23))
    for name,p in plate.items():
        q=bound(transformed(p,TORSO_SCALE,TORSO_OFFSET),p.bone)
        q.slots=np.where(q.slots=='metal','armor',q.slots)
        if getattr(p,'hinge',None):
            q.hinge={**p.hinge,'pivot':(np.asarray(p.hinge['pivot'])*TORSO_SCALE+TORSO_OFFSET).tolist()}
        if name=='CostumeEmblem':
            center=q.verts.mean(0);q.verts=(q.verts-center)*[1.6,1.6,1]+center
        if getattr(q,'hinge',None):
            # Fit the reused cap's broad dome in the resting combat pose. Rotate
            # about its actual mounting pivot so the lug remains seated.
            turn=hinges._turn(rig.deltas('Idle_A'),q.hinge)
            pivot=np.asarray(q.hinge['pivot'])
            q.verts=(q.verts-pivot)@turn+pivot
            if q.normals is not None:q.normals=q.normals@turn
        parts[name]=q
    for name,size,center,bone in [('ChestLining',(1.34,.90,.82),(0,2.16,0),'chest'),
                                 ('WaistLining',(1.04,.48,.82),(0,1.60,0),'spine'),
                                 ('HipLining',(1.02,.42,.79),(0,1.18,0),'hips')]:
        parts[name]=bound(kit.bevel_box(size,.10,'dark',center),bone)
    head_scale=float(recipe.get('head_scale',.80))
    if not np.isfinite(head_scale) or head_scale<=0:raise ValueError('Head scale must be positive and finite')
    head=transformed(helmet(),head_scale,(0,2.94*(1-head_scale),0))
    parts['Neck']=neck()
    parts['Helmet']=bound(head,'head')
    parts['Tabard']=bound(tabard(),'hips')
    for side in ('l','r'):
        for label,start,end,radius in [('UpperArm','upperarm','lowerarm',.19),('Forearm','lowerarm','wrist',.235),
                                       ('Thigh','upperleg','lowerleg',.205),('Greave','lowerleg','foot',.24)]:
            start,end=start+'.'+side,end+'.'+side
            p,limb=segment(rig,start,end,radius)
            parts[label+'.'+side]=bound(p,start)
            if label in ('Forearm','Greave'):
                parts['JointGuard'+label+'.'+side]=bound(armor.cop(limb,0,size=.21,theta=np.pi/2,thickness=.03),start)
        hand=rig.rest['hand.'+side];slot=rig.joint('handslot.'+side)
        p=bodies.hand('fist',2.7,'armor');p.verts-=np.array([0,.27,0])
        p=p.moved(kit.Frame(slot,*hand[:3,:3].T))
        parts['Gauntlet.'+side]=bound(p,'hand.'+side)
        foot=bodies.foot('boot',2.1,'armor','sole')
        foot=foot.moved(kit.Frame(rig.joint('foot.'+side),(-1,0,0),(0,-1,0),(0,0,1)))
        parts['Boot.'+side]=bound(foot,'foot.'+side)
    parts['ExecutionerSword']=attach(executioner.executioner_sword(),rig,'sword','ExecutionerSword')
    fit=recipe.get('shield_fit',{'yaw':-12.,'standoff':.27,'fold_degrees':12.})
    shield=shieldcraft.shieldcraft({'kind':'heroic_sun','details':False,'fold_degrees':fit['fold_degrees']})
    gem=kit.gem(.16,'gem',cut='oval',depth=.055)
    gem.verts += [0,0,.243]
    shield=kit.Piece.merge(shield,gem)
    slots=np.array([{'gold':'trim','crimson':'cloth','iron':'armor','steel':'rivet','wood':'leather'}.get(str(s),str(s)) for s in shield.slots])
    shield=kit.Piece(shield.verts*1.9,shield.faces,slots)
    shield=fit_shield(shield,fit['yaw'],fit['standoff'])
    parts['Shield']=attach(shield,rig,'shield','WardenShield')
    # A hollow curved horn, with broad flared opening and fittings, hangs against the belt.
    path=np.array([[0,.30,0],[.02,.18,.015],[.07,.02,.02],[.19,-.12,.015],[.35,-.20,0]])
    radii=np.array([.055,.065,.09,.135,.19])
    horn=kit.sweep(path,kit.ring_profile(1,1,10),'horn',up=(0,0,1),scale=radii)
    # Sweep creates a capped horn here; carve its actual flared bell.
    a=solid.Assembly().add(horn)
    bell=solid.tube(path[-1]+[.09,-.05,0],path[-2],.11,10)
    a.add(bell,'dark');a.parts=[a.parts[0]-a.parts[1]]
    horn=a.build()
    for point,r in [(path[0],.068),(path[-1],.20)]:
        f=kit.Frame.make(point,(1,-.6,0),up=(0,0,1))
        angles=np.arange(12)*2*np.pi/12
        circle=np.column_stack([r*np.cos(angles),r*np.sin(angles),np.zeros(12)])
        p=kit.sweep(circle,kit.rect_profile(.035,.035,.006),'trim',up=(0,0,1),closed=True).moved(f)
        horn=kit.Piece.merge(horn,p)
    horn=transformed(horn,1,(-.73,1.33,.60))
    strap=kit.bevel_box((.10,.40,.09),.018,'leather',(-.73,1.54,.60))
    parts['WarHorn']=bound(kit.Piece.merge(horn,strap),'hips','WarHorn')
    if not recipe.get('shield',True):parts.pop('Shield')
    return parts
