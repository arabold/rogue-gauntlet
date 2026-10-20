# Item Lifecycle — Program Design

Status: implemented milestone 1, October 19, 2026. This records the delivered code shape and the remaining acceptance boundaries. Written using the [program-design skill](../../.agents/skills/program-design/SKILL.md). Gameplay requirements live in the [GDD](../game-design.md#items-and-their-lifecycle); the [roadmap](../development-roadmap.md) separates later mechanics.

## Decisions

- Retain concrete Godot item Resources. Authored definitions and runtime copies use the existing item subclasses, distinguished by common provenance and identity. Equipment, effects, identification, previews, and action references keep using those Resources.
- Compose independent capabilities through `Buff`, `ScrollEffect`, `AffixPool`, creation policies, and local scene components. Shared strategies hold no actor state. Nodes own active effects, interaction, visuals, and scene lifetime.
- Put tunable creation rules in Resources and execution algorithms in plain C# helpers. There is no new autoload or gameplay registry.
- Deliver RNG isolation, creation/codecs, inventory operations, food, and floor deltas together as the first playable item milestone. Shops, lighting, locks, and backpack acquisition remain separate roadmap work.
- Equipped entries count toward the initial 20-entry backpack. Gold remains a stat currency. Potions and scrolls have explicit caps of 20; gold pickups cap at 999. Food and equipment are always individual. Capacity is saved; larger-backpack acquisition is deferred.

## Ownership and identity contracts

`Item.SourceDefinitionPath` and `Item.InstanceId` are empty on definitions. Each runtime individual or homogeneous stack owns a Resource and ID. Quantity belongs to `InventoryItemSlot` or `LootableItem`. The equipment map references inventory entries, rather than owning a second copy.

Definition paths distinguish authored varieties. `IIdentifiable.TypeId` distinguishes learned effect families. Reward source IDs distinguish placements and encounters. These identities serve different purposes. A path/content/generation change that invalidates saved references requires a version bump, not migration.

`ItemFactory.CopyDefinition` deep-copies Resource state and explicitly carries non-exported food contamination. Runtime-copy tests verify independent affix arrays and nested modifiers. Immutable scene/effect references can be shared safely; callers must not mutate them as gameplay state. Constructors and ordinary resource loading never roll contamination.

Creation initializes variation once. Transfers retain identity and state. Splitting allocates a new record ID; merging retires the source record. Generated IDs use stable source keys plus output ordinals; dynamic/split IDs use GUIDs without consuming gameplay RNG.

## Resource and scene authoring

| Surface | Delivered wiring |
| --- | --- |
| `Item` | Exported `MaxStackSize`, existing stack opt-in, common runtime provenance/identity |
| `ItemCreationPolicy` | `[GlobalClass] Resource`: `RollEquipment`, `EquipmentAffixes`, `RollFoodContamination` |
| `dungeon_item_policy.tres` | Explicit affix pool; equipment and food rolls enabled |
| `fixed_item_policy.tres` | Both rolls disabled; a null policy also means fixed creation |
| `LootTableComponent` | Exported policy alongside table, chance, count, and pickup scene; default component scene assigns the dungeon policy |
| `MapGenerator` | `level.tscn` assigns the dungeon policy for loose finds |
| Starting inventory | Fixed creation through `Inventory.CreateRuntimeCopy`; class supplies stay predictable |
| Authored `LootableItem` | Exported definition, quantity, and optional policy; initializes once with a source-derived RNG before becoming usable |
| `FoodItem` | Exported poison chance/effect; existing recovery buff; runtime contamination is private |
| `EnchantScrollEffect` | Exported affix pool; no hidden resource path in the roller |
| `LevelItemPersistence` | Authored child of `Level`, with exported pickup scene |
| Item playground | `scenes/debug/item_lifecycle_harness.tscn`: exported definitions, policy, run seed, depth, quantity, and production spawn commands |

Runtime pickups and player inventories expose their actual Resources for Remote Inspector traversal. Read-only C# properties expose diagnostic identity. Editing arbitrary exported collections on live equipment is not a supported command: enchanting performs the required unequip/modify/re-equip cycle. The factory and codec APIs use ordinary C# result/data types; playground commands use Godot-compatible signatures and readable `LastResult`.

## Creation and item-codec contracts

Abbreviated delivered signatures:

```csharp
// ItemFactory.cs — plain C# helper.
public static Item CopyDefinition(Item definition, string instanceId = null);
public static Item Create(Item definition, ItemCreationPolicy policy = null,
    uint depth = 1, RandomNumberGenerator rng = null, string instanceId = null);
public static IReadOnlyList<ItemQuantity> CreateBatch(Item definition, int quantity,
    ItemCreationPolicy policy = null, uint depth = 1,
    RandomNumberGenerator rng = null, string idPrefix = null);

// ItemSaveCodec.cs — no RNG or gameplay callbacks.
public static ItemEntrySaveData Capture(Item item, int quantity);
public static Item Restore(ItemEntrySaveData data);

// InventorySaveCodec.cs — validates the complete inventory before replacement.
public static InventorySaveData Capture(Inventory inventory);
public static Inventory Restore(InventorySaveData data);
```

`Create` requires a definition. Relevant randomized policies require an explicit RNG; rolling equipment requires an affix pool and contaminating food requires a poison effect. Invalid authoring/internal creation contracts throw before rolling. `CreateBatch` expands non-stackable quantities into independent pieces and stackable quantities into capped records. Neither factory nor codecs reach into `GameSession`.

Copies and restoration do not roll. Saved equipment carries its exact rarity and modifiers; food carries its contamination bit. Missing definitions, invalid quantities, duplicate inventory identities, invalid equipment mappings, and malformed item state reject restoration. Callers catch these failures at load boundaries; no partial inventory is installed.

This deliberately simplifies the original proposal: factory/codec configuration failures use exceptions, while fallible player operations return `ItemOperationResult`. There are no parallel `TryRestore` result wrappers or a separate nested equipment DTO. `ItemEntrySaveData` holds common identity/quantity plus the existing rarity/affix fields and optional food state.

## Inventory and controller contracts

```csharp
// Inventory.cs — Resource holding entries, equipment map, and local notifications.
public ItemOperationResult CanAdd(Item item, int quantity = 1);
public ItemOperationResult TryAdd(Item item, int quantity = 1);
public ItemOperationResult TrySplit(InventoryItemSlot entry, int quantity);
public ItemOperationResult TryMerge(InventoryItemSlot source, InventoryItemSlot target);
public ItemOperationResult CanRemove(InventoryItemSlot entry, int quantity);
public IDisposable DeferChanges();

// PlayerInventoryController.cs — actor-local coordination.
public bool IsCommittingItemOperation { get; }
public ItemOperationResult TryPickup(LootableItem source);
public ItemOperationResult TryUse(InventoryItemSlot source, InventoryItemSlot target = null);
public ItemOperationResult TryDrop(InventoryItemSlot source, int quantity);
public ItemOperationResult TryEquip(InventoryItemSlot source);
public ItemOperationResult TryUnequip(InventoryItemSlot source);
```

Stack equality uses nonempty definition provenance, not names, learned identities, or Resource reference equality. Authored stackable units must be interchangeable. Food/equipment cannot stack even if the authoring toggle is accidentally enabled. A full backpack can accept a compatible merge within its cap. Acquisition, splitting, and merging preflight the complete operation and preserve quantities on failure.

`TryPickup` retires its world source only after successful insertion. Gold follows its existing currency path. Direct `Player.PickupItem` callers retain a bool facade. Drops prepare an off-tree representation before removing anything from inventory; partial stack drops copy into a new record. Successful whole-item drops preserve the same runtime Resource. There is no separate prepare/commit API on the persistence Node: preparation stays private to the controller.

The controller blocks reentrant operations while committing. `DeferChanges` coalesces inventory `Changed` notifications until ownership is consistent. Save and stairs reject capture during item/reward commits. Expected failures return a typed failure and readable message; unexpected effect exceptions do not promise rollback of arbitrary side effects.

Use revalidates source/target membership, applies one effect, removes one unit, then publishes consumption. Inventory UI selects targets and delegates execution; it never applies a scroll effect before calling use. Successful notifications preserve identification behavior. Starting a targeted read may identify its scroll even if cancelled; it does not consume it.

The backpack displays entry count/capacity, provides split/combine actions, and shows operation failures. Blocked pickups show a brief world message. Equipped entries can be unequipped before dropping through the UI.

## Current-version save contracts

`SaveGame.CurrentVersion` is 7. Only the exact serialized version is accepted; missing/older/newer versions require a new run. No migrations or old-save adapters exist. Slot listing preserves the distinction between an empty slot and an incompatible occupied slot.

- `InventorySaveData`: capacity, item entries, equipment references by instance ID.
- `ItemEntrySaveData`: definition path, instance ID, quantity, equipment rarity/affixes, optional food state.
- `WorldSaveData.ItemChanges`: per-depth differences below.
- `SaveService`: serialize first, write/flush a temporary file beside the slot, then replace the destination. Failed capture/preparation preserves the previous file.

`GameSession` preflights carried items before changing scenes. Actual floor references/IDs are validated against the regenerated floor before `LevelLoaded` permits ordinary spawning. Invalid floor state returns to the menu without saving over the rejected slot. Active timed buffs and living-enemy injury/movement are not added to the save scope by this milestone.

## Seeded baseline and delta contracts

See [Randomness and Seeds](../randomness-and-seeds.md) for existing run/depth derivation and named streams. `LevelGenerationContext` owns layout, encounter, item-placement, and decoration generators. Stable reward sources derive separate loot RNGs from the level seed and source ID; opening/killing sources in another order does not change their base rewards. Regeneration never advances the gameplay loot counter.

Repeatability covers the same build/content and input set. Generator/content/draw-order changes between versions may change a scenario. Physics, combat, and AI are not deterministic replay.

`LevelItemPersistence` keeps a transient initial baseline and activated rewards in memory. Disk holds:

| Delta | Meaning |
| --- | --- |
| `RemovedItemIds` | Baseline objects no longer on this floor |
| `Overrides` | Full changed item record/quantity/position replacing a baseline object |
| `AddedItems` | Dynamic or player-dropped objects absent from this floor's baseline |
| `ActivatedSources` | Source ID, activation position, table/policy paths and reward settings |

Activated-source descriptors preserve the source's authored roll inputs so a cleared enemy need not be alive to regenerate its drop. They contain no complete untouched floor/item list. Their source seed reproduces the reward; saved removals/overrides then preserve what happened to it. A claimed source with no reward is still recorded. This is a small extension of the original activation-position-only proposal, avoiding a new global source registry.

On restore, rebuild the entire initial baseline, reconstruct activated rewards, apply validated deltas, prepare final pickup Nodes, replace generated representations, then mark surviving sources claimed and chests open. Restoration invokes no pickup, reward, effect, or identification callbacks. Seeded monster definitions/positions reconstruct independently; cleared IDs suppress defeated enemies. Living enemies currently reset to their generated initial state on revisit.

## Changed call order

```text
Loot source activation
  → source-local RNG + GenerateOutcome
    → ItemFactory.CreateBatch(definition, policy, depth, RNG, source key)
  → register activation and transient reward baseline under commit guard
  → Level.AddWorldNode(pickup per individual/stack)

LootableItem / backpack UI
  → PlayerInventoryController.TryPickup / TryUse / TryDrop / TryEquip
    → validate ownership, target, quantity, space, or destination
    → guard reentrancy + defer Inventory.Changed
    → transfer ownership / execute exactly one supported effect
    → retire source or consumed unit
    → existing hooks and success notifications
    → release coalesced changes and guard

Save / stairs
  → reject ongoing item/reward commits
  → ItemSaveCodec + InventorySaveCodec capture carried state
  → LevelItemPersistence.Capture → store per-depth DTO in GameSession
  → safe file replacement / change depth

Level.Generate
  → seeded complete MapGenerator baseline
  → RecordInitialBaseline
  → reconstruct activated sources + Apply(saved differences)
  → existing visibility and LevelLoaded flow
    → source spawn points (skip cleared enemies)
    → player restore with the same item codecs
```

## Delivered slices and verification

| Slice | Delivered behavior | Main checks |
| --- | --- | --- |
| 0: RNG isolation | Explicit streams, source rewards, initial scenario reproduction | `GenerationRandomTest`, `GenerationBaselineTest`, layout/seed/loot suites |
| A: Creation/codecs | Runtime provenance, policy Resources, copies, exact item/inventory state | `ItemLifecycleTest`, enchant/class/save-version tests |
| B: Inventory operations | Capacity/caps, split/merge, coordinated pickup/use/drop, UI feedback | `ItemLifecycleTest`, `ItemLifecycleIntegrationTest`, ring-slot tests |
| C: Food | Individual pieces, seeded contamination, authored recovery/poison effects, loot availability | Factory/codec tests and real poisoned-food consumption in integration |
| D: Floor differences | Collected items, surviving rewards, player drops, claimed chests, per-depth DTOs | Integration regeneration/JSON round trip, source-order tests, atomic-write tests |

The implementation lives beside its feature: helpers under `scenes/items/`, controller/inventory under `scenes/player/`, local persistence under `scenes/levels/`, disk codecs under `scripts/saves/`, and runtime tests under `tests/`. Resource/scene authoring uses the [Godot validation workflow](../../.agents/skills/godot-mcp/SKILL.md).

Editor acceptance: run the item playground with F6, assign/edit definitions or policy in the Inspector, spawn supplies, inspect runtime copies in Remote, open the backpack, consume/equip/split/combine/drop, fill the pack, and retry pickup. For disk saves and stairs, start a new run through the menu, leave a drop and an opened chest, travel away/back, save, return to menu, and load. Automated headless checks do not validate navmesh pathfinding or final visual quality; verify those in the editor.

Remaining gameplay work belongs to later roadmap milestones: supply-budget tuning, additional body armor, darkness/lighting, keys/locks, backpack acquisition, shops, and complete-run goals. Do not add dormant fields or a generic capability framework for those features.

Verification on October 19: the complete suite passed 107 tests; 16 final targeted checks passed
including two newly added atomic-write cases. Three strengthened generation checks also passed,
comparing actual item IDs, equipment rolls, and food contamination under the dungeon policy. Debug and ExportRelease compilation passed, and final Godot import reported no load errors. The playground was also exercised in a game window:
spawn an apple, collect it, open its Eat menu, and consume it. The same window reported one
Metal fence timeout during startup but continued rendering and handling item actions.
Generation sweeps emit orphan-node warnings; headless navigation/forced-exit warnings do not
validate or invalidate the real pathfinding acceptance exercise.
