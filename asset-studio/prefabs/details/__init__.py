"""Small accents, fasteners, and hardware details: buckles, studs, trophies, hilts, and surface wear."""

from . import fasteners, trophies, wear, construction, heraldry, primal, fur, hilts

__all__ = ["fasteners", "trophies", "wear", "construction", "heraldry", "primal", "fur", "hilts"]


def catalog():
    """Yield all decorative accents, hardware, and surface wear studies."""
    yield from fasteners.catalog()
    yield from trophies.catalog()
    yield from wear.catalog()
    yield from construction.catalog()
    yield from heraldry.catalog()
    yield from primal.catalog()
    yield from hilts.catalog()
