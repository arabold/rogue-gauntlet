"""
Pauldrons (shoulder plates): a shell over the shoulder, sloping down the arm, in variants (`VARIANTS`). Built for
the character's left shoulder (`build(top, size, ...)`, the arm along +x like stylized's T-pose) and mirrored for the
right (`kit.Piece.mirrored`). Material slots: `plate`, `trim`, `rivet`, `spike`, `fur`, `bone`.
"""

import numpy as np

from core import kit
from prefabs.outfits.belts import skull


def _shell(size, slot, lat=-8.0, thickness=0.03, scale=(1.0, 0.7, 0.95), start=0.0, sides=22):
    rx, ry, rz = (size * k for k in scale)
    return kit.cap(rx, ry, rz, lat, thickness, slot, sides=sides, rings=8, p=2.4, start=start)


def _rim(size, slot, lat=-2.0, scale=(1.0, 0.7, 0.95), width=0.03, height=0.055, drop=0.0, flare=1.0, sides=22):
    """A trim band on the shell just above its lower edge (`flare` > 1 widens it into a turned-out lip)."""
    rx, ry, rz = (size * k for k in scale)
    L = np.radians(lat)
    th = 2 * np.pi * np.arange(sides) / sides
    c, s = np.cos(th), np.sin(th)
    ux, uz = np.sign(c) * np.abs(c) ** (2 / 2.4), np.sign(s) * np.abs(s) ** (2 / 2.4)
    path = np.column_stack([flare * rx * np.cos(L) * ux, np.full(sides, ry * np.sin(L) - drop),
                            flare * rz * np.cos(L) * uz])
    return kit.sweep(path, kit.rect_profile(width, height, 0.3 * height), slot, closed=True)


def _spikes(size, slot, count=3, height=0.55, radius=0.12, along=0.55, lean=0.58):
    """Spikes along the shell's top ridge, from the neck side out along the arm, leaning outward."""
    out = []
    xs = [along * size] if count == 1 else np.linspace(-along, along, count) * size
    for i, x in enumerate(xs):
        y = 0.7 * size * np.sqrt(max(0.0, 1 - (x / size) ** 2)) - 0.01
        h = height * size * (0.78 if count == 1 else (1.0 if i == count // 2 else 0.75))
        out.append(kit.spike((x, y, 0.0), (x + lean * h, y + h, 0.0), radius * size, slot))
    return out


def _dome_frame(size, deg_from_x, lat_deg, scale=(1.0, 0.7, 0.95)):
    """A surface `Frame` (`z` = outward normal, `y` = up toward dome crown) on `_shell` at azimuth `deg_from_x`
    (0 = +x outer arm, 90 = +z front) and latitude `lat_deg`."""
    rx, ry, rz = (size * k for k in scale)
    a, b = np.radians(deg_from_x), np.radians(lat_deg)
    ca, sa = np.cos(a), np.sin(a)
    ux = np.sign(ca) * np.abs(ca) ** (2 / 2.4)
    uz = np.sign(sa) * np.abs(sa) ** (2 / 2.4)
    P = np.array([rx * np.cos(b) * ux, ry * np.sin(b), rz * np.cos(b) * uz])
    N = np.array([np.cos(b) * ca / max(rx, 1e-6), np.sin(b) / max(ry, 1e-6), np.cos(b) * sa / max(rz, 1e-6)])
    N /= np.linalg.norm(N)
    return kit.Frame.make(P, N, up=(0.0, 1.0, 0.0))


def _weather_and_dent_shell(shell, size, base_slot, patina=False, dents=False, stitched=None):
    """Apply multi-tone patina gradients, polygonal stain patches, and 3D carved V-groove gashes to a pauldron dome."""
    from core.export import corner_normals
    shell.normals = corner_normals(shell.verts, shell.faces, crease=60.0)
    is_leather = "leather" in base_slot or "hide" in base_slot
    light_s = f"{base_slot}_light"
    worn_s = f"{base_slot}_worn"
    dark_s = f"{base_slot}_dark"
    stain_s = "grime" if is_leather else "tarnish"
    rust_s = "dirt" if is_leather else "rust"
    paintable = {base_slot, light_s, worn_s, dark_s, stain_s, rust_s}

    if patina:
        shell = kit.weather(shell, base_slot, light_slot=light_s, worn_slot=worn_s, dark_slot=dark_s, rust_slot=rust_s)
        # Polygonal patina patches on lower slopes (flush with surface, sharing smooth normals)
        p1 = np.array([(-0.11, -0.05), (0.09, -0.07), (0.14, 0.04), (-0.04, 0.08)]) * size
        shell = kit.paint_cut_local(shell, _dome_frame(size, 48.0, 7.0), [p1], dark_s, paintable=paintable)
        p2 = np.array([(-0.09, -0.04), (0.08, -0.05), (0.11, 0.05), (-0.05, 0.06)]) * size
        shell = kit.paint_cut_local(shell, _dome_frame(size, -48.0, 10.0), [p2], stain_s, paintable=paintable)

    d_cnt, d_scale = kit.parse_dents(dents, max_allowed=2)
    if size < 0.22:
        d_cnt = min(d_cnt, 1)

    if d_cnt >= 1:
        use_stitch = is_leather if stitched is None else bool(stitched)
        # 1. Main dramatic battle slash across the upper front-outer dome (longer, bold and exaggerated)
        f1 = _dome_frame(size, 42.0, 29.0).rotated(34.0, "z")
        shell = kit.gash(shell, f1, length=0.52 * size * d_scale, width=0.135 * size * d_scale,
                         depth=0.038 * size * d_scale,
                         slot=worn_s, shadow_slot=dark_s, paintable=paintable,
                         stitch_slot="stitch" if use_stitch else None)
    if d_cnt >= 2:
        # 2. Secondary horizontal wedge dent on the outer flank
        f2 = _dome_frame(size, 18.0, 16.0).rotated(82.0, "z")
        shell = kit.gash(shell, f2, length=0.36 * size * d_scale, width=0.115 * size * d_scale,
                         depth=0.032 * size * d_scale,
                         slot=worn_s, shadow_slot=dark_s, paintable=paintable, asymmetry=0.42)
    return shell


def _fur(size, slot, count=16):
    """A thick ruff of fur along the shell's neck side: two rows of short, wide tufts fanning out and up."""
    out = []
    for row, (r, lift, length) in enumerate(((0.8, 0.2, 0.32), (0.95, 0.05, 0.26))):
        for i in range(count):
            a = np.pi * (0.45 + 1.1 * (i + 0.5 * row) / count)  # the half facing the neck (-x)
            base = np.array([r * size * np.cos(a), lift * size, r * 0.95 * size * np.sin(a)])
            out_dir = np.array([np.cos(a), 0.0, np.sin(a)])
            tip = base + length * size * (0.55 * out_dir + [0.0, 0.85, 0.0]) * (0.75 + 0.5 * ((i * 7 + row) % 3) / 2)
            out.append(kit.spike(base, tip, 0.2 * size, slot, sides=3, twist=37.0 * i))
    return out


VARIANTS = {
    "dome": dict(plate="metal", rim="dark", rivet="dark"),
    "layered": dict(plate="metal", lames=3, rim="dark"),
    "spiked": dict(plate="dark", rim="metal", spikes="metal"),
    "flared": dict(plate="metal", rim="metal", flare=1.18, spikes="metal", spike_count=2, rivet="gold"),
    "fur": dict(plate="leather", rim="dark", fur="fur"),
    "skull": dict(plate="dark", rim="bone", skull="bone"),
}


def build(top, size=0.24, variant="dome", tilt=-15.0, **overrides):
    """
    The left pauldron `variant` with the top of its shell at `top` (rig units), `size` its half width along the arm,
    sloping `tilt` degrees down the arm in the T-pose. It turns half as far as the arm, so it is authored tipped up
    (`tilt` < 0): with the arms resting at the sides, it lies close to level on the shoulder, like stylized's.
    `overrides` replace the variant's parameters.
    """
    v = {**VARIANTS[variant], **overrides}
    parts = []
    lames = v.get("lames", 1)
    for i in range(lames):  # each lower lame steps out along the arm and down, a little wider
        k = 1.0 + 0.16 * i
        shell = _shell(size * k, v["plate"], start=0.0 if i == 0 else 30.0 + 12 * i)
        if v.get("patina") or v.get("dents"):
            shell = _weather_and_dent_shell(shell, size * k, v["plate"], patina=bool(v.get("patina")),
                                            dents=v.get("dents") if i == 0 else False, stitched=v.get("stitched"))
        rim = _rim(size * k, v["rim"]) if v.get("rim") else None
        if rim is not None and v.get("patina"):
            is_l = "leather" in v["rim"] or "hide" in v["rim"]
            rim = kit.weather(rim, v["rim"], worn_slot="grime" if is_l else "tarnish",
                              rust_slot="dirt" if is_l else "rust", seed=13 + i)
        lame = kit.Piece.merge(shell, rim)
        parts.append(kit.Piece(lame.verts + [0.22 * size * i, -0.2 * size * i, 0.0], lame.faces, lame.slots,
                               normals=lame.normals))
    if v.get("flare"):
        parts.append(_rim(size, v["rim"], flare=v["flare"], width=0.07, drop=0.02))
    if v.get("spikes"):
        spikes = _spikes(size, v["spikes"], count=v.get("spike_count", 3), height=v.get("spike_height", 0.55),
                         along=v.get("spike_along", 0.55), lean=v.get("spike_lean", 0.58))
        if v.get("patina"):
            spikes = [kit.weather(sp, v["spikes"], light_slot=f"{v['spikes']}_light", worn_slot=f"{v['spikes']}_worn",
                                  dark_slot=f"{v['spikes']}_dark", rust_slot="tarnish", seed=19 + j)
                      for j, sp in enumerate(spikes)]
        parts += spikes
    if v.get("rivet"):
        parts.append(kit.stud(0.12 * size, 0.02, v["rivet"]).moved(
            kit.Frame((0.0, 0.7 * size - 0.004, 0.0), [1, 0, 0], [0, 0, -1], [0, 1, 0])))
    if v.get("fur"):
        parts += _fur(size, v["fur"])
    if v.get("skull"):
        parts.append(skull(0.8 * size, v["skull"]).moved(  # on the outer slope, looking out along the arm
            kit.Frame((0.5 * size, 0.5 * size, 0.0), [0, 0, -1], [0, 1, 0], [1, 0, 0]).rotated(-40, "x")))
    piece = kit.Piece.merge(*parts)
    frame = kit.Frame(np.asarray(top, float) - [0.0, 0.7 * size, 0.0], [1, 0, 0], [0, 1, 0], [0, 0, 1]).rotated(
        -tilt, "z")
    return piece.moved(frame)


def catalog():
    """Every variant on the mannequin's left shoulder (`catalog.shoulder_stand`, mirrored to the left)."""
    from core.catalog import shoulder_stand
    stand = shoulder_stand().mirrored()
    for name in VARIANTS:
        yield name, build((0.38, 1.33, 0.0), variant=name), stand


def _leather_support(pieces,mount_x,sign):
    """A thin strap fitted to actual clothed shoulder hits, from breast to back."""
    from items import solid
    surface = kit.Piece.merge(*pieces)
    angles = np.linspace(-.13,np.pi+.13,25)
    end_x = min(mount_x,.20)
    x = sign*(end_x+(mount_x-end_x)*np.clip(np.sin(angles),0,1)**2)
    centers = np.column_stack([x,np.full(len(x),1.107),np.zeros(len(x))])
    outward = np.column_stack([np.zeros(len(x)),np.sin(angles),np.cos(angles)])
    origins = centers+.65*outward
    hit = kit.ray_distance(origins,-outward,surface.verts,surface.faces,reach=.65)
    if not np.isfinite(hit).all():
        raise ValueError("No shoulder surface for fitted leather strap")
    path = origins-hit[:,None]*outward+.004*outward
    strap = kit.sweep(path,kit.rect_profile(.065,.016,.004),"leather",up=(0,0,1))
    pivot = path[len(path)//2]
    pad = kit.lump(pivot,(.035,.014,.034),"leather",n=10,lat=(-60,-30,0,30,60))
    return solid.fuse_closed(kit.Piece.merge(strap,pad)),pivot


def steel_assembly(torso,pieces,size=.18,gorget=True,leather_straps=False):
    """Rigid steel caps on a gorget or independent shoulder arches, with baked half-arm hinges."""
    from items import solid
    from prefabs.outfits import neckwear
    from prefabs.outfits.costumes import _bound
    emblems = [p for p in pieces if "gem" in set(p.slots)]
    front_drop = min(.105,1.135-max(p.verts[:,1].max() for p in emblems)-.012) if emblems else .105
    supports = neckwear.torso_supports(pieces)
    guard = neckwear.gorget(torso,pieces=supports,front_drop=front_drop)
    result = {"CostumeGorget":guard} if gorget else {}
    side_vertices = guard.verts[np.abs(guard.verts[:,2])<.02]
    mount_x = .86*float(np.max(np.abs(side_vertices[:,0])))
    for side,sign in (("Left",1),("Right",-1)):
        x = sign*mount_x
        hit = kit.ray_distance(np.array([[x,1.65,0.]]),np.array([[0.,-1.,0.]]),guard.verts,guard.faces,reach=.6)[0]
        if not np.isfinite(hit):
            raise ValueError(f"No gorget surface for {side} shoulder mount")
        pivot = np.array([x,1.65-hit-.009,0.])
        mount = kit.lump(pivot,(.049,.029,.052),"trim",n=10,lat=(-60,-30,0,30,60))
        if not gorget and leather_straps:
            # A removable cape is not a structural shoulder attachment.
            mount,pivot = _leather_support([p for p in pieces
                if getattr(p,"garment_type",None)!="cape"],mount_x,sign)
        elif not gorget:
            # Independent shoulder arches attach to the front/back plates, leaving the neck open.
            support = kit.Piece.merge(*supports)
            end_x = sign*min(mount_x,.20)
            ends = []
            for facing in (1,-1):
                origin = np.array([[end_x,1.08,float(facing)]])
                direction = np.array([[0.,0.,-float(facing)]])
                distance = kit.ray_distance(origin,direction,support.verts,support.faces,reach=1.)[0]
                if not np.isfinite(distance):
                    raise ValueError(f"No plate surface for {side} shoulder arch")
                ends.append(origin[0]+distance*direction[0])
            front,back = ends
            path = np.array([front,[end_x,1.19,front[2]+.015],[x,1.27,.12],pivot,
                             [x,1.27,-.12],[end_x,1.19,back[2]-.015],back])
            arch = kit.sweep(path,kit.rect_profile(.070,.025,.007),"armor",up=(0,1,0))
            mount = solid.fuse_closed(kit.Piece.merge(arch,mount))
        result[f"CostumeShoulderMount{side}"] = _bound(mount,"chest")
        # Reuse the existing shell's actual thick dome; trim colors its rolled lower edge.
        cap = _shell(size,"armor",lat=-8,scale=(1.,.78,1.07),sides=18)
        centers = cap.verts[cap.faces].mean(1)
        cap.slots[centers[:,1]<.025] = "trim"
        center = pivot+np.array([sign*.145,-.10 if leather_straps and not gorget else -.12,0.])
        if sign<0:
            cap = cap.mirrored()
        cap.verts += center
        # A short broad neck-side lug connects the cap to a spherical pivot bearing.
        radius = .022 if leather_straps and not gorget else .032
        hub = kit.lump(pivot,(radius,radius,radius),"metal",n=10,lat=(-60,-30,0,30,60))
        leather = leather_straps and not gorget
        path = np.array([pivot,pivot+[sign*.07,.010 if leather else -.012,0],
                         pivot+[sign*.14,.025 if leather else -.014,0]])
        lug = kit.sweep(path,kit.rect_profile(.050,.018,.005) if leather else kit.rect_profile(.056,.035,.009),
                       "leather" if leather else "armor",up=(0,1,0))
        cap = solid.fuse_closed(kit.Piece.merge(cap,hub,lug))
        name = "shoulder_cap.l" if sign>0 else "shoulder_cap.r"
        cap.bone = name
        cap.hinge = dict(parent="chest",follow="upperarm.l" if sign>0 else "upperarm.r",
                         share=.5,pivot=pivot.tolist())
        result[f"CostumePauldron{side}"] = cap
    return result
