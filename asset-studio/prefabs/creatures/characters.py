"""
Whole characters from the kit: a head (`heads`), a body (`body`), clothes (`clothes`) and accessories (`belts`,
`crowns`, `pauldrons`), one recipe per character in its own folder (`CHARACTERS/<family>/<name>/character.json`,
`load`). The result is stylized's split (`Head`, `Body`, `ArmLeft`,
`ArmRight`, `LegLeft`, `LegRight`) plus one piece per garment and accessory, so a character can be dressed and
undressed piece by piece.

An outfit is a list of garments, each `dict(kind=..., ...)` with that kind's arguments (`GARMENTS`), worn in
order: the hosts (torso, arm, leg, head) are filled in, each garment grows its host (`clothes.Worn`) so the next one
sits on top of it, and limb garments come in pairs.

A character's folder also keeps its concept drawing (`reference.png`) and previews; `studio.py` builds,
previews and exports it (see there for the recipe's export entries).
"""

import json
import os

import numpy as np


from core import kit
from prefabs.anatomy import bodies as body, heads
from prefabs.outfits import armor, belts, clothes, costumes, crowns, cuirass, helmets, neckwear, pauldrons

CHARACTERS = os.path.normpath(os.path.join(os.path.dirname(__file__), "../../characters"))


def _pair(piece):
    return kit.Piece.merge(piece, piece.mirrored())


def _sided(piece, side):
    """A piece built on the character's left worn on the `left`, the `right` (mirrored) or `both` sides: a single
    pauldron, a mismatched pair as two garments."""
    return {"both": _pair(piece), "left": piece, "right": piece.mirrored()}[side]


def _worn(host, y0, y1, thickness, piece, side="all"):
    """`piece`, after recording that `host` now wears it from `y0` to `y1`, `thickness` deep on `side` (built first,
    so a garment never sits on top of itself)."""
    host.wear(y0, y1, thickness, side)
    return piece


def _robe(h, thickness=0.03, **k):
    t = h["torso"]
    return _worn(t, t.bottom, t.top, thickness, clothes.robe(t, t.top - 0.04, thickness=thickness, **k))


def _tunic(h, y0, y1=None, thickness=0.03, neck="v", **k):
    t = h["torso"]
    y1 = t.top - 0.04 if y1 is None else y1
    return _worn(t, y0, y1, thickness + max(k.get("flare", (0.0, 0.0))),
                 clothes.shell(t, y0, y1, thickness, neck=neck, **k))


def _on_limb(limb, y0, y1, thickness=0.03, **k):
    piece = _pair(clothes.on_limb(limb, y0, y1, thickness, **k))
    return _worn(limb, y0 * limb.length, y1 * limb.length, thickness + max(k.get("flare", (0.0, 0.0))), piece)


def _shorts(h, down=0.45, thickness=0.03, **k):
    piece = clothes.shorts(h["torso"], h["leg"], down=down, thickness=thickness, **k)
    h["leg"].wear(0.0, down * h["leg"].length, thickness + k.get("flare", 0.02))
    return _worn(h["torso"], h["torso"].bottom, k.get("top", 0.62), thickness, piece)


def _cuffs(h, limb="arm", at=0.9, width=0.08, thickness=0.03, side="both", **k):
    L = h[limb].length
    return _worn(h[limb], at * L - width / 2, at * L + width / 2, thickness,
                 _sided(clothes.cuff(h[limb], at, width, thickness, **k), side))


def _collar(h, height=0.12, thickness=0.05, **k):
    top = h["torso"].top - 0.04
    piece = clothes.collar(h["torso"], top, height=height, thickness=thickness, **k)
    return _worn(h["torso"], top - height * (5 if k.get("style") == "broad" else 1), top, thickness, piece)


def _wraps(h, limb=None, y0=0.0, y1=1.0, thickness=0.02, **k):
    host = h[limb or "torso"]
    span = host.length if limb else 1.0
    piece = clothes.wraps(host, y0, y1, thickness=thickness, **k)
    return _worn(host, y0 * span, y1 * span, 1.3 * thickness, _pair(piece) if limb else piece)


def _plate(h, side, y0=0.62, y1=None, thickness=0.045, **k):
    t = h["torso"]
    make = armor.breastplate if side == "front" else armor.backplate
    piece = make(t, y0, y1, thickness=thickness, **k)
    return _worn(t, y0, t.top - 0.06 if y1 is None else y1, thickness + 0.03, piece, side)


def _belt(h, y=0.66, height=0.13, thickness=0.04, taut=False, conform=False, fit_slot=None, **k):
    """A belt drawn tight round what the torso really wears there (`clothes.Fitted`), recorded as worn with room
    for its studs and fur, so a cape over it clears it."""
    support=h["built"]()
    if conform:
        piece=belts.build_fitted(h["torso"],support,y,height,thickness,fit_slot=fit_slot,**k)
    else:
        fitted=(costumes.BeltHost(h["torso"],support,y,band=.5*height+.01) if taut else
                clothes.Fitted(h["torso"],support,band=.5*height+.01))
        piece=belts.build(fitted,y,height,thickness,**k)
    return _worn(h["torso"],y-height/2,y+height/2,thickness+.03,piece)


def _boots(h, y0=0.55, thickness=0.03, size=1.15, slot="boot", sole="sole", side="both", **k):
    """A boot: a shoe over the foot (`body.foot`'s boot, a size up so it covers a bare foot) and a shaft up the leg;
    on one `side` only for a character who lost the other."""
    leg = h["leg"]
    shoe = body.foot("boot", size, slot, sole)
    shoe = leg.place(kit.Piece(shoe.verts + [0.0, leg.length - 0.03, 0.0], shoe.faces, shoe.slots))
    shaft = clothes.on_limb(leg, y0, 1.0, thickness, slot, **k)
    return _worn(leg, y0 * leg.length, leg.length, thickness + max(k.get("flare", (0.0, 0.0))),
                 _sided(kit.Piece.merge(shoe, shaft), side))


def _fur_trim(h, y, limb=None, **k):
    piece = clothes.fur_trim(h[limb or "torso"], y, **k)
    return _pair(piece) if limb else piece


def _armor(h, element, **k):
    """Any `armor.ELEMENTS` piece on the torso as worn (plates, lames, mail, straps, emblems...)."""
    t = h["torso"]
    piece = armor.ELEMENTS[element](t, **k)
    if "y0" in k:  # plates stand proud of their thickness by their sculpt and trim
        side = {"breastplate": "front", "backplate": "back"}.get(element, k.get("side", "all"))
        t.wear(k["y0"], k.get("y1", t.top - 0.06), k.get("thickness", 0.04) + (0.04 if element in PLATES else 0.0),
               side)
    return piece


PLATES = ("breastplate", "backplate", "plate", "lames", "scales", "quilted")


def _limb_plate(h, limb, y0, y1, thickness=0.035, **k):
    L = h[limb].length
    piece = _pair(armor.limb_plate(h[limb], y0, y1, thickness, **k))
    return _worn(h[limb], y0 * L, y1 * L, thickness + 0.012 + max(k.get("flare", (0.0, 0.0))), piece)


def _cuirass(h, variant="knight", **k):
    """Plate armor round the torso (`cuirass.VARIANTS`), worn with room for its rims and rivets."""
    t = h["torso"]
    piece = cuirass.build(t, variant, **k)
    return _worn(t, float(piece.verts[:, 1].min()), t.top, 0.1, piece)


def _gloves(h, style="mitten", size=1.14, slot="glove"):
    """Gloves: the hand's own shape a size up, over the bare hand."""
    arm = h["arm"]
    hand = body.hand(style, size, slot)
    return _pair(arm.place(kit.Piece(hand.verts + [0.0, arm.length - 0.025, 0.0], hand.faces, hand.slots)))


TORSO_PART_EXCLUSIONS=("Head", "Arm", "Leg", "Helmet", "Crown", "Cape", "Wings", "Tail")


def _costume_set(hosts, name):
    """Package an existing authored garment assembly as one costume without remaking its shapes.

    Named source parts retain the normal layer-host updates. The local built callback
    includes earlier parts with the assembler’s original fitting rules. Source garment
    names retain their skin-weight routing and cape/buckle handling after extraction.
    Nested costume sets are rejected rather than allowing recursive authoring references.
    """
    import json
    from pathlib import Path
    if not name or any(c not in "abcdefghijklmnopqrstuvwxyz0123456789_" for c in name):
        raise ValueError("Costume set name must use lowercase letters, digits and underscores")
    path=Path(__file__).resolve().parents[2]/"costumes"/name/"costume.json"
    spec=json.loads(path.read_text())
    parts={};local=dict(hosts);layer_parts=[]
    local["built"]=lambda:list(hosts["built"]())+[part for key,part in layer_parts
        if not key.startswith(TORSO_PART_EXCLUSIONS)]
    for i,entry in enumerate(spec["parts"]):
        garment=dict(entry);kind=garment.pop("kind")
        if kind=="costume_set":raise ValueError("Nested costume sets are not supported")
        made=GARMENTS[kind](local,**garment)
        if isinstance(made,dict):parts.update({f"Costume{name}{key}{i}":p for key,p in made.items()})
        else:
            made.cover_skin=kind in COVERS or (kind=="armor" and garment.get("element") in ("mail","quilted"))
            parts[garment.get("limb", "").capitalize()+kind.capitalize()+str(i)]=made
            layer_parts.append((garment.get("limb", "").capitalize()+kind.capitalize()+str(i),made))
    return parts


def _cape(hosts, hem=.2, width=62., slot="pelt", trim=None, fit_slot=None, **options):
    """One complete cape, including its continuous shoulder yoke and front bib."""
    from prefabs.outfits import capes
    supports=hosts["built"]()
    if fit_slot is not None:
        supports=[kit.Piece(p.verts,p.faces[p.slots==fit_slot],fit_slot)
                  for p in supports if np.any(p.slots==fit_slot)]
        if not supports:
            raise ValueError(f"Cape has no supporting surface in slot {fit_slot}")
    return capes.build(hosts["torso"],hem,width,slot,trim,pieces=supports,**options)


def _neckcloth(hosts, style="cowl", slot="pelt", trim="pelt_dark"):
    """Soft shoulder-spanning cape collar, fitted before the cape's upper roots."""
    torso=hosts["torso"]
    piece=costumes._bound(neckwear.collar(torso,style,slot,trim),"chest")
    torso.wear(.98,torso.top,.035)
    return piece


GARMENTS = {
    "baldric": lambda h, **options: neckwear.baldric(h["torso"],**options),
    "neckcloth": _neckcloth,
    "costume_set": _costume_set,
    "steel_shoulders": lambda h, size=.18, gorget=True, leather_straps=False: pauldrons.steel_assembly(
        h["torso"],h["built"](),size,gorget,leather_straps),
    "gorget": lambda h, slot="armor", trim="trim": {"CostumeGorget":neckwear.gorget(h["torso"],slot,trim,
        neckwear.torso_supports(h["built"]()))},
    "costume": lambda h, variant="knight_plate", **options: costumes.build(h["torso"], variant, **options),
    "robe": _robe,
    "limb_plate": _limb_plate,
    "cop": lambda h, limb, at, **k: _pair(armor.cop(h[limb], at, **k)),
    "gloves": _gloves,
    "tunic": _tunic,
    "sleeves": lambda h, y0=0.0, y1=0.85, **k: _on_limb(h["arm"], y0, y1, **k),
    "boots": _boots,
    "fur_trim": _fur_trim,
    "armor": _armor,
    "cape": lambda h, **options: _cape(h,**options),
    "helmet": lambda h, style="bear", **k: helmets.STYLES[style](h["head"], **k),
    "shorts": _shorts,
    "cuffs": _cuffs,
    "collar": _collar,
    "wraps": _wraps,
    "shackles": lambda h, **k: _pair(clothes.shackle(h["arm"], **k)),
    "breastplate": lambda h, **k: _plate(h, "front", **k),
    "backplate": lambda h, **k: _plate(h, "back", **k),
    "belt": _belt,
    "pauldrons": lambda h, x=0.4, y=1.27, side="both", **k: _sided(pauldrons.build((x, y, 0.0), **k), side),
    "crown": lambda h, y0=0.42, y1=0.6, **k: crowns.build(h["head"], h["head"].y(y0), h["head"].y(y1), **k),
    "apron": lambda h, **k: clothes.apron(h["torso"], **k),
    "wings": lambda h, y=1.05, **k: clothes.wings(h["torso"], y, **k),
    "tail": lambda h, y=0.6, **k: clothes.tail(h["torso"], y, **k),
    "ribs": lambda h, y0=0.8, y1=1.08, **k: clothes.ribs(h["torso"], y0, y1, **k),
    "cuirass": lambda h, variant="knight", **k: _cuirass(h, variant, **k),
    "necklace": lambda h, y=None, **k: clothes.necklace(h["torso"], h["torso"].top - 0.1 if y is None else y, **k),
    "loincloth": lambda h, y=0.6, hem=0.3, thickness=0.025, **k: _worn(
        h["torso"], hem, y, thickness, clothes.loincloth(h["torso"], y, hem, thickness=thickness, **k)),
}


def head_recipe(spec):
    """A head recipe from a character's `head`: a `heads.ROSTER` name, a full recipe, or a `preset` name with
    entries replacing the preset's."""
    if isinstance(spec, str):
        return heads.ROSTER[spec]
    if "preset" in spec:  # entries replace the preset's, except colors, which add to them
        base = heads.ROSTER[spec["preset"]]
        out = {**base, **{k: v for k, v in spec.items() if k != "preset"}}
        out["colors"] = {**base.get("colors", {}), **spec.get("colors", {})}
        return out
    return spec


# Garments snug and closed enough to completely hide the skin under them (`hide_covered`).
# Partial accessories, straps, bandages, belts, flaps, collars, cuffs, and single-sided plates
# never hide skin, so holes can never open in bare or partially-covered bodies.
COVERS = {"neckcloth", "cuirass", "robe", "tunic", "sleeves", "shorts", "boots", "gloves"}
BODY_PIECES = ("Body", "ArmLeft", "ArmRight", "LegLeft", "LegRight")


def hide_covered(pieces, covers, reach=0.14):
    """
    `pieces` with the skin under snug clothing removed: a body face goes when rays straight out of it (from its
    middle and near its corners) all meet one of the `covers` within `reach`. Skin left under cloth pokes through
    it in deep poses (the two are skinned a little differently); clothed characters in games hide it the same way.
    """
    cloth = kit.Piece.merge(*covers)
    if not len(cloth.faces):
        return pieces
    out = dict(pieces)
    for name in BODY_PIECES:
        p = pieces.get(name)
        if p is None:
            continue
        tri = p.verts[p.faces]
        n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
        c = tri.mean(1)
        covered = np.ones(len(tri), bool)
        for origin in (c, *(c + 0.8 * (tri[:, k] - c) for k in range(3))):
            d = kit.ray_distance(origin + 1e-4 * n, n, cloth.verts, cloth.faces, reach=reach)
            covered &= np.nan_to_num(d, nan=np.inf) < reach
        keep = ~covered
        norms = None if getattr(p, "normals", None) is None else p.normals[keep]
        out[name] = kit.Piece(p.verts, p.faces[keep], p.slots[keep], normals=norms)
    return out


def _expand_palette(colors, explicit):
    """Derive `<base>_light`, `<base>_worn`, `<base>_dark` and `tarnish` from a character's base colors unless
    explicitly set in `explicit`."""
    out = dict(colors)
    for base in ("metal", "leather", "hide", "armor", "iron", "bone", "cloth", "gold", "boot", "pants"):
        if base not in out:
            continue
        c = np.array(out[base], float)
        if f"{base}_light" not in explicit:
            out[f"{base}_light"] = tuple(np.round(np.clip(c * [1.15, 1.14, 1.12] + 8.0, 0, 255)).astype(int))
        if f"{base}_worn" not in explicit:
            out[f"{base}_worn"] = tuple(np.round(np.clip(c * [0.84, 0.81, 0.76], 0, 255)).astype(int))
        if f"{base}_dark" not in explicit:
            out[f"{base}_dark"] = tuple(np.round(np.clip(c * [0.65, 0.58, 0.48], 0, 255)).astype(int))
    if "metal" in out and "tarnish" not in explicit:
        c = np.array(out["metal"], float)
        out["tarnish"] = tuple(np.round(np.clip(c * [0.76, 0.72, 0.64], 0, 255)).astype(int))
    return out


def build(recipe):
    """`(pieces, colors)`: the named pieces of a character recipe (`head`: see `head_recipe`; `body`: a `body`
    recipe; `outfit`: garments) and the colors of every slot they use."""
    head_spec = head_recipe(recipe["head"])
    head, head_piece = heads.build(head_spec)
    hosts, pieces = body.build(recipe["body"])
    hosts = dict({k: clothes.Worn(v) for k, v in hosts.items()}, head=head)  # each layer goes on top of the last
    pieces = dict(Head=head_piece, **pieces)
    # what the torso wears so far, for garments drawn tight round it (belts): the body and torso garments
    hosts["built"] = lambda: [p for k, p in pieces.items()
                              if not k.startswith(TORSO_PART_EXCLUSIONS)]
    covers = []
    for i, garment in enumerate(recipe.get("outfit", [])):
        g = dict(garment)
        kind = g.pop("kind")
        limb = g.get("limb", "")  # a limb's garment is named for it, so it is skinned to the limbs alone
        piece = GARMENTS[kind](hosts, **g)
        if isinstance(piece, dict):
            # A coordinated outfit remains separate rigid plates and fittings in the rigged mesh.
            pieces.update({f"{name}{i}": part for name, part in piece.items()})
            covers.extend(part for part in piece.values() if getattr(part,"cover_skin",True))
            continue
        pieces[f"{limb.capitalize()}{kind.capitalize()}{i}"] = piece
        if kind in COVERS or (kind == "armor" and g.get("element") in ("mail", "quilted")):
            covers.append(piece)
    if any(getattr(p,"garment_type",None)=="cape" for p in pieces.values()) and any(
            "Gorget" in name for name in pieces):
        raise ValueError("Choose a cape or a gorget; the cape includes its own shoulder yoke")
    if recipe.get("hide_covered", True):
        pieces = hide_covered(pieces, covers)
    explicit = {**head_spec.get("colors", {}), **recipe.get("colors", {})}
    colors = _expand_palette(
        {**kit.DEMO_COLORS, **crowns.COLORS, **cuirass.COLORS, **heads.COLORS, **body.COLORS, **clothes.COLORS,
         **armor.COLORS, **helmets.COLORS, **explicit},
        explicit,
    )
    return pieces, colors


def keys(family=None):
    """Every character in `CHARACTERS` as `family/name`, sorted (of one `family`)."""
    out = []
    for fam in sorted(os.listdir(CHARACTERS)):
        if family not in (None, fam) or not os.path.isdir(os.path.join(CHARACTERS, fam)):
            continue
        out += [f"{fam}/{n}" for n in sorted(os.listdir(os.path.join(CHARACTERS, fam)))
                if os.path.exists(os.path.join(CHARACTERS, fam, n, "character.json"))]
    return out


def load(key):
    """The recipe in `CHARACTERS/<family>/<name>/character.json`. A `base` names another character whose recipe it
    extends (its own entries replace the base's: a bare base dressed as a barbarian or a knight)."""
    with open(os.path.join(CHARACTERS, key, "character.json"), encoding="utf-8") as f:
        recipe = json.load(f)
    if "base" in recipe:
        recipe = {**load(recipe["base"]), **{k: v for k, v in recipe.items() if k != "base"}}
    return recipe


def catalog():
    """Every character built from the kit (not the golems), whole."""
    for key in keys():
        recipe = load(key)
        if recipe.get("builder", "kit") == "kit":
            pieces, colors = build(recipe)
            yield key, kit.Piece.merge(*pieces.values()), None, colors
