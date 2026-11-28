# Realm monster roster

Status: partly implemented. The Prison's cast (the orc warrior, spearman, shaman and captain, the giant bat and its roost, the nymph and the Minotaur), the Sewer's sewer rat, acid blob, spitting slime, snake, slime vat and Gelatinous Cube, the Mines' stone golem and the chest mimic have landed, with the capabilities they needed: flight, lobs and ground patches, goo bind, contact corrosion, theft and escape with blinks, ambush, charge and revive. The [monster catalog](../monster-catalog/README.md) lists them, [monster design and authoring](../monster-authoring-design.md) describes their capabilities, [level design](../level-design.md#encounters) their encounters, and the [balance model](../balance-model.md) their stats and threat. This plan keeps the remaining casts, capabilities and decisions. It authorizes design, not implementation.

## Goals

- Each realm gets a cast of classic roguelike monsters (Rogue, NetHack) that reads at a glance in the KayKit style: chunky silhouettes, big heads, simple shapes. No complex anatomies (dragons, centaurs, griffins).
- Some realms field one **tiered humanoid family**: one base body whose tiers differ in gear, size, color and one capability, like the Garrison's skeletons and the Prison's orcs. Gnomes hold the Mines.
- Monsters vary in **how they move and attack**. Each realm introduces at least one new way to move, so every realm teaches the hero something.
- Every realm ends on a gatekeeper with a signature mechanic.
- Some monsters break the rules the way Rogue's best-remembered ones did: thieves that steal and run, an invisible stalker, a gaze that turns the hero to stone, and growths that grab from the floor.

Non-goals: depth-scaled stats (every monster keeps the fixed strength the balance model derives for it), realms beyond the fifth, and reusing the existing Asset Studio monster catalog. Concept art for the aquator, both graspers, the gnome, the soldier ant and the piercer is in `asset-studio/characters/`.

## Remaining roster

Each table gives the classic source, the [balance archetype](../../scenes/enemies/MonsterArchetype.cs) the monster is derived as, and how it moves and fights. **Bold** marks a capability from [remaining capabilities](#remaining-capabilities); everything else composes existing actions, strategies and components.

### Prison, depths 6–10

The cast is complete. The balance model's fodder pair has no Prison pack: the nymph is the realm's only fodder, and she is capped at one a floor.

### Sewer, depths 11–15

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Aquator | Rogue `A` | Standard | Shambles in; its touch **rusts armor**, lowering the hero's armor for a while |
| Sewer tentacles | Grasper family | Standard | Lie hidden under a drain or puddle like a trap; **grasp** the hero who steps close and squeeze with poison until the hero fights free |

### Mines, depths 16–20: the Gnomish Mines

Until these land, giant bats and snakes stand in for the cave dwellers and the bat roost is the realm's nest; both are authored for other realms' depths, so `audit` reports them. The Mines have no gatekeeper yet (`HasGatekeeper` off).

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Gnome | NetHack gnome | Fodder | Small; comes in mobs and flees when hurt |
| Gnome lord | NetHack gnome lord | Shooter | Carries a small crossbow |
| Gnome king | NetHack gnome king | Caster | Leads a mob and empowers gnomes near it |
| Soldier ant | NetHack soldier ant | Swarmer | Very fast zigzag charge, in pairs |
| Piercer | NetHack piercer | Standard | **Ambush**: clings to the ceiling and drops on whoever walks beneath, then crawls slowly |
| Living roots | Grasper family | Standard | Burst from the earth like a trap; **grasp** and crush the hero until the hero fights free; tougher and slower to break than tentacles |
| Rattlesnake | Rogue `R` | Swarmer | The snake's dry-tunnel cousin: a rattle warns, then a lunge whose venom also **saps strength** for a while |
| Leprechaun | Rogue `L` | Fodder | Darts in, **steals gold** and escapes with short blinks; killing it before it gets away drops the gold |
| **Umber hulk** (gatekeeper) | NetHack umber hulk | Boss | **Burrows** under the floor and surfaces beside the hero; a **gaze** that confuses a hero who faces it |

Nest: an ant hill that releases soldier ants.

### Cathedral, depths 21–25: the unholy

The Cathedral has no encounter resource yet: its floors fall back to the development factory.

| Monster | Classic source | Archetype | Movement and attack |
| --- | --- | --- | --- |
| Gargoyle | NetHack gargoyle | Brute | **Ambush** as a statue until the hero passes, then flight; stone skin |
| Wraith | Rogue `W` | Standard | Floats (flight) over chasms and low obstacles; a **draining** touch heals it |
| Ghost | NetHack ghost | Fodder | **Drifts through closed doors**; slow but hard to shake |
| Vampire bat | NetHack vampire bat | Swarmer | Flight; a **draining** bite |
| Phantom | Rogue `P` | Standard | Always **invisible**: given away only by a shimmer, its footsteps and its hits |
| Medusa | Rogue `M`, NetHack medusa | Shooter | An elite. After a clear tell her **gaze** sweeps a wide cone; a hero still facing her when it lands is **turned to stone** for a moment, so the hero must turn around. The gaze does no damage; she hurts with thrown snakes, straight venomous projectiles like crossbow bolts, which find a petrified hero defenseless. Snake hair as chunky KayKit coils |
| **Lich** (gatekeeper) | NetHack lich | Boss | Teleports in short blinks, casts, and **summons** the Garrison's skeletons |

Nest: a haunted reliquary that releases ghosts.

## Remaining capabilities

Each capability is a reusable component, action or strategy, authored on the monsters that use it; shared AI never names a monster. Proposed names are marked.

| Capability | Users | Builds on |
| --- | --- | --- |
| Grasp | Sewer tentacles, living roots | Ambush, poise, poison |
| Petrify | Medusa | The player's held-status components (as goo bind), `StatusCondition`, the Stone Skin look |
| Armor rust and strength sap | Aquator, rattlesnake | Buff modifiers on armor and attributes |
| Gold theft | Leprechaun | `TheftComponent`, gold, `MonsterBlinkAction` |
| Ambush start actions | Piercer, gargoyle | `AmbushComponent`, attack indicator |
| Burrow | Umber hulk | Ground navigation, `InvisibilityComponent` |
| Gaze | Umber hulk, medusa | `MonsterTimedAction`, attack indicator, confusion status, petrify |
| Permanent invisibility | Phantom | `InvisibilityComponent` |
| Door phasing | Ghost | Clearance grid gates |
| Drain | Wraith, vampire bat | Attack payloads, healing |
| Summon | Lich | `MonsterAction`, nest summon rules |

Flight, ambush and blink reuse the landed components: the gargoyle, wraith and vampire bat fly on the air grid, the graspers and gargoyle wait in an `AmbushComponent`, and the lich blinks with a `Spread` of 180 to land around the target rather than away from it.

### Grasp

One monster type with two looks: **sewer tentacles** (slimy, poison) and **living roots** (bark, crushing, tougher). Like a trap, a grasper waits hidden in the floor; unlike a trap, the hero has to fight it.

- **Family:** `scenes/enemies/grasper/` holds one scene setup and two variants, `grasper_tentacles` and `grasper_roots`, that differ in model, health, poise, squeeze damage and status (poison for tentacles, none for roots).
- **Hidden:** an `AmbushComponent` keeps it under the floor in a dormant clip, marked only by a disturbed patch (a bubbling drain, cracked earth); it emerges as its wake clip. The Intelligence-based trap discovery may reveal it early (open decision 5).
- **Grab (proposed):** `MonsterGraspAction : MonsterAction` erupts under the hero and applies a `grasped` `StatusCondition` (proposed). A grasped hero cannot move or dodge but can attack, guard and use items. The squeeze deals damage over time.
- **Breaking free:** the grip ends when the grasper's poise breaks (blunt weapons break it faster), when it dies, or after `MaximumHold`. After a release it sinks back and can grab again after its cooldown. It never moves, so it needs no navigation.
- **Packs:** graspers join packs as stationary members at ordinary spawn points, so the monsters around a held hero get free hits. That pressure is the point.

### Petrify, armor rust and strength sap

- **Petrify:** medusa's gaze applies a `petrified` condition (proposed), held like goo bind by its own held-status component on the player and shown with the existing Stone Skin surface effect. It deals no damage; the danger is her thrown snakes while the hero stands helpless.
- **Armor rust and strength sap:** the aquator's attack payload applies a timed buff that lowers armor, and the rattlesnake's venom one that lowers Strength, both through the existing modifier system. No new component.

### Gold theft

The leprechaun steals gold instead of an item stack and escapes with blinks, as the nymph does. What an escaped thief took is gone; nothing tracks it across floors.

### Ambush, burrow, gaze and invisibility

- **Ambush start actions:** a configured start action for the piercer's drop (a timed attack whose indicator marks the floor below) and the gargoyle's waking roar before it flies; the gargoyle uses a cathedral statue prop as its disguise.
- **Burrow (proposed):** `MonsterBurrowAction : MonsterAction`. The actor submerges (untargetable and hidden, with a moving dirt trail), travels the ground grid to a point beside the target and surfaces with a knockback burst. It moves under floors, never through walls, so it needs no digging rules.
- **Gaze (proposed):** `MonsterGazeAction : MonsterTimedAction`, a telegraphed attack that deals no damage and applies an authored status instead: petrify for medusa, confusion for the umber hulk.
  - **Cone:** `ConeAngle` (120° default; see open decision 3) and `Range`, in front of the actor. `AttackIndicatorComponent` draws the cone through the whole wind-up.
  - **One check at contact:** a target inside the cone, in the actor's line of sight, whose facing (`MovementComponent.LookAtDirection`) points toward the actor (within `FacingTolerance`, 90° by default) gets the status. A hero who has turned around, left the cone or ducked behind a pillar is safe.
  - **Cost of turning away:** while turned around the hero cannot aim at her, so each gaze is a beat where attacking stops and the hero has to read the tell and turn.
- **Medusa's damage:** her second action is an ordinary monster projectile attack whose projectile is a thrown snake carrying the existing venom status. Her action weights favor it right after a gaze lands, so a petrified hero is punished.
- **Permanent invisibility:** the phantom starts invisible through `InvisibilityComponent` and never drops it. A faint shimmer, footstep sounds and its own hits give it away, so it stays fair.

### Door phasing, drain and summon

- **Door phasing (proposed):** `NavigationProfile.IgnoresDoors` makes the actor plan with every gate open, and its collision mask leaves out door bodies, so a ghost drifts through closed doors.
- **Drain:** an attack payload share that heals the attacker by part of the damage it deals.
- **Summon (proposed):** `MonsterSummonAction : MonsterAction` spawns from an authored scene list near the caster, capped by its living summons. Summons pay no rewards, are never persisted and collapse when their summoner dies.

## Encounters

- **Mines:** its gatekeeper scene, fit and name on `mines_encounters.tres` (turning `HasGatekeeper` on), the ant hill as its nest, and packs for the gnome family, ants, piercers, roots, rattlesnakes and the leprechaun.
- **Cathedral:** `cathedral_encounters.tres` in `scenes/levels/encounters/`, chained as the Mines' `Fallback` in place of `dungeon_mob_factory`, with its gatekeeper, the reliquary nest and a `MimicChance`. Every realm already names `boss.tres` as its `BossProfile`, so gatekeeper floors take the linear boss plan as soon as their factory plans a gatekeeper.
- **Packs:** each new realm pack needs a matching archetype pack in `scripts/balance/params.py` within the realm's depths. `simulate.py derive` writes stats and threat; `audit` checks the authored entries against the model.

## Art

- Built with the [Asset Studio](../../asset-studio/README.md) monster pipeline in the KayKit style. Humanoids use KayKit rigs: medium for gnomes, large for the umber hulk. They share the rig's clips where possible, and missing clips (cast, throw, hover) are authored per rig.
- Families share one look per rig: each tier's concept is drawn over the family's approved first tier, so tiers keep its face, proportions and finish and change gear, size, palette and one silhouette mark. Concepts stay empty-handed: tiers hold the game's own weapons on their hand slots, so a tier can swap weapons in its scene.
- The grasper's tentacles and roots share one simple chain rig and clip set (erupt, grab, squeeze, sink); only the mesh and materials differ.
- The piercer animates by transform and shader (squash and stretch, wobble) with no skeleton, like the blobs.
- Each monster lives in its own folder: `scenes/enemies/<family>/<family>_<tier>.tscn` and `<family>_<tier>_definition.tres`.

## Slices

Each slice lands playable: its realm's floors spawn the new roster, and the harness can stage each monster. Every slice: `dotnet build "Rogue Gauntlet.sln"`, targeted GdUnit4 suites while iterating and the full suite at the end, `simulate.py audit` clean for the new monsters, `inspect_resource.gd` on new resources, and a windowed look at each monster from the game's camera. Tests check observable outcomes, never values restated from a `.tres`.

1. **Sewer remainder.** Armor rust (aquator), grasp and its ambush under the floor (sewer tentacles).
   - Verify: the aquator's touch lowers armor for its duration; a grasped hero can attack but not move, and breaking the tentacles' poise frees them.
2. **Mines.** Gnome family, soldier ant, piercer, living roots (the grasper's second look), rattlesnake, leprechaun (gold theft), umber hulk (burrow and gaze), and the ant hill.
   - Verify: a piercer stays hidden until triggered and its drop is telegraphed; burrowing never crosses walls; facing away avoids the gaze; a leprechaun that escapes keeps the gold and one killed first drops it.
3. **Cathedral.** Gargoyle (ambush and flight), wraith (flight and drain), ghost (door phasing), vampire bat, phantom (permanent invisibility), medusa (petrifying gaze), lich (summon and blink), and the reliquary.
   - Verify: a ghost passes a closed door a walker cannot; drain heals by its share; the phantom never becomes visible; a hero facing medusa inside her cone when the gaze lands is petrified and takes no damage from it, while one turned around or behind cover is untouched; her snakes poison; summons are capped, unrewarded and collapse with the lich.

## Open decisions

1. **Gatekeeper choices.** The umber hulk and the lich are proposed. Alternatives: the troll for the Mines, whose approved concept (`asset-studio/characters/troll/`) is parked and which would spare the umber hulk's two new capabilities (burrow and gaze); a vampire lord for the Cathedral, which [realm content](../realm-content.md) stages as a vampire's seat (see the [realm content pacing plan](realm-content-pacing-plan.md)), or medusa.
2. **Hold ownership.** Frozen and goo-bound each hold the hero through their own `FrozenStatusComponent` instance on the player; petrified and grasped would add two more. Keep one instance per condition, or rename the component to a generic held status once more conditions use it.
3. **Medusa's cone.** 120° is proposed; 160° or 180° leaves less room to dodge sideways and makes turning around the only answer. Also open: whether a petrified hero takes reduced damage, as the stone look suggests, or full damage from her snakes.
4. **Drain flavor.** Rogue's wraith drains experience. Draining health is proposed instead, because experience pacing is derived by the balance model.
5. **Grasper discovery.** Whether the trap discovery roll can reveal hidden graspers and mimics early, as it does traps.
