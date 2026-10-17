"""Body parts and creature anatomy prefabs: heads, faces, bodies, limbs, extremities, and appendages."""

from . import appendages, bodies, extremities, faces, heads, limbs

__all__ = ["appendages", "bodies", "extremities", "faces", "heads", "limbs"]


def catalog():
    """Yield all anatomy studies (heads, bodies, appendages) for catalog rendering."""
    yield from heads.catalog()
    yield from bodies.catalog()
    yield from appendages.catalog()
