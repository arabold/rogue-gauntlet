"""Joint landmarks of a reconstruction config, mirrored to both sides, and the limb chains through them."""

from mathutils import Vector


def joints(config):
    """Every landmark by name. A `.l` landmark also yields its `.r` mirror across x = 0."""
    out = {}
    for name, co in config.get("joints", {}).items():
        out[name] = Vector(co)
        if name.endswith(".l"):
            out[name[:-2] + ".r"] = Vector((-co[0], co[1], co[2]))
    return out


def chains(config):
    """Each chain (`arm`: shoulder, forearm, fist tip) as points per side: `{"arm.l": [...], "arm.r": [...]}`."""
    j = joints(config)
    return {f"{name}.{side}": [j[f"{point}.{side}"] for point in points]
            for name, points in config.get("chains", {}).items() for side in ("l", "r")}


def polyline(points, f):
    """Point and direction at fraction `f` of a polyline's length."""
    seg = list(zip(points, points[1:]))
    lens = [(b - a).length for a, b in seg]
    d = f * sum(lens)
    for i, ((a, b), length) in enumerate(zip(seg, lens)):
        if d <= length or i == len(seg) - 1:
            return a + (b - a) * (d / length), (b - a).normalized()
        d -= length
