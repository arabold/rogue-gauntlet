# Documentation

## Vision and delivery

| Document | Purpose |
| --- | --- |
| [Game Design Document](game-design.md) | Intended player experience, design principles, and open gameplay decisions; not an implementation inventory |
| [Development Roadmap](development-roadmap.md) | Existing foundations, acceptance outcomes and open work, linked to GitHub issues |
| [Architecture](../ARCHITECTURE.md) | Current system responsibilities, composition, and project conventions |

## Gameplay concepts

These documents define world building and the intended shape of features; each marks what is implemented.

| Document | Concept coverage |
| --- | --- |
| [Dungeon layout, access, and discovery](dungeon-exploration-design.md) | Realm room pools, floor obligations, composed doors and locks, gatekeeper seals and room-owned secrets |
| [Realms and monster design](realms-and-monster-design.md) | The Sinking World premise, the epoch theme pool, the five-floor realm cadence, realm casts and encounter direction |

## Implemented systems

These documents explain code that exists today. Proposed extensions are marked separately; a description is not a claim that every path has been tested.

### World and generation

| Document | Coverage |
| --- | --- |
| [Level design](level-design.md) | Realms, floor plans and room groups, room assembly with room-owned doorways and theme tags, encounters (packs, budgets, nests, gatekeepers), fountains, trap gauntlets, triggers and plate traps, loot, corridor styling, lighting and atmosphere, clearance-grid navigation, fog of war and reachability |
| [Realm content](realm-content.md) | Each realm's arc across its five floors: room windows, headline candidates, motifs and the set dressing they switch on |
| [Door access](door-access.md) | Locks, lock presets and key chains, consumable keys/picks, colored door markers, room-owned secret doors, discovery and saved access state |
| [Randomness and seeds](randomness-and-seeds.md) | Run and level seeds, generation streams, run-wide schedules, loot RNG, identification and saved world changes |
| [Runs, saving and permadeath](run-lifecycle.md) | Run start, the descent through five realms, saving on floor arrival and exit, death records and the death screen |

### Actors and combat

| Document | Coverage |
| --- | --- |
| [Monster design and authoring](monster-authoring-design.md) | Composition and authoring, data-driven actions (targets, conditions, weights), health phases, signature capabilities (charge, lob, ambush, theft, blink, flight, revive), bodies, defense, validation, awareness and strength |
| [Balance model](balance-model.md) | Progression, threat-based XP, floor budgets, armor, footwork, gear, supplies, traps and class parity from one model and its simulator; calibration and remaining work |
| [Character classes](character-classes.md) | Authored classes, starting gear, stat profiles, and model integration |
| [Actor actions and world interactions](actor-actions-and-interactions.md) | Controls, the off-hand action, item quick slots and timed interactions; monster interactions and levers remain proposed |
| [Combat actions and defense](combat-system.md) | Action ownership, dashes, hit feedback, dodge, guard/parry/guard break, poise and knockback in metres, threat tiers and telegraphs, trap hazards, contact resolution, sneak attacks, attack slots and flanking, persistence |
| [Weapon attacks](weapon-attack-system.md) | Hand capabilities, paired melee, spell weapons and magic missiles, the elemental spell roster with targeting and wards, shared player/monster spell definitions, projectile flight and lobs, combos, rig timing and melee contact |
| [Movement presentation](movement-feedback.md) | Shared locomotion animation sync and size-aware run, stop and dash cues |

### Items and economy

| Document | Coverage |
| --- | --- |
| [Stats and itemization](stats-and-itemization.md) | Actor-neutral buffs/statuses, venom/poison, attributes, modifiers, equipment tiers and quality, loot rolls and enchantment, curses, loot sources, portable lights, item ownership and theft |
| [Item identification](item-identification-system.md) | Per-run disguises, discovery, jewelry, scrolls and targeting |
| [Shops](shops.md) | The Quartermaster on the first floor of each realm after the Garrison, trade tabs in the run menu, merchant profiles, prices, finite stock and saved purchases |

### Presentation

| Document | Coverage |
| --- | --- |
| [User interface](user-interface.md) | Visual language and layout rules, fonts, the generated UI kit, the theme and its variations, the front end on its 3D stage, depth card, HUD and message feed, floor map, run menu (Gear, Pack, Notes, System; Buy and Sell for trades), death and confirmation |
| [Actor and item world effects](world-effects.md) | Reusable burst/sustained VFX, status looks (frozen, chilled, burning, goo, invisibility), shared cloak/skin ownership, item auras (curse, spell element), glowing weapons, item-use and level-up bursts, fountains, teleports and the workbench |
| [Screen effects](screen-effects.md) | Focus blur, darkness, blindness, poison, near-death and curse overlays, their wiring through health, light and status conditions, and the harness |
| [Ambient motion](ambient-motion.md) | Generated banner/foliage motion, shared airflow, per-prop response components and exported authoring controls |

### Tools

| Document | Coverage |
| --- | --- |
| [Combat harness and traces](combat-harness.md) | The game's debug menu in an arena: passive dummy, scatter spawns, hero builds, wall layouts, scenario table, JSONL trace schema and analyzer |
| [Debug harnesses](../scenes/debug/README.md) | The in-game debug menu and its tools, and the room, map, combat and effects entry points |

## Remaining implementation plans

Each plan describes only proposed work; the linked feature documents own what is implemented.

| Document | Remaining scope |
| --- | --- |
| [Dungeon progression](specs/level-progression-prototype-plan.md) | Guaranteed locations per floor (required roles, intentional loops) and a visible gatekeeper seal on the descent |
| [Trap hazards and gauntlets](specs/trap-hazards-plan.md) | Corridor gauntlets, realm hazard pacing and the cathedral nave, optional spike strips, vault gauntlets, burning spouts, acid pipes |
| [Triggers: switches, puzzles and linked doors](specs/triggers-plan.md) | Switches, puzzle conditions, item-weighted plates, saved latches, doors as receivers, then a quest seam |
| [Realm content pacing](specs/realm-content-pacing-plan.md) | Encounter packs that follow floor motifs; open decisions on the Cathedral's gatekeeper, realm merchants and bridging rooms |
| [Props for the new floor plans](specs/floor-plan-props.md) | Missing prayer kneeler, well, garden bed and chasm, lake and lava pieces, with mount, size and the rooms they serve |
| [Realm monster roster](specs/realm-monster-roster-plan.md) | The Mines and Cathedral casts and gatekeepers, the Sewer's aquator and tentacles, and grasp, petrify, burrow, gaze, invisibility, door phasing, drain and summon |
| [Combat redesign](specs/combat-redesign-plan.md) | A guarded walk, and the playtest risks to watch |
| [Elemental spells](specs/elemental-spells-plan.md) | Distinct level 4–5 visuals, area boundaries for moving blockers and height, meteor debris damage, freeze payloads, a reduce-flashing option, acceptance and balance |
| [Magic missile](specs/magic-missile-plan.md) | The finished dart look (shard and roll, launch rune, impacts, garnish) and acceptance |
| [Potion and scroll feedback](specs/consumable-feedback-plan.md) | Not started: a unique activation signature for all 20 potion and scroll types, with world and screen channels and catalog coverage checks |
| [Shop rooms and future merchants](specs/shop-design.md) | A dedicated shop room with a safe threshold, a shelf for every depth, later merchants |
| [Logging](specs/logging-plan.md) | Internal and Debug channels replacing the Combat/AI log toggles, settings, command-line switches and JSONL output |
| [UI redesign](specs/ui-system-plan.md) | The touch HUD, gamepad glyphs, readable item lines, credits and settings |

Completed plans are consolidated into current-system documents and removed. The [roadmap](development-roadmap.md) owns priority and acceptance. Use the [program-design skill](../.agents/skills/program-design/SKILL.md) for substantial future changes; a plan describes proposed work, not delivered behavior.

## Content and authoring references

- [Level generation folders](../scenes/levels/README.md): generator code, realms, floor profiles, room types and their content.
- [Room assembly authoring](../scenes/levels/generators/assembly/README.md): props, groups, styles, steps and room types, scheduling a room in a realm; harness and editor plugin.
- [Monster templates](../scenes/enemies/templates/README.md): duplicating and wiring a monster scene.
- [Gameplay item catalog](item-catalog.md), its [visual catalog](item-catalog/README.md) and the [monster catalog](monster-catalog/README.md): references for authored gameplay content. Use the Asset Studio catalogs below for published models and previews.
- [Asset Studio](../asset-studio/README.md) covers [new character and wearable art](../asset-studio/character-workflow.md), [standalone items](../asset-studio/README.md#standalone-item-workflow), and [existing recipe maintenance](../asset-studio/maintenance/character-and-wearable-recipes.md). See the [architecture and export boundary](../asset-studio/ARCHITECTURE.md) and the [item](../asset-studio/catalogs/items/README.md) and [character](../asset-studio/catalogs/characters/README.md) visual catalogs.

## Organization rules

Keep feature documents flat under `docs/` to preserve links, and implementation plans in `docs/specs/`. Add a current-system document when a feature needs an explanation of its implemented design.

The GDD owns intended gameplay, feature documents own current design and behavior, implementation plans own proposed code changes, and the roadmap owns delivery status. Prefer a link over a second copy of the same rules. Clearly label known limitations, proposals, and open decisions. Changes to a plan alone do not change the current-system description. When a plan lands, move its lasting rules into the owning feature document and remove the plan, or cut it to what remains. Keep algorithms, call stacks, numbers that mirror authored Resources, and implementation details in code; keep test logs, benchmark runs and debugging history out of living documentation.
