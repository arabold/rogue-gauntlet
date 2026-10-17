"""Surface detailing and battle damage: gashes, weathering, gouges, and paint cuts.

Provides procedural operations that cut into geometry or displace vertices to give
weapons and armor a battle-worn appearance.
"""

from core.kit import (
    Frame,
    carve,
    gash,
    paint_cut,
    paint_cut_local,
    weather,
)

WEAR = {
    "weather": weather,
    "gash": gash,
    "carve": carve,
    "paint_cut": paint_cut,
    "paint_cut_local": paint_cut_local,
}


def catalog():
    """Yield battle wear operations applied to sample plates."""
    from core import kit
    base_plate = kit.bevel_box((0.4, 0.4, 0.08), 0.02, "armor")
    frame = Frame((0.0, 0.0, 0.04), [1, 0, 0], [0, 1, 0], [0, 0, 1])

    yield "weathered plate", weather(base_plate, "armor", worn_slot="armor_dark", rust_slot="rust"), None, {
        "armor": (130, 140, 150), "armor_dark": (90, 95, 105), "rust": (160, 80, 45)
    }
    yield "slashed plate", gash(base_plate, frame, 0.28, 0.06, 0.03, slot="armor_dark"), None, {
        "armor": (130, 140, 150), "armor_dark": (70, 75, 80)
    }
