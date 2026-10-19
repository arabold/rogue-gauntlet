"""Coordinated wearable sets. A costume is one recipe choice with separately articulated physical parts."""

import numpy as np

from core import kit
from prefabs.outfits import belts, clothes, cuirass
from items import solid
from prefabs.details.wear import solid_gash


def _bound(piece, bone):
    """A rigid plate follows its carrying bone instead of bending like a tunic."""
    piece.bone = bone
    return piece


def _buckle(height):
    """Use the same bold, closed buckle construction as other character belts."""
    return belts.frame_buckle(height,"trim")


def _chest_emblem(torso, surface_at):
    """Reuse the registered sunburst greeble, seated on the actual breastplate surface."""
    from core.rig import _islands
    from prefabs.details.trophies import TROPHIES
    point = surface_at(0,.60)
    source_frame = kit.band_frame(torso,point[1],np.pi/2,0)
    emblem = TROPHIES["shield_emblem"](torso,point[1],style="sunburst",size=.07,out=0,slot="trim",gem="gem")
    # Union the greeble's original closed elements before welding their coincident ray roots.
    islands = _islands(emblem.faces,len(emblem.verts))
    assembly = solid.Assembly()
    for island in np.unique(islands[emblem.faces[:,0]]):
        keep = islands[emblem.faces[:,0]] == island
        piece = kit.Piece((emblem.verts-source_frame.origin) @ source_frame.R,
                          emblem.faces[keep],emblem.slots[keep])
        assembly.add(piece)
    local = assembly.build()
    normal = np.cross(surface_at(.01,.60)-point,surface_at(0,.61)-point)
    if normal[2]<0:
        normal = -normal
    normal /= np.linalg.norm(normal)
    # Sink the backing slightly into the keel; the ornament shares the chest's carrying bone.
    return _bound(local.moved(kit.Frame.make(point-.004*normal,normal)),"chest")


class WaistHost:
    """Body-close upper belly and a restrained continuous skirt flare below the belt."""

    def __init__(self, torso):
        self.torso = torso

    def __getattr__(self, name):
        return getattr(self.torso,name)

    def section(self, y):
        y = np.atleast_1d(np.asarray(y,float))
        cx,a,cz,b = self.torso.section(y)
        waist_a,waist_b = (float(self.torso.section([.68])[i][0]) for i in (1,3))
        t = np.clip((.68-y)/.26,0,1)**2
        return cx, a+(np.maximum(waist_a-a,0)+.035)*t, cz, b+(np.maximum(waist_b-b,0)+.028)*t


class BeltHost:
    """One taut armor cross-section through the strap height, without a padded layer envelope."""

    def __init__(self, torso, pieces, y, band=.022):
        self.host = clothes.Fitted(torso,pieces,band=band,clearance=.001)
        self.y = y

    def __getattr__(self,name):
        return getattr(self.host,name)

    def section(self,y):
        return self.host.section(np.full(np.atleast_1d(y).shape,self.y))

    def outline(self,y,theta,out):
        result = self.host.outline(self.y,theta,out)
        result[:,1] = np.broadcast_to(y,np.asarray(theta).shape)
        return result


def _hip_panel(host, belt_y, azimuth):
    """A hip-length lap plate wraps from front to back with small center openings."""
    return cuirass.plate(host, belt_y-.215, belt_y-.035, width=78,width_top=74,armhole=0,
        thickness=.042,depth=.026,rows=5,cols=21,rims=("bottom","sides"),rim=.014,rim_height=.004,
        azimuth=azimuth)


def _shoulder_bridge():
    """Short cloth sleeve roots tuck under the cuirass and blend across each shoulder joint."""
    from prefabs.anatomy.bodies import JOINTS
    shoulder = np.asarray(JOINTS["shoulder"],float)
    angles = np.arange(16)*2*np.pi/16
    rings = []
    # Broad inner roots meet the chest/back; the outer ends overlap the existing sleeve tubes.
    for t in np.linspace(0,1,7):
        x = shoulder[0]-.10+.24*t
        depth = .215*(1-t)+.140*t
        height = .130+.015*np.sin(np.pi*t)
        rings.append(np.column_stack([np.full(len(angles),x),
            shoulder[1]+height*np.cos(angles),depth*np.sin(angles)]))
    verts,faces = kit._loft(rings)
    count = len(angles)
    verts,faces = kit._cap_loops(verts,faces,[list(range(count)),
        list(range((len(rings)-1)*count,len(rings)*count))])
    one = kit.Piece(verts,kit._orient_closed(verts,faces),"cloth")
    # No rigid bone: production skin transfer blends the chest and upper arm across the short tube.
    return kit.Piece.merge(one,one.mirrored())


def _plate_costume(torso, belt_y=.625, belt_height=.105, style="paladin",fit_lip=True):
    """Shared approved proportions and attachments; material language belongs to each costume."""
    necromancer = style=="necromancer"
    female = style in ("necromancer","maiden")
    dark = style=="dark_knight"
    pieces = {}
    if style!="paladin":
        pieces["CostumeShoulderCloth"] = _shoulder_bridge()
    top = min(torso.top - .14, 1.18)
    # Broad planar keel: the chest is one shaped shell, not modeled pectoral blocks.
    sculpt = lambda u, t: .07 * (1 - np.abs(u)) + .012 * np.sin(np.pi * t)
    if dark:
        sculpt = lambda u,t: .115*(1-np.abs(u))+.015*np.sin(np.pi*t)
    if female:
        # Shape the existing shell gently; avoid the old detached, oversized cup forms.
        sculpt = lambda u,t: .012*(1-np.abs(u)) + .15*np.exp(-((np.abs(u)-.40)/.30)**2-((t-.55)/.42)**2)
    bottom = (lambda u: .950-.070*np.exp(-((np.abs(u)-.40)/.30)**2)) if female else (lambda u: .765-(.080 if dark else .055)*(1-np.abs(u)))
    chest = cuirass.plate(torso, bottom,
        lambda u: top - .045 * (1 - np.abs(u)), width=88 if dark else 84, width_top=69 if dark else 65, armhole=.62,
        thickness=.032, depth=.028, sculpt=sculpt, rim=.039, rim_height=.012,
        rows=8 if female else 4, cols=15 if female else 7)
    surface_at = chest.surface_at
    if necromancer:
        # Violet boning is colored into the shell's own topology, never layered tubes.
        for sign in (-1,1):
            chest = cuirass.ridge(chest,[(sign*.64,.12),(sign*.48,.42),(sign*.60,.76)],
                                  .018,0,"trim")
        chest = solid.conform_surface(chest)
    elif not female:
        # A real closed negative cutter leaves a carved nick, not an open surface patch.
        p = chest.surface_at(-.48, .64)
        cut = solid_gash(.10,.014,.010).moved(kit.Frame.make(p,(-.35,.12,1)).rotated(-32))
        assembly = solid.Assembly().add(chest).add(cut,"armor_dark")
        assembly.parts = [assembly.parts[0] - assembly.parts[1]]
        chest = assembly.build()
    studs = []
    for t in (.075,.92):
        for u in ((-.72,.72) if style=="knight" else (-.72,0,.72)):
            if style in ("knight","dark_knight","maiden"):
                point,normal = _mesh_mount(chest,surface_at,u,t)
                studs.append(cuirass._rivet(point-.003*normal,normal,.017,"rivet"))
            else:
                studs.append(cuirass._rivet(surface_at(u,t),(u*.3,.1,1),.017,"rivet"))
    if dark:
        for u in (-.70,.70):
            point,normal = _mesh_mount(chest,surface_at,u,.85)
            studs.append(kit.spike(point-.006*normal,point+.075*normal,.028,"trim"))
    chest = kit.Piece.merge(chest,*studs)
    from core.export import corner_normals
    chest.normals = corner_normals(chest.verts, chest.faces, crease=28)
    pieces["CostumeChest"] = _bound(chest, "chest")
    if style=="paladin":
        pieces["CostumeEmblem"] = _chest_emblem(torso,surface_at)
    elif necromancer:
        pieces["CostumeEmblem"] = _skull_mount(surface_at(0,.76),.060,"chest")
    elif style in ("maiden","dark_knight"):
        pieces["CostumeEmblem"] = _gem_mount(surface_at,.072 if female else .095)
    pieces["CostumeBack"] = _bound(cuirass.plate(torso, .725, lambda u: top - .025 * (1-u*u),
        side="back", width=84, width_top=65, armhole=.62, thickness=.035, depth=.028,
        sculpt=lambda u,t: .045*(1-u*u), rows=4, cols=9, rim=.034, rim_height=.010,
        rivets={"top": 3}, rivet_size=.017), "chest")
    if style=="maiden":
        pieces["CostumeBack"] = _bound(_horizontal_bands(pieces["CostumeBack"],side="back",
            bands=((.805,.895),(.975,1.065)),seams=(.805,.895,.975,1.065)),"chest")
    # All waist plates follow one continuous close-to-flared profile.
    waist_host = WaistHost(torso)
    waist_parts = []
    # Underlapping lames are actual separate plates; only the last edge carries brass.
    for side in ("front", "back"):
        for i in range(3):
            y = .71 - .085 * i
            hem_lift = .035 if i==2 else 0
            lower = lambda u, y=y, lift=hem_lift: y - .100 - .020*(1-np.abs(u)) + lift
            upper = lambda u, y=y, i=i, side=side: y + ((.025 if side=="front" else .040) if i==0 else .045) - .020*(1-np.abs(u))
            if female and side=="front" and i==0:
                upper = lambda u: .958-.070*np.exp(-((np.abs(u)-.40)/.30)**2)
            part = cuirass.plate(waist_host, lower, upper,
                side=side, width=88, thickness=.024 + .003*i, depth=.022,
                flare=0, rows=4, cols=11, rim=.012, rim_height=.004,
                rims=("bottom", "sides"), trim="trim" if i==2 else "armor",
                rivets={"bottom": 2} if i==2 else None, rivet_size=.014)
            if i==0 and fit_lip:
                part = _tucked_waist_lip(part,upper,pieces["CostumeChest" if side=="front" else "CostumeBack"],
                                        depth=.016 if female and side=="front" else .030)
            if style=="maiden" and side=="front" and i==0:
                part = _horizontal_bands(part)
            waist_parts.append(part)
            # Preserve the original waist articulation. A lower, tucked upper lip accommodates the chest.
            pieces[f"Costume{side.title()}Lame{i}"] = _bound(part,"spine" if i==0 else "hips")
            if i==0:
                part.underlap_upper = upper
    # Pull the belt against the actual waist shell, not an inflated layer estimate.
    belt_host = BeltHost(waist_host,waist_parts,belt_y)
    strap = belts._band(belt_host,belt_y,belt_height,.018,"leather")
    buckle = _buckle(belt_height*1.10).moved(kit.band_frame(belt_host,belt_y,np.pi/2,.013))
    pieces["CostumeBelt"] = _bound(kit.Piece.merge(strap,buckle),"hips")
    if necromancer:
        mount = kit.band_frame(belt_host,belt_y,np.pi/2,.013)
        pieces["CostumeBuckleSkull"] = _skull_mount(mount.origin,.280,"hips",mount.R,front_half=True)
    for sign,suffix in ((1,"Left"),(-1,"Right")):
        angle = 0 if sign>0 else 180
        plate = _hip_panel(waist_host,belt_y,angle)
        studs = []
        for u in (-.72,0,.72):
            p0 = plate.surface_at(u,.86)
            dx = plate.surface_at(u+.01,.86)-p0
            dy = plate.surface_at(u,.87)-p0
            normal = np.cross(dx,dy)
            if normal @ (p0-[0,p0[1],0]) < 0:
                normal = -normal
            studs.append(cuirass._rivet(p0,normal,.014,"rivet"))
        if dark:
            for u,length in ((-.40,.075),(.40,.055)):
                point,normal = _surface_frame(plate.surface_at,u,.60)
                studs.append(kit.spike(point-.006*normal,point+length*normal,.024,"trim"))
        pieces[f"CostumeTasset{suffix}"] = _bound(kit.Piece.merge(plate,*studs),"hips")
        # Narrow curved straps overlap the belt and the lap plate on the same carrying bone.
        straps = [cuirass.plate(waist_host,belt_y-.105,belt_y+.015,width=3,thickness=.045,depth=.015,
            rows=3,cols=3,rims=(),slot="leather",azimuth=angle+offset) for offset in (-56,0,56)]
        pieces[f"CostumeHangers{suffix}"] = _bound(kit.Piece.merge(*straps),"hips")
    # Side buckled straps join front and back shells across the open armhole sides.
    for sign, suffix in ((1,"Left"),(-1,"Right")):
        parts=[]
        for y in (.77,.99):
            f=kit.band_frame(torso,y,0 if sign>0 else np.pi,.045)
            parts.append(kit.bevel_box((.22,.046,.025),.006,"leather").moved(f))
            parts.append(_buckle(.036).moved(f))
        pieces[f"CostumeSideStraps{suffix}"]=_bound(kit.Piece.merge(*parts),"chest")
    return pieces


def _tucked_waist_lip(plate,upper,cover,depth=.030):
    """Recess only the upper underlap, leaving the visible belly and belt fit intact."""
    vertices = plate.verts.copy()
    # The curved edge's height varies across the front; normalized X approximates its local profile.
    extent = max(abs(vertices[:,0]).max(),1e-8)
    top = upper(np.clip(vertices[:,0]/extent,-1,1))
    blend = np.clip(1-(top-vertices[:,1])/.040,0,1)**2
    # Recess only vertices already beneath the outer plate; visible belly must remain outside the body.
    view = np.array([0.,0.,1. if vertices[:,2].mean()>0 else -1.])
    hit = kit.ray_distance(vertices+.15*view,-np.tile(view,(len(vertices),1)),cover.verts,cover.faces,reach=.30)
    blend *= np.isfinite(hit)&(hit<.15)
    radial = vertices[:,[0,2]]
    length = np.linalg.norm(radial,axis=1)
    vertices[:,[0,2]] *= (1-depth*blend/np.maximum(length,1e-8))[:,None]
    return kit.Piece(vertices,plate.faces,plate.slots)


def _horizontal_bands(plate,side="front",bands=((.70,.765),(.825,.885)),seams=(.765,.825,.885)):
    """Read horizontal lames through flush steel tones and narrow seams on the existing belly shell."""
    def band(bottom,top):
        return [(-.6,bottom),(.6,bottom),(.6,top),(-.6,top)]
    # Keep the fitted silhouette and its single carrying bone; color boundaries split the same surface.
    if side=="back":
        plate = kit.Piece(plate.verts*[1,1,-1],plate.faces[:,::-1],plate.slots)
    painted = kit.paint_cut(plate,[band(lo,hi) for lo,hi in bands],"armor_lame",{"armor"})
    painted = kit.paint_cut(painted,[band(y-.006,y+.006) for y in seams],
                            "armor_joint",{"armor","armor_lame"})
    painted = solid.conform_surface(painted)
    if side=="back":
        painted = kit.Piece(painted.verts*[1,1,-1],painted.faces[:,::-1],painted.slots)
    return painted


def _surface_frame(surface_at,u,t):
    """Use the actual plate location and outward normal for raised fittings."""
    point = surface_at(u,t)
    normal = np.cross(surface_at(u+.005,t)-point,surface_at(u,t+.005)-point)
    if normal @ (point-[0,point[1],0])<0:
        normal = -normal
    normal /= np.linalg.norm(normal)
    return point,normal


def _mesh_mount(plate,surface_at,u,t):
    """Ray-seat fittings on the triangulated shell, including tapered and strongly faceted plate edges."""
    point,normal = _surface_frame(surface_at,u,t)
    start = point+.10*normal
    distance = kit.ray_distance(start[None],-normal[None],plate.verts,plate.faces,reach=.25)[0]
    if not np.isfinite(distance):
        raise ValueError(f"Fitting misses the actual plate at {u=}, {t=}")
    return start-distance*normal,normal


def _gem_mount(surface_at,size):
    """A harvested faceted gem sits in a closed diamond bezel sunk into its carrying plate."""
    from prefabs.details.trophies import gem_trophy
    gem = gem_trophy(size=size)
    vertices = gem.verts.copy()
    vertices[:,2] += .004-vertices[:,2].min()
    bezel = solid.polygon([(-size*.55,0),(0,-size*.70),(size*.55,0),(0,size*.70)],.020)
    badge = solid.Assembly().add(bezel,"trim").add(kit.Piece(vertices,gem.faces,gem.slots)).build()
    point,normal = _surface_frame(surface_at,0,.66)
    return _bound(badge.moved(kit.Frame.make(point-.004*normal,normal)),"chest")


def _skull_mount(point, height, bone, rotation=None, front_half=False):
    """Seat the reusable closed skull boss in its actual supporting surface."""
    from prefabs.details.trophies import skull_trophy
    skull = skull_trophy(size=height,jaw=False)
    if front_half:
        # A buckle is a relief with a closed planar mounting back, not a complete skull.
        assembly = solid.Assembly().add(skull)
        clip = solid.Assembly().add(solid.box((height*2,height*2,height),center=(0,0,height/2),bevel=0),"bone")
        assembly.parts = [assembly.parts[0] ^ clip.parts[0]]
        skull = assembly.build()
    verts = skull.verts.copy()
    verts[:,2] -= verts[:,2].min()+(.020 if front_half else .008)
    local = kit.Piece(verts,skull.faces,skull.slots)
    frame = kit.Frame.make(point,(0,0,1)) if rotation is None else kit.Frame(point,*rotation.T)
    return _bound(local.moved(frame),bone)


def knight_plate(torso, belt_y=.625, belt_height=.105):
    """Approved steel plate reference, articulated waist and curved hip protection."""
    return _plate_costume(torso,belt_y,belt_height)


def necromancer(torso, belt_y=.625, belt_height=.105):
    """Fitted violet corset and bone ornaments on the approved articulated costume pattern."""
    return _plate_costume(torso,belt_y,belt_height,style="necromancer")


def knight(torso,belt_y=.625,belt_height=.105):
    """Practical unadorned steel with fitted waist protection and restrained wear."""
    return _plate_costume(torso,belt_y,belt_height,style="knight")


def maiden(torso,belt_y=.625,belt_height=.105):
    """Female steel plate using the approved breast, belly and shoulder construction."""
    return _plate_costume(torso,belt_y,belt_height,style="maiden")


def dark_knight(torso,belt_y=.625,belt_height=.105):
    """Broader dark steel with a pronounced keel and physically seated angular fittings."""
    return _plate_costume(torso,belt_y,belt_height,style="dark_knight")


def segmented(torso,belt_y=.625,belt_height=.105):
    """Real overlapping metal bands using the common fitted waist and hip construction."""
    from prefabs.outfits.field_costumes import build
    return build(torso,"segmented",belt_y,belt_height)


def brigandine(torso,belt_y=.625,belt_height=.105):
    """Riveted leather with a front closure and metal hardware seated on its shell."""
    from prefabs.outfits.field_costumes import build
    return build(torso,"brigandine",belt_y,belt_height)


def gambeson(torso,belt_y=.625,belt_height=.105):
    """Continuous padded cloth, integrated stitching and a fitted leather belt."""
    from prefabs.outfits.field_costumes import build
    return build(torso,"gambeson",belt_y,belt_height)


def chain_mail(torso,belt_y=.625,belt_height=.105):
    """Staggered closed ring relief fused to continuous fitted mail backing."""
    from prefabs.outfits.mail_costumes import build
    return build(torso,"chain_mail",belt_y,belt_height)


def crusader(torso,belt_y=.625,belt_height=.105):
    """The shared mail foundation with a fitted cloth tabard and integrated cross."""
    from prefabs.outfits.mail_costumes import build
    return build(torso,"crusader",belt_y,belt_height)


def scale(torso,belt_y=.625,belt_height=.105):
    """Shield-derived ridged steel torso scales with plain curved metal hip panels."""
    from prefabs.outfits.mail_costumes import build
    return build(torso,"scale",belt_y,belt_height)


def ranger(torso,belt_y=.625,belt_height=.105):
    """Forest tunic, pointed scarf, lacing and a short traveling mantle."""
    from prefabs.outfits.travel_costumes import build
    return build(torso,"ranger",belt_y,belt_height)


def scout(torso,belt_y=.625,belt_height=.105):
    """Asymmetric dark tunic and folded cowl with a compact shoulder mantle."""
    from prefabs.outfits.travel_costumes import build
    return build(torso,"scout",belt_y,belt_height)


def arcanist(torso,belt_y=.625,belt_height=.105):
    """Divided cloth coat, high scarf, buttons and a belted scroll case."""
    from prefabs.outfits.travel_costumes import build
    return build(torso,"arcanist",belt_y,belt_height)


def adventurer(torso,belt_y=.625,belt_height=.105):
    """Split work tunic, neckerchief and practical twin utility pouches."""
    from prefabs.outfits.travel_costumes import build
    return build(torso,"adventurer",belt_y,belt_height)


def barbarian_primitive(torso,belt_y=.625,belt_height=.115):
    """Approved primitive concept: leather harness, fur-lined single shoulder and hide hips."""
    from prefabs.outfits.primal_costumes import barbarian_primitive as make
    return make(torso,belt_y,belt_height)


def orc_king(torso,belt_y=.625,belt_height=.115):
    """Approved royal orc concept fitted to the brute torso with mismatched horned shoulders."""
    from prefabs.outfits.primal_costumes import orc_king as make
    return make(torso,belt_y,belt_height)


# Keep the original reference identifier stable; its gold and sunburst define the paladin role.
VARIANTS = {"knight_plate": knight_plate, "paladin": knight_plate, "necromancer": necromancer,
            "knight": knight, "maiden": maiden, "dark_knight": dark_knight,
            "segmented":segmented,"brigandine":brigandine,"gambeson":gambeson,
            "chain_mail":chain_mail,"crusader":crusader,"scale":scale,
            "ranger":ranger,"scout":scout,"arcanist":arcanist,"adventurer":adventurer,"barbarian_primitive":barbarian_primitive,"orc_king":orc_king}


def build(torso, variant="knight_plate", **options):
    """Named pieces retain their attachment bones through character assembly and export."""
    return VARIANTS[variant](torso, **options)
