# Documentation

## Vision and delivery

| Document | Purpose |
| --- | --- |
| [Game Design Document](game-design.md) | Intended player experience, design principles, and open gameplay decisions; not an implementation inventory |
| [Development Roadmap](development-roadmap.md) | Existing foundations, planned milestones, and acceptance outcomes |
| [Architecture](../ARCHITECTURE.md) | Current system responsibilities, composition, and project conventions |

## Gameplay and feature concepts

These documents define world building, game mechanics, and feature designs that guide current and upcoming implementation.

| Document | Concept coverage |
| --- | --- |
| [Actor actions and world interactions](actor-actions-and-interactions.md) | Proposed coherent player/monster actions: keyboard/gamepad primary/secondary, spell wand/staff and mana rules, four item quick slots, timed interactions and defenses |
| [Dungeon layout, access, and discovery](dungeon-exploration-design.md) | Realm room pools, floor recipes, required locations/routes/objectives, composed doors and locks, visible boss seals, and random generated secrets |
| [Realms and monster design](realms-and-monster-design.md) | Sinking World, provisional realm order, floor cadence and first-realm encounter direction |

## Implemented systems

These documents explain code that exists today. Any proposed extension or future behavior must be marked separately; a description is not a claim that every path has been tested.

| Document | Coverage |
| --- | --- |
| [Monster design and authoring](monster-authoring-design.md) | Six-monster roster, data-driven actions (targets, conditions, weights), health phases, dashes, shared monster spells, validation and tuning |
| [Runs, saving and permadeath](run-lifecycle.md) | Run start, the endless descent, saving on floor arrival and exit, death records and the death screen |
| [User interface](user-interface.md) | Visual language and layout rules, fonts, the generated UI kit, the theme and its variations, the front end on its 3D stage, depth card, HUD, run menu (Gear, Pack, System), death and confirmation |
| [Character classes](character-classes.md) | Authored classes, starting gear, stat profiles, and model integration |
| [Gameplay item catalog](item-catalog.md) | All active definitions, effect families, source budgets, access keys/picks and deferred tools |
| [Randomness and seeds](randomness-and-seeds.md) | Existing run/level seeds, generation RNG, loot RNG, identification, and saved world changes; isolation and current persistence limits |
| [Door access](door-access.md) | Implemented locks, consumable keys/picks, colored door markers, room-owned secret doors, discovery and saved access state |
| [Level design](level-design.md) | Procedural layouts, assembled rooms with room-owned doorways, realm tags, inward-facing corridor styling, sunken water pools and basins, reachability, monster navigation, fog of war, encounters, lighting, atmosphere and surface finishes |
| [Stats and itemization](stats-and-itemization.md) | Actor-neutral buffs/statuses, venom/poison, attributes, modifiers, equipment tiers, loot rolls and enchantment |
| [Shops](shops.md) | Realm-placed keepers, trade tabs in the run menu, merchant profiles, prices, finite stock and saved purchases |
| [Item identification](item-identification-system.md) | Per-run disguises, discovery, jewelry, and scroll targeting |
| [Weapon attacks](weapon-attack-system.md) | Main/off-hand capabilities, paired melee, shared player/monster spell definitions, attack payloads, combos, weapon execution and contact resolution |
| [Combat actions and defense](combat-system.md) | Shared action ownership and execution, dashes, hit feedback, attack indicators, dodge, guard/parry/guard break, poise, hit reactions, threat tiers, attack slots, RNG and persistence |
| [Combat harness and traces](combat-harness.md) | Arena on the game's debug menu, camera and lighting; passive target, hero builds, wall layouts, path-based scenarios, JSONL schema and analyzer |
| [Screen effects](screen-effects.md) | Blindness/poison/near-death post-process overlays, their gameplay wiring through health and status conditions (including confusion and slowness), and the debug harness |
| [Actor and item world effects](world-effects.md) | Reusable burst/sustained VFX, shared cloak/skin ownership, authored looks, committed potion use, known-cursed item auras (in-world, equipped, dropped and in inventory previews), glowing weapons with flicker lights, fountain pool sparkles and the workbench |
| [Movement presentation](movement-feedback.md) | Shared locomotion animation sync and size-aware run, stop and dash cues |
| [Ambient motion](ambient-motion.md) | Generated banner/foliage motion, shared airflow, per-prop response components and exported authoring controls |
| [Actor invisibility](invisibility.md) | Shared player/monster cloak shader, equipment coverage, reversible materials and effect-harness preview |

## Remaining implementation plans

| Document | Remaining scope |
| --- | --- |
| [Balance model](specs/balance-model.md) | Progression, threat-based XP, leveling, armor slots and class parity derived from one model with a simulator; realm 1 monsters, class sheets, XP and floor budgets follow it, the rest of the game's data does not yet |
| [Balance rework](specs/balance-rework-plan.md) | Proposed: role and floor targets, gear pacing from loot, percentage armor, faster monsters, role behaviors and traps in the budget |
| [Room-owned secret entrances](specs/room-owned-secrets-plan.md) | Room-selected concealed doorways, access and discovery |
| [Exact wall placement experiment](specs/exact-wall-placement-plan.md) | Geometry contracts, room boundaries and doorway fit, corridor and interior follow-up |
| [Dungeon progression](specs/level-progression-prototype-plan.md) | Realm definitions, corridor factories, realm tags, and room-owned doorways landed; per-depth room roles/recipes and visible Warden stair seal remain |
| [Shop rooms and future merchants](specs/shop-design.md) | A dedicated shop room with a safe threshold, and later merchants |
| [Realm monster roster](specs/realm-monster-roster-plan.md) | Proposed classic monsters for realms 2–5 (orcs, oozes, gnomes, the unholy, thieves, graspers and a chest mimic), their gatekeepers and nests, and the flight, lob, grasp, theft, gaze, revive and other capabilities they need, in five slices |
| [Realm monster tuning](specs/realm-monster-tuning-plan.md) | Landed: pace, tactics and attack patterns for the giant bat, nymph, chest mimic, acid blob and snake, with a per-monster speed scale and travel facing |
| [Props for the new floor plans](specs/floor-plan-props.md) | Missing mine, cathedral, cloister, prison and chasm/lake props by priority, with mount, size and the rooms they serve |
| [Floor generators and room groups](specs/floor-generators-plan.md) | Proposed `FloorProfile` IoC architecture, composite `RoomGroup` definitions, taxonomy alignment (`RoomPool`, `FloorAccessProfile`, `prop_groups/`), corridor loot/locks, per-profile room count and map size, and the cluster, hallway, prison, cathedral, cloister, mines, labyrinth and boss floor plans |
| [Elemental spells and effects](specs/elemental-spells-plan.md) | All 20 spells authored at 5 levels; targeting, guard rules, visual direction and playtest acceptance remain |
| [Magic missile](specs/magic-missile-plan.md) | Free wand/staff primary as a homing seeker volley for all elements; flight contract and playable missile landed, finished look and acceptance remain |
| [Potion and scroll feedback](specs/consumable-feedback-plan.md) | Proposed unique activation signature for all 20 potion/scroll types, with world and screen channels and catalog coverage checks |
| [Combat redesign](specs/combat-redesign-plan.md) | Warden phases, swings and Tremor slam are implemented; a guard-breaking attack, guard walking and open tuning risks remain |
| [Monster action architecture](specs/monster-action-architecture-plan.md) | Landed; dodge on the dash component, editor badges, phase content and spell cast validation remain |
| [Logging and message feed](specs/logging-plan.md) | Proposed channels, settings, player messages and diagnostic sinks |
| [UI redesign](specs/ui-system-plan.md) | Front end, depth card, desktop HUD, run menu, death and confirmation landed; the touch HUD, credits and settings remain |
| [Shared debug toolkit](specs/debug-toolkit-plan.md) | Implemented; remaining class switching, captured builds and open harness decisions |

Completed plans are consolidated into current-system documents. The [roadmap](development-roadmap.md) owns priority and acceptance. Use the [program-design skill](../.agents/skills/program-design/SKILL.md) for substantial future changes; a plan describes proposed work, not delivered behavior.

## Content and authoring references

- [Level generation folders](../scenes/levels/README.md): generator code, room types and their content.
- [Debug harnesses](../scenes/debug/README.md): the in-game debug menu, and room, combat and effects entry points.
- [Room assembly authoring](../scenes/levels/generators/assembly/README.md): props, groups, styles, steps and room types; harness and editor plugin.

- [Gameplay item catalog](item-catalog.md) and [monster catalog](monster-catalog/README.md): references for authored gameplay content. Use the Asset Studio catalogs below for published models and previews.
- [Asset Studio](../asset-studio/README.md) covers [new character and wearable art](../asset-studio/character-workflow.md), [standalone items](../asset-studio/README.md#standalone-item-workflow), and [existing recipe maintenance](../asset-studio/maintenance/character-and-wearable-recipes.md). See the [architecture and export boundary](../asset-studio/ARCHITECTURE.md) and the [item](../asset-studio/catalogs/items/README.md) and [character](../asset-studio/catalogs/characters/README.md) visual catalogs.

## Organization rules

Keep the current flat feature-document paths to preserve links. Keep implementation plans in `docs/specs/`, separate from these current-system references. Add a current-system document when a feature needs an explanation of its implemented design; new documents are not limited to future work.

The GDD owns intended gameplay, feature documents own current design and behavior, implementation plans own proposed code changes, and the roadmap owns delivery status. Prefer a link over a second copy of the same rules. Clearly label known limitations, proposals, and open decisions. Changes to a plan alone do not change the current-system description. Consolidate completed plans into the owning feature document and remove obsolete plans. Keep algorithms, call stacks and implementation details in code; keep test logs, benchmark runs and debugging history out of living documentation.
