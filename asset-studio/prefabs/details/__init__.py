"""Small accents, fasteners, and hardware details: buckles, studs, trophies, and surface wear."""

from . import fasteners, trophies, wear

__all__ = ["fasteners", "trophies", "wear"]


def catalog():
    """Yield all decorative accents, hardware, and surface wear studies."""
    yield from fasteners.catalog()
    yield from trophies.catalog()
    yield from wear.catalog()
