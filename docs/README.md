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
| [Actor actions and world interactions](actor-actions-and-interactions.md) | Proposed coherent player/monster actions: keyboard/gamepad primary/secondary, elemental staff/learned-spell/mana rules, four item quick slots, timed interactions and defenses |
| [Dungeon layout, access, and discovery](dungeon-exploration-design.md) | Realm room pools, floor recipes, required locations/routes/objectives, composed doors and locks, visible boss seals, and random generated secrets |
| [Realms and monster design](realms-and-monster-design.md) | Sinking World, provisional realm order, floor cadence and first-realm encounter direction |

## Implemented systems

These documents explain code that exists today. Any proposed extension or future behavior must be marked separately; a description is not a claim that every path has been tested.

| Document | Coverage |
| --- | --- |
| [Monster design and authoring](monster-authoring-design.md) | Implemented five-monster roster, definitions and tactical policies; editable melee/projectile assemblies and editor validation |
| [Character classes](character-classes.md) | Authored classes, starting gear, stat profiles, and model integration |
| [Gameplay item catalog](item-catalog.md) | All active definitions, effect families, source budgets, access keys/picks and deferred tools |
| [Randomness and seeds](randomness-and-seeds.md) | Existing run/level seeds, generation RNG, loot RNG, identification, and saved world changes; isolation and current persistence limits |
| [Door access](door-access.md) | Implemented locks, consumable keys/picks, colored door markers, secret pockets, discovery and saved access state |
| [Level design](level-design.md) | Procedural layouts, reusable arrangements, room authoring, fog of war, encounters and light flicker |
| [Stats and itemization](stats-and-itemization.md) | Attributes, modifiers, equipment tiers, loot rolls, and enchantment |
| [Item identification](item-identification-system.md) | Per-run disguises, discovery, jewelry, and scroll targeting |
| [Weapon attacks](weapon-attack-system.md) | Existing weapon and attack behavior |
| [Combat actions and defense](combat-system.md) | Shared action ownership, dodge (including passing through monsters), guard/parry/guard break, poise and hit reactions, threat tiers, rear and sneak hits, attack slots, RNG and persistence |
| [Combat harness and traces](combat-harness.md) | Interactive/scripted combat, JSONL event schema, capture flags and analyzer |
| [Screen effects](screen-effects.md) | Blindness/poison/near-death post-process overlays, their gameplay wiring through health and status conditions (including confusion and slowness), and the debug harness |
| [Actor and item world effects](world-effects.md) | Reusable burst/sustained VFX, shared cloak/skin ownership, authored looks, committed potion use, known-cursed item auras and the workbench |
| [Actor invisibility](invisibility.md) | Shared player/monster cloak shader, equipment coverage, reversible materials and effect-harness preview |

## Remaining implementation plans

| Document | Remaining scope |
| --- | --- |
| [Secret doors and locks](specs/secret-doors-and-locks-plan.md) | Remaining tuning, hidden mechanisms, consumable key chains and secret shortcuts |
| [Dungeon progression](specs/level-progression-prototype-plan.md) | Realm pools/recipes and the visible Warden stair seal |
| [Actions and interactions](specs/actor-actions-and-interactions-plan.md) | Learned spells/mana and further actor intentions/content |
| [Elemental spells and effects](specs/elemental-spells-plan.md) | Proposed 20-spell roster, targeting, visual direction and delivery slices |
| [Potion and scroll feedback](specs/consumable-feedback-plan.md) | Proposed unique activation signature for all 20 potion/scroll types, with world and screen channels and catalog coverage checks |
| [Actor and item VFX](specs/actor-item-vfx-plan.md) | Remaining visual-quality/skin-choice acceptance, GPU timing and in-game readability review |
| [Combat redesign](specs/combat-redesign-plan.md) | Remaining combat work: a Warden guard-breaking attack and guard walking, plus open tuning risks |
| [Attack status effects](specs/attack-status-effects-plan.md) | Proposed on-hit status payloads, actor-agnostic buffs for monsters and a monster status tint; first slice lets venomous monster attacks poison the player |
| [Actor status integration](specs/actor-status-integration-plan.md) | Shared condition ownership and frozen-state integration; later attack and spell delivery |
| [Logging and message feed](specs/logging-plan.md) | Proposed channels, settings, player messages and diagnostic sinks |
| [Item quality, affixes and curses](specs/item-quality-affixes-and-curses-plan.md) | Quality, affixes, per-item identification and curses landed; remaining richer source ladders, generated Rare names, Unique items and new affix families |

Completed plans are consolidated into current-system documents. The [roadmap](development-roadmap.md) owns priority and acceptance. Use the [program-design skill](../.agents/skills/program-design/SKILL.md) for substantial future changes; a plan describes proposed work, not delivered behavior.

## Content and authoring references

- [Level generation folders](../scenes/levels/README.md): generator code, room presets and arrangement assets.
- [Debug harnesses](../scenes/debug/README.md): room, combat, item and effects entry points.
- [Arrangement authoring](../scenes/levels/arrangements/README.md): editable furnishings, Inspector settings, room-generator commands and room/floor harness.

- [Gameplay item catalog](item-catalog.md) and [monster catalog](monster-catalog/README.md): references for authored gameplay content. Use the Asset Studio catalogs below for published models and previews.
- [Asset Studio](../asset-studio/README.md), its [architecture and export boundary](../asset-studio/ARCHITECTURE.md), [item visual catalog](../asset-studio/catalogs/items/README.md), and [character visual catalog](../asset-studio/catalogs/characters/README.md): visual authoring and published models. A published model does not imply completed gameplay behavior.

## Organization rules

Keep the current flat feature-document paths to preserve links. Keep implementation plans in `docs/specs/`, separate from these current-system references. Add a current-system document when a feature needs an explanation of its implemented design; new documents are not limited to future work.

The GDD owns intended gameplay, feature documents own current design and behavior, implementation plans own proposed code changes, and the roadmap owns delivery status. Prefer a link over a second copy of the same rules. Clearly label known limitations, proposals, and open decisions. Changes to a plan alone do not change the current-system description. Consolidate completed plans into the owning feature document and remove obsolete plans. Keep algorithms, call stacks and implementation details in code; keep test logs, benchmark runs and debugging history out of living documentation.
