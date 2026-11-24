"""Color math shared by the Studio and the Blender reconstruction scripts.

Numpy only: Blender's bundled Python imports this module directly, and it has none of the Studio's other dependencies
(PIL, scipy) that `core.mesh` and `core.export` load.
"""

import numpy as np


def srgb_to_lab(rgb):
    """Converts sRGB `(..., 3)` 0-255 values to CIE L*a*b* (D65)."""
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    xyz = (c @ m.T) / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16.0 / 116.0)
    return np.stack([116.0 * f[..., 1] - 16.0, 500.0 * (f[..., 0] - f[..., 1]), 200.0 * (f[..., 1] - f[..., 2])], -1)


def ramp_coordinates(heights, up_normals, height_range=None, full_span=0.08):
    """Palette ramp positions (0 highlight, 1 shadow) from height and surface slope.

    Height sets the position relative to `height_range` (the extent of `heights` by default), its weight growing with
    the span up to `0.85 * full_span`, so small pieces shade flatter; faces turned down shade darker and faces turned
    up lighter.
    """
    heights = np.asarray(heights)
    low, high = height_range or (heights.min(), heights.max())
    span = max(high - low, 1e-4)
    weight = float(np.clip(span / full_span, 0.25, 0.85))
    centered = 0.5 + ((1.0 - (heights - low) / span) - 0.5) * weight
    slope = np.clip(-np.asarray(up_normals) * 0.35, -0.25, 0.25)
    return np.clip(centered + slope, 0.06, 0.94)
