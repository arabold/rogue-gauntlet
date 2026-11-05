#!/usr/bin/env python3
"""
Assemble a high-resolution, unified asset studio showcase image for docs/README.md.
Combines procedural characters/creatures, armor fittings, weapons, shields, and dungeon props
matching the KayKit aesthetic on the signature dark-slate canvas.
"""

import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUTPUT_PATH = "docs/asset-studio-showcase.png"
WIDTH = 1920
HEIGHT = 1080

BG_COLOR = (20, 23, 31)         # #14171f
CARD_BG = (23, 27, 37)          # #171b25
CARD_BORDER = (42, 50, 68)      # #2a3244
ACCENT_GOLD = (220, 180, 60)    # #dcb43c
ACCENT_BLUE = (56, 189, 248)    # #38bdf8
TEXT_PRIMARY = (242, 245, 250)
TEXT_MUTED = (145, 155, 175)
TEXT_DIM = (105, 115, 135)

FONT_TITLE = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 22)
FONT_SUBTITLE = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 16)
FONT_CAPTION = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 13)
FONT_SECTION = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 12)
FONT_CARD_TITLE = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 13)
FONT_CARD_TAG = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 10)


def get_item_thumb(path):
    with Image.open(path) as im:
        index = im.width // 360 - 4 + 1
        return im.convert("RGB").crop((index * 360, 40, (index + 1) * 360, 400))


def get_costume(path):
    with Image.open(path) as im:
        crop = im.convert("RGB").crop((360, 10, 720, 415))
        arr = np.array(crop)
        # Erase debug text overlays from catalog fitting ("three quarter", "attack")
        arr[:22, :85] = BG_COLOR
        arr[380:, :55] = BG_COLOR
        return Image.fromarray(arr)


def get_creature(path):
    with Image.open(path) as im:
        return im.convert("RGB")


def main():
    canvas = Image.new("RGB", (WIDTH, HEIGHT), BG_COLOR)
    draw = ImageDraw.Draw(canvas)

    # 1. Top Header Bar
    draw.rectangle([0, 0, WIDTH, 76], fill=(13, 16, 23))
    draw.line([(0, 76), (WIDTH, 76)], fill=(34, 42, 58), width=1)
    draw.line([(0, 77), (340, 77)], fill=ACCENT_GOLD, width=2)

    # Header text
    draw.text((40, 24), "ROGUE GAUNTLET", font=FONT_TITLE, fill=TEXT_PRIMARY)
    title_bbox = FONT_TITLE.getbbox("ROGUE GAUNTLET")
    tx = 40 + title_bbox[2] + 16
    draw.text((tx, 27), "|  PROCEDURAL ASSET STUDIO", font=FONT_SUBTITLE, fill=ACCENT_BLUE)

    right_text = "Procedural 3D Asset Pipeline · KayKit-Matched Low-Poly Aesthetic · Python + Blender + Godot 4"
    r_bbox = FONT_CAPTION.getbbox(right_text)
    draw.text((WIDTH - 40 - (r_bbox[2] - r_bbox[0]), 30), right_text, font=FONT_CAPTION, fill=TEXT_MUTED)

    # =========================================================================
    # SECTION 1: CHARACTERS, CREATURES & ARMOR SETS (y: 88 to 516)
    # =========================================================================
    sec1_y = 96
    draw.rectangle([40, sec1_y + 3, 46, sec1_y + 11], fill=ACCENT_GOLD)
    draw.text((54, sec1_y), "ORIGINAL HERO SILHOUETTES, WEARABLE ARMORS & MONSTERS", font=FONT_SECTION, fill=ACCENT_GOLD)
    s1_bbox = FONT_SECTION.getbbox("ORIGINAL HERO SILHOUETTES, WEARABLE ARMORS & MONSTERS")
    draw.line([(58 + s1_bbox[2], sec1_y + 7), (WIDTH - 40, sec1_y + 7)], fill=(34, 42, 58), width=1)

    heroes = [
        ("Paladin Plate", "HERO ARMOR REFERENCE", get_costume("asset-studio/catalogs/costumes/knight_plate/lineup.png"), "costume"),
        ("Forsaken Warden", "FIRST-REALM BOSS", get_creature("/tmp/warden_showcase.png"), "creature"),
        ("Crusader Mail", "TEMPLAR COIF & TABARD", get_costume("asset-studio/catalogs/costumes/crusader/lineup.png"), "costume"),
        ("Stone Golem", "GUARDIAN CONSTRUCT", get_creature("/tmp/golem_showcase.png"), "creature"),
        ("Dark Knight", "HORNED DREADPLATE", get_costume("asset-studio/catalogs/costumes/dark_knight/lineup.png"), "costume"),
        ("Ranger Leather", "SCOUT COWL & TUNIC", get_costume("asset-studio/catalogs/costumes/ranger/lineup.png"), "costume"),
        ("Dire Wolf", "BEAST MONSTER", get_creature("/tmp/wolf_showcase.png"), "creature"),
    ]

    card_w = 242
    card_h = 380
    c_y = 124
    c_gap = (WIDTH - 80 - len(heroes) * card_w) // (len(heroes) - 1)

    for i, (name, tag, img, kind) in enumerate(heroes):
        cx = 40 + i * (card_w + c_gap)
        draw.rounded_rectangle([cx, c_y, cx + card_w, c_y + card_h], radius=8, fill=CARD_BG, outline=CARD_BORDER, width=1)
        draw.rounded_rectangle([cx + 3, c_y + 3, cx + card_w - 3, c_y + card_h - 48], radius=6, fill=BG_COLOR)

        if kind == "costume":
            scaled = img.resize((234, int(234 * img.height / img.width)), Image.Resampling.LANCZOS)
            canvas.paste(scaled, (cx + 4, c_y + 10))
        else:
            scaled = img.resize((230, 230), Image.Resampling.LANCZOS)
            canvas.paste(scaled, (cx + 6, c_y + 45))

        draw.line([(cx + 8, c_y + card_h - 44), (cx + card_w - 8, c_y + card_h - 44)], fill=(32, 38, 52), width=1)
        draw.text((cx + 12, c_y + card_h - 38), name, font=FONT_CARD_TITLE, fill=TEXT_PRIMARY)
        draw.text((cx + 12, c_y + card_h - 20), tag, font=FONT_CARD_TAG, fill=TEXT_MUTED)

    # =========================================================================
    # SECTION 2: WEAPONS & SHIELDS (y: 520 to 780)
    # =========================================================================
    sec2_y = 524
    draw.rectangle([40, sec2_y + 3, 46, sec2_y + 11], fill=ACCENT_BLUE)
    draw.text((54, sec2_y), "PROCEDURAL WEAPONS & SHIELDS (45 WEAPONS · 13 SHIELDS)", font=FONT_SECTION, fill=ACCENT_BLUE)
    s2_bbox = FONT_SECTION.getbbox("PROCEDURAL WEAPONS & SHIELDS (45 WEAPONS · 13 SHIELDS)")
    draw.line([(58 + s2_bbox[2], sec2_y + 7), (WIDTH - 40, sec2_y + 7)], fill=(34, 42, 58), width=1)

    weapons = [
        ("Flame Sword", "asset-studio/catalogs/items/weapons/sword_flame/lineup.png"),
        ("Frost Sword", "asset-studio/catalogs/items/weapons/sword_frost/lineup.png"),
        ("Dragon Shield", "asset-studio/catalogs/items/shields/shield_dragon_scale/lineup.png"),
        ("Gilded Battleaxe", "asset-studio/catalogs/items/weapons/battleaxe_gilded/lineup.png"),
        ("Emerald Staff", "asset-studio/catalogs/items/weapons/staff_emerald/lineup.png"),
        ("Golden Aegis", "asset-studio/catalogs/items/shields/shield_aegis_gold/lineup.png"),
        ("Sunblade", "asset-studio/catalogs/items/weapons/sword_sunblade/lineup.png"),
        ("Spiked Buckler", "asset-studio/catalogs/items/shields/shield_buckler_spiked/lineup.png"),
        ("Storm Trident", "asset-studio/catalogs/items/weapons/trident_storm/lineup.png"),
        ("Crusader Kite", "asset-studio/catalogs/items/shields/shield_kite_crusader/lineup.png"),
    ]

    item_w = 168
    item_h = 224
    i_y = 550
    i_gap = (WIDTH - 80 - len(weapons) * item_w) // (len(weapons) - 1)

    for i, (name, path) in enumerate(weapons):
        ix = 40 + i * (item_w + i_gap)
        draw.rounded_rectangle([ix, i_y, ix + item_w, i_y + item_h], radius=8, fill=CARD_BG, outline=CARD_BORDER, width=1)
        draw.rounded_rectangle([ix + 3, i_y + 3, ix + item_w - 3, i_y + item_h - 38], radius=6, fill=BG_COLOR)

        thumb = get_item_thumb(path).resize((150, 150), Image.Resampling.LANCZOS)
        canvas.paste(thumb, (ix + 9, i_y + 19))

        draw.line([(ix + 8, i_y + item_h - 34), (ix + item_w - 8, i_y + item_h - 34)], fill=(32, 38, 52), width=1)
        t_box = FONT_CARD_TITLE.getbbox(name)
        tw = t_box[2] - t_box[0]
        draw.text((ix + (item_w - tw) // 2, i_y + item_h - 26), name, font=FONT_CARD_TITLE, fill=TEXT_PRIMARY)

    # =========================================================================
    # SECTION 3: DUNGEON PROPS, POTIONS & ARTIFACTS (y: 796 to 1056)
    # =========================================================================
    sec3_y = 794
    draw.rectangle([40, sec3_y + 3, 46, sec3_y + 11], fill=ACCENT_GOLD)
    draw.text((54, sec3_y), "DUNGEON FOUNTAINS, POTIONS, RUNIC SCROLLS & PROPS", font=FONT_SECTION, fill=ACCENT_GOLD)
    s3_bbox = FONT_SECTION.getbbox("DUNGEON FOUNTAINS, POTIONS, RUNIC SCROLLS & PROPS")
    draw.line([(58 + s3_bbox[2], sec3_y + 7), (WIDTH - 40, sec3_y + 7)], fill=(34, 42, 58), width=1)

    props = [
        ("Tiered Fountain", "asset-studio/catalogs/items/fountains/fountain_tiered_health/lineup.png"),
        ("Mana Chalice", "asset-studio/catalogs/items/fountains/fountain_chalice_mana/lineup.png"),
        ("Health Elixir", "asset-studio/catalogs/items/potions/potion_large_crimson/lineup.png"),
        ("Mana Elixir", "asset-studio/catalogs/items/potions/potion_large_cobalt/lineup.png"),
        ("Suncrest Amulet", "asset-studio/catalogs/items/jewelry/amulet_gold_sun_crest/lineup.png"),
        ("Ruby Pendant", "asset-studio/catalogs/items/jewelry/amulet_gold_ruby/lineup.png"),
        ("Brass Lantern", "asset-studio/catalogs/items/tools/tool_lantern_brass/lineup.png"),
        ("Treasury Key", "asset-studio/catalogs/items/keys/key_gold_treasury/lineup.png"),
        ("Skull Key", "asset-studio/catalogs/items/keys/key_bone_skull/lineup.png"),
        ("Algiz Scroll", "asset-studio/catalogs/items/scrolls/scroll_gold_algiz/lineup.png"),
    ]

    p_y = 820
    for i, (name, path) in enumerate(props):
        px = 40 + i * (item_w + i_gap)
        draw.rounded_rectangle([px, p_y, px + item_w, p_y + item_h], radius=8, fill=CARD_BG, outline=CARD_BORDER, width=1)
        draw.rounded_rectangle([px + 3, p_y + 3, px + item_w - 3, p_y + item_h - 38], radius=6, fill=BG_COLOR)

        thumb = get_item_thumb(path).resize((150, 150), Image.Resampling.LANCZOS)
        canvas.paste(thumb, (px + 9, p_y + 19))

        draw.line([(px + 8, p_y + item_h - 34), (px + item_w - 8, p_y + item_h - 34)], fill=(32, 38, 52), width=1)
        t_box = FONT_CARD_TITLE.getbbox(name)
        tw = t_box[2] - t_box[0]
        draw.text((px + (item_w - tw) // 2, p_y + item_h - 26), name, font=FONT_CARD_TITLE, fill=TEXT_PRIMARY)

    # Save final image
    canvas.save(OUTPUT_PATH, quality=95)
    print(f"Generated {OUTPUT_PATH} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
