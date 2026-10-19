# Randomness and Seeds

Status: current implementation, inspected October 19, 2026. The final section lists proposed extensions separately. This document records existing behavior; no code changes or new test runs accompany it.

## Existing run and level seeds

`GameSession.StartNewGame` creates `ActiveSeed` from UTC ticks and stores it in `SaveGame.Seed`. Loading restores that seed and the active depth. Starting a run chooses a seed once; reconstruction uses the saved value.

`GameSession.GetLevelSeed(runSeed, depth)` deterministically derives a level seed:

```text
runSeed XOR (depth × 0x9e3779b97f4a7c15)
```

`GameSession.ConfigureLevel` passes the result to `Level.ConfigureGeneration`, which assigns the generator seed and depth. `MapGenerator.Reset` calls `GD.Seed(Seed)` before generating the map. Layout, room/tile factories, procedural-room decisions, prop placement, and encounter placement currently draw from that seeded global sequence through `GD` calls and collection random selection. They are not yet separately owned RNG streams per subsystem.

Relevant sources: [GameSession](../scripts/GameSession.cs), [Level](../scenes/levels/Level.cs), and [MapGenerator](../scenes/levels/generators/MapGenerator.cs).

## Existing loot sequence

`GameSession.CreateLootRng` creates a separate `RandomNumberGenerator` for the next loot event, using the active level seed XOR the persisted loot-event counter multiplied by the same constant. It increments `_lootRollsConsumed` once per generator request, not once per random number drawn from that generator.

`LootTableComponent.DropLoot` uses this generator for drop chance, number of drops, weighted selection, and equipment rarity/affixes. `MapGenerator.PlaceLooseItems` also requests it for loose-item outcomes. Rolled equipment duplicates its definition through `LootRoller` and retains source definition path, rarity, and affixes.

Saves persist `LootRollsConsumed`, and inventory saves persist equipment rolls. This supports continuation of the loot-event sequence and exact restoration of carried equipment. It does not make every chest or monster's loot a pure function of its source ID: current outcomes also depend on event order and the counter. Rebuilding a floor currently requests new loose-item loot RNG, so it is not yet a no-side-effect reconstruction of original item outcomes.

Relevant sources: [LootTableComponent](../scenes/components/LootTableComponent.cs), [LootRoller](../scenes/items/affixes/LootRoller.cs), and [SaveGame](../scripts/saves/SaveGame.cs). Equipment rules remain in [stats and itemization](stats-and-itemization.md).

## Existing identification randomness

`IdentificationService.Initialize` derives category-specific generators from the run seed and a stable category hash for shuffled appearances. This is already separate from layout and loot randomness. `GameSession` saves discovered type IDs and appearance assignments and restores them on load.

See [IdentificationService](../scripts/IdentificationService.cs) and [item identification](item-identification-system.md) for the implemented mapping and discovery behavior. Existing code contains fallback handling for added catalog entries; the current pre-release policy does not require expanding or preserving cross-version compatibility.

## Existing world differences

The project already reconstructs maps from seeds and stores some changes rather than complete floor snapshots:

- Generated monster spawn points receive deterministic `PersistentId` values based on depth and placement index. `DeathComponent` records cleared IDs; `SpawnPoint.Spawn` skips those entities on regeneration.
- `WorldSaveData.RevealedLevels` records revealed room IDs and opened door connectors per depth. Fog and door state can be reapplied to the regenerated map.
- Inventory saves record carried items and equipment. Complete remaining-floor-item state and player-dropped-item persistence are not implemented.

`SpawnPoint.Spawn` still chooses from its scene array using global `PickRandom` at spawn time. Earlier generation also selects enemy scenes through `MobFactory`; generated spawn arrays may contain only that selected scene. Do not infer an independent seeded monster stream, or guaranteed variant stability for every authored multi-scene spawn, from deterministic positions alone. Deferred callback/global draw order remains part of the current behavior.

Relevant sources: [SpawnPoint](../scenes/levels/SpawnPoint.cs), [DeathComponent](../scenes/components/DeathComponent.cs), and [SaveGame](../scripts/saves/SaveGame.cs).

## Existing checks and their scope

| Tests | Existing coverage |
| --- | --- |
| [SeedDeterminismTest](../tests/SeedDeterminismTest.cs) | Stable run/depth seed derivation and differing input seeds/depths |
| [LootDeterminismTest](../tests/LootDeterminismTest.cs) | Equal initial session state yields equal loot-generator seeds/values; successive requests differ |
| [LayoutConnectivityTest](../tests/LayoutConnectivityTest.cs) | Includes repeated same-seed map comparisons for packed and procedural layouts, alongside connectivity coverage |
| [IdentificationServiceTest](../tests/IdentificationServiceTest.cs) | Identification behavior and seeded appearance assignment; consult individual cases for exact scope |

These tests do not by themselves prove complete actor/item reproduction after arbitrary gameplay. Current documentation is based on source inspection, not a fresh test execution.

## Intended contract and remaining extensions

The intended same-build contract and pre-release save policy are in [AGENTS.md](../AGENTS.md). Code/content/draw-order changes between builds may change scenarios; historical seed compatibility is excluded.

The [item lifecycle plan](specs/item-lifecycle-plan.md#seeded-baseline-and-delta-contracts) extends this foundation rather than introducing seeded generation from scratch. Proposed work includes explicit level/monster/item RNG ownership, source-derived deferred loot, deterministic item identities, and persistence of item removals/overrides/additions. These are not current behavior.

Separate generators isolate random consumption, not deliberate data dependencies: changed room geometry can change monster placement. Seeds recreate the initial scenario; injuries, movement, collected objects, and other gameplay changes require saved differences or an explicit reset rule. Full physics/combat replay is outside the current guarantee.
