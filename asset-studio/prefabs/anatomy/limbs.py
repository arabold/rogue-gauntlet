"""
Arm and leg limb hosts and proportion presets (`Limb`, `ARMS`, `LEGS`).
"""

import numpy as np

from core import kit


def _smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


class Limb:
    """An arm or a leg along its own vertical axis (root at y = 0, tip at `length`), through `rows` of half sizes
    (`_Profile`), placed at `root` pointing along `direction` with its front (local +z) toward `front`."""

    def __init__(self, rows, length, root, direction, front=(0.0, 0.0, 1.0), p=2.2, q=6.0):
        self.profile, self.length, self.p = _Profile(rows, q), length, p
        d = np.asarray(direction, float) / np.linalg.norm(direction)
        z = np.asarray(front, float) - (np.asarray(front, float) @ d) * d
        z /= np.linalg.norm(z)
        self.frame = kit.Frame(root, np.cross(d, z), d, z)

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        a, front, back = self.profile(y / self.length)
        return np.zeros(len(y)), a, (front - back) / 2, (front + back) / 2

    def local_mesh(self, slot="skin", sides=14, rings=14):
        return _closed_tube(self.section, 0.0, self.length, self.p, sides, rings, slot)

    def place(self, piece):
        """A piece built in this limb's own axes, moved onto it."""
        return piece.moved(self.frame)



ARMS = {  # (t from the shoulder to the wrist, half thickness across, front, back)
    "normal": [(0.0, 0.1, 0.1), (0.3, 0.085, 0.085), (0.48, 0.075, 0.075), (0.75, 0.078, 0.078), (1.0, 0.065, 0.065)],
    "thin": [(0.0, 0.075, 0.075), (0.3, 0.06, 0.06), (0.5, 0.05, 0.05), (0.75, 0.055, 0.055), (1.0, 0.045, 0.045)],
    "muscular": [(0.0, 0.16, 0.16), (0.3, 0.15, 0.155), (0.5, 0.115, 0.115), (0.72, 0.135, 0.13), (1.0, 0.1, 0.1)],
    "gorilla": [(0.0, 0.24, 0.24), (0.28, 0.22, 0.225), (0.48, 0.18, 0.185), (0.72, 0.225, 0.22), (1.0, 0.18, 0.17)],
    "bony": [(0.0, 0.06, 0.06), (0.25, 0.04, 0.04), (0.48, 0.06, 0.06), (0.7, 0.038, 0.038), (1.0, 0.05, 0.05)],
}
LEGS = {  # (t from the hip to the ankle, half thickness across, front, back)
    "normal": [(0.0, 0.11, 0.11), (0.35, 0.1, 0.1), (0.6, 0.085, 0.085), (0.78, 0.09, 0.092), (1.0, 0.07, 0.07)],
    "stocky": [(0.0, 0.14, 0.14), (0.35, 0.135, 0.135), (0.6, 0.115, 0.115), (0.78, 0.125, 0.125), (1.0, 0.1, 0.1)],
    "thin": [(0.0, 0.075, 0.075), (0.4, 0.065, 0.065), (0.6, 0.05, 0.05), (0.8, 0.055, 0.058), (1.0, 0.045, 0.045)],
}
