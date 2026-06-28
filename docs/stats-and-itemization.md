# Stats & Itemization System

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
- `LootRoller` (static): turns a dropped equipable definition into a rolled instance — it
  `Duplicate()`s the definition, records `SourceDefinitionPath`, rolls a rarity, rolls affixes for
  the item's slots/rarity, and returns the instance. Non-equipables and a missing pool pass
  through unchanged.

Rolling is seeded: `GameSession.CreateLootRng()` returns an RNG derived from the run/depth seed
and a persisted counter (`_lootRollsConsumed`), advancing each draw so successive drops differ and
the sequence resumes after a load. `LootTableComponent` draws its drop chance, item pick, and
affix rolls from that RNG, then rolls equipables through `LootRoller`. Exact fidelity across a
save does not depend on replaying the draw order — the rolled affixes are persisted on the item.

`EquipableItem.ComposeName(baseName)` builds the display name from the first prefix and first
suffix fragment ("Vicious Broadsword of the Bear"); extra affixes add stats but not name (like a
rare item). `ItemIdentity.ResolveDisplayName` applies it on top of the identification name.

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
  `Kind`, `AffixModifierRange`s, `AllowedSlots`, `Weight`, and `MinRarity`.
- **Tune drop rarity**: edit the `RarityWeights` (base + per-depth) and `RollCounts` on
  `affix_pool.tres`.
- **A class/race profile (future)**: author a new `StatProfile` (and starting attributes) and
  point `PlayerStats.Profile` at it.

After editing any `.tres`, validate it loads — see the `godot-mcp` skill's `inspect_resource.gd`.

## Authoring conventions

- Each `[GlobalClass]` resource (`Affix`, `AffixModifierRange`, `AffixPool`, `AffixRollCount`,
  `RarityWeight`, `RolledAffix`, `StatModifier`, `StatProfile`) lives in its own `.cs` file so
  `.tres` sub-resources reference an unambiguous script.
- Shared authored resources are definitions; `LootRoller` duplicates an equipable before stamping
  instance-specific rolls, so the authored `.tres` is never mutated at runtime.
