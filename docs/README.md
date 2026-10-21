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
| [Dungeon layout, access, and discovery](dungeon-exploration-design.md) | Realm room pools, floor recipes, required locations/routes/objectives, composed doors and locks, visible boss seals, and random generated secrets |
| [Realms and monster design](realms-and-monster-design.md) | The 24-depth Sinking World, eight Sunken Epochs, 3-depth realm cadence, prototype bestiary, and Gatekeeper encounters |
| [Character classes](character-classes.md) | Authored classes, starting gear, stat profiles, and model integration |
| [Gameplay item catalog](item-catalog.md) | All active definitions, effect families, source budgets, and deferred tools/keys |

## Implemented systems

These documents explain code that exists today. Any proposed extension or future behavior must be marked separately; a description is not a claim that every path has been tested.

| Document | Coverage |
| --- | --- |
| [Randomness and seeds](randomness-and-seeds.md) | Existing run/level seeds, generation RNG, loot RNG, identification, and saved world changes; isolation and current persistence limits |
| [Level design](level-design.md) | Procedural layouts, room authoring, fog of war, and the first-realm encounter prototype |
| [Stats and itemization](stats-and-itemization.md) | Attributes, modifiers, equipment tiers, loot rolls, and enchantment |
| [Item identification](item-identification-system.md) | Per-run disguises, discovery, jewelry, and scroll targeting |
| [Weapon attacks](weapon-attack-system.md) | Existing weapon and attack behavior |

## Program designs

| Document | Status and scope |
| --- | --- |
| [Attack animation styles](specs/attack-animation-plan.md) | Implemented weapon styles, actor-specific animation Resources and shared timing |
| [Reliable melee contact](specs/melee-contact-plan.md) | Implemented independent contact volumes, optional swooshes and combat playground diagnostics |
| [Item catalog completion](specs/item-catalog-plan.md) | Definition/effect audit, grouped loot, identity coverage, and validation |
| [Item lifecycle program design](specs/item-lifecycle-plan.md) | Implemented milestone 1: Resource creation policies, runtime items, stacks, food, floor deltas, and a playable item playground |
| [Dungeon progression implementation plan](specs/level-progression-prototype-plan.md) | Remaining Garrison work: realm pools/recipe routes, composed access and persistence, optional locks, then generated secrets; existing encounter systems are reused |

Use the repository's [program-design skill](../.agents/skills/program-design/SKILL.md) for substantial implementation planning. Plans link to current-system documentation instead of redescribing unchanged functionality. As work lands, update the system document and mark verified plan/roadmap outcomes; keep remaining proposals explicit.

## Content and authoring references

- [Gameplay item catalog](item-catalog/README.md) and [monster catalog](monster-catalog/README.md): references for authored gameplay content; catalog counts and images may lag recent asset work.
- [Asset Studio](../asset-studio/README.md), its [architecture and export boundary](../asset-studio/ARCHITECTURE.md), and its [item visual catalog](../asset-studio/catalogs/items/README.md): visual authoring and published models. A published model does not imply completed gameplay behavior.

## Organization rules

Keep the current flat feature-document paths to preserve links. Keep implementation plans in `docs/specs/`, separate from these current-system references. Add a current-system document when a feature needs an explanation of its implemented design; new documents are not limited to future work.

The GDD owns intended gameplay, feature documents own current design and behavior, implementation plans own proposed code changes, and the roadmap owns delivery status. Prefer a link over a second copy of the same rules. Clearly label known limitations, proposals, and open decisions. Changes to a plan alone do not change the current-system description.
