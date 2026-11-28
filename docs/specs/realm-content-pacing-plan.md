# Realm content pacing

Status: partly implemented. Room and plan windows, headlines, shops in each later realm and floor motifs landed: [realm content](../realm-content.md) describes each realm's arc and [level design](../level-design.md#current-generation) the mechanism. What remains is below.

## Encounters follow motifs (proposed)

A pack entry (`MobSpawnEntry`) gains `Motifs`, and the factory learns the floor's motifs (`RealmFloor`) when it draws packs, so skeleton warbands weight up on `undead` floors. A room may also name the packs that prefer to spawn in it, so skeletons rise in the graveyard and bats hang in caverns. This touches every encounter resource and the spawn placement, so it waits until the paced floors have played well. It bumps `SaveGame.CurrentVersion`.

## Open decisions

1. **The Cathedral's gatekeeper.** The [roster plan](realm-monster-roster-plan.md) proposes the lich, which summons the Garrison's skeletons. A vampire lord fits the Cathedral as the vampire's seat better and leaves the undead to the Garrison. Choosing it means a new monster concept in place of the lich, and an update to the roster plan.
2. **Merchants.** Every realm stands the quartermaster until realm merchants exist. A cheap step between the two would be a reskinned keeper per realm, such as an orc fence in the Prison or a gnome trader in the Mines.
3. **Bridging rooms.** Last floors preview the next realm's rooms where the themes allow it (prisons on the Garrison's floors 4–5). The sewer and the mines have their own themes, so the Prison cannot preview drains. A shared transition room per theme pair is possible, but it is not planned here.
