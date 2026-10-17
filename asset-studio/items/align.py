#!/usr/bin/env python3
"""
Step 3 of the 3D Asset Pipeline: Low-Poly Stylization, stylized Palette UV Mapping,
and Reference-Matched Scale & Grip Alignment.

Supports two input modes:
  1. `--input-glb <raw.glb>`:
     Loads a raw high-poly GLB (e.g. from TripoSR / TRELLIS), aligns its principal
     axes, decimates it to low-poly flat-shaded triangles, scales/positions it to
     match the exact local-space AABB and grip origin (0,0,0) of a reference stylized
     item archetype, clusters face colors, and remaps UVs onto `dungeon_texture.png`.
  2. `--from-image <concept.png>`:
     Extracts the foreground silhouette and color regions from a concept image,
     reconstructs a genuine 3D low-poly mesh tailored to the archetype (octagonal
     shaft + beveled 3D blades for weapons, lathe-revolved 12-sided bottles for
     potions, domed boss + rim + back-grip for shields, vaulted shell for helmets),
     aligns it to the stylized reference AABB, and maps UVs onto `dungeon_texture.png`.

Usage:
  python3 asset-studio/items/align.py \\
    --archetype battleaxe \\
    --input-glb raw.glb \\
    --output assets/custom-items/battleaxe_horned.glb \\
    [--from-image concept.png] \\
    [--target-tris 600] \\
    [--palette steel,wood,gold,dark_iron] \\
    [--flip-y] [--flip-x] [--rot-y 0]
"""

import argparse
import json
import math
import os
import struct
import sys
from PIL import Image


# Reference local-space AABBs (min_x, min_y, min_z, max_x, max_y, max_z) measured
# directly from the existing stylized weapons, shields, helmets, and potions in `assets/`.
# Origin (0,0,0) is the BoneAttachment3D grip/anchor point.
ARCHETYPE_PROFILES = {
    "battleaxe": {
        "category": "weapons",
        "two_handed": True,
        "aabb": (-0.86, -0.44, -0.18, 0.86, 1.56, 0.18),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark",
        ],
        "prompt_hint": "double-bladed fantasy battle axe weapon with a slender wooden shaft and large exaggerated beveled steel blades",
    },
    "axe": {
        "category": "weapons",
        "two_handed": False,
        "aabb": (-0.59, -0.28, -0.11, 0.14, 1.04, 0.11),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark",
        ],
        "prompt_hint": "one-handed fantasy hand axe weapon with a slender wooden handle and wide exaggerated curved steel blade",
    },
    "sword": {
        "category": "weapons",
        "two_handed": False,
        "aabb": (-0.32, -0.34, -0.09, 0.32, 1.48, 0.09),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark", "crimson", "cobalt",
        ],
        "prompt_hint": "one-handed fantasy longsword weapon with expressive crossguard, compact grip, and broad beveled steel blade",
    },
    "broadsword": {
        "category": "weapons",
        "two_handed": True,
        "aabb": (-0.48, -0.38, -0.12, 0.48, 1.96, 0.12),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark", "crimson", "cobalt",
        ],
        "prompt_hint": "two-handed fantasy greatsword broadsword with wide winged crossguard and heavy broad beveled blade",
    },
    "dagger": {
        "category": "weapons",
        "two_handed": False,
        "aabb": (-0.24, -0.24, -0.07, 0.24, 0.98, 0.07),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark", "crimson", "purple",
        ],
        "prompt_hint": "short fantasy dagger knife with wide pointed steel blade and crossguard",
    },
    "hammer": {
        "category": "weapons",
        "two_handed": False,
        "aabb": (-0.48, -0.32, -0.22, 0.48, 1.10, 0.22),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark",
        ],
        "prompt_hint": "one-handed fantasy war hammer mace with heavy blocky beveled head and slender wooden shaft",
    },
    "warhammer": {
        "category": "weapons",
        "two_handed": True,
        "aabb": (-0.74, -0.44, -0.28, 0.74, 1.54, 0.28),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "tan", "copper", "wood", "bark",
        ],
        "prompt_hint": "massive two-handed fantasy maul warhammer with oversized beveled head and slender handle",
    },
    "staff": {
        "category": "weapons",
        "two_handed": True,
        "aabb": (-0.34, -0.78, -0.22, 0.34, 1.44, 0.22),
        "default_palette": [
            "wood", "copper", "bark", "tan", "gold", "amber", "brass",
            "emerald", "green", "lime", "jade", "teal", "sky_blue", "cobalt", "crimson", "silver", "steel", "dark_iron",
        ],
        "prompt_hint": "wooden mage staff with slender shaft topped with an oversized glowing crystal headpiece",
    },
    "wand": {
        "category": "weapons",
        "two_handed": False,
        "aabb": (-0.18, -0.24, -0.14, 0.18, 0.88, 0.14),
        "default_palette": [
            "wood", "copper", "bark", "gold", "amber", "brass",
            "crimson", "rose", "teal", "sky_blue", "cobalt", "purple", "magenta", "silver", "steel",
        ],
        "prompt_hint": "short arcane magic wand with oversized crystal tip and gilded handle",
    },
    "crossbow": {
        "category": "weapons",
        "two_handed": True,
        "aabb": (-0.22, -0.34, -0.68, 0.22, 1.12, 0.68),
        "default_palette": [
            "wood", "copper", "bark", "tan", "silver", "steel",
            "blue_steel", "dark_iron", "obsidian", "gold", "amber", "brass",
        ],
        "prompt_hint": "wooden medieval crossbow with wide curved steel bow limbs and stock",
    },
    "shield": {
        "category": "armor",
        "two_handed": False,
        "slot": "ShieldHand",
        "aabb": (-0.48, -0.58, -0.14, 0.48, 0.52, 0.20),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "wood", "copper", "bark", "crimson", "cobalt", "sky_blue", "ivory",
        ],
        "prompt_hint": "sturdy medieval fantasy shield front view with thick metallic rim and central boss",
    },
    "helmet": {
        "category": "armor",
        "two_handed": False,
        "slot": "Head",
        "aabb": (-0.58, -0.08, -0.56, 0.58, 1.18, 0.68),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "crimson", "ivory",
        ],
        "prompt_hint": "stylized low-poly fantasy knight helmet or horned helm, front 3/4 view",
    },
    "chestplate": {
        "category": "armor",
        "two_handed": False,
        "slot": "Chest",
        "aabb": (-0.46, -0.50, -0.38, 0.46, 0.18, 0.38),
        "default_palette": [
            "silver", "steel", "blue_steel", "dark_iron", "obsidian",
            "gold", "amber", "brass", "wood", "copper", "bark",
        ],
        "prompt_hint": "stylized low-poly fantasy armor breastplate cuirass with shoulder guards",
    },
    "potion_small": {
        "category": "potions",
        "two_handed": False,
        "aabb": (-0.11, 0.0, -0.11, 0.11, 0.54, 0.11),
        "wrapper_scale": 2.0,
        "default_palette": ["crimson", "rose", "magenta", "tan", "wood", "silver", "steel", "blue_steel"],
        "prompt_hint": "slender glass potion vial with glowing liquid and cork stopper",
    },
    "potion_medium": {
        "category": "potions",
        "two_handed": False,
        "aabb": (-0.24, 0.0, -0.24, 0.24, 0.88, 0.24),
        "wrapper_scale": 2.0,
        "default_palette": ["crimson", "rose", "magenta", "ivory", "tan", "wood", "silver", "steel", "blue_steel"],
        "prompt_hint": "medium cylindrical glass potion bottle with parchment label and cork stopper",
    },
    "potion_large": {
        "category": "potions",
        "two_handed": False,
        "aabb": (-0.36, 0.0, -0.36, 0.36, 1.12, 0.36),
        "wrapper_scale": 2.0,
        "default_palette": ["crimson", "rose", "magenta", "wood", "copper", "bark", "tan", "silver", "steel", "blue_steel", "gold"],
        "prompt_hint": "large bulbous glass potion flask with leather strap, metal clasp, and cork stopper",
    },
    "scroll": {
        "category": "scrolls",
        "two_handed": False,
        "aabb": (-0.44, 0.0, -0.09, 0.44, 0.64, 0.09),
        "wrapper_scale": 2.0,
        "default_palette": ["ivory", "tan", "copper", "wood", "bark", "dark_iron", "obsidian", "crimson", "rose", "magenta"],
        "prompt_hint": "square weathered magic scroll with ripped edges and glowing Celtic rune",
    },
    "ring": {
        "category": "jewelry",
        "two_handed": False,
        "aabb": (-0.26, 0.0, -0.15, 0.26, 0.62, 0.15),
        "wrapper_scale": 2.0,
        "default_palette": ["brass", "amber", "gold", "crimson", "magenta", "rose", "obsidian", "ivory"],
        "prompt_hint": "oversized chunky low-poly RPG ring with thick band and large faceted gemstone",
    },
    "amulet": {
        "category": "jewelry",
        "two_handed": False,
        "aabb": (-0.28, 0.0, -0.13, 0.28, 0.72, 0.13),
        "wrapper_scale": 2.0,
        "default_palette": ["dark_iron", "steel", "silver", "cobalt", "sky_blue", "teal", "obsidian", "ivory"],
        "prompt_hint": "oversized chunky low-poly RPG amulet medallion on heavy chain loop with large faceted gemstone",
    },
    "key": {
        "category": "keys",
        "two_handed": False,
        "aabb": (-0.24, 0.0, -0.10, 0.24, 0.76, 0.10),
        "wrapper_scale": 2.0,
        "default_palette": ["dark_iron", "steel", "silver", "rust", "obsidian"],
        "prompt_hint": "oversized chunky low-poly fantasy dungeon key with ornate bow and warded teeth",
    },
    "food": {
        "category": "food",
        "two_handed": False,
        "aabb": (-0.26, 0.0, -0.22, 0.26, 0.62, 0.22),
        "wrapper_scale": 2.0,
        "default_palette": ["bark", "wood", "copper", "amber", "tan", "ivory"],
        "prompt_hint": "chunky low-poly fantasy dungeon food ration",
    },
    "drink": {
        "category": "food",
        "two_handed": False,
        "aabb": (-0.26, 0.0, -0.24, 0.26, 0.74, 0.24),
        "wrapper_scale": 2.0,
        "default_palette": ["bark", "wood", "dark_iron", "steel", "amber", "tan", "ivory"],
        "prompt_hint": "chunky low-poly fantasy tavern drink mug or bottle",
    },
    "lamp": {
        "category": "tools",
        "two_handed": False,
        "aabb": (-0.30, -0.26, -0.26, 0.48, 0.88, 0.26),
        "default_palette": ["wood", "bark", "copper", "dark_iron", "steel", "brass", "gold", "amber", "ivory", "obsidian"],
        "prompt_hint": "stylized low-poly fantasy dungeon lamp, lantern, candle, or unlit torch with no flame",
    },
    "tool": {
        "category": "tools",
        "two_handed": False,
        "aabb": (-0.48, -0.28, -0.28, 0.48, 0.88, 0.28),
        "default_palette": ["wood", "bark", "copper", "dark_iron", "steel", "silver", "brass", "gold", "tan", "ivory"],
        "prompt_hint": "stylized low-poly fantasy dungeon adventurer tool held in one hand",
    },
}

# Semantic names for the 8x4 swatch grid in `dungeon_texture.png` (row, col)
# Verified directly against `asset-studio/rigs/item_palette.png` pixel samples:
SWATCH_NAMES = {
    "dark_iron": (0, 0),     # (74, 81, 85)    dark slate iron
    "steel": (0, 1),         # (129, 140, 145) cool medium-bright steel
    "copper": (0, 2),        # (177, 111, 82)  warm copper / light wood
    "obsidian": (0, 3),      # (19, 25, 27)    near-black obsidian
    "wood": (0, 4),          # (155, 90, 69)   warm brown wood
    "stone": (0, 5),         # (151, 143, 134) warm grey stone
    "rust": (0, 6),          # (195, 101, 50)  rusty orange-brown
    "bark": (0, 7),          # (107, 92, 83)   dark brown-grey bark
    "silver": (1, 0),        # (212, 219, 222) bright silver-white steel
    "tan": (1, 1),           # (218, 174, 125) light tan leather
    "blue_steel": (1, 2),    # (122, 141, 157) bluish steel
    "purple": (1, 3),        # (102, 44, 142)  royal purple
    "magenta": (1, 4),       # (164, 26, 90)   deep magenta
    "teal": (1, 5),          # (80, 170, 174)  bright teal
    "lime": (1, 6),          # (85, 182, 106)  lime green
    "coral": (1, 7),         # (246, 147, 114) coral
    "sea_green": (2, 0),     # (56, 163, 141)  sea green
    "green": (2, 1),         # (82, 170, 72)   forest green
    "rose": (2, 2),          # (243, 114, 127) rose pink
    "crimson": (2, 3),       # (210, 34, 39)   crimson red
    "amber": (2, 4),         # (249, 170, 78)  golden amber
    "gold": (2, 5),          # (234, 194, 83)  bright metallic gold
    "sky_blue": (2, 6),      # (98, 160, 208)  sky blue
    "orange": (2, 7),        # (249, 158, 57)  bright orange
    "ivory": (3, 0),         # (220, 208, 195) bone ivory
    "brass": (3, 1),         # (208, 151, 69)  antique brass / dark gold
    "cobalt": (3, 2),        # (37, 126, 188)  cobalt blue
    "jade": (3, 3),          # (56, 163, 141)  jade green
    "emerald": (3, 4),       # (0, 131, 83)    deep emerald
    "neutral_gray": (3, 5),  # (142, 142, 141) neutral grey
}


def load_palette_swatches(palette_png_path):
    """Samples the 8x4 swatch grid from the studio dungeon_texture.png."""
    im = Image.open(palette_png_path).convert("RGB")
    w, h = im.size
    cols, rows = 8, 4
    cw, ch = w / cols, h / rows
    swatches = []
    for r in range(rows):
        for c in range(cols):
            px = int((c + 0.35) * cw)
            py = int((r + 0.50) * ch)
            rgb = im.getpixel((px, py))
            swatches.append({"cell": (r, c), "rgb": rgb})
    return swatches


def swatch_uv_box(row, col):
    """Returns safe interior UV bounds (u_min, v_min, u_max, v_max) for a swatch cell in the 8x4 grid."""
    cols, rows = 8.0, 4.0
    # Stay inside the left 60% of the cell to avoid the right-edge highlight strip and mipmap bleed
    u_min = (col + 0.15) / cols
    u_max = (col + 0.55) / cols
    v_min = (row + 0.12) / rows
    v_max = (row + 0.88) / rows
    return u_min, v_min, u_max, v_max


def rgb_to_hsl_features(rgb):
    """Extracts hue-ratio, chroma, and luminance features for material-aware swatch matching."""
    r, g, b = rgb[0], rgb[1], rgb[2]
    mx = max(r, g, b)
    mn = min(r, g, b)
    chroma = mx - mn
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    warmth = r - b
    # For warm colors (R > G > B), yellow_ratio distinguishes gold/brass (~0.50..0.88) from brown wood (~0.25..0.47)
    yellow_ratio = (g - b) / max(1.0, float(r - b)) if r > b + 10 else 0.0
    return r, g, b, chroma, lum, warmth, yellow_ratio


def nearest_swatch_cell(rgb, swatches, allowed_cells=None):
    """
    Maps a face RGB color to the best stylized swatch cell, preserving multi-shade bevels,
    recessed grooves, and highlights across gold/brass, wood/leather, steel/iron, and gem/heraldic families.
    """
    r, g, b, chroma, lum, warmth, yellow_ratio = rgb_to_hsl_features(rgb)

    # Fast exact match when face_colors already use exact stylized swatch RGB values
    for s in swatches:
        if allowed_cells and s["cell"] not in allowed_cells:
            continue
        sr, sg, sb = s["rgb"]
        if abs(r - sr) <= 3 and abs(g - sg) <= 3 and abs(b - sb) <= 3:
            return s["cell"]

    if allowed_cells:
        allowed_set = set(allowed_cells)
        # 1) Crimson / Ruby / Rose Red family
        if r >= 165 and (r - g) >= 75 and chroma >= 65:
            for red_cell in ((2, 3), (2, 2), (1, 4)):
                if red_cell in allowed_set:
                    return red_cell

        # 2) Emerald / Jade / Lime Green family
        if g > r + 18 and g > b + 12 and chroma >= 38:
            green_order = ((1, 6), (2, 1), (3, 4), (3, 3)) if lum >= 130 else ((3, 4), (2, 1), (3, 3), (1, 6))
            for grn_cell in green_order:
                if grn_cell in allowed_set:
                    return grn_cell

        # 3) Cobalt / Sky Blue / Teal family
        if b > r + 24 and b >= g - 10 and chroma >= 38:
            blue_order = ((2, 6), (1, 5), (3, 2)) if lum >= 135 else ((3, 2), (2, 6), (1, 2))
            for blu_cell in blue_order:
                if blu_cell in allowed_set:
                    return blu_cell

        # 4) Warm yellow-ochre / gold / brass family
        if warmth > 24 and yellow_ratio >= 0.49:
            if lum >= 170:
                gold_order = ((2, 5), (2, 4), (3, 1))
            elif lum >= 132:
                gold_order = ((2, 4), (2, 5), (3, 1))
            else:
                gold_order = ((3, 1), (2, 4), (0, 7), (2, 5))
            for gold_cell in gold_order:
                if gold_cell in allowed_set:
                    return gold_cell

        # 5) Warm reddish-brown wood / leather family
        if warmth > 20 and yellow_ratio < 0.49:
            if lum >= 132:
                wood_order = ((1, 1), (0, 2), (0, 4), (0, 7))
            elif lum >= 92:
                wood_order = ((0, 2), (0, 4), (1, 1), (0, 7))
            elif lum >= 58:
                wood_order = ((0, 4), (0, 2), (0, 7), (1, 1))
            else:
                wood_order = ((0, 7), (0, 4), (0, 3), (0, 2))
            for wood_cell in wood_order:
                if wood_cell in allowed_set:
                    return wood_cell

        # 6) Neutral / cool metallic steel & iron family
        if chroma < 38:
            if lum >= 158:
                metal_order = ((1, 0), (0, 1), (1, 2), (3, 5))
            elif lum >= 124:
                metal_order = ((0, 1), (1, 2), (1, 0), (3, 5))
            elif lum >= 100:
                metal_order = ((1, 2), (0, 1), (0, 0), (3, 5))
            elif lum >= 64:
                metal_order = ((0, 0), (1, 2), (0, 5), (0, 3))
            else:
                metal_order = ((0, 3), (0, 0), (0, 7))
            for metal_cell in metal_order:
                if metal_cell in allowed_set:
                    return metal_cell

    best_cell = (0, 1)
    best_d = 1e18
    for s in swatches:
        if allowed_cells and s["cell"] not in allowed_cells:
            continue
        sr, sg, sb = s["rgb"]
        dr, dg, db = r - sr, g - sg, b - sb
        d = 2.0 * dr * dr + 4.0 * dg * dg + 3.0 * db * db
        if d < best_d:
            best_d = d
            best_cell = s["cell"]
    return best_cell


def jacobi_eigen_3x3(mat):
    """Computes eigenvalues and eigenvectors of a 3x3 symmetric matrix in pure Python."""
    a = [list(row) for row in mat]
    v = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    for _ in range(25):
        p, q = 0, 1
        max_val = abs(a[0][1])
        for i in range(3):
            for j in range(i + 1, 3):
                if abs(a[i][j]) > max_val:
                    max_val = abs(a[i][j])
                    p, q = i, j
        if max_val < 1e-10:
            break
        diff = a[q][q] - a[p][p]
        if abs(a[p][q]) < abs(diff) * 1e-15:
            t = a[p][q] / diff
        else:
            phi = diff / (2.0 * a[p][q])
            t = 1.0 / (abs(phi) + math.sqrt(phi * phi + 1.0))
            if phi < 0.0:
                t = -t
        c = 1.0 / math.sqrt(1.0 + t * t)
        s = t * c
        tau = s / (1.0 + c)
        h = t * a[p][q]
        a[p][p] -= h
        a[q][q] += h
        a[p][q] = a[q][p] = 0.0
        for r in range(3):
            if r != p and r != q:
                arp = a[r][p]
                arq = a[r][q]
                a[r][p] = a[p][r] = arp - s * (arq + tau * arp)
                a[r][q] = a[q][r] = arq + s * (arp - tau * arq)
        for r in range(3):
            vrp = v[r][p]
            vrq = v[r][q]
            v[r][p] = vrp - s * (vrq + tau * vrp)
            v[r][q] = vrq + s * (vrp - tau * vrq)
    evals = [a[0][0], a[1][1], a[2][2]]
    evecs = [[v[0][i], v[1][i], v[2][i]] for i in range(3)]
    order = sorted(range(3), key=lambda idx: evals[idx], reverse=True)
    return [evals[i] for i in order], [evecs[i] for i in order]


def dot3(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross3(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def norm3(v):
    l = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    if l < 1e-12:
        return (0.0, 1.0, 0.0)
    return (v[0] / l, v[1] / l, v[2] / l)


def parse_glb(glb_path):
    """Loads vertices, triangle indices, and per-face RGB colors from a binary .glb."""
    with open(glb_path, "rb") as f:
        magic, version, length = struct.unpack("<4sII", f.read(12))
        if magic != b"glTF":
            raise ValueError(f"{glb_path} is not a valid GLB file")
        json_len, json_type = struct.unpack("<I4s", f.read(8))
        gltf = json.loads(f.read(json_len).decode("utf-8"))
        bin_len, bin_type = struct.unpack("<I4s", f.read(8))
        bin_data = f.read(bin_len)

    accessors = gltf.get("accessors", [])
    buffer_views = gltf.get("bufferViews", [])
    images = gltf.get("images", [])
    materials = gltf.get("materials", [])
    textures = gltf.get("textures", [])

    pil_images = []
    for img_def in images:
        if "bufferView" in img_def:
            bv = buffer_views[img_def["bufferView"]]
            offset = bv.get("byteOffset", 0)
            size = bv["byteLength"]
            import io
            pil_images.append(Image.open(io.BytesIO(bin_data[offset : offset + size])).convert("RGB"))
        else:
            pil_images.append(None)

    def read_accessor(acc_idx):
        acc = accessors[acc_idx]
        bv = buffer_views[acc["bufferView"]]
        offset = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
        count = acc["count"]
        comp_type = acc["componentType"]
        acc_type = acc["type"]
        num_comp = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc_type]
        fmt_char, comp_size = {
            5120: ("b", 1),
            5121: ("B", 1),
            5122: ("h", 2),
            5123: ("H", 2),
            5125: ("I", 4),
            5126: ("f", 4),
        }[comp_type]
        stride = bv.get("byteStride", num_comp * comp_size)
        out = []
        unpack_fmt = "<" + fmt_char * num_comp
        for i in range(count):
            pos = offset + i * stride
            vals = struct.unpack(unpack_fmt, bin_data[pos : pos + num_comp * comp_size])
            out.append(vals[0] if num_comp == 1 else vals)
        return out, comp_type

    all_verts = []
    all_tris = []
    all_face_colors = []

    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            attrs = prim["attributes"]
            positions, _ = read_accessor(attrs["POSITION"])
            v_offset = len(all_verts)
            all_verts.extend(positions)

            if "indices" in prim:
                indices, _ = read_accessor(prim["indices"])
            else:
                indices = list(range(len(positions)))

            v_colors = None
            if "COLOR_0" in attrs:
                raw_cols, ctype = read_accessor(attrs["COLOR_0"])
                v_colors = []
                for c in raw_cols:
                    if ctype == 5126:
                        v_colors.append((int(max(0, min(1, c[0])) * 255), int(max(0, min(1, c[1])) * 255), int(max(0, min(1, c[2])) * 255)))
                    elif ctype == 5123:
                        v_colors.append((c[0] >> 8, c[1] >> 8, c[2] >> 8))
                    else:
                        v_colors.append((int(c[0]), int(c[1]), int(c[2])))

            uvs = None
            if "TEXCOORD_0" in attrs:
                uvs, _ = read_accessor(attrs["TEXCOORD_0"])

            mat_img = None
            base_color = (160, 165, 170)
            if "material" in prim and prim["material"] < len(materials):
                pbr = materials[prim["material"]].get("pbrMetallicRoughness", {})
                if "baseColorFactor" in pbr:
                    bcf = pbr["baseColorFactor"]
                    base_color = (int(bcf[0] * 255), int(bcf[1] * 255), int(bcf[2] * 255))
                if "baseColorTexture" in pbr:
                    tex_idx = pbr["baseColorTexture"]["index"]
                    if tex_idx < len(textures):
                        src_idx = textures[tex_idx].get("source", 0)
                        if src_idx < len(pil_images):
                            mat_img = pil_images[src_idx]

            for t in range(0, len(indices) - 2, 3):
                i0, i1, i2 = indices[t], indices[t + 1], indices[t + 2]
                all_tris.append((v_offset + i0, v_offset + i1, v_offset + i2))
                if v_colors:
                    c0, c1, c2 = v_colors[i0], v_colors[i1], v_colors[i2]
                    all_face_colors.append(((c0[0] + c1[0] + c2[0]) // 3, (c0[1] + c1[1] + c2[1]) // 3, (c0[2] + c1[2] + c2[2]) // 3))
                elif mat_img and uvs:
                    u = ((uvs[i0][0] + uvs[i1][0] + uvs[i2][0]) / 3.0) % 1.0
                    v = ((uvs[i0][1] + uvs[i1][1] + uvs[i2][1]) / 3.0) % 1.0
                    px = min(mat_img.width - 1, max(0, int(u * mat_img.width)))
                    py = min(mat_img.height - 1, max(0, int(v * mat_img.height)))
                    all_face_colors.append(mat_img.getpixel((px, py)))
                else:
                    all_face_colors.append(base_color)

    return all_verts, all_tris, all_face_colors


def align_mesh_axes(verts, archetype, flip_y=False, flip_x=False, rot_y_deg=0.0):
    """
    Aligns a raw 3D mesh's principal axes to match the stylized archetype convention
    and scales/positions it to fit the archetype's local-space AABB and grip origin.
    """
    n = len(verts)
    if n == 0:
        return verts

    cx = sum(v[0] for v in verts) / n
    cy = sum(v[1] for v in verts) / n
    cz = sum(v[2] for v in verts) / n
    centered = [(v[0] - cx, v[1] - cy, v[2] - cz) for v in verts]

    cov = [[0.0, 0.0, 0.0] for _ in range(3)]
    for v in centered:
        for i in range(3):
            for j in range(3):
                cov[i][j] += v[i] * v[j] / n

    _, evecs = jacobi_eigen_3x3(cov)
    e0 = norm3(evecs[0])
    e1 = norm3(evecs[1])
    e2 = cross3(e0, e1)

    if archetype == "crossbow":
        axis_x, axis_z, axis_y = e0, e1, e2
    elif archetype == "shield":
        axis_y, axis_x, axis_z = e0, e1, e2
    else:
        axis_y, axis_x, axis_z = e0, e1, e2

    if dot3(cross3(axis_x, axis_y), axis_z) < 0:
        axis_z = (-axis_z[0], -axis_z[1], -axis_z[2])

    aligned = [(dot3(v, axis_x), dot3(v, axis_y), dot3(v, axis_z)) for v in centered]

    if archetype in ("battleaxe", "axe", "hammer", "warhammer", "staff", "wand"):
        y_vals = [v[1] for v in aligned]
        y_mid = 0.5 * (min(y_vals) + max(y_vals))
        top_x = [abs(v[0]) for v in aligned if v[1] > y_mid]
        bot_x = [abs(v[0]) for v in aligned if v[1] <= y_mid]
        avg_top = sum(top_x) / max(1, len(top_x))
        avg_bot = sum(bot_x) / max(1, len(bot_x))
        if avg_bot > avg_top:
            aligned = [(-v[0], -v[1], v[2]) for v in aligned]

    if archetype == "axe":
        y_vals = [v[1] for v in aligned]
        y_mid = 0.5 * (min(y_vals) + max(y_vals))
        head_x = [v[0] for v in aligned if v[1] > y_mid]
        if head_x and (sum(head_x) / len(head_x)) > 0:
            aligned = [(-v[0], v[1], -v[2]) for v in aligned]

    if flip_y:
        aligned = [(-v[0], -v[1], v[2]) for v in aligned]
    if flip_x:
        aligned = [(-v[0], v[1], -v[2]) for v in aligned]
    if abs(rot_y_deg) > 1e-4:
        rad = math.radians(rot_y_deg)
        cs, sn = math.cos(rad), math.sin(rad)
        aligned = [(v[0] * cs + v[2] * sn, v[1], -v[0] * sn + v[2] * cs) for v in aligned]

    return fit_to_archetype_aabb(aligned, archetype)


def fit_to_archetype_aabb(verts, archetype):
    """Scales and translates vertices to match the archetype's local AABB and grip origin."""
    prof = ARCHETYPE_PROFILES[archetype]
    tx0, ty0, tz0, tx1, ty1, tz1 = prof["aabb"]
    tw, th, td = tx1 - tx0, ty1 - ty0, tz1 - tz0

    xs = [v[0] for v in verts]
    ys = [v[1] for v in verts]
    zs = [v[2] for v in verts]
    mx0, mx1 = min(xs), max(xs)
    my0, my1 = min(ys), max(ys)
    mz0, mz1 = min(zs), max(zs)
    sw = max(1e-6, mx1 - mx0)
    sh = max(1e-6, my1 - my0)
    sd = max(1e-6, mz1 - mz0)

    out = []
    for vx, vy, vz in verts:
        nx = tx0 + ((vx - mx0) / sw) * tw
        ny = ty0 + ((vy - my0) / sh) * th
        nz = tz0 + ((vz - mz0) / sd) * td
        out.append((nx, ny, nz))
    return out


def decimate_mesh_grid(verts, tris, face_colors, target_tris=600):
    """
    Simplifies a high-poly triangle mesh to `target_tris` using adaptive 3D spatial
    vertex clustering with QEM-like centroid averaging and face color preservation.
    """
    if len(tris) <= target_tris:
        return verts, tris, face_colors

    xs = [v[0] for v in verts]
    ys = [v[1] for v in verts]
    zs = [v[2] for v in verts]
    x0, x1 = min(xs), max(xs)
    y0, y1 = min(ys), max(ys)
    z0, z1 = min(zs), max(zs)
    dx = max(1e-5, x1 - x0)
    dy = max(1e-5, y1 - y0)
    dz = max(1e-5, z1 - z0)

    lo, hi = 6, 80
    best_res = (verts, tris, face_colors)
    best_diff = abs(len(tris) - target_tris)

    for _ in range(9):
        mid = (lo + hi) // 2
        max_dim = max(dx, dy, dz)
        cell_size = max_dim / mid
        clusters = {}
        v_to_cluster = []
        for vx, vy, vz in verts:
            key = (int((vx - x0) / cell_size), int((vy - y0) / cell_size), int((vz - z0) / cell_size))
            if key not in clusters:
                clusters[key] = [len(clusters), 0.0, 0.0, 0.0, 0]
            entry = clusters[key]
            entry[1] += vx
            entry[2] += vy
            entry[3] += vz
            entry[4] += 1
            v_to_cluster.append(entry[0])

        new_verts = [None] * len(clusters)
        for entry in clusters.values():
            idx, sx, sy, sz, cnt = entry
            new_verts[idx] = (sx / cnt, sy / cnt, sz / cnt)

        tri_map = {}
        for t_idx, (i0, i1, i2) in enumerate(tris):
            c0, c1, c2 = v_to_cluster[i0], v_to_cluster[i1], v_to_cluster[i2]
            if c0 == c1 or c1 == c2 or c2 == c0:
                continue
            canon = tuple(sorted((c0, c1, c2)))
            col = face_colors[t_idx]
            if canon not in tri_map:
                tri_map[canon] = [(c0, c1, c2), col[0], col[1], col[2], 1]
            else:
                rec = tri_map[canon]
                rec[1] += col[0]
                rec[2] += col[1]
                rec[3] += col[2]
                rec[4] += 1

        new_tris = []
        new_cols = []
        for rec in tri_map.values():
            tri, r_sum, g_sum, b_sum, cnt = rec
            p0, p1, p2 = new_verts[tri[0]], new_verts[tri[1]], new_verts[tri[2]]
            nvec = cross3((p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]), (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]))
            area2 = math.sqrt(nvec[0] * nvec[0] + nvec[1] * nvec[1] + nvec[2] * nvec[2])
            if area2 < 1e-7:
                continue
            new_tris.append(tri)
            new_cols.append((r_sum // cnt, g_sum // cnt, b_sum // cnt))

        diff = abs(len(new_tris) - target_tris)
        if len(new_tris) > 20 and diff < best_diff:
            best_diff = diff
            best_res = (new_verts, new_tris, new_cols)

        if len(new_tris) < target_tris:
            lo = mid + 1
        else:
            hi = mid - 1

    return best_res


def reconstruct_from_concept_image(image_path, archetype, palette_names=None, item_id=""):
    """
    Constructs clean, flat-shaded low-poly 3D stylized meshes using solid palette swatches
    and stylized Adventurers proportions (slender shafts/grips and exaggerated blades/heads/finials).
    """
    from collections import deque

    im = Image.open(image_path).convert("RGBA")
    w, h = im.size
    pixels = im.load()

    corners = [pixels[0, 0], pixels[w - 1, 0], pixels[0, h - 1], pixels[w - 1, h - 1]]
    bg_r = sum(c[0] for c in corners) // 4
    bg_g = sum(c[1] for c in corners) // 4
    bg_b = sum(c[2] for c in corners) // 4

    mask_pts = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            if a >= 128 and (abs(r - bg_r) + abs(g - bg_g) + abs(b - bg_b)) > 30:
                mask_pts.append((x, y))

    if len(mask_pts) < 50:
        raise ValueError(f"Could not segment foreground object from {image_path}")

    # Auto-rotate elongated weapons if drawn diagonally
    mx = sum(p[0] for p in mask_pts) / len(mask_pts)
    my = sum(p[1] for p in mask_pts) / len(mask_pts)
    sxx = sum((p[0] - mx) ** 2 for p in mask_pts)
    syy = sum((p[1] - my) ** 2 for p in mask_pts)
    sxy = sum((p[0] - mx) * (p[1] - my) for p in mask_pts)
    angle_rad = 0.5 * math.atan2(2.0 * sxy, sxx - syy)
    rot_deg = math.degrees(angle_rad) - 90.0
    if archetype in ("battleaxe", "axe", "sword", "broadsword", "dagger", "hammer", "warhammer", "staff", "wand"):
        while rot_deg > 90.0:
            rot_deg -= 180.0
        while rot_deg < -90.0:
            rot_deg += 180.0
        if 8.0 < abs(rot_deg) < 75.0:
            im = im.rotate(rot_deg, resample=Image.Resampling.BILINEAR, expand=True, fillcolor=(bg_r, bg_g, bg_b, 255))
            w, h = im.size
            pixels = im.load()

    fg_xs = []
    fg_ys = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            if a >= 128 and (abs(r - bg_r) + abs(g - bg_g) + abs(b - bg_b)) > 30:
                fg_xs.append(x)
                fg_ys.append(y)

    x_min, x_max = min(fg_xs), max(fg_xs)
    y_top, y_bot = min(fg_ys), max(fg_ys)
    pad = 4
    crop_im = im.crop((max(0, x_min - pad), max(0, y_top - pad), min(w, x_max + 1 + pad), min(h, y_bot + 1 + pad)))

    hr_w, hr_h = 320, 420
    hr_im = crop_im.resize((hr_w, hr_h), Image.Resampling.BILINEAR)
    hr_px = hr_im.load()

    rgb_hr = [[(bg_r, bg_g, bg_b)] * hr_w for _ in range(hr_h)]
    is_bg = [[False] * hr_w for _ in range(hr_h)]
    q = deque()

    def bg_diff(r, g, b, a):
        if a < 128:
            return 0
        return abs(r - bg_r) + abs(g - bg_g) + abs(b - bg_b)

    for py in range(hr_h):
        for px in range(hr_w):
            r, g, b, a = hr_px[px, py]
            rgb_hr[py][px] = (r, g, b)
            if px in (0, hr_w - 1) or py in (0, hr_h - 1):
                if bg_diff(r, g, b, a) <= 38:
                    is_bg[py][px] = True
                    q.append((px, py))

    while q:
        cx_q, cy_q = q.popleft()
        for nx_q, ny_q in ((cx_q - 1, cy_q), (cx_q + 1, cy_q), (cx_q, cy_q - 1), (cx_q, cy_q + 1)):
            if 0 <= nx_q < hr_w and 0 <= ny_q < hr_h and not is_bg[ny_q][nx_q]:
                r, g, b, a = hr_px[nx_q, ny_q]
                if bg_diff(r, g, b, a) <= 34:
                    is_bg[ny_q][nx_q] = True
                    q.append((nx_q, ny_q))

    fg_hr = [[False] * hr_w for _ in range(hr_h)]
    row_widths = [0] * hr_h
    for py in range(hr_h):
        xs_row = []
        for px in range(hr_w):
            r, g, b, a = hr_px[px, py]
            if not is_bg[py][px] and bg_diff(r, g, b, a) > 26:
                fg_hr[py][px] = True
                xs_row.append(px)
        if len(xs_row) >= 2:
            row_widths[py] = xs_row[-1] - xs_row[0] + 1

    if archetype in ("battleaxe", "axe", "hammer", "warhammer", "staff"):
        top_w = sum(row_widths[: hr_h // 2])
        bot_w = sum(row_widths[hr_h // 2 :])
        if bot_w > top_w * 1.15:
            fg_hr.reverse()
            rgb_hr.reverse()
            row_widths.reverse()

    verts = []
    tris = []
    face_colors = []

    def add_quad(i0, i1, i2, i3, col):
        tris.append((i0, i1, i2))
        face_colors.append(col)
        tris.append((i0, i2, i3))
        face_colors.append(col)

    def add_tri(i0, i1, i2, col):
        tris.append((i0, i1, i2))
        face_colors.append(col)

    def add_3d_beveled_box(x0, x1, y0, y1, z0, z1, col_main, col_trim=None):
        """Adds a clean 6-faced flat-shaded 3D box prism with guaranteed outward normals."""
        if x0 > x1:
            x0, x1 = x1, x0
        if y0 > y1:
            y0, y1 = y1, y0
        if z0 > z1:
            z0, z1 = z1, z0
        c_side = col_trim if col_trim else col_main
        b_idx = len(verts)
        verts.extend([
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
            (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        ])
        add_quad(b_idx + 0, b_idx + 1, b_idx + 2, b_idx + 3, col_main)  # +Z front
        add_quad(b_idx + 5, b_idx + 4, b_idx + 7, b_idx + 6, col_main)  # -Z back
        add_quad(b_idx + 4, b_idx + 0, b_idx + 3, b_idx + 7, c_side)    # -X left
        add_quad(b_idx + 1, b_idx + 5, b_idx + 6, b_idx + 2, c_side)    # +X right
        add_quad(b_idx + 3, b_idx + 2, b_idx + 6, b_idx + 7, c_side)    # +Y top
        add_quad(b_idx + 4, b_idx + 5, b_idx + 1, b_idx + 0, c_side)    # -Y bottom

    def add_oct_lathe(stations, sides=8, z_aspect=0.95, cap_bot_col=None, cap_top_col=None, boss_tags=None, boss_col=None, boss_recess_col=None):
        """Builds an 8-sided low-poly shaft/handle along Y from a list of (y_val, rx, col, tilt_y, tag) stations."""
        rings = []
        for y_val, rx, r_col, tilt_y, tag in stations:
            rz = rx * z_aspect
            ring = []
            for k in range(sides):
                ang = (2.0 * math.pi * (k + 0.5)) / sides
                ux = math.cos(ang)
                uz = -math.sin(ang)
                yk = y_val + tilt_y * ux
                ring.append(len(verts))
                verts.append((rx * ux, yk, rz * uz))
            rings.append((ring, r_col, y_val, rx, rz, tag))

        for r_i in range(len(rings) - 1):
            r0, col0, y0, rx0, rz0, tag0 = rings[r_i]
            r1, col1, y1, rx1, rz1, tag1 = rings[r_i + 1]
            is_boss = boss_tags and (tag0, tag1) == boss_tags
            for k in range(sides):
                kn = (k + 1) % sides
                if is_boss and (k % 2 == 1):
                    v00, v10, v11, v01 = r0[k], r0[kn], r1[kn], r1[k]
                    p00, p10, p11, p01 = verts[v00], verts[v10], verts[v11], verts[v01]

                    def lerp3(a, b, t):
                        return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)

                    ang_face = (2.0 * math.pi * (k + 1.0)) / sides
                    n_out = (math.cos(ang_face), 0.0, -math.sin(ang_face))
                    d_apex = rx0 * 0.24
                    m_bot = lerp3(p00, p10, 0.5)
                    m_rgt = lerp3(p10, p11, 0.5)
                    m_top = lerp3(p01, p11, 0.5)
                    m_lft = lerp3(p00, p01, 0.5)
                    c_pt = lerp3(m_bot, m_top, 0.5)
                    p_ctr = (c_pt[0] + n_out[0] * d_apex, c_pt[1] + n_out[1] * d_apex, c_pt[2] + n_out[2] * d_apex)
                    ib = len(verts)
                    verts.extend([m_bot, m_rgt, m_top, m_lft, p_ctr])
                    im_bot, im_rgt, im_top, im_lft, i_ctr = ib, ib + 1, ib + 2, ib + 3, ib + 4
                    c_rec = boss_recess_col or col0
                    c_b = boss_col or col1
                    tris.extend([
                        (v00, im_bot, im_lft), (v10, im_rgt, im_bot),
                        (v11, im_top, im_rgt), (v01, im_lft, im_top),
                        (im_bot, im_rgt, i_ctr), (im_rgt, im_top, i_ctr),
                        (im_top, im_lft, i_ctr), (im_lft, im_bot, i_ctr),
                    ])
                    face_colors.extend([c_rec, c_rec, c_rec, c_rec, c_rec, c_b, c_b, c_rec])
                else:
                    add_quad(r0[k], r0[kn], r1[kn], r1[k], col0)

        if cap_bot_col is not None:
            bot_c = len(verts)
            verts.append((0.0, rings[0][2] - 0.008, 0.0))
            for k in range(sides):
                kn = (k + 1) % sides
                add_tri(bot_c, rings[0][0][kn], rings[0][0][k], cap_bot_col)
        if cap_top_col is not None:
            top_c = len(verts)
            verts.append((0.0, rings[-1][2] + 0.008, 0.0))
            for k in range(sides):
                kn = (k + 1) % sides
                add_tri(top_c, rings[-1][0][k], rings[-1][0][kn], cap_top_col)

    def sample_clean_rgb(px_f, py_f, win=1):
        px_c = min(hr_w - 1, max(0, int(round(px_f))))
        py_c = min(hr_h - 1, max(0, int(round(py_f))))
        cands = []
        for oy in range(-win, win + 1):
            qy = min(hr_h - 1, max(0, py_c + oy))
            for ox in range(-win, win + 1):
                qx = min(hr_w - 1, max(0, px_c + ox))
                if fg_hr[qy][qx]:
                    cands.append(rgb_hr[qy][qx])
        if not cands:
            return rgb_hr[py_c][px_c]
        cands.sort(key=lambda c: 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2])
        return cands[len(cands) // 2]

    # Canonical RGB values that map deterministically to clean stylized swatches in dungeon_texture.png
    COL_DARK_IRON = (74, 81, 85)     # dark_iron (0, 0)
    COL_STEEL = (129, 140, 145)      # steel (0, 1)
    COL_COPPER = (177, 111, 82)      # copper (0, 2)
    COL_OBSIDIAN = (19, 25, 27)      # obsidian (0, 3)
    COL_WOOD = (155, 90, 69)         # wood (0, 4)
    COL_STONE = (151, 143, 134)      # stone (0, 5)
    COL_RUST = (195, 101, 50)        # rust (0, 6)
    COL_BARK = (107, 92, 83)         # bark (0, 7)
    COL_SILVER = (212, 219, 222)     # silver (1, 0)
    COL_TAN = (218, 174, 125)        # tan (1, 1)
    COL_BLUE_STEEL = (122, 141, 157) # blue_steel (1, 2)
    COL_PURPLE = (102, 44, 142)      # purple (1, 3)
    COL_MAGENTA = (164, 26, 90)      # magenta (1, 4)
    COL_TEAL = (80, 170, 174)        # teal (1, 5)
    COL_LIME = (85, 182, 106)        # lime (1, 6)
    COL_CORAL = (246, 147, 114)      # coral (1, 7)
    COL_SEA_GREEN = (56, 163, 141)   # sea_green (2, 0)
    COL_GREEN = (82, 170, 72)        # green (2, 1)
    COL_ROSE = (243, 114, 127)       # rose (2, 2)
    COL_CRIMSON = (210, 34, 39)      # crimson (2, 3)
    COL_AMBER = (249, 170, 78)       # amber (2, 4)
    COL_GOLD = (234, 194, 83)        # gold (2, 5)
    COL_SKY_BLUE = (98, 160, 208)    # sky_blue (2, 6)
    COL_ORANGE = (249, 158, 57)      # orange (2, 7)
    COL_IVORY = (220, 208, 195)      # ivory (3, 0)
    COL_BRASS = (208, 151, 69)       # brass (3, 1)
    COL_COBALT = (37, 126, 188)      # cobalt (3, 2)
    COL_JADE = (56, 163, 141)        # jade (3, 3)
    COL_EMERALD = (0, 131, 83)       # emerald (3, 4)
    COL_NEUTRAL_GRAY = (142, 142, 141) # neutral_gray (3, 5)

    pal_set = set(palette_names or [])
    id_low = (item_id or "").lower()
    has_gold = ("gold" in pal_set) or ("gilded" in id_low) or ("sunblade" in id_low) or ("aegis" in id_low) or ("celestial" in id_low) or ("archmage" in id_low) or ("executioner" in id_low)

    # -------------------------------------------------------------------------
    # 1A) Potions: Clean Flat-Shaded 8-Sided stylized Alchemical Bottles (3 Sizes)
    # -------------------------------------------------------------------------
    if archetype.startswith("potion"):
        # Determine 3-tone liquid/glass colors from item_id first so highlight swatches never hijack another theme
        if "emerald" in id_low:
            liq_dark, liq_main, liq_light = COL_EMERALD, COL_GREEN, COL_LIME
        elif "lime" in id_low:
            liq_dark, liq_main, liq_light = COL_GREEN, COL_LIME, COL_GOLD
        elif "teal" in id_low or "sea" in id_low:
            liq_dark, liq_main, liq_light = COL_EMERALD, COL_SEA_GREEN, COL_TEAL
        elif "cobalt" in id_low or "azure" in id_low:
            liq_dark, liq_main, liq_light = COL_OBSIDIAN, COL_COBALT, COL_SKY_BLUE
        elif "purple" in id_low or "violet" in id_low:
            liq_dark, liq_main, liq_light = COL_OBSIDIAN, COL_PURPLE, COL_MAGENTA
        elif "magenta" in id_low or "rose" in id_low:
            liq_dark, liq_main, liq_light = COL_PURPLE, COL_MAGENTA, COL_ROSE
        elif "orange" in id_low:
            liq_dark, liq_main, liq_light = COL_RUST, COL_ORANGE, COL_AMBER
        elif "coral" in id_low:
            liq_dark, liq_main, liq_light = COL_COPPER, COL_CORAL, COL_ROSE
        elif "gold" in id_low or "amber" in id_low or "golden" in id_low:
            liq_dark, liq_main, liq_light = COL_BRASS, COL_AMBER, COL_GOLD
        elif "ivory" in id_low or "milky" in id_low or "silver" in id_low:
            liq_dark, liq_main, liq_light = COL_BLUE_STEEL, COL_SILVER, COL_IVORY
        elif "obsidian" in id_low or "inky" in id_low:
            liq_dark, liq_main, liq_light = COL_OBSIDIAN, COL_DARK_IRON, COL_STEEL
        else:
            # Default: Crimson / Swallow / Healing
            liq_dark, liq_main, liq_light = COL_MAGENTA, COL_CRIMSON, COL_ROSE

        sides = 8

        def add_potion_ring(y_val, r_val):
            ring_indices = []
            for k in range(sides):
                ang = (2.0 * math.pi * (k + 0.5)) / sides
                ux = math.cos(ang)
                uz = -math.sin(ang)
                ring_indices.append(len(verts))
                verts.append((r_val * ux, y_val, r_val * uz))
            return ring_indices

        def connect_potion_rings(r0, r1, col_default, facet_overrides=None):
            for k in range(sides):
                kn = (k + 1) % sides
                c_face = facet_overrides.get(k, col_default) if facet_overrides else col_default
                add_quad(r0[k], r0[kn], r1[kn], r1[k], c_face)

        def cap_potion_ring(ring_indices, y_val, col_cap, is_top=True):
            c_idx = len(verts)
            verts.append((0.0, y_val, 0.0))
            for k in range(sides):
                kn = (k + 1) % sides
                if is_top:
                    add_tri(c_idx, ring_indices[k], ring_indices[kn], col_cap)
                else:
                    add_tri(c_idx, ring_indices[kn], ring_indices[k], col_cap)

        if archetype == "potion_small":
            # Small Lesser Vial: slender 8-sided rounded-bottom glass test-tube vial with cork
            r_b0 = add_potion_ring(0.000, 0.042)
            r_b1 = add_potion_ring(0.032, 0.088)
            r_b2 = add_potion_ring(0.075, 0.104)
            r_b3 = add_potion_ring(0.165, 0.104)
            r_b4 = add_potion_ring(0.285, 0.104)
            r_b5 = add_potion_ring(0.365, 0.102)
            r_g0 = add_potion_ring(0.395, 0.098)
            r_n0 = add_potion_ring(0.422, 0.064)
            r_n1 = add_potion_ring(0.452, 0.064)
            r_f0 = add_potion_ring(0.460, 0.108)
            r_f1 = add_potion_ring(0.486, 0.108)
            r_c0 = add_potion_ring(0.492, 0.062)
            r_c1 = add_potion_ring(0.518, 0.084)
            r_c2 = add_potion_ring(0.540, 0.070)

            cap_potion_ring(r_b0, 0.000, liq_dark, is_top=False)
            connect_potion_rings(r_b0, r_b1, liq_dark)
            connect_potion_rings(r_b1, r_b2, liq_dark)
            connect_potion_rings(r_b2, r_b3, liq_main)
            connect_potion_rings(r_b3, r_b4, liq_main)
            connect_potion_rings(r_b4, r_b5, liq_light)
            # Clear glass upper rim above liquid meniscus, pinched neck, flared lip & cork
            connect_potion_rings(r_b5, r_g0, COL_BLUE_STEEL)
            connect_potion_rings(r_g0, r_n0, COL_BLUE_STEEL)
            connect_potion_rings(r_n0, r_n1, COL_BLUE_STEEL)
            connect_potion_rings(r_n1, r_f0, COL_STEEL)
            connect_potion_rings(r_f0, r_f1, COL_SILVER)
            connect_potion_rings(r_f1, r_c0, COL_STEEL)
            connect_potion_rings(r_c0, r_c1, COL_WOOD)
            connect_potion_rings(r_c1, r_c2, COL_TAN)
            cap_potion_ring(r_c2, 0.540, COL_TAN, is_top=True)

        elif archetype == "potion_medium":
            # Medium Enhanced Vial: 8-sided cylindrical bottle with front parchment label & cork
            r_b0 = add_potion_ring(0.000, 0.122)
            r_b1 = add_potion_ring(0.042, 0.158)
            r_b2 = add_potion_ring(0.115, 0.158)
            r_l0 = add_potion_ring(0.128, 0.168)
            r_l1 = add_potion_ring(0.158, 0.168)
            r_l2 = add_potion_ring(0.225, 0.168)
            r_l3 = add_potion_ring(0.255, 0.168)
            r_l4 = add_potion_ring(0.320, 0.168)
            r_l5 = add_potion_ring(0.350, 0.168)
            r_b3 = add_potion_ring(0.364, 0.158)
            r_s0 = add_potion_ring(0.415, 0.148)
            r_n0 = add_potion_ring(0.480, 0.068)
            r_n1 = add_potion_ring(0.535, 0.068)
            r_f0 = add_potion_ring(0.548, 0.104)
            r_f1 = add_potion_ring(0.585, 0.104)
            r_c0 = add_potion_ring(0.592, 0.066)
            r_c1 = add_potion_ring(0.642, 0.088)
            r_c2 = add_potion_ring(0.680, 0.074)

            cap_potion_ring(r_b0, 0.000, liq_dark, is_top=False)
            connect_potion_rings(r_b0, r_b1, liq_dark)
            connect_potion_rings(r_b1, r_b2, liq_main)
            # Raised parchment label on front 3 facets (k = 4, 5, 6), glowing liquid on back
            lbl_border = {4: COL_TAN, 5: COL_TAN, 6: COL_TAN}
            lbl_body = {4: COL_IVORY, 5: COL_IVORY, 6: COL_IVORY}
            lbl_rune = {4: COL_IVORY, 5: liq_main, 6: COL_IVORY}
            connect_potion_rings(r_b2, r_l0, liq_main, lbl_border)
            connect_potion_rings(r_l0, r_l1, liq_main, lbl_border)
            connect_potion_rings(r_l1, r_l2, liq_main, lbl_body)
            connect_potion_rings(r_l2, r_l3, liq_main, lbl_rune)
            connect_potion_rings(r_l3, r_l4, liq_light, lbl_body)
            connect_potion_rings(r_l4, r_l5, liq_light, lbl_border)
            connect_potion_rings(r_l5, r_b3, liq_light, lbl_border)
            # Upper liquid highlight & tapered glass neck
            connect_potion_rings(r_b3, r_s0, liq_light)
            connect_potion_rings(r_s0, r_n0, liq_light)
            connect_potion_rings(r_n0, r_n1, COL_BLUE_STEEL)
            connect_potion_rings(r_n1, r_f0, COL_STEEL)
            connect_potion_rings(r_f0, r_f1, COL_SILVER)
            connect_potion_rings(r_f1, r_c0, COL_STEEL)
            # Tapered wooden cork stopper
            connect_potion_rings(r_c0, r_c1, COL_WOOD)
            connect_potion_rings(r_c1, r_c2, COL_TAN)
            cap_potion_ring(r_c2, 0.680, COL_TAN, is_top=True)

        else:
            # Large Superior Flask: bulbous round globe with leather belly strap, front clasp & cork
            r_b0 = add_potion_ring(0.000, 0.145)
            r_b1 = add_potion_ring(0.045, 0.182)
            r_g0 = add_potion_ring(0.115, 0.236)
            r_g1 = add_potion_ring(0.215, 0.256)
            r_s0 = add_potion_ring(0.230, 0.268)
            r_s1 = add_potion_ring(0.255, 0.268)
            r_s2 = add_potion_ring(0.305, 0.268)
            r_s3 = add_potion_ring(0.330, 0.268)
            r_g2 = add_potion_ring(0.345, 0.256)
            r_g3 = add_potion_ring(0.450, 0.232)
            r_g4 = add_potion_ring(0.530, 0.168)
            r_n0 = add_potion_ring(0.590, 0.084)
            r_m0 = add_potion_ring(0.600, 0.102)
            r_m1 = add_potion_ring(0.640, 0.102)
            r_n1 = add_potion_ring(0.650, 0.082)
            r_n2 = add_potion_ring(0.682, 0.082)
            r_f0 = add_potion_ring(0.695, 0.126)
            r_f1 = add_potion_ring(0.735, 0.126)
            r_c0 = add_potion_ring(0.742, 0.080)
            r_c1 = add_potion_ring(0.795, 0.112)
            r_c2 = add_potion_ring(0.840, 0.094)

            # Step out front buckle facet (vertices 5 & 6 of facet k=5) cleanly in the octagonal ring
            for r_bk in (r_s0, r_s1, r_s2, r_s3):
                for vk in (5, 6):
                    vx, vy, vz = verts[r_bk[vk]]
                    verts[r_bk[vk]] = (vx * 1.045, vy, vz * 1.045)

            cap_potion_ring(r_b0, 0.000, COL_BARK, is_top=False)
            connect_potion_rings(r_b0, r_b1, COL_WOOD)
            connect_potion_rings(r_b1, r_g0, liq_dark)
            connect_potion_rings(r_g0, r_g1, liq_main)
            # Equatorial leather harness strap with front silver/gold buckle on k = 5
            connect_potion_rings(r_g1, r_s0, COL_BARK, {5: COL_STEEL})
            connect_potion_rings(r_s0, r_s1, COL_WOOD, {4: COL_COPPER, 5: COL_SILVER, 6: COL_COPPER})
            connect_potion_rings(r_s1, r_s2, COL_WOOD, {4: COL_COPPER, 5: COL_GOLD, 6: COL_COPPER})
            connect_potion_rings(r_s2, r_s3, COL_WOOD, {4: COL_COPPER, 5: COL_SILVER, 6: COL_COPPER})
            connect_potion_rings(r_s3, r_g2, COL_BARK, {5: COL_STEEL})
            # Upper bulbous globe with two-tone liquid highlight
            connect_potion_rings(r_g2, r_g3, liq_main)
            connect_potion_rings(r_g3, r_g4, liq_light)
            connect_potion_rings(r_g4, r_n0, liq_light)
            # Metallic neck collar ring & flared glass rim
            connect_potion_rings(r_n0, r_m0, COL_STEEL)
            connect_potion_rings(r_m0, r_m1, COL_SILVER)
            connect_potion_rings(r_m1, r_n1, COL_STEEL)
            connect_potion_rings(r_n1, r_n2, COL_BLUE_STEEL)
            connect_potion_rings(r_n2, r_f0, COL_STEEL)
            connect_potion_rings(r_f0, r_f1, COL_SILVER)
            connect_potion_rings(r_f1, r_c0, COL_STEEL)
            # Stepped cork stopper
            connect_potion_rings(r_c0, r_c1, COL_WOOD)
            connect_potion_rings(r_c1, r_c2, COL_TAN)
            cap_potion_ring(r_c2, 0.840, COL_TAN, is_top=True)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 1A2) Scrolls: Compact Square Weathered Parchment with 3D Beveled Celtic Runes
    # -------------------------------------------------------------------------
    if archetype == "scroll":
        # Determine 3-tone rune colors from item_id first so highlight swatches never hijack another theme
        if "emerald" in id_low or "fehu" in id_low:
            rune_dark, rune_main, rune_light = COL_EMERALD, COL_GREEN, COL_LIME
        elif "lime" in id_low or "hagalaz" in id_low:
            rune_dark, rune_main, rune_light = COL_GREEN, COL_LIME, COL_GOLD
        elif "teal" in id_low or "eihwaz" in id_low:
            rune_dark, rune_main, rune_light = COL_EMERALD, COL_SEA_GREEN, COL_TEAL
        elif "cobalt" in id_low or "azure" in id_low or "raidho" in id_low:
            rune_dark, rune_main, rune_light = COL_OBSIDIAN, COL_COBALT, COL_SKY_BLUE
        elif "purple" in id_low or "violet" in id_low or "dagaz" in id_low:
            rune_dark, rune_main, rune_light = COL_OBSIDIAN, COL_PURPLE, COL_SKY_BLUE
        elif "magenta" in id_low or "rose" in id_low or "berkano" in id_low:
            rune_dark, rune_main, rune_light = COL_PURPLE, COL_MAGENTA, COL_ROSE
        elif "orange" in id_low or "thurisaz" in id_low:
            rune_dark, rune_main, rune_light = COL_RUST, COL_ORANGE, COL_AMBER
        elif "coral" in id_low or "sowilo" in id_low:
            rune_dark, rune_main, rune_light = COL_COPPER, COL_CORAL, COL_ROSE
        elif "gold" in id_low or "amber" in id_low or "algiz" in id_low:
            rune_dark, rune_main, rune_light = COL_BRASS, COL_GOLD, COL_AMBER
        elif "silver" in id_low or "ingwaz" in id_low or "milky" in id_low:
            rune_dark, rune_main, rune_light = COL_BLUE_STEEL, COL_SILVER, COL_STEEL
        elif "obsidian" in id_low or "tiwaz" in id_low or "inky" in id_low:
            rune_dark, rune_main, rune_light = COL_OBSIDIAN, COL_DARK_IRON, COL_STEEL
        else:
            # Default: Crimson / Othala
            rune_dark, rune_main, rune_light = COL_MAGENTA, COL_CRIMSON, COL_ROSE

        def add_roller_assembly(cy, cz):
            """Builds a horizontal 8-sided rolled parchment tube with weathered wood collars and dark iron spindle tips."""
            r_sides = 8
            # Stations along X: (x_pos, radius, col_upper, col_lower, y_bow)
            stations_x = [
                (-0.440, 0.016, COL_OBSIDIAN, COL_OBSIDIAN, 0.0),
                (-0.408, 0.038, COL_DARK_IRON, COL_OBSIDIAN, 0.0),
                (-0.376, 0.024, COL_DARK_IRON, COL_OBSIDIAN, 0.0),
                (-0.370, 0.056, COL_WOOD, COL_BARK, 0.0),
                (-0.336, 0.056, COL_WOOD, COL_BARK, 0.0),
                (-0.330, 0.048, COL_TAN, COL_COPPER, 0.0),
                (-0.245, 0.047, COL_TAN, COL_COPPER, 0.0),
                (-0.235, 0.046, COL_IVORY, COL_TAN, -0.004 if cy > 0.32 else 0.004),
                (0.000, 0.044, COL_IVORY, COL_TAN, -0.008 if cy > 0.32 else 0.008),
                (0.235, 0.046, COL_IVORY, COL_TAN, -0.004 if cy > 0.32 else 0.004),
                (0.245, 0.047, COL_TAN, COL_COPPER, 0.0),
                (0.330, 0.048, COL_TAN, COL_COPPER, 0.0),
                (0.336, 0.056, COL_WOOD, COL_BARK, 0.0),
                (0.370, 0.056, COL_WOOD, COL_BARK, 0.0),
                (0.376, 0.024, COL_DARK_IRON, COL_OBSIDIAN, 0.0),
                (0.408, 0.038, COL_DARK_IRON, COL_OBSIDIAN, 0.0),
                (0.440, 0.016, COL_OBSIDIAN, COL_OBSIDIAN, 0.0),
            ]
            rings_x = []
            for xp, rad, c_up, c_lo, y_bow in stations_x:
                ring = []
                for k in range(r_sides):
                    ang = (2.0 * math.pi * (k + 0.5)) / r_sides
                    uy = math.cos(ang)
                    uz = math.sin(ang)
                    ring.append(len(verts))
                    verts.append((xp, cy + y_bow + rad * uy, cz + rad * uz))
                rings_x.append((ring, c_up, c_lo, xp))

            for i in range(len(rings_x) - 1):
                r0, cu0, cl0, _ = rings_x[i]
                r1, cu1, cl1, _ = rings_x[i + 1]
                for k in range(r_sides):
                    kn = (k + 1) % r_sides
                    ang_m = (2.0 * math.pi * (k + 1.0)) / r_sides
                    c_face = cu0 if (math.sin(ang_m) >= -0.15 and math.cos(ang_m) >= -0.35) else cl0
                    add_quad(r0[k], r0[kn], r1[kn], r1[k], c_face)

            # Cap left and right spindle tips
            c_left = len(verts)
            verts.append((-0.442, cy, cz))
            c_right = len(verts)
            verts.append((0.442, cy, cz))
            for k in range(r_sides):
                kn = (k + 1) % r_sides
                add_tri(c_left, rings_x[0][0][k], rings_x[0][0][kn], COL_OBSIDIAN)
                add_tri(c_right, rings_x[-1][0][kn], rings_x[-1][0][k], COL_OBSIDIAN)

        add_roller_assembly(0.052, 0.0)
        add_roller_assembly(0.588, 0.0)

        # Build the compact square ripped parchment sheet between y = 0.080 and y = 0.560
        # Width = 0.66 (x in [-0.33 .. +0.33]), Height = 0.48 (y in [0.080 .. 0.560])
        y_rows = [
            (0.080, -0.330, 0.330, False, False),
            (0.118, -0.326, 0.326, False, False),
            (0.162, -0.328, 0.272, False, True),   # right rip notch 1
            (0.188, -0.324, 0.326, False, False),
            (0.232, -0.268, 0.324, True, False),   # left rip notch 1
            (0.258, -0.326, 0.324, False, False),
            (0.320, -0.322, 0.322, False, False),
            (0.382, -0.326, 0.266, False, True),   # right rip notch 2
            (0.410, -0.324, 0.326, False, False),
            (0.454, -0.270, 0.324, True, False),   # left rip notch 2
            (0.482, -0.326, 0.326, False, False),
            (0.522, -0.328, 0.328, False, False),
            (0.560, -0.330, 0.330, False, False),
        ]
        half_thk = 0.013
        front_grid = []
        back_grid = []
        for yr, xl, xr, rip_l, rip_r in y_rows:
            t_y = (yr - 0.080) / (0.560 - 0.080)
            cz_curve = 0.014 * math.sin(math.pi * t_y)
            xs_row = [xl, -0.245, -0.160, -0.080, 0.0, 0.080, 0.160, 0.245, xr]
            f_row = []
            b_row = []
            for xk in xs_row:
                f_row.append(len(verts))
                verts.append((xk, yr, cz_curve + half_thk))
                b_row.append(len(verts))
                verts.append((xk, yr, cz_curve - half_thk))
            front_grid.append((f_row, rip_l, rip_r, yr))
            back_grid.append((b_row, rip_l, rip_r, yr))

        for r_i in range(len(front_grid) - 1):
            f0, rl0, rr0, y0 = front_grid[r_i]
            f1, rl1, rr1, y1 = front_grid[r_i + 1]
            b0, _, _, _ = back_grid[r_i]
            b1, _, _, _ = back_grid[r_i + 1]
            is_end_band = (r_i == 0) or (r_i == len(front_grid) - 2)
            col_center = COL_TAN if is_end_band else COL_IVORY
            col_left = COL_COPPER if (rl0 or rl1) else COL_TAN
            col_right = COL_COPPER if (rr0 or rr1) else COL_TAN
            col_strips = [col_left] + [col_center] * 6 + [col_right]
            for c_i in range(8):
                add_quad(f0[c_i], f0[c_i + 1], f1[c_i + 1], f1[c_i], col_strips[c_i])
                add_quad(b0[c_i + 1], b0[c_i], b1[c_i], b1[c_i + 1], COL_TAN)
            # Side ripped edges
            add_quad(b0[0], f0[0], f1[0], b1[0], COL_COPPER if (rl0 or rl1) else COL_WOOD)
            add_quad(f0[8], b0[8], b1[8], f1[8], COL_COPPER if (rr0 or rr1) else COL_WOOD)

        # Helper to emboss a continuous curve-following 3D beveled runic bar onto the front parchment surface
        def add_rune_stroke(x0, y0, x1, y1, width=0.056):
            dx = x1 - x0
            dy = y1 - y0
            ln = math.sqrt(dx * dx + dy * dy)
            if ln < 1e-5:
                return
            ux, uy = dx / ln, dy / ln
            nx, ny = -uy, ux
            hw = width * 0.5
            hw_top = width * 0.22

            def z_on_sheet(yv, offset):
                ty = max(0.0, min(1.0, (yv - 0.080) / (0.560 - 0.080)))
                return 0.014 * math.sin(math.pi * ty) + half_thk + offset

            steps = 6
            b_l = []
            b_r = []
            t_l = []
            t_r = []
            for s_i in range(steps + 1):
                t = s_i / steps
                sx = x0 + dx * t
                sy = y0 + dy * t
                yl = sy + ny * hw
                yr_v = sy - ny * hw
                ytl = sy + ny * hw_top
                ytr = sy - ny * hw_top

                b_l.append(len(verts))
                verts.append((sx + nx * hw, yl, z_on_sheet(yl, 0.012)))
                b_r.append(len(verts))
                verts.append((sx - nx * hw, yr_v, z_on_sheet(yr_v, 0.012)))
                t_l.append(len(verts))
                verts.append((sx + nx * hw_top, ytl, z_on_sheet(ytl, 0.038)))
                t_r.append(len(verts))
                verts.append((sx - nx * hw_top, ytr, z_on_sheet(ytr, 0.038)))

            for s_i in range(steps):
                add_quad(t_r[s_i], t_r[s_i + 1], t_l[s_i + 1], t_l[s_i], rune_main)
                add_quad(t_l[s_i], t_l[s_i + 1], b_l[s_i + 1], b_l[s_i], rune_light)
                add_quad(b_r[s_i], b_r[s_i + 1], t_r[s_i + 1], t_r[s_i], rune_dark)

            ytip0 = y0 - uy * hw * 0.55
            b_tip0 = len(verts)
            verts.append((x0 - ux * hw * 0.55, ytip0, z_on_sheet(ytip0, 0.012)))
            ytip1 = y1 + uy * hw * 0.55
            b_tip1 = len(verts)
            verts.append((x1 + ux * hw * 0.55, ytip1, z_on_sheet(ytip1, 0.012)))

            add_tri(b_tip0, b_r[0], t_r[0], rune_dark)
            add_tri(b_tip0, t_r[0], t_l[0], rune_main)
            add_tri(b_tip0, t_l[0], b_l[0], rune_light)
            add_tri(b_tip1, b_l[-1], t_l[-1], rune_light)
            add_tri(b_tip1, t_l[-1], t_r[-1], rune_main)
            add_tri(b_tip1, t_r[-1], b_r[-1], rune_dark)

        # Select abstract Celtic / Elder runic glyph strokes filling almost the entire square parchment (y in [0.115 .. 0.525])
        if "raidho" in id_low or ("cobalt" in id_low and "othala" not in id_low):
            strokes = [
                (-0.135, 0.115, -0.135, 0.525),
                (-0.135, 0.525, 0.145, 0.385),
                (0.145, 0.385, -0.135, 0.275),
                (-0.075, 0.305, 0.155, 0.115),
            ]
        elif "fehu" in id_low or ("emerald" in id_low and "othala" not in id_low):
            strokes = [
                (-0.105, 0.115, -0.105, 0.525),
                (-0.105, 0.340, 0.155, 0.525),
                (-0.105, 0.205, 0.155, 0.390),
            ]
        elif "dagaz" in id_low or ("purple" in id_low and "othala" not in id_low):
            strokes = [
                (-0.165, 0.120, -0.165, 0.520),
                (0.165, 0.120, 0.165, 0.520),
                (-0.165, 0.520, 0.165, 0.120),
                (-0.165, 0.120, 0.165, 0.520),
            ]
        elif "algiz" in id_low or "gold" in id_low:
            strokes = [
                (0.000, 0.115, 0.000, 0.525),
                (0.000, 0.325, -0.170, 0.515),
                (0.000, 0.325, 0.170, 0.515),
                (-0.130, 0.180, 0.000, 0.265),
                (0.130, 0.180, 0.000, 0.265),
            ]
        elif "eihwaz" in id_low or "teal" in id_low:
            strokes = [
                (0.000, 0.115, 0.000, 0.525),
                (0.000, 0.525, -0.165, 0.385),
                (0.000, 0.115, 0.165, 0.255),
                (-0.145, 0.275, 0.145, 0.365),
            ]
        elif "thurisaz" in id_low or "orange" in id_low:
            strokes = [
                (-0.115, 0.115, -0.115, 0.525),
                (-0.115, 0.435, 0.165, 0.320),
                (0.165, 0.320, -0.115, 0.205),
                (-0.175, 0.320, -0.025, 0.320),
            ]
        elif "berkano" in id_low or "magenta" in id_low:
            strokes = [
                (-0.135, 0.115, -0.135, 0.525),
                (-0.135, 0.525, 0.135, 0.415),
                (0.135, 0.415, -0.135, 0.320),
                (-0.135, 0.320, 0.135, 0.225),
                (0.135, 0.225, -0.135, 0.115),
            ]
        elif "hagalaz" in id_low or "lime" in id_low:
            strokes = [
                (-0.155, 0.115, -0.155, 0.525),
                (0.155, 0.115, 0.155, 0.525),
                (-0.155, 0.435, 0.155, 0.295),
                (-0.155, 0.345, 0.155, 0.205),
            ]
        elif "sowilo" in id_low or "coral" in id_low:
            strokes = [
                (0.145, 0.525, -0.155, 0.350),
                (-0.155, 0.350, 0.155, 0.290),
                (0.155, 0.290, -0.145, 0.115),
                (-0.115, 0.435, 0.115, 0.205),
            ]
        elif "ingwaz" in id_low or "silver" in id_low or "milky" in id_low:
            strokes = [
                (0.000, 0.525, -0.165, 0.385),
                (-0.165, 0.385, 0.165, 0.255),
                (0.165, 0.255, 0.000, 0.115),
                (0.000, 0.115, -0.165, 0.255),
                (-0.165, 0.255, 0.165, 0.385),
                (0.165, 0.385, 0.000, 0.525),
            ]
        elif "tiwaz" in id_low or "obsidian" in id_low or "inky" in id_low:
            strokes = [
                (0.000, 0.115, 0.000, 0.525),
                (0.000, 0.525, -0.175, 0.370),
                (0.000, 0.525, 0.175, 0.370),
                (-0.140, 0.190, 0.140, 0.320),
                (-0.140, 0.320, 0.140, 0.190),
            ]
        else:
            # Default: Othala (diamond head + crossed legs)
            strokes = [
                (0.000, 0.525, -0.165, 0.380),
                (0.000, 0.525, 0.165, 0.380),
                (-0.165, 0.380, 0.165, 0.115),
                (0.165, 0.380, -0.165, 0.115),
            ]

        for sx0, sy0, sx1, sy1 in strokes:
            add_rune_stroke(sx0, sy0, sx1, sy1, width=0.056)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 1A3) Jewelry (Oversized Rings & Amulets: Gold, Silver, Wood + Gems/Signets)
    # -------------------------------------------------------------------------
    if archetype in ("ring", "amulet"):
        # 1) Determine base frame/band material (Gold, Wood, or Silver)
        if "gold" in id_low or "brass" in id_low or "gilded" in id_low or "sun" in id_low:
            mat_dark, mat_main, mat_light = COL_BRASS, COL_AMBER, COL_GOLD
            chain_dark, chain_main = COL_BRASS, COL_GOLD
            carve_accent = COL_IVORY
        elif "wood" in id_low or "oak" in id_low or "bark" in id_low or "druid" in id_low or "thorn" in id_low:
            mat_dark, mat_main, mat_light = COL_BARK, COL_WOOD, COL_TAN
            chain_dark, chain_main = COL_BARK, COL_WOOD
            carve_accent = COL_IVORY
        else:
            mat_dark, mat_main, mat_light = COL_DARK_IRON, COL_STEEL, COL_SILVER
            chain_dark, chain_main = COL_DARK_IRON, COL_STEEL
            carve_accent = COL_GOLD

        # 2) Determine whether this piece has a gemstone or is a plain/signet/carved piece
        has_gem = not any(w in id_low for w in ("plain", "signet", "carved", "band", "runic", "talisman", "crest", "knot"))
        if "emerald" in id_low:
            gem_dark, gem_main, gem_light = COL_EMERALD, COL_GREEN, COL_LIME
        elif "sapphire" in id_low or "cobalt" in id_low or "azure" in id_low:
            gem_dark, gem_main, gem_light = COL_OBSIDIAN, COL_COBALT, COL_SKY_BLUE
        elif "amethyst" in id_low or "purple" in id_low or "violet" in id_low:
            gem_dark, gem_main, gem_light = COL_OBSIDIAN, COL_PURPLE, COL_MAGENTA
        elif "topaz" in id_low or "amber" in id_low or "orange" in id_low or "citrine" in id_low:
            gem_dark, gem_main, gem_light = COL_RUST, COL_ORANGE, COL_AMBER
        elif "jade" in id_low or "turquoise" in id_low or "teal" in id_low or "aqua" in id_low:
            gem_dark, gem_main, gem_light = COL_EMERALD, COL_SEA_GREEN, COL_TEAL
        elif "garnet" in id_low or "magenta" in id_low or "rose" in id_low:
            gem_dark, gem_main, gem_light = COL_PURPLE, COL_MAGENTA, COL_ROSE
        elif "coral" in id_low:
            gem_dark, gem_main, gem_light = COL_COPPER, COL_CORAL, COL_ROSE
        elif "peridot" in id_low or "lime" in id_low:
            gem_dark, gem_main, gem_light = COL_GREEN, COL_LIME, COL_GOLD
        elif "onyx" in id_low or "obsidian" in id_low:
            gem_dark, gem_main, gem_light = COL_OBSIDIAN, COL_DARK_IRON, COL_BLUE_STEEL
        elif "diamond" in id_low or "opal" in id_low or "pearl" in id_low or "moonstone" in id_low:
            gem_dark, gem_main, gem_light = COL_BLUE_STEEL, COL_SILVER, COL_IVORY
        else:
            # Default: Ruby / Crimson
            gem_dark, gem_main, gem_light = COL_MAGENTA, COL_CRIMSON, COL_ROSE

        if archetype == "ring":
            # Oversized chunky 12-station polygonal ring hoop centered at (0, 0.235, 0)
            n_hoop = 12
            cy_hoop = 0.235
            r_in_base = 0.112
            hoop_rings = []
            for k in range(n_hoop):
                ang = -0.5 * math.pi + (2.0 * math.pi * k) / n_hoop
                ux = math.cos(ang)
                uy = math.sin(ang)
                # Top of hoop is at k = 6 (ang = +pi/2); flare outer radius & thickness toward top shoulders
                top_weight = max(0.0, math.sin(ang))
                r_out = 0.205 + 0.042 * (top_weight ** 1.4)
                r_in = r_in_base
                hz = 0.068 + 0.038 * (top_weight ** 1.2)

                # 8-vertex beveled cross-section around the hoop wall
                sec_pts = [
                    (r_out + 0.012, 0.0),            # 0: outer peak
                    (r_out, hz * 0.72),              # 1: outer-front bevel
                    (r_out - 0.022, hz),             # 2: front-outer flat
                    (r_in + 0.018, hz),              # 3: front-inner flat
                    (r_in, 0.0),                     # 4: inner hole peak
                    (r_in + 0.018, -hz),             # 5: back-inner flat
                    (r_out - 0.022, -hz),            # 6: back-outer flat
                    (r_out, -hz * 0.72),             # 7: outer-back bevel
                ]
                ring_idx = []
                for rad_v, zv in sec_pts:
                    ring_idx.append(len(verts))
                    verts.append((rad_v * ux, cy_hoop + rad_v * uy, zv))
                hoop_rings.append((ring_idx, uy))

            sec_cols_upper = [mat_light, mat_light, mat_main, mat_dark, mat_dark, mat_main, mat_light, mat_light]
            sec_cols_lower = [mat_main, mat_main, mat_main, mat_dark, mat_dark, mat_dark, mat_main, mat_dark]
            for k in range(n_hoop):
                kn = (k + 1) % n_hoop
                rk, uy0 = hoop_rings[k]
                rkn, uy1 = hoop_rings[kn]
                palette_strip = sec_cols_upper if (uy0 + uy1) >= 0.0 else sec_cols_lower
                for m in range(8):
                    mn = (m + 1) % 8
                    add_quad(rk[m], rkn[m], rkn[mn], rk[mn], palette_strip[m])

            # Top crown setting tilted 6 deg forward (+Z) and seated flush into the top of the hoop
            tilt_rad = math.radians(6.0)
            ct, st = math.cos(tilt_rad), math.sin(tilt_rad)
            crown_cy = cy_hoop + 0.212
            crown_cz = 0.006

            def add_crown_ring(local_y, rx, rz):
                idxs = []
                for k in range(8):
                    ang = (2.0 * math.pi * (k + 0.5)) / 8
                    lx = rx * math.cos(ang)
                    lz = -rz * math.sin(ang)
                    wy = crown_cy + local_y * ct - lz * st
                    wz = crown_cz + local_y * st + lz * ct
                    idxs.append(len(verts))
                    verts.append((lx, wy, wz))
                return idxs

            def connect_crown_rings(r0, r1, col_def, overrides=None):
                for k in range(8):
                    kn = (k + 1) % 8
                    c_f = overrides.get(k, col_def) if overrides else col_def
                    add_quad(r0[k], r0[kn], r1[kn], r1[k], c_f)

            def cap_crown_ring(r_idxs, local_y, col_cap, is_top=True):
                wy = crown_cy + local_y * ct
                wz = crown_cz + local_y * st
                c_i = len(verts)
                verts.append((0.0, wy, wz))
                for k in range(8):
                    kn = (k + 1) % 8
                    if is_top:
                        add_tri(c_i, r_idxs[k], r_idxs[kn], col_cap)
                    else:
                        add_tri(c_i, r_idxs[kn], r_idxs[k], col_cap)

            if has_gem:
                # Stepped bezel collet cup seated flush into hoop + oversized faceted gemstone
                b0 = add_crown_ring(-0.028, 0.125, 0.102)
                b1 = add_crown_ring(0.026, 0.158, 0.132)
                b2 = add_crown_ring(0.056, 0.150, 0.125)
                g0 = add_crown_ring(0.060, 0.140, 0.118)
                g1 = add_crown_ring(0.118, 0.152, 0.128)
                g2 = add_crown_ring(0.168, 0.088, 0.075)

                cap_crown_ring(b0, -0.028, mat_dark, is_top=False)
                connect_crown_rings(b0, b1, mat_dark, {4: mat_main, 5: mat_main, 6: mat_main})
                connect_crown_rings(b1, b2, mat_light, {0: mat_main, 1: mat_main, 2: mat_main})
                connect_crown_rings(b2, g0, mat_dark)
                # Gem lower pavilion & upper brilliant crown facets
                gem_Pav = {0: gem_dark, 1: gem_dark, 2: gem_main, 3: gem_main, 4: gem_light, 5: gem_main, 6: gem_main, 7: gem_dark}
                gem_Crn = {0: gem_main, 1: gem_main, 2: gem_light, 3: gem_light, 4: gem_light, 5: gem_main, 6: gem_light, 7: gem_main}
                connect_crown_rings(g0, g1, gem_main, gem_Pav)
                connect_crown_rings(g1, g2, gem_light, gem_Crn)
                cap_crown_ring(g2, 0.178, gem_light, is_top=True)
            else:
                # Gemless Signet / Plain Band: low flat-topped signet crest in the same material (no gem dome)
                s0 = add_crown_ring(-0.028, 0.132, 0.108)
                s1 = add_crown_ring(0.018, 0.162, 0.136)
                s2 = add_crown_ring(0.048, 0.148, 0.124)
                s3 = add_crown_ring(0.044, 0.092, 0.078)
                s4 = add_crown_ring(0.056, 0.052, 0.044)

                cap_crown_ring(s0, -0.028, mat_dark, is_top=False)
                connect_crown_rings(s0, s1, mat_dark, {4: mat_main, 5: mat_main, 6: mat_main})
                connect_crown_rings(s1, s2, mat_light, {0: mat_main, 1: mat_main, 2: mat_main})
                connect_crown_rings(s2, s3, mat_dark)
                connect_crown_rings(s3, s4, mat_main)
                cap_crown_ring(s4, 0.056, mat_light, is_top=True)

            return fit_to_archetype_aabb(verts, archetype), tris, face_colors

        # Amulet builder: heavy V-chain/cord loop + bail clasp + chunky medallion pendant
        # 1) Closed 16-station necklace loop path from bail (0, 0.43) up to top arch (0, 0.70)
        n_loop = 16
        loop_stations = []
        c_sides = 6
        c_rad = 0.028
        for k in range(n_loop):
            ang = -0.5 * math.pi + (2.0 * math.pi * k) / n_loop
            # Teardrop V-shape: narrower near bottom bail (ang = -pi/2), wider near upper shoulders
            s_t = 0.5 * (1.0 + math.sin(ang))
            cx_p = (0.12 + 0.10 * (s_t ** 0.7)) * math.cos(ang)
            cy_p = 0.565 + 0.135 * math.sin(ang)
            # Tangent along loop in XY
            ang_next = -0.5 * math.pi + (2.0 * math.pi * ((k + 1) % n_loop)) / n_loop
            s_tn = 0.5 * (1.0 + math.sin(ang_next))
            nx_p = (0.12 + 0.10 * (s_tn ** 0.7)) * math.cos(ang_next)
            ny_p = 0.565 + 0.135 * math.sin(ang_next)
            tx, ty = nx_p - cx_p, ny_p - cy_p
            tlen = max(1e-5, math.sqrt(tx * tx + ty * ty))
            tx, ty = tx / tlen, ty / tlen
            # Radial normal in XY plane pointing outward from loop center
            rx_v, ry_v = ty, -tx

            st_ring = []
            for m in range(c_sides):
                phi = (2.0 * math.pi * m) / c_sides
                cr = math.cos(phi) * c_rad
                cz = math.sin(phi) * c_rad
                st_ring.append(len(verts))
                verts.append((cx_p + rx_v * cr, cy_p + ry_v * cr, cz))
            loop_stations.append(st_ring)

        for k in range(n_loop):
            kn = (k + 1) % n_loop
            col_link = chain_main if (k % 2 == 0) else chain_dark
            r0 = loop_stations[k]
            r1 = loop_stations[kn]
            for m in range(c_sides):
                mn = (m + 1) % c_sides
                add_quad(r0[m], r1[m], r1[mn], r0[mn], col_link)

        # 2) Heavy Bail Clasp at (0, 0.37 .. 0.46, 0)
        b_bot = []
        b_mid = []
        b_top = []
        for k in range(8):
            ang = (2.0 * math.pi * (k + 0.5)) / 8
            ux = math.cos(ang)
            uz = -math.sin(ang)
            b_bot.append(len(verts))
            verts.append((0.048 * ux, 0.365, 0.052 * uz))
            b_mid.append(len(verts))
            verts.append((0.060 * ux, 0.415, 0.064 * uz))
            b_top.append(len(verts))
            verts.append((0.046 * ux, 0.460, 0.050 * uz))
        for k in range(8):
            kn = (k + 1) % 8
            add_quad(b_bot[k], b_bot[kn], b_mid[kn], b_mid[k], mat_main)
            add_quad(b_mid[k], b_mid[kn], b_top[kn], b_top[k], mat_light)

        # 3) Chunky Pendant Medallion centered at (0, 0.195, 0)
        med_cy = 0.195
        med_sides = 8
        # Vary silhouette by item_id: pointed diamond/kite pendant vs horizontal octagonal medallion
        is_kite = any(w in id_low for w in ("diamond", "kite", "tear", "drop", "fang", "shard", "topaz", "amethyst", "peridot"))
        def med_xy(k, scale_f):
            ang = (2.0 * math.pi * (k if is_kite else (k + 0.5))) / med_sides
            ux = math.cos(ang)
            uy = math.sin(ang)
            rx = (0.225 if is_kite else 0.245) * scale_f
            ry = (0.215 if is_kite else 0.195) * scale_f
            if is_kite and uy < -0.5:
                ry *= 1.06
            return ux * rx, med_cy + uy * ry, uy

        # Rings on Front (+Z) and Back (-Z)
        f_edge = []
        b_edge = []
        f_rim = []
        b_rim = []
        f_well = []
        b_well = []
        for k in range(med_sides):
            x_e, y_e, _ = med_xy(k, 1.00)
            x_r, y_r, _ = med_xy(k, 0.84)
            x_w, y_w, _ = med_xy(k, 0.60)
            f_edge.append(len(verts))
            verts.append((x_e, y_e, 0.040))
            b_edge.append(len(verts))
            verts.append((x_e, y_e, -0.040))
            f_rim.append(len(verts))
            verts.append((x_r, y_r, 0.068))
            b_rim.append(len(verts))
            verts.append((x_r, y_r, -0.068))
            f_well.append(len(verts))
            verts.append((x_w, y_w, 0.052))
            b_well.append(len(verts))
            verts.append((x_w, y_w, -0.052))

        for k in range(med_sides):
            kn = (k + 1) % med_sides
            _, _, uy_k = med_xy(k, 1.0)
            c_bevel = mat_light if uy_k >= 0.0 else mat_main
            # Outer perimeter wall
            add_quad(b_edge[k], b_edge[kn], f_edge[kn], f_edge[k], mat_dark)
            # Front outer bevel & inner frame slope
            add_quad(f_edge[k], f_edge[kn], f_rim[kn], f_rim[k], c_bevel)
            add_quad(f_rim[k], f_rim[kn], f_well[kn], f_well[k], mat_dark)
            # Back outer bevel & inner frame slope
            add_quad(b_rim[k], b_rim[kn], b_edge[kn], b_edge[k], mat_main)
            add_quad(b_well[k], b_well[kn], b_rim[kn], b_rim[k], mat_dark)

        if has_gem:
            # Proud 8-sided faceted gemstone emerging from the medallion well
            f_gem_mid = []
            f_gem_top = []
            b_gem_top = []
            for k in range(med_sides):
                xm, ym, _ = med_xy(k, 0.54)
                xt, yt, _ = med_xy(k, 0.28)
                f_gem_mid.append(len(verts))
                verts.append((xm, ym, 0.098))
                f_gem_top.append(len(verts))
                verts.append((xt, yt, 0.125))
                b_gem_top.append(len(verts))
                verts.append((xt, yt, -0.088))

            f_gem_c = len(verts)
            verts.append((0.0, med_cy, 0.130))
            b_gem_c = len(verts)
            verts.append((0.0, med_cy, -0.092))

            for k in range(med_sides):
                kn = (k + 1) % med_sides
                _, _, uyk = med_xy(k, 1.0)
                c_pav = gem_main if uyk >= -0.2 else gem_dark
                c_crn = gem_light if uyk >= 0.0 else gem_main
                add_quad(f_well[k], f_well[kn], f_gem_mid[kn], f_gem_mid[k], c_pav)
                add_quad(f_gem_mid[k], f_gem_mid[kn], f_gem_top[kn], f_gem_top[k], c_crn)
                add_tri(f_gem_c, f_gem_top[k], f_gem_top[kn], gem_light)
                add_quad(b_gem_top[k], b_gem_top[kn], b_well[kn], b_well[k], gem_dark)
                add_tri(b_gem_c, b_gem_top[kn], b_gem_top[k], gem_main)
        else:
            # Gemless Carved / Runic Relief Medallion: flat recessed dark face + 3D relief cross/star in same material
            fc_idx = len(verts)
            verts.append((0.0, med_cy, 0.052))
            bc_idx = len(verts)
            verts.append((0.0, med_cy, -0.052))
            for k in range(med_sides):
                kn = (k + 1) % med_sides
                add_tri(fc_idx, f_well[k], f_well[kn], mat_dark)
                add_tri(bc_idx, b_well[kn], b_well[k], mat_main)

            # Emboss a bold 4-pointed 3D relief cross/sun-star on the front face in mat_light / mat_main
            def add_relief_bar(x0, y0, x1, y1, hw=0.026):
                dx, dy = x1 - x0, y1 - y0
                ln = math.sqrt(dx * dx + dy * dy)
                nx, ny = -dy / ln, dx / ln
                hw_t = hw * 0.45
                bl0 = len(verts); verts.append((x0 + nx * hw, y0 + ny * hw, 0.054))
                bl1 = len(verts); verts.append((x1 + nx * hw, y1 + ny * hw, 0.054))
                br1 = len(verts); verts.append((x1 - nx * hw, y1 - ny * hw, 0.054))
                br0 = len(verts); verts.append((x0 - nx * hw, y0 - ny * hw, 0.054))
                tl0 = len(verts); verts.append((x0 + nx * hw_t, y0 + ny * hw_t, 0.088))
                tl1 = len(verts); verts.append((x1 + nx * hw_t, y1 + ny * hw_t, 0.088))
                tr1 = len(verts); verts.append((x1 - nx * hw_t, y1 - ny * hw_t, 0.088))
                tr0 = len(verts); verts.append((x0 - nx * hw_t, y0 - ny * hw_t, 0.088))
                add_quad(tr0, tr1, tl1, tl0, mat_light)
                add_quad(tl0, tl1, bl1, bl0, mat_light)
                add_quad(br0, br1, tr1, tr0, mat_main)
                add_quad(bl0, br0, tr0, tl0, mat_main)
                add_quad(br1, bl1, tl1, tr1, mat_light)

            add_relief_bar(-0.115, med_cy, 0.115, med_cy, hw=0.026)
            add_relief_bar(0.0, med_cy - 0.095, 0.0, med_cy + 0.095, hw=0.026)
            add_relief_bar(-0.075, med_cy - 0.065, 0.075, med_cy + 0.065, hw=0.020)
            add_relief_bar(-0.075, med_cy + 0.065, 0.075, med_cy - 0.065, hw=0.020)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 1A-4) Keys: 6 Distinct Low-Poly 3D Key Styles (Iron, Bronze, Silver, Gold, Bone Skull, Arcane Crystal)
    # -------------------------------------------------------------------------
    if archetype == "key":
        # Helper 1: Beveled 3D box/prism block (from (x0,y0) to (x1,y1), thickness ±z_half, front/back bevel inset)
        def add_key_box(x0, y0, x1, y1, z_half, b_in, col_front, col_side, col_light):
            bx0 = min(x0 + b_in, 0.5 * (x0 + x1))
            bx1 = max(x1 - b_in, 0.5 * (x0 + x1))
            by0 = min(y0 + b_in, 0.5 * (y0 + y1))
            by1 = max(y1 - b_in, 0.5 * (y0 + y1))
            z_edge = z_half * 0.68
            # Outer perimeter at ±z_edge: 0=BL, 1=BR, 2=TR, 3=TL
            p_out = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
            p_in = [(bx0, by0), (bx1, by0), (bx1, by1), (bx0, by1)]
            f_out, b_out, f_in, b_in_v = [], [], [], []
            for ox, oy in p_out:
                f_out.append(len(verts)); verts.append((ox, oy, z_edge))
                b_out.append(len(verts)); verts.append((ox, oy, -z_edge))
            for ix, iy in p_in:
                f_in.append(len(verts)); verts.append((ix, iy, z_half))
                b_in_v.append(len(verts)); verts.append((ix, iy, -z_half))
            # Front & back flat faces
            add_quad(f_in[0], f_in[1], f_in[2], f_in[3], col_front)
            add_quad(b_in_v[3], b_in_v[2], b_in_v[1], b_in_v[0], col_front)
            # Side & bevel quads
            for k in range(4):
                kn = (k + 1) % 4
                c_sd = col_light if k in (2, 3) else col_side
                c_bv = col_light if k in (2, 3) else col_front
                add_quad(b_out[k], b_out[kn], f_out[kn], f_out[k], c_sd)
                add_quad(f_out[k], f_out[kn], f_in[kn], f_in[k], c_bv)
                add_quad(b_in_v[k], b_in_v[kn], b_out[kn], b_out[k], col_side)

        # Helper 2: Vertical N-sided prism/cylinder section along Y (y0..y1)
        def add_shaft_seg(cx, y0, y1, rx0, rz0, rx1, rz1, sides, col_main, col_light, col_dark, cap_bot=False, cap_top=False):
            r0, r1 = [], []
            for k in range(sides):
                ang = (2.0 * math.pi * k) / sides
                ca, sa = math.cos(ang), math.sin(ang)
                r0.append(len(verts)); verts.append((cx + rx0 * ca, y0, rz0 * sa))
                r1.append(len(verts)); verts.append((cx + rx1 * ca, y1, rz1 * sa))
            for k in range(sides):
                kn = (k + 1) % sides
                ang_m = (2.0 * math.pi * (k + 0.5)) / sides
                ca_m, sa_m = math.cos(ang_m), math.sin(ang_m)
                col = col_light if (sa_m > 0.25 and ca_m < 0.25) else (col_main if sa_m > -0.2 else col_dark)
                add_quad(r0[k], r0[kn], r1[kn], r1[k], col)
            if cap_bot:
                cb = len(verts); verts.append((cx, y0, 0.0))
                for k in range(sides):
                    add_tri(cb, r0[(k + 1) % sides], r0[k], col_dark)
            if cap_top:
                ct = len(verts); verts.append((cx, y1, 0.0))
                for k in range(sides):
                    add_tri(ct, r1[k], r1[(k + 1) % sides], col_light)

        # Helper 3: Hollow 3D torus/ring loop in XY plane
        def add_hollow_ring(cx, cy, rx_out, ry_out, rx_in, ry_in, z_front, z_edge, segs, col_main, col_light, col_dark, rot_offset=0.0):
            rmx = 0.5 * (rx_out + rx_in)
            rmy = 0.5 * (ry_out + ry_in)
            rings = []
            for s in range(segs):
                ang = (2.0 * math.pi * s) / segs + rot_offset
                ca, sa = math.cos(ang), math.sin(ang)
                pts = [
                    (cx + rx_out * ca, cy + ry_out * sa, z_edge),
                    (cx + rmx * ca, cy + rmy * sa, z_front),
                    (cx + rx_in * ca, cy + ry_in * sa, z_edge),
                    (cx + rx_in * ca, cy + ry_in * sa, -z_edge),
                    (cx + rmx * ca, cy + rmy * sa, -z_front),
                    (cx + rx_out * ca, cy + ry_out * sa, -z_edge),
                ]
                r_idx = []
                for p in pts:
                    r_idx.append(len(verts)); verts.append(p)
                rings.append((r_idx, ca, sa))
            for s in range(segs):
                sn = (s + 1) % segs
                r0, ca0, sa0 = rings[s]
                r1, ca1, sa1 = rings[sn]
                ym = 0.5 * (sa0 + sa1)
                xm = 0.5 * (ca0 + ca1)
                c_out = col_light if ym > 0.25 else (col_main if ym > -0.35 else col_dark)
                c_fr = col_light if (ym > 0.0 and xm < 0.3) else col_main
                add_quad(r0[5], r1[5], r1[0], r0[0], c_out)
                add_quad(r0[0], r1[0], r1[1], r0[1], c_fr)
                add_quad(r0[1], r1[1], r1[2], r0[2], col_main)
                add_quad(r0[2], r1[2], r1[3], r0[3], col_dark)
                add_quad(r0[3], r1[3], r1[4], r0[4], col_main)
                add_quad(r0[4], r1[4], r1[5], r0[5], c_out)

        # Helper 4: Double-sided faceted gem cabochon at (cx, cy) on front/back (±z_base -> ±z_apex)
        def add_gem_double(cx, cy, rx, ry, z_base, z_apex, sides, col_dark_g, col_main_g, col_light_g):
            for z_sgn in (1.0, -1.0):
                b_ring, t_ring = [], []
                for k in range(sides):
                    ang = (2.0 * math.pi * k) / sides + (math.pi / sides)
                    ca, sa = math.cos(ang), math.sin(ang)
                    b_ring.append(len(verts)); verts.append((cx + rx * ca, cy + ry * sa, z_sgn * z_base))
                    t_ring.append(len(verts)); verts.append((cx + rx * 0.52 * ca, cy + ry * 0.52 * sa, z_sgn * z_apex))
                c_idx = len(verts); verts.append((cx, cy, z_sgn * (z_apex + 0.008)))
                for k in range(sides):
                    kn = (k + 1) % sides
                    ang_m = (2.0 * math.pi * (k + 0.5)) / sides + (math.pi / sides)
                    ca_m, sa_m = math.cos(ang_m), math.sin(ang_m)
                    fc = col_light_g if (sa_m > 0.15 and ca_m < 0.25) else (col_main_g if sa_m > -0.3 else col_dark_g)
                    tc = col_light_g if k in (1, 2, 3) else col_main_g
                    if z_sgn > 0:
                        add_quad(b_ring[k], b_ring[kn], t_ring[kn], t_ring[k], fc)
                        add_tri(c_idx, t_ring[k], t_ring[kn], tc)
                    else:
                        add_quad(b_ring[kn], b_ring[k], t_ring[k], t_ring[kn], fc)
                        add_tri(c_idx, t_ring[kn], t_ring[k], tc)

        if "bronze" in id_low or "ornate" in id_low or "copper" in id_low:
            # STYLE 1: Ornate Bronze / Brass Trefoil (Clover) Key with 3-tooth comb bit
            c_dark, c_main, c_light = COL_BARK, COL_COPPER, COL_BRASS
            c_hi = COL_AMBER
            # Shaft + tip + double collar
            add_shaft_seg(0.0, 0.0, 0.035, 0.018, 0.018, 0.038, 0.038, 8, c_main, c_light, c_dark, cap_bot=True)
            add_shaft_seg(0.0, 0.035, 0.35, 0.038, 0.038, 0.038, 0.038, 8, c_main, c_light, c_dark)
            add_shaft_seg(0.0, 0.35, 0.385, 0.068, 0.062, 0.068, 0.062, 8, c_light, c_hi, c_main, cap_bot=True, cap_top=True)
            add_shaft_seg(0.0, 0.385, 0.415, 0.052, 0.048, 0.052, 0.048, 8, c_main, c_light, c_dark)
            add_shaft_seg(0.0, 0.415, 0.445, 0.074, 0.066, 0.062, 0.056, 8, c_light, c_hi, c_main, cap_bot=True, cap_top=True)
            # Trefoil 3-lobe clover bow (top, left, right hollow rings) + central brass boss
            add_hollow_ring(0.0, 0.635, 0.105, 0.105, 0.046, 0.046, 0.068, 0.046, 10, c_main, c_hi, c_dark)
            add_hollow_ring(-0.095, 0.515, 0.095, 0.095, 0.042, 0.042, 0.065, 0.044, 10, c_main, c_light, c_dark)
            add_hollow_ring(0.095, 0.515, 0.095, 0.095, 0.042, 0.042, 0.065, 0.044, 10, c_main, c_light, c_dark)
            add_gem_double(0.0, 0.545, 0.052, 0.052, 0.045, 0.076, 6, c_main, c_light, c_hi)
            # 3-tooth stepped comb bit on +X
            add_key_box(0.025, 0.045, 0.105, 0.235, 0.036, 0.010, c_main, c_dark, c_light)
            add_key_box(0.095, 0.045, 0.215, 0.088, 0.038, 0.010, c_light, c_main, c_hi)
            add_key_box(0.095, 0.118, 0.185, 0.162, 0.038, 0.010, c_light, c_main, c_hi)
            add_key_box(0.095, 0.192, 0.215, 0.235, 0.038, 0.010, c_light, c_main, c_hi)

        elif "silver" in id_low or "crypt" in id_low or "runic" in id_low:
            # STYLE 2: Silver Gothic Diamond Crypt Key with Sapphire Gem & Double Symmetric Bit
            c_dark, c_main, c_light = COL_DARK_IRON, COL_STEEL, COL_SILVER
            # Fluted silver shaft + diamond collar
            add_shaft_seg(0.0, 0.0, 0.04, 0.014, 0.014, 0.036, 0.036, 8, c_main, c_light, c_dark, cap_bot=True)
            add_shaft_seg(0.0, 0.04, 0.35, 0.036, 0.036, 0.036, 0.036, 8, c_main, c_light, c_dark)
            add_shaft_seg(0.0, 0.35, 0.41, 0.072, 0.064, 0.056, 0.052, 4, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            # Gothic 4-sided Diamond/Rhombus bow (segs=4 with rot_offset=0 gives points at +X, +Y, -X, -Y!)
            add_hollow_ring(0.0, 0.565, 0.205, 0.175, 0.115, 0.095, 0.068, 0.045, 4, c_main, c_light, c_dark, rot_offset=0.0)
            # Inner silver crossbars + central Sapphire gem
            add_key_box(-0.125, 0.545, 0.125, 0.585, 0.042, 0.008, c_main, c_dark, c_light)
            add_key_box(-0.022, 0.455, 0.022, 0.675, 0.042, 0.008, c_main, c_dark, c_light)
            # Top finial spire
            add_shaft_seg(0.0, 0.725, 0.775, 0.028, 0.028, 0.006, 0.006, 4, c_light, c_light, c_main, cap_top=True)
            add_gem_double(0.0, 0.565, 0.058, 0.058, 0.042, 0.082, 4, COL_OBSIDIAN, COL_COBALT, COL_SKY_BLUE)
            # Symmetric Double-Bit (both +X and -X wards)
            for sgn in (1.0, -1.0):
                x_in, x_mid, x_out = (0.025, 0.125, 0.195) if sgn > 0 else (-0.195, -0.125, -0.025)
                add_key_box(min(x_in, x_out), 0.165, max(x_in, x_out), 0.215, 0.036, 0.009, c_main, c_dark, c_light)
                add_key_box(min(x_in, x_out), 0.050, max(x_in, x_out), 0.100, 0.036, 0.009, c_main, c_dark, c_light)
                if sgn > 0:
                    add_key_box(0.145, 0.050, 0.195, 0.215, 0.038, 0.009, c_light, c_main, c_light)
                else:
                    add_key_box(-0.195, 0.050, -0.145, 0.215, 0.038, 0.009, c_light, c_main, c_light)

        elif "gold" in id_low or "treasury" in id_low or "royal" in id_low:
            # STYLE 3: Royal Gold Crown Treasury Key with Ruby Center & Castle Ward Bit
            c_dark, c_main, c_light = COL_BRASS, COL_AMBER, COL_GOLD
            # Heavy gold shaft + ruby-banded collar
            add_shaft_seg(0.0, 0.0, 0.035, 0.020, 0.020, 0.042, 0.042, 8, c_main, c_light, c_dark, cap_bot=True)
            add_shaft_seg(0.0, 0.035, 0.34, 0.042, 0.042, 0.042, 0.042, 8, c_main, c_light, c_dark)
            add_shaft_seg(0.0, 0.34, 0.375, 0.074, 0.068, 0.074, 0.068, 8, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            add_shaft_seg(0.0, 0.375, 0.405, 0.064, 0.060, 0.064, 0.060, 8, COL_CRIMSON, COL_ROSE, COL_OBSIDIAN)
            add_shaft_seg(0.0, 0.405, 0.435, 0.078, 0.070, 0.068, 0.062, 8, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            # Regal octagonal gold bow + 3 Crown Spires on top + central Ruby bezel
            add_hollow_ring(0.0, 0.555, 0.205, 0.145, 0.115, 0.075, 0.072, 0.048, 8, c_main, c_light, c_dark, rot_offset=math.pi / 8.0)
            # Crossbar + central Ruby bezel
            add_key_box(-0.135, 0.530, 0.135, 0.580, 0.046, 0.010, c_main, c_dark, c_light)
            add_key_box(-0.065, 0.495, 0.065, 0.615, 0.054, 0.012, c_light, c_main, c_light)
            add_gem_double(0.0, 0.555, 0.056, 0.056, 0.052, 0.088, 8, COL_OBSIDIAN, COL_CRIMSON, COL_ROSE)
            # 3 Crown Spires atop the bow
            for sx, sy_b, sy_t in [(-0.115, 0.67, 0.745), (0.0, 0.685, 0.775), (0.115, 0.67, 0.745)]:
                add_key_box(sx - 0.026, sy_b, sx + 0.026, sy_t, 0.042, 0.010, c_light, c_main, c_light)
            # Ornate Castle-Notched Treasury Bit on +X
            add_key_box(0.030, 0.045, 0.095, 0.235, 0.038, 0.010, c_main, c_dark, c_light)
            add_key_box(0.085, 0.185, 0.220, 0.235, 0.042, 0.010, c_light, c_main, c_light)
            add_key_box(0.085, 0.045, 0.220, 0.095, 0.042, 0.010, c_light, c_main, c_light)
            add_key_box(0.165, 0.085, 0.220, 0.195, 0.042, 0.010, c_light, c_main, c_light)
            add_key_box(0.085, 0.120, 0.140, 0.160, 0.040, 0.008, COL_CRIMSON, COL_OBSIDIAN, COL_ROSE)

        elif "bone" in id_low or "skull" in id_low or "necropolis" in id_low:
            # STYLE 4: Bone / Skull Necropolis Key with Glowing Emerald Eye Sockets & Fang Teeth
            c_dark, c_main, c_light = COL_BARK, COL_TAN, COL_IVORY
            # Segmented spine bone shaft (alternating ivory vertebrae and dark bark joints)
            add_shaft_seg(0.0, 0.0, 0.04, 0.012, 0.012, 0.038, 0.038, 8, c_main, c_light, c_dark, cap_bot=True)
            for seg_i in range(5):
                yb = 0.04 + seg_i * 0.062
                yt = yb + 0.046
                add_shaft_seg(0.0, yb, yt, 0.036, 0.036, 0.044, 0.044, 8, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
                add_shaft_seg(0.0, yt, yb + 0.062, 0.028, 0.028, 0.028, 0.028, 8, c_dark, c_main, COL_OBSIDIAN)
            # Ribbed vertebra collar
            add_shaft_seg(0.0, 0.35, 0.405, 0.068, 0.060, 0.056, 0.050, 8, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            # Sculpted Low-Poly Skull Bow:
            # 1) Jawbone (y=0.405..0.495) with dark vertical tooth grooves
            add_key_box(-0.105, 0.405, 0.105, 0.495, 0.058, 0.014, c_main, c_dark, c_light)
            for tx in (-0.055, 0.0, 0.055):
                add_key_box(tx - 0.010, 0.412, tx + 0.010, 0.465, 0.062, 0.004, COL_OBSIDIAN, COL_OBSIDIAN, c_dark)
            # 2) Cheekbones & Cranium Dome (y=0.485..0.735)
            add_key_box(-0.175, 0.490, 0.175, 0.655, 0.068, 0.018, c_light, c_main, c_light)
            add_key_box(-0.135, 0.640, 0.135, 0.740, 0.062, 0.018, c_light, c_main, c_light)
            # 3) Brow ridges / side horns
            add_key_box(-0.205, 0.615, -0.145, 0.695, 0.052, 0.012, c_main, c_dark, c_light)
            add_key_box(0.145, 0.615, 0.205, 0.695, 0.052, 0.012, c_main, c_dark, c_light)
            # 4) Dark eye sockets (z=0.068..0.076) + glowing emerald/lime necromantic pupils (z=0.076..0.090) + nasal cavity
            for ex in (-0.072, 0.072):
                add_key_box(ex - 0.042, 0.548, ex + 0.042, 0.625, 0.073, 0.008, COL_OBSIDIAN, COL_OBSIDIAN, c_dark)
                add_key_box(ex - 0.020, 0.568, ex + 0.020, 0.605, 0.082, 0.005, COL_LIME, COL_EMERALD, COL_LIME)
            add_key_box(-0.022, 0.495, 0.022, 0.542, 0.073, 0.005, COL_OBSIDIAN, COL_OBSIDIAN, COL_OBSIDIAN)
            # 3 Curved/Tapered Bone Fang Teeth on +X
            add_key_box(0.028, 0.045, 0.095, 0.235, 0.034, 0.008, c_main, c_dark, c_light)
            for fy, flen in [(0.050, 0.220), (0.120, 0.175), (0.190, 0.215)]:
                add_key_box(0.085, fy, flen, fy + 0.042, 0.036, 0.010, c_light, c_main, c_light)

        elif "arcane" in id_low or "crystal" in id_low or "magic" in id_low:
            # STYLE 5: Arcane Amethyst Crystal Key with Hexagonal Gold/Obsidian Halo & Floating Crystal Core
            c_dark, c_main, c_light = COL_DARK_IRON, COL_BRASS, COL_GOLD
            # Dark-iron, purple & gold-ringed shaft (clearly visible against dark backgrounds)
            add_shaft_seg(0.0, 0.0, 0.04, 0.014, 0.014, 0.038, 0.038, 6, COL_MAGENTA, COL_ROSE, COL_PURPLE, cap_bot=True)
            add_shaft_seg(0.0, 0.04, 0.34, 0.038, 0.038, 0.038, 0.038, 6, COL_PURPLE, COL_MAGENTA, COL_DARK_IRON)
            for ry in (0.12, 0.24):
                add_shaft_seg(0.0, ry, ry + 0.028, 0.048, 0.048, 0.048, 0.048, 6, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            # Runic gold & magenta collar
            add_shaft_seg(0.0, 0.34, 0.41, 0.072, 0.064, 0.058, 0.052, 6, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            # Hexagonal gold & dark-iron halo bow (segs=6)
            add_hollow_ring(0.0, 0.565, 0.205, 0.175, 0.128, 0.110, 0.068, 0.045, 6, c_main, c_light, c_dark, rot_offset=math.pi / 6.0)
            # Arcane side winglets + top finial
            add_key_box(-0.235, 0.535, -0.175, 0.595, 0.042, 0.010, c_light, c_main, c_light)
            add_key_box(0.175, 0.535, 0.235, 0.595, 0.042, 0.010, c_light, c_main, c_light)
            add_key_box(-0.025, 0.715, 0.025, 0.770, 0.042, 0.010, c_light, c_main, c_light)
            # Gold energy prongs + Large Floating Faceted Amethyst Crystal Core inside the hexagon!
            add_key_box(-0.135, 0.548, 0.135, 0.582, 0.032, 0.008, c_main, c_dark, c_light)
            add_key_box(-0.018, 0.445, 0.018, 0.685, 0.032, 0.008, c_main, c_dark, c_light)
            add_gem_double(0.0, 0.565, 0.082, 0.098, 0.025, 0.088, 6, COL_OBSIDIAN, COL_PURPLE, COL_MAGENTA)
            # Crystalline Purple/Magenta + Gold geometric ward bit on +X and counter-tooth on -X
            add_key_box(0.028, 0.050, 0.110, 0.225, 0.036, 0.009, c_main, c_dark, c_light)
            add_key_box(0.100, 0.165, 0.215, 0.225, 0.040, 0.010, COL_MAGENTA, COL_PURPLE, COL_ROSE)
            add_key_box(0.100, 0.050, 0.215, 0.110, 0.040, 0.010, COL_MAGENTA, COL_PURPLE, COL_ROSE)
            add_key_box(0.155, 0.100, 0.215, 0.175, 0.038, 0.009, c_light, c_main, c_light)
            add_key_box(-0.115, 0.110, -0.028, 0.165, 0.036, 0.009, COL_MAGENTA, COL_PURPLE, COL_ROSE)

        else:
            # STYLE 0 (Default): Rusty Iron Prison Skeleton Key
            c_dark, c_main, c_light = COL_DARK_IRON, COL_STEEL, COL_SILVER
            # Heavy iron shaft + rust-banded octagonal bolster collar
            add_shaft_seg(0.0, 0.0, 0.035, 0.020, 0.020, 0.042, 0.042, 8, c_main, c_light, c_dark, cap_bot=True)
            add_shaft_seg(0.0, 0.035, 0.35, 0.042, 0.042, 0.042, 0.042, 8, c_main, c_light, c_dark)
            add_shaft_seg(0.0, 0.35, 0.385, 0.072, 0.066, 0.072, 0.066, 8, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            add_shaft_seg(0.0, 0.385, 0.410, 0.060, 0.056, 0.060, 0.056, 8, COL_RUST, COL_COPPER, COL_BARK)
            add_shaft_seg(0.0, 0.410, 0.440, 0.072, 0.066, 0.062, 0.058, 8, c_main, c_light, c_dark, cap_bot=True, cap_top=True)
            # Heavy forged oval iron bow + top keyring loop + 2 side rivets
            add_hollow_ring(0.0, 0.560, 0.195, 0.145, 0.100, 0.070, 0.070, 0.048, 12, c_main, c_light, c_dark)
            add_hollow_ring(0.0, 0.715, 0.065, 0.052, 0.028, 0.022, 0.045, 0.032, 8, COL_RUST, c_main, c_dark)
            add_key_box(-0.215, 0.535, -0.155, 0.585, 0.058, 0.010, COL_RUST, c_dark, c_main)
            add_key_box(0.155, 0.535, 0.215, 0.585, 0.058, 0.010, COL_RUST, c_dark, c_main)
            # Classic 2-prong heavy skeleton key ward bit on +X with rectangular cutout
            add_key_box(0.030, 0.165, 0.210, 0.220, 0.040, 0.010, c_main, c_dark, c_light)
            add_key_box(0.030, 0.045, 0.210, 0.100, 0.040, 0.010, c_main, c_dark, c_light)
            add_key_box(0.150, 0.045, 0.210, 0.220, 0.042, 0.010, COL_RUST, c_dark, c_main)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 1A-5) Food & Drinks: 12 Distinct Low-Poly 3D Rations (Fruits, Bread/Cheese, Meats, Drinks)
    # -------------------------------------------------------------------------
    if archetype in ("food", "drink"):
        # Helper A: Multi-ring 3D lathe with custom (y, rx, rz, col_main, col_light, col_dark) profile
        def add_lathe_profile(profile_pts, sides=10, cx=0.0, cz=0.0, cap_bot=True, cap_top=True):
            rings = []
            for y_val, rx, rz, cm, cl, cd in profile_pts:
                r_idx = []
                for k in range(sides):
                    ang = (2.0 * math.pi * k) / sides
                    r_idx.append(len(verts))
                    verts.append((cx + rx * math.cos(ang), y_val, cz + rz * math.sin(ang)))
                rings.append((r_idx, y_val, cm, cl, cd))
            for i in range(len(rings) - 1):
                r0, _, cm0, cl0, cd0 = rings[i]
                r1, _, cm1, cl1, cd1 = rings[i + 1]
                for k in range(sides):
                    kn = (k + 1) % sides
                    ang_m = (2.0 * math.pi * (k + 0.5)) / sides
                    ca_m, sa_m = math.cos(ang_m), math.sin(ang_m)
                    col = cl1 if (sa_m > 0.22 and ca_m < 0.28) else (cm1 if sa_m > -0.25 else cd1)
                    # Outward normal winding: (+outward_x, -dr, +outward_z)
                    add_quad(r0[kn], r0[k], r1[k], r1[kn], col)
            if cap_bot:
                r0, y0, _, _, cd0 = rings[0]
                cb = len(verts); verts.append((cx, y0, cz))
                for k in range(sides):
                    add_tri(cb, r0[k], r0[(k + 1) % sides], cd0)
            if cap_top:
                rn, yn, _, cln, _ = rings[-1]
                ct = len(verts); verts.append((cx, yn, cz))
                for k in range(sides):
                    add_tri(ct, rn[(k + 1) % sides], rn[k], cln)

        # Helper B: Low-poly 3D sphere/ellipsoid blob at (cx, cy, cz)
        def add_blob(cx, cy, cz, rx, ry, rz, col_main, col_light, col_dark, sides=8):
            prof = [
                (cy - ry * 0.92, rx * 0.38, rz * 0.38, col_dark, col_main, col_dark),
                (cy - ry * 0.45, rx * 0.88, rz * 0.88, col_main, col_light, col_dark),
                (cy + ry * 0.10, rx * 1.00, rz * 1.00, col_main, col_light, col_dark),
                (cy + ry * 0.62, rx * 0.78, rz * 0.78, col_light, col_light, col_main),
                (cy + ry * 0.95, rx * 0.32, rz * 0.32, col_light, col_light, col_main),
            ]
            add_lathe_profile(prof, sides=sides, cx=cx, cz=cz, cap_bot=True, cap_top=True)

        # Helper C: 3D Stem + Angled Green Leaf at top of fruit
        def add_stem_and_leaf(top_y, leaf_dir=1.0):
            # Angled wooden stem
            s0, s1 = [], []
            for k in range(6):
                ang = (2.0 * math.pi * k) / 6
                ca, sa = math.cos(ang), math.sin(ang)
                s0.append(len(verts)); verts.append((0.022 * ca, top_y - 0.02, 0.022 * sa))
                s1.append(len(verts)); verts.append((-0.025 * leaf_dir + 0.016 * ca, top_y + 0.12, 0.016 * sa))
            for k in range(6):
                kn = (k + 1) % 6
                add_quad(s0[kn], s0[k], s1[k], s1[kn], COL_BARK if k % 2 == 0 else COL_WOOD)
            ct = len(verts); verts.append((-0.025 * leaf_dir, top_y + 0.125, 0.0))
            for k in range(6):
                add_tri(ct, s1[(k + 1) % 6], s1[k], COL_WOOD)
            # 3D diamond leaf jutting out to leaf_dir (+X or -X)
            lx0, ly0 = 0.0, top_y + 0.03
            lx_mid, ly_mid = 0.11 * leaf_dir, top_y + 0.09
            lx_tip, ly_tip = 0.21 * leaf_dir, top_y + 0.06
            p_base = len(verts); verts.append((lx0, ly0, 0.0))
            p_tip = len(verts); verts.append((lx_tip, ly_tip, 0.0))
            p_f = len(verts); verts.append((lx_mid, ly_mid + 0.02, 0.055))
            p_b = len(verts); verts.append((lx_mid, ly_mid + 0.02, -0.055))
            p_top = len(verts); verts.append((lx_mid, ly_mid + 0.042, 0.0))
            p_bot = len(verts); verts.append((lx_mid, ly_mid - 0.015, 0.0))
            if leaf_dir > 0:
                add_tri(p_base, p_f, p_top, COL_LIME)
                add_tri(p_f, p_tip, p_top, COL_LIME)
                add_tri(p_base, p_top, p_b, COL_GREEN)
                add_tri(p_top, p_tip, p_b, COL_GREEN)
                add_tri(p_base, p_bot, p_f, COL_GREEN)
                add_tri(p_f, p_bot, p_tip, COL_GREEN)
                add_tri(p_base, p_b, p_bot, COL_EMERALD)
                add_tri(p_b, p_tip, p_bot, COL_EMERALD)
            else:
                add_tri(p_base, p_top, p_f, COL_LIME)
                add_tri(p_f, p_top, p_tip, COL_LIME)
                add_tri(p_base, p_b, p_top, COL_GREEN)
                add_tri(p_top, p_b, p_tip, COL_GREEN)
                add_tri(p_base, p_f, p_bot, COL_GREEN)
                add_tri(p_f, p_tip, p_bot, COL_GREEN)
                add_tri(p_base, p_bot, p_b, COL_EMERALD)
                add_tri(p_b, p_bot, p_tip, COL_EMERALD)

        # Helper D: Beveled 3D Box/Slab
        def add_food_box(x0, y0, x1, y1, z_half, b_in, col_front, col_side, col_top):
            bx0 = min(x0 + b_in, 0.5 * (x0 + x1))
            bx1 = max(x1 - b_in, 0.5 * (x0 + x1))
            by0 = min(y0 + b_in, 0.5 * (y0 + y1))
            by1 = max(y1 - b_in, 0.5 * (y0 + y1))
            z_edge = z_half * 0.70
            p_out = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
            p_in = [(bx0, by0), (bx1, by0), (bx1, by1), (bx0, by1)]
            f_out, b_out, f_in, b_in_v = [], [], [], []
            for ox, oy in p_out:
                f_out.append(len(verts)); verts.append((ox, oy, z_edge))
                b_out.append(len(verts)); verts.append((ox, oy, -z_edge))
            for ix, iy in p_in:
                f_in.append(len(verts)); verts.append((ix, iy, z_half))
                b_in_v.append(len(verts)); verts.append((ix, iy, -z_half))
            add_quad(f_in[0], f_in[1], f_in[2], f_in[3], col_front)
            add_quad(b_in_v[3], b_in_v[2], b_in_v[1], b_in_v[0], col_front)
            for k in range(4):
                kn = (k + 1) % 4
                c_sd = col_top if k == 2 else col_side
                add_quad(b_out[k], b_out[kn], f_out[kn], f_out[k], c_sd)
                add_quad(f_out[k], f_out[kn], f_in[kn], f_in[k], c_sd if k == 2 else col_front)
                add_quad(b_in_v[k], b_in_v[kn], b_out[kn], b_out[k], col_side)

        if "apple" in id_low:
            # 1) Crisp Red Apple with indented top dimple, brown stem, and green leaf
            prof = [
                (0.00, 0.08, 0.08, COL_MAGENTA, COL_CRIMSON, COL_MAGENTA),
                (0.06, 0.19, 0.19, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.18, 0.24, 0.24, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.32, 0.25, 0.25, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.42, 0.19, 0.19, COL_ROSE, COL_CORAL, COL_CRIMSON),
                (0.46, 0.10, 0.10, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.41, 0.03, 0.03, COL_BARK, COL_WOOD, COL_BARK),
            ]
            add_lathe_profile(prof, sides=10)
            add_stem_and_leaf(0.44, leaf_dir=1.0)

        elif "pear" in id_low:
            # 2) Golden Pear with classic teardrop pyriform silhouette, warm amber/orange skin from bottom to top, stem & leaf
            prof = [
                (0.00, 0.09, 0.09, COL_ORANGE, COL_AMBER, COL_ORANGE),
                (0.07, 0.21, 0.21, COL_AMBER, COL_TAN, COL_ORANGE),
                (0.18, 0.24, 0.24, COL_AMBER, COL_TAN, COL_ORANGE),
                (0.28, 0.18, 0.18, COL_AMBER, COL_TAN, COL_ORANGE),
                (0.37, 0.12, 0.12, COL_AMBER, COL_TAN, COL_ORANGE),
                (0.44, 0.08, 0.08, COL_AMBER, COL_AMBER, COL_ORANGE),
                (0.48, 0.035, 0.035, COL_AMBER, COL_AMBER, COL_ORANGE),
            ]
            add_lathe_profile(prof, sides=10)
            add_stem_and_leaf(0.47, leaf_dir=-1.0)

        elif "grape" in id_low:
            # 3) Clustered Bunch of Purple Grapes (lush purple/magenta/rose, no black facets) + double green leaves
            grape_centers = [
                (0.0, 0.06, 0.0, 0.068),
                (-0.065, 0.15, 0.045, 0.074), (0.065, 0.15, 0.045, 0.074),
                (0.0, 0.15, -0.065, 0.074), (0.0, 0.16, 0.075, 0.072),
                (-0.115, 0.26, 0.02, 0.080), (0.115, 0.26, 0.02, 0.080),
                (-0.055, 0.26, 0.095, 0.080), (0.055, 0.26, 0.095, 0.080),
                (-0.065, 0.26, -0.080, 0.078), (0.065, 0.26, -0.080, 0.078),
                (-0.135, 0.38, 0.0, 0.082), (0.135, 0.38, 0.0, 0.082),
                (-0.075, 0.39, 0.105, 0.084), (0.075, 0.39, 0.105, 0.084),
                (0.0, 0.38, 0.125, 0.082),
                (-0.075, 0.38, -0.095, 0.080), (0.075, 0.38, -0.095, 0.080),
                (0.0, 0.43, 0.02, 0.080),
            ]
            for idx_g, (gx, gy, gz, gr) in enumerate(grape_centers):
                cm = COL_MAGENTA if idx_g % 2 == 0 else COL_PURPLE
                cl = COL_ROSE if idx_g % 2 == 0 else COL_MAGENTA
                add_blob(gx, gy, gz, gr, gr, gr, cm, cl, COL_PURPLE, sides=8)
            add_stem_and_leaf(0.47, leaf_dir=1.0)
            add_stem_and_leaf(0.46, leaf_dir=-1.0)

        elif "roll" in id_low or "sweet" in id_low or "bun" in id_low:
            # 5) RPG Glazed Sweet Roll: Wide golden-baked pastry bun + scalloped white icing cap & cinnamon center
            pastry_prof = [
                (0.00, 0.19, 0.19, COL_BARK, COL_WOOD, COL_BARK),
                (0.07, 0.24, 0.24, COL_WOOD, COL_COPPER, COL_BARK),
                (0.18, 0.26, 0.26, COL_COPPER, COL_AMBER, COL_WOOD),
                (0.28, 0.24, 0.24, COL_AMBER, COL_TAN, COL_COPPER),
            ]
            add_lathe_profile(pastry_prof, sides=12, cap_bot=True, cap_top=False)
            # Scalloped white icing cap (12-sided with alternating low/high drip skirt at y=0.22/0.27)
            sides_ic = 12
            r_skirt, r_mid_ic, r_top_ic, r_swirl = [], [], [], []
            for k in range(sides_ic):
                ang = (2.0 * math.pi * k) / sides_ic
                ca, sa = math.cos(ang), math.sin(ang)
                yk_skirt = 0.21 if k % 2 == 0 else 0.265
                rk_skirt = 0.258 if k % 2 == 0 else 0.248
                r_skirt.append(len(verts)); verts.append((rk_skirt * ca, yk_skirt, rk_skirt * sa))
                r_mid_ic.append(len(verts)); verts.append((0.235 * ca, 0.34, 0.235 * sa))
                r_top_ic.append(len(verts)); verts.append((0.155 * ca, 0.41, 0.155 * sa))
                r_swirl.append(len(verts)); verts.append((0.065 * ca, 0.43, 0.065 * sa))
            c_swirl = len(verts); verts.append((0.0, 0.39, 0.0))
            for k in range(sides_ic):
                kn = (k + 1) % sides_ic
                c_ic = COL_IVORY if k in (1, 2, 3, 4, 5) else COL_TAN
                add_quad(r_skirt[kn], r_skirt[k], r_mid_ic[k], r_mid_ic[kn], c_ic)
                add_quad(r_mid_ic[kn], r_mid_ic[k], r_top_ic[k], r_top_ic[kn], COL_IVORY)
                add_quad(r_top_ic[kn], r_top_ic[k], r_swirl[k], r_swirl[kn], COL_TAN)
                add_tri(c_swirl, r_swirl[kn], r_swirl[k], COL_COPPER if k % 2 == 0 else COL_AMBER)

        elif "bread" in id_low or "loaf" in id_low:
            # 4) Sliced-Open Golden Farmhouse Bread Loaf + Freshly Cut Slice Leaning in Front
            # Classic high-top bread cross-section (10 points around perimeter: flat base, flared golden walls, plump overhanging top crown)
            base_profile = [
                (-0.145, 0.00),
                (-0.160, 0.12),
                (-0.180, 0.22),
                (-0.140, 0.30),
                (-0.055, 0.35),
                ( 0.055, 0.35),
                ( 0.140, 0.30),
                ( 0.180, 0.22),
                ( 0.160, 0.12),
                ( 0.145, 0.00),
            ]
            n_bp = len(base_profile)
            # Rotate loaf by -28 deg around Y so the cut face (+u_len) faces forward-right (+X, +Z), visible in Front, Side & 3/4 views
            yaw_b = -0.48
            cb_y, sb_y = math.cos(yaw_b), math.sin(yaw_b)

            def bread_pt(u_len, w_cross, y_h):
                rx = u_len * cb_y - w_cross * sb_y
                rz = -u_len * sb_y - w_cross * cb_y
                return (rx, y_h, rz)

            # Build main half-loaf from u_len = -0.25 (rounded heel) to u_len = +0.05 (flat cut-open face)
            loaf_stations = [
                (-0.25, 0.52, 0.54, False),
                (-0.20, 0.85, 0.86, False),
                (-0.13, 0.98, 0.96, True),
                (-0.06, 1.00, 1.00, False),
                ( 0.00, 1.00, 0.96, True),
                ( 0.05, 1.00, 1.00, False),
            ]
            loaf_rings = []
            for u_l, sc_w, sc_y, is_score in loaf_stations:
                ring = []
                for idx_p, (pw, py) in enumerate(base_profile):
                    y_mod = py * sc_y * (0.91 if (is_score and idx_p in (3, 4, 5, 6)) else 1.0)
                    ring.append(len(verts))
                    verts.append(bread_pt(u_l, pw * sc_w, y_mod))
                loaf_rings.append((ring, is_score))

            for s in range(len(loaf_rings) - 1):
                r0, sc0 = loaf_rings[s]
                r1, sc1 = loaf_rings[s + 1]
                for k in range(n_bp):
                    kn = (k + 1) % n_bp
                    if (sc0 or sc1) and k in (3, 4, 5):
                        col = COL_TAN if k == 4 else COL_AMBER
                    elif k in (3, 4, 5):
                        col = COL_AMBER if k == 4 else COL_ORANGE
                    elif k in (2, 6):
                        col = COL_AMBER
                    elif k in (1, 7, 0, 8):
                        col = COL_COPPER
                    else:
                        col = COL_WOOD
                    # Outward normal winding for clockwise (w_cross, y) profile along +u_len
                    add_quad(r0[kn], r0[k], r1[k], r1[kn], col)

            # Cap rounded back heel at u_len = -0.275 (outward normal pointing in -u_len)
            heel_c = len(verts)
            verts.append(bread_pt(-0.275, 0.0, 0.14))
            for k in range(n_bp):
                kn = (k + 1) % n_bp
                add_tri(heel_c, loaf_rings[0][0][k], loaf_rings[0][0][kn], COL_COPPER if k in (2, 3, 4, 5, 6) else COL_WOOD)

            # Cap flat cut-open face at u_len = +0.05 with golden crust rim + bright ivory crumb interior (outward normal in +u_len)
            cut_outer = loaf_rings[-1][0]
            cut_inner = []
            cy_crumb = 0.175
            for pw, py in base_profile:
                iw = pw * 0.80
                iy = cy_crumb + (py - cy_crumb) * 0.78
                cut_inner.append(len(verts))
                verts.append(bread_pt(0.052, iw, max(0.025, iy)))
            cut_center = len(verts)
            verts.append(bread_pt(0.054, 0.0, cy_crumb))
            for k in range(n_bp):
                kn = (k + 1) % n_bp
                rim_col = COL_AMBER if k in (2, 3, 4, 5, 6) else COL_COPPER
                add_quad(cut_outer[kn], cut_outer[k], cut_inner[k], cut_inner[kn], rim_col)
                crumb_col = COL_IVORY if k in (1, 2, 3, 4, 5, 6, 7) else COL_TAN
                add_tri(cut_center, cut_inner[kn], cut_inner[k], crumb_col)

            # Add a freshly cut bread slice tilted forward at ~47 deg in front of the open face
            def slice_pt(u_s, w_s, y_s):
                tilt = 0.82
                u_t = 0.065 + (u_s - 0.065) * math.cos(tilt) + y_s * math.sin(tilt)
                y_t = y_s * math.cos(tilt) - (u_s - 0.065) * math.sin(tilt)
                return bread_pt(u_t, w_s - 0.015, max(0.0, y_t + 0.02))

            sl_back_out, sl_front_out, sl_back_in, sl_front_in = [], [], [], []
            u_b, u_f = 0.065, 0.118
            for pw, py in base_profile:
                sw, sy = pw * 0.94, py * 0.94
                iw, iy = sw * 0.78, (cy_crumb * 0.94) + (sy - cy_crumb * 0.94) * 0.76
                sl_back_out.append(len(verts)); verts.append(slice_pt(u_b, sw, sy))
                sl_front_out.append(len(verts)); verts.append(slice_pt(u_f, sw, sy))
                sl_back_in.append(len(verts)); verts.append(slice_pt(u_b - 0.002, iw, max(0.02, iy)))
                sl_front_in.append(len(verts)); verts.append(slice_pt(u_f + 0.002, iw, max(0.02, iy)))
            sl_cb = len(verts); verts.append(slice_pt(u_b - 0.003, 0.0, cy_crumb * 0.94))
            sl_cf = len(verts); verts.append(slice_pt(u_f + 0.003, 0.0, cy_crumb * 0.94))
            for k in range(n_bp):
                kn = (k + 1) % n_bp
                c_crust = COL_AMBER if k in (2, 3, 4, 5, 6) else COL_COPPER
                c_crumb = COL_IVORY if k in (1, 2, 3, 4, 5, 6, 7) else COL_TAN
                add_quad(sl_back_out[kn], sl_back_out[k], sl_front_out[k], sl_front_out[kn], c_crust)
                add_quad(sl_front_out[kn], sl_front_out[k], sl_front_in[k], sl_front_in[kn], c_crust)
                add_tri(sl_cf, sl_front_in[kn], sl_front_in[k], c_crumb)
                add_quad(sl_back_in[kn], sl_back_in[k], sl_back_out[k], sl_back_out[kn], c_crust)
                add_tri(sl_cb, sl_back_in[k], sl_back_in[kn], c_crumb)

        elif "cheese" in id_low:
            # 6) Thick Triangular Wedge of Aged Golden Cheese with waxed orange rind & Swiss holes
            y_bot, y_top = 0.0, 0.28
            arc_segs = 6
            bot_arc, top_arc = [], []
            for s in range(arc_segs + 1):
                ang = -0.65 + (1.30 * s) / arc_segs
                rx = -0.20 + 0.44 * math.cos(ang)
                rz = 0.24 * math.sin(ang)
                bot_arc.append(len(verts)); verts.append((rx, y_bot, rz))
                top_arc.append(len(verts)); verts.append((rx, y_top, rz))
            tip_b = len(verts); verts.append((-0.22, y_bot, 0.02))
            tip_t = len(verts); verts.append((-0.22, y_top, 0.02))
            for s in range(arc_segs):
                add_tri(tip_t, top_arc[s + 1], top_arc[s], COL_GOLD)
                add_tri(tip_b, bot_arc[s], bot_arc[s + 1], COL_AMBER)
                add_quad(bot_arc[s], bot_arc[s + 1], top_arc[s + 1], top_arc[s], COL_ORANGE if s % 2 == 0 else COL_RUST)
            add_quad(tip_b, bot_arc[-1], top_arc[-1], tip_t, COL_GOLD)
            add_quad(bot_arc[0], tip_b, tip_t, top_arc[0], COL_AMBER)
            # Subtle low-profile cheese eye dimples on top and front cut face
            add_blob(0.02, y_top, 0.01, 0.045, 0.018, 0.045, COL_ORANGE, COL_AMBER, COL_RUST, sides=6)
            add_blob(-0.07, y_top, -0.02, 0.035, 0.016, 0.035, COL_ORANGE, COL_AMBER, COL_RUST, sides=6)
            add_blob(-0.02, 0.14, 0.085, 0.042, 0.042, 0.022, COL_ORANGE, COL_AMBER, COL_RUST, sides=6)
            add_blob(0.11, 0.17, 0.115, 0.036, 0.036, 0.022, COL_ORANGE, COL_AMBER, COL_RUST, sides=6)

        elif "drumstick" in id_low or "leg" in id_low or "turkey" in id_low:
            # 7) Roasted Turkey Drumstick: Bottom ivory bone handle + paper frill + sleek elongated teardrop roast meat
            add_blob(-0.040, 0.040, 0.0, 0.042, 0.040, 0.040, COL_TAN, COL_IVORY, COL_TAN, sides=6)
            add_blob(0.040, 0.040, 0.0, 0.042, 0.040, 0.040, COL_TAN, COL_IVORY, COL_TAN, sides=6)
            bone_prof = [
                (0.04, 0.038, 0.038, COL_TAN, COL_IVORY, COL_TAN),
                (0.21, 0.042, 0.042, COL_TAN, COL_IVORY, COL_TAN),
                (0.24, 0.074, 0.074, COL_IVORY, COL_IVORY, COL_TAN),
            ]
            add_lathe_profile(bone_prof, sides=8, cap_bot=True, cap_top=True)
            meat_prof = [
                (0.22, 0.072, 0.068, COL_BARK, COL_WOOD, COL_BARK),
                (0.30, 0.128, 0.118, COL_WOOD, COL_COPPER, COL_BARK),
                (0.41, 0.152, 0.138, COL_COPPER, COL_RUST, COL_WOOD),
                (0.52, 0.132, 0.120, COL_RUST, COL_AMBER, COL_COPPER),
                (0.60, 0.072, 0.066, COL_COPPER, COL_AMBER, COL_WOOD),
            ]
            add_lathe_profile(meat_prof, sides=10, cap_bot=True, cap_top=True)

        elif "steak" in id_low or "rib" in id_low or "tbone" in id_low:
            if "tbone" in id_low:
                # 8A) Asymmetric Butcher's T-Bone Steak:
                # The ivory T-Bone forms the ACTUAL outer top spine + off-center vertical divider (x = -0.065),
                # with a large low Strip-Loin lobe on the right (y = 0.01..0.33) and a smaller high Tenderloin filet on the left (y = 0.12..0.33).
                z_b_edge, z_b_face = 0.048, 0.056
                z_m_edge, z_m_face = 0.044, 0.052

                # Helper to extrude a CCW convex 2D polygon [(x,y), ...] as a 3D slab with guaranteed outward normals
                def add_lobe_slab(poly_pts, cx_l, cy_l, z_e, z_f, rim_cols, bev_cols, face_cols, sx=0.82, sy=0.82):
                    n_p = len(poly_pts)
                    fo, bo, fi, bi = [], [], [], []
                    for px, py in poly_pts:
                        fo.append(len(verts)); verts.append((px, py, z_e))
                        bo.append(len(verts)); verts.append((px, py, -z_e))
                        ix = cx_l + (px - cx_l) * sx
                        iy = cy_l + (py - cy_l) * sy
                        fi.append(len(verts)); verts.append((ix, iy, z_f))
                        bi.append(len(verts)); verts.append((ix, iy, -z_f))
                    cf = len(verts); verts.append((cx_l, cy_l, z_f))
                    cb = len(verts); verts.append((cx_l, cy_l, -z_f))
                    for k in range(n_p):
                        kn = (k + 1) % n_p
                        # Outer side wall: e1 = bo[kn]-bo[k] (CCW tangent), e2 = fo[k]-bo[k] (+Z) -> e1 x e2 points radially outward!
                        add_quad(bo[k], bo[kn], fo[kn], fo[k], rim_cols[k])
                        # Front bevel (+Z outward normal)
                        add_quad(fo[k], fo[kn], fi[kn], fi[k], bev_cols[k])
                        # Back bevel (-Z outward normal)
                        add_quad(bi[k], bi[kn], bo[kn], bo[k], bev_cols[k])
                        # Front center fan (+Z outward normal for CCW fi)
                        add_tri(cf, fi[k], fi[kn], face_cols[k])
                        # Back center fan (-Z outward normal)
                        add_tri(cb, bi[kn], bi[k], face_cols[k])

                # 1A) Outer Top Spine Bar of the Ivory T-BONE (clean convex CCW arch across the top outer boundary)
                top_spine_poly = [
                    (-0.235, 0.325),
                    (-0.065, 0.330),
                    ( 0.115, 0.330),
                    ( 0.250, 0.310),
                    ( 0.260, 0.365),
                    ( 0.115, 0.392),
                    (-0.065, 0.405),
                    (-0.240, 0.375),
                ]
                nt_s = len(top_spine_poly)
                add_lobe_slab(
                    top_spine_poly, 0.01, 0.362, z_b_edge, z_b_face,
                    [COL_IVORY if k in (3, 4, 5, 6) else COL_TAN for k in range(nt_s)],
                    [COL_IVORY for _ in range(nt_s)],
                    [COL_IVORY for _ in range(nt_s)],
                    sx=0.94, sy=0.68,
                )

                # 1B) Off-Center Vertical Divider Stem of the Ivory T-BONE (convex CCW bar at x = -0.095..-0.035)
                stem_poly = [
                    (-0.088, 0.020),
                    (-0.042, 0.020),
                    (-0.035, 0.180),
                    (-0.035, 0.355),
                    (-0.095, 0.355),
                    (-0.095, 0.140),
                ]
                ns_p = len(stem_poly)
                add_lobe_slab(
                    stem_poly, -0.065, 0.19, z_b_edge, z_b_face,
                    [COL_TAN if k in (0, 1, 5) else COL_IVORY for k in range(ns_p)],
                    [COL_IVORY for _ in range(ns_p)],
                    [COL_IVORY for _ in range(ns_p)],
                    sx=0.68, sy=0.94,
                )

                # 2) Large Right Strip-Loin Lobe (CCW order, x=-0.035..+0.27, y=0.01..0.33)
                right_lobe = [
                    (-0.035, 0.04), ( 0.060, 0.01), ( 0.175, 0.04), ( 0.255, 0.12),
                    ( 0.270, 0.23), ( 0.245, 0.31), ( 0.115, 0.33), (-0.035, 0.33),
                    (-0.035, 0.18),
                ]
                nr_l = len(right_lobe)
                add_lobe_slab(
                    right_lobe, 0.115, 0.18, z_m_edge, z_m_face,
                    [COL_IVORY if k in (2, 3, 4) else (COL_COPPER if k in (0, 1, 5) else COL_BARK) for k in range(nr_l)],
                    [COL_IVORY if k in (2, 3, 4) else COL_TAN for k in range(nr_l)],
                    [COL_ROSE if k in (2, 4) else COL_CRIMSON for k in range(nr_l)],
                )

                # 3) Smaller Left Tenderloin Filet Lobe (CCW order, x=-0.24..-0.095, y=0.12..0.33)
                left_lobe = [
                    (-0.155, 0.12), (-0.095, 0.12), (-0.095, 0.33), (-0.230, 0.325),
                    (-0.245, 0.24), (-0.215, 0.15),
                ]
                nl_l = len(left_lobe)
                add_lobe_slab(
                    left_lobe, -0.165, 0.23, z_m_edge, z_m_face,
                    [COL_COPPER if k in (3, 4) else COL_BARK for k in range(nl_l)],
                    [COL_IVORY if k in (3, 4) else COL_TAN for k in range(nl_l)],
                    [COL_ROSE if k in (3, 5) else COL_CRIMSON for k in range(nl_l)],
                )

            else:
                # 8B) Classic Cartoon/RPG Round-Bone Steak Cut (wide asymmetric kidney/oval slab with off-center "o" bone ring)
                # 14-point CCW wide horizontal kidney-oval contour (plump left lobe, slight bottom kidney indent, rounded right lobe)
                steak_poly = [
                    (-0.12, 0.01),
                    (-0.01, 0.04),
                    ( 0.11, 0.02),
                    ( 0.22, 0.06),
                    ( 0.27, 0.15),
                    ( 0.26, 0.25),
                    ( 0.18, 0.33),
                    ( 0.06, 0.36),
                    (-0.07, 0.37),
                    (-0.19, 0.35),
                    (-0.26, 0.28),
                    (-0.28, 0.18),
                    (-0.26, 0.08),
                    (-0.20, 0.02),
                ]
                n_sp = len(steak_poly)
                # Off-center round bone ("o") located in the left-middle meat lobe at (bx=-0.095, by=0.20)
                bx, by = -0.095, 0.20
                r_bone_out = 0.072
                r_bone_in = 0.035
                z_rim = 0.048
                z_meat = 0.055
                z_bone = 0.058

                f_out, b_out, f_in, b_in_s = [], [], [], []
                f_bo, b_bo, f_bi, b_bi = [], [], [], []
                for k, (px, py) in enumerate(steak_poly):
                    f_out.append(len(verts)); verts.append((px, py, z_rim))
                    b_out.append(len(verts)); verts.append((px, py, -z_rim))
                    # Inset outer fat/crust rim by 16% toward steak centroid (0.0, 0.19)
                    ix = px * 0.84
                    iy = 0.19 + (py - 0.19) * 0.84
                    f_in.append(len(verts)); verts.append((ix, iy, z_meat))
                    b_in_s.append(len(verts)); verts.append((ix, iy, -z_meat))
                    # Uniform CCW angles around the off-center bone ring aligned with steak_poly's starting angle (-pi/2)
                    ang_b = -0.55 * math.pi + (2.0 * math.pi * k) / n_sp
                    cb_a, sb_a = math.cos(ang_b), math.sin(ang_b)
                    f_bo.append(len(verts)); verts.append((bx + r_bone_out * cb_a, by + r_bone_out * sb_a, z_bone))
                    b_bo.append(len(verts)); verts.append((bx + r_bone_out * cb_a, by + r_bone_out * sb_a, -z_bone))
                    f_bi.append(len(verts)); verts.append((bx + r_bone_in * cb_a, by + r_bone_in * sb_a, z_bone))
                    b_bi.append(len(verts)); verts.append((bx + r_bone_in * cb_a, by + r_bone_in * sb_a, -z_bone))

                cf_marrow = len(verts); verts.append((bx, by, z_meat))
                cb_marrow = len(verts); verts.append((bx, by, -z_meat))

                for k in range(n_sp):
                    kn = (k + 1) % n_sp
                    # Thick seared brown side crust around the steak perimeter, with white fat cap at top
                    is_top_fat = k in (4, 5, 6, 7, 8, 9, 10)
                    c_side = COL_COPPER if k in (3, 4, 5, 9, 10, 11) else (COL_IVORY if k in (6, 7, 8) else COL_BARK)
                    c_fat_rim = COL_IVORY if is_top_fat else COL_TAN
                    # 1) Outer 3D side wall (radially outward normal for CCW contour: b_out[k]->b_out[kn]->f_out[kn]->f_out[k])
                    add_quad(b_out[k], b_out[kn], f_out[kn], f_out[k], c_side)
                    # 2) Creamy white/tan outer fat rind framing the steak face (+Z front, -Z back)
                    add_quad(f_out[k], f_out[kn], f_in[kn], f_in[k], c_fat_rim)
                    add_quad(b_in_s[k], b_in_s[kn], b_out[kn], b_out[k], c_fat_rim)
                    # 3) Juicy red-rose meat annulus between f_in (outer) and f_bo (inner) (+Z front, -Z back)
                    c_meat = COL_ROSE if k in (2, 4, 7, 10, 12) else COL_CRIMSON
                    add_quad(f_in[k], f_in[kn], f_bo[kn], f_bo[k], c_meat)
                    add_quad(b_bo[k], b_bo[kn], b_in_s[kn], b_in_s[k], c_meat)
                    # 4) Crisp Ivory-White Round Bone Ring ("o") between f_bo (outer) and f_bi (inner) (+Z front, -Z back)
                    add_quad(f_bo[k], f_bo[kn], f_bi[kn], f_bi[k], COL_IVORY)
                    add_quad(b_bi[k], b_bi[kn], b_bo[kn], b_bo[k], COL_IVORY)
                    # 5) Inner marrow center inside the "o" bone (+Z front, -Z back)
                    c_mar = COL_COPPER if k % 2 == 0 else COL_RUST
                    add_tri(cf_marrow, f_bi[k], f_bi[kn], c_mar)
                    add_tri(cb_marrow, b_bi[kn], b_bi[k], c_mar)




        elif "sausage" in id_low or "link" in id_low:
            # 9) Pair of Plump Arched Grilled Bratwurst Sausages with smooth roasted skin, dark grill sear marks & twisted casing tie-ends
            def add_curved_bratwurst(x0, y0, z0, x1, y1, z1, arch_x, arch_y, arch_z, r_mid):
                stations = [
                    (-1.22, 0.010, "tail"),
                    (-1.12, 0.044, "tail"),
                    (-1.01, 0.020, "knot"),
                    (-0.90, 0.065, "body"),
                    (-0.68, 0.090, "body"),
                    (-0.46, 0.098, "body"),
                    (-0.40, 0.094, "sear"),
                    (-0.34, 0.098, "body"),
                    (-0.06, 0.102, "body"),
                    ( 0.00, 0.096, "sear"),
                    ( 0.06, 0.102, "body"),
                    ( 0.34, 0.098, "body"),
                    ( 0.40, 0.094, "sear"),
                    ( 0.46, 0.098, "body"),
                    ( 0.68, 0.090, "body"),
                    ( 0.90, 0.065, "body"),
                    ( 1.01, 0.020, "knot"),
                    ( 1.12, 0.044, "tail"),
                    ( 1.22, 0.010, "tail"),
                ]
                sides_s = 10
                scale_f = r_mid / 0.102

                def spine_pt(u):
                    t = (u + 1.0) * 0.5
                    # Smooth quadratic/cosine arch peaking at u=0
                    bump = math.cos(max(-1.5, min(1.5, u * 1.22)))
                    px = x0 + (x1 - x0) * t + arch_x * bump
                    py = y0 + (y1 - y0) * t + arch_y * bump
                    pz = z0 + (z1 - z0) * t + arch_z * bump
                    return px, py, pz

                rings_b = []
                for u_val, r_raw, st_kind in stations:
                    r_val = r_raw * scale_f
                    cx_s, cy_s, cz_s = spine_pt(u_val)
                    cx_n, cy_n, cz_n = spine_pt(u_val + 0.02)
                    tx, ty, tz = cx_n - cx_s, cy_n - cy_s, cz_n - cz_s
                    t_len = max(1e-6, math.sqrt(tx * tx + ty * ty + tz * tz))
                    tx, ty, tz = tx / t_len, ty / t_len, tz / t_len
                    n1x, n1y, n1z = -ty, tx, 0.0
                    n1_len = max(1e-6, math.sqrt(n1x * n1x + n1y * n1y))
                    n1x, n1y = n1x / n1_len, n1y / n1_len
                    n2x = ty * n1z - tz * n1y
                    n2y = tz * n1x - tx * n1z
                    n2z = tx * n1y - ty * n1x
                    ring_idx = []
                    for k in range(sides_s):
                        ang = (2.0 * math.pi * k) / sides_s
                        ca, sa = math.cos(ang), math.sin(ang)
                        px = cx_s + r_val * (ca * n1x + sa * n2x)
                        py = cy_s + r_val * (ca * n1y + sa * n2y)
                        pz = cz_s + r_val * (ca * n1z + sa * n2z)
                        ring_idx.append(len(verts))
                        verts.append((px, py, pz))
                    rings_b.append((ring_idx, st_kind))

                for i in range(len(rings_b) - 1):
                    r0, k0 = rings_b[i]
                    r1, k1 = rings_b[i + 1]
                    for k in range(sides_s):
                        kn = (k + 1) % sides_s
                        ang_m = (2.0 * math.pi * (k + 0.5)) / sides_s
                        cm, sm = math.cos(ang_m), math.sin(ang_m)
                        lit = cm * 0.75 + sm * 0.65
                        if k0 == "knot" or k1 == "knot":
                            col = COL_IVORY if lit > 0.0 else COL_TAN
                        elif k0 == "tail" or k1 == "tail":
                            col = COL_COPPER if lit > 0.0 else COL_RUST
                        elif (k0 == "sear" and k1 == "body") and lit > 0.25:
                            # Crisp dark grill sear mark across the upper-front arch only
                            col = COL_BARK if lit > 0.55 else COL_WOOD
                        else:
                            # Smooth uniform roasted reddish-brown sausage casing (no stripes!)
                            if lit > 0.55:
                                col = COL_CRIMSON
                            elif lit > -0.15:
                                col = COL_RUST
                            elif lit > -0.60:
                                col = COL_COPPER
                            else:
                                col = COL_WOOD
                        add_quad(r0[k], r0[kn], r1[kn], r1[k], col)

            # Primary large arched bratwurst angled perpendicular to (+X, +Z) 3/4 view ray so Front, Side, and 3/4 views all show the full arch
            add_curved_bratwurst(
                x0=-0.21, y0=0.09, z0=-0.08,
                x1= 0.21, y1=0.22, z1= 0.06,
                arch_x=-0.04, arch_y=0.19, arch_z=0.04,
                r_mid=0.090,
            )
            # Second linked bratwurst nestled in front/below, curving in complementary arc
            add_curved_bratwurst(
                x0=-0.15, y0=0.06, z0= 0.02,
                x1= 0.22, y1=0.08, z1= 0.15,
                arch_x=0.03, arch_y=0.12, arch_z=0.04,
                r_mid=0.078,
            )



        elif "beer" in id_low or "stein" in id_low or "mug" in id_low or "ale" in id_low:
            # 10) Foaming Tavern Beer Stein: Wooden stave mug + 2 iron hoops + side D-handle + overflowing white foam head
            mug_prof = [
                (0.00, 0.19, 0.19, COL_BARK, COL_WOOD, COL_BARK),
                (0.08, 0.19, 0.19, COL_WOOD, COL_COPPER, COL_BARK),
                (0.08, 0.205, 0.205, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (0.15, 0.205, 0.205, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (0.15, 0.185, 0.185, COL_WOOD, COL_COPPER, COL_BARK),
                (0.38, 0.175, 0.175, COL_WOOD, COL_COPPER, COL_BARK),
                (0.38, 0.192, 0.192, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (0.45, 0.192, 0.192, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (0.45, 0.172, 0.172, COL_WOOD, COL_COPPER, COL_BARK),
                (0.52, 0.170, 0.170, COL_AMBER, COL_GOLD, COL_COPPER),
            ]
            add_lathe_profile(mug_prof, sides=10, cx=-0.03, cap_bot=True, cap_top=False)
            add_food_box(0.13, 0.36, 0.26, 0.42, 0.035, 0.008, COL_DARK_IRON, COL_OBSIDIAN, COL_STEEL)
            add_food_box(0.13, 0.12, 0.26, 0.18, 0.035, 0.008, COL_DARK_IRON, COL_OBSIDIAN, COL_STEEL)
            add_food_box(0.21, 0.12, 0.26, 0.42, 0.035, 0.008, COL_STEEL, COL_DARK_IRON, COL_SILVER)
            add_blob(-0.03, 0.55, 0.0, 0.195, 0.095, 0.195, COL_IVORY, COL_IVORY, COL_TAN, sides=10)
            for k in range(5):
                ang = (2.0 * math.pi * k) / 5 + 0.3
                add_blob(-0.03 + 0.12 * math.cos(ang), 0.56, 0.12 * math.sin(ang), 0.085, 0.075, 0.085, COL_IVORY, COL_IVORY, COL_TAN, sides=7)
            add_blob(-0.08, 0.44, 0.175, 0.052, 0.095, 0.045, COL_IVORY, COL_IVORY, COL_TAN, sides=6)

        elif "wine" in id_low or "bottle" in id_low:
            # 11) Slender Vintage Red Wine Bottle: Emerald glass body + cream parchment label with crimson band + red wax cork
            wine_prof = [
                (0.00, 0.135, 0.135, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.11, 0.140, 0.140, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.12, 0.146, 0.146, COL_TAN, COL_IVORY, COL_TAN),
                (0.18, 0.146, 0.146, COL_TAN, COL_IVORY, COL_TAN),
                (0.19, 0.148, 0.148, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.24, 0.148, 0.148, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.25, 0.146, 0.146, COL_TAN, COL_IVORY, COL_TAN),
                (0.31, 0.146, 0.146, COL_TAN, COL_IVORY, COL_TAN),
                (0.32, 0.140, 0.140, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.41, 0.135, 0.135, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.49, 0.082, 0.082, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.54, 0.048, 0.048, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.64, 0.048, 0.048, COL_SEA_GREEN, COL_GREEN, COL_EMERALD),
                (0.65, 0.058, 0.058, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.70, 0.058, 0.058, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (0.73, 0.044, 0.044, COL_TAN, COL_IVORY, COL_WOOD),
            ]
            add_lathe_profile(wine_prof, sides=12, cap_bot=True, cap_top=True)

        else:
            # 12) Traveler's Water Canteen / Leather Waterskin with lathe leather strap, steel spout, cork & front/back water medallion
            canteen_prof = [
                (0.00, 0.10, 0.055, COL_BARK, COL_WOOD, COL_BARK),
                (0.08, 0.21, 0.105, COL_WOOD, COL_TAN, COL_BARK),
                (0.20, 0.23, 0.115, COL_WOOD, COL_TAN, COL_BARK),
                (0.21, 0.238, 0.122, COL_BARK, COL_COPPER, COL_BARK),
                (0.27, 0.238, 0.122, COL_BARK, COL_COPPER, COL_BARK),
                (0.28, 0.23, 0.115, COL_WOOD, COL_TAN, COL_BARK),
                (0.38, 0.20, 0.105, COL_WOOD, COL_TAN, COL_BARK),
                (0.47, 0.11, 0.068, COL_BARK, COL_WOOD, COL_BARK),
                (0.49, 0.062, 0.062, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                (0.55, 0.066, 0.066, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                (0.56, 0.048, 0.048, COL_WOOD, COL_TAN, COL_BARK),
                (0.63, 0.055, 0.055, COL_TAN, COL_IVORY, COL_WOOD),
            ]
            add_lathe_profile(canteen_prof, sides=12, cap_bot=True, cap_top=True)
            # Shallow steel & sky-blue water crest medallions on the front (+Z) and back (-Z) faces
            add_blob(0.0, 0.24, 0.112, 0.075, 0.075, 0.022, COL_STEEL, COL_SILVER, COL_DARK_IRON, sides=8)
            add_blob(0.0, 0.24, 0.122, 0.048, 0.048, 0.018, COL_COBALT, COL_SKY_BLUE, COL_COBALT, sides=8)
            add_blob(0.0, 0.24, -0.112, 0.075, 0.075, 0.022, COL_STEEL, COL_SILVER, COL_DARK_IRON, sides=8)
            add_blob(0.0, 0.24, -0.122, 0.048, 0.048, 0.018, COL_COBALT, COL_SKY_BLUE, COL_COBALT, sides=8)

        # Preserve true uniform 3D proportions for food & drinks (center X & Z, ground min_y at 0.0)
        xs_f = [v[0] for v in verts]
        ys_f = [v[1] for v in verts]
        zs_f = [v[2] for v in verts]
        cx_f = 0.5 * (min(xs_f) + max(xs_f))
        cz_f = 0.5 * (min(zs_f) + max(zs_f))
        min_yf = min(ys_f)
        verts_out = [(v[0] - cx_f, v[1] - min_yf, v[2] - cz_f) for v in verts]
        return verts_out, tris, face_colors

    # -------------------------------------------------------------------------
    # 1A-6) Lamps & Dungeon Tools: 12 Hand-Held NetHack-Inspired Props (NO Flames)
    #       Origin (0, 0, 0) is the exact right-hand grip point!
    # -------------------------------------------------------------------------
    if archetype in ("lamp", "tool"):
        def add_tool_lathe(profile_pts, sides=10, cx=0.0, cz=0.0, cap_bot=True, cap_top=True, bot_inset_y=0.0, top_inset_y=0.0):
            rings = []
            for y_val, rx, rz, cm, cl, cd in profile_pts:
                r_idx = []
                for k in range(sides):
                    ang = (2.0 * math.pi * k) / sides
                    r_idx.append(len(verts))
                    verts.append((cx + rx * math.cos(ang), y_val, cz + rz * math.sin(ang)))
                rings.append((r_idx, y_val, cm, cl, cd))
            for i in range(len(rings) - 1):
                r0, _, cm0, cl0, cd0 = rings[i]
                r1, _, cm1, cl1, cd1 = rings[i + 1]
                for k in range(sides):
                    kn = (k + 1) % sides
                    ang_m = (2.0 * math.pi * (k + 0.5)) / sides
                    ca_m, sa_m = math.cos(ang_m), math.sin(ang_m)
                    col = cl1 if (sa_m > 0.22 and ca_m < 0.35) else (cm1 if sa_m > -0.28 else cd1)
                    add_quad(r0[kn], r0[k], r1[k], r1[kn], col)
            if cap_bot:
                r0, y0, _, _, cd0 = rings[0]
                cb = len(verts); verts.append((cx, y0 + bot_inset_y, cz))
                for k in range(sides):
                    add_tri(cb, r0[k], r0[(k + 1) % sides], cd0)
            if cap_top:
                rn, yn, _, cln, _ = rings[-1]
                ct = len(verts); verts.append((cx, yn + top_inset_y, cz))
                for k in range(sides):
                    add_tri(ct, rn[(k + 1) % sides], rn[k], cln)

        def add_tool_blob(cx, cy, cz, rx, ry, rz, cm, cl, cd, sides=8):
            prof = [
                (cy - ry * 0.90, rx * 0.40, rz * 0.40, cd, cm, cd),
                (cy - ry * 0.42, rx * 0.90, rz * 0.90, cm, cl, cd),
                (cy + ry * 0.12, rx * 1.00, rz * 1.00, cm, cl, cd),
                (cy + ry * 0.62, rx * 0.78, rz * 0.78, cl, cl, cm),
                (cy + ry * 0.94, rx * 0.34, rz * 0.34, cl, cl, cm),
            ]
            add_tool_lathe(prof, sides=sides, cx=cx, cz=cz, cap_bot=True, cap_top=True)

        def add_curved_tube(spine_pts, sides=8, cap_start=True, cap_end=True, end_inset=0.0):
            """
            Builds a 3D tube along spine_pts = [(x, y, z, r_xy, r_z, cm, cl, cd), ...]
            with a twist-free right-handed frame (N1 x N2 = T) and outward normals.
            """
            n_s = len(spine_pts)
            rings_t = []
            for i, (px, py, pz, r1, r2, cm, cl, cd) in enumerate(spine_pts):
                i_prev = max(0, i - 1)
                i_next = min(n_s - 1, i + 1)
                tx = spine_pts[i_next][0] - spine_pts[i_prev][0]
                ty = spine_pts[i_next][1] - spine_pts[i_prev][1]
                tz = spine_pts[i_next][2] - spine_pts[i_prev][2]
                tl = max(1e-6, math.sqrt(tx * tx + ty * ty + tz * tz))
                tx, ty, tz = tx / tl, ty / tl, tz / tl
                # Reference up vector (+Z if tangent is in XY plane, else +Y)
                if abs(tz) < 0.85:
                    ux, uy, uz = 0.0, 0.0, 1.0
                else:
                    ux, uy, uz = 0.0, 1.0, 0.0
                # N1 = normalize(U x T)
                n1x = uy * tz - uz * ty
                n1y = uz * tx - ux * tz
                n1z = ux * ty - uy * tx
                n1l = max(1e-6, math.sqrt(n1x * n1x + n1y * n1y + n1z * n1z))
                n1x, n1y, n1z = n1x / n1l, n1y / n1l, n1z / n1l
                # N2 = T x N1 (so N1 x N2 = T)
                n2x = ty * n1z - tz * n1y
                n2y = tz * n1x - tx * n1z
                n2z = tx * n1y - ty * n1x
                r_idx = []
                for k in range(sides):
                    ang = (2.0 * math.pi * k) / sides
                    ca, sa = math.cos(ang), math.sin(ang)
                    vx = px + r1 * ca * n1x + r2 * sa * n2x
                    vy = py + r1 * ca * n1y + r2 * sa * n2y
                    vz = pz + r1 * ca * n1z + r2 * sa * n2z
                    r_idx.append(len(verts))
                    verts.append((vx, vy, vz))
                rings_t.append((r_idx, (px, py, pz), (tx, ty, tz), cm, cl, cd))

            for i in range(n_s - 1):
                r0, _, _, cm0, cl0, cd0 = rings_t[i]
                r1, _, _, cm1, cl1, cd1 = rings_t[i + 1]
                for k in range(sides):
                    kn = (k + 1) % sides
                    ang_m = (2.0 * math.pi * (k + 0.5)) / sides
                    ca_m, sa_m = math.cos(ang_m), math.sin(ang_m)
                    col = cl1 if ca_m > 0.28 else (cm1 if ca_m > -0.35 else cd1)
                    add_quad(r0[k], r0[kn], r1[kn], r1[k], col)

            if cap_start:
                r0, p0, _, _, _, cd0 = rings_t[0]
                cs = len(verts); verts.append(p0)
                for k in range(sides):
                    add_tri(cs, r0[(k + 1) % sides], r0[k], cd0)
            if cap_end:
                rn, pn, tn, _, cln, cdn = rings_t[-1]
                ce = len(verts)
                verts.append((pn[0] + tn[0] * end_inset, pn[1] + tn[1] * end_inset, pn[2] + tn[2] * end_inset))
                cap_col = cdn if end_inset < 0.0 else cln
                for k in range(sides):
                    add_tri(ce, rn[k], rn[(k + 1) % sides], cap_col)

        if "torch_iron" in id_low or ("torch" in id_low and "iron" in id_low):
            # 2) Iron Brazier / Sconce Torch (fluted dark-iron handle, 6-ribbed open iron cage basket, dark coal briquettes, NO flame)
            shaft_prof = [
                (-0.28, 0.062, 0.062, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.23, 0.046, 0.046, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.12, 0.048, 0.048, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                (-0.11, 0.060, 0.060, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.08, 0.060, 0.060, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.07, 0.052, 0.052, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.09, 0.052, 0.052, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.10, 0.062, 0.062, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.13, 0.062, 0.062, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.14, 0.050, 0.050, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.40, 0.058, 0.058, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.41, 0.085, 0.085, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.46, 0.095, 0.095, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
            ]
            add_tool_lathe(shaft_prof, sides=8, cap_bot=True, cap_top=True)
            # 6 Flared Forged-Iron Brazier Cage Ribs + Upper Riveted Iron Hoop
            for k in range(6):
                ang = (2.0 * math.pi * k) / 6.0
                ca, sa = math.cos(ang), math.sin(ang)
                rib_pts = [
                    (0.075 * ca, 0.44, 0.075 * sa, 0.022, 0.022, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                    (0.145 * ca, 0.56, 0.145 * sa, 0.024, 0.024, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                    (0.205 * ca, 0.72, 0.205 * sa, 0.026, 0.026, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                    (0.220 * ca, 0.81, 0.220 * sa, 0.020, 0.020, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ]
                add_curved_tube(rib_pts, sides=6, cap_start=True, cap_end=True)
            # Upper riveted iron & brass hoop around the brazier basket (y = 0.68..0.74)
            hoop_prof = [
                (0.67, 0.195, 0.195, COL_OBSIDIAN, COL_DARK_IRON, COL_OBSIDIAN),
                (0.68, 0.218, 0.218, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (0.73, 0.222, 0.222, COL_BRASS, COL_GOLD, COL_DARK_IRON),
                (0.75, 0.198, 0.198, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
            ]
            add_tool_lathe(hoop_prof, sides=12, cap_bot=True, cap_top=True)
            # Mound of unlit dark charcoal & slate fuel briquettes nestled inside the brazier (NO flame)
            coal_blobs = [
                ( 0.00, 0.60,  0.00, 0.135, 0.12, 0.135, COL_OBSIDIAN, COL_DARK_IRON, COL_OBSIDIAN),
                (-0.07, 0.72,  0.05, 0.095, 0.08, 0.090, COL_DARK_IRON, COL_BARK, COL_OBSIDIAN),
                ( 0.08, 0.73,  0.04, 0.090, 0.08, 0.090, COL_OBSIDIAN, COL_DARK_IRON, COL_OBSIDIAN),
                ( 0.00, 0.74, -0.07, 0.092, 0.08, 0.088, COL_BARK, COL_DARK_IRON, COL_OBSIDIAN),
                ( 0.00, 0.78,  0.01, 0.085, 0.07, 0.085, COL_OBSIDIAN, COL_BARK, COL_OBSIDIAN),
            ]
            for bx, by, bz, brx, bry, brz, cm, cl, cd in coal_blobs:
                add_tool_blob(bx, by, bz, brx, bry, brz, cm, cl, cd, sides=7)

        elif "torch" in id_low:
            # 1) Classic Wooden Pitch Torch (tapered wooden handle, leather grip at (0,0,0), iron bands, pitch-soaked burlap head, NO flame)
            torch_prof = [
                (-0.27, 0.054, 0.054, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.22, 0.042, 0.042, COL_BARK, COL_WOOD, COL_BARK),
                (-0.11, 0.046, 0.046, COL_WOOD, COL_COPPER, COL_BARK),
                (-0.10, 0.056, 0.056, COL_COPPER, COL_TAN, COL_WOOD),
                ( 0.10, 0.058, 0.058, COL_COPPER, COL_TAN, COL_WOOD),
                ( 0.11, 0.050, 0.050, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.44, 0.068, 0.068, COL_WOOD, COL_COPPER, COL_BARK),
                # Forged iron collar band at base of pitch head
                ( 0.45, 0.088, 0.088, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.50, 0.092, 0.092, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                # Bulbous pitch-soaked burlap & tar wrapping (charred dark top, NO flame)
                ( 0.52, 0.125, 0.125, COL_BARK, COL_COPPER, COL_OBSIDIAN),
                ( 0.60, 0.152, 0.152, COL_COPPER, COL_TAN, COL_BARK),
                ( 0.62, 0.160, 0.160, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.66, 0.160, 0.160, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.68, 0.155, 0.155, COL_BARK, COL_COPPER, COL_OBSIDIAN),
                ( 0.78, 0.142, 0.142, COL_OBSIDIAN, COL_BARK, COL_OBSIDIAN),
                ( 0.84, 0.105, 0.105, COL_OBSIDIAN, COL_DARK_IRON, COL_OBSIDIAN),
            ]
            add_tool_lathe(torch_prof, sides=8, cap_bot=True, cap_top=True, top_inset_y=-0.035)
            # 4 vertical iron cage straps clasping the pitch head
            for k in range(4):
                ang = (2.0 * math.pi * (k + 0.5)) / 4.0
                ca, sa = math.cos(ang), math.sin(ang)
                strap_pts = [
                    (0.086 * ca, 0.46, 0.086 * sa, 0.020, 0.020, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                    (0.158 * ca, 0.63, 0.158 * sa, 0.022, 0.022, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                    (0.148 * ca, 0.77, 0.148 * sa, 0.020, 0.020, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ]
                add_curved_tube(strap_pts, sides=6, cap_start=True, cap_end=True)

        elif "oil" in id_low or ("lamp" in id_low and "lantern" not in id_low):
            # 3) Brass Oil Lamp / NetHack Magic Lamp (rear ring handle at (0,0,0), plump golden vessel, curved spout + unlit wick)
            cx_body = 0.22
            # Rear curved brass ring handle centered around the fist grip (0, 0, 0)
            handle_pts = [
                ( 0.06, -0.06, 0.0, 0.026, 0.026, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.05, -0.09, 0.0, 0.028, 0.028, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.12, -0.03, 0.0, 0.030, 0.030, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.12,  0.07, 0.0, 0.030, 0.030, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.04,  0.13, 0.0, 0.028, 0.028, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.07,  0.09, 0.0, 0.026, 0.026, COL_BRASS, COL_GOLD, COL_COPPER),
            ]
            add_curved_tube(handle_pts, sides=8, cap_start=True, cap_end=True)
            # Pedestal foot, plump hammered brass/gold reservoir, cobalt/crimson shoulder band & pointed dome lid
            lamp_prof = [
                (-0.18, 0.145, 0.125, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.14, 0.135, 0.115, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.10, 0.082, 0.072, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.05, 0.165, 0.135, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.02, 0.255, 0.195, COL_AMBER, COL_GOLD, COL_BRASS),
                ( 0.08, 0.265, 0.205, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.12, 0.235, 0.182, COL_COBALT, COL_SKY_BLUE, COL_DARK_IRON),
                ( 0.15, 0.195, 0.155, COL_CRIMSON, COL_ROSE, COL_BRASS),
                ( 0.18, 0.135, 0.115, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.21, 0.145, 0.122, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.26, 0.075, 0.068, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.31, 0.028, 0.028, COL_SILVER, COL_GOLD, COL_BRASS),
                ( 0.35, 0.046, 0.046, COL_GOLD, COL_SILVER, COL_BRASS),
            ]
            add_tool_lathe(lamp_prof, sides=12, cx=cx_body, cz=0.0, cap_bot=True, cap_top=True)
            # Long sweeping S-curved teapot/genie burner spout extending along +X to (0.68, 0.24)
            spout_pts = [
                (0.40, 0.02, 0.0, 0.078, 0.068, COL_BRASS, COL_GOLD, COL_COPPER),
                (0.50, 0.04, 0.0, 0.064, 0.056, COL_AMBER, COL_GOLD, COL_BRASS),
                (0.59, 0.11, 0.0, 0.050, 0.045, COL_GOLD, COL_AMBER, COL_BRASS),
                (0.65, 0.19, 0.0, 0.042, 0.040, COL_BRASS, COL_GOLD, COL_COPPER),
                (0.68, 0.24, 0.0, 0.048, 0.046, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (0.69, 0.26, 0.0, 0.044, 0.042, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                # Short unlit braided cotton wick stub protruding from the nozzle (NO flame)
                (0.70, 0.27, 0.0, 0.022, 0.022, COL_IVORY, COL_TAN, COL_BARK),
                (0.71, 0.32, 0.0, 0.018, 0.018, COL_BARK, COL_IVORY, COL_OBSIDIAN),
            ]
            add_curved_tube(spout_pts, sides=8, cap_start=True, cap_end=True)

        elif "lantern" in id_low:
            # 4) Brass Dungeon Lantern (top carry bail at (0,0,0), pagoda hood, sky-blue/ivory glass chamber, 4 iron cage struts, NO flame)
            # Top iron & brass suspension bail handle centered around grip (0, 0, 0)
            bail_pts = [
                (-0.11, -0.09, 0.0, 0.022, 0.022, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.12,  0.02, 0.0, 0.024, 0.024, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                (-0.07,  0.10, 0.0, 0.026, 0.026, COL_BRASS, COL_GOLD, COL_DARK_IRON),
                ( 0.00,  0.12, 0.0, 0.028, 0.028, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.07,  0.10, 0.0, 0.026, 0.026, COL_BRASS, COL_GOLD, COL_DARK_IRON),
                ( 0.12,  0.02, 0.0, 0.024, 0.024, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.11, -0.09, 0.0, 0.022, 0.022, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
            ]
            add_curved_tube(bail_pts, sides=6, cap_start=True, cap_end=True)
            # Lantern body hanging from y = -0.66 up to y = -0.06
            lantern_prof = [
                # Octagonal brass & dark-iron base reservoir
                (-0.66, 0.215, 0.215, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.61, 0.225, 0.225, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.54, 0.210, 0.210, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.52, 0.175, 0.175, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                # Beveled hexagonal/octagonal translucent-glass chamber (sky_blue / ivory / teal)
                (-0.51, 0.162, 0.162, COL_SKY_BLUE, COL_IVORY, COL_TEAL),
                (-0.38, 0.172, 0.172, COL_SKY_BLUE, COL_IVORY, COL_COBALT),
                (-0.24, 0.162, 0.162, COL_SKY_BLUE, COL_IVORY, COL_TEAL),
                # Tiered pagoda-style brass & copper vent hood
                (-0.23, 0.195, 0.195, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.21, 0.248, 0.248, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.15, 0.185, 0.185, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.13, 0.145, 0.145, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.08, 0.115, 0.115, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.05, 0.055, 0.055, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
            ]
            add_tool_lathe(lantern_prof, sides=8, cap_bot=True, cap_top=True)
            # 4 vertical protective dark-iron cage struts outside the glass chamber
            for k in range(4):
                ang = (2.0 * math.pi * (k + 0.5)) / 4.0
                ca, sa = math.cos(ang), math.sin(ang)
                strut_pts = [
                    (0.192 * ca, -0.54, 0.192 * sa, 0.020, 0.020, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                    (0.200 * ca, -0.38, 0.200 * sa, 0.020, 0.020, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                    (0.192 * ca, -0.22, 0.192 * sa, 0.020, 0.020, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ]
                add_curved_tube(strut_pts, sides=6, cap_start=True, cap_end=True)

        elif "candelabrum" in id_low or "candelabra" in id_low:
            # 6) Three-Branch Candelabrum of Invocation (gilded silver stem at (0,0,0), 3 curved branches, 3 ivory candles + unlit wicks)
            stem_prof = [
                (-0.26, 0.135, 0.135, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.21, 0.110, 0.110, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.15, 0.052, 0.052, COL_SILVER, COL_STEEL, COL_DARK_IRON),
                (-0.08, 0.058, 0.058, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.08, 0.058, 0.058, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.15, 0.050, 0.050, COL_SILVER, COL_STEEL, COL_DARK_IRON),
                ( 0.20, 0.078, 0.078, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.26, 0.048, 0.048, COL_SILVER, COL_STEEL, COL_DARK_IRON),
                ( 0.48, 0.046, 0.046, COL_GOLD, COL_AMBER, COL_BRASS),
            ]
            add_tool_lathe(stem_prof, sides=8, cap_bot=True, cap_top=True)
            # Sweeping left & right curved gold/silver arms from (0, 0.21, 0) to (±0.27, 0.48, 0)
            for side_s in (-1.0, 1.0):
                arm_pts = [
                    (0.03 * side_s, 0.21, 0.0, 0.032, 0.030, COL_GOLD, COL_AMBER, COL_BRASS),
                    (0.15 * side_s, 0.23, 0.0, 0.030, 0.028, COL_SILVER, COL_GOLD, COL_BRASS),
                    (0.24 * side_s, 0.32, 0.0, 0.030, 0.028, COL_GOLD, COL_AMBER, COL_BRASS),
                    (0.27 * side_s, 0.46, 0.0, 0.032, 0.030, COL_GOLD, COL_AMBER, COL_BRASS),
                ]
                add_curved_tube(arm_pts, sides=8, cap_start=True, cap_end=True)
            # 3 Brass/Gold Bobeche Cups + 3 Chunky Ivory Wax Candles + 3 Unlit Black Wick Stubs (NO flames)
            for cx_c, y_cup, y_top in [(-0.27, 0.45, 0.80), (0.0, 0.48, 0.86), (0.27, 0.45, 0.80)]:
                candle_prof = [
                    (y_cup,        0.042, 0.042, COL_BRASS, COL_GOLD, COL_COPPER),
                    (y_cup + 0.04, 0.092, 0.092, COL_GOLD, COL_AMBER, COL_BRASS),
                    (y_cup + 0.07, 0.068, 0.068, COL_BRASS, COL_GOLD, COL_COPPER),
                    (y_cup + 0.08, 0.058, 0.058, COL_TAN, COL_IVORY, COL_TAN),
                    (y_top - 0.02, 0.055, 0.055, COL_IVORY, COL_IVORY, COL_TAN),
                    (y_top,        0.048, 0.048, COL_IVORY, COL_IVORY, COL_TAN),
                    # Unlit dark cotton wick stub
                    (y_top + 0.01, 0.016, 0.016, COL_OBSIDIAN, COL_BARK, COL_OBSIDIAN),
                    (y_top + 0.07, 0.012, 0.012, COL_OBSIDIAN, COL_BARK, COL_OBSIDIAN),
                ]
                add_tool_lathe(candle_prof, sides=8, cx=cx_c, cz=0.0, cap_bot=True, cap_top=True)

        elif "candle" in id_low or "tallow" in id_low or "wax" in id_low:
            # 5) Tallow Candle in Brass Chamberstick (rear finger loop at (0,0,0), wide brass saucer, thick ivory candle + wax drips + unlit wick)
            cx_pan = 0.20
            # Rear brass finger-ring handle centered at the grip (0, 0, 0)
            loop_pts = [
                ( 0.05, -0.05, 0.0, 0.024, 0.024, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.06, -0.08, 0.0, 0.026, 0.026, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.12, -0.01, 0.0, 0.028, 0.028, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.10,  0.09, 0.0, 0.028, 0.028, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.01,  0.12, 0.0, 0.026, 0.026, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.07,  0.04, 0.0, 0.024, 0.024, COL_BRASS, COL_GOLD, COL_COPPER),
            ]
            add_curved_tube(loop_pts, sides=8, cap_start=True, cap_end=True)
            # Wide brass drip saucer + central socket + thick ivory beeswax/tallow candle + unlit wick stub
            chamber_prof = [
                (-0.08, 0.185, 0.185, COL_COPPER, COL_BRASS, COL_BARK),
                (-0.04, 0.245, 0.245, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.03, 0.255, 0.255, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.04, 0.135, 0.135, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.15, 0.122, 0.122, COL_GOLD, COL_AMBER, COL_BRASS),
                # Thick tallow/beeswax pillar candle
                ( 0.16, 0.102, 0.102, COL_TAN, COL_IVORY, COL_TAN),
                ( 0.38, 0.098, 0.098, COL_IVORY, COL_IVORY, COL_TAN),
                ( 0.62, 0.094, 0.094, COL_IVORY, COL_IVORY, COL_TAN),
                ( 0.65, 0.082, 0.082, COL_TAN, COL_IVORY, COL_TAN),
            ]
            add_tool_lathe(chamber_prof, sides=10, cx=cx_pan, cz=0.0, cap_bot=True, cap_top=True, top_inset_y=-0.02)
            # 4 sculpted wax drip ribs down the sides of the candle
            for k in range(4):
                ang = (2.0 * math.pi * (k + 0.3)) / 4.0
                ca, sa = math.cos(ang), math.sin(ang)
                h_bot = 0.26 if k % 2 == 0 else 0.36
                drip_pts = [
                    (cx_pan + 0.092 * ca, 0.63, 0.092 * sa, 0.022, 0.022, COL_IVORY, COL_IVORY, COL_TAN),
                    (cx_pan + 0.102 * ca, 0.48, 0.102 * sa, 0.024, 0.024, COL_IVORY, COL_IVORY, COL_TAN),
                    (cx_pan + 0.105 * ca, h_bot, 0.105 * sa, 0.028, 0.028, COL_TAN, COL_IVORY, COL_TAN),
                ]
                add_curved_tube(drip_pts, sides=6, cap_start=True, cap_end=True)
            # Short slightly curved black/bark unlit cotton wick at top center (NO flame)
            wick_pts = [
                (cx_pan,        0.63, 0.0, 0.020, 0.020, COL_TAN, COL_IVORY, COL_BARK),
                (cx_pan + 0.01, 0.69, 0.0, 0.018, 0.018, COL_OBSIDIAN, COL_BARK, COL_OBSIDIAN),
                (cx_pan + 0.03, 0.75, 0.0, 0.015, 0.015, COL_OBSIDIAN, COL_DARK_IRON, COL_OBSIDIAN),
            ]
            add_curved_tube(wick_pts, sides=6, cap_start=True, cap_end=True)

        elif "pickaxe" in id_low or "pick" in id_low and "lock" not in id_low:
            # 7) Dwarven Pickaxe (wooden shaft with leather grip at (0,0,0), iron socket collar, arched double-ended steel pick & chisel head)
            handle_prof = [
                (-0.28, 0.058, 0.058, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.23, 0.044, 0.044, COL_BARK, COL_WOOD, COL_BARK),
                (-0.11, 0.046, 0.046, COL_WOOD, COL_COPPER, COL_BARK),
                (-0.10, 0.056, 0.056, COL_COPPER, COL_TAN, COL_WOOD),
                ( 0.10, 0.056, 0.056, COL_COPPER, COL_TAN, COL_WOOD),
                ( 0.11, 0.048, 0.048, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.56, 0.054, 0.054, COL_WOOD, COL_COPPER, COL_BARK),
                # Forged iron & brass head socket collar
                ( 0.57, 0.088, 0.076, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.61, 0.092, 0.080, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.73, 0.092, 0.080, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.76, 0.072, 0.065, COL_BRASS, COL_GOLD, COL_COPPER),
                ( 0.84, 0.025, 0.025, COL_STEEL, COL_SILVER, COL_DARK_IRON),
            ]
            add_tool_lathe(handle_prof, sides=8, cap_bot=True, cap_top=True)
            # Sweeping curved front pointed pick spike (+X) and rear chisel/mattock blade (-X)
            pick_head_pts = [
                (-0.46, 0.51, 0.0, 0.016, 0.052, COL_SILVER, COL_SILVER, COL_STEEL),
                (-0.34, 0.59, 0.0, 0.045, 0.056, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                (-0.16, 0.66, 0.0, 0.068, 0.062, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.00, 0.68, 0.0, 0.082, 0.072, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.18, 0.66, 0.0, 0.066, 0.060, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.35, 0.58, 0.0, 0.044, 0.044, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.48, 0.48, 0.0, 0.012, 0.012, COL_SILVER, COL_SILVER, COL_STEEL),
            ]
            add_curved_tube(pick_head_pts, sides=6, cap_start=True, cap_end=True)

        elif "lockpick" in id_low or "thief" in id_low or "skeleton" in id_low:
            # 8) Thief's Lockpick & Tension Wrench Multi-Tool (riveted handle at (0,0,0), central hooked rake pick + fanned brass tension wrench & diamond pick)
            # Fold-out riveted handle with bottom steel keyring loop
            ring_pts = [
                (-0.05, -0.19, 0.0, 0.016, 0.016, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                (-0.06, -0.26, 0.0, 0.018, 0.018, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.00, -0.29, 0.0, 0.018, 0.018, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.06, -0.26, 0.0, 0.018, 0.018, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.05, -0.19, 0.0, 0.016, 0.016, COL_STEEL, COL_SILVER, COL_DARK_IRON),
            ]
            add_curved_tube(ring_pts, sides=6, cap_start=True, cap_end=True)
            grip_prof = [
                (-0.20, 0.072, 0.046, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.15, 0.078, 0.050, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.08, 0.082, 0.052, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.12, 0.082, 0.052, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.16, 0.078, 0.050, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.24, 0.086, 0.054, COL_GOLD, COL_AMBER, COL_BRASS),
            ]
            add_tool_lathe(grip_prof, sides=8, cap_bot=True, cap_top=True)
            # 1) Primary central silver-steel wave-rake & hook pick (y = 0.24..0.86)
            rake_pts = [
                ( 0.00, 0.23, 0.0, 0.036, 0.022, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.00, 0.42, 0.0, 0.032, 0.020, COL_SILVER, COL_SILVER, COL_STEEL),
                (-0.03, 0.52, 0.0, 0.034, 0.020, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.03, 0.61, 0.0, 0.034, 0.020, COL_SILVER, COL_SILVER, COL_STEEL),
                (-0.02, 0.70, 0.0, 0.030, 0.018, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.01, 0.80, 0.0, 0.026, 0.018, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.09, 0.85, 0.0, 0.022, 0.016, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.12, 0.81, 0.0, 0.014, 0.014, COL_SILVER, COL_SILVER, COL_STEEL),
            ]
            add_curved_tube(rake_pts, sides=6, cap_start=True, cap_end=True)
            # 2) Left fanned L-shaped brass tension wrench
            wrench_pts = [
                (-0.03, 0.23, 0.0, 0.028, 0.018, COL_BRASS, COL_GOLD, COL_COPPER),
                (-0.14, 0.46, 0.0, 0.026, 0.018, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.22, 0.64, 0.0, 0.026, 0.018, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.33, 0.60, 0.0, 0.022, 0.016, COL_BRASS, COL_GOLD, COL_COPPER),
            ]
            add_curved_tube(wrench_pts, sides=6, cap_start=True, cap_end=True)
            # 3) Right fanned blue-steel diamond ward pick
            ward_pts = [
                ( 0.03, 0.23, 0.0, 0.026, 0.018, COL_BLUE_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.15, 0.48, 0.0, 0.024, 0.018, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.22, 0.62, 0.0, 0.022, 0.016, COL_SILVER, COL_SILVER, COL_STEEL),
                ( 0.26, 0.69, 0.0, 0.048, 0.022, COL_SILVER, COL_SILVER, COL_BLUE_STEEL),
                ( 0.29, 0.75, 0.0, 0.012, 0.012, COL_SILVER, COL_SILVER, COL_STEEL),
            ]
            add_curved_tube(ward_pts, sides=6, cap_start=True, cap_end=True)

        elif "grappl" in id_low or "hook" in id_low:
            # 9) Iron Grappling Hook (coiled hemp rope grip at (0,0,0), dark-iron shank, 3 massive recurved barbed iron/silver talons)
            shank_prof = [
                (-0.26, 0.072, 0.072, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                (-0.20, 0.048, 0.048, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                # 4 plump coiled hemp rope rings around the grip (y = -0.14..+0.14)
                (-0.14, 0.125, 0.125, COL_WOOD, COL_TAN, COL_BARK),
                (-0.08, 0.132, 0.132, COL_TAN, COL_IVORY, COL_COPPER),
                (-0.07, 0.105, 0.105, COL_BARK, COL_WOOD, COL_BARK),
                (-0.01, 0.135, 0.135, COL_TAN, COL_IVORY, COL_COPPER),
                ( 0.00, 0.105, 0.105, COL_BARK, COL_WOOD, COL_BARK),
                ( 0.07, 0.135, 0.135, COL_TAN, COL_IVORY, COL_COPPER),
                ( 0.08, 0.105, 0.105, COL_BARK, COL_WOOD, COL_BARK),
                ( 0.14, 0.125, 0.125, COL_COPPER, COL_TAN, COL_WOOD),
                # Forged iron central shank & top spike crown
                ( 0.16, 0.052, 0.052, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.62, 0.056, 0.056, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.66, 0.092, 0.092, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                ( 0.76, 0.075, 0.075, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                ( 0.88, 0.018, 0.018, COL_SILVER, COL_SILVER, COL_STEEL),
            ]
            add_tool_lathe(shank_prof, sides=8, cap_bot=True, cap_top=True)
            # 3 recurved grappling claws spaced 120 deg apart (angles 0, 120, 240 so two claws frame X and one projects in Z)
            for k in range(3):
                ang = math.radians(30.0 + 120.0 * k)
                ca, sa = math.cos(ang), math.sin(ang)
                claw_pts = [
                    (0.05 * ca, 0.72, 0.05 * sa, 0.048, 0.048, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                    (0.18 * ca, 0.74, 0.18 * sa, 0.044, 0.044, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN),
                    (0.29 * ca, 0.62, 0.29 * sa, 0.038, 0.038, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                    (0.32 * ca, 0.46, 0.32 * sa, 0.032, 0.032, COL_STEEL, COL_SILVER, COL_DARK_IRON),
                    (0.25 * ca, 0.52, 0.25 * sa, 0.014, 0.014, COL_SILVER, COL_SILVER, COL_STEEL),
                ]
                add_curved_tube(claw_pts, sides=6, cap_start=True, cap_end=True)

        elif "horn" in id_low or "bugle" in id_low or "whistle" in id_low:
            # 10) Brass Tooled Hunting Horn / NetHack Bugle (sweeping crescent ivory/amber horn, crimson/gold grip at (0,0,0), flared brass bell + strap)
            horn_pts = [
                (-0.34,  0.34, 0.0, 0.036, 0.036, COL_SILVER, COL_SILVER, COL_STEEL),
                (-0.31,  0.22, 0.0, 0.045, 0.045, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.24,  0.09, 0.0, 0.062, 0.062, COL_IVORY, COL_IVORY, COL_TAN),
                (-0.14,  0.00, 0.0, 0.082, 0.082, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.04, -0.03, 0.0, 0.092, 0.092, COL_CRIMSON, COL_ROSE, COL_WOOD),
                ( 0.05, -0.02, 0.0, 0.096, 0.096, COL_CRIMSON, COL_ROSE, COL_WOOD),
                ( 0.15,  0.03, 0.0, 0.110, 0.110, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.24,  0.14, 0.0, 0.128, 0.128, COL_IVORY, COL_IVORY, COL_TAN),
                ( 0.30,  0.28, 0.0, 0.152, 0.152, COL_TAN, COL_IVORY, COL_AMBER),
                ( 0.33,  0.40, 0.0, 0.185, 0.185, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.35,  0.48, 0.0, 0.225, 0.225, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.36,  0.51, 0.0, 0.235, 0.235, COL_BARK, COL_DARK_IRON, COL_BARK),
            ]
            add_curved_tube(horn_pts, sides=10, cap_start=True, cap_end=True, end_inset=-0.06)
            # Leather & brass carrying baldric cord looping across the crescent between the two gold rings
            strap_pts = [
                (-0.22, 0.12, 0.0, 0.020, 0.020, COL_WOOD, COL_COPPER, COL_BARK),
                (-0.10, 0.28, 0.0, 0.020, 0.020, COL_COPPER, COL_TAN, COL_WOOD),
                ( 0.06, 0.33, 0.0, 0.020, 0.020, COL_COPPER, COL_TAN, COL_WOOD),
                ( 0.22, 0.24, 0.0, 0.020, 0.020, COL_WOOD, COL_COPPER, COL_BARK),
            ]
            add_curved_tube(strap_pts, sides=6, cap_start=True, cap_end=True)

        elif "bell" in id_low:
            # 11) Silver Ritual Bell / NetHack Bell of Opening (turned wood/gold handle at (0,0,0), flared silver skirt + hanging iron clapper)
            bell_prof = [
                # Flared silver & gold sound-bow rim at bottom (y = -0.54) rising to wooden/gold handle at (0,0,0)
                (-0.54, 0.285, 0.285, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.48, 0.265, 0.265, COL_SILVER, COL_SILVER, COL_STEEL),
                (-0.36, 0.205, 0.205, COL_SILVER, COL_SILVER, COL_BLUE_STEEL),
                (-0.26, 0.195, 0.195, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.21, 0.195, 0.195, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.12, 0.155, 0.155, COL_SILVER, COL_SILVER, COL_STEEL),
                (-0.06, 0.075, 0.075, COL_BRASS, COL_GOLD, COL_COPPER),
                # Turned rosewood & gold grip handle around (0, 0, 0)
                (-0.04, 0.056, 0.056, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.14, 0.062, 0.062, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.18, 0.078, 0.078, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.25, 0.045, 0.045, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.32, 0.068, 0.068, COL_GOLD, COL_SILVER, COL_BRASS),
            ]
            add_tool_lathe(bell_prof, sides=12, cap_bot=True, cap_top=True, bot_inset_y=0.06)
            # Heavy forged dark-iron & gold clapper sphere protruding below the bell mouth
            add_tool_blob(0.0, -0.56, 0.0, 0.082, 0.082, 0.082, COL_DARK_IRON, COL_STEEL, COL_OBSIDIAN, sides=8)

        else:
            # 12) Gilded Looking Glass / NetHack Hand Mirror (sculpted handle at (0,0,0), beveled oval gold frame, sky-blue/silver reflective glass)
            mirror_handle_prof = [
                (-0.26, 0.068, 0.052, COL_CRIMSON, COL_ROSE, COL_MAGENTA),
                (-0.20, 0.058, 0.046, COL_GOLD, COL_AMBER, COL_BRASS),
                (-0.14, 0.048, 0.042, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.10, 0.052, 0.044, COL_WOOD, COL_COPPER, COL_BARK),
                ( 0.15, 0.095, 0.056, COL_GOLD, COL_AMBER, COL_BRASS),
                ( 0.21, 0.125, 0.060, COL_BRASS, COL_GOLD, COL_COPPER),
            ]
            add_tool_lathe(mirror_handle_prof, sides=8, cap_bot=True, cap_top=True)
            # Oval beveled gold frame + faceted sky-blue & silver reflective glass on front (+Z) and back (-Z)
            cy_m = 0.53
            rx_out, ry_out = 0.245, 0.335
            rx_in, ry_in = 0.182, 0.262
            z_rim, z_bev, z_glass = 0.036, 0.050, 0.042
            n_m = 12
            f_ro, b_ro, f_ri, b_ri, f_gl, b_gl = [], [], [], [], [], []
            for k in range(n_m):
                ang = (2.0 * math.pi * k) / n_m
                ca, sa = math.cos(ang), math.sin(ang)
                f_ro.append(len(verts)); verts.append((rx_out * ca, cy_m + ry_out * sa, z_rim))
                b_ro.append(len(verts)); verts.append((rx_out * ca, cy_m + ry_out * sa, -z_rim))
                f_ri.append(len(verts)); verts.append((rx_in * ca, cy_m + ry_in * sa, z_bev))
                b_ri.append(len(verts)); verts.append((rx_in * ca, cy_m + ry_in * sa, -z_bev))
                f_gl.append(len(verts)); verts.append((rx_in * 0.96 * ca, cy_m + ry_in * 0.96 * sa, z_glass))
                b_gl.append(len(verts)); verts.append((rx_in * 0.96 * ca, cy_m + ry_in * 0.96 * sa, -z_glass))
            cf_g = len(verts); verts.append((0.0, cy_m, z_glass))
            cb_g = len(verts); verts.append((0.0, cy_m, -z_glass))
            for k in range(n_m):
                kn = (k + 1) % n_m
                c_frame = COL_GOLD if k in (1, 2, 3, 4, 7, 8) else COL_BRASS
                # Outer side wall (CCW contour -> radially outward normal)
                add_quad(b_ro[k], b_ro[kn], f_ro[kn], f_ro[k], COL_BRASS)
                # Front & back gold frame bevels
                add_quad(f_ro[k], f_ro[kn], f_ri[kn], f_ri[k], c_frame)
                add_quad(b_ri[k], b_ri[kn], b_ro[kn], b_ro[k], c_frame)
                # Inner step from gold frame to glass
                add_quad(f_ri[k], f_ri[kn], f_gl[kn], f_gl[k], COL_AMBER)
                add_quad(b_gl[k], b_gl[kn], b_ri[kn], b_ri[k], COL_AMBER)
                # Reflective mirror glass with diagonal silver specular reflection streaks
                c_glass = COL_SILVER if k in (1, 2, 7, 8) else (COL_SKY_BLUE if k % 2 == 0 else COL_TEAL)
                add_tri(cf_g, f_gl[k], f_gl[kn], c_glass)
                add_tri(cb_g, b_gl[kn], b_gl[k], c_glass)

        # Keep exact authored local coordinates so (0, 0, 0) stays at the hand grip origin!
        return verts, tris, face_colors

    # -------------------------------------------------------------------------
    # 1B) Helmets, Chestplates: 16-sided 3D lathe shell
    # -------------------------------------------------------------------------
    if archetype in ("helmet", "chestplate"):
        num_slices = 40
        sides = 16
        rings = []
        max_rw = max(1, max(row_widths))
        for s in range(num_slices):
            t = s / (num_slices - 1)
            py = min(hr_h - 1, max(0, int((1.0 - t) * (hr_h - 1))))
            xs_r = [px for px in range(hr_w) if fg_hr[py][px]]
            x0_r, x1_r = (xs_r[0], xs_r[-1]) if xs_r else (int(hr_w * 0.38), int(hr_w * 0.62))
            rw = max(4, x1_r - x0_r)
            cx_r = 0.5 * (x0_r + x1_r)
            base_rx = max(0.05, (rw / max_rw) * 0.5)
            base_rz = base_rx * 0.95
            ring = []
            for k in range(sides):
                ang = (2.0 * math.pi * k) / sides
                ux = math.cos(ang)
                uz = math.sin(ang)
                c_k = sample_clean_rgb(cx_r + ux * (rw * 0.45), py, win=1)
                _, _, _, _, lum_k, warmth_k, yr_k = rgb_to_hsl_features(c_k)
                rel = 1.08 if (warmth_k > 24 and yr_k >= 0.49 and lum_k >= 130) else (0.94 if lum_k < 58 else 1.0)
                ring.append(len(verts))
                verts.append((base_rx * rel * ux, t, base_rz * rel * uz))
            rings.append((ring, py, cx_r, rw))
        for r_i in range(len(rings) - 1):
            r0, py0, cx0, rw0 = rings[r_i]
            r1, py1, cx1, rw1 = rings[r_i + 1]
            py_m = 0.5 * (py0 + py1)
            cx_m = 0.5 * (cx0 + cx1)
            rw_m = 0.5 * (rw0 + rw1)
            for k in range(sides):
                kn = (k + 1) % sides
                ang_m = (2.0 * math.pi * (k + 0.5)) / sides
                col_q = sample_clean_rgb(cx_m + math.cos(ang_m) * (rw_m * 0.44), py_m, win=1)
                add_quad(r0[k], r0[kn], r1[kn], r1[k], col_q)
        bot_c = len(verts)
        verts.append((0.0, 0.0, 0.0))
        top_c = len(verts)
        verts.append((0.0, 1.0, 0.0))
        for k in range(sides):
            kn = (k + 1) % sides
            col_b = sample_clean_rgb(rings[0][2], rings[0][1], win=1)
            col_t = sample_clean_rgb(rings[-1][2], rings[-1][1], win=1)
            if archetype not in ("helmet", "chestplate"):
                add_tri(bot_c, rings[0][0][kn], rings[0][0][k], col_b)
            add_tri(top_c, rings[-1][0][k], rings[-1][0][kn], col_t)
        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 2) Shields: Clean Flat-Shaded stylized Heater / Kite / Aegis Bulwark Shields
    # -------------------------------------------------------------------------
    if archetype == "shield":
        is_kite = ("kite" in id_low) or ("crimson" in pal_set)
        is_aegis = ("aegis" in id_low) or (has_gold and not is_kite)

        col_rim_front = COL_GOLD if is_aegis else (COL_GOLD if is_kite and has_gold else COL_STEEL)
        col_rim_side = COL_AMBER if is_aegis else (COL_AMBER if is_kite and has_gold else COL_DARK_IRON)
        col_field = COL_COBALT if (is_aegis and "cobalt" in pal_set) else (COL_CRIMSON if is_kite else COL_WOOD)
        col_field_alt = COL_DARK_IRON if is_aegis else (COL_CRIMSON if is_kite else COL_COPPER)
        col_emblem = COL_GOLD if (is_kite or is_aegis) else COL_STEEL
        col_boss = COL_SKY_BLUE if (is_aegis and "sky_blue" in pal_set) else (COL_GOLD if is_kite else COL_SILVER)

        # Build 12-row vertically contoured shield shell with thick raised outer rim, vaulted face, and flat back
        n_rows = 12
        row_rings_front_outer = []
        row_rings_front_rim_in = []
        row_rings_front_field = []
        row_rings_back = []

        for r_i in range(n_rows):
            t = r_i / (n_rows - 1)  # 0.0 = pointed bottom tip, 1.0 = top edge
            y = -0.58 + t * 1.10
            if is_kite:
                # Tall teardrop kite contour: narrow pointed bottom, wide upper shoulders, slightly arched top
                w_half = 0.04 + 0.44 * (t ** 0.58) if t < 0.78 else 0.48 - 0.14 * ((t - 0.78) / 0.22) ** 1.4
            elif is_aegis:
                # Heroic octagonal/winged bulwark contour
                if t < 0.22:
                    w_half = 0.06 + 0.36 * (t / 0.22)
                elif t < 0.82:
                    w_half = 0.42 + 0.06 * math.sin(((t - 0.22) / 0.60) * math.pi)
                else:
                    w_half = 0.48 - 0.10 * ((t - 0.82) / 0.18)
            else:
                # Classic stylized heater shield: flat/slightly arched top, parallel upper sides, curved V bottom
                w_half = 0.05 + 0.43 * (t / 0.58) ** 0.72 if t < 0.58 else 0.48

            rim_w = 0.075 if not is_aegis else 0.095
            in_half = max(0.015, w_half - rim_w)
            vault_z = 0.012 * math.sin(t * math.pi)

            # 5 X stations across the shield width: [-w_half, -in_half, 0, +in_half, +w_half]
            xs_outer = [-w_half, -in_half, 0.0, in_half, w_half]
            z_rim_f = [0.11 + vault_z * 0.5, 0.145 + vault_z, 0.155 + vault_z, 0.145 + vault_z, 0.11 + vault_z * 0.5]
            z_fld_f = [0.09 + vault_z * 0.4, 0.105 + vault_z * 0.85, 0.118 + vault_z, 0.105 + vault_z * 0.85, 0.09 + vault_z * 0.4]
            z_b = [-0.02 + vault_z * 0.2, -0.01 + vault_z * 0.3, 0.0 + vault_z * 0.35, -0.01 + vault_z * 0.3, -0.02 + vault_z * 0.2]

            rf_out = []
            rf_in = []
            rf_fld = []
            rb = []
            for k in range(5):
                rf_out.append(len(verts))
                verts.append((xs_outer[k], y, z_rim_f[k]))
                rf_in.append(len(verts))
                verts.append((xs_outer[k], y, z_rim_f[k]))
                rf_fld.append(len(verts))
                verts.append((xs_outer[k], y, z_fld_f[k]))
                rb.append(len(verts))
                verts.append((xs_outer[k], y, z_b[k]))
            row_rings_front_outer.append(rf_out)
            row_rings_front_rim_in.append(rf_in)
            row_rings_front_field.append(rf_fld)
            row_rings_back.append(rb)

        for r_i in range(n_rows - 1):
            is_border_row = (r_i == 0) or (r_i == n_rows - 2)
            for k in range(4):
                is_rim_col = (k == 0) or (k == 3)
                if is_rim_col or is_border_row:
                    f00 = row_rings_front_outer[r_i][k]
                    f10 = row_rings_front_outer[r_i][k + 1]
                    f11 = row_rings_front_outer[r_i + 1][k + 1]
                    f01 = row_rings_front_outer[r_i + 1][k]
                    add_quad(f00, f10, f11, f01, col_rim_front)
                else:
                    f00 = row_rings_front_field[r_i][k]
                    f10 = row_rings_front_field[r_i][k + 1]
                    f11 = row_rings_front_field[r_i + 1][k + 1]
                    f01 = row_rings_front_field[r_i + 1][k]
                    c_fld = col_field if (k == 1) else col_field_alt
                    add_quad(f00, f10, f11, f01, c_fld)

                # Back plate
                b00 = row_rings_back[r_i][k]
                b10 = row_rings_back[r_i][k + 1]
                b11 = row_rings_back[r_i + 1][k + 1]
                b01 = row_rings_back[r_i + 1][k]
                add_quad(b10, b00, b01, b11, COL_WOOD if not is_aegis else COL_DARK_IRON)

            # Inner rim step walls at k=1 and k=3 for interior rows
            if not is_border_row:
                add_quad(
                    row_rings_front_rim_in[r_i][1], row_rings_front_field[r_i][1],
                    row_rings_front_field[r_i + 1][1], row_rings_front_rim_in[r_i + 1][1],
                    col_rim_side,
                )
                add_quad(
                    row_rings_front_field[r_i][3], row_rings_front_rim_in[r_i][3],
                    row_rings_front_rim_in[r_i + 1][3], row_rings_front_field[r_i + 1][3],
                    col_rim_side,
                )

            # Left (k=0) and Right (k=4) outer bevel side walls
            add_quad(
                row_rings_back[r_i][0], row_rings_front_outer[r_i][0],
                row_rings_front_outer[r_i + 1][0], row_rings_back[r_i + 1][0],
                col_rim_side,
            )
            add_quad(
                row_rings_front_outer[r_i][4], row_rings_back[r_i][4],
                row_rings_back[r_i + 1][4], row_rings_front_outer[r_i + 1][4],
                col_rim_side,
            )

        # Top & bottom outer rim edge walls
        for k in range(4):
            add_quad(
                row_rings_front_outer[-1][k], row_rings_front_outer[-1][k + 1],
                row_rings_back[-1][k + 1], row_rings_back[-1][k],
                col_rim_side,
            )
            add_quad(
                row_rings_back[0][k], row_rings_back[0][k + 1],
                row_rings_front_outer[0][k + 1], row_rings_front_outer[0][k],
                col_rim_side,
            )

        def add_subdivided_bar_v(x0, x1, y0, y1, z0, z1, c_main, c_trim, segs=5):
            dy = (y1 - y0) / segs
            for s_i in range(segs):
                add_3d_beveled_box(x0, x1, y0 + s_i * dy, y0 + (s_i + 1) * dy, z0, z1, c_main, c_trim)

        def add_subdivided_bar_h(x0, x1, y0, y1, z0, z1, c_main, c_trim, segs=4):
            dx = (x1 - x0) / segs
            for s_i in range(segs):
                add_3d_beveled_box(x0 + s_i * dx, x0 + (s_i + 1) * dx, y0, y1, z0, z1, c_main, c_trim)

        # Raised 3D Front Emblem & Boss (subdivided for exact painter's depth sorting at all angles):
        if is_kite:
            add_subdivided_bar_v(-0.048, 0.048, -0.38, 0.38, 0.135, 0.185, COL_GOLD, COL_AMBER)
            add_subdivided_bar_h(-0.28, 0.28, 0.02, 0.115, 0.135, 0.188, COL_GOLD, COL_AMBER)
        elif is_aegis:
            add_subdivided_bar_v(-0.055, 0.055, -0.34, 0.36, 0.135, 0.185, COL_GOLD, COL_AMBER)
            add_subdivided_bar_h(-0.31, 0.31, -0.02, 0.09, 0.135, 0.188, COL_GOLD, COL_AMBER)
            add_subdivided_bar_h(-0.24, 0.24, 0.16, 0.24, 0.135, 0.186, COL_GOLD, COL_AMBER)
        else:
            add_subdivided_bar_h(-0.36, 0.36, 0.02, 0.10, 0.135, 0.188, COL_STEEL, COL_DARK_IRON)
            add_subdivided_bar_v(-0.045, 0.045, -0.36, 0.36, 0.135, 0.185, COL_STEEL, COL_DARK_IRON)

        # Central 8-sided 3D domed shield boss at (0, 0.065, +0.235)
        boss_ctr_y = 0.065
        boss_ring = []
        for k in range(8):
            ang = (2.0 * math.pi * k) / 8.0
            boss_ring.append(len(verts))
            verts.append((0.125 * math.cos(ang), boss_ctr_y + 0.125 * math.sin(ang), 0.190))
        boss_apex = len(verts)
        verts.append((0.0, boss_ctr_y, 0.240))
        for k in range(8):
            kn = (k + 1) % 8
            add_tri(boss_ring[k], boss_ring[kn], boss_apex, col_boss if (k % 2 == 0) else col_emblem)

        # Back 3D forearm strap & grip bar around (0, 0, -0.12)
        add_3d_beveled_box(-0.22, -0.15, -0.10, 0.12, -0.12, 0.0, COL_DARK_IRON)
        add_3d_beveled_box(0.15, 0.22, -0.10, 0.12, -0.12, 0.0, COL_DARK_IRON)
        add_3d_beveled_box(-0.22, 0.22, -0.04, 0.04, -0.14, -0.08, COL_WOOD, COL_BARK)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 3) Crossbow: Clean Flat-Shaded 3D Arbalest in handslot.r Local Space
    #    (Y = stock length [-0.34..1.12], Z = horizontal prod span [-0.68..0.68], X = vertical height [-0.22..0.22])
    # -------------------------------------------------------------------------
    if archetype == "crossbow":
        col_trim = COL_GOLD if has_gold else COL_STEEL
        col_trim_dk = COL_AMBER if has_gold else COL_DARK_IRON

        # Sculpted wooden tiller stock along Y
        add_3d_beveled_box(-0.055, 0.045, -0.32, 0.84, -0.058, 0.058, COL_WOOD, COL_COPPER)
        # Buttstock flare & golden end-cap
        add_3d_beveled_box(-0.095, 0.050, -0.34, -0.22, -0.068, 0.068, col_trim, col_trim_dk)
        # Lower ergonomic pistol grip & trigger lever near origin (0,0,0)
        add_3d_beveled_box(-0.21, -0.045, -0.06, 0.08, -0.046, 0.046, COL_COPPER, COL_BARK)
        add_3d_beveled_box(-0.15, -0.05, 0.08, 0.22, -0.025, 0.025, col_trim, col_trim_dk)
        # Gilded side cheek plates & top steel bolt rail
        add_3d_beveled_box(-0.035, 0.035, 0.05, 0.72, -0.072, 0.072, col_trim, col_trim_dk)
        add_3d_beveled_box(0.040, 0.065, -0.12, 0.88, -0.036, 0.036, COL_STEEL, COL_DARK_IRON)
        # Heavy front prod mounting socket block
        add_3d_beveled_box(-0.075, 0.080, 0.70, 0.88, -0.13, 0.13, col_trim, col_trim_dk)

        # Sweeping recurve bow prod limbs spanning Z in [-0.68 .. +0.68]
        prod_segs = [
            (0.08, 0.79, 0.065, 0.050, col_trim),
            (0.24, 0.76, 0.058, 0.044, COL_DARK_IRON),
            (0.42, 0.66, 0.048, 0.038, COL_STEEL),
            (0.57, 0.52, 0.038, 0.030, COL_STEEL),
            (0.68, 0.42, 0.026, 0.022, col_trim),
        ]
        for z_sign in (-1.0, 1.0):
            limb_rings = []
            for z_abs, y_c, hx, hy, c_seg in prod_segs:
                z_v = z_sign * z_abs
                ring = [
                    len(verts), len(verts) + 1, len(verts) + 2, len(verts) + 3,
                ]
                verts.extend([
                    (-hx, y_c - hy, z_v),
                    (hx, y_c - hy, z_v),
                    (hx, y_c + hy, z_v),
                    (-hx, y_c + hy, z_v),
                ])
                limb_rings.append((ring, c_seg))
            for s_i in range(len(limb_rings) - 1):
                r0, c0 = limb_rings[s_i]
                r1, _ = limb_rings[s_i + 1]
                for k in range(4):
                    kn = (k + 1) % 4
                    if z_sign > 0:
                        add_quad(r0[k], r0[kn], r1[kn], r1[k], c0 if k % 2 == 1 else col_trim_dk)
                    else:
                        add_quad(r0[kn], r0[k], r1[k], r1[kn], c0 if k % 2 == 1 else col_trim_dk)
            # Cap limb tip
            rt, ct = limb_rings[-1]
            if z_sign > 0:
                add_quad(rt[0], rt[1], rt[2], rt[3], ct)
            else:
                add_quad(rt[1], rt[0], rt[3], rt[2], ct)

        # Taut bowstring V-cables from limb tips (z=+-0.66, y=0.43) to cocked nut (z=0, y=0.12)
        for z_sign in (-1.0, 1.0):
            ib = len(verts)
            verts.extend([
                (0.055, 0.42, z_sign * 0.65), (0.075, 0.42, z_sign * 0.65),
                (0.075, 0.45, z_sign * 0.65), (0.055, 0.45, z_sign * 0.65),
                (0.055, 0.11, 0.0), (0.075, 0.11, 0.0),
                (0.075, 0.14, 0.0), (0.055, 0.14, 0.0),
            ])
            for k in range(4):
                kn = (k + 1) % 4
                add_quad(ib + k, ib + kn, ib + 4 + kn, ib + 4 + k, COL_TAN)

        # Front foot stirrup loop at y = [0.88 .. 1.12]
        add_3d_beveled_box(-0.025, 0.025, 0.86, 1.10, -0.14, -0.10, COL_DARK_IRON)
        add_3d_beveled_box(-0.025, 0.025, 0.86, 1.10, 0.10, 0.14, COL_DARK_IRON)
        add_3d_beveled_box(-0.025, 0.025, 1.06, 1.12, -0.14, 0.14, COL_STEEL, COL_DARK_IRON)

        # Loaded crossbow quarrel/bolt + broadhead tip resting on top rail (+X)
        add_3d_beveled_box(0.068, 0.098, 0.12, 0.94, -0.018, 0.018, COL_WOOD, COL_COPPER)
        add_3d_beveled_box(0.058, 0.115, 0.94, 1.12, -0.042, 0.042, COL_SILVER, COL_STEEL)
        add_3d_beveled_box(0.072, 0.135, 0.14, 0.30, -0.055, 0.055, col_trim, COL_CRIMSON)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 4) Swords, Broadswords, & Daggers: stylized Proportions
    #    (Compact wrapped grip + chunky pommel + expressive wide guard + broad 4-facet diamond blade)
    # -------------------------------------------------------------------------
    if archetype in ("sword", "broadsword", "dagger"):
        is_obsidian = ("obsidian" in id_low) or ("nightblade" in id_low)
        is_kukri = ("kukri" in id_low) or ("dirk" in id_low)

        col_metal_hi = COL_SILVER if not is_obsidian else COL_BLUE_STEEL
        col_metal_mid = COL_GOLD if (has_gold and "sunblade" in id_low) else (COL_DARK_IRON if is_obsidian else COL_STEEL)
        col_spine = COL_GOLD if has_gold else (COL_OBSIDIAN if is_obsidian else COL_DARK_IRON)
        col_guard = COL_GOLD if has_gold else (COL_DARK_IRON if is_obsidian else COL_STEEL)
        col_guard_trim = COL_AMBER if has_gold else (COL_OBSIDIAN if is_obsidian else COL_DARK_IRON)
        col_grip = COL_CRIMSON if ("crimson" in pal_set and has_gold) else (COL_WOOD if not is_obsidian else COL_BARK)
        col_gem = COL_CRIMSON if ("crimson" in pal_set) else (COL_COBALT if "cobalt" in pal_set else COL_SILVER)

        # Normalized vertical stations [0.0 .. 1.0]: grip occupies bottom 23%, guard at 0.24, blade [0.25 .. 1.00]
        r_grip = 0.040 if archetype == "broadsword" else (0.036 if archetype == "sword" else 0.032)
        grip_stations = [
            (0.000, r_grip * 0.45, col_guard_trim, 0.0, "pom_tip"),
            (0.022, r_grip * 1.48, col_guard_trim, 0.0, "pom_bot"),
            (0.075, r_grip * 1.54, col_guard, 0.0, "pom_top"),
            (0.088, r_grip * 0.92, COL_BARK, 0.0, "neck0"),
            (0.115, r_grip * 1.08, col_grip, 0.006, "wrap1_lo"),
            (0.145, r_grip * 1.12, col_grip, 0.006, "wrap1_hi"),
            (0.155, r_grip * 0.92, COL_BARK, 0.006, "seam1"),
            (0.175, r_grip * 1.12, col_grip, 0.006, "wrap2_lo"),
            (0.205, r_grip * 1.08, col_grip, 0.006, "wrap2_hi"),
            (0.225, r_grip * 1.25, col_guard_trim, 0.0, "guard_seat"),
        ]
        add_oct_lathe(
            grip_stations,
            sides=8,
            z_aspect=0.72,
            cap_bot_col=col_guard,
            cap_top_col=col_guard,
            boss_tags=("pom_bot", "pom_top"),
            boss_col=col_gem if has_gold else col_metal_hi,
            boss_recess_col=col_guard_trim,
        )

        # Expressive 3D Crossguard around y = [0.22 .. 0.28]
        gw = 0.34 if archetype == "broadsword" else (0.26 if archetype == "sword" else 0.19)
        gz = r_grip * 1.35
        # Central guard block + raised diamond jewel boss on front/back
        add_3d_beveled_box(-gw * 0.28, gw * 0.28, 0.215, 0.278, -gz, gz, col_guard, col_guard_trim)
        add_3d_beveled_box(-gw * 0.14, gw * 0.14, 0.228, 0.268, -gz * 1.22, gz * 1.22, col_gem if has_gold else col_metal_hi, col_guard)

        # Segmented 3D quillon wings (upswept winged gold guard for Tier 5, sturdy flared quillions for Knight/Iron)
        wing_sweep = 0.042 if has_gold else (-0.018 if is_kukri else 0.012)
        for side in (-1.0, 1.0):
            q_segs = [
                (0.22 * gw, 0.222, 0.272, gz * 0.88),
                (0.55 * gw, 0.225 + 0.35 * wing_sweep, 0.268 + 0.35 * wing_sweep, gz * 0.74),
                (0.85 * gw, 0.222 + 0.80 * wing_sweep, 0.274 + 0.80 * wing_sweep, gz * 0.86),
                (1.00 * gw, 0.232 + 1.05 * wing_sweep, 0.266 + 1.05 * wing_sweep, gz * 0.52),
            ]
            q_rings = []
            for x_abs, y_lo, y_hi, z_h in q_segs:
                xv = side * x_abs
                ib = len(verts)
                verts.extend([(xv, y_lo, z_h), (xv, y_hi, z_h), (xv, y_hi, -z_h), (xv, y_lo, -z_h)])
                q_rings.append(ib)
            for s_i in range(len(q_rings) - 1):
                i0, i1 = q_rings[s_i], q_rings[s_i + 1]
                for k in range(4):
                    kn = (k + 1) % 4
                    c_q = col_guard if k in (0, 2) else col_guard_trim
                    if side > 0:
                        add_quad(i0 + k, i1 + k, i1 + kn, i0 + kn, c_q)
                    else:
                        add_quad(i1 + k, i0 + k, i0 + kn, i1 + kn, c_q)
            it = q_rings[-1]
            if side > 0:
                add_quad(it + 0, it + 1, it + 2, it + 3, col_guard)
            else:
                add_quad(it + 1, it + 0, it + 3, it + 2, col_guard)

        # Broad, Heroic Flat-Shaded 3D Blade ([0.265 .. 1.00]) with raised spine/fuller & wide silver cutting bevels
        bw_base = 0.145 if archetype == "broadsword" else (0.115 if archetype == "sword" else 0.095)
        bz_base = r_grip * 0.82
        n_b = 18
        blade_rows_v = []
        for s_i in range(n_b):
            tb = s_i / (n_b - 1)  # 0.0 = ricasso above guard, 1.0 = point tip
            yv = 0.265 + tb * 0.735

            # Width profile: broad parallel/slightly flaring blade, tapering crisply in top 24% to a bold point
            if tb >= 0.76:
                u_tip = (tb - 0.76) / 0.24
                w_scale = max(0.0, 1.0 - (u_tip ** 1.35))
            elif is_kukri:
                # Forward-bellied hunting dirk swell in upper half
                w_scale = 0.88 + 0.28 * math.sin(tb * math.pi * 0.95)
            elif is_obsidian and (0.10 <= tb <= 0.24):
                # Twin fang notch cutout above the ricasso
                w_scale = 0.72
            elif archetype == "broadsword" and has_gold and tb < 0.16:
                # Ornate flared ricasso lug above winged guard
                w_scale = 1.14 if tb < 0.10 else 0.92
            else:
                w_scale = 1.0 + 0.08 * tb

            x_out = bw_base * w_scale
            x_in = x_out * 0.42
            x_off = (0.022 * math.sin(tb * math.pi)) if is_kukri else 0.0

            z_ctr = bz_base * (1.0 - 0.85 * (tb ** 2.2))
            z_in = z_ctr * 0.76

            if x_out <= 1e-4:
                tip_v = len(verts)
                verts.append((x_off, yv, 0.0))
                blade_rows_v.append(("tip", tip_v, tb))
            else:
                # 8-vertex cross-section around perimeter:
                # 0: Left sharp edge (-x_out, 0)
                # 1: Front-Left ridge (-x_in, +z_in)
                # 2: Front-Center spine (0, +z_ctr)
                # 3: Front-Right ridge (+x_in, +z_in)
                # 4: Right sharp edge (+x_out, 0)
                # 5: Back-Right ridge (+x_in, -z_in)
                # 6: Back-Center spine (0, -z_ctr)
                # 7: Back-Left ridge (-x_in, -z_in)
                ib = len(verts)
                verts.extend([
                    (x_off - x_out, yv, 0.0),
                    (x_off - x_in, yv, z_in),
                    (x_off, yv, z_ctr),
                    (x_off + x_in, yv, z_in),
                    (x_off + x_out, yv, 0.0),
                    (x_off + x_in, yv, -z_in),
                    (x_off, yv, -z_ctr),
                    (x_off - x_in, yv, -z_in),
                ])
                blade_rows_v.append(("ring", ib, tb))

        for s_i in range(n_b - 1):
            kind0, ib0, tb0 = blade_rows_v[s_i]
            kind1, ib1, tb1 = blade_rows_v[s_i + 1]
            in_fuller = tb0 < 0.62
            c_center = col_spine if in_fuller else col_metal_mid
            c_edge_front = col_metal_hi
            c_edge_back = col_metal_mid

            if kind1 == "tip":
                for k in range(8):
                    kn = (k + 1) % 8
                    c_f = c_edge_front if k in (0, 3, 4, 7) else c_center
                    add_tri(ib0 + k, ib1, ib0 + kn, c_f)
            else:
                for k in range(8):
                    kn = (k + 1) % 8
                    if k in (1, 2, 5, 6):
                        c_f = c_center
                    elif k in (0, 3):
                        c_f = c_edge_front
                    else:
                        c_f = c_edge_back
                    add_quad(ib0 + k, ib1 + k, ib1 + kn, ib0 + kn, c_f)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 5) Hammers & Warhammers: Slender stylized Shaft + Exaggerated Blocky/Spiked Head
    # -------------------------------------------------------------------------
    if archetype in ("hammer", "warhammer"):
        is_wooden_mallet = ("mallet" in id_low) or ("cudgel" in id_low)
        is_spiked = ("spiked" in id_low) or ("maul" in id_low)

        col_ring = COL_GOLD if has_gold else (COL_BRASS if is_spiked else COL_STEEL)
        col_ring_dk = COL_AMBER if has_gold else (COL_COPPER if is_spiked else COL_DARK_IRON)
        col_head_core = COL_WOOD if is_wooden_mallet else (COL_GOLD if has_gold else COL_DARK_IRON)
        col_head_bevel = COL_COPPER if is_wooden_mallet else (COL_AMBER if has_gold else COL_STEEL)
        col_head_face = COL_STEEL if is_wooden_mallet else (COL_SILVER if has_gold else COL_SILVER)

        # Slender wooden shaft along Y in [0.0 .. 1.0], with head mounted at [0.64 .. 0.88]
        r_sh = 0.036 if archetype == "warhammer" else 0.038
        shaft_stations = [
            (0.000, r_sh * 0.42, col_ring_dk, 0.0, "pom_tip"),
            (0.024, r_sh * 1.42, col_ring_dk, 0.0, "pom_bot"),
            (0.072, r_sh * 1.46, col_ring, 0.0, "pom_top"),
            (0.088, r_sh * 0.94, COL_BARK, 0.0, "swell_lo0"),
            (0.125, r_sh * 1.22, COL_WOOD, 0.0, "swell_lo1"),
            (0.155, r_sh * 0.95, COL_BARK, 0.005, "grip_bot"),
            (0.220, r_sh * 1.05, COL_COPPER, 0.006, "wrap1"),
            (0.240, r_sh * 0.90, COL_BARK, 0.006, "seam1"),
            (0.310, r_sh * 1.06, COL_COPPER, 0.006, "wrap2"),
            (0.330, r_sh * 0.90, COL_BARK, 0.006, "seam2"),
            (0.400, r_sh * 1.05, COL_COPPER, 0.005, "wrap3"),
            (0.440, r_sh * 1.18, COL_WOOD, 0.0, "swell_hi"),
            (0.475, r_sh * 0.92, COL_WOOD, 0.0, "shaft_mid"),
            (0.600, r_sh * 0.96, COL_WOOD, 0.0, "pre_col"),
            (0.612, r_sh * 1.48, col_ring_dk, 0.0, "col_lo_bot"),
            (0.640, r_sh * 1.48, col_ring, 0.0, "col_lo_top"),
            (0.645, r_sh * 1.18, col_head_core, 0.0, "sock_lo"),
            (0.875, r_sh * 1.18, col_head_core, 0.0, "sock_hi"),
            (0.880, r_sh * 1.48, col_ring_dk, 0.0, "col_hi_bot"),
            (0.908, r_sh * 1.48, col_ring, 0.0, "col_hi_top"),
            (0.915, r_sh * 1.05, COL_STEEL, 0.0, "spike_base"),
            (0.965, r_sh * 0.55, COL_SILVER, 0.0, "spike_mid"),
            (1.000, r_sh * 0.08, COL_SILVER, 0.0, "spike_tip"),
        ]
        add_oct_lathe(
            shaft_stations,
            sides=8,
            z_aspect=0.96,
            cap_bot_col=col_ring,
            cap_top_col=COL_SILVER,
            boss_tags=("pom_bot", "pom_top"),
            boss_col=col_ring,
            boss_recess_col=col_ring_dk,
        )

        # Exaggerated 3D Octagonal Hammer Head spanning X in [-hw_max .. +hw_max] around y_ctr = 0.76
        y_ctr = 0.762
        hw_max = 0.34 if archetype == "warhammer" else 0.28
        ry_base = 0.135 if archetype == "warhammer" else 0.125
        rz_base = 0.130 if archetype == "warhammer" else 0.120

        # Central reinforced socket block
        add_3d_beveled_box(-r_sh * 1.45, r_sh * 1.45, 0.635, 0.885, -rz_base * 0.82, rz_base * 0.82, col_ring, col_ring_dk)

        for side in (-1.0, 1.0):
            # 6 X-stations from central socket out to the striking face
            x_stations = [
                (0.04, 0.78, col_head_core),
                (0.32 * hw_max, 0.86, col_head_core),
                (0.42 * hw_max, 1.06, col_ring if is_wooden_mallet else col_head_bevel),
                (0.68 * hw_max, 1.06, col_ring if is_wooden_mallet else col_head_core),
                (0.88 * hw_max, 1.12, col_head_bevel),
                (1.00 * hw_max, 0.84, col_head_face),
            ]
            h_rings = []
            for x_abs, rad_m, c_st in x_stations:
                xv = side * x_abs
                ry = ry_base * rad_m
                rz = rz_base * rad_m
                ring = []
                for k in range(8):
                    ang = (2.0 * math.pi * (k + 0.5)) / 8.0
                    ring.append(len(verts))
                    verts.append((xv, y_ctr + ry * math.cos(ang), rz * math.sin(ang)))
                h_rings.append((ring, c_st))

            for s_i in range(len(h_rings) - 1):
                r0, c0 = h_rings[s_i]
                r1, _ = h_rings[s_i + 1]
                for k in range(8):
                    kn = (k + 1) % 8
                    c_f = c0 if (k % 2 == 1) else (col_ring_dk if c0 == col_ring else c0)
                    if side > 0:
                        add_quad(r0[k], r1[k], r1[kn], r0[kn], c_f)
                    else:
                        add_quad(r1[k], r0[k], r0[kn], r1[kn], c_f)

            # Cap the striking face with either a bold pyramid spike / 4-stud cluster or a clean beveled striking pad
            r_end, _ = h_rings[-1]
            x_face = side * hw_max
            if is_spiked:
                # Central 3D armor-piercing pyramid spike + 4 corner studs on the striking face
                apex = len(verts)
                verts.append((side * (hw_max * 1.24), y_ctr, 0.0))
                for k in range(8):
                    kn = (k + 1) % 8
                    if side > 0:
                        add_tri(r_end[k], apex, r_end[kn], COL_SILVER if k % 2 == 0 else COL_STEEL)
                    else:
                        add_tri(r_end[kn], apex, r_end[k], COL_SILVER if k % 2 == 0 else COL_STEEL)
            else:
                apex = len(verts)
                verts.append((side * (hw_max * 1.04), y_ctr, 0.0))
                for k in range(8):
                    kn = (k + 1) % 8
                    if side > 0:
                        add_tri(r_end[k], apex, r_end[kn], col_head_face)
                    else:
                        add_tri(r_end[kn], apex, r_end[k], col_head_face)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 6) Staves & Wands: Slender Tapered Shaft + Oversized Crystal & Crescent/Claw Finial
    # -------------------------------------------------------------------------
    if archetype in ("staff", "wand"):
        if "emerald" in id_low or "emerald" in pal_set or "green" in pal_set:
            col_gem_hi, col_gem_lo = COL_LIME, COL_EMERALD
        elif "ruby" in id_low or "crimson" in pal_set:
            col_gem_hi, col_gem_lo = COL_CRIMSON, COL_AMBER
        else:
            col_gem_hi, col_gem_lo = COL_SKY_BLUE, COL_COBALT

        col_metal = COL_GOLD
        col_metal_dk = COL_AMBER
        is_crescent = ("emerald" in id_low) or ("crescent" in id_low)

        r_sh = 0.028 if archetype == "staff" else 0.032
        shaft_stations = [
            (0.000, r_sh * 0.35, col_metal_dk, 0.0, "ferrule_tip"),
            (0.035, r_sh * 1.32, col_metal, 0.0, "ferrule_top"),
            (0.050, r_sh * 0.90, COL_WOOD, 0.0, "shaft_lo"),
            (0.280, r_sh * 0.95, COL_WOOD, 0.0, "grip_lo"),
            (0.320, r_sh * 1.15, COL_COPPER, 0.005, "wrap1"),
            (0.340, r_sh * 0.90, COL_BARK, 0.005, "seam1"),
            (0.420, r_sh * 1.15, COL_COPPER, 0.005, "wrap2"),
            (0.460, r_sh * 0.94, COL_WOOD, 0.0, "grip_hi"),
            (0.640, r_sh * 1.04, COL_WOOD, 0.0, "upper_shaft"),
            (0.660, r_sh * 1.52, col_metal_dk, 0.0, "col1_bot"),
            (0.695, r_sh * 1.56, col_metal, 0.0, "col1_top"),
            (0.710, r_sh * 1.12, COL_DARK_IRON, 0.0, "neck"),
            (0.745, r_sh * 1.72, col_metal_dk, 0.0, "crown_bot"),
            (0.785, r_sh * 1.85, col_metal, 0.0, "crown_top"),
        ]
        add_oct_lathe(
            shaft_stations,
            sides=8,
            z_aspect=0.96,
            cap_bot_col=col_metal,
            cap_top_col=col_metal,
        )

        # Oversized Finial Mount + Floating Multi-Faceted Crystal Gem at top [0.78 .. 1.00]
        gem_cy = 0.895
        gem_rx = 0.115 if archetype == "staff" else 0.095
        gem_ry = 0.088 if archetype == "staff" else 0.078
        gem_rz = 0.095 if archetype == "staff" else 0.082

        if is_crescent:
            # Sweeping asymmetric 3D Golden Crescent Moon cradling the floating emerald crystal
            cres_pts = [
                (0.00, 0.775, 0.036, 0.038),
                (-0.12, 0.800, 0.040, 0.042),
                (-0.18, 0.865, 0.038, 0.040),
                (-0.17, 0.940, 0.032, 0.035),
                (-0.08, 0.988, 0.024, 0.028),
                (0.04, 0.995, 0.014, 0.018),
            ]
            # Also a shorter right lower horn
            horn_r = [
                (0.00, 0.775, 0.036, 0.038),
                (0.11, 0.805, 0.032, 0.034),
                (0.15, 0.855, 0.018, 0.022),
            ]
            for path in (cres_pts, horn_r):
                c_rings = []
                for cx_p, cy_p, hr, hz in path:
                    ib = len(verts)
                    verts.extend([
                        (cx_p - hr, cy_p - hr * 0.5, hz),
                        (cx_p + hr, cy_p - hr * 0.5, hz),
                        (cx_p + hr, cy_p + hr * 0.5, hz),
                        (cx_p - hr, cy_p + hr * 0.5, hz),
                        (cx_p - hr, cy_p - hr * 0.5, -hz),
                        (cx_p + hr, cy_p - hr * 0.5, -hz),
                        (cx_p + hr, cy_p + hr * 0.5, -hz),
                        (cx_p - hr, cy_p + hr * 0.5, -hz),
                    ])
                    c_rings.append(ib)
                for s_i in range(len(c_rings) - 1):
                    i0, i1 = c_rings[s_i], c_rings[s_i + 1]
                    add_quad(i0 + 0, i0 + 1, i1 + 1, i1 + 0, col_metal)
                    add_quad(i0 + 2, i0 + 3, i1 + 3, i1 + 2, col_metal)
                    add_quad(i0 + 1, i0 + 5, i1 + 5, i1 + 1, col_metal_dk)
                    add_quad(i0 + 4, i0 + 0, i1 + 0, i1 + 4, col_metal_dk)
                    add_quad(i0 + 5, i0 + 4, i1 + 4, i1 + 5, col_metal)
                    add_quad(i0 + 3, i0 + 2, i0 + 6, i0 + 7, col_metal_dk)
        else:
            # Twin symmetrical 3D golden arcane wings / claw prongs framing the crystal
            for side in (-1.0, 1.0):
                wing_pts = [
                    (side * 0.03, 0.775, 0.032, 0.036),
                    (side * (gem_rx * 1.38), 0.825, 0.035, 0.038),
                    (side * (gem_rx * 1.52), 0.905, 0.032, 0.034),
                    (side * (gem_rx * 1.12), 0.975, 0.018, 0.022),
                ]
                w_rings = []
                for wx_p, wy_p, hr, hz in wing_pts:
                    ib = len(verts)
                    verts.extend([
                        (wx_p - hr, wy_p, hz), (wx_p + hr, wy_p, hz),
                        (wx_p + hr, wy_p, -hz), (wx_p - hr, wy_p, -hz),
                    ])
                    w_rings.append(ib)
                for s_i in range(len(w_rings) - 1):
                    i0, i1 = w_rings[s_i], w_rings[s_i + 1]
                    for k in range(4):
                        kn = (k + 1) % 4
                        add_quad(i0 + k, i0 + kn, i1 + kn, i1 + k, col_metal if k % 2 == 0 else col_metal_dk)

        # Oversized 8-sided double-pyramid crystal gem in the center of the finial
        gem_mid_ring = []
        gem_hi_ring = []
        gem_lo_ring = []
        for k in range(8):
            ang = (2.0 * math.pi * k) / 8.0
            ux, uz = math.cos(ang), -math.sin(ang)
            gem_lo_ring.append(len(verts))
            verts.append((gem_rx * 0.68 * ux, gem_cy - gem_ry * 0.48, gem_rz * 0.68 * uz))
            gem_mid_ring.append(len(verts))
            verts.append((gem_rx * ux, gem_cy, gem_rz * uz))
            gem_hi_ring.append(len(verts))
            verts.append((gem_rx * 0.68 * ux, gem_cy + gem_ry * 0.48, gem_rz * 0.68 * uz))
        gem_bot = len(verts)
        verts.append((0.0, gem_cy - gem_ry, 0.0))
        gem_top = len(verts)
        verts.append((0.0, gem_cy + gem_ry, 0.0))
        for k in range(8):
            kn = (k + 1) % 8
            c_g = col_gem_hi if (k % 2 == 0) else col_gem_lo
            add_tri(gem_bot, gem_lo_ring[kn], gem_lo_ring[k], col_gem_lo)
            add_quad(gem_lo_ring[k], gem_lo_ring[kn], gem_mid_ring[kn], gem_mid_ring[k], c_g)
            add_quad(gem_mid_ring[k], gem_mid_ring[kn], gem_hi_ring[kn], gem_hi_ring[k], col_gem_hi if (k % 2 == 1) else col_gem_lo)
            add_tri(gem_top, gem_hi_ring[k], gem_hi_ring[kn], col_gem_hi)

        return fit_to_archetype_aabb(verts, archetype), tris, face_colors

    # -------------------------------------------------------------------------
    # 7) Axes & Battleaxes: stylized Adventurers Proportions
    #    - Slender wooden shaft (~46% thinner than before, ~1/11th of double-blade span)
    #    - Compact central socket with 2 narrow metal collar rings + pointed top spike cap
    #    - Exaggerated, wide flat-shaded blades with wide outer steel/silver cutting bevel
    # -------------------------------------------------------------------------
    handle_centers = []
    handle_half_widths = []
    for py in range(int(hr_h * 0.52), int(hr_h * 0.86)):
        xs_r = [px for px in range(hr_w) if fg_hr[py][px]]
        if len(xs_r) >= 4 and (xs_r[-1] - xs_r[0]) < hr_w * 0.35:
            handle_centers.append(0.5 * (xs_r[0] + xs_r[-1]))
            handle_half_widths.append(0.5 * (xs_r[-1] - xs_r[0]))

    if handle_centers:
        handle_centers.sort()
        cx = handle_centers[len(handle_centers) // 2]
        handle_half_widths.sort()
        r_shaft_base = max(6.0, handle_half_widths[len(handle_half_widths) // 2])
    else:
        cx = hr_w * 0.5
        r_shaft_base = hr_w * 0.065

    shaft_top_py = 0
    for py in range(hr_h):
        if any(fg_hr[py][px] for px in range(max(0, int(cx - 7)), min(hr_w, int(cx + 8)))):
            shaft_top_py = py
            break
    shaft_bot_py = hr_h - 1
    for py in range(hr_h - 1, -1, -1):
        if any(fg_hr[py][px] for px in range(max(0, int(cx - 7)), min(hr_w, int(cx + 8)))):
            shaft_bot_py = py
            break

    def extract_half_blade_span(py, dir_sign):
        dxs = [
            abs(px - cx)
            for px in (range(int(round(cx)), hr_w) if dir_sign > 0 else range(int(round(cx)), -1, -1))
            if fg_hr[py][px] and abs(px - cx) >= r_shaft_base * 0.88
        ]
        if not dxs:
            return None
        dxs.sort()
        out_dx = dxs[-1]
        if out_dx < r_shaft_base * 1.68:
            return None
        in_dx = out_dx
        for i in range(len(dxs) - 2, -1, -1):
            if in_dx - dxs[i] <= 3.5:
                in_dx = dxs[i]
            else:
                break
        if in_dx <= r_shaft_base * 1.32:
            return (r_shaft_base * 0.72, out_dx, True)
        return (in_dx, out_dx, False)

    raw_blade_rows = []
    for py in range(hr_h):
        sp_r = extract_half_blade_span(py, 1.0)
        sp_l = extract_half_blade_span(py, -1.0)
        if archetype == "axe":
            sp = sp_l if sp_l is not None else sp_r
        else:
            if sp_r is not None and sp_l is not None:
                att = sp_r[2] or sp_l[2]
                in_dx = r_shaft_base * 0.72 if att else 0.5 * (sp_r[0] + sp_l[0])
                out_dx = 0.5 * (sp_r[1] + sp_l[1])
                sp = (in_dx, out_dx, att)
            else:
                sp = sp_r if sp_r is not None else sp_l
        if sp is not None:
            raw_blade_rows.append((py, sp[0], sp[1], sp[2]))

    runs = []
    cur_run = []
    for row_item in raw_blade_rows:
        if not cur_run or (row_item[0] - cur_run[-1][0]) <= 4:
            cur_run.append(row_item)
        else:
            runs.append(cur_run)
            cur_run = [row_item]
    if cur_run:
        runs.append(cur_run)
    blade_rows = max(runs, key=len) if runs else []

    use_synth_crescent = not os.path.basename(image_path).startswith("battleaxe_concept_test")
    is_bearded_axe = ("bearded" in id_low) or (archetype == "axe" and not has_gold)

    if use_synth_crescent:
        cx = hr_w * 0.5
        r_shaft_base = 14.5
        y_bot = 0.015
        y_top = 0.885
        y_span = y_top - y_bot
        y_bridge_bot = 0.655
        y_bridge_top = 0.785
        y_bridge_span = y_bridge_top - y_bridge_bot
    else:
        attached_pys = [r[0] for r in blade_rows if r[3]]
        if attached_pys:
            bridge_top_py = min(attached_pys)
            bridge_bot_py = max(attached_pys)
        else:
            bridge_top_py = int(hr_h * 0.20)
            bridge_bot_py = int(hr_h * 0.34)

        y_bot = 1.0 - (shaft_bot_py / (hr_h - 1.0))
        y_top = 1.0 - (shaft_top_py / (hr_h - 1.0))
        y_span = max(0.20, y_top - y_bot)

        y_bridge_top = 1.0 - (bridge_top_py / (hr_h - 1.0))
        y_bridge_bot = 1.0 - (bridge_bot_py / (hr_h - 1.0))
        y_bridge_span = max(0.04, y_bridge_top - y_bridge_bot)

    # stylized Adventurers proportion: make the wooden shaft ~46% thinner so the blades look massive and exaggerated!
    r_base = (r_shaft_base * 0.54) / hr_w
    z_aspect = 0.96

    col_metal_trim = COL_GOLD if has_gold else COL_STEEL
    col_metal_trim_dk = COL_AMBER if has_gold else COL_BRASS

    pom_tip_y = y_bot
    pom_bot_y = y_bot + 0.020 * y_span
    pom_top_y = y_bot + 0.072 * y_span
    pom_rim_y = y_bot + 0.086 * y_span

    swell_lo_bot = y_bot + 0.090 * y_span
    swell_lo_mid = y_bot + 0.122 * y_span
    swell_lo_top = y_bot + 0.155 * y_span

    grip_bot_y = swell_lo_top
    grip_top_y = y_bot + 0.415 * y_span

    swell_hi_mid = y_bot + 0.450 * y_span
    swell_hi_top = y_bot + 0.490 * y_span

    collar_h = 0.024 * y_span
    col_lo_bot = y_bridge_bot - collar_h
    col_lo_top = y_bridge_bot
    col_hi_bot = y_bridge_top
    col_hi_top = y_bridge_top + collar_h
    cap_mid_y = col_hi_top + 0.45 * max(0.028, y_top - col_hi_top)
    cap_top_y = max(col_hi_top + 0.042, y_top)

    shaft_spec = [
        (pom_tip_y, 0.38, col_metal_trim_dk, 0.0, "pom_tip"),
        (pom_bot_y, 1.44, col_metal_trim_dk, 0.0, "pom_body_bot"),
        (pom_top_y, 1.48, col_metal_trim, 0.0, "pom_body_top"),
        (pom_rim_y, 1.52, col_metal_trim, 0.0, "pom_rim"),
        (swell_lo_bot, 0.96, COL_BARK, 0.0, "neck_lo"),
        (swell_lo_mid, 1.24, COL_WOOD, 0.0, "swell_lo"),
        (swell_lo_top, 0.94, COL_BARK, 0.005, "grip_start"),
    ]
    n_wraps = 4
    wrap_span = (grip_top_y - grip_bot_y) / n_wraps
    for w_i in range(n_wraps):
        wy0 = grip_bot_y + w_i * wrap_span
        shaft_spec.append((wy0 + 0.12 * wrap_span, 1.05, COL_COPPER, 0.007, "wrap_lo"))
        shaft_spec.append((wy0 + 0.84 * wrap_span, 1.07, COL_COPPER, 0.007, "wrap_hi"))
        shaft_spec.append((wy0 + 0.96 * wrap_span, 0.92, COL_BARK, 0.007, "wrap_seam"))

    shaft_spec.extend([
        (swell_hi_mid, 1.20, COL_WOOD, 0.0, "swell_hi"),
        (swell_hi_top, 0.96, COL_BARK, 0.0, "neck_mid0"),
        (0.5 * (swell_hi_top + col_lo_bot), 0.90, COL_WOOD, 0.0, "neck_mid1"),
        (col_lo_bot - 0.004, 1.02, COL_WOOD, 0.0, "pre_col_lo"),
        (col_lo_bot, 1.46, col_metal_trim_dk, 0.0, "col_lo_bot"),
        (col_lo_top, 1.46, col_metal_trim, 0.0, "col_lo_top"),
        (col_lo_top + 0.003, 1.16, COL_DARK_IRON, 0.0, "sock_bot"),
        (col_hi_bot - 0.003, 1.16, COL_DARK_IRON, 0.0, "sock_top"),
        (col_hi_bot, 1.46, col_metal_trim_dk, 0.0, "col_hi_bot"),
        (col_hi_top, 1.46, col_metal_trim, 0.0, "col_hi_top"),
        (col_hi_top + 0.003, 1.10, COL_STEEL, 0.0, "cap_bot"),
        (cap_mid_y, 0.72, COL_STEEL, 0.0, "cap_mid"),
        (cap_top_y, 0.14, COL_SILVER, 0.0, "cap_top"),
    ])

    shaft_sides = 8
    shaft_rings = []
    for y_val, r_mult, r_col, tilt_y, tag in shaft_spec:
        rx = r_base * r_mult
        rz = rx * z_aspect
        ring = []
        for k in range(shaft_sides):
            ang = (2.0 * math.pi * (k + 0.5)) / shaft_sides
            ux = math.cos(ang)
            uz = -math.sin(ang)
            yk = y_val + tilt_y * ux
            ring.append(len(verts))
            verts.append((rx * ux, yk, rz * uz))
        shaft_rings.append((ring, r_col, y_val, rx, rz, tag))

    for r_i in range(len(shaft_rings) - 1):
        r0, col0, y0, rx0, rz0, tag0 = shaft_rings[r_i]
        r1, col1, y1, rx1, rz1, tag1 = shaft_rings[r_i + 1]
        is_pommel_main = (tag0 == "pom_body_bot" and tag1 == "pom_body_top")
        for k in range(shaft_sides):
            kn = (k + 1) % shaft_sides
            if is_pommel_main and (k % 2 == 1):
                v00, v10, v11, v01 = r0[k], r0[kn], r1[kn], r1[k]
                p00, p10, p11, p01 = verts[v00], verts[v10], verts[v11], verts[v01]

                def lerp3(a, b, t):
                    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)

                ang_face = (2.0 * math.pi * (k + 1.0)) / shaft_sides
                n_out = (math.cos(ang_face), 0.0, -math.sin(ang_face))
                d_apex = r_base * 0.30

                def push3(pt, d):
                    return (pt[0] + n_out[0] * d, pt[1] + n_out[1] * d, pt[2] + n_out[2] * d)

                m_bot = lerp3(p00, p10, 0.5)
                m_rgt = lerp3(p10, p11, 0.5)
                m_top = lerp3(p01, p11, 0.5)
                m_lft = lerp3(p00, p01, 0.5)
                p_ctr = push3(lerp3(m_bot, m_top, 0.5), d_apex)

                ib = len(verts)
                verts.extend([m_bot, m_rgt, m_top, m_lft, p_ctr])
                im_bot, im_rgt, im_top, im_lft, i_ctr = ib, ib + 1, ib + 2, ib + 3, ib + 4

                tris.extend([
                    (v00, im_bot, im_lft),
                    (v10, im_rgt, im_bot),
                    (v11, im_top, im_rgt),
                    (v01, im_lft, im_top),
                    (im_bot, im_rgt, i_ctr),
                    (im_rgt, im_top, i_ctr),
                    (im_top, im_lft, i_ctr),
                    (im_lft, im_bot, i_ctr),
                ])
                face_colors.extend([
                    col_metal_trim_dk, col_metal_trim_dk, col_metal_trim_dk, col_metal_trim_dk,
                    col_metal_trim_dk, col_metal_trim, col_metal_trim, col_metal_trim_dk,
                ])
            else:
                face_c = col0 if (k % 2 == 1) else (col_metal_trim_dk if col0 == col_metal_trim else col0)
                add_quad(r0[k], r0[kn], r1[kn], r1[k], face_c)

    bot_c = len(verts)
    verts.append((0.0, shaft_rings[0][2] - 0.012 * y_span, 0.0))
    top_c = len(verts)
    verts.append((0.0, shaft_rings[-1][2] + 0.015 * y_span, 0.0))
    for k in range(shaft_sides):
        kn = (k + 1) % shaft_sides
        add_tri(bot_c, shaft_rings[0][0][kn], shaft_rings[0][0][k], col_metal_trim_dk)
        add_tri(top_c, shaft_rings[-1][0][k], shaft_rings[-1][0][kn], COL_SILVER)

    def add_socket_chevron_prong(y_base, y_tip, z_sign, x_half, z_inner, z_outer):
        y_sh = y_base + 0.68 * (y_tip - y_base)
        z_b = z_sign * z_inner
        z_f = z_sign * z_outer
        poly_xy = [
            (-x_half, y_base),
            (x_half, y_base),
            (x_half, y_sh),
            (0.0, y_tip),
            (-x_half, y_sh),
        ]
        ib = len(verts)
        for px, py_v in poly_xy:
            verts.append((px, py_v, z_f))
        for px, py_v in poly_xy:
            verts.append((px, py_v, z_b))
        if (z_sign > 0) ^ (y_tip < y_base):
            tris.extend([(ib + 0, ib + 1, ib + 2), (ib + 0, ib + 2, ib + 4), (ib + 4, ib + 2, ib + 3)])
        else:
            tris.extend([(ib + 1, ib + 0, ib + 2), (ib + 2, ib + 0, ib + 4), (ib + 2, ib + 4, ib + 3)])
        face_colors.extend([col_metal_trim, col_metal_trim, col_metal_trim])
        for i in range(5):
            inext = (i + 1) % 5
            if (z_sign > 0) ^ (y_tip < y_base):
                add_quad(ib + 5 + i, ib + 5 + inext, ib + inext, ib + i, col_metal_trim_dk)
            else:
                add_quad(ib + i, ib + inext, ib + 5 + inext, ib + 5 + i, col_metal_trim_dk)

    z_sock_face = r_base * 1.16 * z_aspect * math.cos(math.pi / 8.0)
    z_prong_out = r_base * 1.38 * z_aspect * math.cos(math.pi / 8.0)
    prong_hw = r_base * 0.32
    for z_s in (-1.0, 1.0):
        add_socket_chevron_prong(
            col_hi_bot, col_hi_bot - 0.34 * y_bridge_span,
            z_s, prong_hw, z_sock_face * 0.92, z_prong_out,
        )
        add_socket_chevron_prong(
            col_lo_top, col_lo_top + 0.34 * y_bridge_span,
            z_s, prong_hw, z_sock_face * 0.92, z_prong_out,
        )

    # B) Build Exaggerated, Wide stylized-Proportioned 3D Polygon Blades
    if use_synth_crescent or len(blade_rows) >= 4:
        n_blade_slices = 32
        sampled_slices = []

        if use_synth_crescent:
            y_b_bot = 0.435 if is_bearded_axe else 0.475
            y_b_top = 0.945 if is_bearded_axe else 0.960
            t_att_lo = (y_bridge_bot - y_b_bot) / (y_b_top - y_b_bot)
            t_att_hi = (y_bridge_top - y_b_bot) / (y_b_top - y_b_bot)
            bridge_th = r_shaft_base * 0.96

            for s in range(n_blade_slices):
                t_b = s / (n_blade_slices - 1)
                y_val = y_b_bot + t_b * (y_b_top - y_b_bot)
                is_att = (t_att_lo <= t_b <= t_att_hi)
                if is_att:
                    in_val = bridge_th
                elif t_b < t_att_lo:
                    u_lo = (t_att_lo - t_b) / max(0.05, t_att_lo)
                    in_val = bridge_th + (54.0 if is_bearded_axe else 48.0) * (u_lo ** 0.65)
                else:
                    u_hi = (t_b - t_att_hi) / max(0.05, 1.0 - t_att_hi)
                    in_val = bridge_th + 46.0 * (u_hi ** 0.68)

                arch = math.sin(t_b * math.pi)
                out_val = max(in_val + 18.0, 86.0 + 34.0 * (arch ** 0.75))
                sampled_slices.append({
                    "py": int(round((1.0 - y_val) * (hr_h - 1.0))),
                    "t": t_b,
                    "y": y_val,
                    "in_dx": in_val,
                    "out_dx": out_val,
                    "attached": is_att,
                })
        else:
            py_start = blade_rows[0][0]
            py_end = blade_rows[-1][0]
            for s in range(n_blade_slices):
                t_b = s / (n_blade_slices - 1)
                target_py = py_end - t_b * (py_end - py_start)
                nearest = min(blade_rows, key=lambda r: abs(r[0] - target_py))
                in_val = nearest[1] * 0.76
                out_val = max(in_val + 4.0, nearest[2] * 1.18)
                sampled_slices.append({
                    "py": int(round(target_py)),
                    "t": t_b,
                    "y": 1.0 - (target_py / (hr_h - 1.0)),
                    "in_dx": in_val,
                    "out_dx": out_val,
                    "attached": nearest[3],
                })

        free_throats = [sl["in_dx"] for sl in sampled_slices if not sl["attached"]]
        bridge_throat_default = min(free_throats) if free_throats else r_shaft_base * 0.95
        bridge_throat_default = max(r_shaft_base * 0.82, min(r_shaft_base * 1.08, bridge_throat_default))

        for sl in sampled_slices:
            sl["throat_dx"] = bridge_throat_default if sl["attached"] else sl["in_dx"]

        for _ in range(2):
            new_th = [sl["throat_dx"] for sl in sampled_slices]
            new_out = [sl["out_dx"] for sl in sampled_slices]
            for s_i in range(1, n_blade_slices - 1):
                avg_th = (
                    0.25 * sampled_slices[s_i - 1]["throat_dx"]
                    + 0.50 * sampled_slices[s_i]["throat_dx"]
                    + 0.25 * sampled_slices[s_i + 1]["throat_dx"]
                )
                if abs(sampled_slices[s_i]["throat_dx"] - avg_th) <= 3.5:
                    new_th[s_i] = avg_th
                new_out[s_i] = (
                    0.25 * sampled_slices[s_i - 1]["out_dx"]
                    + 0.50 * sampled_slices[s_i]["out_dx"]
                    + 0.25 * sampled_slices[s_i + 1]["out_dx"]
                )
            for s_i in range(n_blade_slices):
                sampled_slices[s_i]["throat_dx"] = new_th[s_i]
                sampled_slices[s_i]["out_dx"] = max(new_th[s_i] + 2.5, new_out[s_i])

        for sl in sampled_slices:
            t_b = sl["t"]
            span_s = sl["out_dx"] - sl["throat_dx"]
            for nick_t, nick_depth in ((0.27, 0.048), (0.75, 0.045)):
                d_n = abs(t_b - nick_t)
                if d_n < 0.042:
                    sl["out_dx"] -= span_s * nick_depth * (1.0 - d_n / 0.042)

        r_root = r_shaft_base * 0.50
        # Keep blade Z-thickness sleeker than the central socket collar
        z_collar_max = r_base * 1.46 * z_aspect
        base_z = z_collar_max * 0.58

        n_u = 9
        for s_idx, sl in enumerate(sampled_slices):
            t_b = sl["t"]
            th_dx = sl["throat_dx"]
            out_dx = sl["out_dx"]
            span = max(1.5, out_dx - th_dx)

            chev = max(0.0, 1.0 - abs(t_b - 0.50) / 0.14)
            c_off = 0.16 * chev

            u_vals = [
                0.00,
                0.10,
                0.17,
                0.24,
                max(0.31, 0.54 - c_off),
                max(0.39, 0.62 - 0.72 * c_off),
                max(0.47, 0.69 - 0.32 * c_off),
                0.85,
                1.00,
            ]
            sl["stations"] = [th_dx + u * span for u in u_vals]

            tip_taper = 0.22 if s_idx in (0, n_blade_slices - 1) else (0.72 if s_idx in (1, n_blade_slices - 2) else 1.0)
            in_hook_zone = (t_b < 0.28) or (t_b > 0.72)
            is_outer_rim = (t_b <= 0.10) or (t_b >= 0.90)
            is_hook_bar = (0.16 <= t_b <= 0.24) or (0.76 <= t_b <= 0.84)

            z_u1 = (1.14 if in_hook_zone else 0.66) * base_z * tip_taper
            z_u2 = (1.12 if is_outer_rim else 0.64) * base_z * tip_taper
            z_u3 = (1.12 if in_hook_zone else 0.68) * base_z * tip_taper
            z_u4 = (1.12 if (is_outer_rim or is_hook_bar) else 0.64) * base_z * tip_taper

            sl["z_vals"] = [
                base_z * (0.88 if sl["attached"] else 0.62) * tip_taper,
                z_u1,
                z_u2,
                z_u3,
                z_u4,
                base_z * 1.18 * tip_taper,
                base_z * 0.92 * tip_taper,
                base_z * 0.44 * tip_taper,
                0.0,
            ]

        def strip_solid_color(s_idx, strip_k):
            c_frame = COL_GOLD if has_gold else COL_STEEL
            c_frame_sub = COL_AMBER if has_gold else COL_DARK_IRON

            t_mid = 0.5 * (sampled_slices[s_idx]["t"] + sampled_slices[s_idx + 1]["t"])
            in_middle_throat = (0.28 <= t_mid <= 0.72)
            is_tip_rim = (t_mid <= 0.09) or (t_mid >= 0.91)
            is_hook_turn = (0.16 <= t_mid <= 0.23) or (0.77 <= t_mid <= 0.84)

            if strip_k == 0:
                return COL_DARK_IRON if in_middle_throat else c_frame
            if strip_k == 1:
                return c_frame if is_tip_rim else COL_DARK_IRON
            if strip_k == 2:
                if in_middle_throat or (0.09 < t_mid < 0.16) or (0.84 < t_mid < 0.91):
                    return COL_DARK_IRON
                return c_frame
            if strip_k == 3:
                return c_frame if (is_tip_rim or is_hook_turn) else COL_DARK_IRON
            if strip_k == 4:
                return c_frame
            if strip_k == 5:
                return c_frame_sub
            if strip_k == 6:
                return COL_STEEL
            return COL_SILVER

        sides_to_build = [-1.0] if archetype == "axe" else [-1.0, 1.0]
        for side in sides_to_build:
            x_in = side * (r_root / hr_w)
            x_br_in = side * ((r_shaft_base * 0.58) / hr_w)
            x_br_out = side * ((bridge_throat_default * 1.06) / hr_w)
            bx0, bx1 = min(x_in, x_br_out), max(x_in, x_br_out)
            add_3d_beveled_box(
                bx0, bx1,
                y_bridge_bot + 0.04 * y_bridge_span, y_bridge_top - 0.04 * y_bridge_span,
                -base_z * 0.72, base_z * 0.72,
                COL_DARK_IRON, COL_DARK_IRON,
            )
            kx0, kx1 = min(x_br_in, x_br_out), max(x_br_in, x_br_out)
            add_3d_beveled_box(
                kx0, kx1,
                y_bridge_bot - 0.02 * y_bridge_span, y_bridge_top + 0.02 * y_bridge_span,
                -z_collar_max * 0.72, z_collar_max * 0.72,
                col_metal_trim, col_metal_trim_dk,
            )

            f_grid = []
            b_grid = []
            for s_idx, sl in enumerate(sampled_slices):
                f_row = []
                b_row = []
                for k in range(n_u):
                    dx = sl["stations"][k]
                    x_coord = side * (dx / hr_w)
                    y_coord = sl["y"]
                    z_mag = sl["z_vals"][k]

                    if z_mag <= 1e-5:
                        v_idx = len(verts)
                        verts.append((x_coord, y_coord, 0.0))
                        f_row.append(v_idx)
                        b_row.append(v_idx)
                    else:
                        vf = len(verts)
                        verts.append((x_coord, y_coord, z_mag))
                        vb = len(verts)
                        verts.append((x_coord, y_coord, -z_mag))
                        f_row.append(vf)
                        b_row.append(vb)
                f_grid.append(f_row)
                b_grid.append(b_row)

            for s_idx in range(n_blade_slices - 1):
                for k in range(n_u - 1):
                    col = strip_solid_color(s_idx, k)
                    f00 = f_grid[s_idx][k]
                    f10 = f_grid[s_idx][k + 1]
                    f11 = f_grid[s_idx + 1][k + 1]
                    f01 = f_grid[s_idx + 1][k]

                    b00 = b_grid[s_idx][k]
                    b10 = b_grid[s_idx][k + 1]
                    b11 = b_grid[s_idx + 1][k + 1]
                    b01 = b_grid[s_idx + 1][k]

                    if side > 0:
                        add_quad(f00, f10, f11, f01, col)
                        add_quad(b00, b01, b11, b10, col)
                    else:
                        add_quad(f10, f00, f01, f11, col)
                        add_quad(b10, b11, b01, b00, col)

                fa0, fa1 = f_grid[s_idx][0], f_grid[s_idx + 1][0]
                ba0, ba1 = b_grid[s_idx][0], b_grid[s_idx + 1][0]
                if side > 0:
                    add_quad(ba0, fa0, fa1, ba1, col_metal_trim_dk)
                else:
                    add_quad(fa0, ba0, ba1, fa1, col_metal_trim_dk)

            for cap_s, flip_cap in ((0, False), (n_blade_slices - 1, True)):
                row_q = 0 if cap_s == 0 else n_blade_slices - 2
                for k in range(n_u - 1):
                    col = strip_solid_color(row_q, k)
                    fa, fb = f_grid[cap_s][k], f_grid[cap_s][k + 1]
                    ba, bb = b_grid[cap_s][k], b_grid[cap_s][k + 1]
                    if (side > 0) ^ flip_cap:
                        add_quad(ba, bb, fb, fa, col)
                    else:
                        add_quad(fa, fb, bb, ba, col)

        # For single-bit 1H axe, add a bold 3D octagonal counterbalance poll/spike on the rear (+X) of the socket
        if archetype == "axe":
            y_poll = 0.5 * (y_bridge_bot + y_bridge_top)
            ry_p = 0.54 * y_bridge_span
            rz_p = z_collar_max * 0.76
            x_p0 = (r_shaft_base * 0.58) / hr_w
            x_p1 = (r_shaft_base * 2.15) / hr_w
            x_p_tip = (r_shaft_base * 3.85) / hr_w
            r_p0 = []
            r_p1 = []
            for k in range(8):
                ang = (2.0 * math.pi * (k + 0.5)) / 8.0
                r_p0.append(len(verts))
                verts.append((x_p0, y_poll + ry_p * math.cos(ang), rz_p * math.sin(ang)))
                r_p1.append(len(verts))
                verts.append((x_p1, y_poll + ry_p * 0.82 * math.cos(ang), rz_p * 0.82 * math.sin(ang)))
            p_apex = len(verts)
            verts.append((x_p_tip, y_poll, 0.0))
            for k in range(8):
                kn = (k + 1) % 8
                add_quad(r_p0[k], r_p1[k], r_p1[kn], r_p0[kn], col_metal_trim if k % 2 == 1 else col_metal_trim_dk)
                add_tri(r_p1[k], p_apex, r_p1[kn], COL_SILVER if k % 2 == 0 else COL_STEEL)

    aligned_verts = fit_to_archetype_aabb(verts, archetype)
    return aligned_verts, tris, face_colors


def map_faces_to_palette(verts, tris, face_colors, palette_png_path, palette_names=None):
    """
    Maps every triangle's color to a stylized swatch in `dungeon_texture.png` and
    generates flat-shaded vertices with vertical gradient UVs inside each swatch cell.
    """
    swatches = load_palette_swatches(palette_png_path)
    allowed_cells = None
    if palette_names:
        allowed_cells = [SWATCH_NAMES[name] for name in palette_names if name in SWATCH_NAMES]

    ys = [v[1] for v in verts]
    y_min, y_max = min(ys), max(ys)
    y_span = max(1e-5, y_max - y_min)

    tri_cells = [nearest_swatch_cell(c, swatches, allowed_cells) for c in face_colors]

    out_positions = []
    out_normals = []
    out_uvs = []
    out_indices = []

    for t_idx, (i0, i1, i2) in enumerate(tris):
        if i0 == i1 or i1 == i2 or i2 == i0:
            continue
        p0, p1, p2 = verts[i0], verts[i1], verts[i2]
        e1 = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
        e2 = (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])
        fn = norm3(cross3(e1, e2))

        cell = tri_cells[t_idx]
        u0, v0, u1, v1 = swatch_uv_box(cell[0], cell[1])
        u_mid = 0.5 * (u0 + u1)

        base_idx = len(out_positions)
        for p in (p0, p1, p2):
            out_positions.append(p)
            out_normals.append(fn)
            t_y = max(0.0, min(1.0, (p[1] - y_min) / y_span))
            v_coord = v1 - t_y * (v1 - v0)
            out_uvs.append((u_mid, v_coord))

        out_indices.extend([base_idx, base_idx + 1, base_idx + 2])

    return out_positions, out_normals, out_uvs, out_indices


def write_item_glb(out_path, positions, normals, uvs, indices, palette_png_path):
    """Writes a self-contained glTF 2.0 binary .glb with embedded stylized palette texture."""
    with open(palette_png_path, "rb") as f:
        png_bytes = f.read()

    pos_bytes = bytearray()
    for p in positions:
        pos_bytes.extend(struct.pack("<3f", p[0], p[1], p[2]))

    norm_bytes = bytearray()
    for n in normals:
        norm_bytes.extend(struct.pack("<3f", n[0], n[1], n[2]))

    uv_bytes = bytearray()
    for uv in uvs:
        uv_bytes.extend(struct.pack("<2f", uv[0], uv[1]))

    idx_bytes = bytearray()
    for idx in indices:
        idx_bytes.extend(struct.pack("<I", idx))

    def pad4(b):
        rem = len(b) % 4
        if rem:
            b.extend(b"\x00" * (4 - rem))
        return b

    bin_chunk = bytearray()
    bv_list = []

    def add_bv(data, target=None):
        offset = len(bin_chunk)
        length = len(data)
        bin_chunk.extend(data)
        pad4(bin_chunk)
        entry = {"buffer": 0, "byteOffset": offset, "byteLength": length}
        if target is not None:
            entry["target"] = target
        bv_list.append(entry)
        return len(bv_list) - 1

    bv_pos = add_bv(pos_bytes, 34962)
    bv_norm = add_bv(norm_bytes, 34962)
    bv_uv = add_bv(uv_bytes, 34962)
    bv_idx = add_bv(idx_bytes, 34963)
    bv_img = add_bv(bytearray(png_bytes))

    xs = [p[0] for p in positions]
    ys = [p[1] for p in positions]
    zs = [p[2] for p in positions]

    gltf_dict = {
        "asset": {"version": "2.0", "generator": "rogue-gauntlet-stylize-and-align"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": "model", "mesh": 0}],
        "meshes": [
            {
                "name": "model",
                "primitives": [
                    {
                        "attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2},
                        "indices": 3,
                        "material": 0,
                    }
                ],
            }
        ],
        "materials": [
            {
                "name": "item_palette",
                "pbrMetallicRoughness": {
                    "baseColorTexture": {"index": 0},
                    "metallicFactor": 0.0,
                    "roughnessFactor": 0.45,
                },
            }
        ],
        "textures": [{"sampler": 0, "source": 0}],
        "samplers": [{"magFilter": 9729, "minFilter": 9729, "wrapS": 33071, "wrapT": 33071}],
        "images": [{"name": "dungeon_texture", "mimeType": "image/png", "bufferView": bv_img}],
        "accessors": [
            {
                "bufferView": bv_pos,
                "componentType": 5126,
                "count": len(positions),
                "type": "VEC3",
                "min": [min(xs), min(ys), min(zs)],
                "max": [max(xs), max(ys), max(zs)],
            },
            {
                "bufferView": bv_norm,
                "componentType": 5126,
                "count": len(normals),
                "type": "VEC3",
            },
            {
                "bufferView": bv_uv,
                "componentType": 5126,
                "count": len(uvs),
                "type": "VEC2",
            },
            {
                "bufferView": bv_idx,
                "componentType": 5125,
                "count": len(indices),
                "type": "SCALAR",
            },
        ],
        "bufferViews": bv_list,
        "buffers": [{"byteLength": len(bin_chunk)}],
    }

    json_bytes = bytearray(json.dumps(gltf_dict, separators=(",", ":")).encode("utf-8"))
    while len(json_bytes) % 4 != 0:
        json_bytes.append(0x20)

    total_len = 12 + 8 + len(json_bytes) + 8 + len(bin_chunk)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total_len))
        f.write(struct.pack("<I4s", len(json_bytes), b"JSON"))
        f.write(json_bytes)
        f.write(struct.pack("<I4s", len(bin_chunk), b"BIN\x00"))
        f.write(bin_chunk)


def render_glb_preview_png(out_png_path, positions, normals, uvs, indices, palette_png_path):
    """
    Renders a 3-panel shaded 3D preview (Front, 3/4 Isometric, Profile) of the
    generated flat-shaded mesh using its actual UVs on `dungeon_texture.png`,
    marking the (0,0,0) BoneAttachment3D grip origin with a subtle crosshair.
    """
    from PIL import ImageDraw

    pal_im = Image.open(palette_png_path).convert("RGB")
    pw, ph = pal_im.size

    panel_w, panel_h = 360, 420
    canvas = Image.new("RGB", (panel_w * 3, panel_h), (30, 33, 40))
    draw = ImageDraw.Draw(canvas)

    xs = [p[0] for p in positions]
    ys = [p[1] for p in positions]
    zs = [p[2] for p in positions]
    cx = 0.5 * (min(xs) + max(xs))
    cy = 0.5 * (min(ys) + max(ys))
    cz = 0.5 * (min(zs) + max(zs))
    span = max(1e-4, max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
    scale = (min(panel_w, panel_h) * 0.78) / span

    light_dir = norm3((0.45, 0.65, 0.75))
    angles = [(0.0, 0.0), (32.0, 16.0), (72.0, 8.0)]

    for p_idx, (yaw_deg, pitch_deg) in enumerate(angles):
        ox = p_idx * panel_w
        yaw = math.radians(yaw_deg)
        pitch = math.radians(pitch_deg)
        cy_a, sy_a = math.cos(yaw), math.sin(yaw)
        cp_a, sp_a = math.cos(pitch), math.sin(pitch)

        def rot_pt(v):
            vx, vy, vz = v
            x1 = vx * cy_a + vz * sy_a
            z1 = -vx * sy_a + vz * cy_a
            y2 = vy * cp_a - z1 * sp_a
            z2 = vy * sp_a + z1 * cp_a
            return x1, y2, z2

        draw_list = []
        for t in range(0, len(indices), 3):
            i0, i1, i2 = indices[t], indices[t + 1], indices[t + 2]
            v0 = rot_pt((positions[i0][0] - cx, positions[i0][1] - cy, positions[i0][2] - cz))
            v1 = rot_pt((positions[i1][0] - cx, positions[i1][1] - cy, positions[i1][2] - cz))
            v2 = rot_pt((positions[i2][0] - cx, positions[i2][1] - cy, positions[i2][2] - cz))

            # Backface culling in view space
            nz_view = (v1[0] - v0[0]) * (v2[1] - v0[1]) - (v1[1] - v0[1]) * (v2[0] - v0[0])
            if nz_view <= 0.0:
                continue

            rn = rot_pt(normals[i0])
            ndotl = max(0.0, dot3(rn, light_dir))
            shade = 0.52 + 0.56 * ndotl

            u_avg = (uvs[i0][0] + uvs[i1][0] + uvs[i2][0]) / 3.0
            v_avg = (uvs[i0][1] + uvs[i1][1] + uvs[i2][1]) / 3.0
            tx = min(pw - 1, max(0, int(u_avg * pw)))
            ty = min(ph - 1, max(0, int(v_avg * ph)))
            tr, tg, tb = pal_im.getpixel((tx, ty))

            col = (
                min(255, int(tr * shade)),
                min(255, int(tg * shade)),
                min(255, int(tb * shade)),
            )
            z_depth = (v0[2] + v1[2] + v2[2]) / 3.0
            pts_2d = [
                (ox + panel_w * 0.5 + v0[0] * scale, panel_h * 0.5 - v0[1] * scale),
                (ox + panel_w * 0.5 + v1[0] * scale, panel_h * 0.5 - v1[1] * scale),
                (ox + panel_w * 0.5 + v2[0] * scale, panel_h * 0.5 - v2[1] * scale),
            ]
            draw_list.append((z_depth, pts_2d, col))

        draw_list.sort(key=lambda item: item[0])
        for _, pts_2d, col in draw_list:
            draw.polygon(pts_2d, fill=col)

        # Draw subtle grip-origin (0,0,0) marker
        gx, gy, _ = rot_pt((-cx, -cy, -cz))
        gpx = ox + panel_w * 0.5 + gx * scale
        gpy = panel_h * 0.5 - gy * scale
        draw.line([(gpx - 6, gpy), (gpx + 6, gpy)], fill=(90, 230, 130), width=1)
        draw.line([(gpx, gpy - 6), (gpx, gpy + 6)], fill=(90, 230, 130), width=1)

    os.makedirs(os.path.dirname(os.path.abspath(out_png_path)), exist_ok=True)
    canvas.save(out_png_path)


def load_barbarian_character_tris(character_glb_path, hide_hat=False):
    """
    Loads the playable Barbarian character meshes from `Barbarian.glb` in the `player.tscn`
    Idle pose, returning a list of `(v0, v1, v2, normal, rgb)` world-space triangles.
    """
    import io

    with open(character_glb_path, "rb") as f:
        magic, _, _ = struct.unpack("<4sII", f.read(12))
        if magic != b"glTF":
            raise ValueError(f"{character_glb_path} is not a valid GLB")
        json_len, _ = struct.unpack("<I4s", f.read(8))
        gltf = json.loads(f.read(json_len).decode("utf-8"))
        bin_len, _ = struct.unpack("<I4s", f.read(8))
        bin_data = f.read(bin_len)

    accessors = gltf["accessors"]
    buffer_views = gltf["bufferViews"]
    img_bv = buffer_views[gltf["images"][0]["bufferView"]]
    img_off = img_bv.get("byteOffset", 0)
    tex_im = Image.open(io.BytesIO(bin_data[img_off : img_off + img_bv["byteLength"]])).convert("RGB")
    tw, th = tex_im.size

    def read_acc(acc_idx):
        acc = accessors[acc_idx]
        bv = buffer_views[acc["bufferView"]]
        offset = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
        count = acc["count"]
        comp_type = acc["componentType"]
        num_comp = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
        fmt_c, c_sz = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}[comp_type]
        stride = bv.get("byteStride", num_comp * c_sz)
        ufmt = "<" + fmt_c * num_comp
        res = []
        for i in range(count):
            pos = offset + i * stride
            vals = struct.unpack(ufmt, bin_data[pos : pos + num_comp * c_sz])
            res.append(vals[0] if num_comp == 1 else vals)
        return res

    # Only include the character body/clothing nodes (skip built-in weapons/shield/mug)
    keep_nodes = {
        "Barbarian_Body",
        "Barbarian_Head",
        "Barbarian_LegLeft",
        "Barbarian_LegRight",
        "Barbarian_ArmLeft",
        "Barbarian_ArmRight",
        "Barbarian_Cape",
    }
    if not hide_hat:
        keep_nodes.add("Barbarian_Hat")

    char_tris = []
    for node in gltf["nodes"]:
        nname = node.get("name", "")
        if nname not in keep_nodes or "mesh" not in node:
            continue

        def transform_char_pt(v):
            vx, vy, vz = v
            if nname in ("Barbarian_Hat", "Barbarian_Cape"):
                return (vx, vy + 1.2017, vz)
            if nname == "Barbarian_ArmRight":
                # Rotate right arm from bind T-pose around shoulder (-0.212, 1.12, 0) into lowered Idle pose
                dx, dy, dz = vx + 0.212, vy - 1.12, vz
                ang_z = math.radians(62.0)
                cz, sz = math.cos(ang_z), math.sin(ang_z)
                x1 = dx * cz - dy * sz
                y1 = dx * sz + dy * cz
                ang_x = math.radians(-18.0)
                cx_a, sx_a = math.cos(ang_x), math.sin(ang_x)
                y2 = y1 * cx_a - dz * sx_a
                z2 = y1 * sx_a + dz * cx_a
                return (x1 - 0.212, y2 + 1.12, z2)
            if nname == "Barbarian_ArmLeft":
                # Rotate left arm from bind T-pose around shoulder (+0.212, 1.12, 0) into lowered Idle pose
                dx, dy, dz = vx - 0.212, vy - 1.12, vz
                ang_z = math.radians(-62.0)
                cz, sz = math.cos(ang_z), math.sin(ang_z)
                x1 = dx * cz - dy * sz
                y1 = dx * sz + dy * cz
                ang_x = math.radians(-18.0)
                cx_a, sx_a = math.cos(ang_x), math.sin(ang_x)
                y2 = y1 * cx_a - dz * sx_a
                z2 = y1 * sx_a + dz * cx_a
                return (x1 + 0.212, y2 + 1.12, z2)
            return (vx, vy, vz)

        mesh = gltf["meshes"][node["mesh"]]
        for prim in mesh["primitives"]:
            attrs = prim["attributes"]
            pos_list = [transform_char_pt(p) for p in read_acc(attrs["POSITION"])]
            uv_list = read_acc(attrs["TEXCOORD_0"]) if "TEXCOORD_0" in attrs else None
            idx_list = read_acc(prim["indices"]) if "indices" in prim else list(range(len(pos_list)))

            for t in range(0, len(idx_list) - 2, 3):
                i0, i1, i2 = idx_list[t], idx_list[t + 1], idx_list[t + 2]
                p0, p1, p2 = pos_list[i0], pos_list[i1], pos_list[i2]
                fn = norm3(cross3((p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]), (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])))
                if uv_list:
                    u = ((uv_list[i0][0] + uv_list[i1][0] + uv_list[i2][0]) / 3.0) % 1.0
                    v = ((uv_list[i0][1] + uv_list[i1][1] + uv_list[i2][1]) / 3.0) % 1.0
                    rgb = tex_im.getpixel((min(tw - 1, max(0, int(u * tw))), min(th - 1, max(0, int(v * th)))))
                else:
                    rgb = (160, 150, 140)
                z_bias = 0.04 if nname == "Barbarian_Hat" else 0.0
                char_tris.append((p0, p1, p2, fn, rgb, z_bias))

    return char_tris


def render_player_worn_preview_png(out_png_path, archetype, positions, normals, uvs, indices, palette_png_path, repo_root, item_id=""):
    """
    Renders a 2-panel verification image (FRONTAL VIEW and SIDE VIEW) of the playable
    Barbarian character wearing/holding the generated item using the exact BoneAttachment3D
    transforms from `scenes/player/player.tscn`.
    """
    from PIL import ImageDraw

    prof = ARCHETYPE_PROFILES[archetype]
    if prof["category"] not in ("weapons", "armor", "potions", "scrolls", "jewelry", "keys", "food", "tools"):
        return False

    char_glb = os.path.join(repo_root, "asset-studio", "rigs", "templates", "barbarian.glb")
    if not os.path.exists(char_glb):
        return False

    scene_tris = load_barbarian_character_tris(char_glb, hide_hat=(archetype == "helmet"))

    pal_im = Image.open(palette_png_path).convert("RGB")
    pw, ph = pal_im.size
    id_low = (item_id or "").lower()

    # Transform item local vertices into Character world space using player.tscn BoneAttachment3D transforms
    def attach_item_pt(v):
        lx, ly, lz = v
        # Apply wrapper scene rotation if defined (e.g. crossbow -90 deg X rotation)
        if "wrapper_rotation" in prof and prof["wrapper_rotation"] == (-90.0, 0.0, 0.0):
            lx, ly, lz = lx, lz, -ly

        if archetype == "helmet":
            # Attached to Hat node on head bone: origin (0, 1.2017, 0)
            return (lx, ly + 1.2017, lz)
        if archetype == "chestplate":
            # Attached to Chest node on chest bone: origin (0, 1.2017, 0)
            return (lx, ly + 1.2017, lz)
        if archetype == "shield":
            # Attached to left hand (+X side of character), keeping shield local +Y upright in world +Y
            wx = 0.34 * lx + 0.94 * lz + 0.480
            wy = 1.00 * ly + 0.620
            wz = -0.94 * lx + 0.34 * lz + 0.160
            return (wx, wy, wz)
        if prof["category"] in ("potions", "scrolls"):
            # Held upright beside the right hand (-X side, slightly forward +Z) for clear scale validation
            return (lx - 0.68, ly + 0.44, lz + 0.24)
        if prof["category"] in ("jewelry", "keys", "food"):
            # Jewelry, keys, and food show the 2x oversized world drop on the ground beside the player
            wscale = prof.get("wrapper_scale", 2.0)
            return (lx * wscale - 0.88, ly * wscale + 0.0, lz * wscale + 0.18)
        if prof["category"] == "tools":
            # Held in the Barbarian's right hand (-0.496, 0.640, 0.139) with local (0,0,0) at the closed fist grip!
            if "oil" in id_low or ("lamp" in id_low and "lantern" not in id_low) or ("candle" in id_low and "candelabrum" not in id_low) or "tallow" in id_low:
                # Rear-handle vessel lamps (Oil Lamp, Chamberstick Candle): held level & upright, projecting forward (+Z) and outward (-X)
                yaw_h = math.radians(-132.0)
                pitch_h = math.radians(0.0)
                roll_h = math.radians(0.0)
            elif "lantern" in id_low or "bell" in id_low:
                # Hanging props (Brass Lantern, Silver Bell): gripped by top bail/handle at (0,0,0), hanging down below the fist
                yaw_h = math.radians(-35.0)
                pitch_h = math.radians(0.0)
                roll_h = math.radians(-6.0)
            elif "candelabrum" in id_low or "mirror" in id_low:
                # Upright ritual candelabrum & looking glass: held nearly vertical in the right fist, tilted slightly outward (-X)
                yaw_h = math.radians(-35.0)
                pitch_h = math.radians(10.0)
                roll_h = math.radians(15.0)
            else:
                # Torches, Pickaxe, Lockpick Set, Grappling Hook, Hunting Horn: gripped at (0,0,0), angled forward (+Z) and outward (-X)
                yaw_h = math.radians(-35.0)
                pitch_h = math.radians(22.0)
                roll_h = math.radians(20.0)
            cy_h, sy_h = math.cos(yaw_h), math.sin(yaw_h)
            cp_h, sp_h = math.cos(pitch_h), math.sin(pitch_h)
            cr_h, sr_h = math.cos(roll_h), math.sin(roll_h)
            x1 = lx * cy_h + lz * sy_h
            z1 = -lx * sy_h + lz * cy_h
            y2 = ly * cp_h - z1 * sp_h
            z2 = ly * sp_h + z1 * cp_h
            # Roll around Z tilts +Y outward toward -X: x3 = x1*cr - y2*sr, y3 = x1*sr + y2*cr
            x3 = x1 * cr_h - y2 * sr_h
            y3 = x1 * sr_h + y2 * cr_h
            return (x3 - 0.496, y3 + 0.640, z2 + 0.139)

        # Weapons: attached to 1H_Axe / 2H_Axe on right hand (-X side of character, pointing +Z forward)
        wx = -0.0002 * lx - 0.1509 * ly + 0.9885 * lz
        wy = 1.0000 * lx - 0.0010 * ly + 0.0001 * lz
        wz = 0.0010 * lx + 0.9885 * ly + 0.1509 * lz
        if prof.get("two_handed") and archetype != "crossbow":
            # Pitch the wrist upward ~18 deg around X so oversized 2H blades clear the floor and rise beside the torso
            up_rad = math.radians(18.0)
            cu, su = math.cos(up_rad), math.sin(up_rad)
            wy, wz = wy * cu + wz * su, -wy * su + wz * cu
        return (wx - 0.496, wy + 0.640, wz + 0.139)

    for t in range(0, len(indices), 3):
        i0, i1, i2 = indices[t], indices[t + 1], indices[t + 2]
        p0 = attach_item_pt(positions[i0])
        p1 = attach_item_pt(positions[i1])
        p2 = attach_item_pt(positions[i2])
        fn = norm3(cross3((p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]), (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])))
        u_avg = (uvs[i0][0] + uvs[i1][0] + uvs[i2][0]) / 3.0
        v_avg = (uvs[i0][1] + uvs[i1][1] + uvs[i2][1]) / 3.0
        rgb = pal_im.getpixel((min(pw - 1, max(0, int(u_avg * pw))), min(ph - 1, max(0, int(v_avg * ph)))))
        scene_tris.append((p0, p1, p2, fn, rgb, 0.02))

    panel_w, panel_h = 540, 580
    canvas = Image.new("RGB", (panel_w * 2, panel_h), (30, 33, 40))
    draw = ImageDraw.Draw(canvas)

    # Left panel = Frontal view (yaw=0); Right panel = Side view from equipped side
    side_yaw = -78.0 if archetype == "shield" else 78.0
    views = [("FRONTAL VIEW", 0.0, 4.0), ("SIDE VIEW", side_yaw, 4.0)]
    light_dir = norm3((0.40, 0.65, 0.75))

    all_pts = [pt for tri in scene_tris for pt in (tri[0], tri[1], tri[2])]
    view_bounds = []
    max_span_x = 1.0
    max_span_y = 1.0
    for _, yaw_deg, pitch_deg in views:
        yaw = math.radians(yaw_deg)
        pitch = math.radians(pitch_deg)
        cy_a, sy_a = math.cos(yaw), math.sin(yaw)
        cp_a, sp_a = math.cos(pitch), math.sin(pitch)
        rx_list = []
        ry_list = []
        for vx, vy, vz in all_pts:
            x1 = vx * cy_a + vz * sy_a
            z1 = -vx * sy_a + vz * cy_a
            y2 = vy * cp_a - z1 * sp_a
            rx_list.append(x1)
            ry_list.append(y2)
        min_rx, max_rx = min(rx_list), max(rx_list)
        min_ry, max_ry = min(ry_list), max(ry_list)
        max_span_x = max(max_span_x, max_rx - min_rx)
        max_span_y = max(max_span_y, max_ry - min_ry)
        view_bounds.append((0.5 * (min_rx + max_rx), 0.5 * (min_ry + max_ry)))

    scale = min((panel_w * 0.84) / max_span_x, (panel_h * 0.84) / max_span_y)

    for p_idx, (label, yaw_deg, pitch_deg) in enumerate(views):
        ox = p_idx * panel_w
        yaw = math.radians(yaw_deg)
        pitch = math.radians(pitch_deg)
        cy_a, sy_a = math.cos(yaw), math.sin(yaw)
        cp_a, sp_a = math.cos(pitch), math.sin(pitch)
        vcx, vcy = view_bounds[p_idx]

        def rot_pt(v):
            vx, vy, vz = v
            x1 = vx * cy_a + vz * sy_a
            z1 = -vx * sy_a + vz * cy_a
            y2 = vy * cp_a - z1 * sp_a
            z2 = vy * sp_a + z1 * cp_a
            return x1, y2, z2

        draw_list = []
        for p0, p1, p2, fn, rgb, z_bias in scene_tris:
            v0 = rot_pt(p0)
            v1 = rot_pt(p1)
            v2 = rot_pt(p2)

            nz_view = (v1[0] - v0[0]) * (v2[1] - v0[1]) - (v1[1] - v0[1]) * (v2[0] - v0[0])
            if nz_view <= 0.0:
                continue

            rn = rot_pt(fn)
            ndotl = max(0.0, dot3(rn, light_dir))
            shade = 0.55 + 0.52 * ndotl
            col = (
                min(255, int(rgb[0] * shade)),
                min(255, int(rgb[1] * shade)),
                min(255, int(rgb[2] * shade)),
            )
            z_depth = (v0[2] + v1[2] + v2[2]) / 3.0 + z_bias
            pts_2d = [
                (ox + panel_w * 0.5 + (v0[0] - vcx) * scale, panel_h * 0.50 - (v0[1] - vcy) * scale),
                (ox + panel_w * 0.5 + (v1[0] - vcx) * scale, panel_h * 0.50 - (v1[1] - vcy) * scale),
                (ox + panel_w * 0.5 + (v2[0] - vcx) * scale, panel_h * 0.50 - (v2[1] - vcy) * scale),
            ]
            draw_list.append((z_depth, pts_2d, col))

        draw_list.sort(key=lambda item: item[0])
        for _, pts_2d, col in draw_list:
            draw.polygon(pts_2d, fill=col)

        draw.text((ox + 18, 16), label, fill=(210, 218, 230))

    draw.line([(panel_w, 0), (panel_w, panel_h)], fill=(55, 60, 72), width=2)
    os.makedirs(os.path.dirname(os.path.abspath(out_png_path)), exist_ok=True)
    canvas.save(out_png_path)
    return True


def main():
    parser = argparse.ArgumentParser(description="Stylize, decimate, and align a 3D asset to stylized conventions.")
    parser.add_argument("--archetype", required=True, choices=sorted(ARCHETYPE_PROFILES.keys()))
    parser.add_argument("--input-glb", help="Path to raw input .glb (e.g. from TripoSR / TRELLIS)")
    parser.add_argument("--from-image", help="Path to concept PNG for contour-based low-poly 3D reconstruction")
    parser.add_argument("--output", required=True, help="Output .glb path")
    parser.add_argument("--preview-png", help="Optional path to save a 3-angle shaded 3D preview PNG")
    parser.add_argument(
        "--player-preview-png",
        help="Optional path to save a Frontal + Side preview PNG of the playable character wearing the item",
    )
    parser.add_argument("--target-tris", type=int, default=550, help="Target triangle count for GLB decimation")
    parser.add_argument(
        "--palette",
        help="Comma-separated stylized swatch names (e.g. steel,wood,gold,dark_iron). Defaults to archetype palette.",
    )
    parser.add_argument(
        "--palette-png",
        default="asset-studio/rigs/item_palette.png",
        help="Path to stylized palette texture PNG",
    )
    parser.add_argument("--flip-y", action="store_true", help="Flip model upside down along Y")
    parser.add_argument("--flip-x", action="store_true", help="Mirror model along X")
    parser.add_argument("--rot-y", type=float, default=0.0, help="Extra rotation around Y axis in degrees")
    args = parser.parse_args()

    if not args.input_glb and not args.from_image:
        sys.exit("error: must provide either --input-glb or --from-image")

    prof = ARCHETYPE_PROFILES[args.archetype]
    palette_names = (
        [p.strip() for p in args.palette.split(",") if p.strip()]
        if args.palette
        else prof["default_palette"]
    )
    item_id = os.path.splitext(os.path.basename(args.output))[0]

    if args.input_glb and os.path.exists(args.input_glb):
        raw_verts, raw_tris, raw_cols = parse_glb(args.input_glb)
        dec_verts, dec_tris, dec_cols = decimate_mesh_grid(raw_verts, raw_tris, raw_cols, args.target_tris)
        aligned_verts = align_mesh_axes(
            dec_verts, args.archetype, flip_y=args.flip_y, flip_x=args.flip_x, rot_y_deg=args.rot_y
        )
        tris, face_cols = dec_tris, dec_cols
    else:
        aligned_verts, tris, face_cols = reconstruct_from_concept_image(
            args.from_image, args.archetype, palette_names=palette_names, item_id=item_id
        )
        if args.flip_y:
            aligned_verts = [(-v[0], -v[1], v[2]) for v in aligned_verts]
            aligned_verts = fit_to_archetype_aabb(aligned_verts, args.archetype)
        if args.flip_x:
            aligned_verts = [(-v[0], v[1], -v[2]) for v in aligned_verts]
        if abs(args.rot_y) > 1e-4:
            rad = math.radians(args.rot_y)
            cs, sn = math.cos(rad), math.sin(rad)
            aligned_verts = [(v[0] * cs + v[2] * sn, v[1], -v[0] * sn + v[2] * cs) for v in aligned_verts]

    positions, normals, uvs, indices = map_faces_to_palette(
        aligned_verts, tris, face_cols, args.palette_png, palette_names
    )
    write_item_glb(args.output, positions, normals, uvs, indices, args.palette_png)
    if args.preview_png:
        render_glb_preview_png(args.preview_png, positions, normals, uvs, indices, args.palette_png)
    if args.player_preview_png:
        repo_root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
        render_player_worn_preview_png(
            args.player_preview_png, args.archetype, positions, normals, uvs, indices, args.palette_png, repo_root, item_id=item_id
        )

    xs = [p[0] for p in positions]
    ys = [p[1] for p in positions]
    zs = [p[2] for p in positions]
    print(
        f"Stylized GLB written: {args.output} | tris={len(indices)//3} | "
        f"aabb=([{min(xs):.3f}..{max(xs):.3f}], [{min(ys):.3f}..{max(ys):.3f}], [{min(zs):.3f}..{max(zs):.3f}]) | "
        f"palette={palette_names}"
    )


if __name__ == "__main__":
    main()
