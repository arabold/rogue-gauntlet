# Realm monster roster

Status: proposed. The current roster, monster composition and action system are in [monster design and authoring](../monster-authoring-design.md); realm structure and encounter budgets in [level design](../level-design.md#current-generation); stats and threat in the [balance model](balance-model.md). This plan names the monsters of realms 2–5, the capabilities they need that the game lacks, and the order to build them in. It authorizes design, not implementation.

## Goals

- Each realm gets a cast of classic roguelike monsters (Rogue, NetHack) that reads at a glance in the KayKit style: chunky silhouettes, big heads, simple shapes. No complex anatomies (dragons, centaurs, griffins).
- Like the Garrison's skeletons, some realms field one **tiered humanoid family**: one base body whose tiers differ in gear, size, color and one capability. Orcs hold the Prison, gnomes the Mines.
- Monsters vary in **how they move and attack**. Each realm introduces at least one new way to move, so every realm teaches the hero something.
- Every realm ends on a gatekeeper with a signature mechanic.
- Some monsters break the rules the way Rogue's best-remembered ones did: thieves that steal and run, a chest that bites, an invisible stalker, a gaze that turns the hero to stone, and growths that grab from the floor.

Non-goals: depth-scaled stats (every monster keeps the fixed strength the balance model derives for it), realms beyond the fifth, and reusing the existing Asset Studio monster catalog. Monsters are built new for this roster.

## Roster

Each table gives the classic source, the [balance archetype](../../scenes/enemies/MonsterArchetype.cs) the monster is derived as, and how it moves and fights. **New** marks a capability from [New capabilities](#new-capabilities); everything else composes existing actions, strategies and components.

### Garrison, depths 1–5 (unchanged)

Skeleton minion, warrior, crossbowman and mage, the plague rat, and the Forsaken Warden. See the [monster catalog](../monster-catalog/README.md).

### Prison, depths 6–10: the orc warband

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Orc grunt | Rogue `O`, NetHack hill orc | Standard | Charges into heavy, slow melee swings |
| Orc spearman | NetHack Mordor orc | Shooter | Keeps mid range and throws spears: a straight projectile, like the crossbow bolt |
| Orc shaman | NetHack orc shaman | Caster | Hangs back; hastes and heals allies and curses the hero with existing monster spells |
| Orc captain | NetHack Uruk-hai | Brute | Shield guard and bash; empowers grunts near it |
| Giant bat | Rogue `B`, NetHack giant bat | Swarmer | **Flight**: fast, erratic swoops over water, chasms and low obstacles |
| Nymph | Rogue `N` | Fodder | Slips close, **steals** an item from the hero's pack and **escapes**; killing her before she gets away drops it |
| **Troll** (gatekeeper) | Rogue `T`, NetHack troll | Boss | Club swings and a ground slam; **revives** unless finished while down |

Nest: a bat roost that releases giant bats.

From the Prison on, any realm may also hide a **chest mimic** (see [Chest mimic](#chest-mimic)).

### Sewer, depths 11–15: oozes and vermin

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Acid blob | NetHack acid blob | Fodder | Slow; **corrodes** whoever strikes it in melee |
| Spitting slime | NetHack green slime | Shooter | Keeps its distance and **lobs** arcing globs that leave a lingering poison pool |
| Plague rat | Garrison | Swarmer | Returns in larger packs |
| Aquator | Rogue `A` | Standard | Shambles in; its touch **rusts armor**, lowering the hero's armor for a while |
| Snake | Rogue `S` | Swarmer | Coiled ambush; a fast venomous lunge (existing dash and venom), then retreats |
| Sewer tentacles | Grasper family | Standard | Lie hidden under a drain or puddle like a trap; **grasp** the hero who steps close and squeeze with poison until the hero fights free |
| **Gelatinous cube** (gatekeeper) | NetHack gelatinous cube | Boss | Slow and relentless; a transparent cube with bones and loot inside. **Lobs goo** that traps the hero in place while poison digests them, and leaves slowing slime behind it |

Nest: a slime vat that releases acid blobs.

### Mines, depths 16–20: the Gnomish Mines

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Gnome | NetHack gnome | Fodder | Small; comes in mobs and flees when hurt |
| Gnome lord | NetHack gnome lord | Shooter | Carries a small crossbow |
| Gnome king | NetHack gnome king | Caster | Leads a mob and empowers gnomes near it |
| Dwarf | NetHack dwarf | Brute | Slow, heavily armored, hits hard with a mattock |
| Soldier ant | NetHack soldier ant | Swarmer | Very fast zigzag charge, in pairs |
| Piercer | NetHack piercer | Standard | **Ambush**: clings to the ceiling and drops on whoever walks beneath, then crawls slowly |
| Living roots | Grasper family | Standard | Burst from the earth like a trap; **grasp** and crush the hero until the hero fights free; tougher and slower to break than tentacles |
| Rattlesnake | Rogue `R` | Swarmer | The snake's dry-tunnel cousin: a rattle warns, then a lunge whose venom also **saps strength** for a while |
| Leprechaun | Rogue `L` | Fodder | Darts in, **steals** gold and **escapes** with short blinks; killing it before it gets away drops the gold |
| **Umber hulk** (gatekeeper) | NetHack umber hulk | Boss | **Burrows** under the floor and surfaces beside the hero; a **gaze** that confuses a hero who faces it |

Nest: an ant hill that releases soldier ants.

### Cathedral, depths 21–25: the unholy

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Gargoyle | NetHack gargoyle | Brute | **Ambush** as a statue until the hero passes, then **flight**; stone skin |
| Wraith | Rogue `W` | Standard | Floats (**flight**) over chasms and low obstacles; a **draining** touch heals it |
| Ghost | NetHack ghost | Fodder | **Drifts through closed doors**; slow but hard to shake |
| Vampire bat | NetHack vampire bat | Swarmer | **Flight**; a **draining** bite |
| Phantom | Rogue `P` | Standard | Always **invisible**: given away only by a shimmer, its footsteps and its hits |
| Medusa | Rogue `M`, NetHack medusa | Shooter | An elite. After a clear tell her **gaze** sweeps a wide cone; a hero still facing her when it lands is **turned to stone** for a moment, so the hero must turn around. The gaze does no damage; she hurts with **thrown snakes**, straight venomous projectiles like crossbow bolts, which find a petrified hero defenseless. Snake hair as chunky KayKit coils |
| **Lich** (gatekeeper) | NetHack lich | Boss | Teleports in short **blinks**, casts, and **summons** the Garrison's skeletons |

Nest: a haunted reliquary that releases ghosts.

## New capabilities

Each capability is a reusable component, action or strategy, authored on the monsters that use it. No monster-specific branches in shared AI. Proposed names are marked; existing names are linked or plain.

| Capability | Users | Builds on |
| --- | --- | --- |
| Flight | Giant bat, vampire bat, gargoyle, wraith | Clearance grid, `NavigationComponent`, `EnemyMovementStrategy` |
| Arcing lob and ground pools | Spitting slime, gelatinous cube | `Projectile`, `LingeringAreaField`, `AttackIndicatorComponent` |
| Goo bind and petrify | Gelatinous cube, medusa | `FrozenStatusComponent` mechanics, `StatusCondition`, poison status, the Stone Skin look |
| Grasp | Sewer tentacles, living roots | Ambush, poise, poison |
| Theft and escape | Nymph, leprechaun | Inventory, gold, blink |
| Chest mimic | Any realm from the Prison on | Chest placement, ambush |
| Contact corrosion | Acid blob | Hurtbox events, `BuffController` |
| Armor rust and strength sap | Aquator, rattlesnake | Buff modifiers on armor and attributes |
| Revive | Troll | `HealthComponent`, `HealthPhaseComponent` |
| Ambush | Piercer, gargoyle, graspers, chest mimic | `InvisibilityComponent`, attack indicator |
| Burrow | Umber hulk | Ground navigation, `InvisibilityComponent` |
| Gaze | Umber hulk, medusa | `MonsterTimedAction`, attack indicator, confusion status, petrify |
| Permanent invisibility | Phantom | `InvisibilityComponent` |
| Door phasing | Ghost | Clearance grid gates |
| Drain | Wraith, vampire bat | Attack payloads, healing |
| Summon and blink | Lich (blink also for the thieves) | Nest summon rules, `MonsterAction` |

### Flight

Flyers cross water, chasms and low solids (fences, rubble, crates, low furniture), never walls, closed doors or tall props.

Landed for the giant bat; [level design](../level-design.md#doors-and-enemy-navigation) and [monster authoring](../monster-authoring-design.md) describe it. The roost nest remains.

- **Air grid (landed):** `GeneratedFloor.AirClearance`, where pits count as floor and solids no taller than `ClearanceGrid.AirClearanceHeight` (1.2 m) and water's walking barriers do not block. Walls, void, doors and tall solids are built once and copied for both grids.
- **Layer choice (landed):** `NavigationComponent.Layer` on the monster, since the level's `NavigationProfile` is shared by every monster. `INavigableWorld.NavigationFor(layer)` returns the level's or the arena's `AirNavigation`.
- **Body (landed):** the flyer's body hovers above `AirClearanceHeight`, its mask leaves out walking terrain and its movement has no gravity; validation reports a flyer missing any of these. Its hurtbox stays inside the hero's melee arcs and projectile height.
- **Movement (landed):** `ErraticFlightStrategy` wraps another strategy (bite and dart) and replaces its holds and close approaches with a `Hover` goal: darts to fresh points around the target every burst, so the bat never holds still long enough to be pinned.
- **Drops (landed):** a drop with no free spot in throwing range lands on the nearest free floor in sight.

### Arcing lob and ground pools

- **Trajectory (proposed):** `ProjectileTrajectory { Straight, Arc }` on the projectile's definition. An arc flies over low cover and allies to a point aimed at the target's lead, so ducking behind a crate does not help; the hero must step aside.
- **Telegraph:** the landing point shows through `AttackIndicatorComponent` for the whole flight.
- **Impact:** the projectile spawns a `LingeringAreaField` at the landing point. A new `LingeringAreaKind.PoisonPool` applies the existing poison status while the hero stands in it. Fields keep their current ownership and cancellation rules.

### Goo bind and petrify

Two short holds that stop the hero completely, the way freezing does, each with its own look.

- **Goo bind:** the gelatinous cube's goo replaces swallowing the hero. It reuses the arcing lob; on a hit a `goo_bound` `StatusCondition` (proposed) holds the hero, shown with a goo `WorldEffectDefinition`, while the existing poison status digests them for the hold's duration. The cube also leaves a slowing slime trail through the existing `SlowStatusComponent` conditions.
- **Petrify:** medusa's gaze applies a `petrified` condition (proposed), shown with the existing Stone Skin surface effect, to a hero caught facing her when the gaze lands (see [Gaze](#ambush-burrow-gaze-and-invisibility)). It deals no damage; the danger is her thrown snakes while the hero stands helpless.
- **Wiring:** each condition is held by its own `FrozenStatusComponent` instance on the player, with its own appearance (see open decision 2).

### Grasp

One monster type with two looks: **sewer tentacles** (slimy, poison) and **living roots** (bark, crushing, tougher). Like a trap, a grasper waits hidden in the floor; unlike a trap, the hero has to fight it.

- **Family:** `scenes/enemies/grasper/` holds one scene setup and two variants, `grasper_tentacles` and `grasper_roots`, that differ in model, health, poise, squeeze damage and status (poison for tentacles, none for roots).
- **Hidden:** an `AmbushComponent` keeps it under the floor, marked only by a disturbed patch (a bubbling drain, cracked earth). The game's Intelligence-based trap discovery may reveal it early (open decision 8).
- **Grab (proposed):** `MonsterGraspAction : MonsterAction` erupts under the hero and applies a `grasped` `StatusCondition` (proposed). A grasped hero cannot move or dodge but can attack, guard and use items. The squeeze deals damage over time.
- **Breaking free:** the grip ends when the grasper's poise breaks (blunt weapons break it faster), when it dies, or after `MaximumHold`. After a release it sinks back and can grab again after its cooldown. It never moves, so it needs no navigation.
- **Packs:** graspers join packs as stationary members, so the monsters around a held hero get free hits. That pressure is the point.

### Theft and escape

- **Steal (landed for the nymph):** `TheftComponent` takes a random item stack the hero is not wearing on the thief's first damaging melee touch, and the thief carries it ([monster authoring](../monster-authoring-design.md)). Gold for the leprechaun remains.
- **Escape:** after a theft the thief runs out of the hero's reach and vanishes for good with its loot once it has stayed out of the hero's sight or reach for `EscapeTime`; killing it first drops what it carries at its feet. Both landed, and so did fleeing with blinks: the nymph blinks away from a hero who keeps up (`MonsterBlinkAction`).
- **Persistence:** the hero's loss is ordinary inventory and gold state. What an escaped thief took is gone; nothing tracks it across floors.

### Chest mimic

Rogue's Xeroc, as a chest. From the Prison on, a placed chest may be a mimic.

- **Placement (landed):** chest placement rolls `MimicChance` per chest from the realm's encounter factory on its own seeded stream (15% in the Prison and the Sewer). A mimic takes the chest's place and persistent ID and drops the common chest's loot.
- **Reveal (landed):** an `AmbushComponent` shows the ordinary chest and wakes it when the hero tries to open it, lingers beside it or hits it. It then plays its wake clip and hops and bites as a brute.
- **Loot and persistence:** killing it drops the chest's loot. Its cleared ID persists like a monster's, so a reloaded floor never turns it back into a closed chest.

### Contact corrosion, armor rust and strength sap

- **Contact corrosion (landed):** `ContactRetaliationComponent` on the acid blob burns an attacker whose melee hit lands with acid (a short poison, `acid_burn.tres`), read from the hurtbox's contact event. Ranged hits are safe.
- **Armor rust and strength sap:** the aquator's attack payload applies a timed buff that lowers armor, and the rattlesnake's venom one that lowers Strength, both through the existing modifier system. No new component.

### Revive

- **Component (proposed):** `ReviveComponent` on the troll. When health reaches zero, the troll falls into a downed state for `DownedDuration` instead of dying. Any hit while it is down kills it for good. Otherwise it stands up at `ReviveHealthShare` of its maximum.
- **Persistence:** the gatekeeper's defeat and cleared ID are recorded only on the final death, so the descent stays sealed while the troll is down.

```text
HealthComponent: health reaches 0
└─ ReviveComponent intercepts (first time, or until finished)
   ├─ downed: action gate disabled, downed clip, hurtbox stays live
   ├─ hit while downed → HealthComponent dies → Died → gatekeeper cleared, seal lifts
   └─ timer ends → health restored to ReviveHealthShare → resume behavior
```

### Ambush, burrow, gaze and invisibility

- **Ambush (landed for the chest mimic and the snake):** `AmbushComponent` keeps its actor inactive and out of the hero's targeting, shown as a `Disguise` scene or resting in a `DormantClip`, until the hero opens the disguise, comes within `WakeRadius` in its sight for `WakeDelay`, or hits it; then it plays its `WakeClip` and hunts. Remaining: hiding through `InvisibilityComponent` and a configured start action for the piercer's drop (a timed attack whose indicator marks the floor below) and the gargoyle's waking roar before it flies. While dormant, a gargoyle can use a cathedral statue as its disguise.
- **Burrow (proposed):** `MonsterBurrowAction : MonsterAction`. The actor submerges (untargetable and hidden, with a moving dirt trail), travels the ground grid to a point beside the target and surfaces with a knockback burst. It moves under floors, never through walls, so it needs no digging rules.
- **Gaze (proposed):** `MonsterGazeAction : MonsterTimedAction`, a telegraphed attack that deals no damage and applies an authored status instead: petrify for medusa, confusion for the umber hulk.
  - **Cone:** `ConeAngle` (120° default; 160° or 180° for a harsher gaze) and `Range`, in front of the actor. `AttackIndicatorComponent` draws the cone through the whole wind-up.
  - **One check at contact:** a target inside the cone, in the actor's line of sight, whose facing (`MovementComponent.LookAtDirection`) points toward the actor (within `FacingTolerance`, 90° by default) gets the status. A hero who has turned around, left the cone or ducked behind a pillar is safe.
  - **Cost of turning away:** while turned around the hero cannot aim at her, so each gaze is a beat where attacking stops and the hero has to read the tell and turn.
- **Medusa's damage:** her second action is an ordinary monster projectile attack whose projectile is a thrown snake carrying the existing venom status. Her action weights favor it right after a gaze lands, so a petrified hero is punished.
- **Permanent invisibility:** the phantom starts invisible through `InvisibilityComponent` and never drops it. A faint shimmer, footstep sounds and its own hits give it away, so it stays fair.

### Door phasing, drain, summon and blink

- **Door phasing (proposed):** `NavigationProfile.IgnoresDoors` makes the actor plan with every gate open, and its collision mask leaves out door bodies, so a ghost drifts through closed doors.
- **Drain:** an attack payload share that heals the attacker by part of the damage it deals. Wraiths and vampire bats use it.
- **Summon (proposed):** `MonsterSummonAction : MonsterAction` spawns from an authored scene list near the caster, capped by its living summons. Summons follow nest-summon rules: no rewards, never persisted, collapsing when their summoner dies.
- **Blink (landed for the nymph):** `MonsterBlinkAction : MonsterAction` teleports to a reachable point at an authored distance band from the target, on the actor's side of it, with the teleport fade out and in; a stagger during the fade cancels it. The lich will want a band around the target rather than away from it, a `Spread` of 180.

## Encounters

- **Factories:** each realm gets a `RealmEncounterFactory` with `FloorCount = 5`: `prison_encounters.tres`, `sewer_encounters.tres`, `mines_encounters.tres` and `cathedral_encounters.tres` in `scenes/levels/encounters/`. They chain through `Fallback`, the existing seam: `sunken_garrison → prison → sewer → mines → cathedral → dungeon_mob_factory`. The generator keeps one `MobFactory`, so no generation code changes.
- **Gatekeepers and nests:** each factory names its gatekeeper scene, fit and name, and its nest scene. Every realm already names `boss.tres` as its `BossProfile`, so gatekeeper floors take the linear boss plan as soon as their factory plans a gatekeeper.
- **Graspers and mimics:** graspers are stationary pack members placed at ordinary spawn points. `MimicChance` (proposed) on each factory sets how often its chests are mimics; the Garrison's stays zero.
- **Packs:** each realm's packs go into `scripts/balance/params.py` with depth bands inside the realm. For example, Prison: grunt pair, spearman sentry, shaman warband, captain's guard post, bat swarm. `simulate.py derive` writes stats and threat; `audit` checks the authored entries against the model.

## Art

- Built with the [Asset Studio](../../asset-studio/README.md) monster pipeline in the KayKit style. Humanoids use KayKit rigs: medium for orcs, gnomes and dwarves, large for the troll and umber hulk. They share the rig's clips where possible, and missing clips (cast, throw, hover) are authored per rig.
- Families share one base body per rig: tiers change gear, size, palette and one silhouette mark (the captain's shield, the king's crown).
- The grasper's tentacles and roots share one simple chain rig and clip set (erupt, grab, squeeze, sink); only the mesh and materials differ.
- Blobs, the gelatinous cube and the piercer animate by transform and shader (squash and stretch, wobble) with no skeleton. The cube's transparency uses instance-local alpha materials, never `GeometryInstance3D.Transparency`.
- Each monster lives in its own folder: `scenes/enemies/<family>/<family>_<tier>.tscn` and `<family>_<tier>_definition.tres`, like the skeletons.

## Affected files

```text
+ scenes/enemies/orc/, troll/, giant_bat/, nymph/       slices 1, 2 and 4
+ scenes/enemies/slime/, aquator/, snake/, grasper/, gelatinous_cube/
+ scenes/enemies/gnome/, dwarf/, soldier_ant/, piercer/, leprechaun/, umber_hulk/
+ scenes/enemies/chest_mimic/
+ scenes/enemies/gargoyle/, wraith/, ghost/, vampire_bat/, phantom/, medusa/, lich/
+ scenes/levels/encounters/{prison,sewer,mines,cathedral}_encounters.tres
~ scenes/levels/encounters/sunken_garrison.tres     Fallback → prison_encounters
~ scenes/levels/navigation/NavigationProfile.cs       IgnoresDoors
~ scenes/levels/navigation/INavigableWorld.cs         NavigationFor(layer)
~ scenes/levels/generators/MapGenerator.Navigation.cs, GeneratedFloor.cs   air grid
~ scenes/levels/Level.cs, scenes/debug/stage/DebugArena.cs   one FloorNavigation per layer
~ scenes/enemies/NavigationComponent.cs               resolves its layer
+ scenes/enemies/strategies/ErraticFlightStrategy.cs
~ scenes/attacks/Projectile.cs (+ its definition)     arc trajectory, impact field
~ scenes/magic/shared/LingeringAreaDefinition.cs      PoisonPool kind
+ scenes/components/ReviveComponent.cs, AmbushComponent.cs, ContactRetaliationComponent.cs
+ scenes/enemies/actions/MonsterBurrowAction.cs, MonsterGazeAction.cs, MonsterSummonAction.cs, MonsterBlinkAction.cs,
  MonsterGraspAction.cs, MonsterStealAction.cs
+ scenes/buffs/conditions/goo_bound.tres, petrified.tres, grasped.tres
+ scenes/components/GraspedStatusComponent.cs           no movement or dodge while grasped
~ scenes/player/player.tscn                           goo-bound, petrified and grasped status components
~ scenes/levels/encounters/RealmEncounterFactory.cs   MimicChance
~ scenes/levels/generators/MapGenerator.Loot.cs       chest mimics
~ scripts/balance/params.py                           realm packs
~ docs/monster-authoring-design.md, docs/monster-catalog/README.md, docs/level-design.md   as each slice lands
```

## Slices

Each slice lands playable: its realm's floors spawn the new roster, and the harness can stage each monster.

1. **Prison on the ground.** Orc family, the troll with revive, and the prison factory chained from the Garrison. No nest yet.
   - Landed early, with the Sewer's [factory](../../scenes/levels/encounters/sewer_encounters.tres) (sewer rats, acid blobs and snakes; no aquator, tentacles, nest or gatekeeper yet): the [prison factory](../../scenes/levels/encounters/prison_encounters.tres), chained from the Garrison, with the Orc Warrior as the grunt, the giant bat and the nymph with theft. The spearman, shaman, captain and troll remain, so its last floor has no gatekeeper (`RealmEncounterFactory.HasGatekeeper`).
   - Verify: realm encounter tests over the prison band (packs inside it, gatekeeper seals the descent and lifts only on the final death), harness scenarios for each orc and the troll's revive.
   - Limitation: Prison floor 2 has no nest.
2. **Flight.** The giant bat and its roost nest. This is the riskiest engine work, so it comes early and unblocks the Cathedral.
   - Landed: the air grid and the bat's flight, tested on a generated Sewer floor (it crosses a pool walkers must go around, and loot from over water lands on dry floor) and in the harness (it never holds still, and swords and bolts hit its hovering body). The roost nest remains.
   - Verify: plain-C# tests that the air grid crosses water and chasms and keeps walls and closed doors; a harness scenario where a bat crosses a pool to reach the hero; melee and projectiles hit a hovering bat.
3. **Sewer.** Arcing lob and poison pools (spitting slime), contact corrosion (acid blob), armor rust (aquator), the snake, ambush and grasp (sewer tentacles), goo bind and the gelatinous cube, and the slime vat.
   - Verify: a lob lands behind cover; standing in a pool poisons, leaving it stops it; goo binds for its duration and poisons; striking an acid blob corrodes, shooting it does not; a grasped hero can attack but not move, and breaking the tentacles' poise frees them.
4. **Mines and thieves.** Gnome family, dwarf, soldier ant, piercer, living roots (the grasper's second look), rattlesnake, leprechaun (theft and escape), umber hulk (burrow and gaze), and the ant hill. Theft also brings the Prison's nymph, and the chest mimic joins every realm from the Prison on.
   - Verify: a piercer stays hidden until triggered and its drop is telegraphed; burrowing never crosses walls; facing away avoids the gaze; a thief that escapes keeps its loot and one killed first drops it; a mimic keeps its chest's loot and stays cleared after a reload.
5. **Cathedral.** Gargoyle (ambush and flight), wraith (flight and drain), ghost (door phasing), vampire bat, phantom (permanent invisibility), medusa (petrifying gaze), lich (summon and blink), and the reliquary.
   - Verify: a ghost passes a closed door a walker cannot; drain heals by its share; the phantom never becomes visible; a hero facing medusa inside her cone when the gaze lands is petrified and takes no damage from it, while one turned around or behind cover is untouched; her snakes poison; summons are capped, unrewarded and collapse with the lich.

Every slice: `dotnet build "Rogue Gauntlet.sln"`, targeted GdUnit4 suites while iterating and the full suite at the end, `simulate.py audit` clean for the new monsters, `inspect_resource.gd` on new resources, and a windowed look at each monster from the game's camera. Tests check observable outcomes, never values restated from a `.tres`.

## Open decisions

1. **Gatekeeper choices.** Troll, gelatinous cube, umber hulk and lich are proposed. Alternatives: a human executioner for the Prison, the black pudding (splits when struck) for the Sewer, medusa for the Cathedral.
2. **Hold ownership.** Frozen, goo-bound and petrified all stop the hero the same way. Reuse `FrozenStatusComponent` as is with one instance per condition, or rename it to a generic held-status component now that three conditions use it.
3. **Drain flavor.** Rogue's wraith drains experience. Draining health is proposed instead, because experience pacing is derived by the balance model.
4. **Medusa's cone.** 120° is proposed; 160° or 180° leaves less room to dodge sideways and makes turning around the only answer. Also open: whether a petrified hero takes reduced damage, as the stone look suggests, or full damage from her snakes.
5. **Flyer hover height.** It must clear low props yet stay inside every melee arc. Confirm against the hero's attack shapes in slice 2.
6. **Balance model gaps.** Lobs ignoring cover, ground pools, ambushes, grasps, thefts, stationary members, revives and flight evasion are not modeled. Start each monster on its nearest archetype and calibrate with harness traces and playtests before adding archetypes.
7. **Thief targets.** The nymph is proposed to steal only unequipped pack items; stealing worn gear would hit much harder.
8. **Grasper discovery.** Whether the trap discovery roll can reveal hidden graspers and mimics early, as it does traps.
9. **Encounter resource names.** The Garrison's factory is `sunken_garrison.tres`. The new ones use `<realm>_encounters.tres`; renaming the Garrison's to match is optional.
