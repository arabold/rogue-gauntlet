# Room-owned secret entrances

Room-owned secret doors have replaced generated secret pockets; the remaining work is playtesting and broader authored content. It builds on [level generation](../level-design.md), [door access](../door-access.md), [dungeon design](../dungeon-exploration-design.md), the [roadmap](../development-roadmap.md), [README](../../README.md) and [architecture](../../ARCHITECTURE.md). The treasury is included in the Garrison room pool.

## Contract

`RoomType` remains a shared immutable `[GlobalClass] Resource`. Add Inspector properties `SecretDoorChance` (0–1, default 0, per doorway) and `SecretDetection` (`SecretDetectionProfile`, required when the chance is positive). A successful secret roll creates a door regardless of `DoorChance`; other doorways retain the existing door/frame/open selection. A room's doorway location, connector ownership and corridor routing do not change when concealed. The treasury has one doorway, `SecretDoorChance = 0.8` and a detection profile.

The room assembles the secret door and its wall disguise together at the doorway edge. Its disguise uses the room wall library and the same 4-unit span, with a per-instance wall GridMap parented to the door. The `SecretDoorComponent` continues to own discovery and the reversible concealed/revealed state. The disguised surface belongs to the door instance and must rotate and translate with it; discovery captures its mesh, removes its wall collision and exposes the existing closed door. Room-owned secrets can be discovered from either side, including after room rotation. No shared Resource holds mutable per-room state.

Choose secrecy from an independent derived layout stream keyed by room type and doorway index. Discovery uses a separate derived stream keyed by the room seed and doorway index. Same build/content and inputs reproduce doorway choices and layouts; current-version saves persist discovery and open/locked deltas by the existing stable door identity. Bump `SaveGame.CurrentVersion` for changed generation; reject older saves, with no migration.

The old pocket allocation and its profile budgets are removed. Room-owned secret doors register through the existing `Floor.Doors` path, and undiscovered connectors are blocked for free-access and lock/key validation. Secrets can lie on required progression routes; they do not cause a floor retry. Generated keys must remain reachable from the entrance without crossing undiscovered secrets or locked doors. Required entry and exit room definitions default to no secrets.

## Flow

```text
RoomAssembler.TryBuild
  choose footprint and doorways
  choose wall style and independently roll each doorway's secrecy
  build room boundary with doorway gaps
  instantiate normal door or secret door with its own 4-unit wall disguise
  finish collision/reservations, furnish and publish room
MapGenerator
  place and rotate rooms, register their doors, route corridors and build boundary walls
  configure access, placing keys where they are reachable before secrets or locks
  record generated baselines, then restore saved door deltas before gameplay
SecretDoorComponent.Reveal / Restore
  swap disguise and closed door together; notify observers only after state changes
```

The boundary fit reserves the wall/doorframe footprint for a disguise; the [remaining wall-piece plan](exact-wall-placement-plan.md) covers additional families. Detection, fog, physical collision, visual reveal and closed-door navigation must remain functional.

## Slices and verification

1. **Implemented:** per-room secret selection and a room-owned disguise. Exercise the treasury and a regular multi-door room, all four rotations, probability extremes, deterministic seeds, collision and visual reveal. Preserve the world-effect harness.
2. **Implemented:** remove pocket generation and update access/reachability and save/version handling. Test optional secret-only rooms, alternate visible routes, entrance/exit protection, source reachability, fog, discovery and current-version save/load. Replace pocket-specific tests with behavior checks for room-owned secrets.
3. **Implemented:** integrate concealed doorway/frame footprints into both boundary fits. Room and floor tests check overlaps, including junctions; the short wall has a matching box collider. Remaining work is broader visual playtesting and selecting production room types.

Relevant files: `assembly/{RoomType,RoomAssembler}.cs`, `assembly/surfaces/WallStyle.cs`, `Room.cs`, `access/{SecretDoorComponent,LevelAccessProfile}.cs`, `generators/{MapGenerator,MapGenerator.Access,GeneratedFloor}.cs`, corridor decoration and discovery consumers, treasury and access `.tres` files, `SaveGame.cs`, access/room/wall tests, [door access](../door-access.md) and [level design](../level-design.md). Run the documented build, isolated GdUnit4 room/access/save tests, Godot import, main-scene smoke and windowed harness inspection. Navmesh-dependent movement still needs editor verification.
