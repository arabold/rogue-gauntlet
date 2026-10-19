"""Segmented metal, riveted leather and quilted cloth on the approved costume fit."""

import numpy as np

from core import kit
from items import solid
from prefabs.outfits import cuirass


def _slots(piece, mapping):
    """Change actual surface materials while retaining rigid attachment and existing shading normals."""
    piece.slots = np.array([mapping.get(str(slot),str(slot)) for slot in piece.slots],object)
    return piece


def _quilt(plate, side="front"):
    """Stitched diagonal boundaries are cut into a continuous padded shell, never added as strips."""
    if side=="back":
        plate = kit.Piece(plate.verts*[1,1,-1],plate.faces[:,::-1],plate.slots)
    strips = []
    for slope in (-.65,.65):
        for intercept in np.arange(.1473,1.57,.13):
            a,b = np.array([-.6,intercept-.6*slope]),np.array([.6,intercept+.6*slope])
            normal = np.array([-slope,1.])/np.sqrt(1+slope*slope)*.004
            strips.append([a-normal,b-normal,b+normal,a+normal])
    plate = solid.conform_surface(kit.paint_cut(plate,strips,"cloth_dark",{"cloth"}))
    if side=="back":
        plate = kit.Piece(plate.verts*[1,1,-1],plate.faces[:,::-1],plate.slots)
    return plate


def build(torso, style, belt_y=.625, belt_height=.105):
    """Reuse the approved lower construction and replace the torso with each material's own structure."""
    from prefabs.outfits.costumes import _plate_costume, _bound, _mesh_mount, _tucked_waist_lip, WaistHost
    parts = _plate_costume(torso,belt_y,belt_height,style="knight",fit_lip=False)
    del parts["CostumeChest"],parts["CostumeBack"]
    if style in ("brigandine","gambeson"):
        material = "leather" if style=="brigandine" else "cloth"
        mapping = {"armor":material,"armor_dark":material+"_dark","trim":material+"_worn" if style=="brigandine" else "cloth_dark",
                   "rivet":"iron" if style=="brigandine" else "cloth_dark"}
        for name,piece in parts.items():
            _slots(piece,{**mapping,"trim":"iron"} if name=="CostumeBelt" or "SideStraps" in name else mapping)
    top = min(torso.top-.14,1.18)
    for side in ("front","back"):
        if style=="segmented":
            for i,(bottom,upper) in enumerate(((.715,.895),(.855,1.045),(1.005,top))):
                # Each band has real thickness and overlaps the next; the highest follows the neckline.
                upper_edge = (lambda u: top-.045*(1-np.abs(u))) if i==2 else upper
                plate = cuirass.plate(torso,lambda u,b=bottom:b-.018*(1-np.abs(u)),upper_edge,
                    side=side,width=84,width_top=65 if i==2 else 84,armhole=.62,
                    thickness=.027+.005*i,depth=.028,sculpt=lambda u,t:.02*(1-u*u),
                    rim=.016,rim_height=.004,rims=("bottom","sides"),rows=4,cols=13)
                surface = plate.surface_at
                studs = []
                for u in (-.72,.72):
                    point,normal = _mesh_mount(plate,surface,u,.28)
                    studs.append(cuirass._rivet(point-.003*normal,normal,.016,"rivet"))
                parts[f"Costume{side.title()}Segment{i}"] = _bound(kit.Piece.merge(plate,*studs),"chest")
        elif style=="brigandine":
            plate = cuirass.plate(torso,.715,lambda u:top-.040*(1-np.abs(u)),side=side,
                width=84,width_top=65,armhole=.62,thickness=.025,depth=.021,
                sculpt=lambda u,t:.015*(1-u*u),slot="leather",trim="leather_worn",
                rim=.018,rim_height=.004,rows=8,cols=17)
            surface = plate.surface_at
            if side=="front":
                plate = solid.conform_surface(cuirass.ridge(plate,[(0,.03),(0,.98)],.024,0,"leather_dark"))
            studs = []
            for row,t in enumerate((.16,.39,.62,.84)):
                for u in ((-.72,-.24,.24,.72) if row%2==0 else (-.56,0,.56)):
                    point,normal = _mesh_mount(plate,surface,u,t)
                    studs.append(cuirass._rivet(point-.003*normal,normal,.014,"iron"))
            parts[f"Costume{side.title()}Brigandine"] = _bound(kit.Piece.merge(plate,*studs),"chest")
        else:
            def padding(u,t):
                x,y = u*.30,.59+(top-.59)*t
                return .045*np.sin(np.pi*(y-.65*x-.4073)/.13)**2*np.sin(np.pi*(y+.65*x-.4073)/.13)**2
            plate = cuirass.plate(WaistHost(torso),.59,lambda u:top-.040*(1-np.abs(u)),side=side,
                width=88,width_top=65,armhole=.62,thickness=.031,depth=.026,
                sculpt=padding,slot="cloth",trim="cloth_dark",rim=.015,rim_height=.003,rows=20,cols=25)
            parts[f"Costume{side.title()}Gambeson"] = _quilt(plate,side)
            # Continuous padded torso replaces the upper rigid belly plate, retaining the fitted lower skirt.
            del parts[f"Costume{side.title()}Lame0"]
    if style in ("segmented","brigandine"):
        # Fit against this material's actual lower torso edge, not the scaffold's knight breastplate.
        for side in ("Front","Back"):
            cover = kit.Piece.merge(*(piece for name,piece in parts.items()
                if side+"Segment" in name or side+"Brigandine" in name))
            key = "Costume"+side+"Lame0"
            original = parts[key]
            parts[key] = _bound(_tucked_waist_lip(original,original.underlap_upper,cover,depth=.065),"spine")
    if style=="gambeson":
        host = WaistHost(torso)
        # A cloth skirt and quilted hip laps replace the metal lames/rivets, not just their colors.
        for side in ("front","back"):
            for i in (1,2):
                del parts[f"Costume{side.title()}Lame{i}"]
            skirt = cuirass.plate(host,.455,belt_y+min(.040,belt_height*.5-.010),side=side,width=88,thickness=.028,depth=.024,
                slot="cloth",trim="cloth_dark",rim=.010,rim_height=.001,rims=("bottom","sides"),rows=8,cols=17)
            parts[f"Costume{side.title()}ClothSkirt"] = _bound(_quilt(skirt,side),"hips")
        for suffix,azimuth,angle in (("Left",0,-90),("Right",180,90)):
            lap = cuirass.plate(host,belt_y-.215,belt_y-.035,width=78,width_top=74,armhole=0,
                thickness=.034,depth=.025,slot="cloth",trim="cloth_dark",rim=.012,rim_height=.001,
                rows=8,cols=21,rims=("bottom","sides"),azimuth=azimuth)
            rotation = kit.rotation((0,1,0),angle)
            local = kit.Piece(lap.verts@rotation.T,lap.faces,lap.slots)
            quilted = _quilt(local)
            parts[f"CostumeTasset{suffix}"] = _bound(kit.Piece(quilted.verts@rotation,quilted.faces,quilted.slots),"hips")
    return parts
