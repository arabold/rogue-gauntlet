# Stats and itemization

Implemented item ownership, inventory and stat rules. Godot Resources supply definitions and tuning; [item identification](item-identification-system.md) owns hidden type knowledge, the [gameplay catalog](item-catalog.md) owns content references, and [randomness and saves](randomness-and-seeds.md) owns seeded reconstruction.

## Attributes and modifiers

Player stats are copied per actor. Strength, Dexterity, Vitality and Intelligence feed secondary stats through an authored `StatProfile`; class profiles choose their coefficients. Modifiers apply flat contributions before percentage contributions. Equipment and buffs register contributions by source, so removing one source reverses its effects without changing another's.

Supported secondary stats include maximum health, speed, damage range, critical chance and armor. Dexterity does not grant passive accuracy or evasion. Resolved values drive health, movement, damage and the HUD; maximum-health derivation must not be fed back into base stats. See [active defense](combat-system.md#contact-resolution) for hit handling.

Timed stat buffs expire once; permanent buffs remain until explicitly removed. Periodic healing/poison use their own authored effects. Class starting profiles and gear are described in [character classes](character-classes.md).

## Buffs and status conditions

A buff can declare the status it imposes in `Buff.Condition`, a `StatusCondition` Resource such as
`scenes/buffs/conditions/poisoned.tres`. The condition file is the identity and is compared by
reference, so always reference it as an external resource: `ItemFactory`'s deep duplicate copies
embedded buffs but keeps external conditions shared. `BuffController.HasCondition` is true while
any active buff carries the condition, so overlapping sources, such as poisoned food followed by a
poison potion, read as one continuous condition. `BuffController` fires one `BuffsChanged` per
application, removal, expiry sweep or `ClearBuffs`, and observers re-read what they need.
Presentation, such as [screen effects](screen-effects.md), and gameplay react to conditions for
statuses without a magnitude, and to buff subclasses for effects with one:

- A plain `Buff` with only a `Condition` is a status with no behavior of its own: blindness
  (`blinded`) and confusion (`confused`). The blindness overlay and the player's
  `ConfusionComponent` react to those conditions.
- `TimeScaleBuff` (Slowness) slows the whole game. The player's `TimeScaleComponent` applies the
  slowest active one to `Engine.TimeScale` and restores normal speed when the last one ends or the
  scene unloads. Durations count game time.
- `Buff.CuredConditions` ends conditions when any buff is applied; healing potions cure blindness.

## Equipment, rarity and enchantment

Base tier and rolled rarity are independent. Tier describes the base item's progression role; rarity controls eligible affixes. Affix definitions provide weighted ranges, compatible slots and minimum tier/rarity. Concrete rolled modifiers belong to the runtime item and survive restoration without depending on another roll.

An `ItemCreationPolicy` chooses fixed creation or randomized equipment/food initialization. Starting supplies use fixed creation; dungeon sources use an explicit policy and RNG. Copying, transferring or restoring an item never rerolls it.

Enchantment raises rarity within its cap and adds an eligible affix without duplicating its name fragment. Equipped targets are safely rebound so resolved stats reflect the changed item. Display names combine identification-aware names with rolled affix fragments; inventory views share rarity presentation.

## Loot eligibility

Source tables own depth bounds, quantities and category budgets. Items do not infer drop eligibility from their model name. Grouped loot selects an eligible category before a variety inside it, so adding weapon variants cannot displace the authored recovery-supply budget. Simple tables may use flat entries; do not author flat and grouped forms together.

See the [catalog](item-catalog.md#source-budgets-and-eligibility) and the source `.tres` files for actual eligibility and weights. Monster threat and item tier are selection/tuning concepts, not automatic depth-based stat scaling.

## Portable-light equipment

Six `PortableLightItem` definitions provide held illumination. Each explicitly assigns a `HeldScene` and `WeaponHandGrip`/`ShieldHandGrip` adapters; its ordinary `Scene` remains the unlit pickup/inventory model. The attachment manager instances the held scene only while equipped, so removing, dropping or displacing a light removes its model, flame and illumination together. The level darkness flag affects environmental fixtures, not held lights.

The inventory menu offers left (shield) and right (weapon) hands. Automatic equip prefers an empty hand, checking the shield hand first; when both hands are occupied it replaces the shield. A light displaces that hand's occupant, and a shield-hand light also displaces a two-handed weapon. Equipping a two-handed weapon removes either held light. Displaced gear stays in the backpack. Current-version saves preserve the item identity and hand assignment; restoring equipment recreates the held light.

Lights currently illuminate whenever equipped, with no fuel consumption or manual ignition. They are available in the debug spawn menu but excluded from normal loot and quality rolls pending supply and fuel design. Asset Studio recipes export these models with their tops pointing upward in idle and walking poses, so their model wrappers need no extra rotation. The right hand reuses the existing melee grip; the left-hand adapter accounts for its mirrored socket. Light and flame placements follow the authored model rotation.

## Item ownership, food, and persistence

Authored definitions remain shared and immutable. Every runtime individual or homogeneous stack owns its Resource, definition provenance and instance identity. Equipment references inventory entries rather than a second copy. Whole-item transfers preserve identity; splitting creates another record, and merging retires the source record.

The initial backpack holds 20 entries, including equipped gear. Currency remains in player stats. Potions and scrolls stack to 20 of the exact same definition; equipment and food remain individual. Capacity is saved. Compatible merges can succeed in a full backpack without exceeding a stack cap. Larger-backpack acquisition remains future work.

Pickup, use, drop and equipment operations validate ownership, quantity, targets and space before committing. Failed pickup preserves the world source; a successful use applies one effect and consumes one unit. UI and quick slots use the same ownership rules. Identification knowledge does not determine stack equality.

Food recovery and contamination are per-piece state. Dungeon creation may roll contamination once; fixed starting supplies do not. Copying or loading preserves it. Food has no type-wide disguise, stacking cannot disclose contamination, and there is no hunger system.

Inventory restoration validates the complete state before replacement and preserves quantities, rarity, affixes, food state and equipment references. Floors regenerate their untouched baseline and restore saved differences. Current-version compatibility is defined by `SaveGame.CurrentVersion`; older development saves require a new run, without conversion or silent overwrite. Active timed buffs and living-enemy injuries/positions are outside the current persistence scope.

## Authoring guide

- Add buffs through `StatModifierBuff` and its modifier list; set duration to distinguish timed and permanent effects.
- Add affixes and tune rarity through the assigned `AffixPool`, rather than hardcoded gameplay paths.
- Create item definitions with explicit stack limits, effects, slots, tier and visual wrapper. Assign creation policy and loot eligibility at the source.
- Verify handedness and held scale on the actual player rig. See [equipped orientation](item-catalog.md#equipped-orientation); floating previews are insufficient.
- Use the Remote Inspector to inspect runtime copies. Use supported equipment/enchantment commands instead of editing live modifier collections without rebinding.

Run [item_lifecycle_harness.tscn](../scenes/debug/items/item_lifecycle_harness.tscn) with F6 in Godot to exercise creation, equipment, food, stacks and capacity. Its Inspector exposes definitions, policy, seed, depth and quantity. Review save/resume and floor revisits in gameplay; the [roadmap](development-roadmap.md) owns remaining acceptance and extensions.
