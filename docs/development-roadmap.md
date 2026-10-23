# Rogue Gauntlet — Development Roadmap

Status: revised October 23, 2026. Milestones are ordered by dependencies and playable outcomes, without delivery-date commitments. The [Game Design Document](game-design.md) describes the intended experience and open design decisions.

## Existing foundations

The repository already contains procedural floors and fog of war, character classes, combat and enemy behavior, inventory and equipment, stat modifiers, equipment tiers and affixes, item identification, scroll effects, loot tables, monster/chest/floor rewards, and save/session infrastructure. These are foundations to extend and verify together, rather than evidence that the complete game loop is finished.

Existing RNG and world-state behavior is documented in [Randomness and Seeds](randomness-and-seeds.md); this is an extension of seeded generation already in the game, not a new RNG foundation. The [documentation index](README.md) links the other implemented systems.

Asset Studio supplies a growing visual catalog; corresponding gameplay definitions are authored separately in Godot. Food now participates in the item lifecycle. Tools, keys, and additional wearable armor still need behavior, integration, or validation appropriate to their intended roles.

Asset Studio now exports the selected wolf, rat and rigid large-rig stone golem through the normal character commands, with local technical rigs and art-only Godot import validation. See [Studio architecture](../asset-studio/ARCHITECTURE.md) for the export/gameplay boundary and remaining legacy weapon-preview dependencies. This does not create playable enemy definitions.

## Current priority — Playable depths 1–3 and monster authoring

Status: authoring foundation implemented for minion/warrior; ranged/retreat tactics, Studio rollout and roster cleanup remain planned. This bounded first-realm delivery does not wait for shops, later realm order or a complete 24-depth run. [Monster design and authoring](monster-authoring-design.md) owns design decisions; the [implementation plan](specs/monster-authoring-plan.md) owns contracts, retirement rules and delivery slices.

- Retain skeleton minion, warrior and crossbowman. Introduce the approved Studio rat and move the Warden into its own gameplay folder; preserve visuals and Studio sources.
- Author monsters manually in Godot using copyable assemblies, instanced components and Inspector-editable Resources. Keep art-only Studio recipes separate from gameplay configuration.
- Deliver close, keep-distance and attack-then-retreat tactics, plus optional bounded low-health retreat. Each monster has one melee or clear-sight projectile attack; guard/dodge probabilities and conditional riposte are independent capabilities. Defer flanking, group shelter and weapon switching.
- Retire the old Quaternius gameplay roster only after pools, debug/test callers and references are repaired. Preserve adopted Studio-source provenance. Use fixed authored strength, explicit XP and realm/depth encounter eligibility.
- Tune the five-monster roster, finite nests, rewards/recovery and single-phase Warden together. Wolf, spider, golem and later realms follow after the authoring workflow and first realm work well.

**Acceptance:** create/customize a monster in the Godot UI without code, validate independent instances, and play depths 1–3 with two contrasting classes and three seeds, including save/resume and revisit. Review attack readability, useful defenses/escape, doorway congestion, recovery pressure and boss openings. Automated checks cover composition, actual seeded encounters/rewards and persistence; editor playtests establish navigation and balance.

## Action and interaction improvement track

Status: partially implemented. Shared action ownership, timed interaction foundations and dodge/guard/parry exist; broader controls, magic and content rollout remain planned. See [Combat actions and defense](combat-system.md) for current behavior. This track can be exercised in the existing encounter prototype without waiting for shops or full dungeon progression. [Actor actions and world interactions](actor-actions-and-interactions.md) owns the concept; its [implementation plan](specs/actor-actions-and-interactions-plan.md) owns contracts and delivery slices.

Deliver one shared actor action boundary, separate weapon primary/secondary actions from the four item-only quick slots, and auto-assign eligible pickups only into unassigned slots. Replace attack/interact input overlap with a dedicated hold interaction and progress ring. Ordinary operations start at one second; mechanical picking uses its own five/ten-second duration through the same executor. Add dodge, guard/timed parry and restrained enemy requests through that action model. Bodies, equipment and tools grant independent capabilities; monsters do not need a player quick bar. Add a fire-magic vertical slice (free projectile, Fireball/Wall of Fire, learned selection and mana), then expand to four staff families with five spells each. Classes define starting kits/stat affinities, not exclusive magic access. Controls and spell placement must work with keyboard and gamepad.

**Acceptance:** all four item slots assigned while primary/secondary still work; compatible learned spells and mana costs, cross-class casting, no swap-based cooldown/resource reset; no accidental attacks while opening a chest; safe pickup/stack identity and depleted-slot handling; visible cancellable one/five/ten-second operations with no effects before completion; actor-specific door/tool permissions; body-contact dodge, guardable projectile and parry interruption. Verify movement/damage/same-tick cancellation, stale callbacks, deterministic reward claims and current-version binding/cooldown/world-state restoration. Coordinate mechanical-lock authority with the dungeon progression plan rather than creating a second lock/timer system.

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

Status: first-realm encounter prototype implemented; full-run integration remains planned. The current full-run concept is 24 depths across eight realms (later realm order remains provisional), with World-Anchor victory and run-ending death; permanent progression remains undecided. The playable first-realm slice uses skeleton budgets, a crossbow variant, finite nests and the large Forsaken Warden with a persistent descent check. See [Level design](level-design.md#first-realm-encounter-prototype) for current behavior. Recipe routes, locks, secrets, visible seals, shops, bespoke boss phases and the ending are not completed by that slice.

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
