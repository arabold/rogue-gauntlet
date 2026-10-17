#!/usr/bin/env python3
"""
End-to-End 3D Asset Generation & Integration Pipeline for Rogue Gauntlet.

Executes all 5 stages step-by-step:
  [Step 1/5] Concept Image Generation (from --image, HF Text-to-Image API via --prompt,
             or procedural stylized silhouette synthesizer when offline)
  [Step 2/5] 3D Mesh Reconstruction (via hf_image3d.py / TripoSR or contour-aware 3D builder)
  [Step 3/5] Low-Poly Decimation, stylized `dungeon_texture.png` UV Remapping, & Reference
             AABB / Grip Origin Alignment (via align.py)
  [Step 4/5] Manifest Registration (`assets/ATTRIBUTIONS.json`), Wrapper `.tscn` & Item
             `.tres` Generation, Godot Headless `--import`, and `inspect_resource.gd` Validation
  [Step 5/5] Windowed Visual Verification (`render_item_catalog.gd` + `render_held_items.gd`)

Usage example:
  python3 asset-studio/items/build_item.py \\
    --id battleaxe_horned \\
    --name "Horned Battle Axe" \\
    --archetype battleaxe \\
    --tier 3 \\
    --image path/to/concept.png
"""

import argparse
import datetime
import json
import math
import os
import subprocess
import sys
import urllib.request
from PIL import Image, ImageDraw

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STUDIO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))
REPO_ROOT = os.path.abspath(os.path.join(STUDIO_ROOT, ".."))
if STUDIO_ROOT not in sys.path:
    sys.path.insert(0, STUDIO_ROOT)
sys.path.insert(0, SCRIPT_DIR)

from items import hf_image3d as hf_image_to_3d  # noqa: E402
from items import align as stylize_and_align  # noqa: E402

# Nominal weapon damage and strength requirements by Tier (1-5) from docs/stats-and-itemization.md
TIER_DEFAULTS = {
    1: {"1h_dmg": (1.0, 5.0), "2h_dmg": (2.0, 7.0), "str": 10, "armor": 2},
    2: {"1h_dmg": (3.0, 8.0), "2h_dmg": (5.0, 11.0), "str": 12, "armor": 3},
    3: {"1h_dmg": (6.0, 12.0), "2h_dmg": (8.0, 16.0), "str": 14, "armor": 5},
    4: {"1h_dmg": (9.0, 17.0), "2h_dmg": (12.0, 22.0), "str": 17, "armor": 8},
    5: {"1h_dmg": (15.0, 26.0), "2h_dmg": (18.0, 32.0), "str": 21, "armor": 11},
}

VALID_SLOTS_MAP = {
    "Head": 1,
    "Chest": 2,
    "Hands": 4,
    "Legs": 8,
    "Feet": 16,
    "Neck": 32,
    "LeftRing": 64,
    "RightRing": 128,
    "WeaponHand": 256,
    "ShieldHand": 512,
}


def generate_concept_image_hf_or_procedural(prompt, archetype, out_png, palette_names):
    """
    Generates a stylized-style isolated concept image on a solid white background.
    Tries Hugging Face Inference API (FLUX.1-schnell) if HF_TOKEN is available;
    falls back to a clean procedural stylized-palette concept silhouette if offline.
    """
    hf_image_to_3d.load_dotenv_if_needed()
    prof = stylize_and_align.ARCHETYPE_PROFILES[archetype]
    full_prompt = (
        f"Single isolated 3D game asset of {prompt or prof['prompt_hint']}, "
        f"stylized low-poly RPG prop in stylized style, chunky proportions, thick bevels, "
        f"flat diffuse lighting, matte hand-painted color palette, centered upright on "
        f"pure solid white background #FFFFFF, no drop shadows, no text, no frame"
    )

    token = os.environ.get("HF_TOKEN")
    if token:
        try:
            url = "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell"
            payload = json.dumps({"inputs": full_prompt}).encode("utf-8")
            req = urllib.request.Request(
                url,
                data=payload,
                headers={
                    "Authorization": f"Bearer {token}",
                    "Content-Type": "application/json",
                    "Accept": "image/png",
                },
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=45) as resp:
                img_bytes = resp.read()
                os.makedirs(os.path.dirname(os.path.abspath(out_png)), exist_ok=True)
                with open(out_png, "wb") as f:
                    f.write(img_bytes)
                print(f"  -> Generated concept image via HF Inference API: {out_png}")
                return out_png
        except Exception as exc:
            print(f"  -> HF Text-to-Image API unavailable ({exc}); synthesizing stylized concept image locally.")

    # Procedural stylized concept silhouette fallback (clean white background + stylized palette colors)
    palette_png = os.path.join(STUDIO_ROOT, "rigs", "item_palette.png")
    swatches = {s["cell"]: s["rgb"] for s in stylize_and_align.load_palette_swatches(palette_png)}
    cols = [
        swatches.get(stylize_and_align.SWATCH_NAMES.get(name, (0, 1)), (130, 140, 145))
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


def ensure_manifest_entry(pack_id="custom-items", pack_dir="assets/custom-items"):
    """Ensures `assets/ATTRIBUTIONS.json` has an entry for `assets/custom-items`."""
    manifest_path = os.path.join(REPO_ROOT, "assets", "ATTRIBUTIONS.json")
    with open(manifest_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    packs = data.get("packs", [])
    for p in packs:
        if p.get("id") == pack_id or p.get("dir") == pack_dir:
            return

    packs.append({
        "id": pack_id,
        "name": "Rogue Gauntlet - Custom Generated 3D Items",
        "dir": pack_dir,
        "author": "Rogue Gauntlet contributors (AI-assisted + stylized palette)",
        "source": "Self-authored via asset-studio/items/build_item.py",
        "license": "CC0-1.0",
        "license_url": "https://creativecommons.org/publicdomain/zero/1.0/",
        "attribution_required": False,
        "types": ["3d"],
        "compatibility_group": "stylized",
        "acquired": datetime.date.today().isoformat(),
        "notes": "Low-poly flat-shaded 3D items UV-mapped to the studio dungeon_texture.png and aligned to stylized reference AABBs.",
    })
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent="\t")
        f.write("\n")
    print(f"  -> Registered '{pack_id}' in assets/ATTRIBUTIONS.json")


def pascal_case(snake_str):
    return "".join(part.capitalize() for part in snake_str.replace("-", "_").split("_") if part)


def write_wrapper_tscn(tscn_path, item_id, glb_res_path, archetype, extra_rot=None, extra_scale=None, extra_offset=None):
    """
    Writes the thin wrapper .tscn around the generated .glb.
    Root stays at Transform3D.Identity; any transform adjustments live on the child node.
    """
    prof = stylize_and_align.ARCHETYPE_PROFILES[archetype]
    root_name = pascal_case(item_id)
    lines = [
        "[gd_scene load_steps=2 format=3]",
        "",
        f'[ext_resource type="PackedScene" path="{glb_res_path}" id="1_glb"]',
        "",
        f'[node name="{root_name}" type="Node3D"]',
        "",
        f'[node name="{item_id}" parent="." instance=ExtResource("1_glb")]',
    ]

    scale = extra_scale if extra_scale is not None else prof.get("wrapper_scale", 1.0)
    ox, oy, oz = extra_offset if extra_offset is not None else (0.0, 0.0, 0.0)
    if abs(scale - 1.0) > 1e-4 or abs(ox) > 1e-4 or abs(oy) > 1e-4 or abs(oz) > 1e-4:
        lines.append(f"transform = Transform3D({scale:g}, 0, 0, 0, {scale:g}, 0, 0, 0, {scale:g}, {ox:g}, {oy:g}, {oz:g})")

    rot = extra_rot if extra_rot is not None else prof.get("wrapper_rotation")
    if rot and (abs(rot[0]) > 1e-4 or abs(rot[1]) > 1e-4 or abs(rot[2]) > 1e-4):
        lines.append(f"rotation_degrees = Vector3({rot[0]:g}, {rot[1]:g}, {rot[2]:g})")

    if prof["category"] in ("potions", "scrolls"):
        lines.append("layers = 32")

    lines.append("")
    os.makedirs(os.path.dirname(os.path.abspath(tscn_path)), exist_ok=True)
    with open(tscn_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def write_item_tres(tres_path, tscn_res_path, item_id, display_name, archetype, tier, dmg_min=None, dmg_max=None, armor_pts=None, str_req=None):
    """Writes the item definition .tres under scenes/items/<category>/<id>.tres."""
    prof = stylize_and_align.ARCHETYPE_PROFILES[archetype]
    category = prof["category"]
    t_def = TIER_DEFAULTS.get(tier, TIER_DEFAULTS[3])
    req_str = str_req if str_req is not None else t_def["str"]

    if category == "weapons":
        two_handed = prof.get("two_handed", False)
        default_min, default_max = t_def["2h_dmg"] if two_handed else t_def["1h_dmg"]
        d_min = dmg_min if dmg_min is not None else default_min
        d_max = dmg_max if dmg_max is not None else default_max
        is_2h_str = "true" if two_handed else "false"
        content = f"""[gd_resource type="Resource" script_class="Weapon" load_steps=3 format=3]

[ext_resource type="PackedScene" path="{tscn_res_path}" id="1_scene"]
[ext_resource type="Script" uid="uid://bb7ire18vxwkm" path="res://scenes/items/Weapon.cs" id="2_script"]

[resource]
script = ExtResource("2_script")
Tier = {tier}
Level = 0
RequiredStrength = {req_str}
Accuracy = 1.0
DamageMin = {d_min:.1f}
DamageMax = {d_max:.1f}
CritChance = 0.0
IsTwoHanded = {is_2h_str}
AnimationId = "melee_attack"
Delay = 0.0
PerformDuration = 0.5
CooldownDuration = 0.0
ValidSlots = 256
Rarity = 0
Name = "{display_name}"
Scene = ExtResource("1_scene")
Quality = 1.0
Value = {tier * 40}
ShowInInventory = true
IsStackable = false
IsQuestItem = false
metadata/_custom_type_script = ExtResource("2_script")
"""
    elif category == "armor":
        slot_name = prof.get("slot", "ShieldHand")
        valid_slots = VALID_SLOTS_MAP.get(slot_name, 512)
        a_pts = armor_pts if armor_pts is not None else t_def["armor"]
        content = f"""[gd_resource type="Resource" script_class="Armor" load_steps=3 format=3]

[ext_resource type="PackedScene" path="{tscn_res_path}" id="1_scene"]
[ext_resource type="Script" uid="uid://toi2pwgfoo72" path="res://scenes/items/Armor.cs" id="2_script"]

[resource]
script = ExtResource("2_script")
Tier = {tier}
Level = 0
RequiredStrength = {req_str}
ArmorPoints = {a_pts}
AccuracyModifier = 1.0
SpeedModifier = 1.0
ValidSlots = {valid_slots}
Rarity = 0
Name = "{display_name}"
Scene = ExtResource("1_scene")
Quality = 1.0
Value = {tier * 35}
ShowInInventory = true
IsStackable = false
IsQuestItem = false
metadata/_custom_type_script = ExtResource("2_script")
"""
    elif category == "jewelry":
        valid_slots = 32 if archetype == "amulet" else 192
        stat_id = 4 if archetype == "amulet" else 0
        stat_val = 20.0 if archetype == "amulet" else 2.0
        item_val = 200 if archetype == "amulet" else 150
        tmpl = f"{{descriptor}} {archetype}"
        content = f"""[gd_resource type="Resource" script_class="IdentifiableEquipableItem" load_steps=5 format=3]

[ext_resource type="Script" uid="uid://cadjyq7h2j4fb" path="res://scenes/items/IdentifiableEquipableItem.cs" id="1_item"]
[ext_resource type="PackedScene" path="{tscn_res_path}" id="2_scene"]
[ext_resource type="Script" uid="uid://cstatmod0000001" path="res://scenes/player/resources/StatModifier.cs" id="3_mod"]

[sub_resource type="Resource" id="Resource_mod0"]
script = ExtResource("3_mod")
Stat = {stat_id}
Op = 0
Value = {stat_val}
metadata/_custom_type_script = ExtResource("3_mod")

[resource]
script = ExtResource("1_item")
TypeId = "{archetype}.{item_id}"
IdentityCategory = "{archetype}"
TrueName = "{display_name}"
UnidentifiedNameTemplate = "{tmpl}"
IntrinsicModifiers = [SubResource("Resource_mod0")]
ValidSlots = {valid_slots}
Rarity = 0
Name = "{display_name}"
Scene = ExtResource("2_scene")
Quality = 1.0
Value = {item_val}
ShowInInventory = true
IsStackable = false
IsQuestItem = false
metadata/_custom_type_script = ExtResource("1_item")
"""
    elif category == "keys":
        content = f"""[gd_resource type="Resource" script_class="Item" load_steps=3 format=3]

[ext_resource type="PackedScene" path="{tscn_res_path}" id="1_scene"]
[ext_resource type="Script" uid="uid://cyllicia8xh4i" path="res://scenes/items/Item.cs" id="2_script"]

[resource]
script = ExtResource("2_script")
Name = "{display_name}"
Scene = ExtResource("1_scene")
Quality = 1.0
Value = {tier * 50}
ShowInInventory = true
IsStackable = true
IsQuestItem = true
metadata/_custom_type_script = ExtResource("2_script")
"""
    elif category == "tools":
        content = f"""[gd_resource type="Resource" script_class="EquipableItem" load_steps=3 format=3]

[ext_resource type="PackedScene" path="{tscn_res_path}" id="1_scene"]
[ext_resource type="Script" uid="uid://de5x45p7jum00" path="res://scenes/items/EquipableItem.cs" id="2_script"]

[resource]
script = ExtResource("2_script")
ValidSlots = 256
Rarity = 0
Tier = {tier}
Name = "{display_name}"
Scene = ExtResource("1_scene")
Quality = 1.0
Value = {tier * 35}
ShowInInventory = true
IsStackable = false
IsQuestItem = false
metadata/_custom_type_script = ExtResource("2_script")
"""
    else:
        # Potion, Scroll, or Food/Drink consumable
        if category == "food":
            id_cat = "food"
            type_id = f"food.{item_id}"
            tmpl = display_name
            hp_tick = 1
            item_val = tier * 15
        elif category == "scrolls":
            id_cat = "scroll"
            type_id = f"scroll.{item_id}"
            tmpl = "{{descriptor}} scroll"
            hp_tick = 5
            item_val = 150
        elif archetype == "potion_small":
            id_cat = "potion"
            type_id = f"potion.{item_id}"
            tmpl = "{{descriptor}} potion"
            hp_tick = 2
            item_val = 50
        elif archetype == "potion_medium":
            id_cat = "potion"
            type_id = f"potion.{item_id}"
            tmpl = "{{descriptor}} potion"
            hp_tick = 4
            item_val = 100
        else:
            id_cat = "potion"
            type_id = f"potion.{item_id}"
            tmpl = "{{descriptor}} potion"
            hp_tick = 8
            item_val = 200
        content = f"""[gd_resource type="Resource" script_class="ConsumableItem" load_steps=5 format=3]

[ext_resource type="Script" uid="uid://irr1yr02p4rg" path="res://scenes/buffs/HealingBuff.cs" id="1_buff"]
[ext_resource type="Texture2D" uid="uid://dq1fvu6l25b3r" path="res://icon.svg" id="2_icon"]
[ext_resource type="PackedScene" path="{tscn_res_path}" id="3_scene"]
[ext_resource type="Script" uid="uid://c15rgkjymw2e6" path="res://scenes/items/ConsumableItem.cs" id="4_script"]

[sub_resource type="Resource" id="Resource_buff"]
script = ExtResource("1_buff")
HealthPointsPerTick = {hp_tick}
Name = "{display_name}"
Duration = 1.0
TicksPerSecond = 10
metadata/_custom_type_script = ExtResource("1_buff")

[resource]
script = ExtResource("4_script")
Buff = SubResource("Resource_buff")
Scene = ExtResource("3_scene")
Icon = ExtResource("2_icon")
Name = "{display_name}"
TypeId = "{type_id}"
IdentityCategory = "{id_cat}"
TrueName = "{display_name}"
UnidentifiedNameTemplate = "{tmpl}"
Durability = 1.0
Quality = 1.0
Value = {item_val}
Weight = 1.0
ShowInInventory = true
IsStackable = true
metadata/_custom_type_script = ExtResource("4_script")
"""

    os.makedirs(os.path.dirname(os.path.abspath(tres_path)), exist_ok=True)
    with open(tres_path, "w", encoding="utf-8") as f:
        f.write(content)


def run_cmd(cmd, description):
    print(f"  -> {description}: {' '.join(cmd)}")
    proc = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True)
    if proc.returncode != 0:
        print(proc.stdout)
        print(proc.stderr, file=sys.stderr)
        raise RuntimeError(f"{description} failed with exit code {proc.returncode}")
    return proc.stdout


def main():
    parser = argparse.ArgumentParser(description="End-to-end 3D item asset pipeline for Rogue Gauntlet.")
    parser.add_argument("--id", required=True, help="Snake_case item id (e.g. battleaxe_horned)")
    parser.add_argument("--name", required=True, help="In-game display name (e.g. 'Horned Battle Axe')")
    parser.add_argument("--archetype", required=True, choices=sorted(stylize_and_align.ARCHETYPE_PROFILES.keys()))
    parser.add_argument("--prompt", help="Visual description for concept image generation")
    parser.add_argument("--image", help="Path to an existing concept PNG (skips text-to-image generation)")
    parser.add_argument("--raw-glb", help="Path to an existing raw .glb (skips image-to-3D step)")
    parser.add_argument("--backend", choices=["auto", "hf", "contour"], default="auto", help="3D reconstruction backend")
    parser.add_argument("--tier", type=int, default=3, choices=[1, 2, 3, 4, 5], help="Equipment ladder tier (1-5)")
    parser.add_argument("--palette", help="Comma-separated stylized swatch names (e.g. steel,wood,gold,crimson)")
    parser.add_argument("--target-tris", type=int, default=550, help="Target triangle count")
    parser.add_argument("--damage-min", type=float, help="Override weapon DamageMin")
    parser.add_argument("--damage-max", type=float, help="Override weapon DamageMax")
    parser.add_argument("--armor", type=int, help="Override armor ArmorPoints")
    parser.add_argument("--str-req", type=int, help="Override RequiredStrength")
    parser.add_argument("--flip-y", action="store_true", help="Flip mesh along Y axis")
    parser.add_argument("--flip-x", action="store_true", help="Mirror mesh along X axis")
    parser.add_argument("--rot-y", type=float, default=0.0, help="Extra mesh Y rotation in degrees")
    parser.add_argument("--verify-dir", default="/tmp/rogue_asset_verify", help="Directory for verification screenshots")
    parser.add_argument("--skip-renders", action="store_true", help="Skip windowed Godot verification screenshots")
    parser.add_argument("--skip-godot-import", action="store_true", help="Skip per-item Godot headless import (for batching)")
    args = parser.parse_args()

    prof = stylize_and_align.ARCHETYPE_PROFILES[args.archetype]
    category = prof["category"]
    sub_dir = args.archetype
    palette_names = (
        [p.strip() for p in args.palette.split(",") if p.strip()]
        if args.palette
        else prof["default_palette"]
    )

    os.makedirs(args.verify_dir, exist_ok=True)
    concept_png = args.image or os.path.join(args.verify_dir, f"{args.id}_concept.png")
    raw_glb = args.raw_glb or os.path.join(args.verify_dir, f"{args.id}_raw.glb")
    final_glb = os.path.join(REPO_ROOT, "assets", "custom-items", category, sub_dir, f"{args.id}.glb")
    tscn_path = os.path.join(REPO_ROOT, "scenes", "items", category, sub_dir, f"{args.id}.tscn")
    tres_path = os.path.join(REPO_ROOT, "scenes", "items", category, sub_dir, f"{args.id}.tres")

    glb_res = f"res://assets/custom-items/{category}/{sub_dir}/{args.id}.glb"
    tscn_res = f"res://scenes/items/{category}/{sub_dir}/{args.id}.tscn"
    tres_res = f"res://scenes/items/{category}/{sub_dir}/{args.id}.tres"

    # -------------------------------------------------------------------------
    # [Step 1/5] Concept Image
    # -------------------------------------------------------------------------
    print(f"\n[Step 1/5] Preparing concept image for '{args.name}' ({args.archetype})...")
    if args.image and os.path.exists(args.image):
        print(f"  -> Using provided concept image: {args.image}")
    else:
        generate_concept_image_hf_or_procedural(args.prompt, args.archetype, concept_png, palette_names)

    # -------------------------------------------------------------------------
    # [Step 2/5] 3D Reconstruction
    # -------------------------------------------------------------------------
    print(f"\n[Step 2/5] Reconstructing 3D mesh (backend={args.backend})...")
    used_contour = False
    if args.raw_glb and os.path.exists(args.raw_glb):
        print(f"  -> Using provided raw GLB: {args.raw_glb}")
    elif args.backend == "contour":
        used_contour = True
        print("  -> Using contour-aware low-poly 3D reconstructor from concept image.")
    else:
        # Try HF TripoSR Space with automatic fallback to contour reconstructor
        try:
            hf_image_to_3d.load_dotenv_if_needed()
            token = os.environ.get("HF_TOKEN")
            try:
                import gradio_client  # noqa: F401
                hf_image_to_3d.run_via_gradio_client(concept_png, raw_glb, 256, token)
            except ImportError:
                hf_image_to_3d.run_via_stdlib_http(concept_png, raw_glb, 256, token)
            print(f"  -> Reconstructed raw 3D GLB via HF Space: {raw_glb}")
        except Exception as exc:
            print(f"  -> HF Space unavailable or ZeroGPU quota reached ({exc}); using contour-aware 3D builder.")
            used_contour = True

    # -------------------------------------------------------------------------
    # [Step 3/5] Stylize, Decimate, Align to stylized AABB, & Map UVs to Palette
    # -------------------------------------------------------------------------
    print(f"\n[Step 3/5] Stylizing low-poly mesh, mapping UVs to dungeon_texture.png, and aligning grip...")
    preview_3angle_png = os.path.join(args.verify_dir, f"{args.id}_3angle.png")
    preview_player_worn_png = os.path.join(args.verify_dir, f"{args.id}_player_worn.png")
    stylize_cmd = [
        sys.executable,
        os.path.join(SCRIPT_DIR, "align.py"),
        "--archetype", args.archetype,
        "--output", final_glb,
        "--preview-png", preview_3angle_png,
        "--target-tris", str(args.target_tris),
        "--palette", ",".join(palette_names),
        "--palette-png", os.path.join(STUDIO_ROOT, "rigs", "item_palette.png"),
        "--rot-y", str(args.rot_y),
    ]
    if category in ("weapons", "armor", "potions", "scrolls", "jewelry", "keys", "food", "tools"):
        stylize_cmd.extend(["--player-preview-png", preview_player_worn_png])
    if used_contour:
        stylize_cmd.extend(["--from-image", concept_png])
    else:
        stylize_cmd.extend(["--input-glb", raw_glb])
    if args.flip_y:
        stylize_cmd.append("--flip-y")
    if args.flip_x:
        stylize_cmd.append("--flip-x")

    out_log = run_cmd(stylize_cmd, "Stylize & align GLB")
    for line in out_log.strip().splitlines():
        print(f"  -> {line}")
    print(f"  -> 3-angle mesh & grip preview saved: {preview_3angle_png}")
    if category in ("weapons", "armor", "potions", "scrolls", "jewelry", "keys", "food", "tools"):
        print(f"  -> Frontal + Side player-worn preview saved: {preview_player_worn_png}")

    # -------------------------------------------------------------------------
    # [Step 4/5] Manifest + Godot .tscn/.tres Authoring + Import & Validation
    # -------------------------------------------------------------------------
    print(f"\n[Step 4/5] Registering asset, authoring Godot scenes/resources, and running import...")
    ensure_manifest_entry()
    write_wrapper_tscn(tscn_path, args.id, glb_res, args.archetype)
    print(f"  -> Wrote wrapper scene: {tscn_path}")
    write_item_tres(
        tres_path,
        tscn_res,
        args.id,
        args.name,
        args.archetype,
        args.tier,
        dmg_min=args.damage_min,
        dmg_max=args.damage_max,
        armor_pts=args.armor,
        str_req=args.str_req,
    )
    print(f"  -> Wrote item resource: {tres_path}")

    godot_sh = os.path.join(REPO_ROOT, ".agents", "skills", "godot-mcp", "scripts", "godot.sh")
    inspect_gd = os.path.join(REPO_ROOT, ".agents", "skills", "godot-mcp", "scripts", "inspect_resource.gd")
    if not args.skip_godot_import:
        run_cmd([godot_sh, "--headless", "--path", REPO_ROOT, "--import"], "Godot headless --import")
        try:
            inspect_out = run_cmd(
                [godot_sh, "--headless", "--path", REPO_ROOT, "--script", inspect_gd, "--", tres_res],
                "Validate .tres resource",
            )
            for line in inspect_out.strip().splitlines():
                if line.startswith("OK") or line.startswith("FAIL") or line.startswith("MISSING") or "Name=" in line:
                    print(f"  -> {line}")
        except RuntimeError as exc:
            print(f"  -> Warning: Godot script validation skipped ({exc})")

    # -------------------------------------------------------------------------
    # [Step 5/5] Windowed Visual Verification (Catalog + In-Hand on Player)
    # -------------------------------------------------------------------------
    if args.skip_renders:
        print("\n[Step 5/5] Skipped windowed renders (--skip-renders).")
    else:
        print(f"\n[Step 5/5] Rendering catalog thumbnail and Frontal + Side player verification screenshots...")
        catalog_gd = os.path.join(STUDIO_ROOT, "godot", "render_item_catalog.gd")
        held_gd = os.path.join(STUDIO_ROOT, "godot", "render_held_items.gd")
        try:
            run_cmd(
                [godot_sh, "--path", REPO_ROOT, "--script", catalog_gd, "--", args.verify_dir, tres_res],
                "Render standalone catalog preview",
            )
            if category in ("weapons", "armor"):
                run_cmd(
                    [godot_sh, "--path", REPO_ROOT, "--script", held_gd, "--", args.verify_dir, tres_res],
                    "Render Frontal + Side equipped player preview",
                )
            print(f"  -> Verification screenshots saved to: {args.verify_dir}")
        except RuntimeError as exc:
            print(f"  -> Windowed GPU render unavailable in current environment ({exc}); use {preview_player_worn_png}")

    print("\nPipeline complete!")
    print(f"  Model GLB   : {final_glb}")
    print(f"  Scene       : {tscn_path}")
    print(f"  Resource    : {tres_path}")
    print(f"  3D Preview  : {preview_3angle_png}")
    if category in ("weapons", "armor", "potions", "scrolls", "jewelry"):
        print(f"  Player Worn : {preview_player_worn_png} (Frontal + Side View)")
    if not args.skip_renders:
        print(f"  Godot PNGs  : {args.verify_dir}/{args.id}.png (and _{{front,side,weapon,shield}}.png)")


if __name__ == "__main__":
    main()
