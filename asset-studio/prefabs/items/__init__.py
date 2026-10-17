"""Items and equipment prefabs: weapons, shields, and held props."""

from . import props, shields, weapons

__all__ = ["weapons", "shields", "props"]


def catalog():
    """Yield all equipment items (weapons, shields, props) for catalog rendering."""
    yield from weapons.catalog()
    yield from shields.catalog()
    yield from props.catalog()
