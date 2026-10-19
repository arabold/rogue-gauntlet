"""Four cloth costume silhouettes sharing reusable collars, seated closures and fitted utility belts."""

import numpy as np

from core import kit
from items import solid
from prefabs.outfits import belts, clothes, cuirass


def _fused(piece):
    """Union the original indexed islands, preserving palette boundaries and actual support contact."""
    return solid.fuse_closed(piece)


def utility_pouch(host,y,theta,height=.11):
    """Registered belt-pouch geometry, with joined flap/button and its back sunk into the strap."""
    from prefabs.outfits.costumes import _bound
    piece = _fused(belts.pouch(height,"leather","leather_dark","metal"))
    return _bound(piece.moved(kit.band_frame(host,y,theta,.010)),"hips")


def _closure(plate,rows):
    """Bold short lacing seated on the real chest surface, with no colored overlay panels."""
    from prefabs.outfits.costumes import _mesh_mount
    pieces = []
    for t in rows:
        point,normal = _mesh_mount(plate,plate.surface_at,0,t)
        frame = kit.Frame.make(point-.004*normal,normal)
        for angle in (-38,38):
            lace = kit.bevel_box((.105,.023,.019),.006,"leather_worn",center=(0,0,.006))
            pieces.append(lace.moved(frame.rotated(angle)))
    return kit.Piece.merge(*pieces)


def _cape(torso,style,pieces=()):
    """A complete continuous cloth cape; its chest yoke replaces the separate collar."""
    from prefabs.outfits import capes
    hem=.46 if style=="scout" else .29 if style=="arcanist" else .34
    return capes.build(torso,hem=hem,slot="cloak",trim="cloak_dark",pieces=pieces)


def build(torso,style,belt_y=.625,belt_height=.105):
    """Fitted cloth tops and shaped lower hems, with role-specific collars and utility accessories."""
    from prefabs.outfits.costumes import WaistHost,BeltHost,_bound,_shoulder_bridge,_mesh_mount
    if style not in ("ranger","scout","arcanist","adventurer"):
        raise ValueError(f"Unknown travel costume: {style}")
    host = WaistHost(torso)
    parts = {"CostumeShoulderCloth":_shoulder_bridge()}
    # Continuous cloth follows the body's weights rather than chest-binding the entire belly.
    tunic = clothes.shell(host,.590,1.185,.034,"cloth",inset=-.012,sides=32,rows=12,
        neck=dict(style="v",front=.055,back=.025))
    parts["CostumeTunic"] = tunic
    for side in ("front","back"):
        def edge(u):
            if style=="arcanist":
                return .405+.065*(1-abs(u))  # divided coat tails
            if style=="scout":
                return .465+.030*u+.025*(1-u*u)
            if style=="adventurer":
                return .450+.035*np.exp(-(u/.17)**2)+.009*np.sin(u*10)
            return .445+.025*(1-u*u)
        hem = cuirass.plate(host,edge,belt_y+.035,side=side,width=90,width_top=90,armhole=0,
            thickness=.035,depth=.022,slot="cloth",trim="cloth_dark",rim=.015,rim_height=.002,
            rims=("bottom",),rows=7,cols=25)
        parts[f"Costume{side.title()}Hem"] = _bound(hem,"hips")
    # Collar roots overlap the shoulder bridge; the upper tunic remains visible between bib and belt.
    front = cuirass.plate(host,.72,1.12,width=65,thickness=.034,depth=.02,rims=(),rows=9,cols=17)
    if style in ("ranger","scout"):
        parts["CostumeLacing"] = _closure(front,(.39,) if style=="ranger" else (.26,.47))
    elif style=="arcanist":
        buttons = []
        for t in (.22,.40,.58):
            point,normal = _mesh_mount(front,front.surface_at,.10,t)
            buttons.append(cuirass._rivet(point-.004*normal,normal,.019,"metal"))
        parts["CostumeButtons"] = kit.Piece.merge(*buttons)
    else:
        # A shaped facing stripe is colored in the tunic's own surface.
        polygons = [[(-.014,.72),(.014,.72),(.014,.99),(-.014,.99)]]
        parts["CostumeTunic"] = solid.conform_surface(kit.paint_cut(tunic,polygons,"cloth_dark",{"cloth"}))
    belt_host = BeltHost(host,[tunic,*[p for name,p in parts.items() if "Hem" in name]],belt_y)
    strap = belts._band(belt_host,belt_y,belt_height,.018,"leather")
    buckle = belts.frame_buckle(belt_height*1.08,"metal").moved(kit.band_frame(belt_host,belt_y,np.pi/2,.013))
    parts["CostumeBelt"] = _bound(kit.Piece.merge(strap,buckle),"hips")
    parts["CostumePouch"] = utility_pouch(belt_host,belt_y+.025,np.pi/2+.68,.10 if style=="arcanist" else .115)
    if style=="adventurer":
        parts["CostumeSecondPouch"] = utility_pouch(belt_host,belt_y+.025,np.pi/2-.88,.095)
    if style=="arcanist":
        # A compact leather case balances the pouch, attached directly to the same belt.
        case = kit.bevel_box((.10,.18,.075),.014,"leather_dark",center=(0,-.045,.033))
        parts["CostumeScrollCase"] = _bound(case.moved(kit.band_frame(belt_host,belt_y+.025,np.pi/2-.70,.012)),"hips")
    parts["CostumeCape"] = _cape(torso,style,[tunic])
    return parts
