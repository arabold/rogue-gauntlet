#!/usr/bin/env python3
"""
The character kit's pipeline: make a character from the scripted kit (`accessories/`), check it against its
concept drawing, and export it into the game as a drop-in for a KayKit model.

Every character keeps its sources in its own folder next to this skill, outside Godot's asset tree (Godot never
scans dot-folders), and exports into the game's assets:

    .agents/skills/assets/characters/<family>/<name>/
        character.json   the recipe (head, body, outfit, colors) and its export settings
        reference.png    the concept drawing it is modelled after (`reference` draws one: a paid image call)
        preview.png      renders of the build: the drawing, then the build in KayKit's idle pose and the T-pose
        lineup.png       the exported GLB in Godot, posed in its clips beside the KayKit model it replaces
        weapons.json     baked weapons' meshes, dumped once from the game's items
        legacy.glb       the model an export replaced at its path (kept automatically, once)
    -> assets/custom-heroes/<name>.glb                family `heroes`
    -> assets/custom-monsters/<family>/<name>.glb     every other family

`character.json` holds a `characters.build` recipe (`head`: a `heads.ROSTER` preset or a recipe, `body`,
`outfit`, `colors`) plus:

    base       another character (`family/name`) this one extends; its own entries replace the base's
    template   the KayKit GLB the export is built on (`export.TEMPLATES`): `skeleton` for monsters
               (Skeleton_Warrior's clips), `barbarian`, `knight`, `mage`, `rogue` for heroes (that class's clips and
               prop nodes, which `scenes/player/classes/*.tres` look up by name)
    scale      stature through the rig (the existing goblins are 0.74); weapons keep their true size
    weapons    `{"handslot.r": "res://scenes/items/weapons/<item>.tres", "handslot.l": ...}` baked into the GLB
               (monsters; heroes get theirs from the equipment system)
    materials  `{slot: {"metallic": m, "roughness": r} | {"emissive": true}}` over `export.MATERIALS`
    builder    `kit` (default) or `golem` (`golems.py`, on Rig_Large: previews only, no export yet)
    concept    the brief the reference was drawn from

    python character_kit.py list
    python character_kit.py new heroes/elf --base heroes/barbarian_base
    python character_kit.py reference goblin/goblin --brief "turn this mannequin into a sneaky goblin with ..."
    python character_kit.py preview goblin/goblin
    python character_kit.py export goblin/goblin heroes/barbarian      (or --all; --no-import skips Godot)
    python character_kit.py lineup goblin/goblin                       (Godot render beside the template)
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import monster_mesh as mm  # noqa: E402
from accessories import catalog, characters, export, golems, kit, match  # noqa: E402
from accessories.rig import Rig, mannequin, skin, skinned  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = export.REPO
GODOT = os.path.join(REPO, ".agents", "skills", "godot-mcp", "scripts", "godot.sh")
WHITE = (255, 255, 255)
HAT_KINDS, CAPE_KINDS = ("helmet", "crown"), ("cape",)
LINEUP_CLIPS = {"skeleton": [("Idle_Combat", 0.5), ("1H_Melee_Attack_Chop", 0.4), ("Walking_D_Skeletons", 0.5)],
                "hero": [("Idle", 0.3), ("1H_Melee_Attack_Slice_Diagonal", 0.45), ("Walking_B", 0.4)]}


class Character:
    """One character: its source folder, recipe and the GLB it exports to."""

    def __init__(self, key):
        if key.count("/") != 1:
            raise SystemExit(f"a character is family/name, e.g. goblin/goblin (got {key!r})")
        self.key = key
        self.family, self.name = key.split("/")
        self.dir = os.path.join(characters.CHARACTERS, self.family, self.name)
        self.recipe_path = os.path.join(self.dir, "character.json")
        self.reference = os.path.join(self.dir, "reference.png")
        self.title = "_".join(w.capitalize() for w in self.name.split("_"))
        self.glb = (os.path.join(REPO, "assets", "custom-heroes", f"{self.name}.glb") if self.family == "heroes"
                    else os.path.join(REPO, "assets", "custom-monsters", self.family, f"{self.name}.glb"))

    @property
    def res(self):
        return "res://" + os.path.relpath(self.glb, REPO).replace(os.sep, "/")

    def recipe(self):
        if not os.path.exists(self.recipe_path):
            raise SystemExit(f"no {self.recipe_path}")
        return characters.load(self.key)

    def build(self):
        """`(recipe, pieces, colors)`, pieces in the rest pose."""
        r = self.recipe()
        builder = r.get("builder", "kit")
        if builder == "golem":
            return r, golems.build(), golems.COLORS
        return r, *characters.build(r)

    def posed(self):
        """The build merged in the rig's idle pose, and in its rest pose."""
        r, pieces, colors = self.build()
        if r.get("builder") == "golem":
            idle = Rig.load("large").pose(pieces, *golems.POSE, helpers=golems.HELPERS)
        else:
            idle = skinned(Rig.load("medium"), pieces, "Idle_A")
        return kit.Piece.merge(*idle.values()), kit.Piece.merge(*pieces.values()), colors

    def weapons(self, recipe):
        """`export.export`'s weapons: each item's triangles and colors, dumped from the game once and cached."""
        spec = recipe.get("weapons") or {}
        path = os.path.join(self.dir, "weapons.json")
        cache = json.load(open(path)) if os.path.exists(path) else {}
        out = {}
        for bone, item in spec.items():
            if item not in cache or len(cache[item][0]) < 14:  # dumped before materials were recorded
                tmp = path + ".tmp"
                subprocess.run([GODOT, "--headless", "--path", REPO, "--script", os.path.join(HERE, "dump_item_mesh.gd"),
                                "--", tmp, item], check=True, capture_output=True, timeout=600)
                cache[item] = json.load(open(tmp))["tris"]
                os.remove(tmp)
                with open(path, "w") as f:
                    json.dump(cache, f)
            a = np.asarray(cache[item], float)
            pal, lab = mm.cluster_colors(a[:, 9:12].astype(np.uint8), k=8)
            out[bone] = (a[:, :9].reshape(-1, 3, 3)[:, ::-1],  # Godot winds front faces clockwise, glTF the other way
                         np.asarray(pal)[lab], a[:, 12:14])
        return out


def _tile(img, h):
    return img.resize((round(img.width * h / img.height), h))


def preview(c, size=420):
    """`preview.png`: the reference (if any), then the build in the idle pose (front, side) and the rest pose."""
    idle, rest, colors = c.posed()
    tiles = [catalog.render(idle, None, size, yaw=y, pitch=0.0, colors=colors, background=WHITE) for y in (0.0, -90.0)]
    tiles.append(catalog.render(rest, None, size, yaw=-25.0, pitch=8.0, colors=colors, background=WHITE))
    if os.path.exists(c.reference):
        tiles.insert(0, _tile(Image.open(c.reference).convert("RGB"), size))
    out = Image.new("RGB", (sum(t.width for t in tiles), size), WHITE)
    x = 0
    for t in tiles:
        out.paste(t, (x, 0))
        x += t.width
    out.save(os.path.join(c.dir, "preview.png"))
    print(f"{c.key}: {len(idle.faces)} triangles -> {os.path.relpath(os.path.join(c.dir, 'preview.png'), REPO)}")


def reference(c, brief, provider="gemini"):
    """`reference.png`: KayKit's mannequin in the idle pose, front and side, redrawn by `brief` (one paid call)."""
    r = c.recipe() if os.path.exists(c.recipe_path) else {}
    size = "large" if r.get("rig") == "large" else "medium"
    D = Rig.load(size).deltas("Idle_A", 0.0)
    man = kit.Piece.merge(*(kit.Piece(skin(D, V, N, W), F, "skin") for V, F, N, W in mannequin(size).values()))
    views = [catalog.render(man, None, 512, yaw=y, pitch=0.0, colors={"skin": (190, 190, 190)}, background=WHITE)
             for y in (0.0, -90.0)]
    sheet = Image.new("RGB", (1024, 512), WHITE)
    for i, v in enumerate(views):
        sheet.paste(v, (512 * i, 0))
    drawing = match.inspire(sheet, brief, provider, prompt=match.CREATURE_PROMPT).convert("RGB")
    if drawing.width > 1400:
        drawing = drawing.resize((1376, round(drawing.height * 1376 / drawing.width)), Image.LANCZOS)
    os.makedirs(c.dir, exist_ok=True)
    drawing.save(c.reference, optimize=True)
    if os.path.exists(c.recipe_path):
        data = json.load(open(c.recipe_path))
        data["concept"] = brief
        _write(c.recipe_path, data)
    print(f"{c.key}: {os.path.relpath(c.reference, REPO)}")


def _write(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")


def export_glb(c):
    """Write the character's GLB and its `.import` (template import settings, looping clips)."""
    r, pieces, colors = c.build()
    if r.get("builder", "kit") != "kit":
        raise SystemExit(f"{c.key}: the {r['builder']} builder has no GLB export yet (Rig_Large has no KayKit "
                         "template with the game's clips)")
    kind = {k: re.sub(r"\d+$", "", k).lower() for k in pieces}
    hat = [p for k, p in pieces.items() if kind[k] in HAT_KINDS]
    cape = [p for k, p in pieces.items() if kind[k] in CAPE_KINDS]
    worn = {k: p for k, p in pieces.items() if kind[k] not in HAT_KINDS + CAPE_KINDS}
    template = r.get("template", "skeleton")
    legacy = os.path.join(c.dir, "legacy.glb")
    if os.path.exists(c.glb) and not os.path.exists(legacy):  # an older model at this path, not one of ours: keep it
        if mm.read_glb(c.glb)[0].get("asset", {}).get("generator") != export.GENERATOR:
            shutil.copy2(c.glb, legacy)
            print(f"{c.key}: kept the earlier {os.path.relpath(c.glb, REPO)} as {os.path.relpath(legacy, REPO)}")
    export.export(c.glb, c.title, worn, colors, template=template, scale=float(r.get("scale", 1.0)),
                  hat=kit.Piece.merge(*hat) if hat else None, cape=kit.Piece.merge(*cape) if cape else None,
                  weapons=c.weapons(r), materials=r.get("materials"))
    export.import_settings(c.glb, template)
    print(f"{c.key}: {os.path.relpath(c.glb, REPO)} ({os.path.getsize(c.glb) // 1024} KB)")


def godot_import(chars):
    """Import the exported GLBs in Godot and check each against what the game relies on."""
    subprocess.run([GODOT, "--headless", "--path", REPO, "--import"], capture_output=True, timeout=1200)
    run = subprocess.run([GODOT, "--headless", "--path", REPO, "--script", os.path.join(HERE, "inspect_character.gd"),
                          "--", *[c.res for c in chars]], capture_output=True, text=True, timeout=600)
    ok = True
    for line in run.stdout.splitlines():
        if not line.startswith("{"):
            continue
        info = json.loads(line)
        good = info.get("skeleton") and info.get("clips", 0) > 0 and info.get("looping")
        ok &= bool(good)
        print(f"  {'ok ' if good else 'BAD'} {info['path']}: {info.get('bones')} bones, {info.get('clips')} clips "
              f"({len(info.get('looping', []))} looping), props {info.get('props')}, rig scale {info.get('rig_scale')}")
    if not ok:
        raise SystemExit("an exported character is missing what the game expects (see above)")


def lineup(c):
    """`lineup.png`: the KayKit template and the export in Godot, side by side in three of the game's clips."""
    r = c.recipe()
    template = r.get("template", "skeleton")
    original = "res://" + os.path.relpath(export.TEMPLATES.get(template, template), REPO).replace(os.sep, "/")
    args = []
    for clip, t in LINEUP_CLIPS["skeleton" if template == "skeleton" else "hero"]:
        args += [original, clip, str(t), c.res, clip, str(t)]
    out = os.path.join(c.dir, "lineup.png")
    subprocess.run([GODOT, "--path", REPO, "--script", os.path.join(HERE, "render_character_lineup.gd"), "--", out,
                    *args], capture_output=True, timeout=600)
    print(f"{c.key}: {os.path.relpath(out, REPO)}")


def new(c, base=None):
    if os.path.exists(c.recipe_path):
        raise SystemExit(f"{c.key} exists")
    os.makedirs(c.dir, exist_ok=True)
    data = {"base": base} if base else {
        "template": "skeleton", "scale": 1.0, "head": "goblin",
        "body": {"torso": "kaykit", "arms": {"hand": "mitten"}, "legs": {"foot": "boot"}}, "outfit": [], "colors": {}}
    _write(c.recipe_path, data)
    print(f"{c.key}: {os.path.relpath(c.recipe_path, REPO)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["list", "new", "reference", "preview", "export", "lineup"])
    ap.add_argument("characters", nargs="*", help="family/name")
    ap.add_argument("--all", action="store_true", help="every character")
    ap.add_argument("--base", help="new: the character to extend")
    ap.add_argument("--brief", help="reference: what to draw over the mannequin")
    ap.add_argument("--provider", default="gemini")
    ap.add_argument("--no-import", action="store_true", help="export: leave the Godot import for later")
    args = ap.parse_args()
    keys = characters.keys() if args.all or args.command == "list" else args.characters
    chars = [Character(k) for k in keys]
    if args.command == "list":
        for c in chars:
            r = c.recipe()
            state = "exported" if os.path.exists(c.glb) else "-"
            print(f"{c.key:28s} {r.get('builder', 'kit'):6s} {r.get('template', ''):10s} "
                  f"{'ref' if os.path.exists(c.reference) else '   '}  {state:8s} {os.path.relpath(c.glb, REPO)}")
    elif args.command == "new":
        for c in chars:
            new(c, args.base)
    elif args.command == "reference":
        if not args.brief:
            raise SystemExit("reference needs --brief")
        for c in chars:
            reference(c, args.brief, args.provider)
    elif args.command == "preview":
        for c in chars:
            preview(c)
    elif args.command == "export":
        chars = [c for c in chars if not args.all or c.recipe().get("builder", "kit") == "kit"]
        for c in chars:
            export_glb(c)
        if not args.no_import:
            godot_import(chars)
    elif args.command == "lineup":
        for c in chars:
            lineup(c)


if __name__ == "__main__":
    main()
