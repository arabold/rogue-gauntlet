# Dungeon Progression Implementation Plan

Status: proposed remaining work, October 20, 2026. The existing first-realm encounter slice is implemented; no additional gameplay code is implemented by this document. Gameplay requirements live in [Dungeon Layout, Access, and Discovery](../dungeon-exploration-design.md). [Level design](../level-design.md#first-realm-encounter-prototype) owns the implemented baseline; [Randomness and Seeds](../randomness-and-seeds.md) owns existing RNG/save behavior. Follow [AGENTS.md](../../AGENTS.md) and the [program-design skill](../../.agents/skills/program-design/SKILL.md).

## Scope and existing seams

Deliver the Garrison's realm room selection, recipe constraints, composed access, and generated secrets in vertical slices. Reuse existing skeletons, the crossbow variant, finite nests, `RealmEncounterFactory`, `MobSpawnEntry`, and `RealmEncounterController`. Later realms, new monsters, arena lockdown, merchants, environment palette switching, and full boss phases are separate work.

Current call path: `Main` → `GameSession.ConfigureLevel` → `Level.ConfigureGeneration/Generate` → `MapGenerator.GenerateMap` → room factory/layout → corridor connector → loot/navigation/spawn points → local encounter controller → `LevelLoaded`.

The current `MixedRoomFactory` chooses procedural standard rooms with probability 0.4; the authored factory has global entrance/exit/standard/special path lists and no depth context. `PackedRoomLayout` places a compact cluster, not a progression graph. `AStarCorridorConnector` builds connectivity and then connects remaining explicit doorways; this second pass can introduce bypasses. `Door` currently combines interaction, motion, collision, navigation and x-ray presentation, with no lock/discovery capability. `FogOfWar` persists opened connector coordinates through `GameSession`; there is no access-state delta model. Gatekeeper completion already gates both stair triggers and session travel, but has only a text marker.

The highest-risk change is preserving logical route constraints through physical corridor routing, including shared corridor segments. Prove that boundary in slice 1 before adding secrets or mandatory mechanical locks.

## Authored definitions and runtime ownership

The following names are proposed. Existing types named above remain existing types. Each new `[GlobalClass]` Resource gets its own file. Definitions are shared and immutable; per-generation collections, room reuse counts, runtime lock state and discovery state belong to ordinary C# objects or local Nodes.

| Proposed type | Kind and important authoring contract |
| --- | --- |
| `RealmDefinition` | Resource: stable realm ID, exact depth range, three `FloorRecipe` references, existing `RealmEncounterFactory` reference. No depth remapping: Scriptorium remains depths 10–12. |
| `RoomPoolEntry` | Resource: stable entry ID; either authored scene path or `ProceduralRoomBuilder` reference; room role, depth eligibility, positive weight and repeat cap. Validate exactly one room source. |
| `FloorRecipe` | Resource: stable ID, eligible room pool, required slots/obligations, allowed connections/branches, optional lock/secret budgets, bounded placement attempts and references to planned layout/connector strategies. |
| `RoomRequirement` | Resource: stable slot ID, room role/source constraints, must-exist and optional must-traverse destination. Named encounter roles identify a room-local anchor; generated rooms supply a validated floor anchor. Encounter completion is separate from traversal constraints. |
| `FloorPlan`, `FloorPlacement` | Ordinary C# per-generation data: chosen entries, stable slot/connection IDs, planned graph, placed room transforms and assigned doorway endpoints. Not full-floor save DTOs. |
| `LockDefinition` | Resource: accepted key `Item` definitions, optional lockpick tool `Item` definition, pickability and positive pick duration. At least one supported unlocking method. Initially keys/tools are reusable; no new charge fields. |
| `EncounterSealDefinition` | Resource: recipe encounter ID and barrier scene. Completion resolves to that encounter's stable entity/objective ID. |
| `SecretDiscoveryDefinition` | Resource: positive reveal radius and concealed/revealed scene treatments compatible with the host wall. Assigned only to generated secret connections. |

`Level` receives the realm catalog through an exported Resource array assigned by `level.tscn`. It resolves depth locally in `ConfigureGeneration`, validates non-overlapping ranges, and configures the generator and existing encounter factory. Unconfigured later depths retain the current fallback during incremental delivery. `GameSession` remains run/travel coordination, not a realm, connection, or component registry.

The Garrison recipe resources select existing room scenes as placeholders for required roles, with room-local `Marker3D` encounter anchors where needed. Existing `DoorwayMarker` nodes receive stable local endpoint IDs and normal/lock-eligible/authored-lock policy. No fixed secret sockets are added to predefined rooms. Generated corridors and procedural walls supply secret candidates independently. Editor setters validate definitions without generating floors or rolling randomness; recipe edits take effect on explicit regeneration, not on a live floor.

## Planning and geometry contracts

Abbreviated C# contracts, without implementation bodies:

```csharp
// Ordinary helper; outputs remain uncommitted on failure.
FloorPlanResult FloorPlanner.Build(FloorRecipe recipe, uint depth, ulong levelSeed);

// Proposed Resource strategies, invoked only for recipe-configured floors.
PlacementResult PlannedRoomLayout.Place(MapData map, FloorPlan plan, RoomFactory factory,
    RandomNumberGenerator rng);
ConnectionResult PlannedCorridorConnector.Connect(MapData map, FloorPlan plan,
    FloorPlacement placement);

// Add to the existing factory seam; legacy calls remain available.
Room RoomFactory.CreatePlannedRoom(RoomPoolEntry entry, RandomNumberGenerator rng);

// Ordinary helper; includes actual corridor junctions and access dependencies.
FloorValidationResult FloorPlanValidator.Validate(FloorPlan plan, FloorPlacement placement,
    MapData walkableMap);
```

Result types distinguish invalid definitions from a placement attempt that cannot fit; they include slot/connection IDs and readable failure reasons. Planned room construction reuses existing scene loading, procedural builders, rotation, footprints and doorway validation. Extract reusable placement helpers from `PackedRoomLayout` where needed rather than duplicating the packing algorithm. The legacy layout/connector path remains available for floors without recipes.

Reserve required slots first, then ordinary optional content and generated secret destinations. Route only approved graph connections. Explicit doorway markers on this path are endpoints to assign or seal, not permission for the legacy connect-all-doorways pass. A corridor may not merge into an unrelated route or connect extra endpoints unless that junction is in the plan. Close unused openings consistently in geometry, physics, navigation and fog.

Validate the realized tile/connection graph, not just room-center adjacency: removing a must-traverse slot must disconnect its protected destination; treating secrets as unavailable must preserve the required route; a hidden-room destination must lack a visible alternate entrance. Solvability evaluation carries reachable encounter/key conditions so it rejects key-behind-own-lock cycles and access bypasses. Optional locks may have explicit pick-only solutions; do not require tools for the mandatory route in the initial slices.

Failed attempts discard temporary rooms, map changes and spawn descriptors before retrying. Derive attempt RNGs from level seed, recipe ID and attempt ordinal with a fixed configured limit. Optional content can be omitted only within its declared budget. On exhaustion, stop floor loading with a readable error and return safely to the menu without overwriting a save; never emit `LevelLoaded` for an invalid floor.

## Composed runtime access

Proposed Nodes live under `scenes/levels/access/`; `Door` remains the small scene facade.

| Node | Responsibility and injected dependencies |
| --- | --- |
| `AccessConditionComponent` | Abstract Node contract for typed gate references: read-only allowed state and a local `AccessChanged(bool allowed)` signal; lock and encounter-seal components implement it. |
| `DoorMotionComponent` | Own the current tween, physical door state, collision and navigation link; extract these from `Door` while preserving scene behavior. |
| `AccessGate` | Export a typed array of required `AccessConditionComponent` references; all attached conditions must allow access. No conditions means unrestricted access. |
| `LockComponent` | Export `LockDefinition`; own unlocked state and key/tool/access validation. Picking progress belongs to the shared ActorInteractionComponent, not another lock timer. |
| `EncounterSealComponent` | Export seal definition; receive the local encounter completion source from `Level`; drive barrier presentation and allowed state. |
| `SecretDiscoveryComponent` | Export discovery definition; own discovered state, reachable-side proximity detection and concealed presentation. |
| `LevelAccessPersistence` | Bind stable connection IDs to local runtime components; compare baseline and capture/apply access deltas. Not a global lookup service. |

`EncounterCompletionSource` is a level-owned ordinary C# adapter exposing stable source ID, read-only completion and a change event. `RealmEncounterController` supplies it from restored cleared state and the spawned gatekeeper's death signal; it releases subscriptions when the level is cleared. `EncounterSealComponent.Bind` receives this dependency explicitly and never queries an autoload. Motion and discovery publish local state changes for persistence/presentation.

A scene with an ordinary door has motion and interaction. A locked cell adds a gate and lock. A secret door adds discovery, optionally also a lock. A staircase uses a gate and encounter seal without door motion. Discovery is checked before exposing any interaction or lock feedback. The actor-neutral interaction target commits discovery → unlock validation if needed → opening through its Door/Lock handlers; closing remains available after discovery and must not relock the door.

Key contracts:

```csharp
bool AccessGate.AllowsAccess { get; }
AccessAttemptResult LockComponent.TryUnlock(in ActorInteractionContext actor, UnlockMethod method);
bool SecretDiscoveryComponent.TryDiscover(Player actor);
void EncounterSealComponent.Bind(EncounterCompletionSource source);
LevelAccessDeltaSaveData LevelAccessPersistence.Capture();
void LevelAccessPersistence.Apply(LevelAccessDeltaSaveData delta);
```

`AccessAttemptResult` distinguishes committed success, missing key/tool, ineligible method and invalid target state. It is an internal access/commit result, not a channel clock. The [actor action plan](actor-actions-and-interactions-plan.md) owns ActorInteractionContext, accepted/pending/busy/cancelled session outcomes, target reservation and progress. Actors request an interaction offer rather than calling TryUnlock directly. After the shared channel reaches its authored total duration, the target revalidates access and calls TryUnlock idempotently; cancellation mutates neither lock nor inventory. LockDefinition.PickDuration supplies the entire picking duration (for example five/ten seconds), not an additional delay after a one-second input threshold. Matching-key Open uses the ordinary one-second operation. Initial picking deterministically succeeds with the configured reusable tool. Match keys by runtime Item.DefinitionPath against accepted authored definitions; color is a presentation choice. No generic KeyItem fields, consumed-key adapters, random failure economy or charged lockpick types are needed for this slice.

Use local state-change signals after a committed change. Derive collision/navigation, prompts, seal effects and x-ray eligibility from the same component state. Existing decoupled door/fog notifications may remain on `SignalBus`, but they do not request unlock operations. Both `LevelTransitionTrigger` and `GameSession.CanUseTransition` consult the same level-local exit gate; retain the current enforcement while the visible seal is introduced, then remove duplicate gate truth when the component path owns it.

Bind the existing gatekeeper ID from `RealmEncounterController`; derive completed seal state from existing cleared-entity persistence. Move its placement to a recipe encounter anchor when available, preserving the implemented Warden actor and its authored `GatekeeperSpawnMarker` seam. Death removes the barrier automatically. No progression key rolls and no implicit arena lockdown.

## Secrets, fog and navigation

Only planned generated corridor/procedural-wall candidates can become secrets. Each candidate identifies the accessible side, wall span, destination and reserved footprint. Test concealed-wall compatibility in rotated geometry. A hidden chamber may not have another visible entrance; a shortcut may, if allowed by the recipe. Both preserve mandatory constraints.

The hidden destination exists before navigation and occlusion are built. A closed secret connector blocks fog flood and navigation just like a closed ordinary door. Discovery checks distance, accessible side, height and a short unobstructed query to the host wall; do not rely on a spherical overlap through other walls. It reveals the opening treatment, not the destination. Opening enables the connection and normal fog propagation.

Suppress `InteractiveComponent` prompts, door x-ray, map/debug-player indicators and ordinary-door interaction while undiscovered. Update `MapGenerator.UpdateDoorIndicators` and doorway/fog registration to consult discovery instead of merely neighboring revealed tiles. Never disable a concealment collision body before the navigation gap/link policy is established. Rendered editor tests, not only headless assertions, must confirm there is no seam or visibility leak.

## Generation, play and restoration order

```text
GameSession.ConfigureLevel → Level.ConfigureGeneration
  → resolve authored realm/recipe locally; select existing encounter definition
Level.Generate
  → clear old runtime state
  → MapGenerator: build plan → place rooms/secret destinations → route approved edges
      → validate physical topology/access solutions [bounded retries; no partial publish]
      → assign stable connection/encounter/source IDs and instantiate access components
      → build closed-connection navigation/fog boundaries and existing loot/spawn descriptors
  → initialize item baseline and restore item deltas
  → bind local encounters and access sources; capture access baseline
  → restore discoveries/locks/door states and completed seals before interaction
  → restore explored space without exposing undiscovered branches
  → initialize room polling; emit LevelLoaded and enable player interaction
Player interaction / proximity / encounter death
  → local capability commits state → updates motion, navigation, visuals and fog
Save or travel
  → reject while an unlock/item transaction commits → capture item/access deltas
  → GameSession stores supported deltas and proceeds through its existing save/travel path
```

Reorder existing `Level.Generate` initialization as needed so no player frame sees unrestored gates. Keep item baseline ordering intact and test it alongside existing generation/reward tests.

## Identity and save contracts

Use stable recipe slot IDs and connection endpoint IDs scoped by depth; generated branches use deterministic ordinal IDs. Rotate/translate endpoints without changing identity. Room selection/layout/secret choices use layout-domain substreams; encounters retain monster streams and rewards retain source-loot streams. Runtime discovery/picking and cosmetic effects do not advance generation streams. Reproduction is same-build/content only.

Add `WorldSaveData.AccessChanges`, containing per-depth `ConnectionDeltaSaveData` records with connection ID and nullable `Discovered`, `Unlocked`, and `IsOpen` overrides. Store records only when they differ from the immutable generated baseline. Seal completion continues to derive from cleared encounter IDs rather than a second saved boss-defeat flag. Unknown IDs or invalid capability/state combinations reject restoration before enabling interaction.

Replace `RevealedLevelSaveData.OpenedDoors` with stable `ExploredConnectionIds` for exploration history. Current physical openness belongs only to access deltas. Update `FogOfWar`, `MapGenerator.RestoreReveal`, and `GameSession.MarkDoorOpened/GetOpenedDoors` together: reopening exploration history must not physically reopen a door the player closed. Preserve revealed room history separately. Never maintain two authoritative open-state lists.

This changes generation and save schema. Bump `SaveGame.CurrentVersion` when each incompatible slice lands; do not bump it for this documentation change. Reject old/missing versions without conversion or overwriting the slot. Implement no migration, legacy DTO fields or adapters. Verify current-version round trips, including closed explored passages and unlocked-but-closed doors.

## Delivery slices and acceptance

1. **Realm pools and recipe routes.** Add definitions/planner/strategies, scene wiring and Garrison recipes with existing room placeholders. Keep original encounter content and later-depth fallback. Exercise realm eligibility, required-slot placement, a must-traverse checkpoint and intentional loops. Seed sweeps must validate actual geometry, bounded failures and identical outcomes; cramped layouts cannot lose required rooms. Secrets and locks are not active yet.
2. **Composed access and visible stair seal.** Extract door motion, add gates/encounter seal and access persistence, replace opened-door save authority and wire both travel callers. Preserve ordinary doors, fog and AI crossing. Demonstrate boss-sealed descent, death, revisit and reload with the implemented Forsaken Warden. Verify physical barrier, authoritative rejection, no duplicate rewards, and closing/reloading a previously explored door.
3. **Optional mechanical locks.** Add lock definitions/component and integrate with the shared timed interaction executor from the [actor action plan](actor-actions-and-interactions-plan.md); that executor must land before this slice completes. Author pickable prison-cell doors and a matching-key optional door in existing scenes; use the existing lockpick/key Item definitions outside normal loot until their source is deliberately assigned. Test missing tools, correct/wrong keys, reusable tools, interruption, repeated input, alternate entrances, inventory-full acquisition and save/load. No mandatory mechanical locks or probabilistic pick failure in this slice.
4. **Random secret branches.** Add generated candidate/destination selection and discovery components, then fog/x-ray/navigation integration and saved discovery. Verify multiple seeds vary valid walls, authored rooms never acquire fixed secret sockets, undiscovered walls leak no prompt/outline/reveal, and approach through another wall cannot discover them. Test secret-room versus shortcut topology, mandatory-route protection, open/closed restoration and editor navigation after discovery.

For each slice, update [Level design](../level-design.md) and the [roadmap](../development-roadmap.md) only after behavior lands. Authoring acceptance: assign the relevant Resources in the Inspector, regenerate a fixed seed, inspect the realized connections, and exercise interactions through the same components used in normal gameplay. Add a small debug harness only if the normal scene cannot make a boundary reproducible; do not introduce a separate gameplay framework.

## Affected files and verification

```text
+ scenes/levels/progression/*.cs, garrison/*.tres  definitions, planning and recipe strategies above
+ scenes/levels/access/*.cs, *.tscn               capability Nodes and reusable barrier/discovery scenes
~ scenes/levels/Level.cs, level.tscn              realm injection, ordered initialization and persistence
~ scenes/levels/generators/{MapGenerator,RoomFactory}.cs
~ scenes/levels/generators/layouts/PackedRoomLayout.cs  extract shared placement helpers as needed
~ scenes/levels/{DoorwayMarker,FogOfWar,LevelTransitionTrigger}.cs
~ scenes/props/Door.cs, door.tscn                composed interaction/motion and presentation
~ scenes/levels/encounters/RealmEncounterController.cs  recipe anchor and completion binding
~ scripts/GameSession.cs, scripts/saves/SaveGame.cs      focused access capture and version gate
~ scenes/levels/dungeon/rooms/*.tscn              only selected Garrison room endpoints/cell doors
+ tests/{FloorRecipe,AccessGate,SecretDiscovery}Test.cs
~ tests/{FogOfWarReveal,GenerationBaseline,RealmEncounter}Test.cs
~ docs/level-design.md, randomness-and-seeds.md, development-roadmap.md  delivered behavior only
```

Compile Debug and ExportRelease, run focused GdUnit4 suites and existing generation/item-persistence regressions, then import and validate authored scenes with the repository's Godot workflow. Headless tests cover topology/state/AI decisions, not navmesh bake correctness or concealed-wall appearance. Render fixed-seed top-down floors and playtest ordinary/locked/secret connections in the editor. Report remaining visual, navigation and balance limitations instead of treating resource loading as full acceptance.
