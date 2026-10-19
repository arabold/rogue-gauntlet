"""Wearable equipment prefabs: armor, helmets, cuirass, pauldrons, belts, crowns, and clothes."""

from . import armor, belts, capes, clothes, costumes, crowns, cuirass, helmets, neckwear, pauldrons

__all__ = ["armor", "belts", "capes", "clothes", "costumes", "crowns", "cuirass", "helmets", "neckwear", "pauldrons"]


def catalog():
    """Yield representative outfit and armor studies for catalog rendering."""
    yield from helmets.catalog()
    yield from cuirass.catalog()
    yield from armor.catalog()
    yield from pauldrons.catalog()
    yield from belts.catalog()
    yield from crowns.catalog()
