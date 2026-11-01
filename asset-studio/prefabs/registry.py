"""Structured prefab registry and introspectable schema for Asset Studio.

Exposes all modular prefabs organized cleanly into 4 domains:
1. Anatomy: heads, faces, bodies, limbs, extremities, appendages (wings, tails, ribs, horns)
2. Outfits: helmets, cuirass, pauldrons, belts, garments
3. Items: weapons, shields, held props / consumables
4. Details: fasteners, decorative trophies, surface wear

Provides a machine-readable schema suitable for CLI inspection (`studio.py schema`)
and visual character/monster/item creator tools in Godot.
"""

import inspect
from prefabs import library
from prefabs.anatomy import appendages, bodies, extremities, faces, heads, limbs
from prefabs.creatures import characters, golems
from prefabs.details import fasteners, trophies, wear, construction, heraldry, primal, fur, hilts
from prefabs.items import props, shields, weapons
from prefabs.outfits import armor, belts, capes, clothes, costumes, crowns, cuirass, helmets, pauldrons


def _sig_params(fn):
    """Return a JSON-serializable dict of parameter defaults for a callable."""
    try:
        sig = inspect.signature(fn)
    except (TypeError, ValueError):
        return {}
    out = {}
    for name, p in list(sig.parameters.items()):
        if name in ("self", "head", "host", "torso", "limb", "h", "piece", "k", "kwargs"):
            continue
        if p.default is not inspect.Parameter.empty:
            val = p.default
            if isinstance(val, (tuple, set)):
                val = list(val)
            elif not isinstance(val, (int, float, str, bool, list, dict, type(None))):
                val = str(val)
            out[name] = val
        else:
            out[name] = "<required>"
    return out


def get_schema():
    """Return the complete modular prefab catalog and parameter schema as a JSON-serializable dict."""
    details_schema = {
        "fur": {name: _sig_params(fn) for name, fn in fur.FUR.items()},
        "primal": {name: _sig_params(fn) for name, fn in primal.PRIMAL.items()},
        "heraldry": {name: _sig_params(fn) for name, fn in heraldry.HERALDRY.items()},
        "construction": {name: _sig_params(fn) for name, fn in construction.CONSTRUCTION.items()},
        "fasteners": {name: _sig_params(fn) for name, fn in fasteners.FASTENERS.items()},
        "trophies": {name: _sig_params(fn) for name, fn in trophies.TROPHIES.items()},
        "wear": {name: _sig_params(fn) for name, fn in wear.WEAR.items()},
        "hilts": {name: _sig_params(fn) for name, fn in hilts.HILTS.items()},
    }

    return {
        "anatomy": {
            "heads": {
                "forms": list(heads.FORMS.keys()),
                "presets": list(heads.ROSTER.keys()),
                "parameters": _sig_params(heads.Head.__init__),
            },
            "faces": {
                "features": {name: _sig_params(fn) for name, fn in faces.FEATURES.items()},
                "skin_features": list(faces.SKIN_FEATURES),
                "warpaint": list(faces.WARPAINT.keys()),
                "eye_styles": ["beads", "female", "socket", "slit", "glow", "band", "cyclops"],
                "nose_styles": ["wedge", "boxer", "lump", "hooked", "bulb", "button", "snout", "slits"],
                "ear_styles": ["round", "pointed", "goblin", "torn"],
                "mouth_styles": ["grim", "grin", "open", "snarl", "stitched", "fangs"],
                "beard_styles": ["full", "long", "forked", "goatee", "braided", "stubble"],
                "hair_styles": ["short", "long", "mohawk", "ponytail", "tonsure", "wild"],
            },
            "bodies": {
                "torsos": list(bodies.TORSOS.keys()),
                "parameters": _sig_params(bodies.Trunk.__init__),
            },
            "limbs": {
                "arms": list(limbs.ARMS.keys()),
                "legs": list(limbs.LEGS.keys()),
                "parameters": _sig_params(limbs.Limb.__init__),
            },
            "extremities": {
                "hands": {
                    "styles": list(extremities.HAND_STYLES),
                    "parameters": _sig_params(extremities.hand),
                },
                "feet": {
                    "styles": list(extremities.FOOT_STYLES),
                    "parameters": _sig_params(extremities.foot),
                },
            },
            "appendages": {
                "styles": list(appendages.APPENDAGES.keys()),
                "wings": _sig_params(appendages.wings),
                "tail": _sig_params(appendages.tail),
                "ribs": _sig_params(appendages.ribs),
                "horn": _sig_params(appendages.horn),
            },
        },
        "outfits": {
            "costume_ref": {"name": "costumes/<name>/costume.json; complete outfit and palette; character overrides win"},
            "costumes": list(costumes.VARIANTS),
            "costume_parameters": {name: _sig_params(builder) for name, builder in costumes.VARIANTS.items()},
            "helmets": list(helmets.STYLES.keys()),
            "cuirass": {"plate": _sig_params(cuirass.plate), "ridge": _sig_params(cuirass.ridge)},
            "pauldrons": _sig_params(pauldrons.build),
            "cape": _sig_params(capes.build),
            "fitted_belt": _sig_params(belts.build_fitted),
            "belts": list(belts.VARIANTS.keys()),
            "crowns": _sig_params(crowns.build),
            "garments": {name: _sig_params(fn) for name, fn in characters.GARMENTS.items()},
            "armor_elements": list(armor.ELEMENTS.keys()),
        },
        "items": {
            "weapons": list(weapons.WEAPONS.keys()),
            "shields": list(shields.SHIELDS.keys()),
            "props": list(props.PROPS.keys()),
        },
        "details": details_schema,
        "trinkets": details_schema,  # backward compatibility alias
        "creatures": {
            "characters": characters.keys(),
            "golems": ["stone_golem"],
        },
        "library": library.list_prefabs(),
    }
