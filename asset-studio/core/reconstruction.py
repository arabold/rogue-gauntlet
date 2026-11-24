"""`studio.py reconstruct`: the scripted path from an approved concept to a rigged Blender source.

    reconstruct turnaround <key> [--count N]   draft four-view sheets from reference.png (paid image calls)
    reconstruct views <key> [--image SHEET]    install a selected sheet and split it into the four views
    reconstruct tripo <key>                    run the multiview image-to-3D job (spends Tripo credits)
    reconstruct parts <key>                    render a sheet identifying the reconstruction's pieces
    reconstruct build <key>                    build the rigged Blender source from reconstruction.json
    reconstruct palette <key>                  recolor the saved source from reconstruction.json's palette

Selected inputs (sheet, views, prompts, `reconstruction.json`, the source) live in the character folder; drafts, the
reconstruction GLB, part sheets and Blender logs live in the OS temporary work root. Paid steps never run implicitly.
See the character workflow guide for when to use it and how to write `reconstruction.json`.
"""

import argparse
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

from core import ai, blender, character_workflow, mesh, tripo
from core.paths import work_root

STUDIO = Path(__file__).resolve().parents[1]
JOBS = STUDIO / "blender" / "reconstruction"
VIEWS = ("front", "left", "back", "right")  # sheet order: top left, top right, bottom left, bottom right
BACKGROUND = 232
FRAMES = STUDIO / "rigs" / "large" / "attachment_frames.json"  # verified hand-slot mounts of the large rig
PROP_PREFABS = ("executioner_sword", "warden_shield")  # functions of `items.executioner`
GLTF_TO_BLENDER = np.array([[1, 0, 0], [0, 0, -1], [0, 1, 0]])  # glTF's Y-up to Blender's Z-up


class Source:
    """One character's authoring folder and its temporary reconstruction workspace."""

    def __init__(self, key):
        self.key = key
        self.dir = STUDIO / "characters" / key
        if not (self.dir / "reference.png").is_file():
            raise SystemExit(f"{key}: no approved reference.png in {self.dir}")
        self.work = work_root() / "characters" / key / "reconstruction"
        self.work.mkdir(parents=True, exist_ok=True)
        self.glb = self.work / "reconstruction.glb"

    def json(self, name):
        path = self.dir / name
        if not path.is_file():
            raise SystemExit(f"{self.key}: missing {path.relative_to(STUDIO)}")
        return json.loads(path.read_text())


def split_sheet(sheet, size=1024, height=880, margin=0.86):
    """The four figures of a 2 x 2 turnaround as square views at one shared figure height.

    Image models rarely keep the four figures at one scale or inside their quadrants (a blossom crossed the midline),
    so figures are the four largest connected silhouettes, ordered by row and column, and each is scaled to `height`,
    or lower when the widest figure would otherwise fill more than `margin` of the view.
    """
    im = sheet.convert("RGB")
    mask = (np.asarray(im) < BACKGROUND).any(axis=2)
    lab, _ = ndimage.label(ndimage.binary_dilation(mask, iterations=12))
    sizes = ndimage.sum(mask, lab, range(1, lab.max() + 1))
    keep = np.argsort(sizes)[::-1][:4] + 1
    if len(keep) < 4:
        raise ValueError("A turnaround sheet needs four separate figures")
    objects = ndimage.find_objects(lab)
    boxes = []
    for k in keep:
        ys, xs = objects[k - 1]
        yy, xx = np.nonzero(mask[ys, xs] & (lab[ys, xs] == k))
        boxes.append((xs.start + xx.min(), ys.start + yy.min(), xs.start + xx.max() + 1, ys.start + yy.max() + 1))
    h = mask.shape[0]
    boxes.sort(key=lambda b: (round((b[1] + b[3]) / h - 0.5), (b[0] + b[2]) / 2))
    height = min(height, margin * size / max((x1 - x0) / (y1 - y0) for x0, y0, x1, y1 in boxes))
    views = {}
    for name, (x0, y0, x1, y1) in zip(VIEWS, boxes):
        crop = im.crop((x0, y0, x1, y1))
        s = height / (y1 - y0)
        crop = crop.resize((round(crop.width * s), round(crop.height * s)), Image.LANCZOS)
        canvas = Image.new("RGB", (size, size), "white")
        canvas.paste(crop, ((size - crop.width) // 2, (size - crop.height) // 2))
        views[name] = canvas
    return views


def base_color(entry):
    """A palette entry is an RGB triple, shaded by the Studio's standard ramp, or a dict around its `base`: with `light`
    and `shadow` for a ramp that also shifts hue, such as a slime's lime crown over its emerald base, and with `match`
    for a slot the turnaround draws in another tone than the game should show (see `match_color`)."""
    return entry["base"] if isinstance(entry, dict) else entry


def match_color(entry):
    """The tone the turnaround is matched against: a dict entry's `match`, else its albedo. A concept that draws a
    skull's eye sockets mid-grey matches them as such, while the game shades them dark."""
    return entry.get("match", entry["base"]) if isinstance(entry, dict) else entry


def check_config(config):
    """Problems in a `reconstruction.json`, found before Blender runs: every part needs a budget, palette slots that
    exist and a weight rule, and every named joint and chain point must be defined."""
    problems = []
    parts = set(config["parts"])
    pieces = [i for indices in config["parts"].values() for i in indices]
    if len(pieces) != len(set(pieces)):
        problems.append("a reconstruction piece is listed under two parts")
    for label, mapping in (("budgets", config["budgets"]), ("slots", config["slots"]), ("rig.weights", config["rig"]["weights"])):
        if set(mapping) != parts:
            problems.append(f"{label} must name exactly the parts: {sorted(parts ^ set(mapping))}")
    problems += [f"palette {name} needs a base, and light only with shadow" for name, entry in config["palette"].items()
                 if isinstance(entry, dict) and ("base" not in entry or ("light" in entry) != ("shadow" in entry))]
    unknown = {s for slots in config["slots"].values() for s in slots} - set(config["palette"])
    if unknown:
        problems.append(f"slots missing from the palette: {sorted(unknown)}")
    problems += [f"center_on names unknown part {p}" for p in config["center_on"] if p not in parts]
    problems += [f"rings name unknown part {p}" for p in config.get("rings", {}) if p not in parts]
    problems += [f"by_island names unknown part {p}" for p in config.get("by_island", ()) if p not in parts]
    problems += [f"drop_islands names unknown part {p}" for p in config.get("drop_islands", {}) if p not in parts]
    problems += [f"undersides gives {p} slot {u['slot']} outside its slots" for p, u in config.get("undersides", {}).items()
                 if u["slot"] not in config["slots"].get(p, ())]
    kind = config["rig"].get("kind", "humanoid")
    if kind not in ("humanoid", "creature", "authored"):
        problems.append(f"rig.kind must be humanoid, creature or authored, not {kind}")
    if kind == "authored":
        bones = config["rig"].get("bones", {})
        problems += [f"rig.bones {b} names unknown parent {s['parent']}" for b, s in bones.items()
                     if s.get("parent") and s["parent"] not in bones]
        used = {b for rule in config["rig"]["weights"].values()
                for b in ([rule] if isinstance(rule, str) else rule if isinstance(rule, list)
                          else rule.get("blend", rule.get("split", [])))}
        problems += [f"rig.weights names unknown bone {b}" for b in sorted(used - set(bones))]
        keyed = {b for action in config["rig"].get("actions", {}).values()
                 for b in [b for _, poses in action["keys"] for b in poses]
                 + [b for wave in action.get("waves", []) for b in wave["bones"]]}
        problems += [f"rig.actions names unknown bone {b}" for b in sorted(keyed - set(bones))]
    for chain, points in config.get("chains", {}).items():
        problems += [f"chain {chain} needs joint {p}.l" for p in points if f"{p}.l" not in config.get("joints", {})]
    frames = json.loads(FRAMES.read_text())["frames"]
    for name, prop in config.get("props", {}).items():
        if name in parts:
            problems.append(f"prop {name} shares a part's name")
        if prop["prefab"] not in PROP_PREFABS:
            problems.append(f"prop {name} names unknown prefab {prop['prefab']}")
        if prop["frame"] not in frames:
            problems.append(f"prop {name} names unknown frame {prop['frame']}")
        problems += [f"prop {name} maps to {s}, missing from the palette" for s in prop["slots"].values()
                     if s not in config["palette"]]
    return problems


def props(config, rig):
    """The `props` of a reconstruction: rigid equipment the Studio builds procedurally (`prefab` with `args`), mounted
    on the hand slot of its verified `frame` on the large rig and skinned to that bone alone, so the template clips
    swing it as KayKit's equipped weapons are swung. `slots` maps each prefab material to a palette slot. The meshes
    come back in the Blender frame of the unfitted template; the build moves each with its slot as the skeleton is
    fitted, keeping its authored size."""
    entries = config.get("props", {})
    if not entries:
        return {}
    if rig != "large":
        raise SystemExit("props mount on the large rig's verified attachment frames")
    from core.rig import Rig, _quat_matrix
    from items import executioner
    frames = json.loads(FRAMES.read_text())["frames"]
    rest = Rig.load("large").rest
    placed = {}
    for name, entry in entries.items():
        piece = getattr(executioner, entry["prefab"])(**entry.get("args", {}))
        unmapped = {str(s) for s in piece.slots} - set(entry["slots"])
        if unmapped:
            raise SystemExit(f"prop {name}: map the prefab materials {sorted(unmapped)} to palette slots")
        frame = frames[entry["frame"]]
        local = np.eye(4)
        local[:3, :3] = _quat_matrix(np.asarray(frame["rotation"], float))
        local[:3, 3] = frame["translation"]
        world = rest[frame["bone"]] @ local
        verts = (piece.verts @ world[:3, :3].T + world[:3, 3]) @ GLTF_TO_BLENDER.T
        placed[name] = {"bone": frame["bone"], "verts": verts.tolist(), "faces": piece.faces.tolist(),
                        "slots": [entry["slots"][str(s)] for s in piece.slots]}
    return placed


def turnaround(src, count=1):
    """Draft sheets from the approved concept and `turnaround.prompt.txt` with the OpenAI image editor."""
    prompt = (src.dir / "turnaround.prompt.txt").read_text()
    provider = ai.get_provider("openai")
    reference = Image.open(src.dir / "reference.png")
    for i in range(count):
        out = src.work / f"turnaround_{len(list(src.work.glob('turnaround_*.png'))) + 1}.png"
        provider.edit_image(prompt, [reference], size="2048x2048").save(out)
        print(f"draft: {out}")


def views(src, image=None):
    if image:
        shutil.copy2(image, src.dir / "turnaround.png")
    folder = src.dir / "turnaround"
    folder.mkdir(exist_ok=True)
    for name, view in split_sheet(Image.open(src.dir / "turnaround.png")).items():
        view.save(folder / f"{name}.png")
    print(f"{src.key}: {folder.relative_to(STUDIO)}")


def reconstruct(src):
    settings = src.json("provenance.json")["reconstruction"]
    if settings["service"] != "Tripo":
        raise SystemExit(f"{src.key}: provenance names {settings['service']}; only Tripo runs from the Studio")
    record = tripo.multiview(src.dir / "turnaround", settings, src.glb)
    (src.work / "reconstruction.task.json").write_text(json.dumps(record, indent=1))


def parts(src):
    if not src.glb.is_file():
        raise SystemExit(f"{src.key}: run `reconstruct tripo` first")
    frames = src.work / "parts"
    shutil.rmtree(frames, ignore_errors=True)
    frames.mkdir()
    blender.run({"reconstruction": str(src.glb), "out": str(frames)}, src.work, JOBS / "part_sheet.py", timeout=1800)
    pieces = sorted({int(p.stem.split("_")[0]) for p in frames.glob("*.png")})
    tile, cols = 160, 4
    sheet = Image.new("RGB", (cols * tile * 3, ((len(pieces) + cols - 1) // cols) * (tile + 14)), "white")
    draw = ImageDraw.Draw(sheet)
    for i, piece in enumerate(pieces):
        x, y = (i % cols) * tile * 3, (i // cols) * (tile + 14)
        for j, view in enumerate(("front", "side", "top")):
            sheet.paste(Image.open(frames / f"{piece}_{view}.png").convert("RGB").resize((tile, tile)), (x + j * tile, y + 14))
        draw.text((x + 4, y), f"piece {piece}", fill=(0, 0, 0))
    out = src.work / "parts.png"
    sheet.save(out)
    print(f"{src.key}: {out}")


def palette_atlas(src, config):
    """The palette atlas of `config` saved to the work folder, and each slot's cell in it. Its layout depends only on
    the palette's slot count and order, so recoloring a palette keeps every face's UVs."""
    names = list(config["palette"])
    atlas = mesh.Atlas([base_color(config["palette"][n]) for n in names], slot_names=names,
                       looks={n: {"ramp": (v["light"], v["shadow"], v["base"])} for n, v in config["palette"].items()
                              if isinstance(v, dict) and "light" in v})
    atlas_png = src.work / "palette.png"
    atlas.image.save(atlas_png)
    return atlas_png, {n: {"u": float(atlas.u_coords[i]), "v_top": float(atlas.v_top[i]), "v_bot": float(atlas.v_bot[i])}
                       for i, n in enumerate(names)}


def repaint(src):
    """Replace the saved source's packed palette with the atlas of `reconstruction.json`'s current colors, without the
    reconstruction: geometry, slots, UVs, weights and clips stay as built. Refused when the slots changed."""
    config = src.json("reconstruction.json")
    if problems := check_config(config):
        raise SystemExit(f"{src.key}: reconstruction.json: " + "; ".join(problems))
    recipe = src.json("character.json")
    atlas_png, _ = palette_atlas(src, config)
    blend = src.dir / recipe["source"]
    blender.run({"blend": str(blend), "image": config["name"].lower() + "_palette", "atlas_png": str(atlas_png)},
                src.work, JOBS / "repaint.py", source=blend, outputs=("blend",))
    print(f"{src.key}: {blend.relative_to(STUDIO)} repainted")


def build(src):
    if not src.glb.is_file():
        raise SystemExit(f"{src.key}: run `reconstruct tripo` first")
    config = src.json("reconstruction.json")
    if problems := check_config(config):
        raise SystemExit(f"{src.key}: reconstruction.json: " + "; ".join(problems))
    recipe = src.json("character.json")
    atlas_png, cells = palette_atlas(src, config)
    job = {"config": config, "reconstruction": str(src.glb), "views": str(src.dir / "turnaround"),
           "atlas_png": str(atlas_png), "atlas": cells, "report": str(src.work / "build.json"),
           "colors": {n: match_color(v) for n, v in config["palette"].items()},
           "template": str(character_workflow.rig_template(recipe["rig"]) or ""),
           "props": props(config, recipe["rig"]),
           "blend": str(src.dir / recipe["source"])}
    result = blender.run(job, src.work, JOBS / "build.py", outputs=("blend",), timeout=1800)
    print(f"{src.key}: {src.dir.relative_to(STUDIO) / recipe['source']}, {sum(result['triangles'].values())} triangles")
    for part, slots in sorted(result["slots"].items()):
        print(f"  {part}: {slots}")


def main(argv):
    ap = argparse.ArgumentParser(prog="studio.py reconstruct", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["turnaround", "views", "tripo", "parts", "build", "palette"])
    ap.add_argument("key", help="family/name, e.g. nymph/nymph")
    ap.add_argument("--count", type=int, default=1, help="turnaround: drafts to request")
    ap.add_argument("--image", help="views: the selected sheet to install as turnaround.png")
    args = ap.parse_args(argv)
    src = Source(args.key)
    {"turnaround": lambda: turnaround(src, args.count), "views": lambda: views(src, args.image),
     "tripo": lambda: reconstruct(src), "parts": lambda: parts(src), "build": lambda: build(src),
     "palette": lambda: repaint(src)}[args.command]()
