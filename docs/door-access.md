# Doors, keys and secret rooms

Implemented doors, locks, keys and secret doors. [Dungeon design](dungeon-exploration-design.md) owns the intended access and discovery rules; this document describes current behavior and authoring. The [progression plan](specs/level-progression-prototype-plan.md) keeps the visible encounter seal.

## Runtime responsibilities

`Door` owns motion, physical collision, interaction offers and presentation. Its frame is ordinary solid collision; the leaf's collider closes the doorway while the door is shut. An open leaf, and one mid-swing, lets actors walk through it, so a door standing open is never in the way. The shut leaf's footprint is the [navigation gate](level-design.md#doors-and-enemy-navigation) monsters route through; navigation and floor reachability count only the frame as solid. Its explicitly assigned `DoorLockComponent` owns the mechanical lock and `SecretDoorComponent` owns concealment; both emit `Changed`, and the door refreshes its offer, marker and indicator from those signals. Passive discovery is a separate `ProximityDiscoveryComponent` wired to an `IProximityDiscoverable` subject (a secret door or a pressure plate): the subject defines eligibility and what discovery means, the component owns cadence, sight and the roll. Shared Resources are definitions; locks, discovery clocks and saved access belong to instances. No autoload or global registry is involved.

Actor-aware offers flow through `InteractiveComponent` and `ActorInteractionComponent`, the single timer and reservation owner, which captures actor context, checks permission, range and line of sight, and supports cancellation. At completion the door validates the selected method and inventory again, consumes one owned item and unlocks or opens once. Notifications observe committed state. Cancellation, wrong or missing resources and invalid reservations spend nothing.

## Keys, picking and marker

Each key variety is one `KeyItem` definition (`scenes/items/keys/`), which is its identity and carries the marker color; the lockpick is a `LockpickItem`. Both stack through the exact-definition inventory policy. Display names or sampled colors never determine matching. Optional keys can be dropped or destroyed like normal items.

A `DoorLockDefinition` assigns `RequiredKey` and an ordered list of `UnlockMethod` strategy Resources. Each method owns its prompt, duration, permission and the item it spends; the first one the actor may use and can afford is offered, so listing the key first gives it priority. The shared `unlock_key.tres` unlocks and opens quickly with the matching key; `unlock_lockpick.tres` takes longer, needs pick capability and spends one pick. A new way to unlock is a new method Resource; an unpickable lock omits the pick method. Closing and reopening an unlocked door costs nothing.

Locked discovered doors show a slowly turning 3D diamond in the required key's color, self-lit (`world_marker.gdshaderinc`) with a thin dark outline, drawn after the fog without a depth test so torches and fog never wash it out. The marker is suppressed behind unexplored fog and before secret discovery, and disappears on unlock; its material and turn speed are authored in `scenes/props/key_marker.tscn`. With a matching key the prompt shows the interact glyph and “Unlock”; with only a pick, “Pick lock”; with neither, “<key name> needed” in red with no glyph. An actionable nearby interaction takes focus over that blocked prompt, and during the interaction only the progress bar shows. Key loot beams use the marker's palette.

## Generation and guaranteed sources

`MapGenerator.Access.cs` owns floor-local access. A floor plan's `FloorAccessProfile` (`FloorProfile.Access`) supplies the lock definitions, a locked-room chance and caps on locked rooms and doors; without one, a floor has no generated locks. The presets live in `scenes/levels/access/floor_access_profiles/` and share the lock definitions in `access/locks/` (gold, silver and arcane crystal keys):

- `rare_lock.tres`, on every plan but the labyrinth, makes a seeded attempt at one locked room, with at most one locked door on the floor.
- `lock_chains.tres`, on the labyrinth, locks up to three rooms and the exit with ordered key chains (`AllowLockingExit`, `AllowLockChains`): each lock's key lies in space reachable before that lock, possibly inside a room an earlier key opens.

Counts are upper bounds; a lock whose room or key cannot fit is omitted. Every generated locked room except a locked exit holds a [reward chest](level-design.md#loot), so a key always pays.

Generated locks only lock doors rooms built: a room qualifies when every one of its doorways holds a door (room types decide that with `DoorChance` and `SecretDoorChance`), and all of them are locked together. Access never adds a door, skips rooms with more entrances than the door budget, and rejects any lock that leaves an unprotected bypass or cuts off another ordinary region. The arrival's room, the gatekeeper's, rooms holding stairs or other progression anchors (the exit, unless the preset allows locking it), must-traverse rooms and rooms that reserve their access are never locked. Explicit locks and secret thresholds also block key-source validation; key spots must pass both the tile-graph check and the floor walkability check with those thresholds blocked.

One guaranteed matching key is reserved per locked door, reachable from the arrival without any generated lock, secret or pick (or, in a chain, after the locks before it), so several same-color doors have enough keys. Keys may lie in rooms or corridors, weighted toward dead ends like loot; later loot respects their spots. Key sources use stable source IDs and the item baseline/delta persistence, so spending keys never regenerates their pickups on revisit. Lockpicks never spawn as floor supplies: only the Rogue starts with them, and the [Quartermaster](shops.md) sells them. Required large-encounter space is reserved before optional locks, and teleport destinations exclude locked and undiscovered regions so the player is never stranded.

A `RoomType` gives each doorway a `SecretDoorChance` and a detection profile (`SecretDetection`). A successful roll creates a door whose disguise is an instance-owned, wall-sized GridMap from the room's wall library on the same 4-unit span; it moves and rotates with the door, and discovery removes the wall and exposes the closed door. The doorway stays the room's ordinary connector for corridor routing and can be discovered from either side, in any rotation. Secrecy rolls on the room's own secret-door layout stream, and each door's discovery on a separate stream keyed by the room and doorway. The entrance and exit room types have no secret doors, and the boss and labyrinth plans and the gauntlet approach forbid them. The treasury has one door, secret most of the time; storerooms and the Mines' ore chambers have one door, secret half the time; the Cathedral's monk cells and side chapels are sometimes secret; other room types have none. A room reached only through secret doors holds a [reward chest](level-design.md#loot); floor reachability counts secret doors as open, so loot may lie inside, but monsters and fountains never do. Required progression may lead through a secret door, since the player can find it eventually.

## Discovery and visibility

Live discovery briefly glows yellow while the original floor or wall mesh crossfades into the plate or the closed door. The shared, Inspector-authored effect fades private copies of the surface materials (on the Mobile renderer too), restores the original assignments, then removes its temporary geometry; no ring, highlight or text remains, and restoring a saved discovery never replays it. A floor's collider stays intact through a plate reveal, and a secret door stays physically closed through its reveal.

A concealed opening rolls every interval of active gameplay while the player is within its profile's radius at a compatible height, from either side; a ray must reach the actual concealed wall, allowing its own thickness while rejecting intervening geometry. The chance rises with the profile's attribute (Intelligence by default) up to a cap. The clock runs only while the subject is findable, pauses with the game, and slow frames produce no catch-up bursts.

Concealed pressure plates (spike traps and plate traps) use the same policy through `trap_detection.tres`, with twice the secret-door radius for earlier warning, and only on explored tiles. Finding a plate reveals it without triggering or disarming it, and stepping on an unfound plate reveals it as it fires. Found plate IDs persist through regeneration and save/load; cooldowns stay transient.

Discovery permanently reveals a closed door, without opening or unlocking it or revealing the destination. No interaction prompt, key marker, door x-ray, navigation route or exploration sight crosses an undiscovered opening. Loot and x-rays share `ExplorationVisibility`: undiscovered pickups hide their whole subtree (model, glow, sparkles, light, labels and x-ray helpers), stop particle emission and block collection. Nameplates draw over the fog caps, so they check discovery themselves and stay hidden until their own tile is explored. Magnet pickup cannot pull items through walls. Revealed closed doors keep blocking traversal; opening lifts the navigation gate and allows ordinary sight.

`SecretDoorComponent.Reveal()` is idempotent and callable by a future mechanism; removing a door's `ProximityDiscovery` node leaves a button-only secret. Hidden-button content and mechanism authority to unlock or open are not implemented.

## Interior cell gates

`InteriorDoorComponent` is an optional capability wired on `Door`. It owns a stable room-local ID, the gate's opening width and the furniture clearance for its hinge sweep and approaches. The normal door interaction, cancellation, animation, collision and navigation gate handle each gate. The reusable barred gate uses the dungeon kit's iron door mesh; each room instance owns its sized collider.

The prison room type's `CellsStep` emits shared barred dividers, fronts, corners and junctions and one gate per cell along one or two opposing long walls. A row starts flush in a room corner when it fits, keeps its far end free for the entrance and keeps each gate's swing clear of furniture. Cells are unlocked and never secret, and every usable floor area connects with the gates open. Bars do not seal exploration into cells.

Interior gates get identities scoped by placed room and local cell ID. They are not exterior connectors and never take generated locks. Their door baselines and open-state deltas survive regeneration and save/load. Internal mechanical locks and opaque partitions remain future work.

## Persistence and randomness

Access placement and discovery use independently derived streams, isolated from layout, encounters and source loot. Stable ordering and identities reproduce the untouched access configuration for the same build, content and seed. Runtime discovery draws are not replayed after reload; actual discoveries are saved.

Save data stores discovered, locked and open deviations by stable connection identity, and discovered plate IDs, apart from the explored samples. Revisiting an explored branch does not open a door the player closed. Restore validates identities and state combinations first, applies access and item differences before gameplay, and reveals only saved exploration. No half-completed picking resumes. Saves carry `SaveGame.CurrentVersion`; an earlier version needs a new run, without migration.

## Authoring and verification

Assign a `FloorAccessProfile` to a floor plan's `Access` to enable generated locks. Assign `SecretDoorChance` and `SecretDetection` on a room type for concealed entrances. For an authored lock, assign a definition to the door's prewired `DoorLock` child. Unassigned optional capabilities stay absent; regeneration applies Resource changes. Never mutate shared definitions during play.

`DoorAccessTest` and the floor sweeps cover consumption, wrong keys, key preference, interruption, prompts, deterministic generation, reachable key quantities, chains, accessible sides and saved access. The [world-effect harness](../scenes/debug/README.md#world-effects) previews the plate and secret reveals without hunting for them on a floor. Probability, counts, detection radius, reward density and monster pressure still need playtesting.
