# Trap hazards and gauntlets

Status: slices 1 to 5 (wall spouts, pendulum blades, gauntlets, the gauntlet rooms and their placement before a few descents per run, their Asset Studio art and their balance) landed, described in [combat](../combat-system.md#trap-hazards), [level design](../level-design.md#trap-gauntlets) and the [balance model](balance-model.md); the rest is proposed. Adds visible, rhythmic hazards (wall spouts, pendulum blades, optionally timed spike strips) and arranges them into gauntlet rooms and corridor stretches the hero crosses with timing and dodges. The hidden spike plate ([spike plate](../../scenes/props/traps/pressure_plate_spikes.tscn), discovery in [dungeon exploration](../dungeon-exploration-design.md)) stays as it is.

Current systems this builds on: [combat defense](../combat-system.md) (dodge immunity, guard, poise, threat tiers, attack indicators), [level design](../level-design.md) (room assembly, corridor styling, reachability, fog), [realm content](../realm-content.md) (room windows, headlines, motifs), [balance model](balance-model.md) (trap attrition), [room assembly authoring](../../scenes/levels/generators/assembly/README.md).

## Design

### Goals

- **A puzzle you read, then execute.** Hazards are visible from the moment their tiles are explored and move on a fixed beat. The challenge is timing, not discovery; the spike plate remains the hidden threat.
- **Patience crosses; a dodge buys back a mistake.** Every row has a safe window and the hero may stop between rows, so crossing never depends on walking speed, which bonuses and armor change. Dodging (6 m in 0.15 s, two charges, 1.2 s recharge each) passes through blades and projectiles, so a misjudged entry can be rescued, but a gauntlet never demands more than two dodges inside one recharge.
- **Introduce, then combine.** Each realm teaches one hazard alone before mixing it with others or with monsters.
- **Landmarks.** A pendulum hall or a row of gargoyle heads reads as a place, not as noise; hazards are rare set pieces, not floor clutter.

### The hazard kit

| Hazard | Look | Beat | Telegraph | Contact | Defense |
| --- | --- | --- | --- | --- | --- |
| **Wall spout, slit** | A carved stone plate with a dark recess (a real cut, not an overlay) flush on the wall face | Fires one dart every period | 0.6 s: dust puffs from the slit, the attack indicator draws the lane | Physical projectile, armor applies | Dodge-through, guard blocks, or step out of the lane |
| **Wall spout, gargoyle** | Chunky KayKit-style gargoyle head with an open mouth | Fires one fireball every period | 0.6 s: the mouth glows, embers drip, lane indicator | Fire projectile: ignores armor (burning later, see Open decisions) | As above; fire wards absorb it |
| **Pendulum blade** | A crescent scythe blade on a T collar at the end of a long shaft; the pivot hangs 11 m up and the shaft fades out from 5 m, so no ceiling is needed | Swings across the path; period 1.6–2.6 s | The motion itself, plus its shadow under the hero's light | Hazard contact on each pass, pushed out of the blade's path | Dodge-through only (unguardable) |
| **Spike strip** (optional) | A row of the existing spike plates, visible | Spikes rise, hold, retract | 0.4 s rattle: tips poke through first | Hazard contact while up, once per rise | None: only timing avoids it |

Damage scales with depth like the floor trap (`Damage` + `DamagePerDepth`, written by the balance model's `derive` from `params.HAZARDS`). Shares of the reference hero's health per hit: dart 5%, fireball three quarters of a dart (armor never absorbs it), blade 8%; spikes would be 6% (the floor trap is 9% but hidden and rarer).

Monsters stay immune, as they are to floor traps today (their hurtbox filter excludes the `Trap` faction). Spout projectiles still stop on a monster's body, so monsters shield the hero from darts. Gauntlet tiles get no monster spawns, loot, floor traps or teleport landings.

### Rhythm and patterns

Every generated hazard belongs to a **gauntlet**, one arrangement with a shared clock. The clock runs from floor load (it pauses with the game), so every load replays the same beat. Each hazard has a `Period` and a `Phase` (0–1). Patterns set the phases across a gauntlet's rows, in the order the path meets them:

- **Unison:** all at once. The hero waits for one gap and crosses everything in it (introductory).
- **Alternate:** odd and even rows half a period apart. The hero stops between rows.
- **Wave:** each row a quarter beat after the one before: a sweep rolls along the gauntlet, and the hero follows behind it row by row.

Fairness rules the generator enforces, not just the author:

- Rows along a path stand at least one tile (4 m) apart, so the hero can still stop between rows; playtests liked one tile best.
- Every lane is safe for at least 0.5 s per period at the point the hero crosses it.
- Darts fly at 14 m/s, fast enough to read as arrows (crossbow bolts fly at 24–27 m/s), so the hero answers the 0.6 s lane warning rather than the dart itself; fireballs stay slower at 7 m/s. Wind-up is never shorter than 0.6 s (the Unblockable minimum), whatever the hazard's own tier.
- One gauntlet uses at most two hazard kinds.

### Wakefulness

A gauntlet wakes when the hero stands within `ActivationRadius` (16 m) and any of its tiles is explored; hazard visuals stay hidden behind the fog like torches (`ExplorationGateComponent`). Asleep, spouts never fire and pendulums never cut, though they keep swinging from the clock, so a blade never jumps on screen. The clock always runs, so hazards stay in step.

### Arrangements

Gauntlets are never secret: they stand on the route the hero must take, so crossing them is the challenge. A run meets two or three gauntlet rooms, never the same one twice, each guarding a floor's descent ([level design](../level-design.md#trap-gauntlets)).

**Gauntlet rooms (landed).** Room types in `scenes/levels/rooms/room_types/gauntlets/` whose first step, a hazard row step (`CrossFireStep` or `PendulumRowsStep`), lays rows across the door-to-door spine: one row per tile between the doorway tiles at both ends, each spanning the room's full width so it cannot be walked around, and each row's band reserved so furniture never blocks a lane or a swing. All of a room's hazards share one gauntlet. Rows stop at the first that cannot be laid (a wall niche on both sides), so a gauntlet never has a gap twice its spacing; too few rows retry the room. Each room suits the garrison, sewer and jungle themes with that theme's walls and floors. The pool keeps them in their own `Gauntlets` slot, which only the approach group draws, so no layout picks one as an ordinary room; the room harness previews them and **Traps → Rooms** stages one in the combat arena to play.

| Room | Shape | Rows | Beat | Look |
| --- | --- | --- | --- | --- |
| *Arrow gallery* | 3 × 5–7 tiles | 3–5 rows of dart slits, alternating side walls | Wave from the -Z end, 2.0–2.6 s | Stone or ruined walls, torches, a little clutter |
| *Gargoyle crypt* | 3 × 5–7 tiles | 2–5 rows of gargoyle heads, alternating walls | Alternate, 2.6–3.2 s | Crypt walls, sconces, candles, bones, webs |
| *Pendulum hall* | 3 × 6–8 tiles | 4–6 rows of three blades | Wave, 1.8–2.4 s; neighbouring blades half a beat apart, so the gaps between them open and close like scissors | Stone or prison walls, torches |
| *Blade passage* | 1 × 6–9 tiles: a hallway | 4–7 single blades | Wave, 1.8–2.4 s; each blade pins one wall at a time, so the hero passes on the side it has left | Stone or ruined walls, torches |

A spout row whose wall has no plain stretch at that tile (an alcove or a pillar) hangs its spout on the opposite wall instead, so ornate walls such as the crypt's still fill their rows. A blade swings as far as its walls and its neighbours allow: the step gives each blade the space it has (`HazardBeat.Space`), to just short of the wall or halfway to the next blade, which swings toward it at the same time, and the blade narrows its own `Sweep` to fit.

The cathedral nave (pendulums down the aisle with gargoyles firing across) is a `PendulumRowsStep` and a `CrossFireStep` with interleaved rows; it needs a row offset on the step and waits for the Cathedral's turn.

Gauntlet rooms may headline a floor (see Realm schedule). A reward at the far end ("the vault behind the blades") needs a room-owned reward seam that does not exist yet; see Open decisions.

**Corridor gauntlets.** A corridor step puts hazards on straight corridor spans (`CorridorBlueprint.Spans`) of at least four tiles, away from thresholds, junctions and doors: slits along one wall firing across, or pendulums swinging across the corridor. The hallway and prison plans carve their halls as corridor tiles, so their long central halls are natural gauntlet hallways.

**Realm schedule (proposal):**

| Realm | Introduces | Corridors | Rooms |
| --- | --- | --- | --- |
| Garrison 1–5 | slits, then gargoyles | slit spans from floor 2 | arrow gallery from floor 3; gargoyle crypt on `undead` floors |
| Prison 6–10 | pendulums | slit and pendulum spans | pendulum hall (headline candidate 2–4, the execution corridor) |
| Sewer 11–15 | (later: acid pipes, as a spout projectile) | — | — |
| Mines 16–20 | spike strips (optional) | spike spans | — |
| Cathedral 21–25 | combinations | gargoyle spans | gargoyle crypt, pendulum hall, cathedral nave |

Windows, chances and weights are realm data; the table is a starting point for playtests.

## Code shape

### New runtime types

```csharp
/// <summary>One arrangement's beat and wakefulness. Generation creates it as the parent of its hazards.</summary>
[GlobalClass] public partial class TrapGauntlet : Node3D
{
	[Export] public float ActivationRadius { get; set; } = 16f;
	/// <summary>Seconds since the gauntlet entered the tree; advances with physics, pauses with the game.</summary>
	public double Clock { get; }
	public bool IsAwake { get; }
	/// <summary>World-space footprints of every member's danger, for generation exclusion and debug draw.</summary>
	public IEnumerable<Aabb> DangerBounds { get; }
}

/// <summary>A member of a gauntlet: synchronized from the gauntlet's physics tick, never self-timed. A tool script.</summary>
/// Damage, DamagePerDepth and the trap group come from the shared Trap base (triggers plan).
[Tool, GlobalClass] public abstract partial class TrapHazard : Trap
{
	[Export] public float Period { get; set; } = 2f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Phase { get; set; }
	public virtual void Tune(HazardBeat beat);
	/// <summary>hero is the hero the gauntlet woke for, null while asleep: hold still, never fire, hit or warn.</summary>
	public abstract void Sync(double clock, Node3D hero);
	public abstract Aabb DangerBounds { get; }
}

[Tool, GlobalClass] public partial class WallSpout : TrapHazard
{
	[Export] public TrapShotComponent Shot { get; set; }        // projectile, muzzle, speed, range, element, defense traits
	[Export] public AttackIndicatorComponent Indicator { get; set; }  // optional lane telegraph during wind-up
	[Export] public Node3D WindUpEffect { get; set; }            // optional glow, embers or dust
	[Export] public float WindUp { get; set; } = .6f;
	[Signal] public delegate void FiredEventHandler(Node3D shot);
}

[Tool, GlobalClass] public partial class PendulumBlade : TrapHazard
{
	[Export] public Node3D Arm { get; set; }       // turns about this node's origin, the pivot above the walls
	[Export] public ShapeCast3D Edge { get; set; } // the blade's cutting volume, an overlap query
	[Export] public float Sweep { get; set; } = 1.9f;       // metres either side at most; a beat's Space narrows it
	[Export] public float Knockback { get; set; } = 4f;
}

/// <summary>Depth scaling shared by every trap, the spike plate's <see cref="SpikeBurst.CurrentDamage"/> included.</summary>
public static class TrapDamage { public static int At(int damage, float perDepth, uint depth); }
```

Contracts:

- **Spout shots** are pooled `Projectile`s (`ScenePool.Spawn`) launched with the spout as attacker. The spout is in the `trap` group, so the shot's faction is `Trap`, it collides with the player and enemy layers, and enemies filter it. Shots take their seed from `GenerationRandom.DeriveSeed(level seed, "trap", spout id, shot index)`; trap shots never crit, so the seed only keeps traces reproducible.
- **Blade contact** is a `DamageContext` with `DeliveryKind = Hazard`, `DefenseTraits = Dodgeable`, `Faction = Trap`, `IncomingDirection` straight out of the blade's path toward the victim's side, poise equal to damage and a forced push sized to carry the victim to `Clearance` from the path. Each actor is hit at most once per pass (half period), and the push keeps the next pass from reaching them. Hazard delivery has no hit zone, so no rear-hit bonus.
- **Spikes** (optional slice) use `DefenseTraits = None`: dodge immunity never avoids them.
- **Rod fade:** `pendulum_rod.gdshader`, alpha from the rod's local height (opaque at the blade, gone a little below the wall tops), `blend_mix`, `depth_draw_never`; no `GeometryInstance3D.Transparency` (Mobile renderer). The blade stays opaque.
- Hazard scenes live in `scenes/props/traps/` (`wall_spout_slit.tscn`, `wall_spout_gargoyle.tscn`, `pendulum_blade.tscn`, later `spike_strip.tscn`) and are the authored definitions, like `pressure_plate_spikes.tscn`. Each look (`scenes/props/traps/models/`) is shared with the wall launcher of the same look.

### Generation

Landed for rooms (slice 3a):

```csharp
/// <summary>One hazard's beat: period, phase in the gauntlet's clock, and for a swing the free space beside it (0: none).</summary>
public readonly record struct HazardBeat(float Period, float Phase, float Space = 0);

/// <summary>Rows across the door-to-door spine, each covering the room's width. Put it first.</summary>
[Tool, GlobalClass] public abstract partial class HazardRowsStep : FurnishingStep
{
	[Export] public HazardPattern Pattern { get; set; } = HazardPattern.Wave;   // read along +Z
	[Export] public Vector2 PeriodRange { get; set; } = new(2f, 2.6f);
	[Export(PropertyHint.Range, "1,6")] public int RowSpacing { get; set; } = 1;   // tiles
	[Export(PropertyHint.Range, "1,3")] public int EndMargin { get; set; } = 1;    // doorway tiles kept clear
	protected abstract bool LayRow(RoomBlueprint room, RandomNumberGenerator rng, int row, int[] columns, int index, HazardBeat beat);
}

public enum SpoutSides { Alternate, Both, Left, Right }
[Tool, GlobalClass] public partial class CrossFireStep : HazardRowsStep { [Export] public SpoutSides Sides { get; set; } }
[Tool, GlobalClass] public partial class PendulumRowsStep : HazardRowsStep { [Export] public float MinimumSpace { get; set; } = 1.2f; }
```

- `PlacedPiece` carries an optional `HazardBeat`; the steps set it with `RoomBlueprint.AssignBeat`. A pendulum row is one placement plan, so it stands whole or not at all. `RoomBlueprint.EmitProps` puts beat pieces under one `TrapGauntlet` per room and tunes them before they enter the tree.
- The step reserves each row's band (`RoomBlueprint.Reserve`), so later furniture never stands in a lane or under a blade. Hazards are non-blocking: walkability and navigation do not change.
- Patterns stagger rows by fractions of the beat (`TrapGauntlet.PhaseFor`), never by a walking speed.
- Periods come from the room step's stream; no new stream.

Placement (slice 3b, landed): `GauntletProfile` deals a run's gauntlets from the pool's `Gauntlets` slot (`RoomPoolSlot.Gauntlet`); a gauntlet floor builds its descent from the `gauntlet_exit` group (`FloorBuildContext.Goal`) and draws only plans whose layout `HoldsGauntlets`; `GeneratedFloor` counts the tiles of the group's gauntlet slot as occupied. Remaining:

```csharp
/// <summary>Corridor step: the same rows on straight spans.</summary>
[GlobalClass] public partial class CorridorHazardStep : CorridorStep { /* Layout, Pattern, PeriodRange, MinimumSpan */ }
```

- Corridor gauntlets on the required route.
- `CorridorBlueprint` carries beats and emits gauntlets as rooms do.
- Corridor gauntlets have no room, so they need their danger tiles recorded instead (`DangerBounds`, measured once physics knows the walls).
- `MapGenerator.RemoveWallPropsOffMounts` drops wall props whose wall a corridor replaced; a removed spout leaves a row empty, so placement must keep spout walls or drop the whole row.

### Call order

```
MapGenerator.Generate
├─ rooms assemble ── RoomAssembler runs the hazard row step first
│   └─ HazardRowsStep: rows on the spine → each layout lays its row → reserve lanes/arcs → pieces carry a HazardBeat
│      invariant: rows ≥ RowSpacing apart; doorway tiles and thresholds never in a lane or arc
├─ RoomBlueprint.EmitProps → TrapGauntlet per beat key, hazards configured before _Ready
├─ DecorateCorridors → CorridorHazardStep on Spans → CorridorBlueprint.EmitProps (same grouping)
├─ Floor.FreezeDecorations → collect gauntlets, record hazard tiles
├─ PlaceLoot / PlaceShops / GenerateEnemySpawnPoints   (skip hazard tiles)
└─ Floor.Freeze

TrapGauntlet._PhysicsProcess (priority from CombatPhysics, before projectiles)
├─ Clock += delta
├─ awake = hero within ActivationRadius && any member tile explored
└─ each member: Sync(Clock, awake)
    ├─ WallSpout: entering wind-up → telegraph on; cycle boundary → spawn projectile, emit Fired
    └─ PendulumBlade: pose from clock; on overlap, resolve once per pass
```

### Persistence and seeds

Hazards are seeded content with no state of their own, so saves gain nothing. New room types and corridor steps change generated floors: bump `SaveGame.CurrentVersion`.

### Affected files

```
+ scenes/props/traps/TrapHazard.cs, TrapGauntlet.cs, WallSpout.cs, PendulumBlade.cs, TrapDamage.cs, HazardBeat.cs
+ scripts/PlayerLookup.cs                                (the hero lookup Nameplate and gauntlets share)
+ scenes/props/traps/wall_spout_slit.tscn, wall_spout_gargoyle.tscn, pendulum_blade.tscn, pendulum_rod.gdshader
~ scenes/props/traps/SpikeBurst.cs          (CurrentDamage through TrapDamage)
+ scenes/levels/generators/assembly/steps/HazardRowsStep.cs, CrossFireStep.cs, PendulumRowsStep.cs
+ scenes/levels/generators/corridors/CorridorHazardStep.cs
~ scenes/levels/generators/assembly/RoomBlueprint.cs     (PlacedPiece.Beat, gauntlet grouping on emit)
~ scenes/levels/generators/corridors/CorridorBlueprint.cs
~ scenes/levels/generators/GeneratedFloor.cs             (hazard tiles)
+ scenes/levels/rooms/props/traps/dart_slit.tres, gargoyle_head.tres, pendulum_blade.tres   (hazard props)
+ scenes/levels/rooms/room_types/gauntlets/arrow_gallery.tres, gargoyle_crypt.tres, pendulum_hall.tres, blade_passage.tres
~ scenes/levels/rooms/room_pool.tres, scenes/levels/realms/*.tres, *_corridors.tres   (3b: placement)
~ scenes/debug/rooms/RoomGenerationHarness.cs           (lists the gauntlet rooms)
+ tests/GauntletRoomTest.cs
+ scenes/ui/debug/TrapTool.cs                           (debug menu: lists scenes/props/traps/)
~ scenes/ui/debug/DebugCatalog.cs, debug_menu.tscn       (hazard scene listing; the Traps submenu)
~ scripts/saves/SaveGame.cs                             (CurrentVersion)
~ scripts/balance/params.py, content.py, model.py, simulate.py   (HAZARDS, slice 5)
+ tests/TrapHazardTest.cs
~ tests/GeneratedFloorSweepTest.cs
~ docs/level-design.md, docs/realm-content.md, docs/combat-system.md   (once implemented)
```

## Debugging and tests

- **`TrapTool`** (debug menu **Traps**, landed) lists the hazard scenes in `scenes/props/traps/` and stages 1, 3 or 5 rows ahead of the hero, two tiles apart, on a Unison, Alternate or Wave beat, in the arena or on a floor. A wall spout hangs on the first wall beside the path, or on a stand-in wall in open space. New hazard scenes show up without code changes.
- **`TrapHazardTest`** (runtime, fixed delta, outcomes only): a hero standing in a spout lane takes one hit per shot; a dodge timed through the shot is `Dodged`; a raised shield blocks a dart; a pendulum cuts a hero once and pushes them out of its path, so later swings miss; a hero outside the arc is never hit; an asleep gauntlet (hero far, tiles unexplored) fires nothing; monsters in a lane take no damage and stop darts.
- **`GauntletPlacementTest`** (landed): over 200 runs, two or three gauntlets on ordinary floors, never repeated, each suiting its realm; on generated Garrison and Prison gauntlet floors the gauntlet stands between the arrival and the descent and holds no spawns, loot or traps.
- **`GauntletRoomTest`** (landed): every room in the gauntlet folder, over three seeds, puts its hazards under one gauntlet in at least two full-width rows; spouts hang on side walls firing straight across; blades never swing into a wall or a neighbour; no blocking furniture stands in a row. A room staged in the arena wakes its gauntlet for the hero at its entrance.
- Visual checks: a GIF of each hazard in the arena (`record_gif.sh`), and `render_level_topdown.gd` for a floor with gauntlets.

## Slices

1. **Wall spouts and gauntlets (landed).** `TrapGauntlet`, `TrapHazard`, `WallSpout`, `TrapDamage`, a dart spout (the crossbow bolt) and a fireball spout (the fireball spell's projectile) with primitive placeholder mounts, `TrapTool`, `TrapHazardTest`. Exercisable in the arena; nothing generates yet. Playtest note: at game zoom the dart is hard to see in flight, so its lane warning carries the read; the art slice should give it a bigger silhouette or a trail.
2. **Pendulum blade (landed).** `PendulumBlade`, the rod fade shader, a crescent placeholder blade (CSG), tests for the cut, push and dodge-through. For slice 3: the crescent is 1.6 m wide, so a 4 m corridor cannot keep the middle safe at both extremes; corridors need a narrower swing (the blade fitted to the measured walls) or a narrower blade, and the hero passes on the side the blade is not.
3. **Gauntlet rooms (3a, landed).** `HazardBeat` on placed pieces, `CrossFireStep` and `PendulumRowsStep`, the four room types, the harness listing and **Traps → Rooms**, `GauntletRoomTest`. Playable in the arena; no floor places them.
   **Placement (3b, landed).** The run's gauntlet schedule, the approach group before the descent, gauntlet rooms kept free of spawns and loot, save version bump, `GauntletPlacementTest`.
   **Corridor gauntlets (3c, later).** `CorridorHazardStep` spans with recorded danger tiles.
4. **Art (landed).** Studio furnishings `dart_slit`, `gargoyle_head`, `pendulum_blade` and `pendulum_rod` in the floor tiles' pale stone and the pack's iron and steel, exported to `assets/custom-props/traps/`. The wall mounts keep the placeholders' measured bounds, so rooms hang them where they did and the muzzles stay put; the blade now counts in the pendulum's measured footprint, so decor in pendulum rooms keeps out of the blade rows. The rod and blade share one shading ramp (recipe `height_range`), the rod keeps its fade shader on the pack texture, and the blade keeps its faint glint. The dart itself is still the crossbow bolt.
5. **Balance (landed).** `HAZARDS` in the model (damage shares, gauntlets per run, the rooms by hazard, hits per crossing), `derive` writes the hazard scenes' `Damage`/`DamagePerDepth`, `simulate` deals each run's gauntlets and counts their attrition, `content.py` audits the scenes, the gauntlet profile and the rooms. The hits per crossing are an estimate until playtests measure them.
6. **Optional:** spike strips; the reward seam for vault gauntlets; acid pipes for the Sewer.

## Open decisions

- **Dodge through blades?** Proposed yes (dodge immunity), so the dodge is the recovery tool the goals describe; spikes are the pure-timing exception. The alternative makes blades undodgeable and every gauntlet strictly a timing test.
- **Should hazards hurt monsters?** Proposed no for now, matching floor traps, because chasing monsters would need hazard-aware flow fields to avoid walking into blades. Yes would make luring monsters into gauntlets a tactic.
- **Vault gauntlets:** a guaranteed chest behind a gauntlet needs rooms to own a reward anchor that `MapGenerator.Loot` fills; today no room type places loot. Worth its own small plan if wanted.
- **Burning from fire spouts:** burning is a spell-owned contact (`BurningContact` on a `StatusCondition`), not an on-hit buff a projectile can carry, so fire spouts deal fire damage only. Giving them a burn needs a trap-owned burning contact.
- **Monsters inside gauntlets:** proposed none at first (no spawns on hazard tiles); combining patrols with hazards is a later tuning lever.
