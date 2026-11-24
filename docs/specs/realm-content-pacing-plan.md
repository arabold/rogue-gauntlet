# Realm content pacing

Slices 1–3 landed (room windows, headlines, plan windows, shops after each gatekeeper, motifs and the new headline rooms); encounter motifs are proposed. Each realm stages its rooms, floor plans and set dressing across its five floors instead of offering all of them on every floor. Content is introduced, peaks and retires. Every floor has one **headline**: a special room, the realm's shop or its gatekeeper. Themed floors (an undead floor, a crystal floor) bring their rooms, props and, later, monsters together. Realms that share a look still read differently: the Garrison is a keep sinking into its tombs, the Prison an orc warband's jail and the Cathedral an elegant, clerical house that a vampire has made its seat.

Current generation is described in [level design](../level-design.md#current-generation); the monster casts per realm are in the [realm monster roster](realm-monster-roster-plan.md); shops are in [shops](../shops.md).

## Goals

- A floor shows a subset of its realm's rooms, and neighbouring floors differ. Walking down a realm reads as a progression.
- Rooms no longer leak across realms that share a theme. Today the Prison and the Cathedral build from the garrison theme, so their cluster floors offer the Garrison's barracks, libraries, crypts and graveyard.
- Each floor has one headline. The headline is the first thing a floor is remembered by, and runs rotate which headline lands where.
- Rooms, set dressing and monsters that belong together meet: the graveyard and the bone piles on the Garrison floors where skeleton warbands roam, crystal clusters on the floors deep in the Mines.
- A shop greets the hero after each gatekeeper: on the first floor of every realm after the Garrison.
- Every floor stays fillable: enough ordinary rooms, and the compact/medium size mix.

## Starting point

- [`RoomPool`](../../scenes/levels/generators/assembly/RoomPool.cs) holds every room. `ForRealm(theme)` keeps the definitions tagged with the realm's theme (`RoomDefinition.Realms`), and `ForFloor(levelSeed)` rolls each `FloorChance` below 1 once per floor. The rolls are independent, so a landmark can appear on all five floors of a realm or on none.
- Floor plans with their own room lists (prison, cathedral, cloister) draw through the same `Offers` check, so pool gating already reaches them.
- [`Realm.FloorPlans`](../../scenes/levels/generators/Realm.cs) are weighted but the same on every floor.
- Furnishing steps filter by theme (`FurnishingStep.Realms`) and wall style (`WallStyles`), never by floor.
- Encounter packs have depth windows (`MinimumDepth`, `MaximumDepth`): Garrison skeleton warbands from depth 3, orc warbands from depth 8, golem pairs from depth 18. Content follows the same shape.
- Layouts draw ordinary rooms at random from the pool. Nothing guarantees that a given room lands on a floor; only the entrance, the exit and the boss floor's `boss_exit` group are placed on purpose.
- The only shop is the Garrison's quartermaster, on the Warden's floor (`ShopPlacementProfile.FloorIndex = 4`, 0-based).
- Two run-level schedules exist and are the model for this one: [`UniqueFloorPlan`](../../scenes/levels/generators/UniqueFloorPlan.cs) (the labyrinth's one floor) and the [fountain schedule](../level-design.md#fountains). Both draw from the run seed alone, so every floor of a run agrees without shared state.

## Model

1. **Realm floor.** A floor's number within its realm, 1 to 5 (depth minus `FirstDepth` plus one). Below the deepest band the last floor's schedule carries on. Shops move to the same 1-based numbering.
2. **Window.** `FirstFloor`..`LastFloor`: the floors an entry may appear on. A room introduced late starts at 3. A room that retires stops at 2.
3. **Headline.** Each floor has exactly one:
   - **The shop**, on floor 1 of each realm after the Garrison.
   - **The gatekeeper**, on a realm's last floor when its encounter plan has one.
   - **One headline room**, on every other floor, drawn per run from the realm's headline candidates whose window and motif fit. A candidate headlines at most once per realm, so a realm with more candidates than slots rotates them between runs; some runs skip the library, others the alchemist.

   A headline room is offered only on its own floor and is placed before the layout's ordinary rooms.
4. **Motif.** A named theme a realm schedules on some floors (`undead`, `crystals`, `warband`), with a window and a count of floors drawn per run. A room (headline or ordinary), a furnishing step and later an encounter pack can require one. Everything tied to `undead` lands on the same floors.

The realm owns the schedule, the way an encounter factory owns its packs. A room definition keeps what it is and where it can be built: its shapes, styles, steps and theme tags. A realm decides when it appears. One file per realm shows the whole arc.

## Contracts

Everything below exists except where marked proposed.

```csharp
// Window shared by every realm schedule entry. One [GlobalClass] per file.
[Tool, GlobalClass] public abstract partial class RealmContent : Resource
{
	[Export(PropertyHint.Range, "1,10")] public int FirstFloor { get; set; } = 1;
	[Export(PropertyHint.Range, "1,10")] public int LastFloor { get; set; } = 5;
	public bool Spans(int floor);
	public bool FitsIn(int floorCount);
}

[Tool, GlobalClass] public partial class RealmRoom : RealmContent
{
	[Export] public RoomDefinition Room { get; set; }
	/// A headline candidate: placed on at most one floor of its window per run, and only there.
	[Export] public bool Headline { get; set; }
	/// Only on floors carrying this motif; empty means any floor of the window.
	[Export] public string Motif { get; set; } = "";
	/// Ordinary rooms: multiplies the definition's Weight on this realm's floors. Headlines: the draw weight among candidates.
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1f;
}

[Tool, GlobalClass] public partial class RealmMotif : RealmContent
{
	[Export] public string Id { get; set; } = "";
	/// How many floors of the window carry it in one run, drawn from the run seed; 0 means every floor of the window.
	[Export(PropertyHint.Range, "0,10")] public int MinimumFloors { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MaximumFloors { get; set; }
}

// Runtime only (plain C#): one floor's view of its realm's schedule.
public sealed class RealmFloor
{
	public Realm Realm { get; }
	public int Number { get; }                          // 1-based floor within the realm
	public IReadOnlySet<string> Motifs { get; }
	public RoomDefinition Headline { get; }             // null on shop and gatekeeper floors
	public IReadOnlyList<RoomDefinition> Rooms { get; } // ordinary rooms on offer
	public bool Offers(RoomDefinition definition);
	public float WeightOf(RoomDefinition definition);
	public bool IsScheduled { get; }                    // false for a realm without a schedule: every catalog room of its theme
	/// `reserved`: floors whose headline is the shop or the gatekeeper (the generator knows the encounter plan).
	public static RealmFloor Create(Realm realm, uint depth, ulong runSeed, Func<int, bool> reserved);
}
```

Changes to existing types:

- `Realm` (landed):
  - adds `RealmRoom[] Rooms`, `RealmMotif[] Motifs`, `int FloorCount`, `FloorOf(depth)` and `DepthOf(floor)`
  - `PickFloorPlan(rng)` becomes `PickFloorPlan(rng, int floor)`
  - `ShopsAt(depth)` uses the 1-based floor
  - `Validate` also checks:
    - windows inside `1..FloorCount`
    - each scheduled room supports the realm's theme
    - room motifs name motifs the realm schedules
    - every floor has at least one floor plan
    - each room is scheduled once, and every floor offers ordinary rooms
    - (slice 3) every floor without a shop or gatekeeper has at least two headline candidates, so runs vary
- `ShopPlacementProfile` (landed): `FloorIndex` (0-based) becomes `Floor` (1-based), matching the schedule.
- `FloorPlanChoice` (landed): adds `FirstFloor` and `LastFloor`, so the cloister can hold the upper floors and the cathedral plan the lower ones.
- `FloorBuildContext` (landed): adds a `HeadlineSlot` for the attempt. Every layout places the headline before its other ordinary rooms:
  - packed, hallway and sparse layouts as their first ordinary room
  - the courtyard layout as its first side room
  - the boss layout never gets one

  Each attempt tries it first, up to `HeadlineSlot.Tries` times; one that never fits gives way to the attempt's ordinary rooms and is logged, rather than failing the floor.
- `RoomPool` (landed):
  - its `Rooms` list stays as the catalog of every room: a realm with a schedule draws only the rooms it schedules, and a realm without one (test fixtures, previews) keeps every catalog room of its theme
  - `ForFloor(levelSeed)` becomes `ForFloor(RealmFloor floor)`; `ForRealm(theme)` stays
  - `Offers` asks the floor, and weights multiply `WeightOf`
- `RoomDefinition` (landed): `FloorChance` goes, replaced by headlines and windows. `Realms` stays, but it now means the themes a room can be built in, not where it appears. `MaxPerFloor` stays.
- `FurnishingStep` (landed): adds `string[] Motifs`. The step runs only on floors carrying one of them, and none means every floor. It is checked beside `Realms` and `WallStyles` in `RoomAssembler`.
- `RoomAssemblyOptions` (landed): adds `IReadOnlySet<string> Motifs`. Null takes every step, like `Realm = null`, for previews.
- `RoomPool.CreateFrom` (landed) fills `Motifs` from its floor when the caller gives none, so `FloorProfile.CreateRoomOptions` keeps its signature.
- `GenerationRandom`: adds `const string RealmContent = "realm-content"`. Motifs draw per entry, keyed by realm and motif ID. Headlines draw per floor, keyed by realm and floor number, so adding an entry never reshuffles unrelated floors' motifs.

No shims: every `FloorChance` became a headline or a window and shops moved to 1-based floors in the same change that filled the realms.

## Call order

```text
MapGenerator.PrepareFloor
├─ RealmAt(depth)
├─ MobFactory.PlanEncounters(depth)                     gatekeeper floor known before the schedule
├─ RealmFloor.Create(realm, depth, RunSeed, reserved)
│  │  run-seed streams only: every floor and every layout attempt of a run agree; the level seed is not used
│  │  reserved(floor) = the realm has a shop on it ∨ its encounter plan has the gatekeeper on it
│  ├─ motifs: each RealmMotif picks its floors (window, count)
│  ├─ headlines: floors 1..FloorCount in order; a reserved floor takes none; otherwise draw by weight among
│  │  candidates in window ∧ motif present ∧ not yet used in the realm; none eligible leaves the floor without one
│  └─ ordinary rooms: in window ∧ motif present; headline candidates are never ordinary
├─ RoomPool.ForFloor(realmFloor)                        entrances/exits by theme; rooms = realmFloor.Rooms
└─ FloorPlanFor(encounter)                              gatekeeper → boss profile; unique plan; else realm.PickFloorPlan(rng, realmFloor.Number)
MapGenerator.GenerateRooms
└─ Layout.GenerateLayout(context with Headline)
   ├─ places the headline first (retries the attempt when it does not fit)
   └─ RoomPool.CreateFrom(def, rng, profile.CreateRoomOptions(realmFloor, …))
      └─ RoomAssembler.Build: a step whose Motifs miss realmFloor.Motifs is skipped, like a step of another theme
```

A floor plan's own room list (prison cells, cathedral side chapels) still draws through `Offers`. A plan room that the realm schedules obeys its window. A plan room it does not list is always on offer when that plan is picked, because the plan is what brought it.

## Shops

Shops stand on the first floor of each realm after the Garrison: depths 6, 11, 16 and 21, right after the Warden, the Minotaur and the realms whose gatekeepers come later. The quartermaster leaves the Warden's floor, so the Garrison has none. Until [realm merchants](shop-design.md) exist, every realm stands the quartermaster, with a realm-specific shop ID so each keeps its own stock and save key. The shop is its floor's headline.

## Distribution

The legend for the tables below:

| Symbol | Meaning |
| --- | --- |
| ● | On every floor of the window |
| ◆ | Only on floors carrying the realm's motif |
| *plan* | Comes with a floor plan |
| — | Absent |
| **new** | A room that does not exist yet (see [New rooms](#new-rooms)) |

Monster rows restate the current encounter windows, so the rooms can be read against them. Headline candidates list their windows; each run places one per open floor.

### Garrison, depths 1–5: from the keep down into its tombs

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | rats, skeleton pairs and warriors | + patrols, sentries | + warbands | warbands | warbands, the Warden |
| *Headline* | room | room | room | room | the Warden |
| *Plans* | cluster, hallway | cluster, hallway | cluster, hallway | cluster, hallway | boss |
| *Motifs* | | | `undead` on 1–2 of 3–4, and always on 5 | `decay` 4–5 | |
| passage | ● | ● | ● | ● | ● |
| storage | ● | ● | ● | ● | — |
| barracks, kitchen | ● | ● | ● | — | — |
| mess hall, root cellar | ● | ● | — | — | — |
| fenced walkway, partitioned storage | ● | ● | ● | — | — |
| guardroom | — | ● | ● | ● | ● |
| crypt | — | — | ◆ | ◆ | ● |
| ruins | — | — | ● | ● | ● |
| prison | — | — | — | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| great hall | 1–2 |
| training yard **new** | 1–2 |
| library | 2–3 |
| alchemist | 2–3 |
| chapel | 2–4 |
| graveyard (◆ `undead`, weighted high) | 3–4 |
| treasury | 4 |

The Garrison is the graveyard's only realm. Its upper floors are the living garrison. The tombs open from floor 3, where the skeleton warbands start: the graveyard needs `undead`, so it headlines one of those floors with the crypts and bone piles around it. The prison rooms on floors 4–5 foreshadow the next realm.

### Prison, depths 6–10: the orc warband's jail

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | bats, nymphs, orcs | + bat roosts | + orc warbands | warbands | the Minotaur |
| *Headline* | shop | room | room | room | the Minotaur |
| *Plans* | prison, hallway | prison, cluster, hallway | prison, cluster, hallway | prison, cluster | boss |
| *Motifs* | | | `warband` 3–5 | | |
| prison block, guardroom, storage | ● | ● | ● | ● | ● |
| cells | *plan* | *plan* | *plan* | *plan* | — |
| partitioned storage | ● | ● | ● | ● | — |
| kitchen | ● | ● | — | — | — |
| fenced walkway | — | ● | ● | ● | — |
| barracks, mess hall | — | — | ◆ | ◆ | ◆ |
| ruins | — | — | ● | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| warden's office **new** | 2–3 |
| interrogation | 2–4 |
| execution yard **new** | 3–4 |
| fighting pit **new** (◆ `warband`) | 3–4 |
| treasury | 4 |

The Prison shares the garrison's walls but none of its great halls, libraries, chapels or tombs. The interrogation room becomes a headline, and the warband floors bring back barracks and the mess hall, with weapon racks and war maps.

### Sewer, depths 11–15: from the prison's drains into the deep cisterns

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | rats, blobs, snakes | + slime vats, blob trios, snake pairs | | | |
| *Headline* | shop | room | room | room | room, until the gelatinous cube |
| *Plans* | large cluster, cluster | large cluster, cluster | large cluster, cluster | large cluster | large cluster |
| *Motifs* | | | | `flooded` 4–5 | |
| drain tunnel, junction | ● | ● | ● | ● | ● |
| sewer vault | ● | ● | ● | ● | — |
| prison lock-up | ● | — | — | — | — |
| storage, partitioned storage | ● | ● | — | — | — |
| rat warren | — | ● | ● | ● | ● |
| ruins | — | — | ● | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| smugglers' den **new** | 2–3 |
| cistern hall | 2–4 |
| outfall chamber **new** | 3–5 |
| rat king's nest **new** | 3–5 |
| sunken shrine **new** | 4–5 |

### Mines, depths 16–20: a working mine giving way to crystal caves and ruins

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | golems, bats, snakes | + bat roosts | + golem pairs | | |
| *Headline* | shop | room | room | room | room, until the umber hulk |
| *Plans* | mines, large cluster | mines, large cluster | mines, large cluster | mines | mines |
| *Motifs* | | | `crystals` 3–5 | | |
| timbered mine | ● | ● | ● | ● | ● |
| ore chamber | ● | ● | ● | ● | — |
| foreman's hut | ● | ● | ● | — | — |
| cavern, root passage | — | ● | ● | ● | ● |
| ruins, ruined shrine | — | — | — | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| mine shaft **new** | 2–3 |
| smelter **new** | 2–3 |
| crystal cavern | 3–5 |
| temple hall | 4–5 |

The crystal cavern is a deep-mine sight from floor 3 on. `crystals` also switches the caverns' and ore chambers' crystal steps on, so floors 1–2 show ore and timber and floors 3–5 glow whether or not the crystal cavern headlines.

### Cathedral, depths 21–25: the vampire's seat

The Cathedral is elegant and clerical: candlelit chapels, carpets and gilt, a cloister above and the cathedral itself below. Its master is a vampire. The upper floors are still the clergy's. The lower ones are the vampire's: blood-red hangings, coffins and banquets. Nothing here is a ruin or a graveyard; decay belongs to the Garrison.

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | planned: gargoyles, wraiths, ghosts, vampire bats, phantoms, medusa | | | | the vampire (proposed gatekeeper) |
| *Headline* | shop | room | room | room | room, until the gatekeeper |
| *Plans* | cloister, cluster | cloister, cathedral | cloister, cathedral | cathedral | cathedral |
| *Motifs* | | | | `vampire` 4–5 | |
| storage | ● | ● | — | — | — |
| courtyard, monk cell, scriptorium, mess hall, kitchen | *plan* | *plan* | *plan* | — | — |
| chapel | ● | ● | ● | ● | — |
| nave, side chapel | — | *plan* | *plan* | *plan* | *plan* |
| crypt | — | — | — | ◆ | ◆ |

Headline candidates:

| Room | Window |
| --- | --- |
| library | 2–3 |
| baptistry **new** | 2–3 |
| bell tower **new** | 2–4 |
| reliquary **new** | 3–4 |
| bishop's throne room **new** | 3–4 |
| banquet hall **new** (◆ `vampire`) | 4–5 |
| the vampire's crypt **new** (◆ `vampire`) | 4–5 |

### Motif-driven set dressing

Furnishing steps with `Motifs` let one room type change with the floor:

| Motif | Realm | Furnishing it switches on |
| --- | --- | --- |
| `undead` | Garrison | bone piles, open sarcophagi, grave pedestals and skull candles in crypts, guardrooms and ruins |
| `decay` | Garrison | rubble, webs and broken bookcases, tables and statues instead of whole ones |
| `warband` | Prison | weapon racks, war maps and training dummies in guardrooms and barracks |
| `flooded` | Sewer | pools in junctions and vaults |
| `crystals` | Mines | crystal clusters and veins in caverns and ore chambers |
| `vampire` | Cathedral | red hangings and carpets, coffins, goblets and candelabras in chapels, naves and crypts |

Wall and floor styles could take the same `Motifs` filter later, for example mossy walls on deep floors or red carpets in the vampire's halls. They are not in this plan.

## New rooms (landed)

Headlines need at least two candidates per open floor, so runs vary. These room types under `scenes/levels/rooms/room_types/` supply them, built from existing props plus the new ones listed, made with the [furnishing workflow](../../asset-studio/README.md#room-furnishings).

| Realm | Room | Built from | New props |
| --- | --- | --- | --- |
| Garrison | training yard | rows of training dummies and targets, weapon racks, armor stands, benches | archery target |
| Prison | warden's office | desk station, bookcases, scroll racks, chests, armor stand, keyring, banners | desk |
| Prison | execution yard | gallows, stocks, chopping block, iron maiden, hanging cages, braziers | gallows |
| Prison | fighting pit | fighting ring, spectator benches, barrels, weapon racks, braziers | fighting ring (colliders on its stakes only) |
| Sewer | smugglers' den | counting or guard table, crate and barrel stacks, sacks, kegs, coins | — |
| Sewer | outfall chamber | water channel, pipe outlets, sluice gates, valves, flotsam | — |
| Sewer | rat king's nest | the heap, hay bales, bone piles, goo, webs | the rat king's heap |
| Sewer | sunken shrine | centred basin, altar, statues, candles | — |
| Mines | mine shaft | the shaft, ore piles, winch, carts, lanterns, tools | mine shaft |
| Mines | smelter | furnace, workbench, ore piles, tool racks | smelting furnace |
| Cathedral | baptistry | dry fountain, statues, candelabras, pews, blue hangings | dry fountain (a filled one reads as usable) |
| Cathedral | bell tower | bell, stores, benches, banners, webs | church bell |
| Cathedral | reliquary hall | reliquary cabinets, altar, candles, offerings | reliquary cabinet |
| Cathedral | throne room | throne, statues, candelabras, red hangings, rug floors | bishop's throne |
| Cathedral | banquet hall | long laden tables, candelabras, kegs, red hangings | — |
| Cathedral | vampire's crypt | coffins, sarcophagi, candelabras, red hangings, webs | coffin, open and closed |

## Slices

1. **Schedule core (landed).** This slice adds the following:
   - `RealmRoom` with windows, `RealmFloor` and the `RoomPool` floor view
   - headlines, with `FloorBuildContext.Headline` placed by every layout
   - shops moved to 1-based floors and onto floor 1 of realms 2–5
   - `FloorChance` dropped

   Each realm is migrated with the windows above for existing rooms, and its current `FloorChance` rooms become its headline candidates. Floors whose candidates are still missing (the new rooms) may go without a headline for now.
   - Verify:
     - a `RealmScheduleTest` over 200 run seeds per realm checks windows, one headline per open floor, no repeated headline, shops on floor 1 and determinism
     - `ShopTest` updated to the new floors
     - `GeneratedFloorSweepTest` and the room-assembly suites pass
     - bump `SaveGame.CurrentVersion`
2. **Motifs (landed).** Add `RealmMotif`, `RealmRoom.Motif`, `FurnishingStep.Motifs` and `RoomAssemblyOptions.Motifs`. Author the motifs and their steps above, and give the room harness a realm-floor picker and motif toggles.
   - Verify:
     - assembly tests show that a motif step runs only with its motif
     - the schedule test shows that motif rooms appear only on motif floors
     - captures of an undead and a crystal floor
     - bump the save version
3. **Headline rooms, realm by realm (landed).** Build the new rooms and their props in this order:
   - Prison: it shares the Garrison's look and needs separating most
   - Cathedral: together with the floor-plan props
   - Sewer
   - Mines
   - Garrison: the training yard

   Each realm lands when its headline floors all have two candidates.
   - Verify:
     - room harness captures of each new room
     - the schedule test's candidate check passes for that realm
4. **Encounters follow motifs (optional).** `EncounterPack` gains `Motifs`, and `MobFactory.PlanEncounters(depth)` becomes `PlanEncounters(RealmFloor)`, so skeleton warbands weight up on `undead` floors. A room may also name the packs that prefer to spawn in it, so skeletons rise in the graveyard and bats hang in caverns. This touches every encounter resource and the spawn placement, so it waits until 1–3 have played well.

Slices 1 and 2 change generation, and so does each realm in slice 3; each bumps the save version. Slice 1 carries the risk: the pool rewrite, required headline placement in five layouts, and the test fixtures.

## Affected files

```text
+ scenes/levels/generators/RealmContent.cs, RealmRoom.cs, RealmMotif.cs, RealmFloor.cs
~ scenes/levels/generators/Realm.cs, FloorPlanChoice.cs, FloorProfile.cs, FloorBuildContext.cs, MapGenerator.cs, GenerationRandom.cs
~ scenes/levels/generators/layouts/*                  place the headline first; pass the realm floor into CreateRoomOptions
~ scenes/levels/generators/assembly/RoomPool.cs, RoomDefinition.cs, RoomAssembler.cs, steps/FurnishingStep.cs
~ scenes/shops/ShopPlacementProfile.cs, quartermaster_placement.tres (+ one placement per realm)
~ scenes/levels/realms/*.tres                          room and motif schedules, plan windows, shops
~ scenes/levels/rooms/room_types/*.tres                FloorChance removed; motif steps; new headline rooms (slice 3)
~ scenes/debug/rooms/RoomGenerationHarness.cs          realm floor and motif preview
~ tests/TestFactories*.cs, ShopTest.cs                 shop floors
+ tests/RealmScheduleTest.cs
~ scripts/saves/SaveGame.cs                            version bump per slice
~ docs/level-design.md, docs/shops.md, scenes/levels/generators/assembly/README.md
```

## Open decisions

1. **The Cathedral's gatekeeper.** The [roster plan](realm-monster-roster-plan.md) proposes the lich, which summons the Garrison's skeletons. A vampire lord fits the Cathedral as the vampire's seat better and leaves the undead to the Garrison. Choosing it means a new monster concept in place of the lich, and an update to the roster plan.
2. **Merchants.** Every realm stands the quartermaster until realm merchants exist. A cheap step between the two would be a reskinned keeper per realm, such as an orc fence in the Prison or a gnome trader in the Mines.
3. **Bridging rooms.** Last floors preview the next realm's rooms where the themes allow it (prisons on the Garrison's floors 4–5). The sewer and the mines have their own themes, so the Prison cannot preview drains. A shared transition room per theme pair is possible, but it is not planned here.
