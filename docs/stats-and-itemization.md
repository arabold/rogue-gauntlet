# Stats & Itemization System

Status: current-system documentation. Future ideas, where included, are proposals rather than implemented behavior. See the [documentation index](README.md) for related systems and implementation plans. Shared RNG ownership and seed behavior are documented in [Randomness and Seeds](randomness-and-seeds.md).

## Overview

Player stats are a **composable layer** rather than fixed fields, and equipment carries
**instance-level rolls** rather than only authored values. Together they give Rogue Gauntlet
Diablo-style itemization: primary attributes that drive a build, gear that grants stat
modifiers, and randomized affixes rolled per drop.

The system has three layers, each independently useful and building on the one before:

1. **Stat modifiers** — a composable `StatModifier` primitive and a data-driven
   `StatModifierBuff`, so most effects need no bespoke `Buff` subclass.
2. **Primary attributes** — Strength, Dexterity, Vitality, Intelligence, plus a `StatProfile`
   that derives secondary stats from them. This is the seam classes/races plug into later.
3. **Affixes & rarity** — randomized, instance-level modifiers rolled onto dropped equipment,
   seeded from the per-run seed and persisted on the item.

This is distinct from, and coexists with, the [item identification system](item-identification-system.md):
identification is a **type-level disguise** (a potion type's look is hidden until discovered);
affixes are **instance-level rolls** (this particular axe rolled +5 Strength).

## How it builds on existing systems

- Stats live on `PlayerStats` (`ObservableResource`), copied per player instance on spawn and
  persisted in `SaveGame`. The runtime copy is mutated, never the authored `.tres`.
- Buffs run through `BuffController` / `ActiveBuff`; equipment applies effects in
  `EquipableItem.OnEquipped` / `OnUnequipped`.
- `PlayerStatsController` syncs resolved stats into the gameplay components
  (`MovementComponent`, `HurtBoxComponent`, `HealthComponent`) and emits
  `SignalBus.PlayerStatsChanged`; the HUD reacts to that.
- Loot drops come from `LootTableComponent` drawing a shared `LootTable`; the run `Seed` lives
  in `GameSession` / `SaveGame`.

## Layer 1 — Stat modifiers and buffs

- `StatType` (enum): every stat a modifier can target — the primary attributes plus the
  secondary stats `MaxHealth, Speed, Accuracy, MinDamage, MaxDamage, CritChance, Armor, Evasion`.
- `StatModifier` (Resource): the atomic unit — `Stat`, `Op` (`Flat` or `Percent`), `Value`.
  Shared by buffs **and** affixes.
- `StatModifierBuff : Buff`: applies a set of `StatModifier`s on `OnApply` and reverses them on
  `OnRemove`. `Duration 0` means permanent (e.g. a strength potion). Periodic effects still use
  a `PeriodicBuff` subclass (`HealingBuff`, `PoisonBuff`).

### Buffs and status conditions

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

`PlayerStats` owns a registry of active modifiers keyed by the object that added them:

- `AddModifiers(source, modifiers)` / `AddModifier(source, modifier)` — register under a source.
- `RemoveModifiersFrom(source)` — remove exactly what that source added.

Because removal is **by source reference**, not arithmetic, unequipping an item or expiring a
buff reverses its contribution exactly, with no floating-point drift. `ActiveBuff` counts down
all timed buffs and funnels every removal (expiry or explicit) through a single `Deactivate()`,
so `OnRemove` runs exactly once.

## Layer 2 — Primary attributes and stat resolution

`PlayerStats` holds base primary attributes (`BaseStrength`, `BaseDexterity`, `BaseVitality`,
`BaseIntelligence`, default 10) and base secondary stats. A referenced `StatProfile` holds the
**derivation coefficients** that turn attributes into secondary stats — the data a class or race
would later swap.

Resolution, additive then multiplicative:

```
attribute = (baseAttribute + Σ flat mods) * (1 + Σ percent mods)
stat      = (baseStat + attributeDerivation + Σ flat mods) * (1 + Σ percent mods)
```

The default profile (`player_stat_profile.tres`):

| Coefficient | Default | Effect |
| --- | --- | --- |
| `HealthPerVitality` | 5 | each VIT → +5 max health |
| `DamagePerStrength` | 0.1 | each STR → +0.1 min & max damage |
| `DamagePerIntelligence` | 0 | reserved for caster profiles |
| `CritChancePerDexterity` | 0.005 | each DEX → +0.5% crit |
| `EvasionPerDexterity` | 0.2 | each DEX → +0.2 evasion |

Base secondary values are tuned so the starting character (all attributes 10) lands on sensible
numbers: `BaseMaxHealth` is 50 so `50 + 10×5 = 100` max health, and the starting build has
1–3 unarmed damage, 5% crit, and 2 evasion. The public computed properties (`Speed`, `MaxHealth`,
`MinDamage`, `Armor`, …) are thin wrappers over the resolver, so `PlayerStatsController` and the
HUD read them unchanged.

Equipment contributes through `EquipableItem.BuildStatModifiers()`: `Weapon` and `Armor` override
it to yield their intrinsic stats, and the base class yields the item's rolled affixes. On equip
they are all registered under the item instance; on unequip, `RemoveModifiersFrom(item)` removes
them.

> Note: max health is derived and pushed one-way into `HealthComponent` by `SyncStats`.
> `PlayerStatsController` deliberately does **not** write the component's max back into
> `BaseMaxHealth`, which would re-add the Vitality derivation on every sync and inflate it.

## Layer 3 — Affixes and rarity

- `Affix` (Resource): a roll template — a `NameFragment` ("Vicious", "of the Bear"), a `Kind`
  (`Prefix`/`Suffix`), a set of `AffixModifierRange`s (a `StatType`/`Op` with min–max), an
  `AllowedSlots` mask (0 = any), a `Weight`, and a `MinRarity`.
- `RolledAffix` (Resource): the concrete result attached to an item — `NameFragment`, `Kind`, and
  the rolled `StatModifier`s. Self-contained (it does not reference the originating `Affix`), so it
  persists and restores without the pool.
- `AffixPool` (Resource, `affix_pool.tres`): the affix catalog plus the rules —
  `RollCounts` (affixes per rarity) and `RarityWeights` (depth-scaled `BaseWeight + WeightPerDepth × depth`).
- `ItemFactory` (static): copies authored definitions and stamps runtime provenance/identity. An
  exported `ItemCreationPolicy` selects fixed creation or equipment/food rolls with an explicit RNG.
- `LootRoller.ApplyRoll` / `Enchant`: mutate validated runtime equipment using a supplied affix pool.
  Copying and save restoration never reroll state.

`LootTableComponent` uses the source-derived loot streams for stable generated rewards (or the ongoing gameplay stream for dynamic sources) for drop selection and equipment
rolls; see [Randomness and Seeds](randomness-and-seeds.md#generation-streams-and-source-rewards) for seed derivation,
counter persistence, and current reconstruction limits. Rolled inventory equipment saves its concrete
affixes rather than depending on replaying the loot-event sequence.

`EquipableItem.ComposeName(baseName)` builds the display name from the first prefix and first
suffix fragment ("Vicious Broadsword of the Bear"); extra affixes add stats but not name (like a
rare item). `ItemIdentity.ResolveDisplayName` applies it on top of the identification name.

## Layer 4 — Base item ladder and depth-gated drops

Every `EquipableItem` carries a `Tier` (1–5): the base item's ladder rung, independent of
rolled `Rarity`. A Tier-4 "Royal Axe" can still roll Common; tier and rarity are separate
axes — tier is about the *base item*, rarity is about the *roll*.

| Tier | Nominal depth band | 1H avg damage | 2H avg damage |
| --- | --- | --- | --- |
| 1 | 1–3 | ~3 | ~4.5 |
| 2 | 2–5 | ~5.5 | ~8 |
| 3 | 4–8 | ~9 | ~12 |
| 4 | 7–12 | ~13.5 | ~18 |
| 5 | 10–∞ | ~21 | ~26 |

`Affix.MinTier` gates affix potency by the item's tier (independently of `MinRarity`), so
high-end affixes only roll on high-tier bases — `AffixPool.RollAffixes`/`RollSingleAffix`
take the item's `Tier` alongside its slots and rarity.

Depth gating for *drops* (as opposed to affix eligibility) lives on the loot entry, not the
item: `LootTableItem.MinDepth`/`MaxDepth` (0 = unbounded) mark which depths an entry is
eligible at, and `QuantityPerDepth` scales stackable quantity (e.g. gold) with depth.
`LootTable.PickEntry(depth, rng)` selects an eligible category budget, then an eligible variety
inside that `LootTableGroup`. Adding equipment variants preserves supply-group probability. Flat
`Items` is also available for simple custom tables; author either Groups or Items. This
keeps one authored table per drop source (`LootTableComponent.Table`) instead of one table
per depth band, and extends uniformly to any future item category (a scroll or jewelry entry
is just another banded row). `LootTableComponent.DropCountMin`/`DropCountMax` control how many
separate entries a single drop event rolls.

## Layer 5 — Enchantment

`LootRoller.EnsureInstance(item)` duplicates a shared definition into a rolled instance
(stamping `SourceDefinitionPath`) or returns an already-rolled instance unchanged.
`LootRoller.Enchant(item, rng, pool)` bumps `Rarity` one step (capped at Legendary) and adds one
new affix via `AffixPool.RollSingleAffix`, which excludes name fragments the item already
carries so it never stacks the same affix twice. The Scroll of Enchantment
(`EnchantScrollEffect`) unequips the target first, enchants it, then re-equips it — mutating
an equipped item in place would leave its old stat modifiers (registered under the pre-enchant
instance) stale.

## Display

Both the inventory hover tooltip and the right-click context menu render the same reusable
`ItemDetailsView` — a rarity-colored name, the rarity, and one line per rolled affix modifier —
so click and hover show identical information. `RarityPalette` is the single source of rarity
text colors. `ItemSlotButton._MakeCustomTooltip` returns an `ItemDetailsView` for the hover
tooltip; the context menu embeds one in place of its title label. The slot's background tint also
conveys rarity (the low-alpha colors authored on `ItemSlotPanel`).

## Save Schema

`SaveGame.CurrentVersion` is **3**. Changes from v2:

- `PlayerStatsSaveData` persists the base **primary attributes** and drops the old per-stat
  multiplier fields (those are now runtime modifiers, rebuilt from equipment/buffs on load). The
  attribute fields default to 10, so a pre-v3 save loads as a baseline character. (Because old
  `BaseMaxHealth` was 100, a v2 save reads higher max health once Vitality derivation is added; a
  fresh game is exactly 100.)
- `SaveGame.LootRollsConsumed` persists the loot-RNG counter.
- `InventoryItemSaveData` gains `Rarity` (−1 = a plain shared item) and a list of `Affixes`
  (each a `NameFragment`, `Kind`, and saved `StatModifier`s). On load, a rolled item is
  reconstructed by duplicating the base definition at `ItemPath` and re-stamping the saved rarity
  and affixes; plain items load directly from `ItemPath` as before.

## Authoring guide

- **A simple buff/debuff potion**: author a `StatModifierBuff` sub-resource with the target
  `StatModifier`s and a `Duration` (0 = permanent), and reference it from the consumable's `Buff`.
  No new C# class.
- **A new stat**: add a `StatType` value, give it a base field + resolver in `PlayerStats`, and
  (if attribute-driven) a coefficient in `StatProfile`.
- **A new affix**: add an `Affix` sub-resource to `affix_pool.tres` with its `NameFragment`,
  `Kind`, `AffixModifierRange`s, `AllowedSlots`, `Weight`, `MinRarity`, and `MinTier`.
- **Tune drop rarity**: edit the `RarityWeights` (base + per-depth) and `RollCounts` on
  `affix_pool.tres`.
- **A new base item / ladder rung**: author an `EquipableItem` `.tres` with a `Tier` matching
  the depth band table above. Every weapon/shield goes through a thin wrapper `.tscn` around its
  model (named after the item, e.g. `sword_common.tscn`, not the underlying asset) rather than
  referencing a `.glb` directly, so the model can be swapped or corrected later with a one-line
  `ext_resource` change — see the `godot-mcp` skill's `tres-authoring.md` for the wrapper
  pattern. For a `Weapon`, set `IsTwoHanded` based on the model's actual measured size against a
  sibling on the same bone, not a guess from the name (see `docs/character-classes.md`) — it's a
  real gameplay flag (blocks the shield slot), not cosmetic. **Always verify the new item
  equipped on the player**, not just in the floating item catalog — the two don't share failure
  modes (a wrong local-axis convention or a scale inconsistent with a sibling tier can both pass
  a floating render and fail badly once bone-attached). See the `assets` skill's in-hand
  verification section (`render_held_items.gd`).
- **Tune where an item drops**: edit `MinDepth`/`MaxDepth`/`QuantityPerDepth` on its
  `LootTableItem` entry in the relevant loot table (`scenes/items/loot/`).
- **Make a new item spawnable from the in-game debug menu**: nothing to do — `DebugMenu`
  auto-discovers every `.tres` under each category folder (`scenes/items/weapons/`, `armor/`,
  `potions/`, `scrolls/`, `jewelry/`, `gold/`) recursively via `SpawnableCategories`. Deferred tools/keys are excluded. Authoring the item's
  `.tres` in the right folder is sufficient; don't hand-list it anywhere.
- **A class/race profile (future)**: author a new `StatProfile` (and starting attributes) and
  point `PlayerStats.Profile` at it.

After editing any `.tres`, validate it loads — see the `godot-mcp` skill's `inspect_resource.gd`.

## Authoring conventions

- Each `[GlobalClass]` resource (`Affix`, `AffixModifierRange`, `AffixPool`, `AffixRollCount`,
  `RarityWeight`, `RolledAffix`, `StatModifier`, `StatProfile`) lives in its own `.cs` file so
  `.tres` sub-resources reference an unambiguous script.
- Shared authored resources are definitions; `LootRoller` duplicates an equipable before stamping
  instance-specific rolls, so the authored `.tres` is never mutated at runtime.

## Item ownership, food, and persistence

Every carried/world item is a runtime Resource with definition provenance and an instance ID.
The initial backpack counts 20 entries including equipped gear; gold stays in stats. Potions and
scrolls explicitly stack to 20, identical variety only. Food/equipment never stack. Backpack UI
shows capacity and provides split/combine actions; failed pickup preserves its world source.

`PlayerInventoryController` coordinates fallible pickup, use, drop, and equipment commands.
Use applies one effect, removes one unit, then notifies observers. Food definitions reference a
recovery buff and a poison effect; the dungeon creation policy rolls contamination per piece,
while fixed starting supplies stay wholesome. Food has no type-wide hidden identity.

`ItemSaveCodec` / `InventorySaveCodec` restore exact state without RNG. `LevelItemPersistence`
regenerates the initial floor and activated source rewards, then reapplies removals, overrides,
and player-added objects. Saves use exact version 7 with no migrations. Active timed buffs and
living-enemy movement/injuries are outside this save increment.

See the [delivered item lifecycle design](specs/item-lifecycle-plan.md) for contracts and editor
acceptance. Run `scenes/debug/item_lifecycle_harness.tscn` for the authored playground.

The [gameplay item catalog](item-catalog.md) lists active definitions, deliberate effect families,
category budgets, depth eligibility, and deferred mechanics. All 145 exported custom item models
have matching definitions/wrappers; legacy definitions remain available beside them.
