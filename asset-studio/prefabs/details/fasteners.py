"""Hardware and fastener details: studs, buckles, shackles, and chain links.

Provides modular hardware greebles that can be fitted to belts, armor plates, cuffs,
and garments.
"""

from core.kit import stud
from prefabs.outfits.armor import straps
from prefabs.outfits.belts import chain_link, frame_buckle, plate_buckle
from prefabs.outfits.clothes import cuff, necklace, shackle

FASTENERS = {
    "stud": stud,
    "frame_buckle": frame_buckle,
    "plate_buckle": plate_buckle,
    "chain_link": chain_link,
    "shackle": shackle,
    "cuff": cuff,
    "straps": straps,
    "necklace": necklace,
}


def catalog():
    """Yield representative fasteners and hardware studies."""
    from prefabs.anatomy import bodies as body
    hosts, _ = body.build({"torso": "standard"})
    t = hosts["torso"]
    a = hosts["arm"]

    yield "round stud", stud(0.04, 0.015, "gold"), None, {"gold": (230, 180, 50)}
    yield "frame buckle", frame_buckle(0.12, "metal"), None, {"metal": (180, 190, 200)}
    yield "plate buckle", plate_buckle(0.14, "metal", "gold"), None, {"metal": (180, 190, 200), "gold": (230, 180, 50)}
    yield "chain link", chain_link(0.06, 0.03, 0.008, "metal"), None, {"metal": (180, 190, 200)}
    yield "wrist shackle", shackle(a, at=0.82, links=3), a.place(a.local_mesh()), {"rust": (150, 80, 45)}
    yield "tooth necklace", necklace(t, t.top - 0.08, count=9), t.mesh(), {"cord": (40, 30, 25), "tooth": (230, 220, 200), "bead": (180, 50, 40)}
