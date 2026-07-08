# Item Identification System

## Overview

Rogue Gauntlet draws on the original *Rogue* for consumables: potions, scrolls,
rings, and wands have a hidden identity. Each run randomly assigns every item
**type** (e.g. "Healing", "Confusion") a disguising **appearance** (e.g. "fizzy
crimson potion"). The player sees only the appearance until they discover the
effect — by using the item, or later by reading a scroll of identify. Once a type
is identified, every item of that type is shown by its true name for the rest of
the run.

This document describes the identification framework. Potions, scrolls, and jewelry
(rings/amulets) all use it today, each with its own `AppearancePool`; wands/staves
are the remaining category intended to reuse the same pieces.

Identification is **per run**. The type→appearance assignment is derived
deterministically from the run `Seed`, so it re-randomizes on each new game and
regenerates identically on load. The save records which type ids the player has
discovered **and** the full type→appearance assignment, so a restored run reproduces
the exact disguises even if the catalog changes; the seed-derived shuffle is the
baseline for any types not present in the save (see [Save Schema](#save-schema)).

## How it builds on existing systems

The effect side already exists and is unchanged:

- A consumable is an authored `Resource` (`.tres`): `ConsumableItem` → `BuffedItem`
  → `Item`. It carries a `Buff`.
- `Buff` / `StatModifierBuff` / `PeriodicBuff` (`HealingBuff`, `PoisonBuff`) define the
  effect through `OnApply` / `OnTick` / `OnRemove`, applied via `BuffController`. See
  [stats & itemization](stats-and-itemization.md) for the modifier model.
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

- `Initialize(runSeed, identifiedTypeIds = null, persistedAssignments = null)`: loads
  the catalog and builds the type→appearance map for every category at run start. On
  load, pass the discovered type ids and the persisted descriptor assignments to
  restore exactly what the player saw.
- `GetAppearance(item)`: the disguise assigned to the item's type, or null.
- `IsIdentified(item)` / `Identify(typeId)`: read/record discovery (`Identify` returns
  true when the type was newly discovered).
- `GetDisplayName(item)`: `TrueName` when identified, otherwise the templated
  descriptor ("fizzy crimson potion").
- `GetIdentifiedTypeIds()` / `GetAssignmentDescriptors()`: the discovered ids and the
  current type→descriptor assignment, for persistence.

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
| Strength | permanent damage up | `StatModifierBuff` (Duration 0, exists) |
| Haste Self | temporary speed up | `StatModifierBuff` (exists) |
| Poison | damage over time (bad) | `PoisonBuff` (exists) |
| Confusion | scramble input | `ConfusionBuff` (new) |
| Blindness | reduce vision radius | `BlindnessBuff` (new) |
| Levitation | ignore floor traps | `LevitationBuff` (new) |
| Detect Monsters / Magic | reveal on minimap | instantaneous, via `SignalBus` |
| Raise Level | grant XP | instantaneous |

Good and bad outcomes sharing the same disguise pool is what creates the
risk/reward of drinking an unknown potion.

## The `IIdentifiable` Interface

Potions hang their identity fields off `IdentifiableItem : BuffedItem`. Jewelry needs the
same identity fields *and* `EquipableItem`'s slot/rarity/affix machinery, but C# single
inheritance means `IdentifiableItem` and `EquipableItem` can't share a base class — both are
sibling branches of `BuffedItem`. `IIdentifiable` (`common/interfaces/IIdentifiable.cs`) is
the shared contract (`TypeId`, `IdentityCategory`, `TrueName`, `UnidentifiedNameTemplate`,
`HasIdentity`, `Name`) both branches implement, and `IdentificationService`/`ItemIdentity`
consume the interface instead of either concrete type. `IdentityCategory.Types` is typed as
the base `Item` (Godot cannot export a typed array of an interface); callers filter with
`.OfType<IIdentifiable>()`.

`GameSession.IdentifyItemType(item)` is the single hub that records discovery and emits
`SignalBus.ItemIdentified` — reading a scroll (`ItemConsumed`) and wearing jewelry
(`ItemEquipped`) both funnel through it, so a scroll of identify calling it directly is the
same code path.

`ItemIdentity.IsIdentified(item)` (true for non-identifiable items, or once discovered) gates
every UI surface that could otherwise leak a rolled item's rarity or stats before it's
identified: `RarityPalette.TextColor`, `ItemDetailsView`'s rarity/affix/intrinsic-modifier
lines, and `ItemSlotPanel`'s rarity background tint all check it before showing anything
beyond the disguised name.

## Scrolls: A Resource-Strategy Effect, Not A Buff

Scroll effects like identify, enchant, teleport, or summon aren't stat buffs, so they can't
reuse `BuffedItem.Buff`. `ScrollEffect : Resource` (`scenes/items/scrolls/ScrollEffect.cs`) is
an authored strategy exported on `Scroll : ConsumableItem`: `Apply(player)` for untargeted
effects (teleport, summon), or `RequiresTarget = true` plus `IsValidTarget`/`ApplyToTarget`
for effects that need a chosen inventory slot (identify, enchant). Pure buff/debuff scrolls
(haste, protection, rage, weakness) need no `ScrollEffect` at all — they just set `Buff`,
exactly like a potion.

**Targeting flow**: reading a targeted scroll's "Read" button emits
`InventoryItemContextMenu.TargetedUseRequested` instead of consuming immediately.
`InventoryPanel` handles it: if nothing is a valid target, it shows a transient banner and
the scroll is *not* consumed; otherwise it identifies the scroll's own type (reading it far
enough to pick a target reveals what it is, even on cancel) and enters targeting mode via
`InventoryTargetRequest` — ineligible slots dim and disable their button
(`ItemSlotPanel.SetTargetingState`), `Esc` cancels, and confirming an eligible slot applies
the effect and *then* consumes the scroll. This ordering means a cancelled or no-target read
never wastes the scroll. Enchant reuses the identical mechanism with a different predicate.

## Jewelry

`IdentifiableEquipableItem : EquipableItem, IIdentifiable` (`scenes/items/IdentifiableEquipableItem.cs`)
is the ring/amulet base: it duplicates `IdentifiableItem`'s four identity exports (the
accepted cost of the sibling-branch split) and adds `IntrinsicModifiers` — a plain
`StatModifier[]` rather than a `Buff`, so two worn instances of the same ring type register
their stat contributions under distinct item-instance sources and reverse independently on
unequip. Rings use `ValidSlots = LeftRing | RightRing` (192); amulets use `Neck` (32). World
models are authored primitives (`TorusMesh`/`SphereMesh` in `scenes/items/jewelry/`) since no
ring/amulet art exists in the KayKit packs — untextured primitives still tint correctly via
`ItemIdentity`'s flat-color fallback when a surface has no albedo texture.

Wearing jewelry identifies it for free: `GameSession` subscribes
`SignalBus.ItemEquipped += (player, item) => IdentifyItemType(item)` in `_Ready`, so no
jewelry-specific wiring was needed beyond the existing consume-identifies-it hook.

## Rollout (complete)

1. **Framework**: `ItemAppearance`, `AppearancePool`, `IdentifiableItem`, `IIdentifiable`,
   `IdentificationService`; save field (version bump); `GameSession.IdentifyItemType` wired to
   both `ItemConsumed` and `ItemEquipped`.
2. **Potions**: `potion_appearances.tres`; healing/haste/poison/regeneration/slowness potions
   carry a `TypeId`.
3. **Scrolls**: `ScrollEffect`/`Scroll`, `scroll_appearances.tres` (gibberish titles),
   identify/enchant/teleport/summon effects, plus haste/protection/rage/weakness buff scrolls;
   the inventory targeting flow (`InventoryTargetRequest`, `InventoryPanel`,
   `ItemSlotPanel.SetTargetingState`) for the two targeted effects.
4. **Jewelry**: `IdentifiableEquipableItem`, `ring_appearances.tres` /
   `amulet_appearances.tres`, 10 rings + 4 amulets, anti-leak UI gating via
   `ItemIdentity.IsIdentified`.
5. **Wands / Staves (future)**: material descriptors; behavior continues to come from the
   existing `MagicStaff` / attack-definition pipeline — not yet given hidden identity.
