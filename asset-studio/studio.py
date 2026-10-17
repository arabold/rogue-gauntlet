#!/usr/bin/env python3
"""
Rogue Gauntlet Asset Studio CLI (`asset-studio/studio.py`).

Assembles characters, monsters, and items procedurally from modular prefabs
(`asset-studio/prefabs/`), keeps all interim steps (recipes, concept art, previews,
lineups, weapon mesh caches, part studies) inside `asset-studio/` (ignored by Godot via
`.gdignore`), and exports finished `.glb` + `.import` files into the game's `assets/` tree:

    asset-studio/characters/<family>/<name>/
        character.json   modular recipe (head, body, outfit, colors) and export settings
        reference.png    2D concept reference art (`reference` command)
        lineup.png       multi-pose visual strip (reference, idle front/side, attack, walk)
        weapons.json     cached weapon meshes dumped from the game's item resources
    -> assets/custom-heroes/<name>.glb                family `heroes`
    -> assets/custom-monsters/<family>/<name>.glb     every other family

Commands:
    .venv/bin/python asset-studio/studio.py list
    .venv/bin/python asset-studio/studio.py schema [section]
    .venv/bin/python asset-studio/studio.py new heroes/elf --base heroes/barbarian_base
    .venv/bin/python asset-studio/studio.py reference goblin/goblin --brief "turn this mannequin into..."
    .venv/bin/python asset-studio/studio.py preview goblin/goblin
    .venv/bin/python asset-studio/studio.py export goblin/goblin heroes/barbarian   (or --all; --no-import skips Godot)
    .venv/bin/python asset-studio/studio.py lineup goblin/goblin
    .venv/bin/python asset-studio/studio.py catalog anatomy
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

STUDIO_DIR = os.path.dirname(os.path.abspath(__file__))
if STUDIO_DIR not in sys.path:
    sys.path.insert(0, STUDIO_DIR)

from core import catalog, export, kit, match  # noqa: E402
from core import mesh as mm  # noqa: E402
from core.rig import Rig, mannequin, skin, skinned  # noqa: E402
from prefabs.creatures import characters, golems  # noqa: E402
from prefabs import registry  # noqa: E402

REPO = export.REPO
GODOT = os.path.join(REPO, ".agents", "skills", "godot-mcp", "scripts", "godot.sh")
GODOT_SCRIPTS = os.path.join(STUDIO_DIR, "godot")
WHITE = (255, 255, 255)
HAT_KINDS, CAPE_KINDS = ("helmet", "crown"), ("cape",)
LINEUP_CLIPS = {
    "skeleton": [("Idle_Combat", 0.5), ("1H_Melee_Attack_Chop", 0.4), ("Walking_D_Skeletons", 0.5)],
    "hero": [("Idle", 0.3), ("1H_Melee_Attack_Slice_Diagonal", 0.45), ("Walking_B", 0.4)],
}
KNOWN_GENERATORS = {export.GENERATOR, "Rogue Gauntlet character_kit"}


class Character:
    """One character: its source folder in `asset-studio/characters/`, recipe, and target GLB in `assets/`."""

    def __init__(self, key):
        if key.count("/") != 1:
            raise SystemExit(f"a character is family/name, e.g. goblin/goblin (got {key!r})")
        self.key = key
        self.family, self.name = key.split("/")
        self.dir = os.path.join(characters.CHARACTERS, self.family, self.name)
        self.recipe_path = os.path.join(self.dir, "character.json")
        self.reference = os.path.join(self.dir, "reference.png")
        self.title = "_".join(w.capitalize() for w in self.name.split("_"))
        self.glb = (
            os.path.join(REPO, "assets", "custom-heroes", f"{self.name}.glb")
            if self.family == "heroes"
            else os.path.join(REPO, "assets", "custom-monsters", self.family, f"{self.name}.glb")
        )

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
            if item not in cache or len(cache[item][0]) < 14:
                tmp = path + ".tmp"
                subprocess.run(
                    [GODOT, "--headless", "--path", REPO, "--script", os.path.join(GODOT_SCRIPTS, "dump_item_mesh.gd"),
                     "--", tmp, item],
                    check=True,
                    capture_output=True,
                    timeout=600,
                )
                cache[item] = json.load(open(tmp))["tris"]
                os.remove(tmp)
                with open(path, "w") as f:
                    json.dump(cache, f)
            a = np.asarray(cache[item], float)
            pal, lab = mm.cluster_colors(a[:, 9:12].astype(np.uint8), k=8)
            out[bone] = (
                a[:, :9].reshape(-1, 3, 3)[:, ::-1],
                np.asarray(pal)[lab],
                a[:, 12:14],
            )
        return out


def _tile(img, h):
    return img.resize((round(img.width * h / img.height), h))


def _attach_weapons(piece, weaps, rig, clip, t, colors):
    if not weaps:
        return piece, colors
    all_pieces = [piece]
    ext_colors = dict(colors)
    w_worlds = rig.worlds(clip, t)
    for bone, (tris, pal_cols, _) in weaps.items():
        if bone not in w_worlds:
            continue
        W = w_worlds[bone]
        w_tris = tris @ W[:3, :3].T + W[:3, 3]
        V = w_tris.reshape(-1, 3)
        F = np.arange(len(V)).reshape(-1, 3)
        slots = []
        for rgb in pal_cols:
            s_name = f"_wep_{bone}_{rgb[0]}_{rgb[1]}_{rgb[2]}"
            slots.append(s_name)
            ext_colors[s_name] = tuple(int(x) for x in rgb)
        all_pieces.append(kit.Piece(V, F, np.array(slots, dtype=object)))
    return kit.Piece.merge(*all_pieces), ext_colors


def lineup(c, size=420):
    """`lineup.png`: 2D reference (if present), front view, side profile, and action poses in pure Python."""
    r, pieces, colors = c.build()
    template = r.get("template", "skeleton")
    weaps = c.weapons(r)
    builder = r.get("builder", "kit")

    tiles = []
    if os.path.exists(c.reference):
        tiles.append(_tile(Image.open(c.reference).convert("RGB"), size))

    if builder == "golem":
        rig = Rig.load("large")
        golem_idle = rig.pose(pieces, *golems.POSE, helpers=golems.HELPERS)
        golem_piece = kit.Piece.merge(*golem_idle.values())
        for yaw, pitch in ((0.0, 0.0), (-90.0, 0.0), (-30.0, 6.0), (30.0, 6.0)):
            tiles.append(catalog.render(golem_piece, None, size=size, yaw=yaw, pitch=pitch, colors=colors, background=WHITE))
    else:
        rig_path = export.TEMPLATES.get(template, os.path.join(STUDIO_DIR, "rigs", "templates", f"{template}.glb"))
        rig = Rig(rig_path) if os.path.exists(rig_path) else Rig.load("medium")
        clips = LINEUP_CLIPS.get(template, LINEUP_CLIPS.get("hero" if c.family == "heroes" else "skeleton"))

        # Clip 0: Idle Front and Side
        idle_clip, idle_t = clips[0]
        idle_pcs = skinned(rig, pieces, idle_clip, t=idle_t)
        idle_merged = kit.Piece.merge(*idle_pcs.values())
        idle_final, idle_colors = _attach_weapons(idle_merged, weaps, rig, idle_clip, idle_t, colors)
        tiles.append(catalog.render(idle_final, None, size=size, yaw=0.0, pitch=0.0, colors=idle_colors, background=WHITE))
        tiles.append(catalog.render(idle_final, None, size=size, yaw=-90.0, pitch=0.0, colors=idle_colors, background=WHITE))

        # Action clips (Attack, Walk)
        for clip, t in clips[1:]:
            action_pcs = skinned(rig, pieces, clip, t=t)
            action_merged = kit.Piece.merge(*action_pcs.values())
            action_final, action_colors = _attach_weapons(action_merged, weaps, rig, clip, t, colors)
            tiles.append(catalog.render(action_final, None, size=size, yaw=-30.0, pitch=6.0, colors=action_colors, background=WHITE))

    out = Image.new("RGB", (sum(t.width for t in tiles), size), WHITE)
    x = 0
    for t in tiles:
        out.paste(t, (x, 0))
        x += t.width
    out_path = os.path.join(c.dir, "lineup.png")
    out.save(out_path)
    print(f"{c.key}: wrote {os.path.relpath(out_path, REPO)} ({out.size[0]}x{out.size[1]})")
    return out


def preview(c, size=420):
    """Alias for lineup: renders reference and animated action poses to lineup.png."""
    return lineup(c, size=size)


def reference(c, brief, provider="gemini"):
    """`reference.png`: mannequin in the idle pose, front and side, redrawn by `brief` (one paid call)."""
    r = c.recipe() if os.path.exists(c.recipe_path) else {}
    size = "large" if r.get("rig") == "large" else "medium"
    D = Rig.load(size).deltas("Idle_A", 0.0)
    man = kit.Piece.merge(*(kit.Piece(skin(D, V, N, W), F, "skin") for V, F, N, W in mannequin(size).values()))
    views = [
        catalog.render(man, None, size=512, yaw=y, pitch=0.0, colors={"skin": (190, 190, 190)}, background=WHITE)
        for y in (0.0, -90.0)
    ]
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
    """Write the character's GLB to `assets/` and its `.import` (template import settings, looping clips)."""
    r, pieces, colors = c.build()
    if r.get("builder", "kit") != "kit":
        raise SystemExit(f"{c.key}: the {r['builder']} builder has no GLB export yet")
    kind = {k: re.sub(r"\d+$", "", k).lower() for k in pieces}
    hat = [p for k, p in pieces.items() if kind[k] in HAT_KINDS]
    cape = [p for k, p in pieces.items() if kind[k] in CAPE_KINDS]
    worn = {k: p for k, p in pieces.items() if kind[k] not in HAT_KINDS + CAPE_KINDS}
    template = r.get("template", "skeleton")
    legacy = os.path.join(c.dir, "legacy.glb")
    if os.path.exists(c.glb) and not os.path.exists(legacy):
        if mm.read_glb(c.glb)[0].get("asset", {}).get("generator") not in KNOWN_GENERATORS:
            shutil.copy2(c.glb, legacy)
            print(f"{c.key}: kept the earlier {os.path.relpath(c.glb, REPO)} as {os.path.relpath(legacy, REPO)}")
    export.export(
        c.glb,
        c.title,
        worn,
        colors,
        template=template,
        scale=float(r.get("scale", 1.0)),
        hat=kit.Piece.merge(*hat) if hat else None,
        cape=kit.Piece.merge(*cape) if cape else None,
        weapons=c.weapons(r),
        materials=r.get("materials"),
    )
    export.import_settings(c.glb, template)
    print(f"{c.key}: {os.path.relpath(c.glb, REPO)} ({os.path.getsize(c.glb) // 1024} KB)")
    lineup(c)


def godot_import(chars):
    """Import the exported GLBs in Godot and check each against what the game relies on."""
    subprocess.run([GODOT, "--headless", "--path", REPO, "--import"], capture_output=True, timeout=1200)
    run = subprocess.run(
        [GODOT, "--headless", "--path", REPO, "--script", os.path.join(GODOT_SCRIPTS, "inspect_character.gd"),
         "--", *[c.res for c in chars]],
        capture_output=True,
        text=True,
        timeout=600,
    )
    ok = True
    for line in run.stdout.splitlines():
        if not line.startswith("{"):
            continue
        info = json.loads(line)
        good = info.get("skeleton") and info.get("clips", 0) > 0 and info.get("looping")
        ok &= bool(good)
        print(
            f"  {'ok ' if good else 'BAD'} {info['path']}: {info.get('bones')} bones, {info.get('clips')} clips "
            f"({len(info.get('looping', []))} looping), props {info.get('props')}, rig scale {info.get('rig_scale')}"
        )
    if not ok:
        raise SystemExit("an exported character is missing what the game expects (see above)")


def new(c, base=None):
    if os.path.exists(c.recipe_path):
        raise SystemExit(f"{c.key} exists")
    os.makedirs(c.dir, exist_ok=True)
    data = (
        {"base": base}
        if base
        else {
            "template": "skeleton",
            "scale": 1.0,
            "head": "goblin",
            "body": {"torso": "standard", "arms": {"hand": "mitten"}, "legs": {"foot": "boot"}},
            "outfit": [],
            "colors": {},
        }
    )
    _write(c.recipe_path, data)
    print(f"{c.key}: {os.path.relpath(c.recipe_path, REPO)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["list", "schema", "new", "reference", "preview", "export", "lineup", "catalog"])
    ap.add_argument("targets", nargs="*", help="family/name for character commands, or section for schema/catalog")
    ap.add_argument("--all", action="store_true", help="every character")
    ap.add_argument("--base", help="new: the character to extend")
    ap.add_argument("--brief", help="reference: what to draw over the mannequin")
    ap.add_argument("--provider", default="gemini")
    ap.add_argument("--no-import", action="store_true", help="export: leave the Godot import for later")
    ap.add_argument("-o", "--out", help="catalog: output PNG path")
    ap.add_argument("--yaws", default="0,32,90,180", help="catalog: comma-separated yaw angles in degrees")
    ap.add_argument("--index", action="store_true", help="catalog: generate markdown index and individual previews for library")
    args = ap.parse_args()

    if args.command == "schema":
        schema = registry.get_schema()
        if args.targets:
            for key in args.targets:
                schema = schema.get(key, {})
        print(json.dumps(schema, indent=2))
        return

    if args.command == "catalog":
        if args.index or (args.targets and args.targets[0] == "library" and args.index):
            from prefabs import library
            library.generate_index()

        mods = catalog._get_modules()
        which = args.targets[0] if args.targets else "all"
        if which not in mods and which != "all":
            raise SystemExit(f"unknown catalog module {which!r}; choose from: {', '.join([*mods, 'all'])}")
        yaws = [float(a) for a in args.yaws.split(",")]
        if which == "all":
            primary = ["anatomy", "outfits", "items", "details", "characters", "golems"]
            chosen = [mods[k] for k in primary if k in mods]
        else:
            chosen = [mods[which]]
        items = [it for m in chosen for it in m.catalog()]
        out_path = args.out or os.path.join(STUDIO_DIR, "catalogs", f"{which}_catalog.png")
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
        catalog.sheet(items, yaws=yaws).save(out_path)
        print(f"wrote {os.path.relpath(out_path, REPO)} ({len(items)} variants)")
        return

    keys = characters.keys() if args.all or args.command == "list" else args.targets
    chars = [Character(k) for k in keys]
    if args.command == "list":
        for c in chars:
            r = c.recipe()
            state = "exported" if os.path.exists(c.glb) else "-"
            print(
                f"{c.key:28s} {r.get('builder', 'kit'):6s} {r.get('template', ''):10s} "
                f"{'ref' if os.path.exists(c.reference) else '   '}  {state:8s} {os.path.relpath(c.glb, REPO)}"
            )
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
