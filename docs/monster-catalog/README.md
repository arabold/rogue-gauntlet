# Active monster catalog

The playable roster contains five monsters. Gameplay definitions and policies in Godot Resources are authoritative; this is a reference summary, not a second stat database. See [Monster authoring](../monster-authoring-design.md) for composition and [Level design](../level-design.md#first-realm-encounter-prototype) for encounter budgets and persistence.

| Monster | HP | XP | Movement / attack | Defense | First-realm eligibility |
| --- | ---: | ---: | --- | --- | --- |
| [Skeleton Minion](../../scenes/enemies/skeleton/skeleton_minion_definition.tres) | 8 | 2 | Close / melee | None | Depths 1–3 |
| [Skeleton Warrior](../../scenes/enemies/skeleton/skeleton_warrior_definition.tres) | 10 | 30 | Close / melee | Guard, dodge, conditional riposte | Depths 1–3 |
| [Skeleton Crossbowman](../../scenes/enemies/skeleton/skeleton_crossbow_definition.tres) | 8 | 4 | Maintain distance / projectile only | None | Depths 1–3 |
| [Plague Rat](../../scenes/enemies/rat/rat_definition.tres) | 6 | 3 | Bite then retreat / low-health override | None | Depths 2–3 and finite nest slots |
| [Forsaken Warden](../../scenes/enemies/forsaken_warden/forsaken_warden_definition.tres) | 110 | 150 | Close / sword sweep | Guard, conditional sword riposte | Depth 3 gatekeeper |

These are starting values, awaiting first-three-floor balance. In particular, the warrior retains its existing 30 XP and elite loot while the other ordinary monsters use common loot; reward consistency is a playtest/tuning item. No automatic depth-based stat scaling or monster-level field is used. Later depths currently use the four ordinary monsters as a development fallback.

![Skeleton Minion](skeleton-minion.png)
![Skeleton Warrior](skeleton-warrior.png)

The crossbowman reuses the retained KayKit skeleton rig with its own visible crossbow and animation. Approved Studio lineups show the [rat](../../asset-studio/catalogs/characters/beasts/rat/lineup.png) and [Warden](../../asset-studio/catalogs/characters/bosses/forsaken_warden/lineup.png). Skeleton models are KayKit CC0; Studio sources retain their own recorded provenance in [attributions](../../assets/ATTRIBUTIONS.json).

The retired direct Quaternius gameplay roster and its images are removed. Studio art for future monsters remains available without automatically adding those monsters to encounters.
