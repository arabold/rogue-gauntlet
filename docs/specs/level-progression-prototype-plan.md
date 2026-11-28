# Dungeon progression: remaining implementation

Status: partly implemented. Realms, room schedules with headlines and motifs, floor plans, room groups with must-traverse members (the boss and gauntlet approaches), generated locks and room-owned secrets are delivered ([level design](../level-design.md), [door access](../door-access.md)). Two pieces of [dungeon design](../dungeon-exploration-design.md) remain: guaranteed locations per floor, and a visible encounter seal on the descent.

## Required locations per floor

Today a floor's headline is placed first but dropped when it does not fit, so no location is guaranteed. A floor recipe should name required roles (a cell block, the Warden's hall), reserve them before optional rooms, and ask for intentional loops, while the existing must-traverse check keeps checkpoints unavoidable in the realized floor.

- Build on the current pieces rather than beside them: realm schedules and headlines (`RealmRoom`), floor plans (`FloorProfile`) and room groups (`RoomGroup`, `MustTraverse`). Plans and realized placements stay per-generation data; shared Resources stay immutable.
- Required roles may use existing rooms as placeholders. Optional content may be omitted; a required room, objective or access solution never is. A floor that cannot meet its recipe retries a bounded number of times and then fails safely, without loading a broken floor or overwriting a save.
- Hidden destinations must not gain visible alternate entrances, and secrets must not bypass a required checkpoint.
- Acceptance: seeded sweeps check realized geometry against each recipe's guarantees, and generated floors are walked in the editor. Bump `SaveGame.CurrentVersion` when generation changes.

## Visible encounter seal

The gatekeeper's defeat already gates the descent: `RealmEncounterController` keeps it sealed until the gatekeeper dies, a label over the stairs states the objective, and both the stairs' `LevelTransitionTrigger` and `GameSession` travel check `Level.CanDescend`. What remains is a readable barrier on the down-stairs, bound to the same cleared-gatekeeper state, that blocks physical passage and lifts on the kill.

- Reuse the access and interaction boundary of [door access](../door-access.md) instead of a competing door list or timer; no global registry. Ordinary picks and keys never bypass it.
- Acceptance: the seal holds and lifts correctly through death, revisits, reloads and every travel path.
