---
name: assets
description: Use for Rogue Gauntlet 3D art authoring, fitting, preview and export. Routes new characters, helmets and body armor to the concept-to-Blender workflow and standalone items and props to Asset Studio recipes.
---

# Asset Studio

Read the [Studio guide](../../../asset-studio/README.md) and select the workflow for the asset.

- **New enemies, helmets, body armor, and future heroes:** Follow the [character workflow](../../../asset-studio/character-workflow.md). The [approved Orc Warrior](../../../asset-studio/characters/orc/orc_warrior/README.md) shows the source layout. Get the concept approved, create a consistent four-view turnaround, reconstruct untextured parts, and finish topology, palette UVs and rigging in Blender. Use the regular Godot combat harness camera and lighting for acceptance.
- **Food, potions, weapons, shields, tools, and props:** Follow the [standalone item workflow](../../../asset-studio/README.md#standalone-item-workflow). Use the existing recipe, validation, lineup, publication and export commands. See the [weapon guide](../../../asset-studio/objects/weapons/README.md) and [room furnishing workflow](../../../asset-studio/README.md#room-furnishings) when applicable.
- **Existing kit characters, creature rigs, and costumes:** Use the [maintenance guide](../../../asset-studio/maintenance/character-and-wearable-recipes.md) and target named recipes. The earlier Orc Warrior kit recipe is archived. The active Orc recipe exports its saved Blender source.

Run Studio commands with `./.venv/bin/python`. Keep selected references, exact prompts, and the editable source in the authoring folder. Stage reconstructions, candidate GLBs, shots, and review artifacts in the OS temporary work root. Publish a selected result to the catalog explicitly. Game exports are a separate operation. See the [folder policy](../../../asset-studio/README.md#current-catalogs-and-folder-policy).

For new worn body armor, model a full replacement body on the compatible hero skeleton and provide a static torso-only inventory model. Fit helmets to the actual supported heads. The hero equips gear in Godot. Enemy armor and fixed weapons are baked into the enemy model. Review the worn or held item and each relevant gameplay clip after import.

Use [Blender job tooling](../../../asset-studio/README.md#scripted-item-models) for existing scripted item sources.

Room props follow the [room furnishing workflow](../../../asset-studio/README.md#room-furnishings):
- **Proportions:** exaggerate them the pack's way, never realistically. Measure against the hero, the pack's barrel, its chest and the 4 m wall.
- **Concept first:** `item reference --provider gemini` draws a world recipe against the pack render. Keep the selected concept and its prompt beside the recipe.
- **Colors:** use only the `PACK` swatches, never a per-prop shade.
- **Review:** render the model side by side with related pack props in [compare models](scripts/compare_models.gd) before export:

  ```bash
  /Applications/Godot_mono.app/Contents/MacOS/Godot --path "$PWD" --script .agents/skills/assets/scripts/compare_models.gd -- <out.png> 30 res://assets/custom-heroes/knight.glb res://assets/kaykit-dungeon-remastered/barrel_large.gltf <your model.glb>
  ```

  Absolute `.glb` paths load a staged build directly.
- **Pack render:** regenerate `asset-studio/prompts/world-props-reference.png` with the same script. Use the knight, `barrel_large`, `chest`, `bed_decorated`, `table_long`, `wall` and `crates_stacked` at yaw 30, cropped to the floor band.

Keep gameplay properties in the Godot scenes and resources. Use project-native names in code and asset identifiers. An art reference or concept prompt can name KayKit for style matching.
