# Rogue Gauntlet — Development Roadmap

Status: initial roadmap, October 19, 2026. Milestones are ordered by dependencies and playable outcomes, without delivery-date commitments. The [Game Design Document](game-design.md) describes the intended experience and open design decisions.

## Existing foundations

The repository already contains procedural floors and fog of war, character classes, combat and enemy behavior, inventory and equipment, stat modifiers, equipment tiers and affixes, item identification, scroll effects, loot tables, monster/chest/floor rewards, and save/session infrastructure. These are foundations to extend and verify together, rather than evidence that the complete game loop is finished.

Existing RNG and world-state behavior is documented in [Randomness and Seeds](randomness-and-seeds.md); this is an extension of seeded generation already in the game, not a new RNG foundation. The [documentation index](README.md) links the other implemented systems.

Asset Studio supplies a growing visual catalog and some corresponding gameplay definitions. Food now participates in the item lifecycle. Tools, keys, and additional wearable armor still need behavior, integration, or validation appropriate to their intended roles.

Asset Studio now exports the selected wolf, rat and rigid large-rig stone golem through the normal character commands, with local technical rigs and art-only Godot import validation. See [Studio architecture](../asset-studio/ARCHITECTURE.md) for the export/gameplay boundary and remaining legacy weapon-preview dependencies. This does not create playable enemy definitions.

## Milestone 1 — Items retain their identity and state

Status: implemented core loop; final visual/stairs acceptance remains an editor playtest. Runtime checks cover creation, capped stacks, individual food, pickup/use/drop, codecs, and seeded floor reconstruction.

See the [item lifecycle program design](specs/item-lifecycle-plan.md) for delivered contracts, Resource wiring, and the item playground. RNG isolation, runtime identity, inventory operations, food initialization, and per-floor item differences are implemented together. Additional gameplay families remain in later milestones.

**Outcome:** an item stays the same through its complete lifecycle.

- Establish consistent definition references and runtime state across supported item families; migrate existing equipment incrementally.
- Initialize random properties through explicit, seeded creation rather than resource constructors.
- Extend the existing seeded generation and separate loot/identification RNG paths with explicit level/monster/item stream ownership; verify same-build reproduction. Changed draw order between builds may change scenarios; historical seed compatibility is excluded.
- Make stackability an explicit authored opt-in restricted to interchangeable units of one variety; food remains individual regardless of poison state. Preserve variety and quantity through stack splitting and merging.
- Retain the existing 20-entry capacity as the initial gameplay tuning baseline and enforce slot limits consistently. Equipped entries count toward capacity, currency stays in stats, and potion/scroll caps are 20; allow additions to existing stacks when all slots are occupied. Persist capacity and provide clear acquisition failures.
- Preserve item state and quantities through pickup, use, equipment changes, dropping, transfers, floor transitions, and save/load.
- Rebuild untouched floors, encounters, and source loot from level/domain/source seeds; persist only differences such as collected items, changed stacks, player drops, and opened/claimed sources. Verify actual monster definitions, positions, and item rolls reproduce before applying changes.
- Pre-release save compatibility is excluded: bump the save-format counter for incompatible changes and reject mismatched or missing versions. Do not implement migrations or old-save adapters; verify current-version save/load only.

**Acceptance:** create two food instances with different contamination state and a rolled weapon. Transfer, drop, recover, save, and reload them without changing properties, losing quantity, duplicating rewards, or mutating shared definitions. Both food items remain individual and stacking reveals no contamination. Identical ammunition stacks; different varieties stay separate. At capacity, compatible stacks accept units within their limit while failed acquisition preserves the source item. Verify capacity survives loading and revisiting a floor preserves collected and dropped items.

## Milestone 2 — Coherent rewards at every depth

Status: source profiles, category budgets, and full active-catalog coverage are implemented. Reward usefulness and final balance still require playtesting; see the [gameplay item catalog](item-catalog.md).

Depends on milestone 1.

**Outcome:** monsters, chests, and floor finds deliver varied rewards appropriate to the floor.

- Route every source through consistent depth eligibility and item creation; retain the depth filtering and quantity scaling now shared by source and floor loot.
- Author source-specific reward profiles with category budgets and bounded reward bonuses.
- Maintain basic supplies while shifting equipment and consumable potency with depth.
- Keep required progression objects separate from random reward selection.

**Acceptance:** seeded generation is repeatable; all sources respect depth bounds; adding equipment definitions does not displace the authored supply budget. Sample reward distributions across representative shallow and deep floors, and playtest whether discoveries remain useful.

## Milestone 3 — A useful set of dungeon objects

Status: planned; existing consumables, equipment slots, and item effects provide partial foundations.

Depends on milestones 1–2. Deliver the following in small playable increments.

- **Food (implemented):** authored food provides recovery and per-instance contamination, with persistent item state. Tune supply rates and recovery through playtesting.
- **Armor:** all exported custom shields now have definitions, protection/tradeoffs, depth eligibility, and held visuals. Additional wearable body armor still needs slot and visual integration.
- **Lighting:** settle visibility and equipment-slot rules; add one light source with activation, fuel consumption, and feedback.
- **Access:** compose door motion, mechanical locks, and encounter conditions. Add optional pickable prison cells and matching-key rewards; initial lockpicking is a timed action with a reusable tool. Key color is presentation. Follow [Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md) and the separate [implementation plan](specs/level-progression-prototype-plan.md).
- **Carrying capacity:** add a larger backpack upgrade available later in the descent, preserving contents and enforcing the chosen slot-accounting rules. Decide its acquisition method and size through playtesting.
- **Attack animation styles (implemented):** explicit weapon styles and per-actor animation profiles distinguish handedness, slash/chop/thrust, ranged, and casting motions. Contact/release, recovery, and interruption share one execution clock; monsters use their own melee/ranged clip mappings. Independent melee contact volumes and optional slash/chop swooshes are implemented for heroes and monsters. See the [animation design](specs/attack-animation-plan.md) and [melee contact design](specs/melee-contact-plan.md). Bow gameplay, final balance tuning and improved single-clip monster presentation remain follow-ups.
- **Presentation:** verify world pickups, inventory previews, item details, and worn or held objects at gameplay scale.

**Acceptance:** in one playable descent, find and consume food, loot and wear armor, use a portable light, discover a key, and unlock a reward chest. Acquire a larger backpack and verify its capacity increase and contents survive save/load. Initial mechanical locks cannot obstruct required progression, and each tool has an observable purpose.

## Milestone 4 — A meaningful shop visit

Status: planned.

Depends on milestones 1–3 and decisions about interface pause behavior and merchant knowledge.

**Outcome:** earned currency creates a useful preparation choice.

- Place one shop type using a deliberate encounter rule.
- Generate finite, depth-appropriate supplies, equipment, and utility offers.
- Add buying and selling with atomic item/currency transfers and understandable prices.
- Persist stock, quantities, and transactions across visits and save/load.
- Prevent accidental hidden-property disclosure through pricing and trivial buy/sell profit loops.

**Acceptance:** buy supplies or equipment, sell loot, leave, return, and reload. Stock and balances remain correct. Full inventory, insufficient funds, and repeated input cannot lose or duplicate items or currency. Shallow and deep shops respect their configured eligibility.

## Milestone 5 — First complete run

Status: first-realm encounter prototype implemented; full-run integration remains planned. The intended run is 24 depths across eight realms, with World-Anchor victory and run-ending death; permanent progression remains undecided. The playable first-realm slice uses skeleton budgets, a crossbow variant, finite nests and the large Forsaken Warden with a persistent descent check. See [Level design](level-design.md#first-realm-encounter-prototype) for current behavior. Recipe routes, locks, secrets, visible seals, shops, bespoke boss phases and the ending are not completed by that slice.

Depends on milestones 1–4.

**Outcome:** a short run has a recognizable beginning, progression, and ending.

- Integrate the defined run length, World-Anchor objective, victory and death flow; decide permanent progression separately.
- Deliver realm room pools and floor recipes, composed access with a visible Warden stair seal, optional mechanical locks, then secrets at generated corridor/procedural-room walls. Validate required routes and solvability in actual geometry; fixed secret sockets in predefined rooms are excluded. The [progression plan](specs/level-progression-prototype-plan.md) owns engineering slices and checks.
- Connect class selection, descent, rewards, preparation, shops, and the ending.
- Tune encounter difficulty, recovery availability, equipment progression, and currency together.
- Prototype crowd encounters alongside smaller tactical fights. Establish a target enemy count and frame-time budget on a named test machine; verify navigation, hit feedback, visibility, and escape options at that scale before expanding density.
- Establish sufficient feedback for a new player to understand core interactions and important consequences.

**Acceptance:** play complete runs with contrasting classes and multiple seeds, including resuming a saved run. Each run reaches a valid win/loss outcome without progression locks or broken persistence. Review avoidable deaths, item usefulness, supply shortages, backpack pressure, and whether shop choices matter. Crowd encounters meet the established performance target and remain readable, with useful responses available to contrasting builds.

## Later candidates

These are options, not committed milestones: more tool/environment interactions, restrictive curses and removal, hunger or spoilage, additional shop specialties, biome-specific rewards, unique equipment, crafting, and between-run progression.

Promote a candidate when it supports a design pillar and a playtested need. Expand the item catalog alongside working interactions rather than treating every available model as required playable content.

## Keeping the roadmap useful

Mark a milestone complete only when its acceptance outcome has been verified. Update status and link implementation work as it lands. Revise the Game Design Document when intended behavior changes; keep detailed architecture and implementation notes in their existing documents.
