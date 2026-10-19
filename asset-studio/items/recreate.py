"""Migrate the original custom-item catalog to explicit, reproducible solid-family recipes.

Legacy art supplies only the target placement envelope. Fresh builders never use its
geometry. IDs and destinations stay stable so existing authored game resources still
reference the same files. Replacement needs an explicit export after visual review.
"""

import numpy as np
from pathlib import Path
from items import align, model
from items.designs import COLORS


def design_for(category, name, archetype):
    """Resolve the known legacy catalog once, then persist the choices in each recipe."""
    design = {}
    if category == "weapons":
        family, design = "weapon", {"kind": archetype}
    elif category in ("shields", "armor"):
        family, design = "shield", {"kind": "kite" if "kite" in name else "heater"}
    elif category == "potions":
        family, design = "potion", {"size": archetype.removeprefix("potion_")}
    elif category == "scrolls":
        family, design = "scroll", {"variant": name.split("_")[-1]}
    elif category == "jewelry":
        family, design = "jewelry", {"kind": archetype, "variant": name.split("_")[-1]}
    elif category == "keys":
        family = "key"
        design = {"variant": "arcane" if "arcane" in name else "bone" if "bone" in name else "royal" if "gold" in name else "plain"}
    elif category == "food":
        kinds = {"apple": "apple", "pear": "pear", "loaf": "bread", "roll": "roll", "cheese": "cheese",
                 "grapes": "grapes", "drumstick": "drumstick", "sausage": "sausage", "steak": "steak",
                 "tbone": "tbone", "beer": "beer", "water": "water", "wine": "wine"}
        matches = [kind for token, kind in kinds.items() if token in name.split("_")]
        if len(matches) != 1:
            raise ValueError(f"No unambiguous food design for {name}")
        family, design = "food", {"kind": matches[0]}
    elif category == "tools":
        kinds = {"lantern": ("lamp", "lantern"), "candle": ("lamp", "candle"), "torch": ("lamp", "torch"),
                 "oil": ("lamp", "oil"), "candelabrum": ("lamp", "candelabrum"), "bell": ("tool", "bell"),
                 "grappling": ("tool", "grappling_hook"), "bugle": ("tool", "horn"), "lockpick": ("tool", "lockpicks"),
                 "mirror": ("tool", "mirror"), "pickaxe": ("tool", "pickaxe")}
        matches = [value for token, value in kinds.items() if token in name.split("_")]
        if len(matches) != 1:
            raise ValueError(f"No unambiguous tool design for {name}")
        family, kind = matches[0]
        design = {"kind": kind}
    else:
        raise ValueError(f"No catalog recreation for {category}/{name}")
    colors = dict(COLORS)
    swatches = {s["cell"]: s["rgb"] for s in align.load_palette_swatches(str(Path(__file__).resolve().parents[1] / "rigs/item_palette.png"))}
    tokens = name.split("_")
    for token in tokens:
        if token in align.SWATCH_NAMES:
            tint = swatches[align.SWATCH_NAMES[token]]
            if category == "potions":
                colors["liquid"] = tint
            elif category == "scrolls":
                colors["gem"] = tint
            elif category in {"jewelry", "keys"} and token not in {"gold", "silver", "wood", "bone"}:
                colors["gem"] = tint
    gem_colors = {"ruby": (173, 43, 60), "emerald": (40, 143, 89), "sapphire": (48, 93, 173),
                  "amethyst": (137, 72, 173), "diamond": (176, 215, 225), "jade": (59, 145, 102),
                  "garnet": (141, 38, 56), "peridot": (134, 169, 55), "topaz": (219, 148, 42),
                  "onyx": (44, 45, 54), "sun": (227, 160, 46), "druid": (87, 129, 66)}
    for token in tokens:
        if token in gem_colors:
            colors["gem"] = gem_colors[token]
    if any(t in tokens for t in ["gold", "gilded", "brass", "bronze"]):
        colors["metal"] = colors["gold"]
    if "wood" in tokens or "wooden" in tokens:
        colors["metal"] = colors["wood"]
    if "bone" in tokens:
        colors["metal"] = colors["bone"]
    if "apple" in tokens:
        colors["food"] = (166, 48, 43)
    elif "pear" in tokens:
        colors["food"] = (187, 159, 52)
    elif "grapes" in tokens:
        colors["food"] = (105, 65, 139)
    elif "cheese" in tokens:
        colors["food"] = (220, 165, 59)
    elif "sausage" in tokens:
        colors["food"] = (132, 62, 44)
    materials = {}
    if any(t in tokens for t in ["gold", "gilded", "brass", "bronze"]):
        materials["metal"] = {"metallic": .5, "roughness": .3, "crease": 45}
    if "wood" in tokens or "wooden" in tokens or "bone" in tokens:
        materials["metal"] = {"metallic": 0, "roughness": .85, "crease": 50}
    return family, design, {key: value for key, value in colors.items() if tuple(value) != tuple(COLORS[key])}, materials


def recreate(item):
    """Replace an adopted recipe, keeping old bounds and its export destination as the handoff contract."""
    from items.workflow import save, digest
    data = item.recipe()
    if data.get("provenance", {}).get("kind") == "solid_catalog_recreation":
        return False
    if data.get("provenance", {}).get("kind") != "existing_custom_item":
        raise ValueError(f"{item.key}: recreate only migrates adopted legacy recipes")
    source = item.resolve(data["source"])
    p, _, _ = model.geometry(source)
    family, design, colors, materials = design_for(item.category, item.id, data.get("archetype"))
    data.pop("source", None)
    data.update(backend="procedural", builder=family, design=design, colors=colors, materials=materials,
                color_mode="character", budget={"triangles": 2400, "materials": 8, "texture_size": 2048},
                placement={"pivot": "center", "dimensions": np.ptp(p, axis=0).tolist(),
                           "offset": ((p.min(axis=0) + p.max(axis=0)) / 2).tolist()},
                provenance={"kind": "solid_catalog_recreation", "previous_model_sha256": digest(source),
                            "notes": "Fresh solid-family geometry; legacy art supplies only its placement envelope."})
    if design.get("kind") in {"steak", "tbone"}:
        data["placement"]["rotation_degrees"] = [90, 0, 0]
    save(item.path, data)
    return True
