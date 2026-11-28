# Elemental spells: remaining work

**Status:** partly implemented. All 20 spells cast from wands and staves at five levels, with targeting, areas, wards, focus lifetime and ward saves in place; [weapon attacks](../weapon-attack-system.md#elemental-spells) describes them. Visual acceptance, a few gameplay extensions and balance remain. The [roadmap](../development-roadmap.md) owns priority.

## Higher-level visuals

Levels 4 and 5 reuse the level-3 compositions: the spell visuals clamp their level to three while reach and damage keep growing. Give each spell its own level 4 and 5 composition that grows the same signature, up to a screen-filling spectacle at level 5 that still keeps the player, enemies, telegraphs and floor hazards readable. Wards need a strong continuous shell, a visible hit flare and an unmistakable reflected trail toward the attacker at every level.

## Gameplay extensions

- **Area boundaries.** Visuals clip to the line-of-sight boundary captured at cast time; moving blockers and height differences are not accounted for yet. Validate with the caster next to a wall, across a doorway and near corners, from both sides.
- **Meteor debris.** Smaller falling meteors are cosmetic. Explore incidental damage, smaller than a main strike, only when a piece physically hits a monster.
- **Freeze payloads.** Only Frost Nova freezes. Other water spells may opt into an authored freeze later, through the area path rather than the direct-hit status payload.
- **Reduce flashing.** A player-facing option to tone down impact flashes and lightning strobes, once there is an options menu.

## Acceptance and balance

For each element, compare every spell at levels 1 and 5 in the [combat harness](../combat-harness.md) (**Spells** casts any spell at any level; **Monsters → Count**, **Level → Walls** and **Level → Dark level** stage targets, walls and darkness) and on generated floors, including overlapping effects and long fights. Timed instances must fade before freeing, wards must last until depleted or unequipped, and areas must stay inside their mask next to walls. Measure particle, light and draw budgets on target hardware.

Open for playtesting: mana costs and damage scales, Gust and nova push distances, freeze duration and boss resistance, Hailstorm and Ice Wall timings, feedback for a cast without targets, and the poise and reaction of a hit a ward fully absorbs.
