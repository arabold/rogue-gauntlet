"""Renders the game's UI kit into assets/ui-kit/: hand-carved wooden frames with oversized gold curls, recessed
translucent item slots, wooden and gold plaques, curl-ended dividers, a scalloped header band, a tone-on-tone
backdrop, and the quiet pieces in-game UI is built from: feathered dark shades and thin carved outlines.

Everything is drawn from signed distance fields, supersampled and box-filtered, so the kit is our own art (CC0)
and can be re-rendered at any time. Shapes get their depth from height fields lit by one top-left light, the
same direction for every piece. Outlines wander and chip like hand-cut wood, ornaments are chunky and exaggerated,
and every corner's curls differ a little. Run:
    ./.venv/bin/python asset-studio/ui/ui_kit.py [out_dir]
"""
import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image

SS = 3  # supersampling per output pixel
LIGHT = np.array([-0.45, -0.75, 0.62]) / np.linalg.norm([-0.45, -0.75, 0.62])
WOBBLE = 5.0  # px the outlines of large pieces wander, like hand-cut wood
CHUNK = 1.7  # how much thicker than plain ornament, rims and bevels are drawn
ASYM = 1.0  # how much each corner's curls differ from the others
FRAME = 320  # panel_frame texture size; its 9-slice margin is 112


def color(hex_code):
    return np.array([int(hex_code[i:i + 2], 16) / 255 for i in (1, 3, 5)])


INK = color('#120b06')
WOOD_DEEP = color('#2b1d12')
WOOD_DARK = color('#3d2a1a')
WOOD = color('#5b4129')
WOOD_LIGHT = color('#8a6842')
LEATHER = color('#2a1d14')
RECESS = color('#18110b')
BRONZE = color('#6b4a22')
GOLD_DARK = color('#7a531b')
GOLD = color('#c99a42')
GOLD_LIGHT = color('#f6d684')
OCHRE = color('#b8862c')
OCHRE_LIGHT = color('#e9b955')
GLOW = color('#fff3d2')
BACKDROP = color('#3a2819')
SHADE = color('#140c07')


# ---------------------------------------------------------------- canvas and compositing

class Canvas:
    """A supersampled RGBA layer stack; coordinates are in output pixels."""

    def __init__(self, width, height):
        self.width, self.height = width, height
        ys, xs = np.mgrid[0:height * SS, 0:width * SS].astype(np.float64)
        self.x = (xs + 0.5) / SS
        self.y = (ys + 0.5) / SS
        self.rgba = np.zeros((height * SS, width * SS, 4))

    def over(self, rgb, alpha):
        """Composites straight-alpha `rgb` (HxWx3 or a color) with coverage `alpha` over the layer."""
        composite(self.rgba, rgb, alpha)

    def save(self, path):
        h, w = self.height, self.width
        img = self.rgba.reshape(h, SS, w, SS, 4)
        # Average premultiplied color so transparent samples do not darken edges.
        premult = img[..., :3] * img[..., 3:4]
        alpha = img[..., 3].mean(axis=(1, 3))
        rgb = premult.mean(axis=(1, 3)) / np.maximum(alpha[..., None], 1e-6)
        out = np.dstack([rgb, alpha])
        Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').save(path)
        print(f'wrote {path} ({w}x{h})')


def composite(rgba, rgb, alpha):
    """Composites straight-alpha `rgb` (HxWx3 or a color) with coverage `alpha` over the straight-alpha `rgba`."""
    rgb = np.broadcast_to(rgb, rgba[..., :3].shape)
    a = np.clip(alpha, 0, 1)[..., None]
    below = rgba[..., 3:4]
    out = a + below * (1 - a)
    rgba[..., :3] = (rgb * a + rgba[..., :3] * below * (1 - a)) / np.maximum(out, 1e-6)
    rgba[..., 3:4] = out


def smooth(edge0, edge1, value):
    t = np.clip((value - edge0) / (edge1 - edge0), 0, 1)
    return t * t * (3 - 2 * t)


def fill(distance):
    """Antialiased coverage of the inside (distance < 0) of a field in output pixels."""
    return np.clip(0.5 - distance * SS, 0, 1)


def mix(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return a * (1 - t) + b * t


# ---------------------------------------------------------------- distance fields

def round_rect(x, y, x0, y0, x1, y1, radius):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hx, hy = (x1 - x0) / 2 - radius, (y1 - y0) / 2 - radius
    qx, qy = np.abs(x - cx) - hx, np.abs(y - cy) - hy
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - radius


def box(c, x0, y0, x1, y1, radius, seed=0, amount=1.0):
    """A rounded slab outline, roughened like hand-cut wood; small pieces pass a lower `amount`."""
    return rough(c, round_rect(c.x, c.y, x0, y0, x1, y1, radius), seed, amount)


def rough(c, distance, seed, amount=1.0):
    """Hand-cut irregularity: a slow wander of the outline plus small chips, different along every edge."""
    wander = noise(c, 52, 1000 + seed, octaves=2) * 2 - 1
    chips = noise(c, 9, 2000 + seed, octaves=1) * 2 - 1
    return distance + WOBBLE * amount * (wander + 0.35 * chips)


def polygon(x, y, points):
    """Signed distance to a closed polygon (negative inside)."""
    points = np.asarray(points, dtype=np.float64)
    distance = np.full(x.shape, np.inf)
    inside = np.zeros(x.shape, dtype=bool)
    for i in range(len(points)):
        a, b = points[i], points[(i + 1) % len(points)]
        distance = np.minimum(distance, segment(x, y, a, b))
        crosses = ((a[1] > y) != (b[1] > y)) & (x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1] + 1e-12) + a[0])
        inside ^= crosses
    return np.where(inside, -distance, distance)


def segment(x, y, a, b):
    bax, bay = b[0] - a[0], b[1] - a[1]
    pax, pay = x - a[0], y - a[1]
    h = np.clip((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-12), 0, 1)
    return np.hypot(pax - bax * h, pay - bay * h)


def polyline(x, y, points):
    distance = np.full(x.shape, np.inf)
    for a, b in zip(points[:-1], points[1:]):
        distance = np.minimum(distance, segment(x, y, a, b))
    return distance


def scroll(start, heading, radius, turns, lead=0.0, steps=90):
    """A C-scroll: an optional straight lead along `heading`, then a spiral that curls toward the side its sign picks."""
    hx, hy = math.cos(heading), math.sin(heading)
    points = [start, (start[0] + hx * lead, start[1] + hy * lead)]
    side = 1 if radius > 0 else -1
    radius = abs(radius)
    px, py = -hy * side, hx * side
    end = points[-1]
    center = (end[0] + px * radius, end[1] + py * radius)
    angle0 = math.atan2(end[1] - center[1], end[0] - center[0])
    for i in range(1, steps + 1):
        t = i / steps
        r = radius * (1 - 0.72 * t)
        angle = angle0 + side * t * turns * 2 * math.pi
        points.append((center[0] + r * math.cos(angle), center[1] + r * math.sin(angle)))
    return points


def noise(c, scale, seed, octaves=4):
    """Fractal value noise in [0, 1], seeded so every render matches; `scale` is the coarsest cell in pixels."""
    rng = np.random.default_rng(seed)
    total = np.zeros(c.x.shape)
    amplitude, weight = 1.0, 0.0
    for octave in range(octaves):
        cells = scale / 2 ** octave
        grid = rng.random((int(c.height / cells) + 3, int(c.width / cells) + 3))
        gx, gy = c.x / cells, c.y / cells
        ix, iy = gx.astype(int), gy.astype(int)
        fx, fy = gx - ix, gy - iy
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        top = grid[iy, ix] * (1 - fx) + grid[iy, ix + 1] * fx
        bottom = grid[iy + 1, ix] * (1 - fx) + grid[iy + 1, ix + 1] * fx
        total += (top * (1 - fy) + bottom * fy) * amplitude
        weight += amplitude
        amplitude *= 0.5
    return total / weight


def lit(height, strength, samples=SS):
    """Lambert shading of a height field (in output pixels of rise, sampled `samples` times per pixel), 1.0 for a
    flat surface."""
    gy, gx = np.gradient(height * samples)
    normal = np.dstack([-gx * strength, -gy * strength, np.ones_like(height)])
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    return (normal @ LIGHT) / LIGHT[2]


def texture(c, seed, amount):
    """Painted unevenness across a surface."""
    return 1 + (noise(c, 28, seed) - 0.5) * amount


# ---------------------------------------------------------------- shared pieces

def drop_shadow(c, distance, offset=5, spread=7, opacity=0.6):
    """A soft shadow below a shape, from its distance field shifted down by `offset`."""
    shifted = np.roll(distance, int(offset * SS), axis=0)
    c.over(INK, opacity * (1 - smooth(-2, spread, shifted)) ** 1.5)


def emboss_gold(c, distance, width, outline=1.4, dark=GOLD_DARK, mid=GOLD, light=GOLD_LIGHT, opacity=1.0):
    """Rounded raised strokes along a centerline distance field, outlined in ink so they read on any wood."""
    c.over(INK * 1.4, fill(distance - width - outline) * 0.85 * opacity)
    tube = np.sqrt(np.clip(1 - (distance / width) ** 2, 0, 1)) * width
    shade = np.clip(lit(tube, 1.4), 0, 2)
    tone = mix(dark, mid, np.clip(shade, 0, 1)) + (light - mid) * np.clip(shade - 1, 0, 1)[..., None] * 2.2
    c.over(np.clip(tone, 0, 1), fill(distance - width) * opacity)


def raised(c, distance, height, dark, light, outline=1.4):
    """A raised solid (stud, rivet, rosette) shaded from its own bevel."""
    c.over(INK, fill(distance - outline) * 0.9)
    shade = np.clip(lit(smooth(0, height, -distance) * height, 1.2), 0, 2)
    c.over(np.clip(mix(dark, light, np.clip(shade - 0.25, 0, 1)), 0, 1), fill(distance))


def corner_scrolls(x, y, band, variation, size=1.0):
    """Top-left corner ornament in local coordinates: scrolls running out along both edges and a corner rosette.
    `variation` in [-1, 1] makes each corner's curls a little different."""
    mid = band / 2
    v = variation * ASYM
    radius = (14 + 11 * (CHUNK - 1)) * (1 + 0.18 * v) * size
    turns = 1.2 + 0.25 * v
    lead = max(8, 30 - 30 * (CHUNK - 1)) * (1 - 0.2 * v) * size
    top = scroll((mid + 10 * size, mid), 0.0, -radius, turns, lead=lead)
    side = scroll((mid + 10 * size, mid), 0.0, -radius * (1 - 0.25 * v), turns - 0.15 * v, lead=lead * (1 + 0.25 * v))
    left = [(py, px) for px, py in side]
    inner_top = scroll((mid + 14 * size, mid + 8 * size), 0.0, 7.5 * CHUNK ** 0.5 * size, 0.95, lead=10 * size)
    inner_left = [(py, px) for px, py in inner_top]
    strokes = np.minimum.reduce([polyline(x, y, s) for s in (top, left, inner_top, inner_left)])
    rosette = (np.abs(x - mid) + np.abs(y - mid) - 13 * CHUNK ** 0.5 * size) / math.sqrt(2)
    return strokes, rosette


def corner_coordinates(c, corner):
    x = c.x if corner[0] == 0 else c.width - c.x
    y = c.y if corner[1] == 0 else c.height - c.y
    return x, y


CORNER_VARIATIONS = {(0, 0): 0.0, (1, 0): 1.0, (0, 1): -1.0, (1, 1): 0.5}


def carved_band(c, outer, inner, base, bevel):
    """A raised wooden band between two distance fields, bevelled on both edges."""
    height = smooth(0, bevel, -outer) * smooth(0, bevel, inner) * bevel
    shade = lit(height, 1.3)
    tone = base * (0.86 + 0.28 * smooth(c.height, 0, c.y))[..., None] * texture(c, 7, 0.18)[..., None]
    return np.clip(tone * np.clip(shade, 0.35, 1.6)[..., None], 0, 1)


# ---------------------------------------------------------------- kit pieces

def panel_frame(out):
    """Framed window for menus: translucent leather interior, carved wooden band, gold inlay and four corner
    ornaments. 9-slice 112."""
    c = Canvas(FRAME, FRAME)
    edge = 30
    band = int(round(24 + 20 * (CHUNK - 1)))
    outer = box(c, edge, edge, FRAME - edge, FRAME - edge, 18, seed=1)
    inner = box(c, edge + band, edge + band, FRAME - edge - band, FRAME - edge - band, 10, seed=2)
    drop_shadow(c, outer, offset=6, spread=12, opacity=0.65)
    c.over(INK, fill(outer - 1.5 * CHUNK) * (1 - fill(inner)))
    c.over(carved_band(c, outer, inner, WOOD, band * 0.22), fill(outer) * (1 - fill(inner)))
    # The interior lets the scene through a little; it darkens under the frame like a recess.
    depth = np.clip(-inner, 0, None)
    shadow = 1 - 0.55 * np.exp(-depth / 10) - 0.25 * np.exp(-depth / 34)
    interior = LEATHER * texture(c, 3, 0.16)[..., None] * shadow[..., None]
    c.over(interior, fill(inner) * (0.86 + 0.12 * np.exp(-depth / 16)))
    emboss_gold(c, np.abs(inner + 2.5), 1.6 * CHUNK ** 0.5, outline=0.8)
    c.over(WOOD_DEEP, fill(np.abs(outer + 7) - 0.8) * 0.7)
    for corner, variation in CORNER_VARIATIONS.items():
        x, y = corner_coordinates(c, corner)
        strokes, rosette = corner_scrolls(x - edge, y - edge, band, variation)
        emboss_gold(c, strokes, 2.8 * CHUNK)
        raised(c, rosette, 6 * CHUNK, GOLD_DARK, GOLD_LIGHT, outline=1.6)
    c.save(out / 'panel_frame.png')


def outline_frame(out):
    """A thin carved bronze line with small curls on its corners and nothing inside: groups content in-game without
    walling it in. 9-slice 64."""
    c = Canvas(192, 192)
    edge = 22
    line = box(c, edge, edge, 192 - edge, 192 - edge, 12, seed=71, amount=0.35)
    emboss_gold(c, np.abs(line), 1.3, outline=0.9, dark=BRONZE * 0.8, mid=BRONZE * 1.35, light=GOLD, opacity=0.9)
    for corner, variation in CORNER_VARIATIONS.items():
        x, y = corner_coordinates(c, corner)
        strokes, rosette = corner_scrolls(x - edge, y - edge, 0, variation, size=0.55)
        emboss_gold(c, strokes, 1.9, outline=1.0, dark=BRONZE * 0.8, mid=GOLD_DARK * 1.4, light=GOLD, opacity=0.95)
        raised(c, rosette, 3, BRONZE, GOLD, outline=1.0)
    c.save(out / 'outline_frame.png')


def shade(out):
    """A feathered dark area: in-game structure from darkness rather than frames, the scene still showing through
    its edges. 9-slice 56."""
    c = Canvas(160, 160)
    inside = round_rect(c.x, c.y, 28, 28, 132, 132, 20)
    alpha = 0.72 * (1 - smooth(-12, 26, inside)) * texture(c, 81, 0.12)
    c.over(SHADE, alpha)
    c.save(out / 'shade.png')


def plaque(out, name, base, rim, state='normal'):
    """A wooden (or gold) button plaque with end-cap studs. 9-slice: 34 left/right, 26 top/bottom."""
    c = Canvas(200, 80)
    edge, radius = 8, 14
    sink = 2 if state == 'pressed' else 0
    resting = box(c, edge, edge, 200 - edge, 80 - edge, radius, seed=11, amount=0.45)
    shape = box(c, edge, edge + sink, 200 - edge, 80 - edge, radius, seed=11, amount=0.45)
    drop_shadow(c, resting, offset=4 - sink, spread=6, opacity=0.55)
    c.over(INK, fill(shape - 1.6 * CHUNK))
    bevel = (4 if state != 'pressed' else 2.5) * CHUNK
    height = smooth(0, bevel, -shape) * bevel
    shading = lit(height if state != 'pressed' else -height, 1.4)
    brighten = {'normal': 1.0, 'hover': 1.18, 'pressed': 0.82, 'disabled': 0.6}[state]
    vertical = (1.12 - 0.3 * (c.y - edge) / (80 - 2 * edge))[..., None]
    tone = base * brighten * vertical * texture(c, 11, 0.12)[..., None]
    if state == 'disabled':
        tone = mix(tone, tone.mean(axis=2, keepdims=True).repeat(3, axis=2), 0.7)
    c.over(np.clip(tone * np.clip(shading, 0.4, 1.7)[..., None], 0, 1), fill(shape))
    rim_tone = rim * (1.35 if state == 'hover' else 1.0) * (0.55 if state == 'disabled' else 1.0)
    c.over(np.clip(rim_tone, 0, 1), fill(np.abs(shape + 5) - 0.9 * CHUNK) * 0.95)
    # End caps inside the 9-slice margins: diamond studs.
    for side in (-1, 1):
        cx = 100 + side * (100 - edge - 15)
        stud = (np.abs(c.x - cx) + np.abs(c.y - 40 - sink) - 6.5 * CHUNK ** 0.5) / math.sqrt(2)
        raised(c, stud, 3, np.clip(rim_tone * 0.55, 0, 1), np.clip(rim_tone * 1.5, 0, 1), outline=1.2)
    c.save(out / f'{name}_{state}.png')


def glow_ring(out):
    """Selection glow: a bright core line and a soft halo around a rounded square, hollow inside. 9-slice 40."""
    c = Canvas(160, 160)
    shape = box(c, 22, 22, 138, 138, 16, seed=21, amount=0.4)
    halo = np.exp(-np.maximum(np.abs(shape) - 1, 0) / 6.5) * 0.85
    c.over(GLOW, halo)
    c.over(np.array([1, 1, 1]), fill(np.abs(shape) - 1.8))
    c.save(out / 'slot_glow.png')


def slot(out, name, filled):
    """A recessed item slot whose dark interior lets the scene through; filled slots glow ochre like a lit niche.
    9-slice 26."""
    c = Canvas(128, 128)
    edge, radius = 6, 14
    lip_width = 4 * CHUNK
    outer = box(c, edge, edge, 128 - edge, 128 - edge, radius, seed=31, amount=0.4)
    inner = box(c, edge + lip_width, edge + lip_width, 128 - edge - lip_width, 128 - edge - lip_width, radius - 3, seed=32, amount=0.4)
    c.over(INK, fill(outer - 1.2) * 0.75)
    # The lip catches light on its lower edge, the way a niche cut into wood does.
    lip = smooth(0, lip_width, -outer) * lip_width
    lip_shade = np.clip(lit(-lip, 1.2), 0.3, 1.8)
    c.over(np.clip(WOOD_DARK * lip_shade[..., None], 0, 1), fill(outer) * (1 - fill(inner)) * 0.9)
    depth = np.clip(-inner, 0, None)
    if filled:
        glow = np.exp(-((c.x - 64) ** 2 + (c.y - 70) ** 2) / (2 * 38 ** 2))
        tone = mix(OCHRE, OCHRE_LIGHT, glow * 0.9)
        opacity = 0.95
    else:
        lift = np.exp(-((c.x - 64) ** 2 + (c.y - 76) ** 2) / (2 * 44 ** 2))
        tone = np.broadcast_to(RECESS, c.rgba[..., :3].shape) * (1 + 0.5 * lift)[..., None]
        opacity = 0.55
    # Inner shadow falls from the top edge, deepest where the lip overhangs.
    top_bias = 1 + 0.6 * smooth(64, 10, c.y)
    shadow = 1 - 0.6 * np.exp(-depth / (5 * top_bias)) - (0.2 if filled else 0.3) * np.exp(-depth / 18)
    tone = tone * shadow[..., None] * texture(c, 5, 0.08)[..., None]
    c.over(np.clip(tone, 0, 1), fill(inner) * (opacity + (1 - opacity) * np.exp(-depth / 8)))
    c.save(out / f'{name}.png')


def card(out):
    """A recessed translucent card with a bronze rim and rivets in its corners. 9-slice 24."""
    c = Canvas(96, 96)
    edge = 4
    shape = box(c, edge, edge, 96 - edge, 96 - edge, 12, seed=41, amount=0.4)
    c.over(INK, fill(shape - 1.4) * 0.8)
    depth = np.clip(-shape, 0, None)
    tone = RECESS * 1.35 * (1 - 0.45 * np.exp(-depth / 7))[..., None] * texture(c, 9, 0.1)[..., None]
    c.over(np.clip(tone, 0, 1), fill(shape) * 0.7)
    c.over(BRONZE, fill(np.abs(shape + 2.2) - 0.9 * CHUNK ** 0.5))
    for cx in (14, 82):
        for cy in (14, 82):
            rivet = np.hypot(c.x - cx, c.y - cy) - 3.4 * CHUNK ** 0.5
            raised(c, rivet, 2.5, GOLD_DARK, GOLD_LIGHT, outline=1.0)
    c.save(out / 'card.png')


def divider(out):
    """A gold rule with scrolls at both ends. 9-slice 80 left/right."""
    c = Canvas(400, 40)
    rule = segment(c.x, c.y, (60, 20), (340, 20))
    left = scroll((60, 20), math.pi, 7 * CHUNK ** 0.5, 1.1, lead=16)
    left_lower = scroll((54, 20), math.pi, -5 * CHUNK ** 0.5, 0.9, lead=6)
    right = [(400 - px, py) for px, py in left]
    right_lower = [(400 - px, py) for px, py in left_lower]
    strokes = np.minimum.reduce([rule, polyline(c.x, c.y, left), polyline(c.x, c.y, right),
                                 polyline(c.x, c.y, left_lower), polyline(c.x, c.y, right_lower)])
    emboss_gold(c, strokes, 1.6 * CHUNK ** 0.5, outline=1.0)
    for cx in (66, 334):
        diamond = (np.abs(c.x - cx) + np.abs(c.y - 20) - 5 * CHUNK ** 0.5) / math.sqrt(2)
        raised(c, diamond, 3, GOLD_DARK, GOLD_LIGHT, outline=1.0)
    c.save(out / 'divider.png')


def ribbon(out):
    """A gold ribbon tag with swallowtail ends. 9-slice 30 left/right."""
    c = Canvas(150, 52)
    points = [(8, 10), (142, 10), (132, 26), (142, 42), (8, 42), (18, 26)]
    shape = rough(c, polygon(c.x, c.y, points), 51, amount=0.4)
    drop_shadow(c, shape, offset=3, spread=5, opacity=0.5)
    c.over(INK, fill(shape - 1.5))
    height = smooth(0, 3.5, -shape) * 3.5
    shading = np.clip(lit(height, 1.4), 0.4, 1.8)
    tone = mix(GOLD, OCHRE_LIGHT, smooth(42, 10, c.y)) * shading[..., None]
    c.over(np.clip(tone, 0, 1), fill(shape))
    c.over(GOLD_DARK, fill(np.abs(shape + 4) - 0.7) * 0.8)
    c.save(out / 'ribbon.png')


def tab_pennant(out, dark=RECESS, light=WOOD_DARK):
    """The open tab's marker: a swallowtail pennant of dark cloth with a gold hem, hanging from the top edge of the
    screen in front of the header band and past its scallops. Soft folds run down it. 9-slice 44 left/right, 8 top,
    70 bottom, so it widens with its word and its tails keep their cut. `dark` and `light` are the cloth's shadow and
    lit tones."""
    c = Canvas(230, 196)
    shape = rough(c, polygon(c.x, c.y, [(20, -20), (210, -20), (210, 178), (115, 140), (20, 178)]) - 3, 71, amount=0.35)
    drop_shadow(c, shape, offset=6, spread=10, opacity=0.6)
    c.over(INK, fill(shape - 1.6))
    folds = 0.5 + 0.5 * np.cos((c.x - 115) / 190 * np.pi * 4)
    tone = mix(dark, light, np.clip(0.25 + 0.6 * folds * smooth(0, 5, -shape), 0, 1)) * texture(c, 7, 0.12)[..., None]
    c.over(np.clip(tone * (1.05 - 0.25 * c.y / c.height)[..., None], 0, 1), fill(shape))
    emboss_gold(c, np.where(c.y > 4, np.abs(shape + 9), np.inf), 1.9, outline=1.0)
    c.save(out / 'tab_pennant.png')


def header_band(out):
    """A tileable carved header strip whose lower edge hangs in scalloped tongues, each holding a gold scroll, with a
    gold rim along the whole silhouette. Its top 88 px stay clear for a centered screen title. Tiles horizontally
    every 256 px."""
    c = Canvas(256, 148)
    band_bottom = 100
    tongues = np.full(c.x.shape, np.inf)
    for i in range(-1, 6):
        tongues = np.minimum(tongues, np.hypot((c.x - (i * 64 + 32)) / 1.15, c.y - band_bottom) - 21)
    body = rough(c, np.minimum(c.y - band_bottom, tongues), 61, amount=0.7)
    drop_shadow(c, body, offset=7, spread=12, opacity=0.75)
    c.over(INK, fill(body - 1.8 * CHUNK))
    height = smooth(0, 6 * CHUNK, -body) * 6 * CHUNK
    shading = np.clip(lit(height, 1.3), 0.35, 1.6)
    tone = WOOD_LIGHT * (1.05 - 0.45 * c.y / 148)[..., None] * texture(c, 13, 0.14)[..., None]
    c.over(np.clip(tone * shading[..., None], 0, 1), fill(body))
    strokes = np.where(c.y > 30, np.abs(body + 5), np.inf)
    for i in range(-1, 6):
        curl = scroll((i * 64 + 20, band_bottom + 2), 0.0, -7.5 * CHUNK ** 0.5, 1.05, lead=12)
        strokes = np.minimum(strokes, polyline(c.x, c.y, curl))
    emboss_gold(c, strokes, 1.8 * CHUNK ** 0.5, outline=1.0)
    c.save(out / 'header_band.png')


def arrow(out):
    """Carousel arrows: a chunky carved gold dart, drawn pointing right and mirrored for the left one."""
    c = Canvas(64, 64)
    shape = rough(c, polygon(c.x, c.y, [(16, 9), (53, 32), (16, 55), (26, 32)]) - 3.5, 91, amount=0.25)
    drop_shadow(c, shape, offset=3, spread=4, opacity=0.55)
    raised(c, shape, 6, GOLD_DARK, GOLD_LIGHT, outline=2.2)
    c.save(out / 'arrow_right.png')
    Image.open(out / 'arrow_right.png').transpose(Image.FLIP_LEFT_RIGHT).save(out / 'arrow_left.png')


def capsule(c, a, b, radius):
    return segment(c.x, c.y, a, b) - radius


def disc(c, x, y, radius):
    return np.hypot(c.x - x, c.y - y) - radius


def rect(c, x0, y0, x1, y1, radius=0.0):
    return round_rect(c.x, c.y, x0, y0, x1, y1, radius)


def poly(c, points, grow=0.0):
    return polygon(c.x, c.y, points) - grow


def cut(shape, *holes):
    """`shape` with every hole carved out of it."""
    for hole in holes:
        shape = np.maximum(shape, -hole)
    return shape


def icon_shapes(c):
    """Chunky silhouettes on a 96 px canvas: what each of the seven gear slots holds (helmet, armor, amulet, ring, weapon,
    shield), and the touch actions "use" and "dodge"."""
    union = np.minimum.reduce
    visor = union([rect(c, 26, 48, 70, 57, 2), rect(c, 43, 55, 53, 76, 2)])
    return {
        'helmet': cut(union([np.maximum(disc(c, 48, 46, 30), c.y - 50), rect(c, 18, 44, 78, 74, 7)]), visor),
        'armor': cut(poly(c, [(22, 22), (38, 17), (48, 27), (58, 17), (74, 22), (82, 40), (68, 45), (68, 76), (48, 83),
                              (28, 76), (28, 45), (14, 40)], 3), np.abs(c.x - 48) - 1.3 + 100 * (c.y < 34)),
        'weapon': union([capsule(c, (42, 54), (76, 20), 6.5), capsule(c, (30, 44), (52, 66), 4.5),
                         capsule(c, (36, 60), (25, 71), 4), disc(c, 21, 75, 6.5)]),
        'shield': cut(poly(c, [(22, 20), (74, 20), (74, 48), (48, 82), (22, 48)], 4),
                      np.abs(poly(c, [(22, 20), (74, 20), (74, 48), (48, 82), (22, 48)], 4) + 9) - 1.6),
        'ring': union([np.abs(disc(c, 48, 58, 19)) - 6.5, (np.abs(c.x - 48) + np.abs(c.y - 27) - 13) / math.sqrt(2)]),
        'amulet': union([np.maximum(np.abs(disc(c, 48, 26, 26)) - 3, 26 - c.y), cut(disc(c, 48, 62, 15), disc(c, 48, 62, 5))]),
        'hand': union([rect(c, 30, 42, 67, 82, 11), capsule(c, (36, 46), (36, 22), 5.2), capsule(c, (46, 46), (46, 16), 5.2),
                       capsule(c, (56, 46), (56, 19), 5.2), capsule(c, (65, 50), (66, 28), 4.6), capsule(c, (31, 62), (17, 46), 5.6)]),
        'dodge': union([poly(c, [(14, 20), (44, 48), (14, 76), (26, 48)], 3), poly(c, [(46, 20), (76, 48), (46, 76), (58, 48)], 3)]),
    }


def icons(out):
    """Gear-slot placeholders carved in muted wood, the gold touch actions, and the gold coin."""
    c = Canvas(96, 96)
    for index, (name, shape) in enumerate(icon_shapes(c).items()):
        c.rgba[:] = 0
        shape = rough(c, shape, 100 + index, amount=0.18)
        if name in ('hand', 'dodge'):
            drop_shadow(c, shape, offset=3, spread=4, opacity=0.5)
            raised(c, shape, 5, GOLD_DARK, GOLD_LIGHT, outline=2.2)
        else:
            raised(c, shape, 5, WOOD_DARK * 0.85, WOOD_LIGHT * 0.8, outline=2)
            c.rgba[..., 3] *= 0.85
        c.save(out / f'icon_{name}.png')
    c = Canvas(32, 32)
    raised(c, disc(c, 16, 16, 12), 5, GOLD_DARK, GOLD_LIGHT, outline=2)
    c.over(GOLD_DARK, fill(np.abs(disc(c, 16, 16, 7)) - 0.9) * 0.7)
    c.save(out / 'coin.png')


def backdrop(out):
    """A tileable warm-wood backdrop with low tone-on-tone scroll relief, 256 px square."""
    c = Canvas(256, 256)
    tone = BACKDROP * (0.9 + 0.2 * noise(c, 64, 21))[..., None]
    strokes = np.full(c.x.shape, np.inf)
    for (mx, my), side in [((64, 80), 1), ((192, 208), -1)]:
        for ox in (-256, 0, 256):
            for oy in (-256, 0, 256):
                for heading in (0.0, math.pi):
                    curl = scroll((mx + ox, my + oy), heading, side * 30, 1.2, lead=34)
                    strokes = np.minimum(strokes, polyline(c.x, c.y, curl))
    width = 4.5 * CHUNK ** 0.5
    relief = np.sqrt(np.clip(1 - (strokes / width) ** 2, 0, 1)) * 3
    shading = lit(relief, 0.9)
    c.over(np.clip(tone * (1 + (shading - 1) * 0.5)[..., None] * (1 + 0.05 * (relief > 0))[..., None], 0, 1),
           np.ones(c.x.shape))
    c.save(out / 'backdrop.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('out', nargs='?', type=Path,
                        default=Path(__file__).resolve().parents[2] / 'assets' / 'ui-kit')
    out = parser.parse_args().out
    out.mkdir(parents=True, exist_ok=True)
    panel_frame(out)
    outline_frame(out)
    shade(out)
    for state in ('normal', 'hover', 'pressed', 'disabled'):
        plaque(out, 'button', WOOD, GOLD, state=state)
        plaque(out, 'button_primary', OCHRE, GOLD_DARK, state=state)
    glow_ring(out)
    slot(out, 'slot', filled=False)
    slot(out, 'slot_filled', filled=True)
    card(out)
    divider(out)
    ribbon(out)
    header_band(out)
    tab_pennant(out)
    arrow(out)
    icons(out)
    backdrop(out)


if __name__ == '__main__':
    main()
