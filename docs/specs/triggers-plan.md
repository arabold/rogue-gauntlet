# Triggers: plates, switches and linked mechanisms

Status: slice 1 (the trigger core, pressure plates and the spike trap rebuilt on them) landed; the rest is proposed. Adds one general trigger model: **sources** that are on or off, **links** that carry a source's changes to **receivers** in one of three modes, and **conditions** that combine sources into puzzles. The first use is a triggered trap: a hidden pressure plate that makes a mechanism on the wall across from it shoot a fast dart or fireball. Switches that open a door elsewhere on the map, switch puzzles, plates weighed down by dropped items, and later a quest system build on the same model.

Current systems this builds on:
- The spike trap ([`FloorTrap`](../../scenes/props/FloorTrap.cs), discovery in [dungeon exploration](../dungeon-exploration-design.md)).
- Wall spouts and their shots ([trap hazards](trap-hazards-plan.md), [combat](../combat-system.md#trap-hazards)).
- Doors ([door access](../door-access.md)) and timed interactions ([actor actions](../actor-actions-and-interactions.md)).
- Loot and trap placement ([level design](../level-design.md#loot)) and saved floor deltas ([randomness and seeds](../randomness-and-seeds.md)).

## Design

### Goals

- **Read the floor, or pay.** A trap plate is hidden like the spike trap and found the same way; once found, the hero walks around it. Its mechanism is in plain sight on the wall, so an attentive player suspects the plate first. The mechanism fires almost at once (0.1 s) and fast (darts 30 m/s, fireballs 18 m/s) over a few tiles, so a careless step is a hit, and a dodge on the click passes.
- **Switches and puzzles.** A switch is a visible lever the hero pulls between two positions. It may open a gate across the map, and several switches or plates can have to be set a certain way together. Plates also take dropped items as weight, so an item left on a plate can hold a gate open.
- **One model.** Plates, switches and puzzle conditions are sources; launchers, spikes and doors are receivers. Neither side knows the other's type, and a quest system later listens to the same changes.

### Sources

Every source is on or off and reports each change, with the actor that caused it.

| Source | On while | Notes |
| --- | --- | --- |
| **Pressure plate** | anything it accepts stands or lies on it | Accepts the hero, and optionally monsters and dropped items (`PressedBy`). Concealed plates are found like the spike trap; visible plates are puzzle pieces. |
| **Switch** | it is in position B | A lever the hero pulls with a short timed interaction (the door's), from A to B and back. Its authored start position is part of the floor. |
| **Condition** | every source it watches is in its required state | A puzzle: the sources it needs on, and those it needs off. A condition is itself a source, so conditions nest. |

### Modes

A link carries its source's changes to its receivers in one of three modes. These are the user-facing trigger mechanisms:

| Mode | Receivers get | Example |
| --- | --- | --- |
| **Pulse** (one-time) | one pulse each time the source turns on; turning off does nothing | Stepping on a plate fires one fireball; stepping on again fires another. |
| **Hold** (on-off) | on when the source turns on, off when it turns off | A gate open while a plate holds weight; a switch's A/B position opens and closes a door. |
| **Latch** (permanent) | on the first time the source turns on, then never off | A solved puzzle opens the vault for good; a plate starts a flame jet that never stops. |

### Receivers

Every receiver answers a pulse (a one-time effect) and being switched on and off (a lasting effect):

| Receiver | Pulse | On | Off |
| --- | --- | --- | --- |
| **Wall launcher** | fires one shot | fires every `Interval` | stops firing |
| **Spikes** | spring and retract | stay up, hurting whoever steps in | retract |
| **Door** (slice 5) | toggles open or shut | opens | shuts |
| **Condition** | — (it watches sources directly) | — | — |

The spike trap becomes a concealed plate with a Pulse link to spikes on the same tile, so the trap behaves as today, and all plates share one class for discovery, saving and floor alignment.

### Rules

- **Pressing a plate.** The plate is on while at least one accepted occupant is on it. A concealed plate is revealed when it turns on (without the discovery pulse).
- **Monsters.** Trap plates are pressed by the hero only (see Open decisions). Puzzle plates choose their own `PressedBy`.
- **Launcher shots** belong to the trap faction like spout shots: monsters ignore them, but a monster's body still stops them. A pulse during the launcher's delay does nothing more.
- **Effects on load.** Restoring a saved floor sets every Hold and Latch receiver to its state without effects: no shot fires and doors snap, never swing.
- **Placement of trap pairs.**
  - Some of a floor's trap slots become plate and launcher pairs where a lane fits:
    - an open tile within `MaxLane` (8 m) of a wall that takes wall decor;
    - the lane from the wall to the plate clear of walls and blocking props;
    - the launcher at least `MinLane` (3 m) away, so the shot is not point-blank.
  - The rest stay spike plates. Pairs are rolled from the trap stream, as traps are now.
  - Switches, conditions and door links are authored in rooms or room groups, not scattered.

## Code shape

### Trigger core (new)

```csharp
/// <summary>
/// How an output carries its source's changes: a Strategy, one shared stateless `.tres` per mode in
/// `scenes/props/triggers/modes/` (pulse, hold, latch); the output keeps whatever a mode remembers.
/// </summary>
[Tool, GlobalClass] public abstract partial class TriggerMode : Resource
{
	public abstract void Carry(TriggerOutputComponent output, bool on, in TriggerActivation activation);
}
// PulseTriggerMode: on → output.PulseTargets. HoldTriggerMode: output.SetTargetsActive(on).
// LatchTriggerMode: first on → output.TryLatch() → output.SetTargetsActive(true).

/// <summary>Who or what changed a source: the source and the actor that pressed, pulled or left it. Restored is set
/// when a saved state is replayed on load, so receivers apply it without effects.</summary>
public readonly record struct TriggerActivation(Node Source, Node3D Activator, bool Restored = false);

/// <summary>A receiver: a one-time effect on a pulse, a lasting one while on.</summary>
public interface ITriggerable
{
	void Pulse(in TriggerActivation activation);
	void SetActive(bool active, in TriggerActivation activation);
}

/// <summary>
/// A source's output: its on/off state, the link to its receivers and the mode it carries changes in. Sources report
/// their state with <see cref="Report"/>; conditions watch other sources' outputs through <see cref="Switched"/>. A
/// target that is not an <see cref="ITriggerable"/> is a configuration warning, never a crash. A latched output
/// stays on and is saved.
/// </summary>
[Tool, GlobalClass] public partial class TriggerOutputComponent : Node
{
	[Export] public TriggerMode Mode { get; set; }   // pulse.tres, hold.tres or latch.tres
	[Export] public Node[] Targets { get; set; } = [];
	public bool IsOn { get; }
	public bool IsLatched { get; }
	[Signal] public delegate void SwitchedEventHandler(bool on, Node3D activator);
	public void Report(bool on, Node3D activator);
	public void PulseTargets(in TriggerActivation activation);              // used by modes
	public void SetTargetsActive(bool active, in TriggerActivation activation);
	public bool TryLatch();
	// Slice 4: PersistentId and RestoreLatched() replay a saved latch to receivers, Restored.
}
```

### Sources

```csharp
[Flags] public enum PlatePressers { Hero = 1, Monsters = 2, Items = 4 }

/// <summary>A floor plate, on while something it accepts stands or lies on it; concealed plates are found like secret doors.</summary>
[GlobalClass] public partial class PressurePlate : Node3D, IProximityDiscoverable
{
	[Export] public TriggerComponent Trigger { get; set; }          // existing Area3D: bodies, and items' areas
	[Export] public TriggerOutputComponent Output { get; set; }
	[Export] public PlatePressers PressedBy { get; set; } = PlatePressers.Hero;
	[Export] public bool Concealed { get; set; } = true;
	[Export] public MeshInstance3D FloorSurface { get; set; }
	[Export] public DiscoveryPulse DiscoveryEffect { get; set; }
	[Export] public ProximityDiscoveryComponent Discovery { get; set; }   // optional: found only by stepping on it
	public string PersistentId { get; }
	public bool IsDiscovered { get; }
	public void Initialize(string persistentId, ulong levelSeed, GridMap surfaceGrid, Vector3I surfaceCell);
	public void RestoreDiscovered(bool discovered);
}

/// <summary>A lever the hero pulls between positions A and B; on in B.</summary>
[GlobalClass] public partial class Switch : Node3D
{
	[Export] public InteractiveComponent Interactive { get; set; }
	[Export] public TriggerOutputComponent Output { get; set; }
	[Export] public Node3D Lever { get; set; }
	[Export] public bool StartsInB { get; set; }
	public bool IsInB { get; }
	public void RestorePosition(bool inB);       // no swing, Restored
}

/// <summary>A puzzle: on while every watched output is in its required state; itself a source.</summary>
[Tool, GlobalClass] public partial class TriggerCondition : Node
{
	[Export] public TriggerOutputComponent[] RequiredOn { get; set; } = [];
	[Export] public TriggerOutputComponent[] RequiredOff { get; set; } = [];
	[Export] public TriggerOutputComponent Output { get; set; }
}
```

### Receivers

```csharp
/// <summary>Anything that hurts as a trap: depth-scaled damage, the trap faction's group, seeded rolls.</summary>
[Tool, GlobalClass] public abstract partial class Trap : Node3D
{
	[Export] public int Damage { get; set; }
	[Export] public float DamagePerDepth { get; set; }
}

/// <summary>Spikes that spring from their plate and hurt whoever stands on it (today's spike trap).</summary>
[Tool, GlobalClass] public partial class SpikeBurst : Trap, ITriggerable
{
	[Export] public MeshInstance3D Spikes { get; set; }
	[Export] public TriggerComponent Contact { get; set; }
}

/// <summary>A wall-mounted mechanism: one fast shot along its front per pulse, a shot every Interval while active.</summary>
[Tool, GlobalClass] public partial class WallLauncher : Trap, ITriggerable
{
	[Export] public TrapShotComponent Shot { get; set; }
	[Export] public Node3D ClickEffect { get; set; }
	[Export(PropertyHint.Range, "0,0.5,0.01,suffix:s")] public float Delay { get; set; } = .1f;
	[Export(PropertyHint.Range, "0.2,5,0.05,suffix:s")] public float Interval { get; set; } = 1f;
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
	public float MeasureLane();
}

/// <summary>Makes its door a receiver: a pulse toggles it, on opens it, off shuts it.</summary>
[GlobalClass] public partial class DoorTriggerComponent : Node, ITriggerable { [Export] public Door Door { get; set; } }
```

Related changes:
- `WallSpout` keeps its beat and warning and fires through its `TrapShotComponent`; its projectile exports move there.
- `FloorTrap` is removed (landed):
  - `scenes/props/traps/pressure_plate_spikes.tscn` is a concealed, hero-pressed `PressurePlate` whose Pulse output springs the `SpikeBurst` on its tile. It replaces `floor_trap.tscn` everywhere: the level's `TrapScene`, tests, and the balance model's `TRAP_SCENE`, whose damage moved to the `SpikeBurst` node.
  - A bare `pressure_plate.tscn`, for launchers, comes with slice 2 and its own plate model.
- New scenes:
  - `wall_launcher_dart.tscn` and `wall_launcher_gargoyle.tscn` reuse the spout models.
  - `switch_lever.tscn` is a KayKit-style lever.

### Placement

```csharp
/// <summary>One kind of triggered trap a floor may place in its trap slots: the plate, the mechanism, the lane it needs.</summary>
[Tool, GlobalClass] public partial class TriggeredTrapProfile : Resource
{
	[Export] public PackedScene PlateScene { get; set; }
	[Export] public PackedScene LauncherScene { get; set; }
	[Export] public TriggerMode Mode { get; set; }   // pulse by default
	[Export] public float Weight { get; set; } = 1;
	[Export] public uint FirstDepth { get; set; } = 1;
	[Export] public float MinLane { get; set; } = 3;
	[Export] public float MaxLane { get; set; } = 8;
}
```

`MapGenerator.TriggeredTraps` (array) is assigned in `level.tscn`.

### Saving

- `GeneratedFloor.Traps` becomes `Plates`, and the save's `DiscoveredTrapIds` becomes `DiscoveredPlateIds`.
- Each floor also saves its switches that are out of their start position (`FlippedSwitchIds`) and its latched outputs (`LatchedOutputIds`).
- Plate occupancy is not saved: items left on a plate are item deltas already, and the plate re-reads them on load.
- Bump `SaveGame.CurrentVersion` with the first slice that changes the save.

### Call order

```
Hero steps on a plate (TriggerComponent body/area entered; PressedBy filter)
└─ PressurePlate: occupants 0 → 1 → reveal (no pulse) → Output.Report(true, hero)
    └─ TriggerOutputComponent (state changed)
        ├─ Pulse: on → each target.Pulse(activation)
        ├─ Hold:  on/off → each target.SetActive(on, activation)
        ├─ Latch: first on → each target.SetActive(true, activation); latched for good, saved
        └─ emit Switched(on, activator) → watching TriggerConditions re-evaluate → their own Output.Report
Hero leaves (occupants → 0): Output.Report(false, hero) → Hold receivers off; Pulse and Latch ignore it

Floor load (after generation, before handoff)
├─ restore plate discovery, switch positions (RestorePosition) and latched outputs (RestoreLatched)
└─ outputs replay Hold/Latch state to receivers with Restored = true (no shots, doors snap)
```

## Slices

1. **Trigger core and plates (landed).**
   - Adds `TriggerMode`, `TriggerActivation`, `ITriggerable`, `TriggerOutputComponent`, `PressurePlate` (hero only) and `SpikeBurst`, and rebuilds the spike trap on them with no behavior change.
   - Renames plates in the floor and the save, bumps the save version and points the balance model at the new scene.
   - Verification:
     - The existing trap, discovery and save tests pass.
     - New tests: Pulse pulses once per press; Hold follows the plate on and off; Latch stays on after the hero leaves.
2. **Wall launcher (landed).**
   - Adds `Trap` (the base of every trap: depth damage, the trap group, seeded rolls), `TrapShotComponent` (shared with `WallSpout`), `WallLauncher` (pulse and interval fire), the dart and gargoyle launcher scenes and the bare `pressure_plate.tscn`.
   - **Traps → Triggered** in the debug menu stages a plate a few metres from a launcher in the **Plate mode** chosen.
   - Verification (`PlateTrapTest`): an unaware hero who steps on the plate is hit once, and walking past it fires nothing; a dodge out of the lane on the click escapes; a Hold launcher stops when the hero steps off; a monster on the plate sets nothing off.
3. **Trap placement.**
   - Adds `TriggeredTrapProfile` and lane search in `PlaceTraps`, and the profiles in `level.tscn`.
   - The balance model gets a share for launcher damage, and the sweep test checks that every launcher faces its plate over a clear lane.
   - Visual check on a generated floor.
4. **Switches, conditions and items.**
   - Adds `Switch`, `TriggerCondition`, item weight on plates (`PlatePressers.Items`), saved switch positions and latches, and restore without effects.
   - Verification: a switch pull toggles its output, and survives save and load; a condition turns on only when every requirement holds; an item dropped on a plate holds it on.
5. **Doors as receivers, and authored puzzles.**
   - Adds `DoorTriggerComponent` and one or two authored uses: a switch that opens a gate elsewhere on the floor (placed with its gate by a room group), and a switch-combination vault.
   - Door seals and access stay consistent with triggered doors: a gate held by a trigger is never also a lock.
6. **Quest seam.** `TriggerOutputComponent` also broadcasts `SignalBus.TriggerSwitched(persistentId, on)` for a quest system to listen to. Deferred until quests are planned.

## Open decisions

- **Do monsters press trap plates?** Proposed no: trap plates are for the hero, and monsters walking into their own traps would need trap-aware navigation. Yes would let the hero lure monsters onto plates. Puzzle plates choose for themselves.
- **Is a trap's mechanism visible before its plate is found?** Proposed yes, as an attentive player's hint.
- **Where and how often?** Proposed: darts from depth 2 and fireballs from the Prison, sharing a floor's existing trap count rather than adding traps. Hold-mode launchers (a stream of shots while the hero stays on the plate) and Latch-mode ones (a jet that never stops) are rarer variants.
- **Damage:** proposed to match the wall spouts (dart 3, fireball 2 plus depth scaling) until the balance model gives launchers their own share.
- **Switch-to-gate placement:** which room groups pair a switch with a gate elsewhere, and how far apart, is a level-design choice for slice 5. Generated access (locks and keys) must still solve.
