# Realm content

Each realm stages its rooms, floor plans and set dressing across its five floors instead of offering all of them on every floor: content is introduced, peaks and retires, and walking down a realm reads as a progression. Every floor has one **headline**, a special room, the realm's shop or its gatekeeper, and runs rotate which special room lands where. Themed floors (an undead floor, a crystal floor) bring their rooms and props together. Realms that share a look still read differently: the Garrison is a keep sinking into its tombs, the Prison an orc warband's jail and the Cathedral an elegant, clerical house that a vampire has made its seat.

[Level design](level-design.md#current-generation) describes the mechanism: room and plan windows, headlines, motifs and the run-seeded draws. The realm resources (`scenes/levels/realms/*.tres`) are the source of truth for the schedules below, and the [room generation harness](../scenes/debug/README.md) previews any room with a realm's motifs. Shops are in [shops](shops.md); the monster casts are in the [realm monster roster](specs/realm-monster-roster-plan.md).

## Realm by realm

The legend for the tables below:

| Symbol | Meaning |
| --- | --- |
| ● | On every floor of the window |
| ◆ | Only on floors carrying the realm's motif |
| *plan* | Comes with a floor plan |
| — | Absent |

Monster rows restate the current encounter windows, so the rooms can be read against them. Headline candidates list their windows; each run places one per open floor.

### Garrison, depths 1–5: from the keep down into its tombs

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | rats, skeleton pairs and warriors | + patrols, sentries | + warbands | warbands | warbands, the Warden |
| *Headline* | room | room | room | room | the Warden |
| *Plans* | cluster, hallway | cluster, hallway | cluster, hallway | cluster, hallway | boss |
| *Motifs* | | | `undead` on 1–2 of 3–4, and always on 5 | `decay` 4–5 | |
| passage | ● | ● | ● | ● | ● |
| storage | ● | ● | ● | ● | — |
| barracks, kitchen | ● | ● | ● | — | — |
| mess hall, root cellar | ● | ● | — | — | — |
| fenced walkway, partitioned storage | ● | ● | ● | — | — |
| guardroom | — | ● | ● | ● | ● |
| crypt | — | — | ◆ | ◆ | ● |
| ruins | — | — | ● | ● | ● |
| prison | — | — | — | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| great hall | 1–2 |
| training yard | 1–2 |
| library | 2–3 |
| alchemist | 2–3 |
| chapel | 2–4 |
| graveyard (◆ `undead`, weighted high) | 3–4 |
| treasury | 4 |

The Garrison is the graveyard's only realm. Its upper floors are the living garrison. The tombs open from floor 3, where the skeleton warbands start: the graveyard needs `undead`, so it headlines one of those floors with the crypts and bone piles around it. The prison rooms on floors 4–5 foreshadow the next realm.

### Prison, depths 6–10: the orc warband's jail

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | bats, nymphs, orcs | + bat roosts | + orc warbands | warbands | the Minotaur |
| *Headline* | shop | room | room | room | the Minotaur |
| *Plans* | prison, hallway | prison, cluster, hallway | prison, cluster, hallway | prison, cluster | boss |
| *Motifs* | | | `warband` 3–5 | | |
| prison block, guardroom, storage | ● | ● | ● | ● | ● |
| cells | *plan* | *plan* | *plan* | *plan* | — |
| partitioned storage | ● | ● | ● | ● | — |
| kitchen | ● | ● | — | — | — |
| fenced walkway | — | ● | ● | ● | — |
| barracks, mess hall | — | — | ◆ | ◆ | ◆ |
| ruins | — | — | ● | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| warden's office | 2–3 |
| interrogation | 2–4 |
| execution yard | 3–4 |
| fighting pit (◆ `warband`) | 3–4 |
| treasury | 4 |

The Prison shares the garrison's walls but none of its great halls, libraries, chapels or tombs. The interrogation room becomes a headline, and the warband floors bring back barracks and the mess hall, with weapon racks and war maps.

### Sewer, depths 11–15: from the prison's drains into the deep cisterns

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | rats, blobs, snakes, spitting slime sentries | + slime vats, blob trios, snake pairs | | | |
| *Headline* | shop | room | room | room | the gelatinous cube |
| *Plans* | large cluster, cluster | large cluster, cluster | large cluster, cluster | large cluster | large cluster |
| *Motifs* | | | | `flooded` 4–5 | |
| drain tunnel, junction | ● | ● | ● | ● | ● |
| sewer vault | ● | ● | ● | ● | — |
| prison lock-up | ● | — | — | — | — |
| storage, partitioned storage | ● | ● | — | — | — |
| rat warren | — | ● | ● | ● | ● |
| ruins | — | — | ● | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| smugglers' den | 2–3 |
| cistern hall | 2–4 |
| outfall chamber | 3–5 |
| rat king's nest | 3–5 |
| sunken shrine | 4–5 |

### Mines, depths 16–20: a working mine giving way to crystal caves and ruins

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | golems, bats, snakes | + bat roosts | + golem pairs | | |
| *Headline* | shop | room | room | room | room, until the umber hulk |
| *Plans* | mines, large cluster | mines, large cluster | mines, large cluster | mines | mines |
| *Motifs* | | | `crystals` 3–5 | | |
| timbered mine | ● | ● | ● | ● | ● |
| ore chamber | ● | ● | ● | ● | — |
| foreman's hut | ● | ● | ● | — | — |
| cavern, root passage | — | ● | ● | ● | ● |
| ruins, ruined shrine | — | — | — | ● | ● |

Headline candidates:

| Room | Window |
| --- | --- |
| mine shaft | 2–3 |
| smelter | 2–3 |
| crystal cavern | 3–5 |
| temple hall | 4–5 |

The crystal cavern is a deep-mine sight from floor 3 on. `crystals` also switches the caverns' and ore chambers' crystal steps on, so floors 1–2 show ore and timber and floors 3–5 glow whether or not the crystal cavern headlines.

### Cathedral, depths 21–25: the vampire's seat

The Cathedral is elegant and clerical: candlelit chapels, carpets and gilt, a cloister above and the cathedral itself below. Its master is a vampire. The upper floors are still the clergy's. The lower ones are the vampire's: blood-red hangings, coffins and banquets. Nothing here is a ruin or a graveyard; decay belongs to the Garrison.

| | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| *Monsters* | planned: gargoyles, wraiths, ghosts, vampire bats, phantoms, medusa | | | | the vampire (proposed gatekeeper) |
| *Headline* | shop | room | room | room | room, until the gatekeeper |
| *Plans* | cloister, cluster | cloister, cathedral | cloister, cathedral | cathedral | cathedral |
| *Motifs* | | | | `vampire` 4–5 | |
| storage | ● | ● | — | — | — |
| courtyard, monk cell, scriptorium, mess hall, kitchen | *plan* | *plan* | *plan* | — | — |
| chapel | ● | ● | ● | ● | — |
| nave, side chapel | — | *plan* | *plan* | *plan* | *plan* |
| crypt | — | — | — | ◆ | ◆ |

Headline candidates:

| Room | Window |
| --- | --- |
| library | 2–3 |
| baptistry | 2–3 |
| bell tower | 2–4 |
| reliquary | 3–4 |
| bishop's throne room | 3–4 |
| banquet hall (◆ `vampire`) | 4–5 |
| the vampire's crypt (◆ `vampire`) | 4–5 |

### Motif-driven set dressing

Furnishing steps with `Motifs` let one room type change with the floor:

| Motif | Realm | Furnishing it switches on |
| --- | --- | --- |
| `undead` | Garrison | bone piles, open sarcophagi, grave pedestals and skull candles in crypts, guardrooms and ruins |
| `decay` | Garrison | rubble, webs and broken bookcases, tables and statues instead of whole ones |
| `warband` | Prison | weapon racks, war maps and training dummies in guardrooms and barracks |
| `flooded` | Sewer | pools in junctions and vaults |
| `crystals` | Mines | crystal clusters and veins in caverns and ore chambers |
| `vampire` | Cathedral | red hangings and carpets, coffins, goblets and candelabras in chapels, naves and crypts |

Wall and floor styles do not take motifs yet; mossy walls on deep floors or red carpets in the vampire's halls would use the same filter.
