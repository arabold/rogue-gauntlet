# Rogue Gauntlet — Development Roadmap

Status: initial roadmap, October 19, 2026. Milestones are ordered by dependencies and playable outcomes, without delivery-date commitments. The [Game Design Document](game-design.md) describes the intended experience and open design decisions.

## Existing foundations

The repository already contains procedural floors and fog of war, character classes, combat and enemy behavior, inventory and equipment, stat modifiers, equipment tiers and affixes, item identification, scroll effects, loot tables, monster/chest/floor rewards, and save/session infrastructure. These are foundations to extend and verify together, rather than evidence that the complete game loop is finished.

Existing RNG and world-state behavior is documented in [Randomness and Seeds](randomness-and-seeds.md); this is an extension of seeded generation already in the game, not a new RNG foundation. The [documentation index](README.md) links the other implemented systems.

Asset Studio supplies a growing visual catalog and some corresponding gameplay definitions. Tools, keys, food, and wearable armor still need behavior, integration, or validation appropriate to their intended roles.

## Milestone 1 — Items retain their identity and state

Status: planned; equipment instance rolls and inventory persistence provide a partial foundation.

See the [item lifecycle program design](specs/item-lifecycle-plan.md) for proposed types and method contracts, Resource/scene wiring, transfer and restore call order, affected files, an RNG isolation/verification prerequisite extending existing generation, and four delivery slices. It follows the repository's [program-design skill](../.agents/skills/program-design/SKILL.md). Capacity accounting recommendations there remain proposals until agreed.

**Outcome:** an item stays the same through its complete lifecycle.

- Establish consistent definition references and runtime state across supported item families; migrate existing equipment incrementally.
- Initialize random properties through explicit, seeded creation rather than resource constructors.
- Extend the existing seeded generation and separate loot/identification RNG paths with explicit level/monster/item stream ownership; verify same-build reproduction. Changed draw order between builds may change scenarios; historical seed compatibility is excluded.
- Make stackability an explicit authored opt-in restricted to interchangeable units of one variety; food remains individual regardless of poison state. Preserve variety and quantity through stack splitting and merging.
- Retain the existing 20-entry capacity as the initial gameplay tuning baseline and enforce slot limits consistently. Decide equipment/currency/key accounting and stack caps; allow additions to existing stacks when all slots are occupied. Persist capacity and provide clear acquisition failures.
- Preserve item state and quantities through pickup, use, equipment changes, dropping, transfers, floor transitions, and save/load.
- Rebuild untouched floors, encounters, and source loot from level/domain/source seeds; persist only differences such as collected items, changed stacks, player drops, and opened/claimed sources. Verify actual monster definitions, positions, and item rolls reproduce before applying changes.
- Pre-release save compatibility is excluded: bump the save-format counter for incompatible changes and reject mismatched or missing versions. Do not implement migrations or old-save adapters; verify current-version save/load only.

**Acceptance:** create two food instances with different contamination state and a rolled weapon. Transfer, drop, recover, save, and reload them without changing properties, losing quantity, duplicating rewards, or mutating shared definitions. Both food items remain individual and stacking reveals no contamination. Identical ammunition stacks; different varieties stay separate. At capacity, compatible stacks accept units within their limit while failed acquisition preserves the source item. Verify capacity survives loading and revisiting a floor preserves collected and dropped items.

## Milestone 2 — Coherent rewards at every depth

Status: planned; depth-gated tables and equipment rolls already exist.

Depends on milestone 1.

**Outcome:** monsters, chests, and floor finds deliver varied rewards appropriate to the floor.

- Route every source through consistent depth eligibility and item creation; fix floor loot's current bypass of depth filtering and quantity scaling.
- Author source-specific reward profiles with category budgets and bounded reward bonuses.
- Maintain basic supplies while shifting equipment and consumable potency with depth.
- Keep required progression objects separate from random reward selection.

**Acceptance:** seeded generation is repeatable; all sources respect depth bounds; adding equipment definitions does not displace the authored supply budget. Sample reward distributions across representative shallow and deep floors, and playtest whether discoveries remain useful.

## Milestone 3 — A useful set of dungeon objects

Status: planned; existing consumables, equipment slots, and item effects provide partial foundations.

Depends on milestones 1–2. Deliver the following in small playable increments.

- **Food:** connect authored food to recovery and per-instance contamination, with clear effect feedback and persistent state.
- **Armor:** integrate a small selection with distinct protection/tradeoffs, correct slots, and compatible equipped visuals.
- **Lighting:** settle visibility and equipment-slot rules; add one light source with activation, fuel consumption, and feedback.
- **Access:** add matching keys and optional locked reward chests. Add lockpicks only with explicit eligibility and cost rules.
- **Carrying capacity:** add a larger backpack upgrade available later in the descent, preserving contents and enforcing the chosen slot-accounting rules. Decide its acquisition method and size through playtesting.
- **Presentation:** verify world pickups, inventory previews, item details, and worn or held objects at gameplay scale.

**Acceptance:** in one playable descent, find and consume food, loot and wear armor, use a portable light, discover a key, and unlock a reward chest. Acquire a larger backpack and verify its capacity increase and contents survive save/load. Locks cannot block the required route, and each tool has an observable purpose.

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

Status: proposed scope; run length, objective, and death rules need design decisions.

Depends on milestones 1–4.

**Outcome:** a short run has a recognizable beginning, progression, and ending.

- Define the initial dungeon length, final objective, victory, and death flow.
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
