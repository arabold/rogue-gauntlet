# Item Identification System

Current-system documentation for hidden item identities. Shared RNG ownership and seed behavior are documented in [randomness and seeds](randomness-and-seeds.md); quality, affixes and curses in [stats and itemization](stats-and-itemization.md).

## Overview

Rogue Gauntlet draws on the original *Rogue* for consumables and jewelry: potions, scrolls, rings and amulets have a hidden identity. Each run randomly assigns every item **type** (e.g. "Healing", "Confusion") a disguising **appearance**: a model and a name ("small crimson potion"). The player sees only the appearance until they discover the effect, by using or wearing the item or by reading a scroll of identify. Once a type is identified, every item of that type shows its true name for the rest of the run. Wands and staves have no type disguise: their spell is part of their visible name.

Identification is **per run**. The type→appearance assignment is derived deterministically from the run seed, so it re-randomizes on each new game and regenerates identically on load. The save records which types the player has discovered and the full type→appearance assignment, so a restored run reproduces the exact disguises (see [saving](#saving)).

The effect side is ordinary item data: a potion is a `ConsumableItem` carrying a `Buff`, a scroll may carry a `ScrollEffect` strategy instead, and jewelry is equipment with intrinsic modifiers. Identification adds a **disguise layer** on top: a stable type id, a pool of appearances, and a per-run service that maps types to appearances and tracks what has been discovered.

## Core types

- `ItemAppearance` (Resource): one disguise, made of a `Descriptor` ("crimson", "golden ruby") and the `Models` it is shown with, keyed by appearance form. Scrolls and jewelry have one model (form `""`); potions have one per size (`small`, `medium`, `large`). The model is the disguise. Scroll appearances also pair a `GlyphId`, `GlyphMesh` and luminous `GlyphColor`, which the [scroll-reading ring](world-effects.md#scroll-reading-preview) shows; the glyph follows the appearance, not the scroll's type.
- `AppearancePool` (Resource): an ordered list of `ItemAppearance`s for one category (potion, scroll, ring, amulet) in [`scenes/items/identity/`](../scenes/items/identity/). It must hold at least as many appearances as the category has types; each holds 12 today.
- `IdentityCatalog` (`identity_catalog.tres`): every active hidden type per category.
- `IIdentifiable` (`common/interfaces/IIdentifiable.cs`): the identity contract, `TypeId` (stable key such as `"potion.healing"`, independent of file path), `IdentityCategory`, `TrueName` and `UnidentifiedNameTemplate` (e.g. `"small {descriptor} potion"`), plus `AppearanceForm`, which of the appearance's models the item shows. `IdentifiableItem : BuffedItem` implements it for potions and scrolls, and `IdentifiableEquipableItem : EquipableItem` for jewelry; C# single inheritance keeps them sibling branches, so `IdentificationService` and `ItemIdentity` consume the interface. `IdentityCategory.Types` is typed as the base `Item` because Godot cannot export a typed array of an interface; callers filter with `.OfType<IIdentifiable>()`.
- `IdentificationService`: per-run state owned by the run's `RunState`. It shuffles each pool against the run seed to assign appearances, resolves an item's appearance and display name, and records discovered types.

## Discovery

Discovery is driven by the player's own item operations. `RunState.IdentifyItemType(item)` records a type and emits `SignalBus.ItemIdentityRevealed` with the disguised and true names, which the [message feed](user-interface.md#message-feed-and-notes) announces ("Small murky potion is Small Potion of Healing"), and `SignalBus.ItemIdentified`, which relabels every open item slot. `PlayerInventoryController` calls it when an item is consumed (before `ItemConsumed` is emitted), when one is equipped (auto-equipped starting gear included) and when a targeted scroll is read far enough to pick a target; the scroll of identify calls it for its target. A class's starting consumables are known from the start (`PreIdentifiedTypeIds`): the Mage knows its healing potions.

Identity is **type-level**, not instance-level, so stacking and inventory saving are unaffected: two unidentified potions of the same type stack normally, and identifying one reveals the whole stack.

## Display resolution

UI and world pickups ask for presentation instead of reading the item's true `Name` or `Scene` directly. The guiding rule: **an item looks like its type's run appearance for the whole run, identified or not; only its label changes.** Healing might be a crimson bottle this run and a cobalt one the next. Identifying a "small inky black potion" renames it "Small Potion of Poison" but leaves the bottle as it looked.

- **Labels:** `ItemIdentity.ResolveDisplayName(item)` returns the disguised name until the type is identified, then the true name; `ItemIdentity.ComposeDisplayName` adds the quality word, "Cursed" and capitalization once ("Small crimson potion", "Shiny golden ruby ring"). It surfaces in the run menu, the shop and the log.
- **Models:** `ItemIdentity.ResolveScene(item)` returns the run appearance's model for the item's `AppearanceForm`. World pickups (`LootableItem`), item icons, the quick slots and `BoneAttachmentManager` all show it. Identity definitions leave their own `Scene` empty, so no model is tied to a type; pickup, drop and equip check the resolved model instead. `ItemCatalogTest` enforces this.
- **Running a scene directly:** `GameSession.Run` creates a run on first use when none was started or loaded, and its identification initializes from that seed on first use, so a directly run `main.tscn` shows disguises exactly like a run started from the menu. `ItemIdentity` reads the session's run for UI-wide display; gameplay reads the same run through its `Level`.

### Model and size: two independent channels

- **Model = per-run disguise.** Every category draws its appearances from Asset Studio models: potions in 12 colors, each in three sizes; scrolls as 12 color-and-rune models with NetHack-style labels; rings and amulets as 12 material-and-gem models each. A potion's color is the only cue for its type, so no two may read alike; an Asset Studio test enforces a minimum distance between liquid tints. The model is deliberately *unrelated* to the effect; it is the only thing the player learns to associate with an effect, and only after identifying it. Jewelry descriptors read `{material} {gem}` ("golden ruby", "wooden jade"), and material is part of the disguise, never a sign of strength: a "wooden coral amulet" can turn out to be an "Amulet of Strength". Jewelry true names carry no material.
- **Quality = per-item strength (jewelry).** Like NetHack's per-ring enchantment, each ring or amulet rolls a visible [quality](stats-and-itemization.md#equipment-quality-affixes-and-enchantment): Poor and Superior scale its bonus, harmful types included, and Magic and Rare add affixes from the jewelry roll tables. The quality is never part of the disguise: its word leads the unidentified name and its color shows everywhere, so "shiny golden ruby ring" and "magic golden ruby ring" are the same unknown type in two qualities. A type's tiers (1–5, deeper ones giving more) share its appearance and name.
- **Size = potency (potions only).** A potion type has small, medium and large definitions that share one `TypeId`, so they share the run's color and differ only in bottle size and magnitude. Potency is authored entirely in the buff.

There is no separate "unknown" model and no model swap on identification.

## Saving

`SaveGame` stores an `IdentificationSaveData`: the discovered type ids and the full `TypeId → descriptor` assignment. A restored save therefore reproduces the exact disguise every item had, even if the catalog changes; the seed-derived shuffle remains the baseline only for types the save does not name. Items persist by definition path as usual.

## Potion effects

Potions follow Rogue and NetHack. Effects map onto the `Buff` model. A status with no behavior of its own is a plain `Buff` carrying a [status condition](stats-and-itemization.md#buffs-and-status-conditions), and the systems that care react to the condition. Add dedicated buff classes only when behavior genuinely diverges. Every type comes in small, medium and large, and size sets the strength.

| Potion | Effect | Buff |
| --- | --- | --- |
| Healing | heals over a few seconds; cures blindness and poison | `HealingBuff` with `CuredConditions` |
| Strength, Dexterity, Vitality, Intelligence | timed attribute boost | `StatModifierBuff` |
| Poison | damage over several seconds, less than healing of its size restores | `PoisonBuff` + `poisoned` |
| Slowness | slows the whole game, monsters and hero alike | `TimeScaleBuff` (`TimeScaleComponent`) |
| Blindness | vision closes to a circle around the hero | `Buff` + `blinded` (blindness screen effect) |
| Confusion | movement staggers off course; faint poison overlay | `Buff` + `confused` (`ConfusionComponent`) |

Good and bad outcomes sharing the same disguise pool is what creates the risk and reward of drinking an unknown potion. Not yet in the catalog: Raise Level (leveling exists, the potion does not), Hallucination (fake monsters), and Detect Monsters or Magic, See Invisible and Levitation, each of which needs its system first.

## Per-item knowledge

Type knowledge is per run; attribute and curse knowledge belong to each equipment instance, and both are saved with the item:
- `EquipableItem.AttributesKnown` is false for Magic and Rare items when they drop. It is true for every other quality, for definitions and for fixed creation.
- `EquipableItem.CurseStatusKnown` is false for every rolled item. See [curses](stats-and-itemization.md#equipment-quality-affixes-and-enchantment).

| Learned by | Type (run-wide) | This item's attributes | Curse status |
| --- | --- | --- | --- |
| Consuming a potion or scroll | ✓ | – | – |
| Wearing equipment | ✓ | ✓ | ✓ |
| Trying to remove a worn cursed item | – | – | ✓ (cursed) |
| Scroll of Identify | ✓ | ✓ | ✓ |
| Scroll of Enchantment | – | ✓ | ✓ (lifted) |
| Scroll of Remove Curse | – | – | ✓ (lifted) |
| Buying from a shop | – | ✓ | ✓ (uncursed) |

Each `EquipableItem` owns what is known about it: `RevealAttributes()`, `RevealCurseStatus()`, `RevealAll()` (equipping, the scroll of identify, shop stock) and `LiftCurse()` (Enchant and Remove Curse). Each emits the item's own `Changed`, which `ItemSlotPanel` follows to relabel just that slot.

`ItemIdentity` gates every UI surface that could leak hidden knowledge:
- `IsTypeKnown(item)` hides a jewelry, potion or scroll type behind its disguise.
- `AreStatsKnown(item)` additionally needs the item's own attributes, which unidentified Magic and Rare items hide along with their affixes and rolled value. It gates `ItemDetailsView`'s stat lines, which show "Unidentified" instead. The name is then just the quality word and base name: "Magic Broadsword", or "Magic Ring of Strength" once another ring revealed the type.
- `IsFullyIdentified(item)` also needs the curse status. It is what makes an item a valid target for the scroll of identify.

`EquipableItem.CurseStatusKnown` gates the "Cursed"/"Uncursed" line under the item's name, the penalty lines, the "Cursed" name prefix, and the curse aura on world, equipped and inventory models (see [world effects](world-effects.md#item-auras)). The quality is deliberately visible either way, so name color, the quality line and `ItemSlotPanel`'s tint use `ItemQualityPalette` without those checks. Shop prices follow the same gates (see [shops](shops.md#prices)).

## Scrolls and targeting

Scroll effects like identify, enchant, teleport or summon aren't stat buffs, so they can't reuse `BuffedItem.Buff`. `ScrollEffect : Resource` (`scenes/items/scrolls/ScrollEffect.cs`) is an authored strategy exported on `Scroll : ConsumableItem`: `Apply(player)` for untargeted effects (teleport, summon), or `RequiresTarget = true` plus `IsValidTarget`/`ApplyToTarget` for effects that need a chosen inventory slot (identify, enchant, remove curse). Pure buff and debuff scrolls (haste, protection, rage, weakness, vitality, clarity) need no `ScrollEffect`; they just set `Buff`, exactly like a potion.

**Targeting flow:** **Read** on a targeted scroll in the run menu's Pack tab does not consume it immediately. `PackTab` handles it: if nothing is a valid target, it shows a banner and the scroll is *not* consumed; otherwise it identifies the scroll's own type (reading it far enough to pick a target reveals what it is, even on cancel) and enters targeting mode. Ineligible slots dim (`ItemSlotPanel.SetTargetingState`), cancel backs out, and confirming an eligible slot calls `PlayerInventoryController.TryUse`, which revalidates both entries, applies the effect once, and then consumes the scroll. A cancelled or no-target read never wastes the scroll.

## Jewelry

`IdentifiableEquipableItem : EquipableItem, IIdentifiable` (`scenes/items/IdentifiableEquipableItem.cs`) is the ring and amulet base. It duplicates `IdentifiableItem`'s identity exports (the accepted cost of the sibling-branch split) and adds `IntrinsicModifiers`, a plain `StatModifier[]` rather than a `Buff`, so two worn instances of the same ring type register their contributions under distinct item-instance sources and reverse independently on unequip. The authored values are the tier's baseline; the item's rolled `MagnitudeScale` scales them and its quality's affixes add to them. There are nine ring types (two worn at once) and seven amulet types (one neck slot), listed in the [catalog](item-catalog.md#jewelry-16-types-68-definitions); amulets give more per piece than rings for that reason. World models come from the run appearance.

Wearing jewelry identifies its type, attributes and curse status for free, as consuming identifies a potion.
