# Magic missile: finished look

**Status:** partly implemented. Every wand and staff fires its element's homing missile volley, and [weapon attacks](../weapon-attack-system.md#spell-weapons-wands-and-staves) describes its rules and flight settings. The darts still wear a first-pass look; this plan covers only that and the acceptance review.

## Look

All four elements share one silhouette, size and timing; the element changes only the color ramp and one small garnish. Build from the shared toon pieces in `scenes/magic/shared/toon/` and the [spell look](../weapon-attack-system.md#elemental-spells).

- **Missile.** A small, elongated faceted crystal (about 0.35 m) with a hot white core and element-colored rim (`toon_core` with the element ramp), rolling about its flight axis, followed by a short `RibbonTrail` with `toon_ribbon_glow`. No light in flight. The first pass is a sphere-mesh crystal without roll; an arrowhead shard from `FacetedSpikeMesh` and the roll remain.
- **Launch.** A tiny sparkle at the wand tip per missile, and a small rotating rune flash replacing the element's staff muzzle burst, which currently dwarfs the darts.
- **Impact.** A first-frame `toon_flash` card, a thin ring and four to six `toon_spark` diamonds, gone within 0.3 s; no scorch, smoke column or decal, since this fires constantly. Expiry at range plays a smaller fizzle.
- **Element garnish.** Fire sheds embers; water leaves frost motes; earth is an opaque amber-obsidian shard trailing grit; air crackles with tiny additive sparks. Water and air currently read as two similar blues.
- **Readability.** Bodies stay blended and below the brightness of real spells; only the core band glows, so a stream of missiles never hides monster telegraphs.

## Acceptance

Review each element in the [combat harness](../combat-harness.md): equip each wand from **Items**, fire at a ring of moving targets with walls, slow motion and **Dark level**, in bright and dark rooms, next to walls and in long held streams; compare a hands-on Mage run in `main.tscn` against a crossbow. Check the particle and draw budget with several casters on target hardware. Then remove this plan.

## Open decisions

- **Damage numbers.** Several hits a second may crowd the floating numbers; consider merging a volley's hits on one target.
- **Cast motion.** Wands share the staff's `staff.cast` clip; a dedicated one-handed wand style may read better.
