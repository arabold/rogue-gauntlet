"""Shields item prefabs: heater, round, kite, and spiked shields.

Provides modular shield prefabs loaded from the uncolored mesh library with on-demand
slot mapping, scaling, and orientation.
"""

from prefabs import library


def shield_heater(slot="armor", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Classic medieval heater shield with curved top and pointed base."""
    return library.load("shield_heater", slot=slot, scale=scale, offset=offset, **kw)


def shield_kite(slot="armor", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Long cavalry / infantry kite tower shield for full-body protection."""
    return library.load("shield_kite", slot=slot, scale=scale, offset=offset, **kw)


def shield_round(slot="armor", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Viking / barbarian round shield with center boss and reinforced rim."""
    return library.load("shield_round", slot=slot, scale=scale, offset=offset, **kw)


def shield_spiked(slot="armor", scale=1.0, offset=(0.0, 0.0, 0.0), **kw):
    """Aggressive spiked boss shield with offensive studs and flanges."""
    return library.load("shield_spiked", slot=slot, scale=scale, offset=offset, **kw)


SHIELDS = {
    "shield_heater": shield_heater,
    "shield_kite": shield_kite,
    "shield_round": shield_round,
    "shield_spiked": shield_spiked,
}


def catalog():
    """Yield all shields for contact-sheet catalog rendering."""
    colors = {
        "armor": (130, 140, 155),
        "metal": (180, 190, 200),
    }
    yield "heater shield", shield_heater(), None, colors
    yield "kite shield", shield_kite(), None, colors
    yield "round shield", shield_round(), None, colors
    yield "spiked shield", shield_spiked(), None, colors
