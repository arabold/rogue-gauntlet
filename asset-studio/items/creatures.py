"""Rigless monster bodies built from solids, for creatures the game animates by transform and shader instead of a
skeleton. The gelatinous cube is two models: its jelly shell, which the game colors with a translucent shader, and the
bones, coins and blade it has swallowed, which show through it."""
import numpy as np
import manifold3d as mf

from items.furnishing_kit import ball, block, random, rod
from items.furnishings import bone, skull
from items.solid import Assembly


def gelatinous_cube_shell(width=2.2, height=2.3, seed="gelatinous_cube_shell"):
    """The cube's jelly: a chunky beveled block seated in a foot of ooze that spreads past its walls in soft lobes,
    with a few low swells on its top so it reads as wobbling jelly rather than cut glass. Its one slot is a
    placeholder: the game draws the shell with its own jelly shader."""
    rng = random(seed)
    a = Assembly()
    a.add(block((width, height * .94, width), (0, height * .47, 0), width * .1), "slime")
    for _ in range(4):
        x, z = rng.uniform(-.28, .28) * width, rng.uniform(-.28, .28) * width
        r = rng.uniform(.22, .32) * width
        a.add(ball((x, height * .93, z), (r, r * .32, r), 10), "slime")
    # The ooze foot: overlapping flattened lobes melted into one sheet around the base.
    lobes = [(0.0, 0.0, width * .62, width * .62, .2)]
    for i in range(7):
        t = i / 7 * 2 * np.pi + rng.uniform(-.2, .2)
        reach = rng.uniform(.5, .62) * width
        lobes.append((np.cos(t) * reach, np.sin(t) * reach, rng.uniform(.16, .24) * width,
                      rng.uniform(.16, .24) * width, rng.uniform(.12, .18)))

    def field(x, y, z):
        total = sum(np.exp(-6 * (((x - cx) / rx) ** 2 + (y / h) ** 2 + ((z - cz) / rz) ** 2))
                    for cx, cz, rx, rz, h in lobes)
        return float(np.log(max(total, 1e-12)) / 6 + 1)
    span = width * .95
    foot = mf.Manifold.level_set(field, [-span, -.05, -span, span, .3, span], .08)
    a.add(foot.trim_by_plane((0, 1, 0), 0.0).simplify(.012), "slime")
    return a.build()


def gelatinous_cube_contents(width=2.2, height=2.3, seed="gelatinous_cube_contents"):
    """What the cube has swallowed, sized to float inside its shell: a big skull facing forward high in the jelly, a
    short sword tilted through the middle, two femurs and a scatter of gold coins low down, and a few air bubbles,
    every piece clear of the walls."""
    rng = random(seed)
    a = Assembly()
    skull(a, -width * .08, width * .08, 8, y=height * .5, tilt=-6, size=1.7)
    bone(a, (-width * .3, height * .2, width * .12), (width * .06, height * .13, width * .22), .085)
    bone(a, (width * .05, height * .22, -width * .2), (width * .32, height * .33, -width * .04), .085)
    # The sword, built upright along +Y from its pommel, then tilted through the jelly.
    sword = Assembly()
    sword.slots = a.slots
    sword.add(block((.22, .74, .06), (0, .64, 0), .02), "steel")
    sword.add(rod((0, 1.0, 0), (0, 1.16, 0), .11, 4).scale((1, 1, .4)), "steel")
    sword.add(block((.52, .1, .12), (0, .24, 0), .03), "copper")
    sword.add(rod((0, .03, 0), (0, .22, 0), .055, 6), "leather")
    sword.add(ball((0, .02, 0), (.08, .08, .08), 6), "copper")
    blade = sword.build_solid().rotate((0, 0, 38)).rotate((12, -20, 0)).translate((width * .22, height * .16, width * .06))
    a.join(blade)
    for _ in range(5):
        x, z = rng.uniform(-.3, .2) * width, rng.uniform(-.25, .25) * width
        coin = mf.Manifold.cylinder(.04, .12, .12, 8, True)
        a.add(coin.rotate(tuple(rng.uniform(-70, 70, 3))).translate((x, height * rng.uniform(.1, .3), z)), "gold")
    for _ in range(5):
        r = rng.uniform(.05, .11)
        x, z = rng.uniform(-.32, .32) * width, rng.uniform(-.3, .3) * width
        a.add(ball((x, height * rng.uniform(.35, .78), z), (r, r, r), 6), "ore_crystal")
    return a.build(loose=True)


KINDS = {"gelatinous_cube_shell": gelatinous_cube_shell, "gelatinous_cube_contents": gelatinous_cube_contents}


def creature(design):
    """Build one explicit creature body; unknown kinds fail instead of falling back. Other design keys are arguments."""
    kind = design.get("kind")
    if kind not in KINDS:
        raise ValueError(f"Unknown creature design {kind}")
    return KINDS[kind](**{key: value for key, value in design.items() if key != "kind"})
