# Active monster catalog

The tables list every monster the dungeon spawns, realm by realm. Gameplay definitions and policies in Godot Resources are authoritative; this is a reference summary, not a second stat database. See [Monster authoring](../monster-authoring-design.md) for composition, [Level design](../level-design.md#first-realm-encounter-prototype) for encounter budgets and persistence, and [Realm content](../realm-content.md#realm-by-realm) for how each realm's cast grows floor by floor.

Each realm's encounter table (`scenes/levels/encounters/*_encounters.tres`, starting from `sunken_garrison.tres`) prices its packs against the floor's budget. The realm's second floor turns its first two encounters into destructible nests, and its last floor holds its gatekeeper, if it has one.

## Garrison, depths 1–5

| Monster definition | Role | Availability |
| --- | --- | --- |
| [Skeleton Minion](../../scenes/enemies/skeleton/skeleton_minion_definition.tres) | Direct melee | Depths 1–5 |
| [Skeleton Warrior](../../scenes/enemies/skeleton/skeleton_warrior_definition.tres) | Melee with guard, dodge and conditional riposte | Depths 1–5 |
| [Skeleton Crossbowman](../../scenes/enemies/skeleton/skeleton_crossbow_definition.tres) | Projectile-only distance fighter | Depths 2–5 |
| [Skeleton Mage](../../scenes/enemies/skeleton/skeleton_mage_definition.tres) | Missile volleys, fireballs, ally healing, self healing and empowerment | Depths 3–5 |
| [Plague Rat](../../scenes/enemies/rat/rat_definition.tres) | Venomous bite and rush, then retreat; bounded low-health override | Depths 1–5 and bone nests |
| [Forsaken Warden](../../scenes/enemies/forsaken_warden/forsaken_warden_definition.tres) | Gatekeeper: sword and guard with conditional riposte | Depth 5 |

## Prison, depths 6–10

| Monster definition | Role | Availability |
| --- | --- | --- |
| [Giant Bat](../../scenes/enemies/bat/giant_bat_definition.tres) | Swarmer that flies on the air grid over water, pits and low solids; bites and swoops, darting erratically so it never holds still | Depths 6–10 and bat roosts |
| [Nymph](../../scenes/enemies/nymph/nymph_definition.tres) | Thief: jabs and kicks; her first damaging hit steals a pack item, then she runs and blinks away and vanishes with it unless killed first | Depths 6–10 |
| [Orc Warrior](../../scenes/enemies/orc/orc_warrior_definition.tres) | Armored melee grunt: chop, diagonal and sweep swings | Depths 6–10 |
| [Orc Spearman](../../scenes/enemies/orc/orc_spearman_definition.tres) | Shooter: keeps his distance throwing javelins, stabs up close | Depths 6–10 |
| [Orc Shaman](../../scenes/enemies/orc/orc_shaman_definition.tres) | Caster: earth missile volleys, heals allies and himself, hastes nearby orcs | Depths 6–10 |
| [Orc Captain](../../scenes/enemies/orc/orc_captain_definition.tres) | Brute: morning-star chops and sweeps, shield guard, bash and riposte | Depths 6–10 |
| [The Minotaur](../../scenes/enemies/minotaur/minotaur_definition.tres) | Gatekeeper: punches, a guard-breaking smash and a lane-warned charge that staggers it against walls; faster charges below half health | Depth 10 |

## Sewer, depths 11–15

| Monster definition | Role | Availability |
| --- | --- | --- |
| [Sewer Rat](../../scenes/enemies/rat/sewer_rat_definition.tres) | The plague rat's scene with tougher stats, in larger packs | Depths 11–15 |
| [Acid Blob](../../scenes/enemies/slime/acid_blob_definition.tres) | Slow slam; burns whoever hits it in melee with acid and leaves a slime trail | Depths 11–15 and slime vats |
| [Spitting Slime](../../scenes/enemies/slime/spitting_slime_definition.tres) | Shooter on the acid blob's scene: keeps its distance and lobs globs that leave a poison pool; slams up close | Depths 11–15 |
| [Snake](../../scenes/enemies/snake/snake_definition.tres) | Coiled, concealed ambusher that wakes within 3.5 m; venomous bite and lunge | Depths 11–20 |
| [The Gelatinous Cube](../../scenes/enemies/gelatinous_cube/gelatinous_cube_definition.tres) | Gatekeeper: slow; lobs goo over cover that holds the hero in place while poison digests them, engulfs up close and drags a slowing slime trail; lobs more often below half health | Depth 15 |

## Mines, depths 16–20

The Mines have no gatekeeper yet. Giant bats and snakes stand in for the rest of the [planned cast](../specs/realm-monster-roster-plan.md), and bat roosts are the realm's nests.

| Monster definition | Role | Availability |
| --- | --- | --- |
| [Stone Golem](../../scenes/enemies/golem/stone_golem_definition.tres) | Brute twice the hero's height that guards its room: punches and a guard-breaking smash; revives once from rubble | Depths 16–20 |

## In any realm from the Prison on

| Monster definition | Role | Availability |
| --- | --- | --- |
| [Mimic](../../scenes/enemies/mimic/chest_mimic_definition.tres) | Waits disguised as a real chest; wakes when opened, lingered beside or hit, then bites and lashes | Each placed chest at depths 6–20 has a 15% chance |

Stats, rewards and policy tuning live in the linked definitions and Resources. They await [first-realm balance acceptance](../development-roadmap.md#first-three-floor-acceptance); no automatic depth-based stat scaling or monster-level field is used. The Cathedral (depths 21–25) has no cast yet: until its part of the [realm monster roster](../specs/realm-monster-roster-plan.md) lands, it falls back to a development factory that spawns single Garrison skeletons and rats without budgets, nests, a gatekeeper or mimics.

Use the [Asset Studio character catalog](../../asset-studio/catalogs/characters/README.md) for published monster models and previews, including the [rat](../../asset-studio/catalogs/characters/beasts/rat/lineup.png) and [Warden](../../asset-studio/catalogs/characters/bosses/forsaken_warden/lineup.png). The gameplay roster above is separate from the art catalog: Studio art for future monsters does not automatically add them to encounters.

The four skeleton variants use KayKit models; their linked gameplay definitions select the active scenes. The crossbowman reuses the skeleton rig with its own visible crossbow and animation. Skeleton models are KayKit CC0; Studio sources retain their own recorded provenance in [attributions](../../assets/ATTRIBUTIONS.json).
