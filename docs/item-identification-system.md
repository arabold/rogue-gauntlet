# Item Identification System

## Overview

Rogue Gauntlet draws on the original *Rogue* for consumables: potions, scrolls,
rings, and wands have a hidden identity. Each run randomly assigns every item
**type** (e.g. "Healing", "Confusion") a disguising **appearance** (e.g. "fizzy
crimson potion"). The player sees only the appearance until they discover the
effect — by using the item, or later by reading a scroll of identify. Once a type
is identified, every item of that type is shown by its true name for the rest of
the run.

This document describes the identification framework. Potions are the first
category to use it; scrolls, rings, and wands are intended to reuse the same
pieces by swapping the appearance pool.

Identification is **per run**. The type→appearance assignment is derived
deterministically from the run `Seed`, so it re-randomizes on each new game and
regenerates identically on load. The save only needs to record which type ids the
player has discovered.

## How it builds on existing systems

The effect side already exists and is unchanged:

- A consumable is an authored `Resource` (`.tres`): `ConsumableItem` → `BuffedItem`
  → `Item`. It carries a `Buff`.
- `Buff` / `PeriodicBuff` (`HealingBuff`, `PoisonBuff`, `SpeedBuff`, …) define the
  effect through `OnApply` / `OnTick` / `OnRemove`, applied via `BuffController`.
- Inventory persists items by `ResourcePath`; the run `Seed` lives in
  `GameSession` / `SaveGame`.
- `SignalBus.ItemConsumed(player, item)` already fires when a consumable is used.

Identification adds a **disguise layer** on top: a stable type id, a pool of
appearances, and a per-run service that maps types to appearances and tracks what
has been discovered.

## Core Types

- `ItemAppearance` (Resource): one disguise — a `Descriptor` ("fizzy crimson") and a
  `TintColor`. The item's own mesh is recolored with `TintColor` while unidentified,
  which reads as a differently colored bottle without new art.
- `AppearancePool` (Resource): an ordered list of `ItemAppearance`s for one category
  ("potion", "scroll", "ring", "wand"). Authored once per category.
- `IdentifiableItem` (Resource): base for any item with a hidden identity. Sits
  between `BuffedItem` and the concrete consumable. Adds:
  - `TypeId`: stable identity key (e.g. `"potion.healing"`), independent of file path.
  - `IdentityCategory`: which `AppearancePool` to disguise with.
  - `TrueName`: shown once identified ("Potion of Healing").
  - `UnidentifiedNameTemplate`: e.g. `"{descriptor} potion"`.
- `IdentificationService`: per-run state owned by `GameSession`. Resolves
  appearances and tracks discovery.

### Appearance assignment

`AppearancePool` ordering plus the run `Seed` produce a deterministic shuffle that
assigns each `TypeId` an `ItemAppearance`. Because it is seed-derived, the
assignment is identical on every load of the same run and re-randomized for a new
run. The pool must have at least as many appearances as there are types in the
category.

## Identification Service

`IdentificationService` exposes:

- `Initialize(runSeed, allTypes, pool)`: builds the type→appearance map for a
  category at run start (or load).
- `GetAppearance(typeId)`: the disguise for a type.
- `IsIdentified(typeId)` / `Identify(typeId)`: read/record discovery.
- `GetDisplayName(item)`: `TrueName` when identified, otherwise the templated
  descriptor ("fizzy crimson potion").

Discovery is driven by existing events: `GameSession` (or a small listener) hooks
`SignalBus.ItemConsumed`, calls `Identify(item.TypeId)`, and re-emits so the
inventory relabels. A future "scroll of identify" calls `Identify` directly.

Identity is **type-level**, not instance-level, so item stacking and inventory
saving are unaffected: two unidentified potions of the same type stack normally,
and identifying one reveals the whole stack.

## Display Resolution

UI and world pickups ask the service for presentation instead of reading the
item's true `Name` directly. The guiding rule: **an item's model never changes — only
its label does.** A potion is shown as its authored bottle, tinted with the per-run
disguise colour, for the whole run; identifying it swaps the name from
"inky black potion" to "Potion of Poison" but leaves the bottle exactly as it looked.

- **Inventory / labels**: `ItemIdentity.ResolveDisplayName(item)` returns the disguised
  descriptor name until identified, then the true name. It surfaces as the item slot's
  hover tooltip and the title of the inventory context menu. When a type is discovered,
  `GameSession` emits `SignalBus.ItemIdentified`; each `ItemSlotPanel` listens and
  relabels, so every stack of that type reveals at once. The model/tint is the same
  either way.
- **World + preview model**: an item keeps its own `Scene` (its bottle), and
  `ItemIdentity.ResolveTint(item)` returns the per-run appearance `TintColor` — applied
  whether or not the item is identified, by `LootableItem`/`Preview`. This keeps world,
  inventory preview, and labels consistent.
- **Tinting** (`ItemIdentity.ApplyTint`): the KayKit bottles are a single mesh sharing
  one baked atlas material, so there is no separate "liquid" surface to recolor and a
  flat-colour override would erase all surface detail. Instead a small spatial shader
  (`item_tint.gdshader`) keeps the original albedo texture's luminance for light/dark
  detail and multiplies in the disguise hue. It is assigned as a per-surface override,
  leaving the `MaterialOverride`/`MaterialOverlay` slots free (the pickup highlight uses
  the overlay).

### Shape vs colour: two independent channels

- **Colour = per-run disguise.** Each type is assigned a random appearance tint at run
  start (reset every run). The colour is deliberately *unrelated* to the effect — it is
  pure disguise, the only thing the player learns to associate with an effect, and only
  after identifying it.
- **Shape = potency, authored via the bottle.** Bottles come in three sizes —
  `bottle_A` (small), `bottle_B` (medium), `bottle_C` (large). A potion's `Scene`
  simply uses the bottle that matches its strength, so silhouette hints at potency
  without revealing the effect. Same-effect variants of different potency share one
  `TypeId` (so they share the run colour) and differ only by bottle size and magnitude
  — e.g. small/medium/large healing all show the same mystery colour in different
  sizes. Potency is authored entirely in the scene/buff; there is no separate tier
  field.

The bottle is tinted at runtime, so a single green base model yields every disguise
colour; there is no separate "unknown" model and no model swap on identification.

## Save Schema

`SaveGame` gains an additive, versioned field (bump `SaveGame.CurrentVersion`; old
saves default to "nothing identified"):

```csharp
public sealed class IdentificationSaveData
{
    public List<string> IdentifiedTypeIds { get; set; } = [];
    public List<AppearanceAssignmentSaveData> Assignments { get; set; } = [];
}
```

The discovered type ids **and** the full `TypeId → AppearanceDescriptor` assignment
are stored, so a restored save always reproduces the exact disguise — and therefore
effect — every potion had, even if the catalog changes in a later game version. The
seed-derived deterministic shuffle remains as a baseline only for types not present
in the save (newly added content). Items already persist by `ResourcePath`; nothing
about inventory saving changes.

## Effects To Grow Into

Effects map onto the existing `Buff` model. A generic `StatModifierBuff` (a stat
field plus a delta, applied on `OnApply` and reversed on `OnRemove`) covers most
buff/debuff potions without a dedicated class each. Add dedicated buff classes only
when behavior genuinely diverges.

| Potion | Effect | Buff |
| --- | --- | --- |
| Healing (S/M/L) | restore health over time | `HealingBuff` (exists) |
| Extra Healing | heal + raise max health | `HealingBuff` + `StatModifierBuff` |
| Strength | permanent damage up | `StatModifierBuff` (Duration 0) |
| Haste Self | temporary speed up | `SpeedBuff` (exists) |
| Poison | damage over time (bad) | `PoisonBuff` (exists) |
| Confusion | scramble input | `ConfusionBuff` (new) |
| Blindness | reduce vision radius | `BlindnessBuff` (new) |
| Levitation | ignore floor traps | `LevitationBuff` (new) |
| Detect Monsters / Magic | reveal on minimap | instantaneous, via `SignalBus` |
| Raise Level | grant XP | instantaneous |

Good and bad outcomes sharing the same disguise pool is what creates the
risk/reward of drinking an unknown potion.

## Reusing The Framework

Other Rogue categories reuse the same three pieces by swapping the
`AppearancePool`:

- **Scrolls**: appearance pool of random titles
  (`UnidentifiedNameTemplate = "scroll labeled {descriptor}"`); one-shot effects via
  an `IPlayerAction` consumable.
- **Rings**: gem descriptors; equip-time persistent buffs through `EquipableItem` +
  `BuffedItem`.
- **Wands / Staves**: material descriptors; behavior continues to come from the
  existing `MagicStaff` / attack-definition pipeline.

## Rollout

1. **Framework**: `ItemAppearance`, `AppearancePool`, `IdentifiableItem`,
   `IdentificationService`; save field (version bump); wire `ItemConsumed` →
   `Identify` and relabel UI/world.
2. **Content**: author a potion appearance pool; convert existing healing potions to
   carry a `TypeId`.
3. **Effects & loot**: add `StatModifierBuff`, `ConfusionBuff`, etc., new potion
   `.tres` files, and entries in the shared `LootTable` resources
   (`scenes/items/loot/`) that chests and enemies draw from.
4. **Generalize**: scrolls, then rings and wands.
