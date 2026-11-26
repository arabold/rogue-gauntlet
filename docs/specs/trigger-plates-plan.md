# Trigger plates and linked mechanisms

Status: proposed. Adds a general trigger link, from a source that is activated to the receivers that respond to it, and builds the first triggered trap on it: a hidden pressure plate that makes a mechanism on the wall across from it shoot a fast dart or fireball. The same link later opens and shuts doors, and becomes the event a quest system listens to.

Current systems this builds on:
- The spike trap ([`FloorTrap`](../../scenes/props/FloorTrap.cs), discovery in [dungeon exploration](../dungeon-exploration-design.md)).
- Wall spouts and their shots ([trap hazards](trap-hazards-plan.md), [combat](../combat-system.md#trap-hazards)).
- Doors ([door access](../door-access.md)) and loot and trap placement ([level design](../level-design.md#loot)).

## Design

### Goals

- **Read the floor, or pay.** A plate is hidden like the spike trap and found the same way, by proximity rolls from explored space. Once found, the hero walks around it. The mechanism it fires is in plain sight on the wall: a slit or a gargoyle facing back across the floor, so an attentive player suspects the plate before finding it.
- **Fast enough to hit.** The mechanism clicks and fires almost at once (0.1 s), and its shot flies fast (darts 30 m/s, fireballs 18 m/s), over at most a few tiles. A hero who steps on the plate without dodging is hit. A dodge timed on the click still passes through, as with every trap shot.
- **One trigger model for everything.** A plate, and later a lever, a room's first entry or a slain guardian, activates its linked receivers through one contract. Wall launchers, doors and spikes are receivers. Neither side knows the other's type.

### Concepts

- **Trigger source:** something that is activated, by an activator (usually the hero). The first source is the `PressurePlate`, pressed when the hero steps on it.
- **Receiver:** something that responds to an activation (`ITriggerable`). The first receivers are the spikes of today's spike trap and the wall launcher.
- **Link:** a source's list of receivers (`TriggerLinkComponent.Targets`), authored as node paths in a scene or wired by generation.

The spike trap becomes a plate with spikes as its receiver, so plates share one class for discovery, saving and surface alignment.

### Behavior

- **Plates:**
  - Only the hero presses a plate; monsters know their dungeon and cross plates unharmed (see Open decisions).
  - A plate re-arms once the hero steps off, so every crossing fires, as the spike trap does now.
  - Pressing a plate reveals it, and saves its discovery like today's traps.
- **Wall launcher:**
  - On activation it plays a short click (a glint, dust from the slit) and fires one shot after `Delay` (0.1 s) along its front, through the plate's centre at chest height.
  - Shots belong to the trap faction like spout shots: monsters ignore them, but a monster's body still stops them.
  - It fires once per activation, and an activation during its delay does nothing more.
- **Placement:**
  - Some of a floor's trap slots become plate and launcher pairs where a lane fits:
    - an open tile within `MaxLane` (8 m) of a wall that takes wall decor;
    - the lane from the wall to the plate clear of walls and blocking props;
    - the launcher at least `MinLane` (3 m) away, so the shot is not point-blank.
  - The rest stay spike plates. Pairs are rolled from the trap stream, as traps are now.

## Code shape

### Trigger contract (new)

```csharp
/// <summary>Who or what set a trigger off: the source that fired and the actor that pressed, pulled or entered it.</summary>
public readonly record struct TriggerActivation(Node Source, Node3D Activator);

/// <summary>A receiver a trigger activates: a launcher fires, spikes spring, a door opens.</summary>
public interface ITriggerable { void Activate(in TriggerActivation activation); }

/// <summary>
/// A trigger source's receivers, as node paths authored in its scene or set by generation. Activating it activates
/// every target in order; a target that is not an <see cref="ITriggerable"/> is a configuration warning, never a crash.
/// </summary>
[Tool, GlobalClass] public partial class TriggerLinkComponent : Node
{
	[Export] public Node[] Targets { get; set; } = [];
	[Signal] public delegate void ActivatedEventHandler(Node3D activator);
	public void Activate(Node3D activator);
}
```

### Plate and receivers

```csharp
/// <summary>A floor plate the hero presses by stepping on it; concealed plates are found like secret doors.</summary>
[GlobalClass] public partial class PressurePlate : Node3D, IProximityDiscoverable
{
	[Export] public TriggerComponent Trigger { get; set; }      // existing Area3D, body entered
	[Export] public TriggerLinkComponent Link { get; set; }
	[Export] public MeshInstance3D FloorSurface { get; set; }
	[Export] public DiscoveryPulse DiscoveryEffect { get; set; }
	[Export] public ProximityDiscoveryComponent Discovery { get; set; }   // optional: found only by stepping on it
	[Export] public bool Concealed { get; set; } = true;          // false: a visible plate (levers of the floor)
	public string PersistentId { get; }
	public bool IsDiscovered { get; }
	public void Initialize(string persistentId, ulong levelSeed, GridMap surfaceGrid, Vector3I surfaceCell);
	public void RestoreDiscovered(bool discovered);
}

/// <summary>Spikes that spring from their plate and hurt whoever pressed it (today's spike trap).</summary>
[GlobalClass] public partial class SpikeBurst : Node3D, ITriggerable { [Export] int Damage; [Export] float DamagePerDepth; [Export] MeshInstance3D Spikes; }

/// <summary>A wall-mounted mechanism that fires one fast shot along its front when activated.</summary>
[Tool, GlobalClass] public partial class WallLauncher : Node3D, ITriggerable
{
	[Export] public TrapShotComponent Shot { get; set; }
	[Export] public Node3D ClickEffect { get; set; }
	[Export(PropertyHint.Range, "0,0.5,0.01,suffix:s")] public float Delay { get; set; } = .1f;
	[Export] public int Damage { get; set; }
	[Export] public float DamagePerDepth { get; set; }
	[Signal] public delegate void FiredEventHandler(Node3D shot);
}

/// <summary>A trap's projectile and how it leaves the wall; shared by wall spouts and wall launchers.</summary>
[Tool, GlobalClass] public partial class TrapShotComponent : Node
{
	[Export] public PackedScene ProjectileScene { get; set; }
	[Export] public Marker3D Muzzle { get; set; }
	[Export] public float Speed { get; set; }
	[Export] public float Range { get; set; }
	[Export] public DamageElement Element { get; set; }
	[Export] public AttackDefenseTraits DefenseTraits { get; set; }
	public Vector3 FireDirection { get; }
	public Projectile Fire(Node3D attacker, int damage, ulong seed, AttackIdentity identity);
}
```

- `WallSpout` keeps its beat and warning and fires through its `TrapShotComponent`; its projectile exports move there.
- `FloorTrap` is removed. `pressure_plate.tscn` is a concealed plate with no receivers. `pressure_plate_spikes.tscn` inherits it and adds a `SpikeBurst` linked to it, replacing `floor_trap.tscn` everywhere it is used: the level's `TrapScene`, tests and the balance model's `TRAP_SCENE`, whose damage moves to the `SpikeBurst` node.
- New scenes `wall_launcher_dart.tscn` and `wall_launcher_gargoyle.tscn` reuse the spout models.

### Placement

```csharp
/// <summary>One kind of triggered trap a floor may place in its trap slots: the plate, the mechanism, the lane it needs.</summary>
[Tool, GlobalClass] public partial class TriggeredTrapProfile : Resource
{
	[Export] public PackedScene PlateScene { get; set; }
	[Export] public PackedScene LauncherScene { get; set; }
	[Export] public float Weight { get; set; } = 1;
	[Export] public uint FirstDepth { get; set; } = 1;
	[Export] public float MinLane { get; set; } = 3;
	[Export] public float MaxLane { get; set; } = 8;
}
```

`MapGenerator.TriggeredTraps` (array) is assigned in `level.tscn`.

### Saving

- `GeneratedFloor.Traps` becomes `Plates`, and the save's `DiscoveredTrapIds` becomes `DiscoveredPlateIds`. Bump `SaveGame.CurrentVersion`.
- Launchers keep no state.
- One-shot triggers, needed for doors, are later work: they would record themselves among the run's cleared entities.

### Call order

```
MapGenerator.PlaceTraps (trap stream)
├─ per slot: roll a TriggeredTrapProfile by weight (FirstDepth) or the spike plate
│   └─ triggered: find a lane from the tile to a decor-taking wall within MinLane..MaxLane, clear of solids
│       ├─ none fits → the slot places a spike plate instead
│       └─ stand the plate, hang the launcher on the wall facing the plate, plate.Link.Targets = [launcher]
└─ plates Initialize(id, seed, floor grid, cell); GeneratedFloor.Freeze collects plates

Hero steps on a plate (TriggerComponent.BodyEntered, hero only, armed)
├─ PressurePlate: reveal (no pulse), disarm until the hero steps off
└─ TriggerLinkComponent.Activate(hero) → each ITriggerable.Activate
    ├─ SpikeBurst: spring, damage the activator
    └─ WallLauncher: click → after Delay → TrapShotComponent.Fire → Projectile (trap faction, Dodgeable | Guardable)
```

## Slices

1. **Trigger contract and plates.**
   - Adds `ITriggerable`, `TriggerActivation`, `TriggerLinkComponent`, `PressurePlate` and `SpikeBurst`, and rebuilds the spike trap on them, with no behavior change.
   - Renames plates in the floor and the save, bumps the save version and points the balance model at the new scene.
   - Verification: the existing trap, discovery and save tests pass, plus a test that a plate activates every linked receiver and re-arms only after the hero steps off.
2. **Wall launcher.**
   - Adds `TrapShotComponent` (shared with `WallSpout`), `WallLauncher` and the dart and gargoyle launcher scenes.
   - **Traps → Triggered** in the debug menu stages a plate a few metres from a launcher on a real or stand-in wall.
   - Verification: an unaware hero who steps on the plate is hit; a dodge on the click passes; a discovered plate walked around fires nothing; monsters never press it.
3. **Placement.**
   - Adds `TriggeredTrapProfile` and lane search in `PlaceTraps`, and the profiles in `level.tscn`.
   - The balance model gets a share for launcher damage, and the sweep test checks that every launcher faces its plate over a clear lane.
   - Visual check on a generated floor.
4. **Doors as receivers.** A `DoorTriggerComponent` (Open, Close, Toggle) on a door makes it a receiver, together with authored uses such as a gate that slams shut behind the hero, or a vault opened by a plate. One-shot triggers are recorded as cleared entities.
5. **Quest seam.** `TriggerLinkComponent` also broadcasts `SignalBus.TriggerActivated(sourceId, activator)` with stable ids, for a quest system to listen to. Deferred until quests are planned.

## Open decisions

- **Do monsters press plates?** Proposed no: plates are for the hero, and monsters walking into their own traps would need trap-aware navigation. Yes would let the hero lure monsters onto plates.
- **Is the mechanism visible before its plate is found?** Proposed yes, as an attentive player's hint. Hiding it too makes the first hit pure luck.
- **Where and how often?** Proposed: darts from depth 2 and fireballs from the Prison, sharing a floor's existing trap count rather than adding traps.
- **Damage:** proposed to match the wall spouts (dart 3, fireball 2 plus depth scaling) until the balance model gives launchers their own share.
