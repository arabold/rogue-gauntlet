# Randomness and Seeds

Status: current implementation, October 19, 2026. Generation isolation, runtime item creation, and saved floor differences are implemented; verification is recorded in the item lifecycle program design.

## Existing run and level seeds

`GameSession.StartNewGame` creates `ActiveSeed` from UTC ticks and stores it in `SaveGame.Seed`. Loading restores that seed and the active depth. Starting a run chooses a seed once; reconstruction uses the saved value.

`GameSession.GetLevelSeed(runSeed, depth)` deterministically derives a level seed:

```text
runSeed XOR (depth × 0x9e3779b97f4a7c15)
```

`GameSession.ConfigureLevel` passes the result to `Level.ConfigureGeneration`, which assigns the generator seed and depth. `MapGenerator.Reset` creates a fresh `LevelGenerationContext` for each generation; it does not reseed Godot's global RNG.

## Generation streams and source rewards

`LevelGenerationContext` owns separate `RandomNumberGenerator` instances for layout, monsters, item placement, and decoration. `GenerationRandom` derives them from the level seed and a named domain using length-delimited FNV-1a hashing. Strategies receive their RNG as an invocation argument rather than retaining one on a shared Resource.

| Stream | Current consumers |
| --- | --- |
| Layout | Room selection, footprint construction, procedural contents, and room placement |
| Monsters | Initial encounter count, tile selection, enemy scene, and facing |
| Items | Chest, trap, and loose-item placement |
| Decoration | Corridor and wall mesh variants |
| Source loot | Weighted reward selection and equipment rolls for a stable chest, monster, or loose-item source |

Generated monster spawn points already select a concrete scene before callbacks. General multi-scene `SpawnPoint` instances now select with a source-derived monster RNG, using their persistent ID or level-relative path. Generated rooms assign IDs to authored reward components and spawn points; generated chests and loose items have depth/placement IDs. These IDs are stable within the supported build/content, not across arbitrary generator edits.

`MapGenerator.CreateSourceLootRng(sourceId)` derives a fresh stream from the level seed, loot domain, and source ID. `LootTableComponent.CreateDropRng` uses its authored/generated `SourceId`, or its owner's `persistent_id` metadata. Thus opening source A before B does not change either source's base reward. Loose items use the same source-derived rule and honor entry depth eligibility and quantity scaling. No generation path requests the ongoing gameplay loot counter merely to rebuild a floor.

Related sources: [GenerationRandom](../scenes/levels/generators/GenerationRandom.cs), [LevelGenerationContext](../scenes/levels/generators/LevelGenerationContext.cs), [MapGenerator](../scenes/levels/generators/MapGenerator.cs), and [LootTableComponent](../scenes/components/LootTableComponent.cs).

## Gameplay-time randomness

`GameSession.CreateLootRng` remains the ongoing stream mechanism for actions such as enchantment, summoning, and rewards without a stable source ID. It creates a generator using the active level seed XOR the persisted loot-event counter multiplied by the same constant used in level derivation. It increments `LootRollsConsumed` once per generator request, not once per draw from that generator. Restoring a save restores that continuation counter.

Dynamic sources without an active session use an explicit fixed fallback seed for editor/debug use; they do not promise a persistent dynamic sequence. Other existing gameplay-time randomness, including AI decisions and teleport destinations, is outside this generation-isolation slice. Those calls do not advance the explicitly owned generation streams.

Saves retain carried item identities, equipment rolls, food contamination, and quantities. `LevelItemPersistence` reconstructs initial items and activated rewards before applying saved removals, overrides, and added objects. Activation descriptors retain source table/policy/settings and location so cleared enemies need not exist to reconstruct their drops.

See [GameSession](../scripts/GameSession.cs), [SaveGame](../scripts/saves/SaveGame.cs), and [stats and itemization](stats-and-itemization.md).

## Existing identification randomness

`IdentificationService.Initialize` derives category-specific generators from the run seed and a stable category hash for shuffled appearances. This is already separate from layout and loot randomness. `GameSession` saves discovered type IDs and appearance assignments and restores them on load.

See [IdentificationService](../scripts/IdentificationService.cs) and [item identification](item-identification-system.md) for the implemented mapping and discovery behavior. Existing code contains fallback handling for added catalog entries; the current pre-release policy does not require expanding or preserving cross-version compatibility.

## Existing world differences

The project already reconstructs maps from seeds and stores some changes rather than complete floor snapshots:

- Generated monster spawn points receive deterministic `PersistentId` values based on depth and placement index. `DeathComponent` records cleared IDs; `SpawnPoint.Spawn` skips those entities on regeneration.
- Large gatekeeper placement may retry up to eight layouts. Attempt zero uses the normal layout seed; later attempts derive `fit-attempt:N` layout streams. Encounter/item streams remain independently derived. The same seed/content reproduces the same successful attempt. Authored `GatekeeperSpawnMarker` anchors bypass this search and retain the same gatekeeper persistent ID.
- First-realm nests and gatekeepers share cleared-entity persistence. Nest summons derive RNG and stable IDs from the nest source and finite slot number. Destroyed nests collapse bound survivors; killed slots do not refill. Gatekeeper defeat restores unlocked descent on regeneration. Surviving health, AI positions and summon timers reset. See [first-realm encounters](level-design.md#first-realm-encounter-prototype).
- `WorldSaveData.RevealedLevels` records revealed room IDs and opened door connectors per depth. Fog and door state can be reapplied to the regenerated map.
- `WorldSaveData.ItemChanges` records collected/moved/modified baseline items, added/player-dropped objects, and claimed reward sources per depth. `Level` reconstructs the transient baseline before applying these differences. Inventory and world records use the same item codec.

Encounter placement uses map topology and collision clearance, independently of navmesh bake completion. Navigation-dependent AI still requires a usable navmesh; headless spawn determinism checks do not validate pathfinding.

Relevant sources: [SpawnPoint](../scenes/levels/SpawnPoint.cs), [DeathComponent](../scenes/components/DeathComponent.cs), and [SaveGame](../scripts/saves/SaveGame.cs).

## Existing checks and their scope

| Tests | Existing coverage |
| --- | --- |
| [GenerationRandomTest](../tests/GenerationRandomTest.cs) | Stable, distinct, unambiguously encoded domain/source keys |
| [GenerationBaselineTest](../tests/GenerationBaselineTest.cs) | Domain isolation, source reward order, regenerated map/encounter/loot outcomes, and unchanged gameplay counter |
| [RealmEncounterTest](../tests/RealmEncounterTest.cs) | Authored threat budgets/fallback, finite nests, cleared-source revisits, gatekeeper unlock/restoration, and crossbow shooting/projectile setup |
| [SaveGameVersionTest](../tests/SaveGameVersionTest.cs) | Exact version gate and current-version seed/counter round trips |
| [SeedDeterminismTest](../tests/SeedDeterminismTest.cs) | Stable run/depth seed derivation and differing input seeds/depths |
| [LootDeterminismTest](../tests/LootDeterminismTest.cs) | Equal initial session state yields equal loot-generator seeds/values; successive requests differ |
| [LayoutConnectivityTest](../tests/LayoutConnectivityTest.cs) | Includes repeated same-seed map comparisons for packed and procedural layouts, alongside connectivity coverage |
| [IdentificationServiceTest](../tests/IdentificationServiceTest.cs) | Identification behavior and seeded appearance assignment; consult individual cases for exact scope |

These checks do not imply deterministic replay of physics, AI, or arbitrary gameplay history. Save format 9 rejects previous or unversioned saves before current DTO deserialization; occupied incompatible slots require a new run and are not silently overwritten.

## Intended contract and remaining extensions

The intended same-build contract and pre-release save policy are in [AGENTS.md](../AGENTS.md). Code/content/draw-order changes between builds may change scenarios; historical seed compatibility is excluded.

The [item lifecycle plan](specs/item-lifecycle-plan.md#seeded-baseline-and-delta-contracts) extends this foundation rather than introducing seeded generation from scratch. Explicit generation streams and source-derived loot are implemented. Runtime item identities, factory/codec ownership, and persistence of item removals/overrides/additions are implemented. No untouched-floor snapshots are stored.

Separate generators isolate random consumption, not deliberate data dependencies: changed room geometry can change monster placement. Seeds recreate the initial scenario; injuries, movement, collected objects, and other gameplay changes require saved differences or an explicit reset rule. Full physics/combat replay is outside the current guarantee.
