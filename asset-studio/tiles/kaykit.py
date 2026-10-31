"""KayKit Dungeon Remastered tiles (CC0) as studio pieces, so derived tiles keep the original footprint.

Each face takes the slot of the texture swatch its UVs sample, and each corner keeps where it sits on
that swatch's vertical gradient (`uv_t`), so the studio palette reproduces KayKit's own shading:
light caps, the pale plinth band, darker bases. `gradients()` gives every swatch's exact gradient. Authored normals are kept. The readers are shared with the decor prefabs.
"""

from functools import lru_cache

from prefabs.decor import DUNGEON as SOURCE, gradients, swatch_piece  # noqa: F401


@lru_cache(maxsize=None)
def load(name):
    """(piece, colors) for one KayKit dungeon tile; nodes must carry no transforms (true for the walls)."""
    piece, colors, _ = swatch_piece(SOURCE / f"{name}.gltf")
    return piece, colors
