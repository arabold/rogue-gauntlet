"""
Heads in KayKit's vocabulary: a rounded cranium with separate, overlapping pieces for the face and the hair, the way
KayKit builds its own characters (`Knight.glb`'s head is one cranium plus separate eye, brow, nose and ear shells).
A recipe (`build`) picks the cranium's form and each feature's style; every face carries a material slot that the
monster's palette colors.

The cranium (`Head`) is a superellipsoid: superellipse rings (exponent `p`) whose half widths follow a superellipse
profile over the height (exponent `q`), with a fuller jaw, a chin or a brow ridge as bumps on that profile. It is a
host like a torso (`section`), so crowns, headbands and wraps fit it like a belt fits a waist. Face pieces are
modelled flat in face coordinates (x across, y up, z out of the skin) and bent onto the cranium (`Head.conform`),
so a brow or a mouth follows the face's curve.

Feature positions and sizes are shares of the head (x and sizes of its half width, y of its half height from its
centre), so one recipe fits any head size.
"""

import numpy as np

from accessories import kit

FRONT = np.pi / 2

# Head material slots and their catalog colors (a recipe's `colors` override them).
COLORS = {
    "skin": (240, 190, 150), "skin_dark": (205, 145, 110), "eye": (28, 32, 36), "iris": (90, 200, 120),
    "glint": (250, 250, 245), "brow": (90, 60, 40), "hair": (120, 80, 50), "mouth": (110, 60, 50),
    "tusk": (240, 232, 205), "wrap": (232, 214, 162), "wrap_dark": (186, 160, 108), "dark": (40, 32, 30),
    "scar": (150, 60, 60), "tie": (232, 182, 64), "socket": (150, 110, 90), "scalp": (40, 36, 40),
    "wart": (170, 140, 100), "nostril": (60, 42, 36), "tusk_tip": (248, 244, 234), "lip": (120, 86, 64), "ear_dark": (170, 110, 90),
    "gold": (232, 182, 64),
    "liner": (24, 22, 26), "halo": (200, 250, 246), "paint": (190, 40, 40), "beard": (176, 176, 182),
}


def _bump(t, at, width):
    return np.exp(-((t - at) / width) ** 2)


def _spow(v, e):
    return np.sign(v) * np.abs(v) ** e


class Head:
    """
    The cranium: `width` x `height` x `depth` around `center`; `back` < 1 flattens the back of the head. `jaw` widens
    the lower face, `chin` pushes it forward, `brow` adds a ridge over the eyes, `crown` narrows the top. `bite`
    pushes everything below the `mouth` line forward in one smooth piece with the head (an orc's underbite): a
    crisp step `edge` tall at the line makes the lower lip, the jaw under it rounds back into the head on its own.
    `q_low` (> `q`) squares off the lower half: a blunt, square chin. `relief` sculpts the face: mirrored bumps (or dents, `amount` < 0) `(x, y, rx, ry, amount)` in face shares
    (x, rx of the half width, y, ry of the half height, amount of the half width), fading toward the sides: a
    heavy brow, sunken eye sockets, cheekbones.
    """

    def __init__(self, width=0.94, height=1.0, depth=0.96, center=(0.0, 1.72, 0.0), p=2.5, q=2.6, jaw=0.0,
                 chin=0.0, brow=0.0, crown=0.0, back=1.0, bite=0.0, mouth=-0.45, edge=0.05, relief=(), q_low=None):
        self.params = dict(width=width, height=height, depth=depth, center=tuple(float(c) for c in center), p=p, q=q,
                           jaw=jaw, chin=chin, brow=brow, crown=crown, back=back, bite=bite, mouth=mouth, edge=edge,
                           relief=[list(r) for r in relief], q_low=q_low)
        self.q_low = q if q_low is None else q_low
        self.relief = np.asarray(relief, float).reshape(-1, 5)
        self.bite, self.mouth, self.edge = bite, mouth, edge
        self.A, self.Hh, self.Bf, self.Bk = width / 2, height / 2, depth / 2, depth / 2 * back
        self.cx, self.cy, self.cz0 = center
        self.p, self.q = p, q
        self.jaw, self.chin, self.brow, self.crown = jaw, chin, brow, crown

    @property
    def centre(self):
        return np.array([self.cx, self.cy, self.cz0], float)

    @property
    def size(self):
        """The mean of the half width, half height and half depth."""
        return (self.A + self.Hh + 0.5 * (self.Bf + self.Bk)) / 3

    def proportions(self, d):
        """The half sizes along each axis on the side of the centre each offset `d` points to (a head's front and
        back can be deep differently)."""
        d = np.atleast_2d(np.asarray(d, float))
        return np.column_stack([np.full(len(d), self.A), np.full(len(d), self.Hh), np.where(d[:, 2] >= 0, self.Bf, self.Bk)])

    def inside(self, P):
        P = np.atleast_2d(np.asarray(P, float))
        yn = (P[:, 1] - self.cy) / self.Hh
        cx, a, cz, b = self.section(P[:, 1])
        u = np.abs((P[:, 0] - cx) / np.maximum(a, 1e-9)) ** self.p + np.abs((P[:, 2] - cz) / np.maximum(b, 1e-9)) ** self.p
        return (np.abs(yn) < 1) & (u <= 1)

    def radius(self, dirs, iters=40):
        """Distance from `centre` to the surface along each unit direction (bisection: the cranium is star-shaped
        around its centre)."""
        dirs = np.atleast_2d(np.asarray(dirs, float))
        lo = np.zeros(len(dirs))
        hi = np.full(len(dirs), 2.5 * max(self.A, self.Hh, self.Bf, self.Bk))
        for _ in range(iters):
            mid = 0.5 * (lo + hi)
            ins = self.inside(self.centre + mid[:, None] * dirs)
            lo, hi = np.where(ins, mid, lo), np.where(ins, hi, mid)
        return 0.5 * (lo + hi)

    @staticmethod
    def fit(verts, p=2.4, q=2.4, samples=1200, seed=0):
        """The `Head` closest to a sculpted skull's vertices; robust, so ears, a nose or a neck stump barely pull
        on it. Front and back stay equally deep: a fit can't tell a deeper back from a centre further back."""
        from scipy.optimize import least_squares
        V = np.asarray(verts, float)
        V = V[np.random.default_rng(seed).choice(len(V), min(samples, len(V)), replace=False)]
        lo, hi = np.percentile(V, 10, 0), np.percentile(V, 90, 0)
        x0 = np.r_[0.5 * (lo + hi), hi - lo]

        def make(x):
            return Head(width=x[3], height=x[4], depth=x[5], center=x[:3], p=p, q=q)

        def residual(x):
            h = make(x)
            d = V - h.centre
            r = np.linalg.norm(d, axis=1)
            return r - h.radius(d / r[:, None], iters=24)

        x = least_squares(residual, x0, loss="soft_l1", f_scale=0.02, x_scale=0.1,
                          bounds=([-np.inf] * 3 + [0.05] * 3, [np.inf] * 3 + [5.0] * 3)).x
        return make(x)

    def y(self, yn):
        """A height given as a share of the half height from the centre."""
        return self.cy + yn * self.Hh

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        yn = np.clip((y - self.cy) / self.Hh, -1.0, 1.0)
        q = np.where(yn < 0, self.q_low, self.q)  # a flatter lower half: a square chin
        w = np.where(np.abs(yn) >= 1 - 1e-9, 0.0, (1 - np.abs(yn) ** q) ** (1 / q))  # exact poles: the
        a = self.A * w  # round trip through y leaves |yn| a hair under 1, which would open a pole ring * (1 + self.jaw * _bump(yn, -0.45, 0.35)) * (1 - self.crown * np.clip(yn, 0, 1) ** 2)
        lower = np.clip((self.mouth - yn) / max(self.edge, 1e-6), 0.0, 1.0)
        lower = lower * lower * (3 - 2 * lower)  # 0 above the mouth line, 1 an `edge` below it: the lip's step
        front = self.Bf * w * (1 + self.chin * _bump(yn, -0.7, 0.25) + self.brow * _bump(yn, 0.08, 0.14)
                               + self.bite * lower)
        back = self.Bk * w
        return (np.full(len(y), float(self.cx)), np.maximum(a, 0.0), self.cz0 + (front - back) / 2,
                np.maximum((front + back) / 2, 0.0))

    def _point(self, theta, phi):
        yn = _spow(np.cos(phi), 2 / self.q)
        y = self.cy + self.Hh * yn
        cx, a, cz, b = self.section(y)
        P = np.stack([cx + a * _spow(np.cos(theta), 2 / self.p), y, cz + b * _spow(np.sin(theta), 2 / self.p)], -1)
        if len(self.relief):
            xs, front = np.abs((P[..., 0] - self.cx) / self.A), np.clip(np.sin(theta), 0.0, 1.0) ** 2
            d = sum(amt * np.exp(-((xs - x) / rx) ** 2 - ((yn - yy) / ry) ** 2) for x, yy, rx, ry, amt in self.relief)
            r = P - self.centre
            P = P + (d * front * self.A)[..., None] * r / np.maximum(np.linalg.norm(r, axis=-1, keepdims=True), 1e-9)
        return P

    def surface(self, theta, phi, out=0.0, normals=False):
        """Points at angle `theta` around (0 = +x, pi/2 = the front) and `phi` down from the top (0 .. pi), lifted
        `out` along the surface normal."""
        theta, phi, out = (a.ravel() for a in np.broadcast_arrays(np.asarray(theta, float), np.asarray(phi, float),
                                                                  np.asarray(out, float)))
        P = self._point(theta, phi)
        e = 1e-4
        n = np.cross(self._point(theta + e, phi) - self._point(theta - e, phi),
                     self._point(theta, phi + e) - self._point(theta, phi - e))
        length = np.linalg.norm(n, axis=1, keepdims=True)
        pole = length[:, 0] < 1e-12
        n = np.where(pole[:, None], np.column_stack([0 * phi, np.sign(np.cos(phi)), 0 * phi]), n / np.maximum(length, 1e-30))
        out = out[:, None]
        return (P + out * n, n) if normals else P + out * n

    def phi(self, yn):
        """The angle down from the top of the height share `yn`."""
        return np.arccos(np.clip(_spow(np.clip(yn, -1, 1), self.q / 2), -1, 1))

    def front_angles(self, x, y):
        """`(theta, phi)` of the front surface point at `x`, `y`."""
        x, y = np.broadcast_arrays(np.asarray(x, float), np.asarray(y, float))
        cx, a, _, _ = self.section(y.ravel())
        u = np.clip((x.ravel() - cx) / np.maximum(a, 1e-9), -1, 1)  # past the crown `a` is 0
        return np.arccos(_spow(u, self.p / 2)), self.phi((y.ravel() - self.cy) / self.Hh)

    def conform(self, piece, x0, y0, out=0.0):
        """`piece`, modelled flat in face coordinates around the front point (`x0`, `y0`), bent onto the face: its
        local z becomes the height over the skin, plus `out`."""
        v = piece.verts
        P, n = self.surface(*self.front_angles(x0 + v[:, 0], y0 + v[:, 1]), normals=True)
        return kit.Piece(P + (out + v[:, 2:3]) * n, piece.faces, piece.slots)

    def mesh(self, slot="skin", sides=32, rings=16):
        if len(self.relief):  # a sculpted face needs the faces to show it
            sides, rings = max(sides, 48), max(rings, 26)
        th = 2 * np.pi * np.arange(sides) / sides
        phis = np.pi * np.arange(rings - 1, 0, -1) / rings
        if self.bite:  # rings along the lip's step, so its edge stays crisp
            extra = self.phi(np.array([self.mouth + 0.02, self.mouth, self.mouth - 0.2 * self.edge,
                                       self.mouth - 0.35 * self.edge, self.mouth - 0.5 * self.edge,
                                       self.mouth - self.edge, self.mouth - 2.5 * self.edge]))  # (the lip's band too)
            phis = np.sort(np.r_[phis, extra])[::-1]
            phis = phis[np.r_[True, np.abs(np.diff(phis)) > 1e-3]]
            rings = len(phis) + 1
        rows = [self.surface(th, np.full(sides, ph)) for ph in phis]
        verts, faces = kit._loft(rows)
        n = sides
        verts, faces = kit._cap_loops(verts, faces, [list(range(n)), list(range((rings - 2) * n, (rings - 1) * n))],
                                      centers=[self.surface(0.0, np.pi)[0], self.surface(0.0, 0.0)[0]])
        return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


# ---------------------------------------------------------------------------
# Flat shapes for the face
# ---------------------------------------------------------------------------
def blob(w, h, n=14, p=2.4):
    """A rounded-rectangle outline `w` x `h` around the origin (superellipse exponent `p`)."""
    t = 2 * np.pi * (np.arange(n) + 0.5) / n
    return np.column_stack([w / 2 * _spow(np.cos(t), 2 / p), h / 2 * _spow(np.sin(t), 2 / p)])


def clip(poly, nx, ny, c):
    """The convex polygon `poly` cut to the half plane `nx * x + ny * y <= c`."""
    out = []
    poly = np.asarray(poly, float)
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        da, db = nx * a[0] + ny * a[1] - c, nx * b[0] + ny * b[1] - c
        if da <= 0:
            out.append(a)
        if da * db < 0:
            out.append(a + (b - a) * da / (da - db))
    return np.asarray(out)


def slab(poly, depth, slot, embed=0.015, bevel=0.004):
    """A flat face piece: `poly` standing `depth` over the skin, sunk `embed` into it so no gap shows."""
    p = kit.prism(poly, depth + embed, slot, bevel=bevel)
    return kit.Piece(p.verts - [0.0, 0.0, embed], p.faces, slot)


# ---------------------------------------------------------------------------
# Features
# ---------------------------------------------------------------------------
def bead_mesh(w, h, d=None, p=2.0, sides=16, rings=8, slot="eye", angry=0.0):
    """
    A 3D rounded bead centered at (0, 0, 0) with width `w`, height `h`, and depth `d`.
    Its equator (z = 0) divides the bead:
      z in [-d/2, 0]: back half, sunk into the head or socket
      z in [0, +d/2]: front half, protruding outward
    Normals are outward facing and smooth.
    """
    if d is None:
        d = 0.85 * min(w, h)
    u = 2 * np.pi * np.arange(sides) / sides
    v = np.linspace(-np.pi / 2, np.pi / 2, rings + 1)

    verts = []
    verts.append([0.0, 0.0, -d / 2])

    sp = lambda val, exp: np.sign(val) * (np.abs(val) ** exp)

    for vi in v[1:-1]:
        cv = np.cos(vi)
        sv = np.sin(vi)
        r_scale = sp(cv, 2.0 / p)
        xs = (w / 2) * r_scale * sp(np.cos(u), 2.0 / p)
        ys = (h / 2) * r_scale * sp(np.sin(u), 2.0 / p)
        zs = np.full(sides, (d / 2) * sv)
        for x, y, z in zip(xs, ys, zs):
            verts.append([x, y, z])

    verts.append([0.0, 0.0, d / 2])
    verts = np.asarray(verts, float)

    if angry and abs(w - h) > 1e-4:
        ang = np.radians(-angry * 15.0)
        R = np.array([[np.cos(ang), -np.sin(ang), 0.0],
                      [np.sin(ang),  np.cos(ang), 0.0],
                      [0.0,          0.0,         1.0]])
        verts = verts @ R.T

    faces = []
    for i in range(sides):
        nxt = (i + 1) % sides
        faces.append([0, 1 + nxt, 1 + i])

    for r in range(rings - 2):
        r1 = 1 + r * sides
        r2 = 1 + (r + 1) * sides
        for i in range(sides):
            nxt = (i + 1) % sides
            faces.append([r1 + i, r1 + nxt, r2 + nxt])
            faces.append([r1 + i, r2 + nxt, r2 + i])

    top_pole = len(verts) - 1
    last_r = 1 + (rings - 2) * sides
    for i in range(sides):
        nxt = (i + 1) % sides
        faces.append([last_r + i, last_r + nxt, top_pole])

    faces = np.asarray(faces, int)
    return kit.Piece(verts, faces, slot)


def eyes(head, style="dot", x=0.42, y=-0.2, w=0.2, h=0.27, angry=0.0, pupil=False, glint=False, outline=0.32,
         socket=0.0, liner=0.0, flick=0.3, lashes=0, halo=0.0, uneven=1.0, cut="round",
         carve_socket=False, socket_depth=None, socket_slot="socket", depth=None, out=None,
         slot=None, glow=False, ring=0.0, rings=None, makeup=None, ring_slot="ring",
         outline_slot="eye", iris_slot="iris", outer=False, outer_shape=False, bead_scale=None):
    """
    Both eyes. `bead`: small solid 3D rounded bead catching specular light reflections (sunk halfway).
    `dot`: KayKit's dark rounded pills. `glow`: a colored iris (slot `iris`) in a dark rim (slot `eye`,
    `outline` its margin as a share of the iris) with an optional `pupil` and an optional `glint`.
    `angry` slants the top edge down toward the nose (0 .. 1).

    Around them: `socket` > 0 sets the eye in a shadowed patch (slot `socket`) that much bigger;
    `carve_socket` carves the eye socket as a 3D recess in the cranium (for ghoul and zombie);
    `ring` / `makeup` / `rings` draws rings or makeup patches of customizable size and slot around the eyes;
    `liner` draws a heavy upper lid (slot `liner`, its thickness a share of the eye's height) that
    sweeps past the outer corner by `flick` (a share of its width) with `lashes` strokes; `halo` rings
    a glowing eye in a lighter glow (slot `halo`) that much bigger; `uneven` scales the right eye (a zombie's
    mismatched stare). `cut="triangle"` gives a devilish eye: the slanted top meets a straight edge rising from the
    bottom in a sharp inner corner (an imp's); `pupil="slit"` a cat's narrow upright pupil.
    """
    A = head.A
    W, H = w * A, h * A
    x0, y0 = x * A, head.y(y)
    bead_slot = slot or ("glow" if (glow or style == "glow_bead") else "eye")

    def top_line(margin):  # the slanted lid of an angry eye: y <= c + slope * x
        return angry * H / W, 0.55 * H - 0.5 * angry * H + margin

    def shape(k, margin=0.0):
        poly = blob(W * k, H * k)
        if angry:  # the eye at +x has its inner corner toward -x
            slope, c = top_line(margin)
            poly = clip(poly, -slope, 1.0, c)
            if cut == "triangle":  # a straight lower-inner edge from the inner corner down to the bottom
                x0_, y0_ = -0.5 * W * k, c - slope * 0.5 * W * k
                s2 = (-0.5 * H * k - y0_) / (0.65 * W * k)
                poly = clip(poly, s2, -1.0, -y0_ - s2 * (-x0_) + 0.0)
        return poly

    has_outer = bool(pupil or cut == "triangle" or outer or outer_shape)
    rim = 1.0 + (outline if (outline is not None and (has_outer or style not in ("dot", "bead", "glow_bead"))) else 0.0)
    skin_cuts = []  # (slot, polygon) in eye coordinates, cut into the face around it: never patches laid on it
    carve_list = []

    if carve_socket:
        sw = W * (1.0 + (socket if socket > 0 else 0.55))
        sh = H * (1.0 + 1.1 * (socket if socket > 0 else 0.55))
        sox = 0.02 * W
        soy = -0.12 * H
        sp_poly = blob(sw, sh) + [sox, soy]
        poly_left = sp_poly + [x0, y0]
        poly_right = ((blob(sw * uneven, sh * uneven) + [sox * uneven, soy * uneven]) * [-1, 1] + [-x0, y0])[::-1]
        c_depth = (socket_depth if socket_depth is not None else 0.055) * A
        carve_list.append((socket_slot, [poly_left], poly_left, c_depth))
        carve_list.append((socket_slot, [poly_right], poly_right, c_depth))
        bead_out = out if out is not None else (-c_depth * 0.55)
    else:
        if socket:
            skin_cuts.append((socket_slot, blob(W * (1 + socket), H * (1 + 1.1 * socket)) + [0.04 * W, -0.22 * socket * H]))
        bead_out = out if out is not None else 0.0

    # Collect rings / makeup specifications around the eyes
    ring_entries = []
    if makeup is not None and not ring and not rings:
        ring = makeup

    if rings:
        if isinstance(rings, (list, tuple)):
            for r in rings:
                if isinstance(r, dict):
                    ring_entries.append((r.get("slot", ring_slot), float(r.get("scale", r.get("ring", 1.0))),
                                         float(r.get("x", 0.04 * W)), float(r.get("y", -0.15 * H))))
                elif isinstance(r, (list, tuple)):
                    sl_ = r[0] if isinstance(r[0], str) else ring_slot
                    sc_ = float(r[1] if isinstance(r[0], str) else r[0])
                    ox_ = float(r[2]) if len(r) > 2 else 0.04 * W
                    oy_ = float(r[3]) if len(r) > 3 else -0.15 * sc_ * H
                    ring_entries.append((sl_, sc_, ox_, oy_))
                else:
                    sc_ = float(r)
                    ring_entries.append((ring_slot, sc_, 0.04 * W, -0.15 * sc_ * H))
    elif ring:
        if isinstance(ring, dict):
            ring_entries.append((ring.get("slot", ring_slot), float(ring.get("scale", 1.0)),
                                 float(ring.get("x", 0.04 * W)), float(ring.get("y", -0.15 * H))))
        else:
            sc_ = float(ring)
            ring_entries.append((ring_slot, sc_, 0.04 * W, -0.15 * sc_ * H))

    for r_slot, r_scale, rox, roy in ring_entries:
        rw = W * (1.0 + r_scale)
        rh = H * (1.0 + 1.1 * r_scale)
        skin_cuts.append((r_slot, blob(rw, rh) + [rox, roy]))

    if halo:
        skin_cuts.append(("halo", shape(rim + halo, (rim + halo - 1) * 0.5 * H)))

    if style in ("bead", "glow_bead", "glow"):
        if has_outer:
            # Outer eye shape retained by color on the head mesh
            if outline > 0:
                skin_cuts.append((outline_slot or "eye", shape(rim)))
            if pupil or cut == "triangle":
                skin_cuts.append((iris_slot or "iris", shape(1.0)))

            if bead_scale is not None:
                if isinstance(bead_scale, (tuple, list)):
                    bw, bh = W * bead_scale[0], H * bead_scale[1]
                else:
                    bw, bh = W * bead_scale, H * bead_scale
            elif pupil == "slit":
                bw, bh = W * 0.35, H * 0.65
            elif pupil:
                bw, bh = W * 0.55, H * 0.60
            else:
                bw, bh = W * 0.50, H * 0.50
        else:
            bw, bh = W, H

        ball = bead_mesh(bw, bh, d=depth * A if depth is not None else None, angry=angry * 0.5 if has_outer else angry, slot=bead_slot)
        cuts = []  # specular light reflections come from PBR roughness, no white paint cuts
    elif style == "dot":
        ball = slab(shape(1.0), 0.022, "eye")
        cuts = [("glint", blob(0.3 * W, 0.3 * W) + [0.18 * W, 0.18 * H], {"eye"})] if glint else []
    else:
        m = outline * H
        ball = slab(shape(rim, m), 0.026, "eye")
        cuts = [("iris", shape(1.0), {"eye"})]
        if pupil == "slit":
            cuts.append(("eye", blob(0.14 * W, 0.62 * H) + [0.04 * W, -0.04 * H], {"iris"}))
        elif pupil:
            cuts.append(("eye", clip(blob(0.38 * W, 0.62 * H), -angry * H / W, 1.0, 0.3 * H), {"iris"}))
        if glint:
            cuts.append(("glint", blob(0.26 * W, 0.26 * W) + [0.2 * W, 0.08 * H], {"iris", "eye"}))

    for sl, poly, on in cuts:
        ball = kit.paint_cut(ball, [poly], sl, on)

    if liner:
        t = np.linspace(np.pi * 0.98, np.pi * 0.02, 11)  # along the upper edge, inner corner to outer
        ex, ey = 0.5 * W * rim * _spow(np.cos(t), 2 / 2.4), 0.5 * H * rim * _spow(np.sin(t), 2 / 2.4)
        if angry:
            slope, c = top_line(outline * H if style not in ("dot", "bead") else 0.0)
            ey = np.minimum(ey, c + slope * ex)
        T = liner * H
        tip_ = np.array([ex[-1], ey[-1]]) + flick * W * np.array([np.cos(0.5), np.sin(0.5)])
        path = np.vstack([np.column_stack([ex, ey - 0.3 * T]), tip_])
        widths = np.r_[np.linspace(0.5, 1.0, 6), np.ones(5), 0.15] * T
        for k in range(len(path) - 1):  # the lid line as quads cut into the skin above the eye
            d = path[k + 1] - path[k]
            nrm = np.array([-d[1], d[0]]) / max(np.linalg.norm(d), 1e-9)
            skin_cuts.append(("liner", np.array([path[k] - 0.5 * widths[k] * nrm, path[k + 1] - 0.5 * widths[k + 1] * nrm,
                                                 path[k + 1] + 0.5 * widths[k + 1] * nrm,
                                                 path[k] + 0.5 * widths[k] * nrm])))
        for i_ in range(lashes):  # little strokes off the outer half of the lid
            k = 7 + i_ * max(1, 3 // max(lashes, 1))
            base_ = np.array([ex[min(k, 10)], ey[min(k, 10)]])
            d = np.array([np.cos(0.9 + 0.35 * i_), np.sin(0.9 + 0.35 * i_)])
            skin_cuts.append(("liner", np.array([base_ - 0.35 * T * d[::-1] * [1, -1], base_ + 0.35 * T * d[::-1] * [1, -1],
                                                 base_ + 1.6 * T * d])))

    one = head.conform(ball, x0, y0, out=bead_out)
    other = one.mirrored()
    if uneven != 1.0:  # one eye bigger than the other (a zombie's), grown about its own centre across the face
        c = other.verts.mean(0)
        other = kit.Piece(c + (other.verts - c) * [uneven, uneven, 1.0], other.faces, other.slots)
    both = kit.Piece.merge(one, other)
    paint_ = []
    for sl, poly in skin_cuts:  # around the left eye, and mirrored round the right one
        q = np.asarray(poly, float)
        paint_.append((sl, q + [x0, y0]))
        paint_.append((sl, (q * [-uneven, uneven] + [-x0, y0])[::-1]))
    both.paint = paint_
    both.carve = carve_list
    return both


def brows(head, style="bar", x=0.44, y=0.06, length=0.5, thickness=0.11, angle=0.0, depth=0.04, slot="brow",
          taper=0.0, arch=0.12):
    """
    Both brows over the eyes, the inner end `angle` degrees lower (angry) or higher (worried). `bar`: KayKit's
    bevelled blocks (`taper` < 1 thins the outer end); `bushy`: a bar with a ragged top of tufts. `arc`: a brush stroke arching by `arch` (a share of its
    length), thick at the inner end and tapering outward.
    """
    A = head.A
    L, T = length * A, thickness * A
    a = np.radians(angle)
    rot = np.array([[np.cos(a), np.sin(a)], [-np.sin(a), np.cos(a)]])
    if style == "bushy":  # a soft bar with a ragged top edge of tufts, thickest at the inner end (KayKit's orcs)
        u = np.linspace(-0.5, 0.5, 11)
        bend = arch * L * (1 - (2 * u) ** 2)  # arching up in the middle
        top = T * (0.5 - 0.22 * (u + 0.5)) + 0.22 * T * (np.arange(11) % 2) + bend  # tufts every other point
        bottom = -T * (0.42 - 0.12 * (u + 0.5)) + bend
        poly = np.vstack([np.column_stack([u * L, bottom]), np.column_stack([u[::-1] * L, top[::-1]])]) @ rot
        piece = kit.extrude(poly, depth, slot)
        lift = np.clip(piece.verts[:, 2] / max(depth, 1e-9), 0, 1)  # rounded: the front face shrinks in a little
        c = poly.mean(0)
        piece = kit.Piece(np.column_stack([c + (piece.verts[:, :2] - c) * (1 - 0.1 * lift[:, None]),
                                           piece.verts[:, 2]]), piece.faces, piece.slots)
    elif style == "arc":
        u = np.linspace(-0.5, 0.5, 9)
        path = np.column_stack([u * L, arch * L * (1 - (2 * u) ** 2)]) @ rot
        piece = kit.ribbon(path, T * (1 - taper * (u + 0.5)), depth, slot, embed=0.015)
    else:
        poly = blob(L, T, n=18, p=3.2)  # a soft bar, its ends rounded like KayKit's, thinning outward by `taper`
        poly[:, 1] *= 1 - taper * (poly[:, 0] / L + 0.5)
        piece = slab(poly @ rot, depth, slot, bevel=0.45 * depth)
    one = head.conform(piece, x * A, head.y(y))
    return kit.Piece.merge(one, one.mirrored())


def nose(head, style="button", y=-0.38, size=1.0, slot="skin", wart=False, nostril="nostril", embed=0.12):
    """`button` (a small rounded knob), `chevron` (an orc's: a short rounded bridge swelling out of the face to a
    broad V-shaped tip), `teardrop` (KayKit's knight: a narrow bridge swelling to a round tip), `flat`
    (KayKit's barbarian: a broad rounded block), `wedge` (a ridge from the brow to the tip), `wide` (a broad
    flat snout with dark nostrils), `snub` (two nostril dents, nothing raised: for undead and ghosts) or `hooked` (a
    goblin's or a witch's: long, narrow between the eyes, humped, hooking down to its tip over flared nostril wings,
    and with `wart` a wart on its side)."""
    s = size * head.A
    if style == "hooked":  # too long to bend onto the face: lofted from its profile, set rigidly on it, pointing ahead
        # the side profile (y, z) from the root inside the face toward the tip, its top and bottom lines, with the
        # half widths of the bridge on top and of the nostril wings below
        top = [(0.14, -0.12), (0.1, 0.12), (0.04, 0.3), (-0.08, 0.46), (-0.26, 0.6), (-0.44, 0.66)]
        bottom = [(-0.42, -0.12), (-0.46, 0.1), (-0.5, 0.28), (-0.54, 0.42), (-0.58, 0.52), (-0.62, 0.58)]
        bridge = [0.07, 0.07, 0.075, 0.085, 0.1, 0.09]
        wings = [0.2, 0.17, 0.12, 0.11, 0.11, 0.08]
        rings = []
        for (yt, zt), (yb, zb), wt, wb in zip(top, bottom, bridge, wings):
            T, B = np.array([yt, zt]), np.array([yb, zb])
            hi, lo = T + 0.2 * (B - T), T + 0.75 * (B - T)
            rings.append(np.array([(0.0, *T), (wt, *hi), (wb, *lo), (0.0, *B), (-wb, *lo), (-wt, *hi)]) * s)
        verts, faces = kit._loft(rings)
        k = len(rings) * 6
        verts, faces = kit._cap_loops(verts, faces, [list(range(6)), list(range(k - 6, k))],
                                      centers=[rings[0].mean(0), np.array([0.0, -0.6, 0.66]) * s])  # the blunt hooked tip
        parts = [kit.Piece(verts, kit._orient_closed(verts, faces), slot)]
        if wart:
            parts.append(kit.lump((0.08 * s, -0.08 * s, 0.4 * s), (0.04 * s, 0.04 * s, 0.04 * s), "wart", n=6,
                                  lat=(-40, 20)))
        (P,), _ = head.surface(*head.front_angles(0.0, head.y(y)), normals=True)
        return kit.Piece.merge(*parts).moved(kit.Frame.make(P, (0.0, 0.0, 1.0)))
    elif style == "teardrop":  # KayKit's knight: a narrow bridge swelling to a round tip
        bridge = [(dx * 0.05 * s, 0.3 * s, 0.025 * s) for dx in (-1, 1)] + [(0, 0.32 * s, 0.0)]
        bulb = kit.lump((0, -0.08 * s, 0.12 * s), (0.13 * s, 0.11 * s, 0.11 * s), slot, n=12,
                        lat=(-70, -40, -12, 15, 45, 70))
        base = [(dx * 0.12 * s, -0.12 * s, -0.02) for dx in (-1, 1)] + [(0, 0.2 * s, -0.02)]
        piece = kit.hull(np.vstack([bridge, bulb.verts, base]), slot)
    elif style == "flat":  # KayKit's barbarian: a broad rounded block of a nose
        piece = kit.rounded_box((0.5 * s, 0.32 * s, 0.3 * s), slot, center=(0, 0.04 * s, 0.1 * s), roundness=0.5)
    elif style == "button":
        t = np.radians(np.arange(0, 360, 45))
        pts = [(0.17 * s * np.cos(a), 0.14 * s * np.sin(a), -0.02) for a in t]
        pts += [(0.11 * s * np.cos(a), 0.08 * s * np.sin(a) - 0.02 * s, 0.14 * s) for a in t]
        piece = kit.hull(pts, slot)
    elif style == "chevron":  # an orc's: a short bridge swelling out of the face to a broad V-shaped tip
        # rounded cross-sections down the nose (height, half width, depth out of the face), deepest at the tip;
        # their hull is a solid wedge in profile and narrows to a V at the bottom seen from the front
        k = np.linspace(-1, 1, 13)
        pts = []
        for y_, a_, d_ in ((0.22, 0.06, 0.03), (0.08, 0.1, 0.1), (-0.06, 0.17, 0.2), (-0.14, 0.2, 0.25),
                           (-0.2, 0.13, 0.21), (-0.25, 0.04, 0.12)):
            pts += [(a_ * s * kk, y_ * s, d_ * s * (1 - np.abs(kk) ** 2.2) ** 0.7 - 0.02) for kk in k]
        piece = kit.hull(np.asarray(pts), slot)
    elif style == "broad":  # a chunky wedge from between the eyes to a broad, raised, rounded tip (KayKit's orcs)
        bridge = [(dx * 0.07 * s, 0.24 * s, -0.02) for dx in (-1, 1)] + [(dx * 0.06 * s, 0.2 * s, 0.07 * s) for dx in (-1, 1)]
        tip = kit.lump((0.0, -0.13 * s, 0.2 * s), (0.2 * s, 0.11 * s, 0.13 * s), slot, n=10, lat=(-60, -25, 10, 45))
        wings = [(dx * 0.24 * s, -0.2 * s, -0.02) for dx in (-1, 1)] + [(dx * 0.22 * s, -0.22 * s, 0.1 * s) for dx in (-1, 1)]
        piece = kit.hull(np.vstack([bridge, tip.verts, wings]), slot)
    elif style == "wedge":
        piece = kit.hull([(-0.2 * s, -0.2 * s, -0.02), (0.2 * s, -0.2 * s, -0.02), (0.0, 0.3 * s, -0.02),
                          (-0.1 * s, -0.21 * s, 0.17 * s), (0.1 * s, -0.21 * s, 0.17 * s), (0.0, 0.26 * s, 0.04 * s)],
                         slot)
    elif style == "wide":
        bridge = kit.hull([(-0.17 * s, -0.1 * s, -0.02), (0.17 * s, -0.1 * s, -0.02), (0.0, 0.16 * s, -0.02),
                           (-0.13 * s, -0.11 * s, 0.11 * s), (0.13 * s, -0.11 * s, 0.11 * s),
                           (-0.07 * s, 0.06 * s, 0.07 * s), (0.07 * s, 0.06 * s, 0.07 * s)], slot)
        piece = bridge
        for dx in (-1, 1):  # nostrils dented into the nose's lower front, not plates on it
            hole = blob(0.07 * s, 0.045 * s) + [dx * 0.07 * s, -0.085 * s]
            piece = kit.carve(piece, [hole], hole, nostril, 0.03 * s, piece.verts.mean(0) + [0, 0.05 * s, 0], {slot})
    elif style == "snub":  # two slits dented into the face itself
        empty = kit.Piece.merge()
        empty.carve = []
        for dx in (-1, 1):
            a_ = np.radians(-dx * 25)
            R = np.array([[np.cos(a_), -np.sin(a_)], [np.sin(a_), np.cos(a_)]])
            hole = blob(0.06 * s, 0.09 * s) @ R.T + [dx * 0.05 * s, head.y(y)]
            empty.carve.append(("dark", [hole], hole, 0.03 * s))
        return empty
    else:
        return None
    # sunk well into the skull: the face is curved, so a back cut flat on it leaves gaps at the sides
    V = piece.verts.copy()
    V[:, 2] = np.where(V[:, 2] < 0.02 * s, V[:, 2] - embed * s, V[:, 2])
    return head.conform(kit.Piece(V, piece.faces, piece.slots), 0.0, head.y(y))


def _two_tone(poly, root_y, toward, split, depth, slot, tip, bevel=0.003):
    """A flat tooth (`poly`, convex) as two prisms side by side: from its root at `root_y` to `split` of the way to
    its point (`toward` +1 up, -1 down) in `slot`, the rest in `tip` (KayKit's yellowed teeth with white points)."""
    P = np.asarray(poly, float)
    reach = np.max(toward * (P[:, 1] - root_y))
    cut = root_y + toward * split * reach
    near = clip(P, 0.0, toward, toward * cut)          # toward * y <= toward * cut: the root part
    far = clip(P, 0.0, -toward, -toward * cut)          # the rest: the point
    parts = [kit.prism(q, depth, sl, bevel=bevel) for q, sl in ((near, slot), (far, tip or slot)) if len(q) >= 3]
    return kit.Piece.merge(*parts)


def mouth(head, y=-0.7, width=0.32, curve=0.0, thickness=0.035, teeth=(), tooth=0.07, slot="mouth", tooth_slot="tusk",
          tip="tusk_tip", split=0.45, style="line", height=0.25, upper=None, lower=None, lip="skin_dark",
          lip_width=0.05, depth=0.05, seed=0):
    """
    A mouth, its corners `curve` up (a smile, > 0) or down (a frown, < 0) as a share of its width.

    `line`: a closed mouth, a crease cut into the skin (`thickness` wide); `teeth` = `(x, length)` pairs along it
    (x a share of the half width, length of the head's half width): fangs over the upper lip (length > 0) or up
    from the lower one (< 0), `tooth` wide.

    `open`: a sculpted open mouth `height` tall at its middle (a share of the half width), tapering shut at the
    corners: an opening carved `depth` (a share of the half width) into the face, colored `slot`, its lips the
    skin round it recolored `lip`; upper and lower rows of teeth fitted inside it, following its curve: `upper` /
    `lower` = `dict(count, shape="row" | "fang" | "needle", length (a share of the opening), span (a share of the
    width they cover), crooked (0..1: uneven, tilted, a gap or two))`. A `row` is one band of teeth side by side,
    notched where they meet; a grin is a wide `curve`d mouth with a full `row` above.

    Every tooth is two-toned: `tooth_slot` at the root, `tip` past `split` of its length (None: one color). The
    returned piece is the teeth; the opening, lips and creases are outlines `build` cuts into the skin.
    """
    A = head.A
    W = width * A
    y0 = head.y(y)
    parts, paint, carve = [], [], []

    def to_face(poly):  # mouth coordinates -> the front view `build` cuts in
        return np.asarray(poly, float) + [0.0, y0]

    if style != "open":
        lip_y = lambda u: curve * W * (2 * u / W) ** 2  # noqa: E731
        u = np.linspace(-W / 2, W / 2, 9)
        half = 0.5 * thickness * A
        for a, b in zip(u[:-1], u[1:]):  # the crease, a strip of quads cut into the skin
            paint.append((slot, to_face([(a, lip_y(a) - half), (b, lip_y(b) - half), (b, lip_y(b) + half),
                                          (a, lip_y(a) + half)])))
        for x, length in teeth:
            ux, w, L = x * W / 2, tooth * A / 2, length * A
            root = lip_y(ux) - np.sign(L) * 0.4 * thickness * A
            parts.append(_two_tone([(ux - w, root), (ux + w, root), (ux, root - L)], root, -np.sign(L), split, 0.03,
                                   tooth_slot, tip, bevel=0.006))
    else:
        H = height * A
        top = lambda s_: curve * W * s_ ** 2  # noqa: E731
        bottom = lambda s_: top(s_) - H * np.clip(1 - s_ ** 2, 0, 1) ** 0.8  # noqa: E731
        s_ = np.linspace(-1, 1, 17)
        strips = []
        lw = lip_width * A
        for a, b in zip(s_[:-1], s_[1:]):  # the opening as convex quads, and a band of lip along each edge
            xa, xb = a * W / 2, b * W / 2
            strips.append(to_face([(xa, bottom(a)), (xb, bottom(b)), (xb, top(b)), (xa, top(a))]))
            for edge, sgn in ((top, 1), (bottom, -1)):
                ka, kb = lw * (1 - 0.6 * a ** 2), lw * (1 - 0.6 * b ** 2)
                paint.append((lip, to_face([(xa, edge(a)), (xb, edge(b)), (xb, edge(b) + sgn * kb),
                                            (xa, edge(a) + sgn * ka)])))
        outline = to_face([(x * W / 2, top(x)) for x in s_] + [(x * W / 2, bottom(x)) for x in s_[::-1][1:-1]])
        carve.append((slot, strips, outline, depth * A))
        rng = np.random.default_rng(seed)
        for spec, sgn in ((upper, -1), (lower, 1)):  # upper teeth hang from the top edge, lower ones stand up
            if not spec:
                continue
            n, shape = spec.get("count", 6), spec.get("shape", "row")
            crooked, length = spec.get("crooked", 0.0), spec.get("length", 0.5)
            span = spec.get("span", 0.9 if shape == "row" else 0.7)
            edges = np.linspace(-span, span, n + 1)
            for k in range(n):
                a, b = edges[k], edges[k + 1]
                if shape != "row":  # fangs and needles: narrower than their slot, never wider than a fang should be
                    c, half = 0.5 * (a + b), min(0.3 * (b - a), 0.14 * span)
                    a, b = c - half, c + half
                if crooked and rng.random() < 0.18 * crooked:
                    continue  # a lost tooth
                us = np.linspace(a, b, 4)
                edge = top if sgn < 0 else bottom
                gap = np.array([top(u_) - bottom(u_) for u_ in us])
                if gap.min() < 0.12 * H:
                    continue
                L = gap * length * (1 + crooked * rng.uniform(-0.35, 0.25))
                drop = L * (np.array([0.72, 1.0, 1.0, 0.72]) if shape == "row" else np.array([0.0, 0.6, 0.6, 0.0]))
                root = np.array([edge(u_) for u_ in us]) + sgn * 0.02 * gap  # just inside the lip
                pts = [(u_ * W / 2, r) for u_, r in zip(us, root)]
                pts += [(u_ * W / 2, r + sgn * d) for u_, r, d in zip(us[::-1], root[::-1], drop[::-1])]
                if shape != "row":
                    pts = pts[:4] + [(0.5 * (a + b) * W / 2,
                                      root.mean() + sgn * L.max() * (1.25 if shape == "needle" else 1.0))]
                if crooked:  # tilted a little about its root
                    t = np.radians(crooked * rng.uniform(-12, 12))
                    x0, yr = 0.5 * (a + b) * W / 2, root.mean()
                    R = np.array([[np.cos(t), -np.sin(t)], [np.sin(t), np.cos(t)]])
                    pts = [tuple(R @ (np.array(p_) - [x0, yr]) + [x0, yr]) for p_ in pts]
                parts.append(_two_tone(pts, root.mean(), sgn, split, 0.008, tooth_slot, tip, bevel=0.0))
    teeth_piece = head.conform(kit.Piece.merge(*parts), 0.0, y0) if parts else kit.Piece.merge()
    teeth_piece.paint, teeth_piece.carve = paint, carve
    return teeth_piece


def tusks(head, x=0.2, y=-0.68, length=0.3, radius=0.07, spread=-0.2, curve=0.35, broken=0.0, mirror=True,
          out=-0.01, slot="tusk", tip="tusk_tip", split=0.5):
    """
    Tusks from the lower jaw (`out` over the skin: a jaw piece's ledge), curving up past the lip by `curve`, their
    tips leaning in toward the nose (`spread` < 0) so they frame the face rather than hide the eyes. A pair at
    +-`x`, or one at `x` (`mirror` false: an odd tooth); `broken` > 0 snaps that share off the tip; `tip` colors
    the part past `split` of its length in that slot (KayKit's yellowed tusks with white points).
    """
    A = head.A
    (theta,), (phi,) = head.front_angles(x * A, head.y(y))
    (P,), (n,) = head.surface(theta, phi, out, normals=True)
    L = length * A
    up = np.array([0.0, 1.0, 0.0])
    side = np.array([np.sign(x) or 1.0, 0.0, 0.0])
    fwd = n * [0.2, 1.0, 1.0]  # out of the face, not sideways where the face turns
    fwd /= np.linalg.norm(fwd)
    path = kit.bezier(P, P + curve * L * fwd + 0.4 * L * up, P + curve * L * fwd + L * up + spread * L * side, 8)
    path = np.vstack([P - 0.35 * radius * A * fwd - 0.2 * radius * A * up, path])  # rooted inside the lip
    keep = int(round(9 * (1 - broken))) + 1
    t = np.linspace(0.0, 1.0, 10)[:keep]
    th = 2 * np.pi * np.arange(8) / 8
    one = kit.sweep(path[:keep], np.column_stack([radius * A * np.cos(th), 0.8 * radius * A * np.sin(th)]), slot,
                    scale=np.maximum(np.where(t < 0.15, 1.0, (1 - (t - 0.15) / 0.85) ** 0.7), 0.06))
    if tip:  # two-toned: yellowed at the root, white toward the tip
        c = one.verts[one.faces].mean(1)
        along = np.linalg.norm(c - path[0], axis=1) / max(np.linalg.norm(path[keep - 1] - path[0]), 1e-9)
        one = kit.Piece(one.verts, one.faces, np.where(along > split, tip, slot).astype(object))
    return kit.Piece.merge(one, one.mirrored()) if mirror else one


def jaw(head, top=-0.48, droop=0.2, width=80.0, chin=0.35, jowl=0.2, lip=0.16, bottom=-0.98, rise=0.3, cols=23,
        rows=14, slot="skin", mouth="mouth", tusks=None, lip_slot="skin"):
    """
    A heavy lower jaw: a smooth patch of skin swelling out of the lower face and sinking back into it at its sides
    and bottom, so it reads as part of the head (KayKit's skeletons keep the jaw a separate piece too). Its top
    edge, the mouth line, `droop`s toward the corners (a scowl; shares of the half height); its lower edge rises
    by `rise` toward the sides. `chin` juts out its middle, `jowl` swells it between chin and sides, `lip` rolls a
    rounded lower lip along the top edge (an underbite; shares of the half width), in `lip_slot`. `mouth` is the
    dark crease above the lip, cut flat into the face (the piece carries it as `paint` outlines for `build`);
    `tusks` (a dict or a list of `tusks` arguments: mix pairs, odd teeth and broken ones) stand on the lip.
    """
    A = head.A
    u = np.linspace(-1, 1, cols)
    th = FRONT + np.radians(width) * u
    t = np.linspace(0, 1, rows)[:, None]

    def edge_top(u):
        return top - droop * u ** 2

    low = np.minimum(bottom + rise * u ** 2, edge_top(u) - 0.1)  # its corners keep some height
    yn = edge_top(u) + (low - edge_top(u)) * t
    swell = np.sin(np.pi * np.clip(t, 0, 1) ** 1.1) ** 0.8  # rounded: nothing at the edges, full through the middle
    lipped = np.clip(1 - t / 0.25, 0, 1) ** 2  # the top edge stands proud too, so the lip sits on a shelf
    out = A * (swell * (chin * (1 - u ** 2) ** 2 + jowl * 4 * u ** 2 * (1 - u ** 2))
               + 0.6 * lip * (1 - u ** 2) ** 1.5 * lipped)
    phi = head.phi(yn)
    grid = np.broadcast_to(th, yn.shape)
    outer = head.surface(grid, phi, out).reshape(rows, cols, 3)
    inner = head.surface(grid, phi, -0.03).reshape(rows, cols, 3)
    parts = [kit.patch_shell(outer, inner, slot)]
    if lip:  # the lower lip: a rounded roll along the top edge, thick in the middle, thinning to the corners
        ul = np.linspace(-0.92, 0.92, 15)
        thl = FRONT + np.radians(width) * ul
        k = (1 - ul ** 2) ** 0.7
        path = head.surface(thl, head.phi(edge_top(ul) - 0.05), A * 0.6 * lip * (1 - ul ** 2) ** 1.5)
        parts.append(kit.sweep(path, kit.ring_profile(0.09 * A, 0.07 * A, 8), lip_slot, up=(0.0, 1.0, 0.0),
                               scale=np.maximum(k, 0.15)))
    piece = kit.Piece.merge(*parts)
    paint = []
    if mouth:  # the crease above the lip, cut flat into the face
        um = np.linspace(-0.85, 0.85, 9)
        line = head.surface(FRONT + np.radians(width) * um, head.phi(edge_top(um) + 0.035))
        path = (line[:, :2] - [head.cx, head.cy]) / [A, head.Hh]
        paint = FEATURES["paint"](head, [dict(path=path, width=(0.03, 0.06, 0.07, 0.06, 0.03), slot=mouth)])
    parts = [piece]
    for spec in ([] if not tusks else tusks if isinstance(tusks, list) else [tusks]):
        spec = dict(spec)
        x = spec.pop("x", 0.26)
        (theta,), _ = head.front_angles(x * A, head.y(top))
        ux = (theta - FRONT) / np.radians(width)
        parts.append(FEATURES["tusks"](head, x=x, y=edge_top(ux) - 0.04, out=0.6 * lip * (1 - ux ** 2) ** 1.5 * A,
                                       **spec))
    out_piece = kit.Piece.merge(*parts)
    out_piece.paint = paint
    return out_piece


def block_jaw(head, top=-0.36, bottom=-1.12, width=0.86, front=1.12, chin=0.66, back=0.05, slot="skin",
              shade="skin_dark", lip="lip", mouth="mouth", tusk="tusk", tusks=0.75, tusk_x=0.62, tusk_width=0.17,
              tooth=0.16):
    """
    An orc's underbite: a squared jaw block jutting out under the face, wider than the skull, from the mouth line
    (`top`) down past it (`bottom`; shares of the half height), `width` half wide and reaching `front` forward
    (shares of the half width), tapering to a `chin` wide; darker (`shade`) below its middle. A thick `lip` rolls
    along its top front edge, the mouth's crease above it is cut into the face, and two `tusks` (length, a share of
    the jaw's height) rise from its corners at +-`tusk_x`, with one small chipped `tooth` between them.
    """
    A = head.A
    # rows down the jaw: (height share, half width, reach forward, reach back; shares of the half width)
    rows = [(top, width * 0.92, front, back + 0.25), (top - 0.22, width, front * 1.01, back + 0.35),
            (top + 0.6 * (bottom - top), width * 0.96, front * 0.94, back + 0.4),
            (bottom, width * chin, front * 0.76, back + 0.15)]
    def ring(yn, hw, fz, bz, k=1.0):  # a squarish ring: flat front, rounded corners
        out = []
        for a in np.radians(np.arange(0, 360, 12)):
            c, sn = np.cos(a), np.sin(a)
            z = (fz if sn > 0 else bz) * np.sign(sn) * abs(sn) ** 0.55
            out.append((head.cx + k * hw * A * np.sign(c) * abs(c) ** 0.45, head.y(yn), head.cz0 + k * z * A))
        return out

    mid = top + 0.5 * (bottom - top)
    upper = kit.hull(np.asarray(ring(*rows[0]) + ring(*rows[1]) + ring(mid, *rows[2][1:])), slot)
    lower = kit.hull(np.asarray(ring(mid + 0.04, *rows[2][1:], k=0.985) + ring(*rows[3], k=0.985)
                                + ring(*rows[2], k=0.985)), shade)  # the darker lower jaw, a clean band below
    parts = [upper, lower]
    # the lower lip: a thick roll along the top front edge
    ys = head.y(top) - 0.02
    ul = np.linspace(-0.85, 0.85, 13)
    zf = head.cz0 + front * A * np.abs(np.sin(np.arccos(np.clip(ul, -1, 1)))) ** 0.55 - 0.03
    path = np.column_stack([head.cx + ul * width * 0.9 * A, np.full(len(ul), ys), zf])
    parts.append(kit.sweep(path, kit.ring_profile(0.13 * A, 0.1 * A, 10), lip, up=(0.0, 1.0, 0.0),
                           scale=np.maximum((1 - ul ** 2) ** 0.35, 0.3)))
    H = (top - bottom) * head.Hh
    for sgn in (-1, 1):  # tusks from the corners, thick at the root, curving up past the lip and in toward the cheek
        base = np.array([head.cx + sgn * tusk_x * A, ys, head.cz0 + front * A * 0.86])
        L = tusks * H
        path = kit.bezier(base - [0, 0.12 * L, 0.06 * L], base + [sgn * 0.06 * L, 0.55 * L, 0.1 * L],
                          base + [-sgn * 0.1 * L, L, 0.0], 9)
        t = np.linspace(0, 1, len(path))
        parts.append(kit.sweep(path, kit.ring_profile(tusk_width * A, 0.75 * tusk_width * A, 8), tusk,
                               up=(0.0, 0.0, 1.0), scale=np.maximum((1 - t) ** 0.8, 0.05)))
    if tooth:  # a small chipped tooth off centre
        b = np.array([head.cx + 0.14 * A, ys + 0.02, head.cz0 + front * A * 0.95])
        parts.append(kit.rounded_box((tooth * A, 1.2 * tooth * A, 0.6 * tooth * A), tusk, center=b + [0, 0.5 * tooth * A, 0],
                                     roundness=0.4))
    piece = kit.Piece.merge(*parts)
    um = np.linspace(-0.78, 0.78, 7)  # the mouth's crease, cut flat into the face just above the lip
    piece.paint = paint(head, [dict(path=[(u * width * 0.9, top + 0.07) for u in um], width=(0.04, 0.09, 0.04),
                                    slot=mouth)])
    return piece


def beard(head, top=0.0, under_nose=-0.52, width=92.0, bulk=0.16, length=0.55, tufts=5, mustache=0.1, cols=21,
          rows=10, slot="beard"):
    """
    A full beard: a patch of hair running from sideburns at the temples (`top`) down round the cheeks to under the
    nose (`under_nose`, where a drooping `mustache` lies over the mouth) and over the chin, `bulk` thick (shares of
    the half width), lumpy in `tufts`, its lower edge pulled down into a jagged point `length` below the chin.
    """
    A = head.A
    u = np.linspace(-1, 1, cols)
    th = FRONT + np.radians(width) * u
    t = np.linspace(0, 1, rows)[:, None]
    edge = under_nose + (top - under_nose) * np.abs(u) ** 4  # bare cheeks under the eyes, sideburns at the sides
    yn = edge + (-0.97 - edge) * t
    phi = head.phi(yn)
    grid = np.broadcast_to(th, yn.shape)
    lumps = 1 + 0.3 * np.abs(np.sin(0.5 * np.pi * tufts * (u + 1)))
    swell = A * bulk * lumps * (1 - 0.6 * u ** 2) * np.sin(np.pi * (0.12 + 0.88 * t) ** 0.8) + 0.012
    jag = 1 + 0.35 * np.abs(np.sin(0.5 * np.pi * (2 * tufts + 1) * (u + 1)))  # the lower edge in points
    sag = (length * A * t ** 2 * (1 - u ** 2) ** 1.5 * jag)[..., None] * [0.0, -1.0, 0.25]
    outer = head.surface(grid, phi, swell).reshape(rows, cols, 3) + sag
    inner = head.surface(grid, phi, -0.02).reshape(rows, cols, 3) + 0.8 * sag
    parts = [kit.patch_shell(outer, inner, slot)]
    if mustache:
        for sgn in (-1, 1):
            pts = [(sgn * 0.04, under_nose + 0.04), (sgn * 0.28, under_nose - 0.02), (sgn * 0.46, under_nose - 0.16),
                   (sgn * 0.5, under_nose - 0.32)]
            (th_m, ph_m) = head.front_angles(np.array([p[0] for p in pts]) * A, head.y(np.array([p[1] for p in pts])))
            path = head.surface(th_m, ph_m, A * bulk * 0.6 + 0.03)
            parts.append(kit.tube(kit.spline(path, 8), mustache * A, slot, sides=6, taper=0.6))
    return kit.Piece.merge(*parts)


def _ear_shape(style, s, base, angle, notch_depth=0.0):
    """
    One ear in its own frame (x across its thickness, the inner side toward -x; y up; z out from the head): a
    lofted leaf with rounded rims, thinning toward the tip, its inner side a sunken bowl (deepest near the root).
    Returns `(piece, lower_edge(t) -> (y, z), slot_of_face)` where faces carry "skin" / "inner" / "canal".
    `notch_depth` > 0 bites a rounded dip out of the upper rim.
    """
    L = {"round": 0.42, "long": 0.8}.get(style, 0.85) * s
    a = np.radians({"round": 0.0, "long": 60.0}.get(style, angle))
    h = 0.56 * s * base
    th0 = 0.075 * s * base * (0.8 if style == "leaf" else 1.0)

    def half_width(t):
        if style == "round":  # KayKit's half disc
            return 0.62 * h * np.sqrt(np.clip(1 - t ** 2, 0, 1)) + 0.04 * h
        if style == "leaf":  # broadest a third of the way out
            return 0.62 * h * (1 - t) ** 0.75 * (0.75 + 1.1 * t)
        return 0.5 * h * (1 - t) ** 0.9 * (1 + 0.3 * np.sin(np.pi * t))

    ts = np.r_[-0.3, -0.15, np.linspace(0.0, 0.94, 15), 0.985]  # the root runs on into the skull
    phis = 2 * np.pi * (np.arange(20) + 0.5) / 20
    rings, info = [], []
    for t in ts:
        w = half_width(max(t, 0.0))
        w_up = w * (1 - notch_depth * np.exp(-((t - 0.55) / 0.07) ** 2))
        w_low = 0.94 * w
        th = th0 * (1 - 0.55 * max(t, 0.0))
        rise, z = (L * np.sin(a) * t + 0.04 * L * t * (1 - t), L * np.cos(a) * t) if t >= 0 else (0.0, L * t)
        c, sn = np.cos(phis), np.sin(phis)
        dish = np.clip(1 - (c / 0.68) ** 2, 0, 1) * np.clip(np.sin(np.pi * np.clip((t - 0.04) / 0.8, 0, 1)), 0, 1)
        x = th * sn + np.where(sn < 0, 1.35 * th * dish * -sn, 0.0)  # the inner side sunk into a bowl
        y = rise + np.where(c >= 0, w_up, w_low) * np.sign(c) * np.abs(c) ** 0.85
        rings.append(np.column_stack([x, y, np.full(len(c), z)]))
        info.append(np.column_stack([np.full(len(c), t), c, sn, dish]))
    verts, faces = kit._loft(rings)
    tip = np.array([0.0, L * np.sin(a) + 0.0, L * np.cos(a)])
    n = len(phis)
    verts, faces = kit._cap_loops(verts, faces, [list(range(n)), list(range((len(ts) - 1) * n, len(ts) * n))],
                                  centers=[rings[0].mean(0), tip])
    info = np.vstack(info)
    nq = 2 * (len(ts) - 1) * n  # the loft's faces come in quad pairs: color whole quads, so edges follow the rows
    quad = np.arange(len(faces)) // 2
    ring_i, col_j = quad // n, quad % n
    r0, r1, c1 = np.minimum(ring_i, len(ts) - 1), np.minimum(ring_i + 1, len(ts) - 1), (col_j + 1) % n
    centre = 0.25 * (info[r0 * n + col_j] + info[r0 * n + c1] + info[r1 * n + col_j] + info[r1 * n + c1])
    fi = np.where((np.arange(len(faces)) < nq)[:, None], centre, np.vstack([info, info[:1], info[-1:]])[faces].mean(1))
    in_canal = (fi[:, 0] < 0.35) & (fi[:, 3] > 0.6) & (fi[:, 2] < -0.42)
    in_inner = (fi[:, 2] < -0.35) & (fi[:, 3] > 0.25)
    in_mid = (fi[:, 2] < -0.20) & (fi[:, 3] > 0.12)
    slots = np.where(in_canal, "canal",
            np.where(in_inner, "inner",
            np.where(in_mid, "mid", "skin"))).astype(object)
    piece = kit.Piece(verts, kit._orient_closed(verts, faces), slots)

    def lower_edge(t):
        return L * np.sin(a) * t + 0.04 * L * t * (1 - t) - 0.94 * half_width(t), L * np.cos(a) * t

    return piece, lower_edge


def ears(head, style="round", y=-0.15, size=1.0, angle=0.0, sweep=20.0, base=1.0, slot="skin", inner="skin_dark",
         canal=None, ring=None, ring_slot="gold", rings=1, ring_size=0.07, notch=None, notch_depth=0.35):
    """
    Both ears on the sides of the head, lofted with rounded rims and a sunken inner bowl (`inner`; a `canal` slot
    darkens its deepest part near the root, for big ears). `round`: KayKit's half discs. `pointed`: leaves reaching out (and up by `angle`
    degrees), swept back by `sweep`, an orc's or a goblin's. `long`: tall pointed ears standing up and back, an
    elf's or a demon's. `leaf`: huge ears broadest well out from the head, a goblin's. Jewelry: `rings` rings
    `ring_size` across through the lower rim of the `ring` ear ("left", "right", "both"), in `ring_slot`; a `notch`
    (a rounded dip `notch_depth` of the rim's half height deep) in the upper rim of one ear ("left", "right",
    "both"). `base` scales the ear's root height and thickness against its length.
    """
    s = size * head.A
    f = kit.band_frame(head, head.y(y), 0.0, -0.06 * s).rotated(-sweep, "y")  # local x points back, z out

    def built(depth):
        piece, edge = _ear_shape(style, s, base, angle, depth)
        mid_slot = f"{inner}_mid" if inner != slot else slot
        names = {"skin": slot, "mid": mid_slot, "inner": inner, "canal": canal or inner}
        return kit.Piece(piece.verts, piece.faces, np.array([names[k] for k in piece.slots], object)).moved(f), edge

    one, lower_edge = built(0.0)
    cut = built(notch_depth)[0] if notch else one
    ears_ = [cut if notch in ("left", "both") else one, (cut if notch in ("right", "both") else one).mirrored()]
    if ring:  # rings pierced through the lower rim, spaced out from the root
        r = ring_size * s
        loops = []
        for k in range(rings):
            yy, zz = lower_edge(0.2 + 0.17 * k)
            t = 2 * np.pi * np.arange(12) / 12
            loops.append(kit.sweep(np.column_stack([r * np.cos(t), yy + 0.25 * r - r + r * np.sin(t) * 1.0,
                                                    np.full(12, zz)]),
                                   kit.ring_profile(0.22 * r, 0.22 * r, 6), ring_slot, closed=True).moved(f))
        loop = kit.Piece.merge(*loops)
        for side, k in (("left", 0), ("right", 1)):
            if ring in (side, "both"):
                ears_.append(loop if k == 0 else loop.mirrored())
    return kit.Piece.merge(*ears_)


def horns(head, x=0.38, y=0.62, length=1.0, radius=0.17, out=0.35, back=0.3, curl=0.25, slot="horn"):
    """A pair of curved horns growing from the top of the forehead at +-`x`: up, out by `out` and back by `back`
    (shares of their `length`, in head half widths), their tips curling in by `curl` (a devil's or an imp's)."""
    A = head.A
    L = length * A
    out_h = []
    for sgn in (-1, 1):
        (P,), (n,) = head.surface(*head.front_angles(sgn * x * A, head.y(y)), normals=True)
        side, up, rear = np.array([sgn, 0.0, 0.0]), np.array([0.0, 1.0, 0.0]), np.array([0.0, 0.0, -1.0])
        path = kit.spline([P - 0.7 * radius * A * n, P + L * (0.35 * out * side + 0.35 * up + 0.2 * back * rear),
                           P + L * (out * side + 0.75 * up + 0.6 * back * rear),
                           P + L * ((out - curl) * side + 1.0 * up + back * rear)], 10)
        out_h.append(kit.tube(path, radius * A, slot, sides=7, taper=0.8))
    return kit.Piece.merge(*out_h)


def hair(head, style="cap", **kw):
    """Scripted hair (`cap`, `topknot`) or a sculpted style from the library refitted to this head (`sculpt`, with
    the sculpt's `name`)."""
    if style == "sculpt":
        from accessories import sculpts
        return sculpts.hair(head, **kw)
    return {"cap": hair_cap, "topknot": topknot, "tuft": tuft}[style](head, **kw)


def hair_cap(head, line=((0, 0.45), (40, 0.3), (90, -0.2), (130, -0.8), (180, -0.85)), peak=0.0, thickness=0.05,
             slot="hair",
             sides=32, rows=9):
    """
    A shell of hair over the cranium down to a hairline: `line` gives its height (a share of the half height) at
    angles from the front (degrees, 0 = the forehead, 180 = the nape), `peak` dips it at the forehead's middle (a
    widow's peak). Its rim is chamfered like KayKit's hair.
    """
    th = 2 * np.pi * np.arange(sides) / sides
    from_front = np.degrees(np.abs(np.angle(np.exp(1j * (th - FRONT)))))
    deg, yn = np.asarray(line, float).T
    end = np.interp(from_front, deg, yn) - peak * np.exp(-(from_front / 14.0) ** 2)
    phi_end = head.phi(np.clip(end, -0.97, 0.97))
    outer = [head.surface(th, phi_end * i / rows, thickness) for i in range(rows + 1)]
    inner = [head.surface(th, phi_end * i / rows, -0.02) for i in range(rows + 1)]
    lip = head.surface(th, phi_end + 0.04, 0.4 * thickness)
    verts, faces = kit.weld_collapsed(*kit._loft(outer + [lip] + inner[::-1]), tol=1e-7)
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


def topknot(head, size=0.3, tail=0.75, slot="hair", tie="tie"):
    """A warrior's bun on the crown, tied off, its tail falling back."""
    A = head.A
    r = size * A
    (P,), _ = head.surface(3 * np.pi / 2, 0.3, 0.0, normals=True)
    up = np.array([0.0, 1.0, 0.0])
    c = P + 0.55 * r * up  # its lower part inside the scalp: no gap under a rounded crown
    t = np.radians(np.arange(0, 360, 45))
    bun = kit.hull([c + [r * np.cos(a) * np.cos(b), 0.9 * r * np.sin(b), r * np.sin(a) * np.cos(b)]
                    for a in t for b in np.radians((-50, 0, 45, 80))], slot)
    band = kit.sweep(np.array([P + 0.15 * r * up + 0.62 * r * np.array([np.cos(a), 0.0, np.sin(a)])
                               for a in np.linspace(0, 2 * np.pi, 9)[:-1]]),
                     kit.rect_profile(0.35 * r, 0.25 * r, 0.05 * r), tie, closed=True)
    back = np.array([0.0, 0.0, -1.0])
    L = tail * A
    tail_piece = kit.tube(kit.spline([c + 0.4 * r * back, c + 0.45 * L * back + 0.15 * L * up,
                                      c + 0.75 * L * back - 0.35 * L * up, c + 0.8 * L * back - 0.8 * L * up], 10),
                          0.7 * r, slot, sides=6, taper=0.9)
    return kit.Piece.merge(bun, band, tail_piece)


def tuft(head, columns=4, height=3, size=0.17, slot="hair", seed=4):
    """A mohawk tuft (KayKit's orc): `columns` columns of chunky faceted clumps along the top of the head from the
    hairline back, the front ones stacked `height` clumps tall, each column leaning further back."""
    from accessories.golems import rock

    A = head.A
    rng = np.random.default_rng(seed)
    parts = []
    for k in range(columns):
        phi = 0.36 - 0.2 * k  # from above the forehead back over the crown, the clumps touching
        theta = np.pi / 2 if phi >= 0 else 3 * np.pi / 2
        (P,), (n,) = head.surface(theta, abs(phi), 0.0, normals=True)
        r = size * A * (1.0 - 0.12 * k)
        back = np.radians(12 + 14 * k)
        lean = np.array([0.0, np.cos(back), -np.sin(back)])
        for lvl in range(max(1, height - k // 2)):
            c = P + 0.25 * r * n + (0.15 + 0.85 * lvl) * r * lean + rng.normal(0.0, 0.12 * r, 3) * [1.5, 0.2, 0.5]
            sz = r * (1.0 - 0.12 * lvl) * rng.uniform(0.85, 1.15)
            parts.append(rock((1.15 * sz, 1.6 * sz, 1.1 * sz), c, slot, bevel=0.2, bulge=0.04, jitter=0.16, taper=0.35,
                              turn=(-np.degrees(back) + rng.uniform(-15, 15), rng.uniform(-40, 40), rng.uniform(-20, 20)),
                              seed=seed + 10 * k + lvl))
    return kit.Piece.merge(*parts)


def surface_band(head, yn, height, thickness, slot, tilt=(0.0, 0.0), theta=None, sides=28, bevel=0.35, inset=0.012):
    """
    A band over the cranium centred on the height share `yn`, rising by `tilt` = (toward the front, toward +x) as it
    goes round, `height` tall (a share of the half height), `thickness` off the skin. `theta` limits it to an arc of
    angles (then its ends are capped): a mummy's wraps, an eye slit, a headband.
    """
    th = 2 * np.pi * np.arange(sides) / sides if theta is None else np.asarray(theta, float)
    centre = yn + tilt[0] * np.sin(th) + tilt[1] * np.cos(th)
    c = bevel * thickness
    dt = min(c / max(height * head.Hh, 1e-9), 0.45)
    profile = [(0.0, -inset), (0.0, thickness - c), (dt, thickness), (1 - dt, thickness), (1.0, thickness - c),
               (1.0, -inset)]
    rings = [head.surface(th, head.phi(np.clip(centre + (t - 0.5) * height, -0.97, 0.97)), out) for t, out in profile]
    verts, faces = kit._loft(rings, closed_rings=theta is None, closed_profile=True)
    if theta is not None:
        n = len(th)
        verts, faces = kit._cap_loops(verts, faces, [[k * n for k in range(len(rings))],
                                                     [k * n + n - 1 for k in range(len(rings))]])
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


def wraps(head, bands=((-0.9, 0.16, (0.06, 0.0)), (-0.68, 0.2, (-0.05, 0.03)), (0.3, 0.2, (0.05, -0.02)),
                       (0.52, 0.2, (-0.06, 0.03)), (0.72, 0.16, (0.04, 0.0))),
          slit=(-0.12, 0.42, 62.0), crown=0.62, diagonal=(-0.1, 0.3, 0.22, 85.0), thickness=0.03, slot="wrap",
          slit_slot="dark"):
    """
    A mummy's bandages: tilted bands (`bands` = (height share, band height, tilt)) over a cranium of a darker wrap
    color, a wrapped `crown` down to that height share, an eye `slit` = (height share, height, half angle in
    degrees) of dark cloth across the face, and a `diagonal` bandage over the back of the head where the slit's
    height leaves it bare = (height share at the back, band height, rise from one side to the other, half angle).
    """
    out = [surface_band(head, yn, h, thickness, slot, tilt=tilt) for yn, h, tilt in bands]
    out.append(hair_cap(head, line=((0, crown), (180, crown + 0.08)), thickness=0.6 * thickness, slot=slot))
    if slit:
        yn, h, half = slit
        arc = FRONT + np.radians(np.linspace(-half, half, 13))
        out.append(surface_band(head, yn, h, 0.012, slit_slot, theta=arc))
        for edge, tilt in ((yn + h / 2 + 0.05, (0.03, 0.0)), (yn - h / 2 - 0.05, (-0.03, 0.0))):
            out.append(surface_band(head, edge, 0.17, thickness + 0.004, slot, tilt=tilt))
    if diagonal:
        yn, h, rise, half = diagonal
        arc = 3 * np.pi / 2 + np.radians(np.linspace(-half, half, 15))
        out.append(surface_band(head, yn, h, thickness + 0.008, slot, tilt=(0.0, rise), theta=arc))
    return kit.Piece.merge(*out)


def scar(head, x=-0.42, y=-0.05, length=0.55, width=0.05, slot="scar", count=2, angle=-55.0):
    """Slashes across one eye as `paint` strokes: `count` parallel cuts `length` long (half widths) at `angle`
    degrees, centred on (`x`, `y`) in the face's shares."""
    a = np.radians(angle)
    d = np.array([np.cos(a), np.sin(a)])
    strokes = []
    for i in range(count):
        off = np.array([np.sin(a), -np.cos(a)]) * (i - (count - 1) / 2) * 0.12
        ends = [np.array([x, 0.0]) + off + sgn * 0.5 * length * d for sgn in (-1, 1)]
        strokes.append(dict(path=[(e[0], y + e[1] * head.A / head.Hh) for e in ends], width=(0.6 * width, width,
                                                                                              0.4 * width),
                            slot=slot))
    return strokes


def paint(head, strokes, slot="paint"):
    """
    Face paint, scars and tattoos as outlines to cut into the face. `strokes` mixes `WARPAINT` pattern names
    (reusable on any head: "war", "stripe", ...) and strokes; each stroke `dict(path=[(x, y), ...],
    width=..., mirror=False, slot=...)` (x and width shares of the half width, y of the half height; `width` one
    value or one per path point) becomes convex `(slot, polygon)` pieces in front view: a band per segment and a
    round joint at every bend. `build` cuts them into the cranium, jaw and nose (`kit.paint_cut`), so paint is flat
    on the skin, never a raised bar, and keeps the one-flat-color-per-slot look without a texture.
    """
    A = head.A
    out = []
    if isinstance(strokes, str):
        strokes = [strokes]
    strokes = [s_ for item in strokes for s_ in (WARPAINT[item] if isinstance(item, str) else [item])]
    for stroke in strokes:
        P = np.asarray(stroke["path"], float) * [A, head.Hh] + [head.cx, head.cy]
        dense = kit.resample(P, 0.03) if len(P) > 1 else P
        width = np.atleast_1d(np.asarray(stroke.get("width", 0.08), float)) * A  # one value, or a profile along it
        w = np.interp(np.linspace(0, 1, len(dense)), np.linspace(0, 1, len(width)), width) / 2
        sl = stroke.get("slot", slot)
        polys = []
        for k in range(len(dense) - 1):
            a, b = dense[k], dense[k + 1]
            t = (b - a) / max(np.linalg.norm(b - a), 1e-9)
            nrm = np.array([-t[1], t[0]])
            polys.append([a + w[k] * nrm, b + w[k + 1] * nrm, b - w[k + 1] * nrm, a - w[k] * nrm])
        for k in range(1, len(dense) - 1):  # round joints, so bends show no notches
            th = 2 * np.pi * np.arange(10) / 10
            polys.append(dense[k] + w[k] * np.column_stack([np.cos(th), np.sin(th)]))
        for q in polys:
            q = np.asarray(q, float)
            out.append((sl, q))
            if stroke.get("mirror"):
                out.append((sl, (q * [-1, 1] + [2 * head.cx, 0])[::-1]))
    return out


FEATURES = {"earrings": None, "eyes": eyes, "brows": brows, "nose": nose, "mouth": mouth, "jaw": jaw, "block_jaw": block_jaw,
            "tusks": tusks, "horns": horns,
            "beard": beard,
            "ears": ears, "hair": hair, "wraps": wraps, "scar": scar, "paint": paint}
SKIN_FEATURES = ("nose", "jaw", "block_jaw")  # what face paint is projected onto, with the cranium


def _moved(fn, args):
    """A feature's arguments with its `move` = [dx, dy] (face shares: x of the half width, y of the half height)
    added to its own `x` / `y`: the easy way to nudge anything on a face. Paired features (eyes, brows, ears,
    horns) move apart for dx > 0."""
    import inspect

    args = dict(args)
    move = args.pop("move", None)
    if move:
        params = inspect.signature(fn).parameters
        for key, d in zip(("x", "y"), move):
            if d and key in params:
                args[key] = args.get(key, params[key].default) + d
    return args


def _front_points(piece, outlines=True):
    """A built feature's front-view footprint: its vertices' (x, y), and the outlines it cuts into the skin."""
    pts = [piece.verts[:, :2]] if len(piece.faces) else []
    if outlines:
        pts += [np.asarray(q, float) for _, q in getattr(piece, "paint", [])]
        pts += [np.asarray(c[2], float) for c in getattr(piece, "carve", [])]
    return np.vstack(pts) if pts else np.zeros((0, 2))


# Who must clear whom on a face (front view, top to bottom) and who gives way: (upper, lower, mover, direction).
# Brows may overlap the eyes (an angry look) and a hooked nose hangs in front of the mouth, so neither counts.
LAYOUT = (("eyes", "mouth", "mouth", -1), ("nose", "mouth", "mouth", -1))


def layout(head, recipe, margin=0.06, limit=0.35):
    """
    `recipe` with its face features spaced apart: for each pair in `LAYOUT` overlapping in the front view (column
    by column across their shared width), the mover goes down (or up) until there is `margin` (a share of the
    half height) between them, by at most `limit`. Returns `(recipe, notes)`; the notes say what moved, and what
    still collides past the limit (then move things by hand with `move`).
    """
    notes = []
    recipe = dict(recipe)

    def build_one(key):
        spec = recipe.get(key)
        if not isinstance(spec, dict) or (key == "nose" and spec.get("style") == "hooked"):
            return None
        piece = FEATURES[key](head, **_moved(FEATURES[key], spec))
        return _front_points(piece, outlines=key == "mouth") if piece is not None else None

    for upper, lower, mover, sign in LAYOUT:
        for _ in range(3):
            a, b = build_one(upper), build_one(lower)
            if a is None or b is None or not len(a) or not len(b):
                break
            bins = np.floor(np.r_[a[:, 0], b[:, 0]] / (0.06 * head.A)).astype(int)
            ba, bb = bins[:len(a)], bins[len(a):]
            gap = np.inf
            for k in set(ba) & set(bb):
                gap = min(gap, a[ba == k, 1].min() - b[bb == k, 1].max())
            need = margin * head.Hh - gap
            if not np.isfinite(gap) or need <= 1e-4:
                break
            shift = min(need / head.Hh, limit)
            spec = dict(recipe[mover])
            spec["y"] = spec.get("y", __import__("inspect").signature(FEATURES[mover]).parameters["y"].default) \
                + sign * shift
            recipe[mover] = spec
            notes.append(f"{mover} moved {'down' if sign < 0 else 'up'} {shift:.2f} to clear the {upper if mover == lower else lower}")
            if shift >= limit:
                notes.append(f"{mover} still touches the {upper if mover == lower else lower}: move them apart by hand")
                break
    return recipe, notes


def build(recipe, center=(0.0, 1.72, 0.0)):
    """
    `(head, piece)` for a recipe: `form` (the `Head` arguments) plus one entry per feature (`FEATURES`; a dict of
    the feature function's arguments, or a list of them for several; any feature takes `move` = [dx, dy] to nudge
    it). Features are spaced apart first (`layout`, its notes on `head.layout`; `"layout": false` turns it off), `skin` the cranium's slot, `lip` the lower lip
    on an underbite, a rounded band cut into the top of its step ([slot, share of the step's `edge` it reaches down,
    half width as a share of the head's]), `shade` lighter
    and darker bands of skin (`{"top": [slot, from yn], "bottom": [slot, below yn]}`). Under sculpted
    hair the scalp is painted in the hair's slot, so gaps between its locks show hair, not skin.
    """
    skin_slot = recipe.get("skin", "skin")
    sculpted = recipe.get("sculpt")
    if sculpted:  # a library head (an image-to-3D shape, colored by the kit); kit features go onto its surface
        from accessories import sculpts
        sc = sculpted if isinstance(sculpted, dict) else {"name": sculpted}
        cranium, head, meta = sculpts.head(sc["name"], top=center[1] + sc.get("top", 0.56), height=sc.get("height", 1.1))
    else:
        head = Head(center=center, **recipe.get("form", {}))
        cranium = head.mesh(skin_slot)
    recipe, head.layout = layout(head, recipe) if recipe.get("layout", True) else (recipe, [])
    parts, skin, painted, carved = [], [], [], []
    for key, fn in FEATURES.items():
        spec = recipe.get(key)
        for one in ([] if spec is None or spec is False else spec if isinstance(spec, list) else [spec]):
            args = _moved(fn, one) if fn is not None else dict(one)
            if sculpted and key == "eyes" and "x" not in args and np.isfinite(meta.get("eyes", np.nan)).all():
                e = meta["eyes"][1]  # the sculpt's own sockets (the left eye; eyes come in mirrored pairs)
                args.setdefault("x", (e[0] - head.cx) / head.A)
                args.setdefault("y", (e[1] - head.cy) / head.Hh)
            if key in ("paint", "scar"):  # cut into the skin once everything is built
                painted += paint(head, **args) if key == "paint" else paint(head, scar(head, **args))
                continue
            if key == "earrings":
                from accessories import sculpts
                parts.append(sculpts.earrings(cranium, head, **args))
                continue
            piece = fn(head, **args)
            if sculpted and key in ("eyes", "brows", "mouth"):
                piece = sculpts.onto(piece, head, cranium)
            elif sculpted and key in ("ears", "tusks", "nose", "horns"):  # kit parts set rigidly on the sculpt
                to_y = None
                if key == "tusks":  # rooted on the sculpt's lip, wherever its shape put it
                    to_y = lambda x: sculpts.lip_top(cranium, x, head.cy - 0.5 * head.Hh)  # noqa: E731
                piece = sculpts.attach(piece, head, cranium, axis="x" if key == "ears" else "z", to_y=to_y)
            painted += getattr(piece, "paint", [])  # outlines a feature asks to cut into the skin (a jaw's mouth)
            carved += getattr(piece, "carve", [])  # recesses it asks to carve into it (an open mouth)
            if key == "hair" and one.get("style") == "sculpt":
                from accessories import sculpts
                cranium = sculpts.paint_scalp(cranium, head, one["name"], piece, one.get("slot", "hair"))
            if key in SKIN_FEATURES:
                skin.append(piece)
            parts.append(piece)
    lip = recipe.get("lip")
    if lip and head.bite:  # the lower lip: a rounded band cut into the top of the underbite step (KayKit's darker lip)
        sl, share = lip[0], lip[1]
        half_width = lip[2] if len(lip) > 2 else 0.55
        top_y = head.y(head.mouth) + 0.004
        t = np.linspace(0, 2 * np.pi, 25)[:-1]

        # Softened outer transition band
        hw_mid = half_width * 1.15
        share_mid = share * 1.3
        mid_y_outer = head.y(head.mouth - 0.5 * share_mid * head.edge)
        oval_mid = np.column_stack([head.cx + hw_mid * head.A * np.cos(t),
                                    mid_y_outer + 1.15 * (top_y - mid_y_outer) * np.sin(t)])
        oval_mid = clip(oval_mid, 0.0, 1.0, top_y)

        # Core inner lip
        mid_y = head.y(head.mouth - 0.5 * share * head.edge)
        oval_core = np.column_stack([head.cx + half_width * head.A * np.cos(t),
                                     mid_y + 1.15 * (top_y - mid_y) * np.sin(t)])  # reaching `share` of the step down
        oval_core = clip(oval_core, 0.0, 1.0, top_y)  # flat along the mouth line, rounded below and at the corners

        sl_mid = f"{sl}_mid"
        bare = {skin_slot, "skin", "skin_dark", sl_mid}
        cranium = kit.paint_cut(cranium, [oval_mid], sl_mid, bare, min_facing=0.0)
        cranium = kit.paint_cut(cranium, [oval_core], sl, bare, min_facing=0.0)
    shade = recipe.get("shade")
    if shade:  # KayKit's gradient as flat bands: the crown lighter (`top` = [slot, from yn]), under the jaw darker
        yv = (cranium.verts[cranium.faces][:, :, 1] - head.cy) / head.Hh  # by whole faces, so edges follow a ring
        slots = cranium.slots.copy()
        bare = slots == skin_slot
        if "top" in shade:
            slots[bare & (yv > shade["top"][1]).all(1)] = shade["top"][0]
        if "bottom" in shade:
            slots[bare & (yv < shade["bottom"][1]).all(1)] = shade["bottom"][0]
        cranium = kit.Piece(cranium.verts, cranium.faces, slots)
    if carved:  # recesses: the skin cut along their outlines and sunk (an open mouth), never a plate laid on it
        bare = {skin_slot, "skin", "skin_dark", *(v[0] for v in (shade or {}).values())}
        for sl, strips, outline, depth in carved:
            cranium = kit.carve(cranium, strips, outline, sl, depth, head.centre, bare, min_facing=0.3)
    if painted:  # paint and scars: the skin itself recolored inside their outlines (cranium, nose, jaw)
        makeup_slots = {s_ for s_, _ in painted if any(k in s_ for k in ("ring", "makeup", "shadow"))}
        eye_slots = {s_ for s_, _ in painted if s_ in ("eye", "socket", "iris")}
        bare = {skin_slot, "skin", "skin_dark", *makeup_slots, *eye_slots, *(v[0] for v in (shade or {}).values())}
        on_top = ["eye", "socket", "iris", "liner", "mouth", "lip", "lip_mid", "scar", "paint", "nostril", "ring_inner", "makeup_inner", "ring", "makeup", "ring_outer", "makeup_outer", "halo"]
        for sl in sorted({s_ for s_, _ in painted}, key=lambda x: on_top.index(x) if x in on_top else 50.0):
            polys = [q for s_, q in painted if s_ == sl]
            cranium = kit.paint_cut(cranium, polys, sl, bare, min_facing=0.3)  # the face, not over the crown
            cut = {id(p): kit.paint_cut(p, polys, sl, bare) for p in skin}
            parts = [cut.get(id(p), p) for p in parts]
            skin = [cut[id(p)] for p in skin]
    return head, kit.Piece.merge(cranium, *parts)


FORMS = {
    "human": dict(width=0.94, height=1.0, depth=0.96),
    "round": dict(width=1.0, height=1.0, depth=1.0, p=2.2, q=2.2),
    "orc": dict(width=1.1, height=0.98, depth=1.0, p=2.7, q=2.8, jaw=0.16, chin=0.12, brow=0.1, crown=0.12),
    "long": dict(width=0.84, height=1.2, depth=0.9, p=2.3, q=2.4, crown=0.1, jaw=-0.12),
    "brute": dict(width=1.12, height=0.86, depth=0.96, p=2.9, q=3.0, jaw=0.22, brow=0.14, back=0.9),
}

ROSTER = {
    "necromancer": dict(
        form=dict(width=0.96, height=0.98, depth=0.92, p=2.3, q=2.4),
        eyes=dict(style="glow", angry=0.5, pupil=True, w=0.27, h=0.3, y=-0.2, x=0.43, liner=0.32, flick=0.3,
                  lashes=3),
        brows=dict(style="arc", angle=24, thickness=0.08, length=0.44, y=0.06, arch=0.1, taper=0.6, slot="hair"),
        nose=dict(style="button", size=0.7, y=-0.4),
        mouth=dict(curve=-0.25, width=0.22, y=-0.62),
        hair=dict(style="cap", line=((0, 0.36), (30, 0.22), (55, -0.45), (90, -0.8), (180, -0.95)), peak=0.16),
        colors=dict(skin=(196, 226, 204), hair=(30, 30, 34), iris=(70, 226, 96), eye=(24, 40, 30),
                    mouth=(70, 90, 76), liner=(26, 26, 30))),
    "orc_king": dict(  # after KayKit's orcs (inspiration only): one smooth head, the jaw an underbite step in it
        form={"width": 0.86, "height": 1.08, "depth": 1.0, "p": 2.7, "q": 3.0, "crown": 0.16, "jaw": 0.24, "brow": 0.0, "back": 0.92, "bite": 0.34, "mouth": -0.36, "edge": 0.3, "q_low": 4.2, "relief": [[0.36, 0.22, 0.34, 0.1, 0.13], [0.37, 0.0, 0.16, 0.12, -0.035], [0.56, -0.16, 0.2, 0.14, 0.08], [0.0, -0.22, 0.32, 0.12, 0.06]]},
        eyes={"style": "bead", "w": 0.14, "h": 0.15, "y": 0.02, "x": 0.37, "angry": 0.25, "glint": False},
        brows={"style": "bushy", "thickness": 0.27, "length": 0.56, "y": 0.32, "x": 0.42, "angle": 6, "depth": 0.05, "slot": "brow", "arch": 0.1},
        nose={"style": "chevron", "y": -0.1, "size": 0.95},
        tusks=[{"x": 0.5, "y": -0.39, "length": 0.22, "radius": 0.11, "spread": -0.03, "curve": 0.1, "out": 0.0, "tip": "tusk_tip", "split": 0.45}],
        ears={"style": "pointed", "angle": 8, "size": 0.7, "y": -0.06, "sweep": 35, "ring": "right", "rings": 2, "ring_size": 0.2, "notch": "left", "ring_slot": "metal", "base": 1.25},
        hair={"style": "tuft", "columns": 4, "height": 3, "size": 0.36},
        paint={"strokes": "war"},
        lip=["lip", 0.4, 0.56],
        shade={"top": ["skin_light", 0.5], "bottom": ["skin_dark", -0.72]},
        colors={"skin": [132, 196, 58], "skin_dark": [98, 152, 44], "brow": [96, 74, 50], "hair": [120, 94, 64], "eye": [18, 18, 20], "glint": [250, 250, 245], "tusk": [226, 196, 160], "tusk_tip": [244, 236, 222], "paint": [226, 70, 46], "nostril": [52, 84, 32], "lip": [104, 104, 52], "metal": [150, 156, 166], "skin_light": [146, 202, 62]}),
    "mummy": dict(
        form=dict(width=1.0, height=1.04, depth=1.0, p=2.2, q=2.2),
        skin="wrap_dark",
        wraps=dict(),
        eyes=dict(style="bead", glow=True, angry=0.5, w=0.14, h=0.14, y=-0.12, ring=0.75, ring_slot="dark"),
        brows=dict(angle=26, thickness=0.08, length=0.40, y=0.08, slot="dark", depth=0.05),
        colors=dict(wrap=(232, 214, 160), wrap_dark=(190, 164, 112), glow=(250, 200, 40), eye=(250, 200, 40),
                    dark=(44, 34, 28))),
    "wraith": dict(
        form=dict(width=0.98, height=0.96, depth=0.96, p=2.2, q=2.3),
        eyes=dict(style="bead", glow=True, angry=0.5, w=0.16, h=0.16, y=-0.16,
                  rings=[("dark", 0.55), ("halo", 1.2)]),
        brows=dict(style="arc", angle=22, thickness=0.11, length=0.46, y=0.08, arch=0.12, taper=0.5),
        nose=dict(style="button", size=0.65, y=-0.4),
        hair=dict(style="sculpt", name="hair_spiky"),
        colors=dict(skin=(66, 170, 142), brow=(34, 92, 82), glow=(245, 230, 105), eye=(245, 230, 105),
                    halo=(45, 125, 115), dark=(26, 70, 66), hair=(172, 228, 214))),
    "golem": dict(  # the orc's jaw without tusks, on a blocky brute
        form=dict(FORMS["brute"], p=3.2, q=3.2),
        eyes=dict(style="glow", angry=0.3, w=0.16, h=0.12, y=-0.12, glint=False, outline=0.25, halo=0.5),
        brows=dict(angle=10, thickness=0.16, length=0.6, y=0.18, depth=0.08, slot="skin_dark"),
        jaw=dict(top=-0.42, droop=0.1, chin=0.28, jowl=0.16, lip=0.08, mouth=None),
        colors=dict(skin=(150, 140, 128), skin_dark=(112, 104, 96), iris=(255, 190, 80), eye=(60, 50, 44),
                    halo=(230, 120, 40))),
    "goblin": dict(
        form=dict(width=1.0, height=1.02, depth=0.94, p=2.3, q=2.4, jaw=-0.3, chin=0.16, brow=0.14),
        eyes=dict(style="bead", angry=0.4, pupil=True, w=0.36, h=0.34, y=-0.12, x=0.42, outline=0.2),
        brows=dict(angle=24, thickness=0.12, length=0.56, y=0.16, depth=0.07, slot="skin_dark"),
        nose=dict(style="hooked", y=-0.05, size=1.15),
        mouth=dict(style="open", y=-0.6, width=0.72, curve=0.26, height=0.16,
                   upper=dict(count=2, shape="fang", length=0.85), lower=dict(count=4, shape="fang", length=0.5)),
        ears=dict(style="leaf", size=2.3, angle=9, sweep=30, y=-0.06, inner="ear", ring="left"),
        colors=dict(skin=(118, 150, 66), skin_dark=(88, 116, 48), ear=(184, 120, 98), ear_dark=(140, 84, 70),
                    iris=(250, 200, 40), eye=(40, 34, 20),
                    mouth=(60, 40, 30), tusk=(240, 232, 205))),
}

# Face paint examples (`paint` strokes) on plain heads, for the catalog.
WARPAINT = {
    # an orc's: two stripes down the forehead, a slash under each eye out to the cheek, a stripe down the chin
    "war": [dict(path=[(0.24, 0.94), (0.24, 0.42)], width=(0.13, 0.15), mirror=True),
            dict(path=[(0.42, -0.12), (0.72, -0.44)], width=(0.15, 0.07), mirror=True),
            dict(path=[(0.0, -0.6), (0.0, -0.92)], width=(0.18, 0.06))],
    "slashes": [dict(path=[(0.3, 0.5), (0.5, -0.3)], width=(0.07, 0.1, 0.05)),  # three claw marks over one eye
                dict(path=[(0.42, 0.5), (0.62, -0.3)], width=(0.07, 0.1, 0.05)),
                dict(path=[(0.54, 0.45), (0.72, -0.2)], width=(0.06, 0.09, 0.04))],
    "stripe": [dict(path=[(0.0, 0.75), (0.0, -0.8)], width=(0.16, 0.12))],
    "under eyes": [dict(path=[(0.18, -0.42), (0.45, -0.4), (0.68, -0.32)], width=(0.07, 0.1, 0.05), mirror=True)],
    "mask": [dict(path=[(-0.85, -0.12), (0.0, -0.2), (0.85, -0.12)], width=(0.3, 0.36, 0.3), slot="paint")],
}


def catalog():
    """The roster's heads, each form bare with KayKit-style features, then face paint examples."""
    for name, recipe in ROSTER.items():
        _, piece = build(recipe)
        yield name, piece, None, {**COLORS, **recipe.get("colors", {})}
    plain = dict(eyes=dict(), brows=dict(), nose=dict(style="wedge"), mouth=dict(curve=0.15), ears=dict(style="round"),
                 hair=dict(style="cap"))
    for name, form in FORMS.items():
        _, piece = build(dict(plain, form=form))
        yield f"form {name}", piece, None, COLORS
    for name, strokes in WARPAINT.items():
        _, piece = build(dict(plain, form=FORMS["human"], paint=dict(strokes=strokes)))
        yield f"paint {name}", piece, None, COLORS
