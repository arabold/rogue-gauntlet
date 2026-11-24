# Props for the new floor plans

Status: proposed. The [floor generators plan](floor-generators-plan.md) describes the floor plans these props serve; [level design](../level-design.md) owns room assembly and furnishing.

The prison, cathedral, cloister and mine plans, and the rooms that line them, are built from props that already exist. This list covers what is still missing for each plan to read as itself at a glance. Each entry becomes a `Prop` under `scenes/levels/rooms/props/<category>/` (a mesh-library item, or a scene when it needs nodes such as a light), then joins the furnishing steps of the rooms named here. Prop scale is measured from the model, so nothing about size is hand-authored.

Style follows the existing kit: the KayKit texture and palette, matte non-metal surfaces, chunky readable silhouettes with exaggerated details, real cut geometry rather than overlay shapes, and wall props hanging at the shared wall face depth. A prop's front faces local +Z; back-to-wall props stand with −Z against the wall.

## Priority 1: mines

The mine plan has no props of its own yet, so its rooms read as empty caves. These come first.

| Prop | Mount | Rough size | Used by | Notes |
| --- | --- | --- | --- | --- |
| Mine cart | Floor | 1.2 × 2 m | Ore chamber, foreman's hut, tunnel dead ends | Empty and ore-filled variants; tipped-over variant for a collapsed gallery |
| Rail track | Floor inlay | One tile, 4 m | Ore chamber, tunnels | Straight, curve and end-bumper pieces; walkable, no collision |
| Ore vein | Wall | 1–2 m wide | Ore chamber, tunnel walls | Raw rock with glinting ore; two or three variants so walls do not repeat |
| Ore pile | Floor | 1–1.5 m | Ore chamber, cart sidings | Loose heap; doubles as debris |
| Tool rack | Wall | 1.5 m wide | Foreman's hut, ore chamber | Pickaxes and shovels; a loose pickaxe as a separate scatter prop |
| Mine lantern | Wall and floor | 0.5 m | Tunnels, every mine room | Scene with a light (`EmitsLight`); hanging and post variants |
| Timber frame | Corridor passageway | One tile | Mine tunnels | Posts and lintel across a tunnel, like the existing passageway pieces; a cracked variant |
| Winch | Floor | 1.5 m | Ore chamber, grotto edge | Rope drum with a bucket |
| Blasting powder kegs | Floor | 1 m | Foreman's hut, store rooms | A marked keg stack; reads as hazard, no gameplay yet |

## Priority 2: plan identity

Each existing plan works today, but one or two signature props would make it unmistakable.

| Prop | Mount | Rough size | Used by | Notes |
| --- | --- | --- | --- | --- |
| Prayer kneeler | Floor | 1 m | Side chapel, nave | Small and repeatable in front of altars |
| Well | Floor | 1.5 m | Cloister courtyard, herb garden | Low wall, winch and bucket |
| Garden bed | Floor | 2 × 1 m | Herb garden, courtyard | Raised planter with herbs; walkable around |

The church bell, reliquary cabinet and bishop's throne are built (`scenes/levels/rooms/props/cathedral/`) for the Cathedral's bell tower, reliquary hall and throne room (see the [content pacing plan](realm-content-pacing-plan.md#new-rooms)). The baptistry centres on a dry fountain, the fountains' own models with their liquid hidden (`props/decor/fountain_dry.tres`), since a filled basin would read as a usable fountain.

## Priority 3: chasm and lake plan

The chasm/lake plan waits on these. Chasm tiles and water rendering already exist inside rooms; these extend them to the whole floor.

| Prop | Mount | Rough size | Used by | Notes |
| --- | --- | --- | --- | --- |
| Plank bridge | Floor tile | One tile | Choke points over chasm or water | Straight and end pieces; walkable deck with rope rails that keep walkers on |
| Stone bridge | Floor tile | One tile | Same, for halls and cathedral floors | Arched span; heavier look than the plank bridge |
| Chasm edge | Floor tile | One tile | Every chasm border | Broken stone lip, straight, outer and inner corner; the drop below fades to black |
| Lake shore | Floor tile | One tile | Underground lake borders | Rough rock shore in the same three pieces as the chasm edge; the existing quay pieces suit built pools only |
| Lava surface | Floor tile | One tile | Fire chasm variant | Emissive lava under the chasm edge pieces, for a cathedral-in-flames realm |

The prison's props are built (`scenes/levels/rooms/props/prison/`): the interrogation room centers on a torture rack, lines a wall with stocks or an iron maiden (closed or open on its spikes) and may hang a cage, empty or with bones, which cell blocks hang too. They are sized chunky beside the 2.2 m hero, like the KayKit pack. The Garrison's graveyard (`scenes/levels/rooms/props/graveyard/`) is built too: rows of graves, grave pedestals, and a mausoleum behind an iron fence with an arched gateway.

## Rooms already built from existing props

These need nothing new: side chapel, monk cell, interrogation room and scriptorium (alongside the earlier cell, nave and courtyard). The props above add to them later; the rooms do not wait.