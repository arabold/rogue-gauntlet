"""Weapons item prefabs: swords, axes, daggers, crossbows, and wands.

Provides modular weapon prefabs loaded from the uncolored mesh library with on-demand
slot mapping, scaling, and orientation.
"""

from prefabs import library


def sword_1h(slot="metal", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Single-handed arming sword with crossguard and pommel."""
    return library.load("sword_1h", slot=slot, scale=scale, offset=offset, **kw)


def sword_2h(slot="metal", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Two-handed greatsword with fuller and flared crossguard."""
    return library.load("sword_2h", slot=slot, scale=scale, offset=offset, **kw)


def axe_1h(slot="metal", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Single-handed battleaxe with bearded cutting blade."""
    return library.load("axe_1h", slot=slot, scale=scale, offset=offset, **kw)


def axe_double(slot="metal", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Two-handed double-bitted war axe with reinforced shaft."""
    return library.load("axe_double", slot=slot, scale=scale, offset=offset, **kw)


def dagger(slot="metal", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Assassin's dagger with tapered blade and wrapped hilt."""
    return library.load("dagger_assassin", slot=slot, scale=scale, offset=offset, **kw)


def wand(slot="wood", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Arcane wand / rod with gem-ready tip."""
    return library.load("wand_arcane", slot=slot, scale=scale, offset=offset, **kw)


def crossbow(slot="wood", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """One-handed light crossbow with stirrup and trigger mechanism."""
    return library.load("crossbow_1h", slot=slot, scale=scale, offset=offset, **kw)


WEAPONS = {
    "sword_1h": sword_1h,
    "sword_2h": sword_2h,
    "axe_1h": axe_1h,
    "axe_double": axe_double,
    "dagger": dagger,
    "wand": wand,
    "crossbow": crossbow,
}


def catalog():
    """Yield all weapons for contact-sheet catalog rendering."""
    colors = {
        "metal": (175, 185, 195),
        "wood": (135, 95, 60),
    }
    yield "1h sword", sword_1h(), None, colors
    yield "2h sword", sword_2h(), None, colors
    yield "1h axe", axe_1h(), None, colors
    yield "double axe", axe_double(), None, colors
    yield "dagger", dagger(), None, colors
    yield "wand", wand(), None, colors
    yield "crossbow", crossbow(), None, colors
