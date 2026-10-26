# Item Identification System

Status: current-system documentation. Future ideas, where included, are proposals rather than implemented behavior. See the [documentation index](README.md) for related systems and implementation plans. Shared RNG ownership and seed behavior are documented in [Randomness and Seeds](randomness-and-seeds.md).

## Overview

Rogue Gauntlet draws on the original *Rogue* for consumables: potions, scrolls,
rings, and wands have a hidden identity. Each run randomly assigns every item
**type** (e.g. "Healing", "Confusion") a disguising **appearance**: a model and a
name ("small crimson potion"). The player sees only the appearance until they discover the
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
- Inventory persists runtime items by their definition provenance and instance ID; the run `Seed` lives in
  `RunState` / `SaveGame`.
- `SignalBus.ItemConsumed(player, item)` already fires when a consumable is used.

Identification adds a **disguise layer** on top: a stable type id, a pool of
appearances, and a per-run service that maps types to appearances and tracks what
has been discovered.

## Core Types

- `ItemAppearance` (Resource): one disguise, made of a `Descriptor` ("crimson", "golden ruby") and the
  `Models` it is shown with, keyed by appearance form. Scrolls and jewelry have one model (form `""`).
  Potions have one per size (`small`, `medium`, `large`). The model is the disguise.
- `AppearancePool` (Resource): an ordered list of `ItemAppearance`s for one category
  ("potion", "scroll", "ring", "wand"). Authored once per category.
- `IdentifiableItem` (Resource): base for any item with a hidden identity. Sits
  between `BuffedItem` and the concrete consumable. Adds:
  - `TypeId`: stable identity key (e.g. `"potion.healing"`), independent of file path.
  - `IdentityCategory`: which `AppearancePool` to disguise with.
  - `TrueName`: shown once identified ("Potion of Healing").
  - `UnidentifiedNameTemplate`: e.g. `"small {descriptor} potion"` or `"{descriptor} ring"`.
    Equipment names are then led by their visible quality word: the Poor or Superior adjective, "magic"
    or "rare", and nothing for Normal.
  - `AppearanceForm`: which of the appearance's models this item shows, a potion's bottle size.
- `IdentificationService`: per-run state owned by the run's `RunState`. Resolves
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
- `GetDisplayName(item)`: `TrueName` when identified, otherwise the filled template
  ("small crimson potion", "golden ruby ring"). `ItemIdentity.ComposeDisplayName` adds the
  quality word, "cursed" and capitalization once ("Small crimson potion", "Shiny golden ruby ring").
- `GetIdentifiedTypeIds()` / `GetAssignmentDescriptors()`: the discovered ids and the
  current type→descriptor assignment, for persistence.

Discovery is driven by the player's own item operations: the inventory controller identifies
what it consumes or equips through the run, which re-emits so the inventory relabels. The scroll
of identify calls the same `IdentifyItemType` directly.

Identity is **type-level**, not instance-level, so item stacking and inventory
saving are unaffected: two unidentified potions of the same type stack normally,
and identifying one reveals the whole stack.

## Display Resolution

UI and world pickups ask the service for presentation instead of reading the
item's true `Name` or `Scene` directly. The guiding rule: **an item looks like its type's
run appearance for the whole run, identified or not; only its label changes.** Healing
might be a crimson bottle this run and a cobalt one the next. Identifying a "small inky
black potion" renames it "Small Potion of Poison" but leaves the bottle as it looked.

- **Inventory / labels**: `ItemIdentity.ResolveDisplayName(item)` returns the disguised
  descriptor name until identified, then the true name. It surfaces as the item slot's
  hover tooltip and the title of the inventory context menu. When a type is discovered,
  the run's `RunState` emits `SignalBus.ItemIdentified`; each `ItemSlotPanel` listens and
  relabels, so every stack of that type reveals at once. The model is the same
  either way.
- **World, preview and action-bar model**: `ItemIdentity.ResolveScene(item)` returns the
  run appearance's model for the item's `AppearanceForm`. `LootableItem`, `ItemSlotPanel`'s
  `Preview`, the action bar (`PlayerQuickItemController` → `ActionContainer`) and
  `BoneAttachmentManager` all show it, so world, inventory and labels stay consistent. Identity
  definitions leave their own `Scene` empty, so no model is tied to a type; pickup, drop and
  equip check the resolved model instead. `ItemCatalogTest` enforces this.
- **Running a scene directly**: `GameSession.Run` creates the default run on first use when none
  was started or loaded, and its `Identification` initializes itself from that seed on first use,
  so a directly run `main.tscn` shows disguises exactly like a run started from the menu, and
  nothing loads at boot. `ItemIdentity` reads the session's run for UI-wide display; gameplay
  reads the same run through its `Level`.

### Model and size: two independent channels

- **Model = per-run disguise.** Every category draws its appearances from Asset Studio
  models:
  - potions: 12 colors, each in three sizes;
  - scrolls: 12 color-and-rune models, paired with the NetHack-style labels;
  - rings and amulets: 12 material-and-gem models each.

  Each type gets a random appearance at run start. The model is deliberately *unrelated*
  to the effect; it is the only thing the player learns to associate with an effect, and
  only after identifying it. Jewelry descriptors read `{material} {gem}` ("golden ruby",
  "wooden jade"), and material is part of the disguise, never a sign of strength: a
  "wooden coral amulet" can turn out to be an "Amulet of Strength". Jewelry true names
  carry no material.
- **Quality = per-item strength (jewelry).** Like NetHack's per-ring enchantment, each ring or
  amulet rolls a visible [quality](stats-and-itemization.md#equipment-quality-affixes-and-enchantment):
  Poor and Superior scale its bonus, harmful types included, and Magic and Rare add affixes
  from the jewelry roll tables. The quality is never part of the disguise: its word leads the
  unidentified name and its color shows everywhere, so "shiny golden ruby ring" and "magic
  golden ruby ring" are the same unknown type in two qualities. One type cannot look different
  in different qualities, which is why each jewelry type has exactly one definition.
- **Size = potency (potions only).** A potion type has small, medium and large
  definitions that share one `TypeId`, so they share the run's color and differ only in
  bottle size and magnitude. Potency is authored entirely in the buff; there is no
  separate tier field.

There is no separate "unknown" model and no model swap on identification.

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

## Potion Effects

Potions follow Rogue and NetHack. Effects map onto the `Buff` model. A status with no
behavior of its own is a plain `Buff` carrying a [status condition](stats-and-itemization.md#buffs-and-status-conditions),
and the systems that care react to the condition. Add dedicated buff classes only when behavior
genuinely diverges. Every type comes in small, medium and large, and size sets the strength.

| Potion | Effect | Buff |
| --- | --- | --- |
| Healing | heals over a few seconds; cures blindness | `HealingBuff` with `CuredConditions` |
| Strength, Dexterity, Vitality, Intelligence | timed attribute boost | `StatModifierBuff` |
| Poison | damage over time | `PoisonBuff` + `poisoned` |
| Slowness | slows the whole game, monsters and hero alike | `TimeScaleBuff` (`TimeScaleComponent`) |
| Blindness | vision closes to a circle around the hero | `Buff` + `blinded` (blindness screen effect) |
| Confusion | movement staggers off course; faint poison overlay | `Buff` + `confused` (`ConfusionComponent`) |

Not yet implemented:
- Raise Level: XP accumulates, but nothing raises the character's level yet.
- Hallucination: fake monsters popping in and out of existence.
- Detect Monsters / Magic, See Invisible and Levitation: each needs its system first.

Good and bad outcomes sharing the same disguise pool is what creates the
risk/reward of drinking an unknown potion.

## The `IIdentifiable` Interface

Potions hang their identity fields off `IdentifiableItem : BuffedItem`. Jewelry needs the
same identity fields *and* `EquipableItem`'s slot/quality/affix machinery, but C# single
inheritance means `IdentifiableItem` and `EquipableItem` can't share a base class — both are
sibling branches of `BuffedItem`. `IIdentifiable` (`common/interfaces/IIdentifiable.cs`) is
the shared contract (`TypeId`, `IdentityCategory`, `TrueName`, `UnidentifiedNameTemplate`,
`HasIdentity`, `Name`) both branches implement, and `IdentificationService`/`ItemIdentity`
consume the interface instead of either concrete type. `IdentityCategory.Types` is typed as
the base `Item` (Godot cannot export a typed array of an interface); callers filter with
`.OfType<IIdentifiable>()`.

## Per-item knowledge

Type knowledge is per run; attribute and curse knowledge belong to each equipment instance, and
both are saved with the item:
- `EquipableItem.AttributesKnown` is false for Magic and Rare items when they drop. It is true for
  every other class, for definitions and for fixed creation.
- `EquipableItem.CurseStatusKnown` is false for every rolled item. See
  [curses](stats-and-itemization.md#equipment-quality-affixes-and-enchantment).

| Learned by | Type (run-wide) | This item's attributes | Curse status |
| --- | --- | --- | --- |
| Consuming a potion or scroll | ✓ | – | – |
| Wearing equipment | ✓ | ✓ | – |
| Trying to remove a worn cursed item | – | – | ✓ (cursed) |
| Scroll of Identify | ✓ | ✓ | ✓ |
| Scroll of Enchantment | – | ✓ | ✓ (lifted) |
| Scroll of Remove Curse | – | – | ✓ (lifted) |

Run-wide and per-item knowledge have separate owners:
- **Types:** `RunState.IdentifyItemType(item)` records type discovery. `PlayerInventoryController`
  calls it when an item is consumed (before `ItemConsumed` is emitted), when one is equipped
  (auto-equipped starting gear included) and when a targeted scroll is read far enough to pick a
  target; the scroll of identify calls it for its target. It emits
  `SignalBus.ItemIdentified(typeId)`, and every open item slot relabels.
- **One item:** each `EquipableItem` owns what is known about it.
  - `RevealAttributes()` runs from `OnEquipped`, from the scroll of identify and from enchanting.
  - `RevealCurseStatus()` runs from the scroll of identify, and from `PlayerInventoryController` when the
    player's own unequip, equip or drop is blocked by a curse.
  - `LiftCurse()` runs from Enchant and Remove Curse.

  Each emits the item's own `Changed`, which `ItemSlotPanel` follows to relabel just that slot.

`ItemIdentity` gates every UI surface that could leak hidden knowledge:
- `IsTypeKnown(item)` hides a jewelry, potion or scroll type behind its disguise.
- `AreStatsKnown(item)` additionally needs the item's own attributes, which unidentified Magic and
  Rare items hide along with their affixes and rolled value. It gates `ItemDetailsView`'s stat lines,
  which show "Unidentified" instead. The name is then just the quality word and base name: "Magic
  Broadsword", or "Magic Ring of Strength" once another ring revealed the type.
- `IsFullyIdentified(item)` also needs the curse status. It is what makes an item a valid target
  for the scroll of identify.

`EquipableItem.CurseStatusKnown` gates the "Cursed"/"Uncursed" tooltip line, the penalty lines and
the "Cursed" name prefix.

The quality is deliberately visible either way, so name color, the tooltip's quality line and
`ItemSlotPanel`'s tint use `ItemQualityPalette` without those checks.

## Scrolls: A Resource-Strategy Effect, Not A Buff

Scroll effects like identify, enchant, teleport, or summon aren't stat buffs, so they can't
reuse `BuffedItem.Buff`. `ScrollEffect : Resource` (`scenes/items/scrolls/ScrollEffect.cs`) is
an authored strategy exported on `Scroll : ConsumableItem`: `Apply(player)` for untargeted
effects (teleport, summon), or `RequiresTarget = true` plus `IsValidTarget`/`ApplyToTarget`
for effects that need a chosen inventory slot (identify, enchant, remove curse). Pure buff/debuff
scrolls (haste, protection, rage, weakness, vitality, clarity) need no `ScrollEffect` at all — they just set `Buff`,
exactly like a potion.

**Targeting flow**: reading a targeted scroll's "Read" button emits
`InventoryItemContextMenu.TargetedUseRequested` instead of consuming immediately.
`InventoryPanel` handles it: if nothing is a valid target, it shows a transient banner and
the scroll is *not* consumed; otherwise it identifies the scroll's own type (reading it far
enough to pick a target reveals what it is, even on cancel) and enters targeting mode via
`InventoryTargetRequest` — ineligible slots dim and disable their button
(`ItemSlotPanel.SetTargetingState`), `Esc` cancels, and confirming an eligible slot applies
`PlayerInventoryController.TryUse`, which revalidates both entries, applies the effect once, and then consumes the scroll. This ordering means a cancelled or no-target read
never wastes the scroll. Enchant reuses the identical mechanism with a different predicate.

## Jewelry

`IdentifiableEquipableItem : EquipableItem, IIdentifiable` (`scenes/items/IdentifiableEquipableItem.cs`)
is the ring/amulet base: it duplicates `IdentifiableItem`'s four identity exports (the
accepted cost of the sibling-branch split) and adds `IntrinsicModifiers` — a plain
`StatModifier[]` rather than a `Buff`, so two worn instances of the same ring type register
their stat contributions under distinct item-instance sources and reverse independently on
unequip. The authored values are the baseline; the item's `MagnitudeScale` scales them when
stats are built. Jewelry rolls no affixes. There are nine ring types (Strength, Dexterity,
Vitality, Mind, Health, Protection, Savagery, cursed Frailty and Lethargy) and seven amulet
types (Health, Mind, Protection, Strength, Swiftness, Vitality, cursed Dread), one definition
each in `scenes/items/jewelry/{ring,amulet}/`. Amulet baselines are about 1.5× a ring's,
because the neck has one slot and the hands two. Rings use `ValidSlots = LeftRing | RightRing` (192); amulets use `Neck` (32). World
models come from the run appearance (the Asset Studio rings and amulets in
`scenes/items/jewelry/ring/` and `amulet/`).

Wearing jewelry identifies its type and its own attributes for free: the inventory controller's
equip hook identifies the type, as its consume hook does for consumed items, and
`EquipableItem.OnEquipped` reveals the item's attributes.

## Rollout (complete)

1. **Framework**: `ItemAppearance`, `AppearancePool`, `IdentifiableItem`, `IIdentifiable`,
   `IdentificationService`; save field (version bump); type discovery on consume and equip.
2. **Potions**: `potion_appearances.tres` (11 colors × three sizes); nine types in three sizes
   each (see [Potion Effects](#potion-effects)).
3. **Scrolls**: `ScrollEffect`/`Scroll`, `scroll_appearances.tres` (gibberish titles),
   identify/enchant/remove-curse/teleport/summon effects, plus haste/protection/rage/weakness buff
   scrolls; the inventory targeting flow (`InventoryTargetRequest`, `InventoryPanel`,
   `ItemSlotPanel.SetTargetingState`) for the targeted effects.
4. **Jewelry**: `IdentifiableEquipableItem`, `ring_appearances.tres` /
   `amulet_appearances.tres`, 9 ring and 7 amulet types graded by visible quality,
   anti-leak UI gating via `ItemIdentity.IsIdentified`.
5. **Wands / Staves (future)**: material descriptors; behavior continues to come from the
   existing `MagicStaff` / attack-definition pipeline — not yet given hidden identity.
