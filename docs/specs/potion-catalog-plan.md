# Potion Catalog and Model Appearances — Program Design

Status: **implemented**. Slices 1–4 below have landed; only the items under [limitations and follow-ups](#limitations-and-follow-ups) remain. The current behavior is described in [item identification](../item-identification-system.md), [status conditions](../stats-and-itemization.md#buffs-and-status-conditions), [screen effects](../screen-effects.md) and the [item catalog](../item-catalog.md). This plan records the decisions behind it: it replaced the tint disguise with per-run model appearances for every identity category, rebuilt the potion catalog around Rogue/NetHack effects, and added blindness, confusion and slowness.

## Decisions

- **Only the Asset Studio models are used.** Potions use the 36 models in `assets/custom-items/potions/` (12 colors × small, medium and large): 11 colors are disguises and silver is the neutral fallback bottle. Legacy potions, including their KayKit bottles, are removed.
- **The model is the disguise; tint is removed.** Every identity category (potion, scroll, ring, amulet) assigns each type a random appearance per run. The appearance is a descriptor plus the model to show, and the type keeps it for the whole run. `ItemAppearance.TintColor`, `ItemIdentity.ResolveTint`/`ApplyTint` and `item_tint.gdshader` are deleted.
- **Size is strength, for potions only.** A potion type has small, medium and large definitions that share one `TypeId`, so they share the run's color and differ only in bottle size and magnitude. Jewelry and scrolls have one model per appearance.
- **Jewelry material is part of the disguise, not the tier.** A "red wooden amulet" is revealed as an "Amulet of Strength". Material words leave jewelry true names; the tier stays in data and stats only.
- **Potion set:** Healing, Strength, Dexterity, Vitality, Intelligence, Poison, Slowness, Blindness and Confusion.
  - Dropped: Haste, Regeneration (a duplicate of Healing), Protection, Evasion and Precision.
  - Deferred: Raise Level, because XP accumulates but nothing raises the level yet (`PlayerStats.LevelUp` has no callers). Hallucination and the detection potions also wait for their systems.
- **Slowness slows the whole game** through `Engine.TimeScale`, so the player, monsters and animations all slow down together.
- **Confusion** scrambles movement directions and also shows a mild version of the poison overlay.
- **Healing cures blindness**, as in Rogue.

## Contracts

```csharp
// ~ scenes/items/identity/ItemAppearance.cs (TintColor removed)
[Export] public string Descriptor { get; set; }
/// Model per appearance form: "" for single-model categories, "small"/"medium"/"large" for potions.
[Export] public Godot.Collections.Dictionary<string, PackedScene> Models { get; set; }
public PackedScene GetModel(string form);

// ~ common/interfaces/IIdentifiable.cs
string AppearanceForm { get; }   // IdentifiableItem exports it; IdentifiableEquipableItem returns ""

// ~ scenes/items/identity/ItemIdentity.cs (tint API removed)
/// The model to show: the run's appearance model for the item's form, else the definition's Scene.
public static PackedScene ResolveScene(Item item);

// ~ scenes/buffs/Buff.cs: no longer abstract; a plain Buff is a status with only a Condition.
// + scenes/buffs/TimeScaleBuff.cs : Buff
[Export(PropertyHint.Range, "0.1,1,0.05")] public float TimeScale { get; set; } = 0.5f;
// ~ scenes/buffs/Buff.cs
[Export] public Array<StatusCondition> CuredConditions { get; set; } // ended when this buff is applied

// ~ scenes/player/BuffController.cs: one notification; conditions derived from the active buffs
[Signal] public delegate void BuffsChangedEventHandler();   // once per apply/remove/expiry/clear
public bool HasCondition(StatusCondition condition);       // any active buff carries it

// + scenes/player/TimeScaleComponent.cs (child of player.tscn): Engine.TimeScale = slowest active TimeScaleBuff; 1 on exit
// + scenes/player/ConfusionComponent.cs (child of player.tscn): while confused, rotates InputComponent.InputDirection
//   by a wandering angle; runs at CombatPhysics.InputModifier, between Input and Intent.

// ~ scenes/effects/screen/StatusScreenEffectDriver.cs
/// Condition → intensity; the effect follows the strongest active condition.
[Export] public Godot.Collections.Dictionary<StatusCondition, float> Conditions { get; set; }
```

`ResolveScene` replaces the tint at every display site: `LootableItem`, `ItemSlotPanel`/`Preview`, the action bar (`PlayerQuickItemController` → `ActionContainer`) and `BoneAttachmentManager`. The definition's `Scene` remains the fallback for the editor and for tests that run without a session.

## Content

- **Potion definitions** move to `scenes/items/potions/potion_<type>_<size>.tres`. Existing families are renamed with their UIDs and values kept. Their fallback `Scene` is the same neutral model for every type of a size, so no definition encodes a type color. `UnidentifiedNameTemplate` is "small {descriptor} potion" (and medium, large).
- **Models** stay in `potion_small/`, `potion_medium/` and `potion_large/` and are referenced only by `potion_appearances.tres`: 12 appearances, each with its three sizes.
- **New potions:**
  - Blindness: plain `Buff`, `blinded`, 8/12/16 s.
  - Confusion: plain `Buff`, `confused`, 6/9/12 s.
  - Slowness: `TimeScaleBuff` 0.75/0.6/0.45 for 6/8/10 s of game time. It replaces the old speed-stat debuff.
- **Healing** cures `blinded`.
- **Scrolls:** the 12 scroll models become the appearances of the existing labels ("scroll labeled ZELGO MER"). Definitions and their files are unchanged.
- **Rings and amulets:** each pool gets its 12 models with color-and-material descriptors ("red wooden", "blue silver"). True names drop the material ("Gold Ring of Strength" becomes "Ring of Strength").
- **Loot, identity catalog, starting gear:** remove dropped potions; add blindness and confusion at the existing size depth bands (small 1–5, medium 3–9, large 7+); repoint `class_mage` and the item lifecycle harness to the renamed healing potion.
- **Screen effects:** `BlindnessDriver {blinded: 1}`; `PoisonDriver {poisoned: 1, confused: 0.35}`.
- **Saves:** descriptors, catalog and loot change, so `SaveGame.CurrentVersion` goes from 13 to 14.

## Slices

Hybrid delivery: one shared mechanism first, then content, then effects. The game stays playable after each slice.

1. **Model appearances.**
   - Change: `ItemAppearance.Models`, `AppearanceForm`, `ResolveScene` at every display site, tint removed, the four pools rewritten, jewelry names, save version.
   - Risk first: confirm that a hand-authored typed `Dictionary` export loads from `.tres`.
   - Tests: `ItemCatalogTest` (each custom model in an identity category is used by exactly one appearance; scrolls and jewelry still have their definitions) and an appearance test (distinct models per type; potion forms resolve per size).
2. **Potion catalog.**
   - Change: rename kept definitions, delete legacy and dropped potions and unreferenced KayKit bottles, update loot, catalog, class and harness references and test paths.
   - Tests: the loot coverage and identity tests pass unchanged.
3. **New effects.**
   - Change: `Buff` concrete, `TimeScaleBuff` with `TimeScaleComponent`, `ConfusionComponent`, multi-condition screen driver, Healing cures, the new definitions and conditions, player scene wiring.
   - Tests: confusion deflection and its reset, time scale with overlapping slowness and reset on removal, healing cure, wiring of blindness and confusion in the gameplay scene.
   - Visual check: windowed harness.
4. **Docs.** Item identification (appearance = model), item catalog potions and effects, screen effects, status conditions, and the index. The wiring plan is retired.

## Limitations and follow-ups

- **Merge conflicts with your main checkout:** the uncommitted scroll and jewelry edits there (deleted models such as `ring_gold_signet`) will conflict with the rewritten pools. After merging, drop the appearances whose models were deleted. Each pool must still have at least as many appearances as types.
- **Slowness and buff timers:** slowness durations count game time, so at 0.5 time scale a 10 s slowness lasts 20 s of real time.
- **Deferred:** Raise Level until character leveling exists. Hallucination (fake monsters popping in and out), detection potions, levitation and see invisible wait for their systems.
