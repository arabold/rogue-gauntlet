"""Wearable equipment prefabs: armor, helmets, cuirass, pauldrons, belts, crowns, and clothes."""

from . import armor, belts, capes, clothes, costumes, crowns, cuirass, helmets, neckwear, pauldrons

__all__ = ["armor", "belts", "capes", "clothes", "costumes", "crowns", "cuirass", "helmets", "neckwear", "pauldrons"]


def catalog():
    """Yield reusable wearable parts for catalog rendering."""
    yield from helmets.catalog()
    yield from pauldrons.catalog()
    yield from belts.catalog()
    yield from crowns.catalog()
