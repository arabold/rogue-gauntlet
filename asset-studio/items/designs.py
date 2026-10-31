"""Fresh catalog families built from shared surfaces and real solid unions.

Each recipe supplies a visible design, size, colors, and finish. This module does
not inspect old meshes or infer geometry from item names. Decorative colors live
on exterior faces; every structural addition must intersect its supporting solid.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, box, lathe, loop, polygon, tube

COLORS = {**kit.DEMO_COLORS, "wood": (112, 73, 46), "metal": (150, 167, 183),
          "glass": (61, 142, 157), "liquid": (177, 54, 73), "cork": (151, 106, 64),
          "paper": (217, 193, 141), "wax": (231, 204, 151), "food": (170, 94, 43),
          "leaf": (67, 110, 53), "bone": (230, 211, 173), "gem": (58, 173, 173),
          "cloth": (134, 59, 49), "dark": (48, 39, 37), "gold": (192, 144, 51),
          "crust": (105, 49, 34), "marrow": (157, 109, 76), "meat": (159, 55, 51), "fat": (227, 198, 150)}
MATERIALS = {"wood": {"metallic": 0, "roughness": .85, "crease": 50},
             "paper": {"metallic": 0, "roughness": 1, "crease": 40},
             "glass": {"metallic": .05, "roughness": .3, "crease": 55},
             "liquid": {"metallic": 0, "roughness": .65, "crease": 55}}


def sphere(center, radii):
    return mf.Manifold.sphere(1, 12).scale(radii).translate(center)


def piece(shape, slot):
    mesh = shape.to_mesh()
    return kit.Piece(mesh.vert_properties[:, :3], mesh.tri_verts, slot)


def profile_piece(profile, slot, sides=14):
    return piece(lathe(profile, sides), slot)


def potion(design):
    """Bottle, shoulders, neck, lip, and stopper share one exterior lathe topology."""
    size = design.get("size", "medium")
    radius = {"small": .13, "medium": .22, "large": .29}[size]
    h = {"small": .52, "medium": .72, "large": .83}[size]
    body = profile_piece([(radius * .65, 0), (radius, h * .08), (radius, h * .50),
                          (radius * .85, h * .65), (radius * .32, h * .76), (radius * .32, h * .86),
                          (radius * .41, h * .87), (radius * .41, h * .92),
                          (radius * .29, h * .93), (radius * .30, h)], "glass", 12)
    y = body.verts[body.faces].mean(1)[:, 1] / h
    body.slots = np.where(y > .92, "cork", np.where((y > .38) & (y < .5), "paper",
                          np.where(y < .62, "liquid", "glass"))).astype(object)
    return body


def weapon(design):
    kind = design.get("kind", "sword")
    long = kind in {"broadsword", "battleaxe", "warhammer", "staff"}
    h = 1.65 if long else 1.18
    assembly = Assembly()
    assembly.add(tube((0, -.28, 0), (0, .25 if kind in {"sword", "broadsword", "dagger"} else h * .80, 0), .047), "wood")
    assembly.add(sphere((0, -.27, 0), (.065, .065, .065)), "metal")
    if kind in {"sword", "broadsword", "dagger"}:
        length = .62 if kind == "dagger" else h
        width = .095 if kind == "dagger" else (.15 if long else .12)
        assembly.add(box((.47 if long else .34, .09, .10), (0, .20, 0), .025), "metal")
        # Diamond cross-section loft makes an actual blade ridge, not a painted slab.
        rings = [np.array([[-width, y, 0], [0, y, -.035], [width, y, 0], [0, y, .035]]) * [factor, 1, factor]
                 for y, factor in [(.19, 1), (length * .75, .87), (length, .015)]]
        v, f = kit._loft(rings)
        v, f = kit._cap_loops(v, f, [list(range(4)), list(range(8, 12))])
        assembly.add(kit.Piece(v, kit._orient_closed(v, f), "metal"), "metal")
    elif kind in {"axe", "battleaxe"}:
        for sign in ([1, -1] if kind == "battleaxe" else [1]):
            silhouette = np.array([[-.04, h * .62], [.33, h * .52], [.48, h * .69], [.40, h * .91], [-.04, h * .80]])
            silhouette[:, 0] *= sign
            assembly.add(polygon(silhouette, .085), "metal")
    elif kind in {"hammer", "warhammer"}:
        assembly.add(box((.65 if long else .47, .30, .25), (0, h * .76, 0), .045), "metal")
    elif kind in {"staff", "wand"}:
        assembly.add(profile_piece([(.07, h * .72), (.11, h * .77), (.09, h * .84)], "gold"), "gold")
        assembly.add(sphere((0, h * .85, 0), (.15, .22, .13)), "gem")
    elif kind == "crossbow":
        assembly.add(box((.17, .75, .16), (0, .34, 0)), "wood")
        for sign in [-1, 1]:
            assembly.add(tube((0, .59, 0), (sign * .44, .43, 0), .045), "metal")
        assembly.add(tube((-.44, .43, 0), (.44, .43, 0), .013, 6), "cloth")
    else:
        raise ValueError(f"Unknown weapon design {kind}")
    return assembly.build()


def shield(design):
    kind = design.get("kind", "heater")
    if kind == "round":
        angles = np.arange(16) * 2 * np.pi / 16
        points = np.column_stack([np.cos(angles) * .42, np.sin(angles) * .5])
    else:
        points = [(-.38, .43), (.38, .43), (.38, -.02), (.22, -.32), (0, -.55), (-.22, -.32), (-.38, -.02)]
        if kind == "kite":
            points = [(x, y * 1.22) for x, y in points]
    # An integral grip on the back, welded through two intersecting supports.
    assembly = Assembly().add(polygon(points, .11), "wood")
    assembly.add(sphere((0, 0, .048), (.17, .17, .09)), "metal")
    for x in [-.12, .12]:
        assembly.add(tube((x, -.14, -.045), (x, -.14, -.18), .04), "metal")
    assembly.add(tube((-.12, -.14, -.18), (.12, -.14, -.18), .045), "wood")
    result = assembly.build()
    centers = result.verts[result.faces].mean(1)
    front = centers[:, 2] > .05
    result.slots[front & (np.abs(centers[:, 0]) < .29) & (result.slots == "wood")] = "cloth"
    return result


def scroll(design):
    strokes = {
        "raidho": [((.28,.15),(.28,.85)), ((.28,.85),(.75,.66)), ((.75,.66),(.28,.48)), ((.28,.48),(.75,.15))],
        "sowilo": [((.70,.85),(.28,.62)), ((.28,.62),(.72,.38)), ((.72,.38),(.30,.15))],
        "othala": [((.50,.85),(.23,.57)), ((.23,.57),(.77,.15)), ((.77,.57),(.23,.15)), ((.50,.85),(.77,.57))],
        "fehu": [((.30,.15),(.30,.85)), ((.30,.64),(.75,.85)), ((.30,.43),(.75,.64))],
        "algiz": [((.50,.15),(.50,.85)), ((.50,.55),(.20,.85)), ((.50,.55),(.80,.85))],
        "hagalaz": [((.25,.15),(.25,.85)), ((.75,.15),(.75,.85)), ((.25,.70),(.75,.35))],
        "berkano": [((.28,.15),(.28,.85)), ((.28,.85),(.75,.67)), ((.75,.67),(.28,.50)), ((.28,.50),(.75,.32)), ((.75,.32),(.28,.15))],
        "tiwaz": [((.50,.15),(.50,.85)), ((.50,.85),(.20,.58)), ((.50,.85),(.80,.58))],
        "thurisaz": [((.30,.15),(.30,.85)), ((.30,.75),(.78,.50)), ((.78,.50),(.30,.25))],
        "dagaz": [((.22,.20),(.22,.80)), ((.78,.20),(.78,.80)), ((.22,.20),(.78,.80)), ((.22,.80),(.78,.20))],
        "ingwaz": [((.50,.85),(.20,.50)), ((.20,.50),(.50,.15)), ((.50,.15),(.80,.50)), ((.80,.50),(.50,.85))],
        "eihwaz": [((.50,.15),(.50,.85)), ((.50,.85),(.25,.65)), ((.50,.15),(.75,.35))],
    }
    lines = strokes.get(design.get("variant"), strokes["algiz"])
    nx, ny = 12, 16
    vertices = [(x, y, z) for z in [-.0125, .0125] for y in np.linspace(0, .58, ny + 1) for x in np.linspace(-.265, .265, nx + 1)]
    layer = (nx + 1) * (ny + 1)
    faces, slots = [], []
    for y in range(ny):
        for x in range(nx):
            a = y * (nx + 1) + x
            b, c, d = a + 1, a + nx + 2, a + nx + 1
            point = np.array([(x + .5) / nx, (y + .5) / ny])
            ink = False
            for start, end in lines:
                start, end = np.array(start), np.array(end)
                delta = end - start
                closest = start + np.clip((point - start) @ delta / (delta @ delta), 0, 1) * delta
                ink |= np.linalg.norm(point - closest) < .052
            faces.extend([(a, c, b), (a, d, c), (a + layer, b + layer, c + layer), (a + layer, c + layer, d + layer)])
            slots.extend(["paper", "paper", "gem" if ink else "paper", "gem" if ink else "paper"])
    perimeter = [*range(nx), *[y * (nx + 1) + nx for y in range(ny)],
                 *[ny * (nx + 1) + x for x in range(nx, 0, -1)], *[y * (nx + 1) for y in range(ny, 0, -1)]]
    for i, a in enumerate(perimeter):
        b = perimeter[(i + 1) % len(perimeter)]
        faces.extend([(a, b, b + layer), (a, b + layer, a + layer)])
        slots.extend(["paper", "paper"])
    panel = kit.Piece(vertices, faces, slots)
    assembly = Assembly().add(panel)
    for y in [.035, .545]:
        assembly.add(tube((-.30, y, 0), (.30, y, 0), .052, 12), "paper")
    return assembly.build()


def jewelry(design):
    kind = design.get("kind", "ring")
    if kind == "ring":
        assembly = Assembly().add(loop((0, .23, 0), (.18, .20), .052), "metal")
        if design.get("variant") not in {"band", "carved"}:
            assembly.add(sphere((0, .435, 0), (.10, .09, .08)), "gem")
        return assembly.build()
    shape = sphere((0, .25, 0), (.20, .23, .06))
    assembly = Assembly().add(shape, "metal")
    assembly.add(loop((0, .52, 0), (.055, .08), .027), "metal")
    assembly.add(sphere((0, .25, .055), (.12, .14, .045)), "gem")
    return assembly.build()


def key(design):
    assembly = Assembly().add(loop((0, .50, 0), (.16, .16), .055, 12), "metal")
    assembly.add(box((.065, .43, .065), (0, .18, 0), .009), "metal")
    for y in [.025, .13]:
        assembly.add(box((.20, .065, .065), (.07, y, 0), .008), "metal")
    if design.get("variant") in {"arcane", "royal", "bone"}:
        assembly.add(sphere((0, .63, 0), (.07, .065, .055)), "gem")
    return assembly.build()


def food(design):
    kind = design.get("kind", "apple")
    if kind == "roll" and design.get("sliced"):
        from items.bread import sliced_roll
        return sliced_roll(design.get("footprint_scale", 1), design.get("height_scale", 1))
    if kind == "tbone":
        # Partition a single steak volume into flush fat, meat, and T-shaped bone.
        angles = np.arange(20) * 2 * np.pi / 20
        outline = np.column_stack([np.cos(angles) * .30, np.sin(angles) * .22])
        outer, inner = polygon(outline, .10), polygon(outline * .86, .10)
        bone = polygon([(-.18, .10), (-.035, .10), (-.035, -.16), (.035, -.16),
                        (.035, .10), (.18, .10), (.18, .16), (-.18, .16)], .10) ^ inner
        assembled = Assembly().add(outer - inner, "fat").add(inner - bone, "meat").add(bone, "bone").build()
        assembled.verts = assembled.verts @ kit.rotation([1, 0, 0], -90).T + [0, .05, 0]
        return assembled
    if kind == "steak":
        from items.builders import steak
        v, f, cols = steak()
        palette, labels = np.unique(cols, axis=0, return_inverse=True)
        # Preserve the modeled flush regions as authored palette slots.
        names = ["crust", "marrow", "meat", "fat", "bone"]
        slots = np.array([names[i] for i in labels], dtype=object)
        return kit.Piece(v, f, slots)
    assembly = Assembly()
    if kind in {"apple", "pear"}:
        body = sphere((0, .23, 0), (.25, .23, .22))
        if kind == "pear":
            body = body + sphere((0, .41, 0), (.13, .20, .13))
        assembly.add(body, "food")
        assembly.add(tube((0, .39 if kind == "apple" else .54, 0), (.035, .58 if kind == "apple" else .70, 0), .023, 6), "wood")
    elif kind in {"bread", "roll"}:
        assembly.add(sphere((0, .14, 0), (.37 if kind == "bread" else .24, .14, .17)), "food")
    elif kind == "sausage":
        path = [(-.30, .10, 0), (-.16, .17, 0), (.16, .17, 0), (.30, .10, 0)]
        assembly.add(kit.sweep(path, kit.ring_profile(.075, .075, 10), "food"), "food")
    elif kind == "cheese":
        assembly.add(polygon([(-.25, 0), (.25, 0), (.15, .32), (-.16, .32)], .30), "food")
    elif kind == "grapes":
        # Every grape intersects the bunch; union eliminates hidden spherical faces.
        for y, xs in [(.13, [0]), (.29, [-.095, .095]), (.45, [-.16, 0, .16])]:
            for x in xs:
                assembly.add(sphere((x, y, 0), (.115, .115, .12)), "food")
        assembly.add(tube((0, .48, 0), (.05, .66, 0), .025, 6), "wood")
    elif kind == "drumstick":
        assembly.add(sphere((0, .22, 0), (.22, .25, .19)), "food")
        assembly.add(tube((0, .35, 0), (0, .66, 0), .048), "bone")
        for x in [-.042, .042]:
            assembly.add(sphere((x, .65, 0), (.064, .06, .06)), "bone")
    elif kind in {"beer", "water", "wine"}:
        if kind == "beer":
            body = lathe([(.16, 0), (.19, .035), (.19, .44), (.16, .47)])
            cavity = mf.Manifold.cylinder(.5, .13, circular_segments=14).rotate((-90, 0, 0)).translate((0, .07, 0))
            assembly.add(body - cavity, "wood")
            assembly.add(loop((.23, .26, 0), (.12, .17), .045), "metal")
        elif kind == "water":
            assembly.add(sphere((0, .25, 0), (.24, .27, .10)), "metal")
            assembly.add(tube((0, .44, 0), (0, .59, 0), .05), "cork")
        else:
            result = profile_piece([(.12, 0), (.16, .04), (.16, .43), (.13, .49), (.045, .55),
                                    (.045, .72), (.058, .73), (.058, .76), (.04, .78)], "glass", 12)
            y = result.verts[result.faces].mean(1)[:, 1]
            result.slots[y > .75] = "cork"
            return result
    else:
        raise ValueError(f"Unknown food design {kind}")
    return assembly.build()


def lamp(design):
    kind = design.get("kind", "lantern")
    assembly = Assembly()
    if kind == "lantern":
        # Union handles into the actual shell, then restore pane colors on the exterior.
        assembly.add(lathe([(.15, 0), (.21, .045), (.21, .10), (.17, .14), (.17, .46),
                            (.23, .48), (.13, .60), (.07, .63)], 8), "metal")
        assembly.add(loop((0, .72, 0), (.10, .14), .028), "metal")
        result = assembly.build()
        c = result.verts[result.faces].mean(1)
        result.slots[(c[:, 1] > .145) & (c[:, 1] < .455)] = "glass"
        return result
    if kind in {"candle", "torch"}:
        profile = [(.07, 0), (.07, .37), (.11 if kind == "torch" else .07, .39), (.10 if kind == "torch" else .065, .53)]
        result = profile_piece(profile, "wax" if kind == "candle" else "wood")
        c = result.verts[result.faces].mean(1)
        result.slots[c[:, 1] > .40] = "dark" if kind == "torch" else "wax"
        return result
    if kind == "oil":
        assembly.add(sphere((0, .13, 0), (.25, .13, .18)), "metal")
        assembly.add(tube((.15, .14, 0), (.36, .23, 0), .055), "metal")
        assembly.add(loop((-.25, .21, 0), (.115, .12), .035), "metal")
    elif kind == "candelabrum":
        assembly.add(lathe([(.19, 0), (.19, .04), (.07, .10), (.045, .47)]), "metal")
        for x in [-.22, 0, .22]:
            assembly.add(tube((0, .32, 0), (x, .45, 0), .033), "metal")
            assembly.add(tube((x, .43, 0), (x, .68, 0), .045), "wax")
    else:
        raise ValueError(f"Unknown lamp design {kind}")
    return assembly.build()


def tool(design):
    kind = design.get("kind", "pickaxe")
    assembly = Assembly()
    if kind == "pickaxe":
        assembly.add(tube((0, -.20, 0), (0, .70, 0), .045), "wood")
        for sign in [-1, 1]:
            assembly.add(polygon([(0, .64), (sign * .14, .74), (sign * .45, .54), (sign * .31, .72), (0, .81)], .08), "metal")
    elif kind == "grappling_hook":
        assembly.add(tube((0, 0, 0), (0, .65, 0), .045), "metal")
        for sign in [-1, 1]:
            assembly.add(tube((0, .08, 0), (sign * .23, .23, 0), .045), "metal")
            assembly.add(tube((sign * .23, .23, 0), (sign * .23, .43, 0), .045), "metal")
        assembly.add(loop((0, .71, 0), (.08, .1), .035), "metal")
    elif kind == "bell":
        body = lathe([(.23, 0), (.24, .035), (.17, .16), (.08, .34), (.05, .37)])
        cavity = lathe([(.20, -.01), (.12, .15), (.03, .27)])
        assembly.add(body - cavity, "metal")
        assembly.add(loop((0, .42, 0), (.055, .08), .026), "metal")
    elif kind == "mirror":
        assembly.add(tube((0, -.20, 0), (0, .28, 0), .045), "wood")
        assembly.add(sphere((0, .46, 0), (.22, .27, .045)), "metal")
    elif kind == "horn":
        path = [(0, 0, 0), (0, .16, 0), (.08, .34, 0), (.25, .48, 0), (.41, .50, 0)]
        assembly.add(kit.sweep(path, kit.ring_profile(.1, .1, 10), "metal", scale=[.35, .45, .65, .9, 1.3]), "metal")
    elif kind == "lockpicks":
        assembly.add(loop((0, .05, 0), (.12, .12), .025), "metal")
        for x in [-.075, 0, .075]:
            assembly.add(tube((x, .12, 0), (x, .47, 0), .018, 6), "metal")
            assembly.add(tube((x, .47, 0), (x + .05, .47, 0), .018, 6), "metal")
    else:
        raise ValueError(f"Unknown tool design {kind}")
    return assembly.build()


from items.arsenal import arsenal
from items.shieldcraft import shieldcraft
from items.furnishings import furnishing
from items.fountains import fountain


FAMILIES = {"shieldcraft": shieldcraft, "arsenal": arsenal, "furnishing": furnishing, "potion": potion, "weapon": weapon,
            "shield": shield, "scroll": scroll, "jewelry": jewelry, "key": key, "food": food, "lamp": lamp, "tool": tool,
            "fountain": fountain}
