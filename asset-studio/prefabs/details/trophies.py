"""Decorative trophies, jewels, and accessories: mini skulls, gems, pouches, and brooches.

Provides modular decorative accents for belts, bandoliers, helmets, and armor plates.
"""

from prefabs import library
from prefabs.outfits.armor import emblem
from prefabs.outfits.belts import (
    pouch,
    sash_knot,
    scarab,
    skull,
)


def skull_trophy(center=(0.0, 0.0, 0.0), size=0.2, slot="bone", jaw=True):
    """Load a stylized skull model scaled and placed at `center`."""
    name = "skull_complete" if jaw else "skull_cranium"
    return library.load(name, slot=slot, scale=size, offset=center)


def gem_trophy(center=(0.0, 0.0, 0.0), size=0.15, slot="gem"):
    """Load a multi-faceted diamond crystal scaled and placed at `center`."""
    return library.load("gem_diamond", slot=slot, scale=size, offset=center)


def horn_trophy(center=(0.0, 0.0, 0.0), size=0.25, slot="horn"):
    """Load a curved beast horn scaled and placed at `center`."""
    return library.load("horn_curved", slot=slot, scale=size, offset=center)


TROPHIES = {
    "skull_trophy": skull_trophy,
    "gem_trophy": gem_trophy,
    "horn_trophy": horn_trophy,
    "skull_buckle": skull,
    "scarab_clasp": scarab,
    "belt_pouch": pouch,
    "sash_knot": sash_knot,
    "shield_emblem": emblem,
}


def catalog():
    """Yield decorative trophy and accent studies."""
    yield "skull trophy", skull_trophy(size=0.3), None, {"bone": (230, 224, 210)}
    yield "gem trophy", gem_trophy(size=0.25), None, {"gem": (70, 220, 240)}
    yield "horn trophy", horn_trophy(size=0.35), None, {"horn": (225, 210, 180)}
    yield "skull buckle", skull(0.18), None, {"bone": (230, 224, 210)}
    yield "scarab clasp", scarab(0.16), None, {"gold": (230, 180, 50), "gem": (40, 180, 220)}
    yield "belt pouch", pouch(0.18, "leather", "leather_dark", "gold"), None, {
        "leather": (140, 95, 65), "leather_dark": (90, 60, 40), "gold": (230, 180, 50)
    }
    yield "sash knot", sash_knot(0.15, "cloth"), None, {"cloth": (170, 50, 50)}
