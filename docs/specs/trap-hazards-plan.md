# Trap hazards and gauntlets: remaining work

Status: partly implemented. Wall spouts, pendulum blades, gauntlet beats, the three gauntlet rooms, their placement before a few descents per run, their art and their balance landed: [combat](../combat-system.md#trap-hazards) describes the hazards, [level design](../level-design.md#trap-gauntlets) the rooms and their placement, and the [balance model](../balance-model.md) their damage. What remains is below.

## Design rules to keep

- **Read, then execute.** Hazards are visible once their tiles are explored and move on a fixed beat; the challenge is timing, not discovery. The hidden spike plate stays the hidden threat.
- **Patience crosses; a dodge buys back a mistake.** Every row leaves a safe window each period and the hero may stop between rows, so crossing never depends on walking speed. A gauntlet never demands more than two dodges inside one dodge recharge.
- **Introduce, then combine.** Each realm teaches one hazard alone before mixing it with others or with monsters.
- **Landmarks.** Hazards are rare set pieces, never floor clutter.

## Corridor gauntlets

A `CorridorHazardStep` lays the same rows on straight corridor spans (`CorridorBlueprint` spans of at least four tiles, away from thresholds, junctions and doors): slits along one wall firing across, or pendulums swinging across the corridor. The hallway and prison plans lay their halls as corridor tiles, so their long halls are natural gauntlet hallways. It needs:

- `CorridorBlueprint` to carry beats and emit gauntlets as rooms do.
- Recorded danger tiles (from each hazard's `DangerBounds`, measured once physics knows the walls), since a corridor gauntlet has no room to mark as occupied, so spawns, loot and traps skip them.
- Spout walls that survive `MapGenerator.RemoveWallPropsOffMounts`, or a whole row dropped with its spout, so no row goes missing.
- A swing fitted to the corridor: a 4 m corridor cannot keep the middle safe at both blade extremes, so the blade narrows its swing to the measured walls and the hero passes on the side the blade is not. A one-tile passage of single blades was dropped from the rooms after playtests left about 0.2 s to pass each blade.

Bump `SaveGame.CurrentVersion`, since generated floors change.

## Realm pacing (proposal)

Today the gauntlet profile deals any gauntlet room that suits a floor's theme. A realm schedule would introduce hazards in order:

| Realm | Introduces | Corridors | Rooms |
| --- | --- | --- | --- |
| Garrison 1–5 | slits, then gargoyles | slit spans from floor 2 | arrow gallery from floor 3; gargoyle crypt on `undead` floors |
| Prison 6–10 | pendulums | slit and pendulum spans | pendulum hall, the execution corridor |
| Sewer 11–15 | acid pipes later, as a spout projectile | — | — |
| Mines 16–20 | spike strips (optional) | spike spans | — |
| Cathedral 21–25 | combinations | gargoyle spans | gargoyle crypt, pendulum hall, the cathedral nave |

The cathedral nave (pendulums down the aisle with gargoyles firing across) interleaves a `PendulumRowsStep` and a `CrossFireStep`, which needs a row offset on the step.

## Optional extensions

- **Spike strips:** a visible row of spike plates that rise, hold and retract after a short rattle; hazard contact while up, with no dodge immunity, so only timing avoids them.
- **Vault gauntlets:** a guaranteed chest behind a gauntlet needs rooms to own a reward anchor that the loot pass fills; no room type places loot today.
- **Burning fire spouts:** burning is a spell-owned contact, so fire spouts deal fire damage only; a burn needs a trap-owned burning contact.
- **Acid pipes** for the Sewer, as a spout projectile.

## Open decisions

- **Should hazards hurt monsters?** No for now, matching floor traps: chasing monsters would need hazard-aware flow fields. Yes would make luring monsters into gauntlets a tactic.
- **Monsters inside gauntlets:** none at first; combining patrols with hazards is a later tuning lever.
- **Dart visibility:** at game zoom the dart is hard to see in flight, so its lane warning carries the read; a bigger silhouette or a trail would help.
