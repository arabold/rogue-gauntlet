# Realm monster tuning

Status: landed. `SpeedScale` and `FacesTravel` exist, and all five monsters are tuned as below; the open decisions took their first options. It covers the five monsters built from concept art so far: the giant bat, nymph and chest mimic of the Prison, and the acid blob and snake of the Sewer. Their roles and future capabilities are in the [realm monster roster](realm-monster-roster-plan.md), archetype stats in the [balance model](balance-model.md), and composition in [monster design and authoring](../monster-authoring-design.md). This plan settles how each one moves and fights with the systems that exist, and the two small seams that needs. The roster's capabilities (flight, theft, ambush, contact corrosion) live in the roster plan; all four have since landed, with the nymph's blinks and the snake's coiled ambush.

## Problems

- **Backpedaling bodies.** Every movement goal except an approach keeps a monster facing its target (`EnemyContext.ExecuteTactics`), so a retreating or circling monster moves backward or sideways. That suits humanoids and a fluttering bat, but not a snake or a hopping chest.
- **One pace per archetype.** `derive` writes `MoveSpeed` from the archetype alone: fodder 5.0 m/s, swarmers 6.5, brutes 4.0, against the hero's 6.0. The blob glides at 5.0 although the roster calls it slow. The snake slithers at a rat's 6.5 although its threat is the lunge.
- **Generic tactics.** The mimic roams and waits in the engagement ring like a crowd fighter, the nymph brawls with three martial attacks, and the snake wanders and backpedals instead of lying in wait.

## Pace against the hero

The hero runs at 6.0 m/s. Footwork avoids a melee monster fully at 40% of that (2.4 m/s) and not at all at the hero's speed or faster ([balance model](balance-model.md), `ENGAGEMENT["footwork"]`).

| Monster | Realm and depths | Archetype | Now | Proposed | Why |
| --- | --- | --- | --- | --- | --- |
| Acid blob | Sewer, 11–15 | Fodder | 5.0 m/s | 2.2 m/s (scale 0.45) | A slow wall: the hero kites it or burns it, and its threat comes from numbers and, later, corrosion |
| Snake | Sewer, 12–15 | Swarmer | 6.5 m/s | 4.5 m/s (0.7) | Slithers slower than the hero; the 3 m lunge (about 8.6 m/s) is the threat, and a fleeing snake can be caught |
| Giant bat | Prison, 6–10 | Swarmer | 6.5 m/s | 6.5 m/s (1.0) | Cannot be outrun but is frail |
| Nymph | Prison, 6–10 | Fodder | 5.0 m/s | 5.5 m/s (1.1) | A quick thief that a committed hero can still run down; her blink gives her the escape |
| Chest mimic | Prison on | Brute | 4.0 m/s | 3.0 m/s (0.75) | Ponderous hops; dangerous only up close |

Each walk clip's `NaturalLocomotionSpeed` follows its new pace, so feet, hops and the slither wave do not slide.

## Each monster

### Acid blob

Sewer fodder in packs of two to four, released by the slime vat nest. It teaches the hero that not everything should be fought in melee.

- **Movement:** `close_distance` replaces the engagement ring, since blobs ooze straight in instead of taking turns. It glides in any direction, so it keeps facing the hero.
- **Attack:** the slam stays its only attack, with fodder's 0.35 s wind-up and 1.6 m reach.
- **Corrosion (landed):** every melee hit that damages it burns its attacker with acid (`ContactRetaliationComponent`, `acid_burn.tres`); ranged hits are safe.

### Snake

A Sewer swarmer, alone or in pairs at water and drains, among rats. It teaches the hero to watch the floor and to step out of a lunge.

- **Lies in wait:** `RoamRadius` 0 keeps it at its spawn, and `DetectionRange` 7 makes it notice the hero late.
- **Movement:** it faces its travel (`FacesTravel`, below) and turns at `MaxTurnRate` 270°/s, so it never slides backward. It slithers at 4.5 m/s.
- **Attack pattern:** the lunge (a 3 m dash after a 0.25 s coil) opens every exchange, with a higher weight. The bite follows only while the hero stays beside it. After a bite it slithers away head first (`bite_and_dart`, 3.5 m), coils in its idle, and lunges again. Venom stays at 60%.
- **Coiled ambush (landed):** it lies in a tight coil, out of the hero's targeting and without a nameplate, until the hero comes within 3.5 m in its sight or hits it; then it uncoils and lunges (`AmbushComponent` with `DormantClip` `Coiled`).

### Giant bat

A Prison swarmer in swarms of three to five from the bat roost. It teaches the hero to keep moving against many fast enemies.

- **Movement:** speed unchanged. Its erratic flight darts around the hero over `bite_and_dart`'s in and out, facing the hero throughout, which suits a flyer.
- **Attack pattern:** the swoop's weight rises so it swoops more than it nibbles. The bite stays for a hero who stands still.
- **Flight (landed):** it flies the air grid over water, pits and low solids (`ErraticFlightStrategy`); see the [roster plan](realm-monster-roster-plan.md#flight).

### Nymph

Prison fodder that comes alone or beside an orc pack. It teaches the hero to guard the pack and to chase down thieves.

- **Movement:** she circles behind the hero (`flank_rear`, like the plague rat) and darts off after each touch (`bite_and_dart`) at 5.5 m/s. She may backpedal while facing the hero.
- **Attack pattern:** the jab is her quick touch, and the kick shoves the hero back before she darts away. The uppercut goes. Damage stays light for fodder, since her danger is theft.
- **Theft (landed):** her first touch that damages the hero steals a loose item stack, and she runs off facing her travel (`TheftComponent`); see [monster authoring](../monster-authoring-design.md). From a second after the theft she blinks 8–11 m away from a hero within 5 m, at most every five seconds (`MonsterBlinkAction`).

### Chest mimic

A brute that takes a placed chest's place from the Prison on (`MimicChance`). It teaches the hero that not every chest is loot.

- **Stays put:** `RoamRadius` 0 and `StayInHomeRoom`, so it never wanders before it wakes or leaves its room afterward.
- **Movement:** it hops at 3.0 m/s, faces its travel, and turns at `MaxTurnRate` 180°/s, so the hero can circle behind it.
- **Attack pattern:** `close_distance` replaces the ring. The tongue lash (2.9 m) catches a hero who backs off, and the chomp, a heavy blow with a long tell, punishes one who stays close.
- **Ambush (landed):** it waits as a real chest and wakes when the hero opens it, lingers beside it or hits it (`AmbushComponent`); from the Prison on, each placed chest may be one (`MimicChance`).

## Seams

### Speed scale

`MonsterDefinition.SpeedScale` (proposed, `[Export] float`, default 1) is the monster's share of its archetype's pace. `derive` writes `MoveSpeed` as the archetype speed times `SpeedScale` and counts that speed in footwork, threat and XP. A slow blob therefore costs less, and floors field more of them. `audit` reports a `MoveSpeed` that disagrees with it. The scale is authored data on the definition, like `BalanceDepth`, so the model never names individual monsters.

### Facing travel

`EnemyBehaviorProfile.FacesTravel` (proposed, `[Export] bool`, default false) turns the body toward its travel while it repositions, instead of keeping the target in front.

```text
EnemyContext.ExecuteTactics
├─ decision = TacticalSelector.Select(...)                    // unchanged
├─ approach → FollowTargetPath                                // faces its path, as now
├─ retreat → FollowRetreat; encircle or flank → FollowRing     // unchanged
└─ facing
   ├─ hold, or any goal without FacesTravel → Movement.FaceDirection(target)   // as now
   └─ FacesTravel while retreating or circling → no call: MovementComponent turns toward its motion under MaxTurnRate
```

Invariant: attacks and dashes still aim through their own facing (the attack's wind-up turn and facing lock, the dash's `Aim`), so a monster that faces its travel still turns back to strike.

## Affected files

```text
~ scenes/enemies/MonsterDefinition.cs            SpeedScale
~ scenes/enemies/EnemyBehaviorProfile.cs         FacesTravel
~ scenes/enemies/ai/EnemyContext.cs              facing rule
~ scripts/balance/content.py                     MoveSpeed and threat from SpeedScale; audit
~ docs/specs/balance-model.md                    speed scale sentence
~ docs/monster-authoring-design.md               FacesTravel and SpeedScale
~ scenes/enemies/slime/acid_blob*.tres|.tscn     pace, close_distance, walk clip speed
~ scenes/enemies/snake/snake*.tres|.tscn         pace, FacesTravel, turn rate, roam, detection, lunge weight
~ scenes/enemies/bat/giant_bat_combat.tres       swoop weight
~ scenes/enemies/nymph/nymph*.tres|.tscn         pace, flank and dart, jab and kick
~ scenes/enemies/mimic/chest_mimic*.tres|.tscn   pace, FacesTravel, turn rate, roam, home room, close_distance
+ tests/MonsterFacingTest.cs                     a retreating FacesTravel monster faces away from its target; a default one faces it
```

## Slices

1. **Seams:** `SpeedScale` with `derive` and `audit`, and `FacesTravel` with its test. Verify with the build, `simulate.py derive` and `audit`, and the new test with `MonsterAuthoringTest`.
2. **Sewer:** blob and snake tuning, re-derived. Verify with harness recordings of the hero kiting blobs and of the snake lunging, biting, slithering off and lunging again.
3. **Prison:** bat, nymph and mimic tuning, re-derived. Verify with harness recordings of a bat swarm, the nymph's flank and dart, and the mimic's lash and chomp.

The giant bat and nymph have since joined the Prison's [encounter pool](../../scenes/levels/encounters/prison_encounters.tres) and the acid blob and snake the [Sewer's](../../scenes/levels/encounters/sewer_encounters.tres), each sized for its realm's middle floor, and mimics hide among both realms' chests.

## Open decisions

1. **Blob pace:** 0.45 (fully kitable, cheap, many per floor) or about 0.6 (some pressure on a kiting hero).
2. **Snake after a bite:** slither off and coil again, as the roster's "then retreats" reads, or hold and keep biting.
3. **Nymph before theft exists:** tune her as a skirmisher now, or leave her until theft lands.
4. **Mimic leash:** its whole room (`StayInHomeRoom`), or a short leash around its chest spot, which needs a new leash radius.
