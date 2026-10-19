"""Wearable armor studies on the character rig, rendered by Godot without publishing game assets."""

import argparse
import copy
import json
import os
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from core import export, kit
from prefabs.creatures import characters
from core.paths import work_root
from core import publication

STUDIO = Path(__file__).resolve().parent
OUTPUT = STUDIO / "catalogs/costumes"
CACHE = work_root(STUDIO) / "armor"
SHOTS = ("front", "three_quarter", "side", "back", "walking", "attack", "game_context")
NEUTRAL = {"skin": [104, 112, 122], "skin_dark": [104, 112, 122],
           "skin_light": [104, 112, 122], "nail": [104, 112, 122],
           "pants": [62, 68, 78], "sole": [62, 68, 78]}
REFERENCE_KNIGHT = STUDIO.parent / "assets/kaykit-adventurers/Knight.glb"
MOTION_FRAMES = 24


def studies():
    """List current authored costumes; legacy builtins and draft experiments are excluded."""
    result = {}
    for path in sorted((STUDIO / "costumes").glob("*/costume.json")):
        spec = json.loads(path.read_text())
        if spec.get("status") == "draft" or path.parent.name == "gorget_study":
            continue
        result["costume_" + path.parent.name] = dict(torso=spec["body"]["torso"], body=spec["body"], colors=spec["colors"],
            outfit=spec["outfit"], source=str(path.relative_to(STUDIO)),
            status=spec.get("status","needs_visual_review"),
            review=spec.get("review","Costume study: torso, articulated waist, belt and thigh plates. No limb armor."))
    return result


def recipe(study):
    """Use the production character assembler, layered hosts and coverage culling on a featureless dummy."""
    return dict(template="knight", head={"form": {}}, body={"torso":study["torso"],
        "arms":{"hand":"mitten","scale":1.25}, "legs":{"foot":"bare"}, **copy.deepcopy(study.get("body",{}))},
        outfit=copy.deepcopy(study["outfit"]), colors={**study["colors"], **NEUTRAL})


def lineup_path(key):
    """Review images are disposable; final catalog images require explicit publication."""
    folder = CACHE / "reviews" / key
    folder.mkdir(parents=True, exist_ok=True)
    return folder / "lineup.png"


def source_path(key):
    return STUDIO / studies()[key]["source"]


def published_path(key):
    return OUTPUT / key.removeprefix("costume_") / "lineup.png"


def publish_costume(key):
    """Publish the current reviewed wearable, still and motion to the final catalog."""
    spec = studies()[key]
    folder = lineup_path(key).parent
    files = ("costume.glb", "lineup.png", "lineup.gif")
    publication.publish(folder, published_path(key).parent, key,
        publication.fingerprint(STUDIO, source_path(key)), files, spec)
    catalog()


def build(keys):
    """Stage fully skinned GLBs with the shared character palette, crease normals and animation clips."""
    CACHE.mkdir(parents=True, exist_ok=True)
    records = []
    available = studies()
    for key in keys:
        spec = available[key]
        pieces, colors = characters.build(recipe(spec))
        if not all(np.isfinite(p.verts).all() for p in pieces.values()):
            raise ValueError(f"{key}: non-finite geometry")
        path = CACHE / f"{key}.glb"
        export.export(str(path), key, pieces, colors, template="knight",
                      materials={"mail": dict(metallic=.45, roughness=.5, crease=55),
                                 "scale": dict(metallic=.35, roughness=.5, crease=48)})
        if key.startswith("costume_"):
            # The wearable GLB contains the coordinated outfit and rig, without the fitting dummy.
            wearable = {name: p for name, p in pieces.items()
                        if name not in {"Head", "Body", "ArmLeft", "ArmRight", "LegLeft", "LegRight"}}
            costume_path = lineup_path(key).parent / "costume.glb"
            export.export(str(costume_path), key, wearable, colors, template="knight")
        publication.stage_stamp(lineup_path(key).parent, publication.fingerprint(STUDIO, source_path(key)))
        records.append(dict(key=key, source=spec["source"], torso=spec["torso"],
                            triangles=sum(len(p.faces) for p in pieces.values()),
                            review=spec["review"], status=spec.get("status","needs_visual_review")))
        print(f"{key}: staged {records[-1]['triangles']} triangles")
    return records


def compose(records):
    """Compose temporary review lineups without publishing catalog output."""
    for record in records:
        key = record["key"]
        columns = 4 if key.startswith("costume_") else len(SHOTS)
        rows = (len(SHOTS) + columns - 1) // columns
        strip = Image.new("RGB", (columns * 360, rows * 388 + 10), (20, 23, 31))
        draw = ImageDraw.Draw(strip)
        for i, shot in enumerate(SHOTS):
            x, y = (i % columns) * 360, (i // columns) * 388
            with Image.open(CACHE / key / f"{shot}.png") as frame:
                strip.paste(frame.convert("RGB").resize((360, 360)), (x, y + 28))
            draw.text((x + 10, y + 10), shot.replace("_", " "), fill="white")
        draw.text((10, strip.height - 16), key + " — " + record["torso"] + " dummy", fill="white")
        strip.save(lineup_path(key))


def catalog():
    """Refresh the current source/status index and overview without a new GPU render."""
    OUTPUT.mkdir(parents=True,exist_ok=True)
    specs = studies()
    current = [key for key in specs if (published_path(key).parent / "manifest.json").exists()]
    if not current:
        raise ValueError("No published costumes; use armor publish <costume_key>")
    receipts = {key: publication.read(published_path(key).parent,
        publication.fingerprint(STUDIO, source_path(key))) for key in current}
    sheet = Image.new("RGB", (5 * 280, ((len(current) + 4) // 5) * 310), (20, 23, 31))
    draw = ImageDraw.Draw(sheet)
    for i, key in enumerate(current):
        x, y = (i % 5) * 280, (i // 5) * 310
        with Image.open(published_path(key)) as strip:
            sheet.paste(strip.crop((360, 28, 720, 388)).resize((280, 280)), (x, y))
        draw.text((x + 8, y + 286), key, fill="white")
    sheet.save(OUTPUT / "overview.png")
    updated = {}
    for key in current:
        receipt = receipts[key]
        spec = receipt["metadata"]
        updated[key] = dict(key=key, source=spec["source"], torso=spec["torso"],
            status=spec.get("status", "needs_visual_review") if receipt["source_current"] else "needs_visual_review",
            source_current=receipt["source_current"], inputs=receipt["inputs"], review=spec["review"])
    # One current plate-family sheet makes the approved reference and new silhouettes easy to compare.
    families = {
        "travel_family":(("costume_ranger","Ranger"),("costume_scout","Scout"),
                         ("costume_arcanist","Arcanist"),("costume_adventurer","Adventurer")),
        "plate_family":(("costume_knight_plate","Paladin (approved)"),("costume_knight","Practical knight"),
                        ("costume_maiden","Maiden"),("costume_dark_knight","Dark knight"),
                        ("costume_segmented","Segmented steel")),
        "mail_family":(("costume_chain_mail","Chain mail"),("costume_crusader","Crusader"),("costume_scale","Silver scale")),
        "field_family":(("costume_segmented","Segmented plate"),("costume_brigandine","Brigandine"),
                        ("costume_gambeson","Gambeson")),
    }
    for filename,family in families.items():
        if not all(published_path(key).exists() for key,_ in family):
            continue
        family_sheet = Image.new("RGB",(len(family)*360,776),(20,23,31))
        labels = ImageDraw.Draw(family_sheet)
        for column,(key,title) in enumerate(family):
            with Image.open(published_path(key)) as source:
                for row,(offset,view) in enumerate(((360,"three-quarter"),(720,"side"))):
                    family_sheet.paste(source.crop((offset,28,offset+360,388)),(column*360,row*388+28))
                    labels.text((column*360+10,row*388+10),title+" | "+view,fill="white")
        family_sheet.save(OUTPUT/(filename+".png"))
    (OUTPUT / "status.json").write_text(json.dumps([updated[k] for k in current], indent=2) + "\n")
    lines = ["# Published costumes", "",
        "Final wearable GLBs, Godot lineups and walk/attack animations. Source recipes remain under `costumes/`.", "",
        "Build and review in OS temporary storage: `studio.py armor compare costume_knight`. "
        "After visual review, publish explicitly: `studio.py armor publish costume_knight`. "
        "`armor catalog` recomposes published outputs only. Changed inputs require fresh review; old final artifacts remain visible with `source_current: false`.", "",
        "Only published outputs appear in this index. Legacy studies and one-off comparisons are excluded.", "",
        "[Plate family](plate_family.png) · [Field family](field_family.png) · [Mail family](mail_family.png) · [Travel family](travel_family.png)", "",
        "![Costumes](overview.png)", "", "| Costume | Source | Status | Review focus |", "| --- | --- | --- | --- |"]
    for key in current:
        preview = os.path.relpath(published_path(key), OUTPUT)
        preview = str(Path(preview).parent)
        lines.append(f"| [{key}]({preview}/lineup.png) · [motion]({preview}/lineup.gif) · [GLB]({preview}/costume.glb) | [{specs[key]['source']}]({'../../' + specs[key]['source'].split(':')[0]}) | {updated[key]['status'].replace('_',' ')} | {specs[key]['review']} |")
    (OUTPUT / "README.md").write_text("\n".join(lines) + "\n")


def compose_comparison(key, frames):
    """Pair reference and studio at one shared world scale; retain one still sheet and one motion review."""
    target = lineup_path(key).parent
    sheet = Image.new("RGB", (1440, 4*388), (20,23,31))
    draw = ImageDraw.Draw(sheet)
    for i, shot in enumerate(SHOTS):
        for member, label in enumerate(("Original knight", "Studio costume")):
            x, y = (i % 2)*720 + member*360, (i//2)*388
            suffix = "original" if member == 0 else "studio"
            with Image.open(frames/f"{shot}_{suffix}.png") as source:
                sheet.paste(source.convert("RGB").resize((360,360)), (x,y+28))
            draw.text((x+8,y+10), shot.replace("_"," ")+" | "+label,fill="white")
    sheet.save(target/"comparison.png")
    captures = json.loads((frames/"captures.json").read_text())
    animation, solo_animation, durations = [], [], []
    for capture in captures:
        if not capture["label"].startswith("motion_"):
            continue
        frame = Image.new("RGB", (840,458), (20,23,31))
        labels = ImageDraw.Draw(frame)
        for i, (member,label) in enumerate((("original","Original knight"),("studio","Studio costume"))):
            with Image.open(frames/f"{capture['label']}_{member}.png") as source:
                frame.paste(source.convert("RGB").resize((420,420)),(i*420,30))
            labels.text((i*420+10,10),label+" | "+capture["clip"]+f"  {capture['time']:.2f}s",fill="white")
        animation.append(frame)
        solo = Image.new("RGB", (420,458), (20,23,31))
        solo.paste(frame.crop((420,0,840,458)),(0,0))
        solo_animation.append(solo)
        durations.append(max(20,round(capture["duration"]*1000/MOTION_FRAMES)))
    animation[0].save(target/"comparison.gif",save_all=True,append_images=animation[1:],
                      duration=durations,loop=0,disposal=2,optimize=False)
    solo_animation[0].save(target/"lineup.gif",save_all=True,append_images=solo_animation[1:],
                          duration=durations,loop=0,disposal=2,optimize=False)
    publication.render_stamp(target, publication.fingerprint(STUDIO, source_path(key)),
        ("costume.glb", "lineup.png", "lineup.gif"))
    print(f"Matched comparison: {target/'comparison.png'} and {target/'comparison.gif'}")


def package_costume(key, destination=None):
    """Export a portable art review bundle with the wearable GLB and current animated previews."""
    source = lineup_path(key).parent
    destination = Path(destination).resolve() if destination else source/"export"
    marker = destination/"manifest.json"
    if destination.exists() and any(destination.iterdir()):
        if not marker.exists() or json.loads(marker.read_text()).get("costume") != key:
            raise ValueError(f"Export destination contains unrelated files: {destination}")
    destination.mkdir(parents=True,exist_ok=True)
    files = {"costume.glb":source/"costume.glb", "preview.gif":source/"lineup.gif",
             "lineup.png":source/"lineup.png", "comparison.png":source/"comparison.png",
             "comparison.gif":source/"comparison.gif"}
    for name, path in files.items():
        shutil.copy2(path,destination/name)
    marker.write_text(json.dumps({"costume":key,"kind":"rigged_3d_art", "files":list(files),
        "preview":"Walk and overhead attack, rendered in Godot; GLB contains the wearable without the dummy."},indent=2)+"\n")
    print(f"Exported costume and animated previews: {destination}")
    return destination


def compare(key):
    """Use the actual original model with source materials and identical animation timestamps."""
    from items.workflow import run_godot
    frames = CACHE / (key+"_comparison")
    frames.mkdir(parents=True,exist_ok=True)
    manifest = CACHE / "comparison_jobs.json"
    manifest.write_text(json.dumps([[str(CACHE/f"{key}.glb"),str(frames),str(REFERENCE_KNIGHT)]]))
    result = run_godot(["--script",str(STUDIO/"godot/render_armor_comparison.gd"),"--",str(manifest)],headless=False)
    expected = 2*(len(SHOTS)+2*MOTION_FRAMES)
    if result.count("ARMOR_COMPARISON ") != expected:
        raise ValueError("Godot did not capture the complete matched comparison:\n"+result)
    compose_comparison(key,frames)
    shutil.rmtree(frames)
    manifest.unlink()


def character_review(key, empty_hands=False):
    """Stage the associated dressed character and replace its current Godot still and motion previews."""
    from studio import Character
    from items.workflow import run_godot
    spec = json.loads(source_path(key).read_text())
    if not spec.get("character"):
        raise ValueError("Costume has no associated character recipe")
    character = Character(spec["character"])
    recipe, pieces, colors = character.build()
    hat = [p for name,p in pieces.items() if name.lower().rstrip("0123456789") in ("crown","helmet")]
    cape = [p for name,p in pieces.items() if name.lower().rstrip("0123456789") == "cape"]
    worn = {name:p for name,p in pieces.items() if name.lower().rstrip("0123456789") not in ("crown","helmet","cape")}
    review_root=(work_root(STUDIO)/"characters"/character.key).resolve()
    if not review_root.is_relative_to(work_root(STUDIO)) or review_root.is_relative_to(STUDIO.parent):
        raise ValueError("Character review outputs must stay in external temporary storage")
    review_root.mkdir(parents=True,exist_ok=True)
    output=review_root/"character.glb"
    export.export(str(output),character.title,worn,colors,template=recipe.get("template","skeleton"),
        scale=recipe.get("scale",1),hat=kit.Piece.merge(*hat) if hat else None,
        cape=kit.Piece.merge(*cape) if cape else None,
        weapons={} if empty_hands else character.weapons(recipe),materials=recipe.get("materials"))
    frames = review_root/"frames"
    frames.mkdir(parents=True,exist_ok=True)
    manifest = review_root/"render-job.json"
    manifest.write_text(json.dumps([[str(output),str(frames)]]))
    result = run_godot(["--script",str(STUDIO/"godot/render_costume_character.gd"),"--",str(manifest)],headless=False)
    if result.count("COSTUME_CHARACTER ") != len(SHOTS)+2*MOTION_FRAMES:
        raise ValueError("Godot did not capture complete dressed character review:\n"+result)
    sheet = Image.new("RGB",(1440,786),(20,23,31))
    draw = ImageDraw.Draw(sheet)
    for i,shot in enumerate(SHOTS):
        x,y = (i%4)*360,(i//4)*388
        with Image.open(frames/(shot+".png")) as image:
            sheet.paste(image.convert("RGB").resize((360,360)),(x,y+28))
        draw.text((x+10,y+10),shot.replace("_"," "),fill="white")
    draw.text((10,770),character.key,fill="white")
    sheet.save(review_root/"lineup.png")
    captures = json.loads((frames/"captures.json").read_text())
    images,durations = [],[]
    for capture in captures:
        if not capture["label"].startswith("motion_"):
            continue
        image = Image.new("RGB",(420,458),(20,23,31))
        with Image.open(frames/(capture["label"]+".png")) as frame:
            image.paste(frame.convert("RGB").resize((420,420)),(0,30))
        ImageDraw.Draw(image).text((10,10),character.name+" | "+capture["clip"],fill="white")
        images.append(image)
        durations.append(max(20,round(capture["duration"]*1000/MOTION_FRAMES)))
    images[0].save(review_root/"lineup.gif",save_all=True,append_images=images[1:],
                   duration=durations,loop=0,disposal=2,optimize=False)
    shutil.rmtree(frames)
    manifest.unlink()
    print(f"Dressed character review (external temporary storage): {review_root}")


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("list", "catalog", "build", "lineup", "compare", "export", "character", "publish"))
    parser.add_argument("targets", nargs="*")
    parser.add_argument("--all", action="store_true")
    parser.add_argument("-o", "--out", help="export: output bundle directory (default: temporary review/export)")
    parser.add_argument("--empty-hands", action="store_true", help="character: omit held weapons to expose the costume")
    args = parser.parse_args(argv)
    available = studies()
    if args.command == "catalog":
        catalog()
        return
    if args.command == "list":
        print("\n".join(available))
        return
    keys = list(available) if args.all else args.targets
    if not keys or set(keys) - available.keys():
        parser.error("choose known armor studies or --all; use armor list")
    if args.command in ("compare","export") and (len(keys)!=1 or not keys[0].startswith("costume_")):
        parser.error("compare/export requires one authored costume study")
    if args.command == "character":
        if len(keys)!=1 or not keys[0].startswith("costume_"):
            parser.error("character requires one authored costume study")
        character_review(keys[0], args.empty_hands)
        return
    if args.command == "publish":
        for key in keys:
            publish_costume(key)
        return
    records = build(keys)
    if args.command in ("lineup", "compare", "export"):
        from items.workflow import run_godot
        manifest = CACHE / "jobs.json"
        manifest.write_text(json.dumps([[str(CACHE / f"{k}.glb"), str(CACHE / k)] for k in keys]))
        result = run_godot(["--script", str(STUDIO / "godot/render_armor_fittings.gd"), "--", str(manifest)], headless=False)
        if result.count("ARMOR_SHOT ") != len(keys) * len(SHOTS):
            raise ValueError("Godot did not render every fitting view:\n" + result)
        compose(records)
        for key in keys:
            shutil.rmtree(CACHE / key)
        manifest.unlink()
        print(f"Temporary armor reviews: {CACHE / 'reviews'}")
    if args.command in ("compare","export"):
        compare(keys[0])
    if args.command == "export":
        package_costume(keys[0], args.out)
