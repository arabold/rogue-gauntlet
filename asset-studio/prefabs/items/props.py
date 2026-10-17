"""Held props and consumable item prefabs: spellbooks, tomes, flasks, and potions.

Provides modular held props loaded from the uncolored mesh library with on-demand
slot mapping, scaling, and orientation.
"""

from prefabs import library


def spellbook(slot="leather", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Closed leather-bound spellbook with corner brackets and clasp."""
    return library.load("tome_spellbook", slot=slot, scale=scale, offset=offset, **kw)


def spellbook_open(slot="leather", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Open grimoire / spellbook displaying parchment pages."""
    return library.load("tome_open", slot=slot, scale=scale, offset=offset, **kw)


def potion_flask(slot="glass", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Alchemical potion flask with cork stopper and liquid chamber."""
    return library.load("flask_potion", slot=slot, scale=scale, offset=offset, **kw)


PROPS = {
    "spellbook": spellbook,
    "spellbook_open": spellbook_open,
    "potion_flask": potion_flask,
}


def catalog():
    """Yield all props for contact-sheet catalog rendering."""
    colors = {
        "leather": (135, 90, 60),
        "glass": (90, 180, 230),
    }
    yield "spellbook closed", spellbook(), None, colors
    yield "spellbook open", spellbook_open(), None, colors
    yield "potion flask", potion_flask(), None, colors
