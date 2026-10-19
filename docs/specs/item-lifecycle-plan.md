# Item Lifecycle — Program Design

Status: proposed implementation plan, October 19, 2026. Written using the repository's [program-design skill](../../.agents/skills/program-design/SKILL.md). Signatures below are abbreviated contracts, not compilable source files. Names marked as proposed do not exist yet. No gameplay implementation or engine validation is claimed.

Implements [roadmap milestone 1](../development-roadmap.md#milestone-1--items-retain-their-identity-and-state). Gameplay requirements live in the [GDD](../game-design.md#items-and-their-lifecycle); system principles live in [ARCHITECTURE.md](../../ARCHITECTURE.md). Existing seeded generation, loot RNG, identification, and saved world differences are documented in [Randomness and Seeds](../randomness-and-seeds.md). This plan specifies extensions to those systems, not their initial introduction. See the [documentation index](../README.md) for current-system references.

## Decisions

- **Retain concrete item Resources.** Existing `Item` subclasses remain definitions and runtime copies, distinguished by common provenance and runtime identity. This preserves equipment, `IPlayerAction`, identification, UI, and scroll integration. A separate instance wrapper would require changing all those consumers together without a demonstrated benefit for this increment.
- **Compose new independent capabilities.** Existing `Buff`, `ScrollEffect`, `AffixPool`, and scene components remain the strategy/composition seams. Do not add subclasses for every combination or build a generic capability registry. Shared strategies hold no per-player mutable state. Future active lighting belongs in an actor-side component, with authored configuration and instance state on Resources.
- **Separate authoring from execution.** Definitions, creation policies, effects, and loot tables are Inspector-authored Resources. Factory and codec algorithms are plain C# helpers with explicit inputs. Local Nodes own interactions and scene lifetime. No new autoload or gameplay registry.
- **Deliver a hybrid sequence.** First extend RNG isolation and verify the existing seeded baseline, then land a shared creation/codec contract through a complete existing path, including saves. Deliver inventory operations, food, and per-floor deltas as subsequent usable slices. This exposes Resource-copying uncertainty early while keeping intermediate builds coherent.
- **Keep existing capacity accounting provisionally.** Equipped entries count toward the 20-slot capacity, gold stays in player stats, optional keys count normally. These are recommendations pending gameplay agreement; required-key acquisition, stack caps, and backpack upgrade sizes still need decisions. Capacity persistence is included; upgrade acquisition is deferred.

## Ownership and identity contracts

`Item.SourceDefinitionPath` and `Item.InstanceId` become common runtime properties. They are empty on authored definitions. Each individual item or homogeneous stack record gets its own runtime Resource and ID (source-derived for generated items, separately allocated for dynamic records); quantity remains on the owning inventory/world entry. Equipment references the same inventory entry instead of owning another item.

Definition paths distinguish authored varieties; `IIdentifiable.TypeId` continues to distinguish learned effect families; persistent source IDs identify monsters, chests, or floor placements. These identities are not interchangeable. Keep path-based definition resolution. A path rename or generation/content change that invalidates saved references requires a save-version bump, not migration.

Factory creation copies the definition, owns mutable collections, then initializes supported variation once. Transfers retain identity and state. Splitting a valid homogeneous stack copies its record with a new ID; merging retires empty source records. Generated IDs derive from stable source IDs and output ordinals; dynamic/split records use a separate allocator or persisted counter. Runtime IDs do not consume loot RNG.

Copy routines explicitly isolate affix arrays and rolled modifiers. Shared scenes, buffs, and stateless effect definitions remain shared. Do not treat `Duplicate(true)` or `ResourceLocalToScene` as proof of isolation for every nested value or dynamically created item. Setters/constructors remain safe during Inspector creation, load, and preview; no gameplay randomness there.

## Resource and scene authoring

| Surface | Contract and wiring |
| --- | --- |
| `Item` subclasses | Keep `[GlobalClass]` and exported definition properties; add authored stack caps and observable runtime diagnostics |
| Proposed `ItemCreationPolicy` | `[GlobalClass] Resource`; exported equipment-roll toggle, `AffixPool`, and food-contamination-roll toggle; fixed policy defaults to both toggles off |
| `LootTableComponent` / `MapGenerator` | Export creation-policy references alongside their existing tables; pass RNG/depth explicitly to the helper |
| `CharacterClass` / fallback `Player` | Export starting-item policy; fixed/default policy keeps starting gear predictable |
| Authored `LootableItem` | Keep existing exported `Item` and `Quantity` as authoring inputs; add a fixed/default policy for definitions not already initialized |
| Runtime pickups / player | Expose their item/inventory Resources for Remote Inspector traversal; never load a fresh definition to display a runtime item |
| Proposed `LevelItemPersistence` | Child Node of `Level`, wired by its parent; captures deviations from the seeded baseline locally |
| Proposed lifecycle test scene | Inspector-assigned definitions, policies, seed, and quantity; debug commands call production operations and show their results |

Designer-controlled fields use typed exports, useful grouping/hints, and validation. Runtime identity/provenance should be diagnostic rather than normal writable authoring controls; use a custom property list or debug view if needed. Runtime changes use observable setters. Nested Resource changes require explicit subscription/unsubscription; editing equipped affixes must refresh source-keyed modifiers through the same command as enchanting. Arbitrary direct mutation of an exported collection is not a safe live-edit contract.

The test scene's command methods use Godot-compatible arguments and a readable result property. Internal C# result types and DTOs need not be Variant-compatible or editable gameplay configuration. Editor actions must not require editing factory code to select an existing strategy or change balance.

## Creation and item-codec contracts

Proposed common and policy properties:

```csharp
// Item.cs — existing Resource; definition exports versus runtime diagnostics.
[Export] public int MaxStackSize { get; protected set; }
public string SourceDefinitionPath { get; private set; }
public string InstanceId { get; private set; }
public bool IsRuntimeInstance { get; }
internal void InitializeRuntimeIdentity(string definitionPath, string instanceId);

// ItemCreationPolicy.cs — new [GlobalClass] Resource.
[Export] public bool RollEquipment { get; set; }  // default false
[Export] public AffixPool EquipmentAffixes { get; set; }
[Export] public bool RollFoodContamination { get; set; } // default false

// Plain C# records; constructors shown only to define their fields.
public sealed record ItemQuantity(Item Item, int Quantity);
public sealed record ItemCreationResult(
    ItemFailure Failure, IReadOnlyList<ItemQuantity> Entries);

// ItemFactory.cs — new helper, no singleton or hidden asset loads.
public static ItemCreationResult TryCreate(
    Item definition, int quantity, ItemCreationPolicy policy,
    uint depth, RandomNumberGenerator rng, string instanceIdPrefix);
internal static Item CopyDefinition(Item definition, string instanceId);

// LootRoller.cs — change Roll to initialize an existing runtime item.
public static void ApplyRoll(EquipableItem instance, uint depth,
    RandomNumberGenerator rng, AffixPool pool);
public static void Enchant(EquipableItem instance,
    RandomNumberGenerator rng, AffixPool pool);

// ItemSaveCodec.cs — new helper; restore does not call TryCreate/ApplyRoll.
public static ItemSaveData Capture(Item item);
public static ItemRestoreResult TryRestore(ItemSaveData data);
public sealed record ItemRestoreResult(ItemFailure Failure, Item Item);
```

`TryCreate` accepts only an authored, resolvable definition and positive quantity. Null policy means fixed defaults. RNG is required only when a configured strategy actually rolls; a roll-enabled equipment policy needs its assigned pool. Failure returns no entries and creates no world nodes. Validate the entire request before consuming RNG; ordinary configuration failures do not advance it. No rollback of RNG is promised after an unexpected programming exception.

`instanceIdPrefix` identifies the stable generation source or a separately allocated dynamic creation event; output ordinals complete each identity. Recreating a source must reproduce those IDs. Non-stackable quantity N produces N independent runtime items of quantity one. Stackable requests produce homogeneous records respecting the definition's cap. Both record types carry common provenance. Fixed creation uses authored defaults; randomized creation uses supplied RNG. The helper never calls `GameSession` itself.

`CopyDefinition` is the internal non-random path used by creation, stack splitting, and codec restoration. It allocates no world state and never invokes pickup/equip/use callbacks. `ApplyRoll` and `Enchant` operate only on validated runtime equipment; definitions are rejected. `EnchantScrollEffect` exports its affix pool and passes it explicitly, removing `LootRoller`'s hidden pool path. Update existing tests/callers alongside this API change.

`TryRestore` resolves `SourceDefinitionPath`, obtains a copy through `CopyDefinition`, and stamps saved identity/state without RNG. Expected bad content/save conditions use `ItemFailure` values; programming-contract violations in internal helpers use exceptions. `Capture` rejects an uninitialized definition instead of saving an ambiguous reference.

Proposed `ItemFailure` cases: `None`, `InvalidQuantity`, `InvalidDefinition`, `InvalidPolicy`, `IncompatibleStack`, `InsufficientSpace`, `NotOwned`, `InvalidTarget`, `ProtectedItem`, `WorldUnavailable`, `Busy`, and `InvalidSave`. UI maps them to readable messages; code does not compare message text.

## Inventory and controller contracts

```csharp
// ItemOperationResult.cs — ordinary C# value; operations are all-or-nothing.
public readonly record struct ItemOperationResult(ItemFailure Failure);

// Inventory.cs — retain Resource ownership and existing equipment map.
public ItemOperationResult CanAdd(Item item, int quantity);
public ItemOperationResult TryAdd(Item item, int quantity);
public ItemOperationResult TrySplit(InventoryItemSlot source, int quantity);
public ItemOperationResult TryMerge(
    InventoryItemSlot source, InventoryItemSlot destination);
internal ItemOperationResult CanRemove(InventoryItemSlot source, int quantity);
internal void RemoveValidated(InventoryItemSlot source, int quantity);

// PlayerInventoryController.cs — orchestration stays on the actor.
public bool IsCommittingItemOperation { get; }
public ItemOperationResult TryPickup(LootableItem source);
public ItemOperationResult TryDrop(InventoryItemSlot source, int quantity);
public ItemOperationResult TryUse(
    InventoryItemSlot source, InventoryItemSlot target = null);

// LootableItem.cs — existing scene adapter and source ownership.
public void InitializeRuntime(Item item, int quantity);
public void CommitCollected();

// LevelItemPersistence.cs — also provides level-local world integration.
public ItemOperationResult TryPrepareDrop(ItemQuantity entry,
    Vector3 position, out LootableItem prepared);
public void CommitPreparedDrop(LootableItem prepared);
public void CancelPreparedDrop(LootableItem prepared);
```

`CanAdd`/`CanRemove` are non-mutating. Mutators validate ownership, quantities, and membership. `TryAdd` takes a single runtime individual/stack record; non-stackable quantity greater than one is invalid, because creation expands it beforehand. It preflights aggregate space across merge targets and any new entries, then commits the complete request. A full inventory can add to an existing stack below its cap. `TrySplit` creates a new entry directly, bypassing auto-merge; `TryMerge` never partially fills a destination.

Stack equality uses definition provenance, not display names, `TypeId`, or Resource reference equality. Stackable definitions opt in and cannot generate per-unit affixes, poison, charges, or other variation. Both wholesome and poisoned food are individual. Catalog validation rejects incompatible configuration; cap values are explicit content decisions before migration.

`TryPickup` receives the world source so the controller can coordinate source retirement with insertion. Gold follows its current stat-currency path. Keep `Player.PickupItem(Item,int)` as a compatibility facade for unowned/direct callers while migrating `LootableItem.TryCollect` to the source-aware controller API; all callers must be audited before removing the facade. `InitializeRuntime` rejects a definition or invalid quantity; authored pickups create their runtime representation once before becoming interactive. Migrate authored single pickups to quantities that fit one record; batch reward generation materializes all factory output records as separate pickups.

Prepared drops are inactive and excluded from persistence capture. Preparation validates destination/scene and creates the representation before inventory changes. Failure preserves source inventory and equipment. A successful drop uses the same runtime Resource, updates equipment as necessary, activates its world owner, and then notifies. Cancelling preparation frees only the uncommitted representation. `Level` initializes this local component and exports the pickup scene dependency; no controller-local hardcoded scene path.

The controller serializes reentrant item operations with a local guard; `Busy` does not mutate anything. Transfer commits publish local changes and `SignalBus` events only after both owners are consistent. Hold/coalesce inventory change notifications during multi-owner operations. `GameSession.SaveActiveGame` and `ChangeDungeonDepth` check `IsCommittingItemOperation` before capture and return false with feedback if busy; they do not change depth or write partial state. The caller can retry after the synchronous operation completes; do not introduce an automatic retry loop or expose partially transferred state to observer callbacks.

`TryUse` revalidates source/target ownership, executes exactly one supported effect, removes one consumed unit, then emits success. `InventoryPanel` selects a target; it no longer calls `ApplyToTarget` before consumption. `InventoryItemContextMenu` delegates ordinary use/drop to the controller too. Keep `ScrollEffect.IsValidTarget`, `Apply`, and `ApplyToTarget`, and `IPlayerAction`; existing void effect methods require success after prevalidation and no callbacks before commit. Exceptions are defects, not an implied rollback of arbitrary gameplay side effects. Add a result-bearing effect contract later if a supported effect can fail after prevalidation. Cancelled/no-target reads preserve the scroll and existing reading-identifies behavior.

Remove the old consume signal's role as an effect-execution request. `PlayerInventoryController.OnItemConsumed` no longer invokes the effect again. Successful consume/equip notifications keep existing identification and UI integrations. Enchanting continues to unequip/refresh/re-equip the same runtime object, preserving identity and action references. Two identical rings retain distinct modifier sources.

## Current-version save contracts

Replace DTO fields as needed for the current supported format; plain C# is the disk representation, Resources remain gameplay state. Pre-release save compatibility is explicitly excluded by `AGENTS.md`:

```csharp
// New item-level DTO; owning entries carry quantity.
public sealed class ItemSaveData
{
    public string DefinitionPath { get; set; }
    public string InstanceId { get; set; }
    public EquipmentItemSaveData Equipment { get; set; }
    public FoodItemSaveData Food { get; set; }
}
// EquipmentItemSaveData: Rarity and existing RolledAffixSaveData list.
// FoodItemSaveData: IsPoisoned. Null sections mean not applicable.

// InventorySaveData: add Capacity; entries contain ItemSaveData + Quantity.
// EquippedItemSaveData: replace ItemIndex with ItemInstanceId.
// WorldSaveData: add per-depth LevelItemDeltaSaveData records.
// LevelItemDeltaSaveData: Depth, RemovedBaselineItemIds, ItemOverrides, AddedItems,
//                         ActivatedRewardSources.
// ItemOverrideSaveData: baseline InstanceId + replacement state/quantity/position.
// AddedWorldItemSaveData: full ItemSaveData, Quantity, Position.
// RewardSourceDeltaSaveData: SourceId, activation position (e.g. enemy death location).

// InventorySaveCodec.cs — new helper; preflight all entries before replacing live state.
public static InventorySaveData Capture(Inventory inventory);
public static InventoryRestoreResult TryRestore(InventorySaveData data);
public sealed record InventoryRestoreResult(ItemFailure Failure, Inventory Inventory);
```

`GameSession.CaptureInventory` and `ApplyInventory` delegate item encoding/decoding to the codecs; retain player-specific unequip/equip and stats synchronization in session/controller integration. Restore validates the whole inventory before replacing it. The equipment mapping uses instance IDs and is checked against restored entries.

Use `SaveGame.CurrentVersion` as an exact format gate. Bump it whenever a slice changes schema or invalidates saved content/generation references. Do not reserve version numbers for unimplemented slices, retain obsolete fields, or add migration helpers, old-save adapters, default reconstruction, or over-capacity recovery for previous versions.

`SaveService.Load` checks the serialized version before deserializing the current DTO shape. A missing or mismatched version returns failure with a clear new-run-required message. Avoid interpreting a missing version through the DTO's `CurrentVersion` initializer. Save-slot listing must distinguish an incompatible occupied slot from an empty slot and allow the normal explicit new-run/overwrite flow. Rejection itself never overwrites a file.

Current-version records must contain valid capacity, quantities, unique identities, supported item state, and valid equipment references. Missing definitions or invalid data fail the complete load visibly and preserve the original file. State that was not saved must not be invented or rerolled. Existing development saves may be discarded by the player; preserving them is not a delivery requirement.

`SaveService.Save` keeps its bool signature and writes a temporary file beside the destination before safe replacement. A failed write/replacement must retain the last valid save. Verify the actual replacement API on this machine in the persistence slice, including injected failure cases; this plan does not assert filesystem guarantees not yet tested.

## Seeded baseline and delta contracts

Regeneration reconstructs the untouched scenario from the run seed, depth, and current generator/content version. Saved state describes differences from that baseline. Do not serialize every remaining item, pre-generate/store all future floors, or require an item snapshot merely to distinguish a visited floor. A visited floor with no changes can have the same empty item delta as an unvisited floor; fog/room discovery separately records exploration.

### RNG ownership

The reproducibility contract is **same build/content + same run seed + same relevant inputs and draw order = same baseline**. A code or content change that inserts, removes, or reorders draws can change that domain's entire output, and may change the wider scenario through data dependencies. This is acceptable during development. Do not preserve historical draw schedules, old PRNG implementations, or previous-build seed outputs; bump the save version if baseline changes invalidate deltas.

Use the existing `GameSession.GetLevelSeed(runSeed, depth)` as the first derivation step. Derive domain seeds from that level seed and named domain tags, then optional source seeds from the domain and stable source ID. Persist the run seed, not redundant seeds for every untouched floor. The harness exposes the run seed, depth, and domain/source seed diagnostics so a scenario can be reproduced in the editor.

| Domain | Seed/continuation rule |
| --- | --- |
| Level layout and environment | Level seed + layout tag; owns rooms, corridors, and structural generation |
| Monsters | Level seed + monster tag; owns initial encounter placement/roster/variants; resolve every spawn slot before filtering defeated entities |
| Items and reward-source placement | Level seed + item tag; owns chest/loose-item placements and item-generation choices in stable order |
| Deferred source loot | Item domain + stable source ID; reconstruct a chest/monster's base reward without depending on activation order |
| Gameplay-time random actions | Separate ongoing stream/counter, such as the existing `CreateLootRng` sequence for enchantment/dynamic actions; persist continuation state when needed |
| Cosmetic randomness | Separate stream; cannot advance layout, encounter, or loot generation |

A single RNG per domain is sufficient when traversal/draw order is fixed; adding a draw may change later values within that domain. Source-derived substreams are specifically useful for lazy rewards and delta reconstruction, not a requirement for every random choice. Derivation uses an explicitly defined stable hash/mixer with unambiguous input encoding and domain constants; never runtime `GetHashCode`, object IDs, wall clock, or random collection enumeration. The initial run seed may come from time once when starting a new run; reconstruction always uses its persisted value.

Stream independence does not imply output independence when systems consume each other's data. Monster placement depends on room geometry and may intentionally avoid chests; changing those inputs may change encounters despite separate RNGs. Tests isolate inputs when checking stream independence and compare the full scenario when checking regeneration. Do not claim that adding a room or item definition preserves every other outcome.

Reset generation streams at regeneration. Resume ongoing gameplay streams from their saved state/counter, not from their initial seed, when future outcomes depend on prior actions. Do not accidentally save a post-generation cursor and resume baseline generation from it. Global RNG use remains acceptable only for effects explicitly excluded from scenario reproduction and baseline generation; never reseed it to coordinate deterministic subsystems.

The [current implementation](../randomness-and-seeds.md) already derives run/level seeds, chooses procedural monster scenes during generation, persists a separate loot counter, and seeds identification by category. The change here is explicit subsystem ownership and source-local reward reconstruction. Preserve generated encounter selection in `MapGenerator.GenerateEnemySpawnPoints` (which already assigns a single selected scene); harden general multi-scene `SpawnPoint` use rather than implying procedural monsters are not selected until callbacks. Rebuild complete placements before applying deltas so removed objects do not shift baseline results.

The fixed input set includes ordered content pools and generator algorithms, not just the numeric seed. Determinism tests compare actual selected definitions, transforms, item IDs, quantities, and rolled properties. Seed equality alone is insufficient. This contract covers initial generation, not deterministic replay of physics, combat, or AI simulation.

Proposed contracts (plain C# helpers/context; RNGs are local to the current generation):

```csharp
// GenerationRandom.cs — no autoload or gameplay registry.
public static ulong DeriveSeed(ulong levelSeed, string domain, string sourceId);
public static RandomNumberGenerator Create(ulong levelSeed, string domain, string sourceId);

// LevelGenerationContext.cs — transient context, not an authored shared Resource.
public ulong LevelSeed { get; }
public RandomNumberGenerator Layout { get; }
public RandomNumberGenerator Encounters { get; }
public RandomNumberGenerator Items { get; }
public RandomNumberGenerator Decoration { get; }

// Existing Resource factory methods gain explicit RNG inputs; implementations/callers follow.
public abstract PackedScene CreateEnemy(uint dungeonDepth, RandomNumberGenerator rng);
public abstract Room CreateEntrance(RandomNumberGenerator rng);
public abstract Room CreateExit(RandomNumberGenerator rng);
public abstract Room CreateStandardRoom(RandomNumberGenerator rng);
public abstract Room CreateSpecialRoom(RandomNumberGenerator rng);
public abstract int GetCorridorTileIndex(RandomNumberGenerator rng);
public abstract int GetWallTileIndex(RandomNumberGenerator rng);
// Half/corner tile variants forward the same explicit generator.

// SpawnPoint.cs — select/assign a concrete scene during baseline construction.
public void ConfigureGeneratedSpawn(string sourceId, PackedScene selectedScene);
// Existing Spawn() uses that selected scene; no global random selection on restore.
```

Resource strategies receive the context/RNG as invocation input rather than retaining mutable stream state on shared definitions. Thread explicit generators through layout/connectors/procedural-room helpers that currently consume global randomness; preserve editor preview through an explicit preview context. Document any remaining gameplay-time randomness separately from baseline generation.

### Delta representation and application

```csharp
// LevelItemPersistence.cs — proposed level-local Node.
public LevelItemDeltaSaveData Capture(uint depth);
public ItemOperationResult TryApply(LevelItemDeltaSaveData changes);
public void Initialize(Level level, PackedScene pickupScene);

// GameSession.cs — DTO storage only, no live entity registry.
public LevelItemDeltaSaveData FindLevelItemChanges(uint depth);
public void StoreLevelItemChanges(LevelItemDeltaSaveData changes);

// LootTableComponent.cs — generation and activation are separate operations.
public ItemCreationResult GenerateOutcome(ulong levelSeed, string sourceId);
public void RestoreClaimed(bool claimed);
// Chest.cs — restore without ordinary Opened/reward events.
public void RestoreOpenState(bool isOpen);
```

The delta captures:

- **Removed baseline item IDs:** items consumed, collected, destroyed, or moved out of their original floor position. Inventory records save carried item state in full; its baseline world copy stays suppressed.
- **Item overrides:** changed quantities, properties, or positions for regenerated items still owned by this floor. A partial stack pickup records the remainder. A stable baseline ID has either removal or override, never conflicting records.
- **Added items:** player drops and genuinely dynamic items not represented at their current location by the generated baseline. Save their full state/quantity/position. A carried baseline item dropped later keeps its ID: suppress the original baseline and add its current world record exactly once. Cross-floor transfers suppress the origin and add the destination.
- **Activated reward sources:** opened chests and defeated source monsters, including an activation position when loot appears somewhere other than the original spawn. Derive their complete base reward from the source seed, then apply item removals/overrides. Record activation even when a source rolls no loot, so it cannot trigger again.

The existing cleared-enemy set remains authoritative for defeated baseline monsters; source-reward activation is recorded in the same guarded operation as death/reward completion. Existing fog and opened-door deltas remain responsible for their features. The item component retains only necessary change records and transient baseline comparisons, not a serialized full-floor snapshot or replay log. Unchanged generated items need no disk record.

Restore order is: reconstruct the full baseline descriptors and source outcomes in memory (loot can remain lazy per source), apply cleared/opened/activated source state, apply item removals/overrides/additions, then materialize live objects and enable interactions. Generating a deterministic outcome is a pure creation operation, not emitting a drop/open/death event. Repeated `GenerateOutcome` calls return the same baseline; source activation owns the exactly-once materialization guard.

Source rewards are keyed by source ID plus output ordinal. Initial loose loot uses placement IDs; chest and monster rewards use the same source convention. Avoid random generated item IDs or consumption of the ongoing `LootRollsConsumed` sequence when reconstructing any baseline. Regeneration is expected to draw from its reset generation streams, while leaving ongoing gameplay streams untouched.

Capture only after synchronous item/source operations complete, before manual save or changing depth. Full captured item state is appropriate for inventory and dynamic additions; untouched floor content is regenerated. Empty deltas do not mean rewards should be replenished: activated/removed records encode those changes explicitly. Invalid IDs or contradictory records fail restoration rather than silently duplicating items.

Initial monster type and spawn transform regenerate. Saving moved, injured, alerted, or otherwise modified living monsters would require additional actor deltas or an explicit reset-on-revisit rule. That gameplay choice remains open and is not implied by item persistence. Summoned actors likewise require dynamic actor persistence if later supported. Seeds alone cannot recover arbitrary gameplay history.

## Changed call order

Creation and fixed pickups:

```text
LootTableComponent.DropLoot / MapGenerator.PlaceLooseItems
  eligible selection + stable source ID -> source-derived RNG/depth
  + ItemFactory.TryCreate(definition, quantity, exported policy, depth, rng, source ID)
    CopyDefinition -> ApplyRoll / explicit food initialization
  + materialize each ItemQuantity as an initialized LootableItem
    Level.AddWorldNode                         # all pickups reachable by local capture

CharacterClass.CreateStartingInventory / Player._EnterTree fallback
  + ItemFactory.TryCreate(... fixed starting policy ...)
  Inventory entries -> existing AutoEquipItems # definitions never become runtime owners

LootableItem._Ready, only for an authored definition
  + ItemFactory.TryCreate(... exported fixed/default policy ...)
  InitializeRuntime -> enable interaction     # preinitialized runtime drops skip creation
```

Transfer and use:

```text
LootableItem.TryCollect
  + PlayerInventoryController.TryPickup(source)
    preflight insertion -> enter guarded commit
    source retired + Inventory.TryAdd          # notifications held until ownership agrees
    publish inventory/SignalBus notifications -> source.QueueFree

InventoryItemContextMenu.DropItem
  + PlayerInventoryController.TryDrop(entry, quantity)
    CanRemove -> LevelItemPersistence.TryPrepareDrop
    enter guarded commit -> unequip/remove + CommitPreparedDrop
    publish notifications                      # failed preparation loses nothing

InventoryPanel target confirmation / context-menu use
  + PlayerInventoryController.TryUse(source, target)
    validate ownership + ScrollEffect.IsValidTarget when applicable
    guarded effect + consume-one commit        # exactly once, no effect in consume listener
    publish successful consume/identification notifications
```

Save, travel, and regeneration:

```text
GameSession.SaveActiveGame
  capture only after any synchronous item commit completes
  + LevelItemPersistence.Capture(current depth) -> StoreLevelItemChanges
  CapturePlayer -> InventorySaveCodec.Capture -> ItemSaveCodec.Capture
  SaveService.Save                             # temporary write, then replace

GameSession.ChangeDungeonDepth
  + capture departing depth BEFORE ActiveDungeonDepth changes
  existing CapturePlayerForLevelTransition -> change depth -> reload

GameSession.LoadGame
  SaveService.Load -> exact serialized-version check -> deserialize current DTOs
  preflight definitions/state                  # mismatch/missing version: fail, new run required
  existing scene transition

GameSession.ConfigureLevel -> Level.Generate
  + create reset level/domain generation streams
  MapGenerator.GenerateMap                     # rebuild complete baseline descriptors
  + FindLevelItemChanges(depth) -> LevelItemPersistence.TryApply
    regenerate activated source loot           # source seeds; no ongoing RNG consumption
    apply removals/overrides/additions          # no reward events; materialize only final owners
  existing LevelLoaded / player-spawn flow      # after successful floor restoration

GameSession.OnPlayerSpawned -> ApplyPlayerSave
  InventorySaveCodec.TryRestore -> replace inventory/equipment -> sync stats
  no item effects or rolls merely from decoding # equip callbacks occur once during binding
```

## Affected paths

Paths are repository-relative for this tree. Companion `.cs.uid` files are generated/imported for new C# scripts and referenced by authored Resources. New names below are proposed.

```text
scenes/items/
~ Item.cs                           common provenance/identity, stack cap, copy boundary
~ EquipableItem.cs                  move common provenance to Item; retain equipment state
~ FoodItem.cs                       explicit initialization; remove constructor randomness
+ ItemCreationPolicy.cs             Inspector-authored fixed/randomized policy
+ ItemFactory.cs                    creation and non-random copy helper
+ ItemOperationResult.cs            failures, creation/restore result contracts
+ ItemQuantity.cs                   item plus owner-record quantity
+ policies/fixed_items.tres         explicit fixed defaults
+ policies/dungeon_items.tres       randomized supported loot rules and affix-pool reference
~ affixes/LootRoller.cs             initialize runtime equipment; inject pool
~ scrolls/EnchantScrollEffect.cs    preserve runtime identity; export/pass pool
~ scrolls/scroll_enchant.tres       assign pool on its existing effect sub-resource
~ LootableItem.cs                   one-time initialization and source-aware pickup
~ lootable_item.tscn                export policy defaults and inspection wiring
~ food/**/*.tres                    convert edible definitions to FoodItem, disable stacking,
                                   supply poison effect/chance only where eligible
~ **/*.tres                         audit only existing stackable definitions for explicit caps
scenes/player/
~ Inventory.cs                     result-bearing operations, batching notifications, capacity
~ InventoryItemSlot.cs             stack/quantity invariants and copy ownership
~ PlayerInventoryController.cs     guarded transfer/use orchestration
~ Player.cs                        starting initialization and compatibility pickup facade
~ player.tscn                      fallback starting-policy/dependency wiring
~ classes/CharacterClass.cs         starting-policy export and runtime creation
~ classes/class_*.tres              assign fixed starting policy
scenes/components/
~ LootTableComponent.cs            creation-policy reference, claimed-state restoration
~ loot_table_component.tscn         assign default policy
scenes/levels/
+ LevelItemPersistence.cs           local drop preparation and baseline-delta persistence
~ Level.cs                         initialize component; generation/restore ordering
~ level.tscn                       compose persistence child and shared level dependencies
~ generators/MapGenerator.cs       seeded baseline descriptors, stable IDs, delta materialization
+ generators/GenerationRandom.cs    stable domain/source seed derivation
+ generators/LevelGenerationContext.cs  per-generation RNG ownership
~ generators/*Factory.cs            explicit RNG inputs, including mixed-room forwarding
~ dungeon/*Factory.cs               explicit RNG inputs for authored content selection
~ generators/layouts/*.cs           pass explicit layout RNG through affected generation calls
~ generators/connectors/*.cs        pass explicit layout RNG through affected generation calls
~ generators/ProceduralRoomBuilder.cs  explicit generation RNG
~ SpawnPoint.cs                    deterministic selected scene before cleared-source filtering
~ generators/map_generator.tscn    default loose-item creation-policy reference
scenes/main/
~ main.tscn                        override level configuration only where gameplay needs it
scenes/props/
~ Chest.cs                         non-rewarding open-state restoration
scenes/ui/
~ InventoryItemContextMenu.cs      delegate fallible use/drop commands
~ InventoryPanel.cs                target selection only; one controller use commit
scripts/
~ GameSession.cs                   codec delegation; capture before travel; per-depth DTOs
~ saves/SaveGame.cs                current-format inventory/world DTOs and version counter
~ saves/SaveService.cs             exact version gate, slot status, safe file replacement
+ saves/ItemSaveCodec.cs            shared inventory/world item encoding
+ saves/InventorySaveCodec.cs       all-or-nothing inventory decode
scenes/debug/
+ ItemLifecycleHarness.cs           Godot-compatible commands, seed/settings, result diagnostics
+ item_lifecycle_harness.tscn       authored sandbox using production operations
tests/
+ ItemFactoryTest.cs                Resource isolation, fixed/random creation, RNG boundaries
+ InventoryOperationsTest.cs        ownership, split/merge/caps/capacity/failure conservation
+ ItemSaveCodecTest.cs              identity/equipment/food round trips
+ SaveGameVersionTest.cs            mismatched/missing versions, occupied slot, no overwrite
+ ItemLifecycleIntegrationTest.cs   pickup/use/drop/enchant failures and observer ordering
+ LevelItemPersistenceTest.cs       source loot, deltas, cross-floor drops, empty-change floors
+ GenerationRandomTest.cs           stable domain/source streams and independence
~ SeedDeterminismTest.cs            seed derivation and actual baseline reproduction
~ InventoryEquipSlotTest.cs         existing behavior with runtime items
~ LootDeterminismTest.cs            explicit factory/pool inputs
~ LootRollerEnchantTest.cs          explicit factory/pool inputs
~ SaveGameCharacterClassTest.cs     current-version fields and rejection behavior where applicable
docs/
~ stats-and-itemization.md          creation, explicit stacks, current verified persistence
~ item-identification-system.md     controller use flow, retained discovery rules
~ development-roadmap.md            verified slice outcomes
~ ARCHITECTURE.md (repo root)        high-level responsibilities only when implemented
```

Before a slice adds or renames an authored asset, verify its exact existing path with `rg --files`; the tree is not authorization to create duplicate parallel scenes. Starting gear, direct debug spawns, tests, and fixed pickup placements must be audited as callers, not just randomized loot.

## Independently verifiable slices

### 0 — Extend and verify existing RNG isolation

Exercise: regenerate the same floor before and after unrelated gameplay/cosmetic draws and cleared-source filtering; compare initial descriptors before deltas.

Retain existing run/level derivation and seeded identification. Extend domain/source derivation and explicit generation RNG plumbing through factories, layout/room helpers, encounter placement, and deferred spawn selection. Preserve Inspector-authored Resource strategies. Keep generated encounters selected before callbacks, and make general multi-scene selection deterministic; omit cleared monsters only after baseline selection. No full-floor persistence is introduced in this slice.

Verify `GenerationRandomTest`, existing layout/seed tests, and a same-build engine baseline comparison of source IDs, monster scenes/transforms, loose-item definitions/rolls, and source reward outcomes. Change kill/chest order, skip a cleared entity, and add unrelated cosmetic draws while holding generation inputs fixed; verify relevant baselines remain stable. Exercise an extra draw in one generator and confirm it does not advance another generator; do not require unchanged downstream placement when its input geometry/content actually changes. Audit gameplay-affecting global RNG calls and document any intentionally unresolved runtime ones. Bump the save version because generation changes can invalidate development saves. Tests compare repeated outcomes within a build rather than requiring historical scenario snapshots to stay identical across code changes.


### A — Runtime equipment and ordinary consumables through save/load

Exercise: choose a class, acquire a rolled weapon and ordinary consumable, equip/drop/recover, save and reload with identity and rolls preserved.

Land common provenance/identity, fixed/random policies, factory, explicit pool injection, and current-format item/inventory codecs together; bump the save version. Update starting gear, reward sources, authored pickup initialization, enchanting, and version rejection in load/slot listing. Introduce the harness and Resource inspection. Keep existing stack sizes/merge behavior until B introduces `MaxStackSize` and cap enforcement across both factory and inventory; the contracts above describe the completed milestone. Keep the old inventory operation APIs as delegating compatibility entry points until slice B. Existing food keeps its current ordinary consumable behavior until C; do not enable unsaved contamination early.

Verify `ItemFactoryTest`, `ItemSaveCodecTest`, `SaveGameVersionTest`, existing equipment/enchant/determinism/class-save tests, build, resource import, and a focused editor harness exercise. First confirm two identical runtime resources have independent affix/modifier collections and definitions remain unchanged. Floor delta persistence is explicitly not yet delivered.

### B — Stack-safe inventory and successful ownership transfers

Exercise: fill the backpack, add to a compatible stack, split/merge, attempt invalid use/drop, and inspect unchanged ownership on failure.

Land explicit authored stack caps, ownership/quantity checks, result-bearing controller commands, notification guards, UI routing, and prepared drops. Introduce the level-local component for drop preparation; its delta responsibilities arrive in D. Keep codec round trips working for all new entry states. This slice does not imply food can stack once contamination is enabled.

Verify `InventoryOperationsTest` and `ItemLifecycleIntegrationTest`: full-pack merge, incompatible variety, caps, failed splits/merges, failed scene preparation, stale targets, reentrant callbacks, exactly-once consumption, two-ring modifiers, and conserved quantities. Import affected scenes/resources and exercise the same cases through the harness.

### C — Individual food and supported content validation

Exercise: create wholesome and poisoned examples, select one piece, consume it, and save/reload the other without hidden-state leakage.

Land `FoodItem` initialization, edible Resource migration, unconditional non-stacking food, assigned poison configuration, and the corresponding save-version bump. Audit other stackable definitions for the interchangeability contract. Earlier saves are rejected after the version bump; no legacy quantity expansion, poison fallback, or over-capacity conversion is needed. No hunger, spoilage, fuel, or charges.

Verify food creation/use and codec round trips, rejection of previous-version saves, no poison constructor roll, no identification of every apple from one contaminated instance, and no stacking/presentation leak. Inspect and edit eligible food/policy Resources in Godot; demonstrate selecting an existing effect without changing factory code.

### D — Deterministic floor reconstruction with saved differences

Exercise: generate a floor twice with identical initial monster definitions/transforms and item rolls; change it by collecting loot, opening a chest, dropping equipment, and defeating a monster elsewhere. Leave/return/save/load and observe the same baseline with exactly those differences applied.

Land per-depth deltas and a save-version bump, source-derived reward outcomes, deterministic item IDs, and pre-materialization delta application. Reuse the baseline contract established in slice 0. Route loose and dynamic pickups through level-local ownership. Keep existing fog/door and enemy-cleared deltas. A visited unchanged floor needs no complete item list; old versions are rejected.

Verify `LevelItemPersistenceTest` and integration coverage for partial stacks, modified ground items, consumed loot, player drops, cross-floor transfers, zero-reward claimed sources, and a cleared enemy whose exact drop persists at its death location. Regeneration must not advance gameplay RNG/counters, and different chest/kill activation order must not change their base loot. Check notification/save ordering and reject contradictory records without writing partial state. Run `MainSceneSmokeTest` plus editor stairs/save/load checks; document living-enemy reset/persistence semantics separately.

## Verification and outstanding boundaries

For each implementation slice, use `dotnet build "Rogue Gauntlet.sln"` (or `/usr/local/share/dotnet/dotnet` when not on PATH), targeted GdUnit4 tests in the existing project with `.runsettings`/`GODOT_BIN`, and the [godot-mcp workflow](../../.agents/skills/godot-mcp/SKILL.md) for imports, UIDs, scene/resource checks, and editor exercises. Pure DTO/capacity calculations use plain C# tests where possible; Resource copies, effects, scene lifetime, and restoration need the engine. Each named new test file is proposed, not an existing passing check.

Before implementation settles API details, verify runtime diagnostic-property presentation, nested Resource change propagation, scene readiness during restore, and safe file replacement against installed Godot/.NET behavior. No arbitrary runtime-property editing guarantee is made. A new Resource type gets one script file and safe parameterless construction.

Open gameplay choices: stack-cap values and capacity accounting recommendations must be resolved before B's content migration. Backpack acquisition/sizes, required locks, darkness, lantern fuel, charges, shops, restrictive curses, general monster snapshots, and a generic capability system remain outside this milestone. They can use the creation/ownership/codec seams when their behavior is implemented; do not add dormant fields or framework types now.
