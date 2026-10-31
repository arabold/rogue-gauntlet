# Weapon style guide

Weapons match the modern KayKit packs the characters come from: Adventurers (`assets/kaykit-adventurers/axe_1handed.fbx`, `axe_2handed.fbx`, `sword_*.fbx`, `dagger.fbx`, `staff.fbx`, `wand.fbx`, `crossbow_*.fbx`) and Skeletons (`assets/kaykit-skeletons/Skeleton_Axe.gltf`, `Skeleton_Blade`, `Skeleton_Staff`, `Skeleton_Crossbow`). The older `kaykit-dungeon-legacy` set only illustrates tier conventions. Compare new work against these references in Godot at the same scale before review.

All dimensions are metres. The origin is the hand grip and +Y runs along the weapon. One-handed axe blades point to -X.

## Reference proportions

| KayKit model | Length | Shaft or grip width | Head width | Blade slab |
| --- | ---: | ---: | ---: | ---: |
| `axe_1handed` | 1.24 | 0.10–0.14 | 0.69 | 0.09, 0.18 at the socket |
| `axe_2handed` | 1.72 | 0.16 | 1.24 | 0.15, 0.26 at the socket |
| `Skeleton_Axe` | 1.25 | 0.15 with straps | 0.99 | 0.13 |

KayKit grips are about 8–10% of a weapon's length. Our pre-guide axes, polearms and staffs were about half of that.

## Shaft

- Keep it thick: 0.10–0.12 for one-handed weapons and 0.13–0.16 for two-handed ones. Taper it slightly toward the head and flare or cap the butt. A gentle bend reads as hand-made.
- Use eight sides. The flat facets catch the palette gradient like KayKit's hafts do.
- Use one to three chunky bands, 0.06–0.10 tall and 0.015–0.02 proud, or two or three diagonal straps. Never a full-length spiral wrap or fine ring grooves.
- End in a wooden knob, a chunky octagonal cap or, for rare tiers, a metal cap with a gem. Spikes are not part of the language.

## Head and blade

- Build one thick slab. Outlines are straight facets, with three to five segments along a cutting edge, never smooth or wavy curves.
- A cutting edge is a broad flat bevel, 0.08–0.12 wide, down to a thin edge, one step lighter (`edge`). Other edges stay square at full thickness.
- The socket is a chunky chamfered block, and the haft's end shows above it.
- Ornament is a raised plate on the cheek, gems, or one chunky carved motif. No inset panels, outline frames, trim strips or decorative spikes.
- Detail thinner than about 0.02 disappears at the game camera. Avoid wire guards, thin rings and fine grooves.

## Aging

- Wear lives in the silhouette: V-chips bitten out of cutting edges, a clipped corner, an uneven double bit. Cut faces show bright metal.
- Scale wear inversely with tier. Plain low-tier weapons carry the most chips, and rare ones at most a small nick.
- No noise textures, painted scratches or dirt decals.

## Material, color and tiers

- Shading comes from the palette gradient. Metals stay nearly matte (metallic 0.1, roughness 0.55); wood, leather and cloth are fully matte. Strong metallic values read as chrome next to KayKit art.
- Keep the palette small: steel with its edge, wood, one wrap color, and optional gold with a gem.
- A tier changes form as well as color. Rare weapons add gold cheek plates, gems, a lighter wrap and a capped butt.

## Construction

`items/axes.py` builds the axes from this vocabulary: the bent octagonal haft, chunky bands, wedge-shaped bevelled bits and chip wear. Recipes choose a `kind`; each kind authors wear to suit its tier. Metal slots pass an explicit `ramp` (light, shadow, base) in their recipe materials, because the default metal ramp washes up-facing bevels to white. The other weapons predate this guide and still need rebuilding against it.
