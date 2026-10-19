"""Primitive leather and royal orc armor fitted to the shared costume rig and shading."""
import numpy as np
from core import kit
from items import solid
from prefabs.outfits import armor,belts,clothes,cuirass,pauldrons
from prefabs.details.primal import fur_edge,horned_skull
from prefabs.details.heraldry import horn
from prefabs.details.construction import horn_socket
from prefabs.details.wear import solid_gash


def _joined(*pieces):
    """Require actual support contact when combining rigid decorative geometry."""
    return solid.fuse_closed(kit.Piece.merge(*pieces))


def _studs(plate,locations,radius=.016,slot="rivet"):
    from prefabs.outfits.costumes import _mesh_mount
    return [cuirass._rivet(point-.004*normal,normal,radius,slot)
            for point,normal in (_mesh_mount(plate,plate.surface_at,u,t) for u,t in locations)]


def _skirt(torso,parts,belt_y,metal):
    """A fitted short hide skirt with hip-mounted wraparound armor and connected fur hem."""
    from prefabs.outfits.costumes import WaistHost,BeltHost,_bound
    host=WaistHost(torso)
    skirt=clothes.loincloth(host,belt_y+.035,hem=.425 if metal else .440,
                            flare=.025,ragged=12,jag=.025,dip=.015,
                            thickness=.026,slot="cloth" if metal else "hide")
    parts["CostumeHideSkirt"]=_bound(skirt,"hips")
    if not metal:
        theta=np.arange(64)*2*np.pi/64
        path=kit.ring_points(host,.444,.026,64,theta)
        outward=np.column_stack([np.cos(theta),np.zeros(64),np.sin(theta)])
        fur=fur_edge(path,outward,length=.055,thickness=.035)
        # The upper roots tuck into the actual hem, preserving the skirt's ragged edge.
        parts["CostumeHideSkirt"]=_bound(_joined(skirt,fur),"hips")
    for side,azimuth in (("Left",0),("Right",180)):
        layers=2 if metal else 1
        for i in range(layers):
            panel=cuirass.plate(host,belt_y-.205-i*.018,belt_y-.020-i*.085,
                azimuth=azimuth,width=74,width_top=70,armhole=0,
                thickness=.038 if metal else .030,depth=.034 if metal else .024,
                rows=4,cols=15,rim=.020 if metal else .015,rim_height=.006,
                rims=("bottom","sides"),slot="armor" if metal else "leather",
                trim="trim" if metal else "leather_dark")
            panel=_joined(panel,*_studs(panel,[(-.64,.80),(.64,.80)],.017))
            parts[f"CostumeHip{side}{i}"]=_bound(panel,"hips")
        straps=[cuirass.plate(host,belt_y-.105,belt_y+.020,azimuth=azimuth+a,
            width=5,rows=3,cols=3,thickness=.040,depth=.022,rims=(),slot="leather") for a in (-48,48)]
        parts[f"CostumeHipHangers{side}"]=_bound(kit.Piece.merge(*straps),"hips")
    belt_host=BeltHost(host,[p for n,p in parts.items() if "Skirt" in n or "Hip" in n],belt_y)
    band=belts._band(belt_host,belt_y,.115,.023,"leather")
    frame=kit.band_frame(belt_host,belt_y,np.pi/2,.018)
    buckle=(kit.bevel_box((.30,.18,.037),.018,"trim",center=(0,0,.012)) if metal else
            belts.frame_buckle(.123,"trim")).moved(frame)
    parts["CostumeBelt"]=_bound(_joined(band,buckle),"hips")
    if metal:
        skull=horned_skull(.285).moved(kit.Frame(frame.origin+[0,-.010,.018],*frame.R.T))
        parts["CostumeBelt"]=_bound(_joined(parts["CostumeBelt"],skull),"hips")
    else:
        for sign in (-1,1):
            mount=kit.band_frame(belt_host,belt_y-.012,np.pi/2+sign*.53,.016)
            fang=horn((0,0,0),(sign*.014,-.046,.020),(sign*.006,-.090,.032),.019).moved(mount)
            parts["CostumeBelt"]=_bound(_joined(parts["CostumeBelt"],fang),"hips")
    return host


def _shoulder(torso,metal,sign=1,large=True):
    """Rigid asymmetrical pad with a connected leather bearing and baked half-arm animation."""
    from prefabs.outfits.costumes import _bound
    size=(.285 if large else .19) if metal else .215
    pivot=np.array([sign*(.29 if metal else .255),1.275 if metal else 1.235,0.])
    center=np.array([sign*((.48 if large else .43) if metal else .365),1.205 if metal else 1.17,0.])
    cap=pauldrons._shell(size,"armor" if metal else "leather",lat=-8,scale=(1.,.78,1.05),sides=20)
    cap.slots[cap.verts[cap.faces].mean(1)[:,1]<.023]="trim" if metal else "leather_dark"
    additions=[]
    if metal:
        # Overlapping outer lame: a real thick plate, with its inboard edge embedded in the main dome.
        outer=pauldrons._shell(size*1.10,"armor",lat=-8,scale=(1.,.63,1.05),start=38,sides=20)
        outer.verts += [.045,-.040,0]
        outer.slots[outer.verts[outer.faces].mean(1)[:,1]<-.025]="trim"
        additions.append(outer)
    else:
        theta=np.arange(48)*2*np.pi/48
        direction=np.column_stack([np.cos(theta),np.zeros(48),np.sin(theta)])
        # Fur backing is smaller at its upper root so it intersects the pad's closed rolled edge.
        path=direction*[size*.98,0,size*1.03]+[0,-.004,0]
        additions.append(fur_edge(path,direction,length=.060,thickness=.045))
    if metal and large:
        for x,z,length,radius in ((-.095,.045,.235,.048),(.035,.030,.285,.052),(.145,-.045,.205,.046),(.055,-.145,.140,.034)):
            origin=np.array([[x,.8,z]]);direction=np.array([[0.,-1.,0.]])
            hit=kit.ray_distance(origin,direction,cap.verts,cap.faces,reach=1.)[0]
            if not np.isfinite(hit):raise ValueError("Horn root has no pauldron surface")
            root=origin[0]+hit*direction[0]-[0,.016,0]
            boot=horn_socket(radius*1.15,.060,"trim");boot.verts+=root
            tooth=horn(root+[0,.008,0],root+[.025,length*.78,.015],root+[.012,length*1.35,.028],radius*1.10)
            additions.extend((boot,tooth))
    # Large functional rivets and stitched seams sit on the actual shell surface.
    for x,z in ((-.10,.11),(.10,.11),(-.10,-.11),(.10,-.11)):
        origin=np.array([[x,.8,z]])
        hit=kit.ray_distance(origin,np.array([[0.,-1.,0.]]),cap.verts,cap.faces,reach=1.)[0]
        if np.isfinite(hit):
            point=origin[0]+[0,-hit-.005,0]
            additions.append(cuirass._rivet(point,(0,1,0),.015,"rivet"))
    if not metal:
        for z in (-.13,-.065,0,.065,.13):
            hit=kit.ray_distance(np.array([[0,.8,z]]),np.array([[0.,-1.,0.]]),cap.verts,cap.faces,reach=1.)[0]
            point=np.array([0,.8-hit-.003,z])
            additions.append(kit.bevel_box((.045,.012,.012),.003,"stitch",center=point))
    cap=_joined(cap,*additions)
    if sign<0:cap=cap.mirrored()
    cap.verts+=center
    # Leather support follows the shoulder from breast to back, without a floating collar.
    y0=.96 if metal else 1.03
    angle=np.linspace(-.10,np.pi+.10,21)
    x=sign*(.17+((.29 if metal else .255)-.17)*np.sin(np.clip(angle,0,np.pi))**2)
    # Use the actual torso envelope on the ends and the shoulder cap's bearing at the crown.
    path=[]
    for a,px in zip(angle,x):
        f=kit.band_frame(torso,y0,np.pi/2 if np.cos(a)>0 else 3*np.pi/2,.025)
        z=float(f.origin[2])*abs(np.cos(a))
        path.append([px,y0+(pivot[1]-y0)*np.sin(np.clip(a,0,np.pi)),z])
    path=np.asarray(path);path[len(path)//2]=pivot
    support=kit.sweep(path,kit.rect_profile(.078,.025,.006),"leather",up=(0,0,1))
    bearing=kit.lump(pivot,(.035,.023,.040),"leather",n=10,lat=(-60,-30,0,30,60))
    support=_joined(support,bearing)
    # A broad tongue is rooted into both the cap and its bearing; same pivot on both objects.
    lug=kit.sweep([pivot,pivot+[sign*.065,.012,0],center+[0,size*.58,0]],
                  kit.rect_profile(.065,.028,.007),"leather",up=(0,0,1))
    cap=_joined(cap,lug,bearing)
    cap.bone="shoulder_cap.l" if sign>0 else "shoulder_cap.r"
    cap.hinge=dict(parent="chest",follow="upperarm.l" if sign>0 else "upperarm.r",share=.5,pivot=pivot.tolist())
    suffix="Left" if sign>0 else "Right"
    return {"CostumePauldron"+suffix:cap,"CostumeShoulderSupport"+suffix:_bound(support,"chest")}


def barbarian_primitive(torso,belt_y=.625,belt_height=.115):
    """Bare chest, one connected fur-lined leather shoulder and a belt-mounted hide skirt."""
    parts={}
    _skirt(torso,parts,belt_y,False)
    # Existing conforming strap geometry leaves the chest exposed, with no painted-on skin shirt.
    harness=armor.straps(torso,belt_y-.015,1.19,width=.10,
                        thickness=.022,spread=57,cross=False,boss=None)
    front=kit.ring_points(torso,1.19,.010,1,np.array([np.pi/2-np.radians(57)]))[0]
    back=kit.ring_points(torso,1.19,.010,1,np.array([3*np.pi/2+np.radians(57)]))[0]
    bridge=kit.sweep([front,front+[.012,.027,-.022],[.255,1.235,0],
                     back+[.012,.027,.022],back],kit.rect_profile(.100,.028,.006),"leather",up=(0,0,1))
    harness=_joined(harness,bridge)
    y=.94
    theta=np.pi/2-np.radians(57)+2*np.radians(57)*(1.19-y)/(1.19-(belt_y-.015))
    point=kit.band_frame(torso,y,theta,.012)
    buckle=belts.frame_buckle(.075,"trim").moved(point.rotated(-35))
    parts["CostumeHarness"]=_joined(harness,buckle)
    parts.update(_shoulder(torso,False))
    return parts


def orc_king(torso,belt_y=.625,belt_height=.115):
    """Brute-fit iron plates, a horned skull buckle and mismatched tusked shoulder guards."""
    from prefabs.outfits.costumes import _bound,_mesh_mount,WaistHost
    parts={};host=WaistHost(torso)
    for side in ("front","back"):
        plate=cuirass.plate(torso,lambda u:.755-.045*(1-abs(u)),
            lambda u:1.185-.045*(1-abs(u)),side=side,width=85,width_top=70,armhole=.60,
            thickness=.039,depth=.027,sculpt=lambda u,t:.055*(1-abs(u)),
            rim=.026,rim_height=.007,rows=6,cols=17,slot="armor",trim="trim")
        surface=plate.surface_at
        if side=="front":
            # A broad forged panel break is cut into the existing shell, not laid over it.
            plate=solid.conform_surface(cuirass.ridge(plate,[(-.05,.08),(.07,.48),(-.02,.91)],.020,.006,"trim"))
        if side=="back":
            # Bands are flush face color regions; their edge geometry remains the continuous shell.
            from prefabs.outfits.costumes import _horizontal_bands
            plate=_horizontal_bands(plate,side="back",bands=((.85,.90),(1.01,1.06)),seams=(.85,.90,1.01,1.06))
        else:
            p,n=_mesh_mount(plate,surface,-.50,.48)
            cutter=solid_gash(.105,.017,.012).moved(kit.Frame.make(p,n).rotated(-27))
            a=solid.Assembly().add(plate).add(cutter,"armor_dark");a.parts=[a.parts[0]-a.parts[1]];plate=a.build()
        plate=_joined(plate,*_studs_proxy(plate,surface,[(-.7,.13),(.7,.13),(-.67,.86),(.67,.86)]))
        parts["CostumeChest" if side=="front" else "CostumeBack"]=_bound(plate,"chest")
        for i,(low,high) in enumerate(((.635,.775),(.555,.685))):
            lame=cuirass.plate(host,low,high,side=side,width=88,thickness=.030,depth=.027,
                rows=4,cols=15,rim=.014,rim_height=.004,slot="armor",trim="trim",rims=("bottom",))
            parts[f"Costume{side.title()}Belly{i}"]=_bound(lame,"spine" if i==0 else "hips")
    _skirt(torso,parts,belt_y,True)
    parts.update(_shoulder(torso,True,1,True));parts.update(_shoulder(torso,True,-1,False))
    # Broad fitted side straps visibly tie front and back together beneath the armholes.
    for side,theta in (("Left",0),("Right",np.pi)):
        straps=[cuirass.plate(torso,y-.028,y+.028,azimuth=np.degrees(theta),width=28,
             thickness=.038,depth=.027,rows=3,cols=7,rims=(),slot="leather") for y in (.81,1.01)]
        parts["CostumeSideStraps"+side]=_bound(kit.Piece.merge(*straps),"chest")
    # A short connected fur collar behind the neck leaves the front neckline unobstructed.
    theta=np.arange(64)*2*np.pi/64
    fitted=clothes.Fitted(torso,[parts["CostumeChest"],parts["CostumeBack"]],band=.035,clearance=0)
    path=kit.ring_points(fitted,1.16,.008,64,theta)
    outward=np.column_stack([np.cos(theta),np.zeros(64),np.sin(theta)])
    collar=fur_edge(path,outward,length=.045,thickness=.040)
    parts["CostumeFurCollar"]=_bound(collar,"chest")
    return parts


def _studs_proxy(plate,surface,locations):
    """Retain the pre-carve analytical surface for ray-mounted rivets on a boolean result."""
    from prefabs.outfits.costumes import _mesh_mount
    return [cuirass._rivet(p-.004*n,n,.018,"rivet") for p,n in
            (_mesh_mount(plate,surface,u,t) for u,t in locations)]
