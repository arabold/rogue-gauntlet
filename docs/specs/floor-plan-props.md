# Props for the new floor plans

Status: partly built. The mine, prison, graveyard and Cathedral props are built (under `scenes/levels/rooms/props/` in the `mining`, `prison`, `graveyard` and `cathedral` folders, plus the dry fountains in `decor`). This page lists only the props still missing. The [floor generators plan](floor-generators-plan.md) describes the floor plans these props serve, and [level design](../level-design.md) owns room assembly and furnishing.

Each entry becomes a `Prop` under `scenes/levels/rooms/props/<category>/`: a mesh-library item, or a scene when it needs nodes such as a light. It then joins the furnishing steps of the rooms named here. Props follow the Studio's [room furnishing workflow](../../asset-studio/README.md#room-furnishings):
- start from a concept drawn against the pack's own props;
- size in the pack's exaggerated proportions, measured against the hero, the barrel, the chest and the 4 m wall;
- color only from the pack's swatches;
- cut real geometry rather than overlay shapes.

A prop's front faces local +Z; back-to-wall props stand with −Z against the wall. Sizes below are footprints, not heights.

## Plan identity

One or two signature props would make each plan unmistakable.

| Prop | Mount | Footprint | Used by | Notes |
| --- | --- | --- | --- | --- |
| Prayer kneeler | Floor | 1.5 m | Side chapel, nave | Small and repeatable in front of altars |
| Well | Floor | 2 m | Cloister courtyard, herb garden | Low wall, winch and bucket |
| Garden bed | Floor | 2.5 × 1.2 m | Herb garden, courtyard | Raised planter with herbs; walkable around |

## Chasm and lake plan

The chasm/lake plan waits on these. Chasm tiles and water rendering already exist inside rooms; these extend them to the whole floor.

| Prop | Mount | Footprint | Used by | Notes |
| --- | --- | --- | --- | --- |
| Plank bridge | Floor tile | One tile | Choke points over chasm or water | Straight and end pieces; walkable deck with rope rails that keep walkers on |
| Stone bridge | Floor tile | One tile | Same, for halls and cathedral floors | Arched span; heavier look than the plank bridge |
| Chasm edge | Floor tile | One tile | Every chasm border | Broken stone lip, straight, outer and inner corner; the drop below fades to black |
| Lake shore | Floor tile | One tile | Underground lake borders | Rough rock shore in the same three pieces as the chasm edge; the existing quay pieces suit built pools only |
| Lava surface | Floor tile | One tile | Fire chasm variant | Emissive lava under the chasm edge pieces, for a cathedral-in-flames realm |
