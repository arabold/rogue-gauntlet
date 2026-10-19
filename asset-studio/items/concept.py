"""Offline blockout references for the explicit procedural item backend.

These silhouettes seed known archetype builders; they are not AI concepts or a
faithful reconstruction of an arbitrary design. AI references use core.ai instead.
"""

import os
from pathlib import Path
from PIL import Image, ImageDraw
from items import align

STUDIO = str(Path(__file__).resolve().parents[1])


def blockout(archetype, out_png, palette_names=None):
    """Draw a deterministic, isolated silhouette without network access."""
    prof = align.ARCHETYPE_PROFILES[archetype]
    # Procedural stylized concept silhouette fallback (clean white background + stylized palette colors)
    palette_png = os.path.join(STUDIO, "rigs", "item_palette.png")
    swatches = {s["cell"]: s["rgb"] for s in align.load_palette_swatches(palette_png)}
    cols = [
        swatches.get(align.SWATCH_NAMES.get(name, (0, 1)), (130, 140, 145))
        for name in (palette_names or prof["default_palette"])
    ]
    while len(cols) < 4:
        cols.append(cols[-1])

    im = Image.new("RGBA", (512, 512), (255, 255, 255, 255))
    draw = ImageDraw.Draw(im)
    c_primary, c_secondary, c_accent, c_dark = cols[0], cols[1], cols[2], cols[3]

    if archetype in ("battleaxe", "axe"):
        # Pommel & wooden shaft
        draw.polygon([(242, 460), (270, 460), (266, 95), (246, 95)], fill=c_secondary)
        draw.polygon([(234, 475), (278, 475), (272, 448), (240, 448)], fill=c_accent)
        # Left blade wing
        draw.polygon([(246, 130), (110, 95), (85, 195), (120, 295), (246, 250)], fill=c_primary)
        draw.polygon([(115, 102), (90, 195), (124, 288), (150, 265), (132, 195), (150, 125)], fill=c_accent)
        if archetype == "battleaxe":
            # Right blade wing
            draw.polygon([(266, 130), (402, 95), (427, 195), (392, 295), (266, 250)], fill=c_primary)
            draw.polygon([(397, 102), (422, 195), (388, 288), (362, 265), (380, 195), (362, 125)], fill=c_accent)
        # Top spike / collar
        draw.polygon([(236, 135), (276, 135), (256, 65)], fill=c_accent)
    elif archetype in ("sword", "broadsword", "dagger"):
        w_blade = 34 if archetype == "broadsword" else (24 if archetype == "sword" else 20)
        w_guard = 110 if archetype == "broadsword" else (85 if archetype == "sword" else 65)
        draw.polygon([(244, 455), (268, 455), (266, 360), (246, 360)], fill=c_accent)
        draw.polygon([(234, 475), (278, 475), (270, 450), (242, 450)], fill=c_secondary)
        draw.polygon([(256 - w_guard, 340), (256 + w_guard, 340), (256 + w_guard - 12, 368), (256 - w_guard + 12, 368)], fill=c_secondary)
        draw.polygon([(256 - w_blade, 340), (256 + w_blade, 340), (256 + w_blade - 4, 120), (256, 55), (256 - w_blade + 4, 120)], fill=c_primary)
    elif archetype in ("hammer", "warhammer"):
        draw.polygon([(242, 465), (270, 465), (266, 110), (246, 110)], fill=c_secondary)
        draw.polygon([(115, 115), (397, 115), (412, 235), (100, 235)], fill=c_primary)
        draw.polygon([(115, 115), (145, 115), (145, 235), (100, 235)], fill=c_accent)
        draw.polygon([(367, 115), (397, 115), (412, 235), (367, 235)], fill=c_accent)
    elif archetype.startswith("potion"):
        draw.polygon([(225, 125), (287, 125), (280, 75), (232, 75)], fill=c_accent)
        draw.polygon([(210, 150), (302, 150), (295, 125), (217, 125)], fill=c_secondary)
        draw.ellipse([130, 170, 382, 445], fill=c_primary)
    elif archetype == "shield":
        draw.polygon([(110, 90), (402, 90), (415, 265), (256, 445), (97, 265)], fill=c_primary)
        draw.polygon([(140, 120), (372, 120), (382, 255), (256, 405), (130, 255)], fill=c_secondary)
        draw.ellipse([210, 200, 302, 292], fill=c_accent)
    else:
        # Helmet / chestplate / staff / wand
        draw.polygon([(140, 390), (125, 210), (190, 95), (322, 95), (387, 210), (372, 390)], fill=c_primary)
        draw.polygon([(180, 240), (332, 240), (320, 370), (192, 370)], fill=c_dark)

    os.makedirs(os.path.dirname(os.path.abspath(out_png)), exist_ok=True)
    im.save(out_png)
    print(f"  -> Synthesized stylized concept silhouette: {out_png}")
    return out_png
