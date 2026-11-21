# Existing character and wearable recipes

These commands maintain existing recipe-built assets. Use the [Blender workflow](../character-workflow.md) for new characters, helmets, and body armor. Use the [item workflow](../README.md#standalone-item-workflow) for food, potions, weapons, shields, and props.

## Character recipes and creature rigs

Use the named recipe for each operation. See the [Orc Warrior archive](orc-warrior-kit/README.md) for the former kit recipe.

```bash
./.venv/bin/python asset-studio/studio.py list
./.venv/bin/python asset-studio/studio.py lineup beasts/wolf beasts/rat golem/stone_golem
./.venv/bin/python asset-studio/studio.py export beasts/wolf beasts/rat golem/stone_golem
./.venv/bin/python asset-studio/studio.py audit
```

`lineup` stages reviews in temporary storage. The bound-source and rigid creature `export` handlers verify the Godot import, preserve the UID, and write art metadata and current catalog outputs. `--no-import` defers Godot verification. The [architecture](../ARCHITECTURE.md) describes the builders and [creature rig docs](../rigs/creatures/README.md) cover technical rig inputs.

- The [wolf](../characters/beasts/wolf/README.md) and [rat](../characters/beasts/rat/README.md) export from already-bound sources. The [spider](../characters/beasts/spider/README.md) is a derivative review candidate on the same workflow.
- The [stone golem](../characters/golem/stone_golem/README.md) binds rigid pieces and bakes shoulder helpers. The [Forsaken Warden](../characters/bosses/forsaken_warden/README.md) uses the same large-rig family with separate named gear.
- Existing kit character recipes can select whole outfits with `{"kind":"costume_ref","name":"knight"}`. Character colors take precedence over costume colors. Low-level `kind: costume` and `costume_set` remain supported by the older recipes. Explicit recipe export paths preserve game asset locations.

## Class heroes

The shipped recipes in `characters/heroes/` extend `hero_base` and place a shared torso, tunic, arms and legs under their class head. Their exported hair, ears and scalp are named for equipment visibility. The two shoulder-cap bones in [`hero_hinges.json`](../rigs/hero_hinges.json) carry baked motion for the existing plate suits. Use a named `export heroes/<class>` for existing art maintenance. [Character class integration](../../docs/character-classes.md#the-model-swap) describes the game-side contract.

## Wearables

The existing [costume manual](../costumes/README.md) and [published costume catalog](../catalogs/costumes/README.md) document legacy construction, fitting and publication. [Paladin plate](../costumes/knight_plate/README.md) remains a reference for those recipes. Their fitted torso layers, replacement limbs and static torso display model reflect the existing equipment implementation.

```bash
./.venv/bin/python asset-studio/studio.py armor compare costume_knight_plate
./.venv/bin/python asset-studio/studio.py armor export costume_knight_plate
./.venv/bin/python asset-studio/studio.py armor publish costume_knight_plate
```

Use `armor compare` to generate a temporary comparison and motion review and `armor export` to stage an art review bundle. `armor publish` replaces the selected catalog entry. `armor suit` reviews an existing costume on a class hero and `armor equip` writes its current wearable and display GLBs to the game.

For existing scripted helmets such as `headgear/shaman_antler_headdress` and `headgear/dark_knight_winged_helm`, `item model` regenerates their checked-in `source.glb` and provenance from `source.py` in a fresh Blender session. `item compare` scores their existing three-view concept and `item lineup --mount head` reviews the result on a host. They remain fitting studies unless a game item explicitly equips them.
