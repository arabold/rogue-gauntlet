"""Linked mail, cloth-over-mail and shield-derived steel scale armor on the shared costume fit."""

import numpy as np

from core import kit
from items import solid
from prefabs.outfits import belts, cuirass
from prefabs.details.heraldry import scale_plate
from scipy.spatial import ConvexHull


def _ring():
    """A broad closed eight-sided wire ring; the center remains an actual hole."""
    a = np.arange(8)*2*np.pi/8
    path = np.column_stack([.041*np.cos(a),.034*np.sin(a),np.zeros(8)])
    return kit.sweep(path,kit.ring_profile(.0045,.0045,4),"mail",up=(0,0,1),closed=True)


def _ornamented(host,bottom,top,side,kind,azimuth=None,width=84,width_top=65,rows=6,cols=7,tabard_cover=False,belt_y=.625,belt_height=.105):
    """Fuse surface-fitted ring or scale relief to one closed continuous garment backing."""
    from prefabs.outfits.costumes import _mesh_mount
    scale = kind=="scale"
    cape_back = tabard_cover and side=="back" and azimuth is None
    backing = cuirass.plate(host,bottom,lambda u:top-.035*(1-np.abs(u)),side=side,width=width,
        width_top=width_top,armhole=.62 if azimuth is None else 0,thickness=.022,depth=.019,
        slot="leather" if scale else "mail_dark",trim="leather" if scale else "mail",
        rim=.012,rim_height=.002,rows=8 if cape_back else 12,cols=15 if cape_back else 21,azimuth=azimuth)
    surface = backing.surface_at
    assembly = solid.Assembly().add(backing)
    clip = None
    if scale and azimuth is None:
        xy = backing.verts[:,:2]
        outline = xy[ConvexHull(xy).vertices]
        clip = solid.polygon(outline,1.0)
    if scale:
        # Same actual greeble as the approved scale shield, with depth adapted to a wearable.
        ornament = scale_plate(.135,.110) if azimuth is not None else scale_plate(.170,.160)
        ornament.verts[:,2] *= .48 if azimuth is not None else .70
    else:
        ornament = _ring()
    row_positions = np.linspace(.34,.86,rows) if scale and azimuth is None else np.array([.48]) if scale else np.linspace(.14,.88,rows)
    for row,t in enumerate(row_positions):
        for column,u in enumerate(np.linspace(-.80,.80,cols)+(row%2)*(.80/max(cols-1,1))):
            u = float(np.clip(u,-.88,.88))
            if tabard_cover and azimuth is None and abs(u)<(.91 if side=="back" else .45):
                continue  # Tabard/cape conceal these rings; retain the continuous mail backing.
            try:
                point,normal = _mesh_mount(backing,surface,u,float(t))
            except ValueError:
                continue
            if tabard_cover and azimuth is not None and abs(point[2]) >= abs(point[0])*.80:
                continue  # Keep hip-ring relief out from beneath the narrower front/back cloth hems.
            # Keep relief clear of the belt. The backing itself continues behind the strap.
            extent = (.065 if azimuth is not None else .093) if scale else .043
            if point[1]-extent<belt_y+belt_height*.5+.0075 and point[1]+extent>belt_y-belt_height*.5-.0025:
                continue
            frame = kit.Frame.make(point-(.007 if scale else .002)*normal,normal)
            if scale:
                piece = ornament.moved(frame)
                slot = ("steel_scale","steel_scale_light","steel_scale_dark")[(row+column)%3]
            else:
                piece = ornament.moved(frame.rotated(16 if row%2 else -16,"x").rotated(12 if row%2 else -12))
                slot = "mail"
            assembly.add(piece,slot)
            if clip is not None:
                label = assembly.slots.index(slot)
                clipped = assembly.parts[-1] ^ clip
                assembly.parts[-1] = clipped.set_properties(1,lambda position,old,label=label:[label])
    return assembly.build()


def _cross(plate,side):
    """Color the tabard's existing surface with a broad cross, preserving its cloth shading."""
    if side=="back":
        plate = kit.Piece(plate.verts*[1,1,-1],plate.faces[:,::-1],plate.slots)
    # Paint cutter requires convex polygons; intersecting bars provide the same connected cross.
    bars = [[(-.03,.795),(.03,.795),(.03,1.075),(-.03,1.075)],
            [(-.115,.925),(.115,.925),(.115,.985),(-.115,.985)]]
    painted = solid.conform_surface(kit.paint_cut(plate,bars,"heraldry",{"cloth"}))
    if side=="back":
        painted = kit.Piece(painted.verts*[1,1,-1],painted.faces[:,::-1],painted.slots)
    return painted


def build(torso,style,belt_y=.625,belt_height=.105):
    """Shared fit, distinct ring/scale surfaces and actual fitted tabard/belt layering."""
    from prefabs.outfits.costumes import _costume_foundation, _bound, WaistHost, BeltHost
    parts = _costume_foundation(torso,belt_y,belt_height,waist=False,panels=False)
    host = WaistHost(torso)
    top = min(torso.top-.14,1.18)
    scale = style=="scale"
    for side in ("front","back"):
        body = _ornamented(host,.59,top,side,"scale" if scale else "mail",rows=5 if scale else 6,cols=7,tabard_cover=style=="crusader",belt_y=belt_y,belt_height=belt_height)
        # Let the continuous backing follow the body; chest-binding the belly would drive it through the belt.
        parts[f"Costume{side.title()}Torso"] = body
        skirt = cuirass.plate(host,.455,belt_y+min(.040,belt_height*.5-.010),side=side,
            width=88,thickness=.024,depth=.020,slot="armor" if scale else "mail_dark",
            trim="trim" if scale else "mail",rim=.010,rim_height=.002,
            rims=("bottom","sides"),rows=5,cols=15)
        parts[f"Costume{side.title()}Skirt"] = _bound(skirt,"hips")
    for suffix,azimuth in (("Left",0),("Right",180)):
        if scale:
            lap = cuirass.plate(host,belt_y-.215,belt_y-.035,azimuth=azimuth,
                width=78,width_top=74,armhole=0,thickness=.034,depth=.025,
                slot="armor",trim="trim",rim=.012,rim_height=.003,
                rims=("bottom","sides"),rows=5,cols=21)
        else:
            lap = _ornamented(host,belt_y-.215,belt_y-.035,"front","mail",
                azimuth=azimuth,width=78,width_top=74,rows=2,cols=6,
                tabard_cover=style=="crusader",belt_y=belt_y,belt_height=belt_height)
        parts[f"CostumeTasset{suffix}"] = _bound(lap,"hips")
    supports=[p for name,p in parts.items() if "Torso" in name or "Skirt" in name]
    if style=="crusader":
        from prefabs.outfits import capes
        # One garment runs down the whole front and back, with no separate bib/tabard.
        cape=capes.build(torso,hem=.34,front_hem=.423,slot="cloth",trim="cloak_dark",
                         pieces=supports,back_clearance=.018)
        cape=_cross(cape,"front")
        cape.garment_type="cape"
        cape.cover_skin=False
        parts["CostumeCape"]=cape
        # Belt over the front cloth, beneath the freely hanging rear drape.
        front=cape.faces[cape.verts[cape.faces].mean(1)[:,2]>0]
        belt_supports=[torso.mesh(),*supports,kit.Piece(cape.verts,front,cape.slots[
            cape.verts[cape.faces].mean(1)[:,2]>0])]
        fitted=belts.build_fitted(host,belt_supports,belt_y,height=belt_height,thickness=.018,
                                  variant="leather",buckle_scale=1.1)
        parts["CostumeBelt"]=_bound(fitted,"hips")
    else:
        belt_host=BeltHost(host,supports,belt_y)
        strap=belts._band(belt_host,belt_y,belt_height,.018,"leather")
        buckle=belts.frame_buckle(belt_height*1.10,"trim").moved(kit.band_frame(belt_host,belt_y,np.pi/2,.013))
        parts["CostumeBelt"]=_bound(kit.Piece.merge(strap,buckle),"hips")
    return parts
