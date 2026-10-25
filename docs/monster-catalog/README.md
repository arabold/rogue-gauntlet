# Active monster catalog

The playable roster contains five monsters. Gameplay definitions and policies in Godot Resources are authoritative; this is a reference summary, not a second stat database. See [Monster authoring](../monster-authoring-design.md) for composition and [Level design](../level-design.md#first-realm-encounter-prototype) for encounter budgets and persistence.

| Monster definition | Role | First-realm availability |
| --- | --- | --- |
| [Skeleton Minion](../../scenes/enemies/skeleton/skeleton_minion_definition.tres) | Direct melee | Depths 1–3 |
| [Skeleton Warrior](../../scenes/enemies/skeleton/skeleton_warrior_definition.tres) | Melee with guard, dodge and conditional riposte | Depths 1–3 |
| [Skeleton Crossbowman](../../scenes/enemies/skeleton/skeleton_crossbow_definition.tres) | Projectile-only distance fighter | Depths 1–3 |
| [Plague Rat](../../scenes/enemies/rat/rat_definition.tres) | Bite/retreat, bounded low-health override | Depths 2–3 and finite nests |
| [Forsaken Warden](../../scenes/enemies/forsaken_warden/forsaken_warden_definition.tres) | Sword/guard gatekeeper with conditional riposte | Depth 3 |

Stats, rewards and policy tuning live in the linked definitions and Resources. They await [three-floor balance acceptance](../development-roadmap.md#first-three-floor-acceptance); no automatic depth-based stat scaling or monster-level field is used. Later depths use a development fallback rather than finished realm content.

Use the [Asset Studio character catalog](../../asset-studio/catalogs/characters/README.md) for published monster models and previews, including the [rat](../../asset-studio/catalogs/characters/beasts/rat/lineup.png) and [Warden](../../asset-studio/catalogs/characters/bosses/forsaken_warden/lineup.png). The gameplay roster above is separate from the art catalog: Studio art for future monsters does not automatically add them to encounters.

The three skeleton variants use KayKit models; their linked gameplay definitions select the active scenes. The crossbowman reuses the skeleton rig with its own visible crossbow and animation. Skeleton models are KayKit CC0; Studio sources retain their own recorded provenance in [attributions](../../assets/ATTRIBUTIONS.json).
