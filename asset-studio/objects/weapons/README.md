# Weapon style guide

Weapons match the modern KayKit packs the characters come from: Adventurers (`assets/kaykit-adventurers/axe_1handed.fbx`, `axe_2handed.fbx`, `sword_*.fbx`, `dagger.fbx`, `staff.fbx`, `wand.fbx`, `crossbow_*.fbx`) and Skeletons (`assets/kaykit-skeletons/Skeleton_Axe.gltf`, `Skeleton_Blade`, `Skeleton_Staff`, `Skeleton_Crossbow`). The older `kaykit-dungeon-legacy` set only illustrates tier conventions. KayKit's paid *Fantasy Weapons Bits* pack is a style reference only: never import, trace or redistribute its models. Compare new work against these references in Godot at the same scale before review.

All dimensions are metres. The origin is the hand grip and +Y runs along the weapon. One-handed axe blades point to -X.

## Reference proportions

| KayKit model | Length | Shaft or grip width | Head width | Blade slab |
| --- | ---: | ---: | ---: | ---: |
| `axe_1handed` | 1.24 | 0.10–0.14 | 0.69 | 0.09, 0.18 at the socket |
| `axe_2handed` | 1.72 | 0.16 | 1.24 | 0.15, 0.26 at the socket |
| `Skeleton_Axe` | 1.25 | 0.15 with straps | 0.99 | 0.13 |
| `sword_1handed` | 1.78 | 0.11–0.13 | 0.50 guard | 0.07–0.10 |
| `sword_2handed` | 2.37 | 0.11 | 0.84 guard | 0.10–0.13 |
| `dagger` | 1.21 | 0.10 | 0.26 | 0.08–0.09 |

KayKit grips are about 8–10% of a weapon's length.

## Shaft

- Keep it thick: 0.10–0.12 for one-handed weapons and 0.13–0.16 for two-handed ones. Sword and dagger grips are just as chunky, 0.11–0.13, which plays up KayKit's comically big weapons. Pommels and guards stay at least as deep as the grip they hold. Taper it slightly toward the head and flare or cap the butt. A gentle bend reads as hand-made.
- Use eight sides. The flat facets catch the palette gradient like KayKit's hafts do.
- Wrap the grip zone in a stack of bulging rope turns, wider than the bare haft, as KayKit's Fantasy Weapons Bits do. Accent with one or two chunky bands (0.06–0.10 tall, 0.015–0.02 proud), riveted metal strap bands, or two or three diagonal leather straps. Never a full-length spiral wrap or fine ring grooves.
- End in a chunky knob: a pointed metal cap on axes, mauls and polearms, a flared wooden knob, a disc, ring or ball pommel, or, for rare tiers, a metal cap with a gem. Apart from these caps, a maul's studs and a morning star, spikes are not part of the language.
- Bind plain axe sockets with a rope X across both faces, and hang a feather tuft under spear and halberd heads.

## Head and blade

- Build one thick slab. Outlines are straight facets, with three to five segments along a cutting edge, never smooth or wavy curves.
- A cutting edge is a broad flat bevel, 0.08–0.12 wide, down to a thin edge, one step lighter (`edge`). Other edges stay square at full thickness.
- The socket is a chunky chamfered block, and the haft's end shows above it.
- Ornament is a raised plate on the cheek, gems, or one chunky carved motif. No inset panels, outline frames, trim strips or decorative spikes.
- Sword and dagger blades are thick slabs too, at least 0.05 at mid-length. A flat center with bevels on both edges meets in a ridge at an angular point. Narrow thrusting blades are bevelled across their whole width into a center ridge. Curved blades carry their edge on the convex side, with a square spine.
- Blunt heads are big: chamfered blocks, barrels or flanged cores about half a weapon's width, on the same thick hafts. Studs and spikes are chunky pyramids that belong to the head's function: maul faces, a morning star, a shod cudgel.
- Staff and wand heads hold big faceted crystals or stones seated in their collar, inside chunky claws or crescents. Bone, horn and gnarled wood stay as thick as the shaft.
- Guards and pommels are chunky blocks and faceted discs, not plates. Use straight bars with cut ends, wings or horns swept toward the blade, drooping crescents, or ring pommels. Long two-handed grips carry several cream cloth wraps.
- Detail thinner than about 0.02 disappears at the game camera. Avoid wire guards, thin rings and fine grooves.

## Aging

- Wear lives in the silhouette: V-chips bitten out of cutting edges, rows of small comb notches, a clipped corner, an uneven double bit. Cut faces show bright metal.
- Scale wear inversely with tier. Plain low-tier weapons carry the most chips, and rare ones at most a small nick.
- No noise textures, painted scratches or dirt decals.

## Material, color and tiers

- Shading comes from the palette gradient. Metals stay nearly matte (metallic 0.1, roughness 0.55); wood, leather and cloth are fully matte. Strong metallic values read as chrome next to KayKit art.
- Keep the palette small: steel with its edge, wood, one wrap color, and optional gold with a gem.
- Magic stones glow: crystals, emeralds and moonstones are emissive. The game may add a light or VFX on top; gems set in gold stay plain.
- Elemental weapons show their element in shape, color and glow only; they deal ordinary damage. Fire is a blade of chunky flame tongues, orange round a yellow core; water is ice crusting a steel blade, with shards jutting up and out; earth is a rough-hewn stone head with a glowing carved glyph; air is zig-zag lightning prongs. Flame and lightning always glow, while ice and stone stay unlit.
- A tier changes form as well as color. Rare weapons add gold cheek plates, gems, a lighter wrap and a capped butt.

## Construction

Knobs and handles are registered prefabs in `prefabs/details/hilts.py` (see `studio.py schema details` and the details prefab catalog): disc, ring and ball pommels, spiked, flared and gem caps, rope wraps, straps, riveted bands, rope crosses and feather tufts. Each is built round the Y axis and sized from the haft radius it fits; `Haft.wraps`, `Haft.band`, `Haft.fit` and `Haft.knob` place them.

`items/forge.py` holds the shared vocabulary: octagonal hafts and grips, bands and collars, bevelled `Blade` slabs with chip wear, chamfered plates, discs, rings, studs, tubes, crystals, gems and gnarled branches. One builder per family composes weapons from it: `axe` (`items/axes.py`), `blade` (swords and daggers, `items/blades.py`), `blunt` (hammers, maces, mauls and clubs, `items/blunt.py`), `polearm` (`items/polearms.py`), `staff` (staffs and wands, `items/staves.py`) and `ranged` (crossbows, `items/ranged.py`). Recipes choose a `kind`, and each kind authors its wear to suit its tier. Every weapon recipe starts from the forge's shared palette (`COLORS`, `MATERIALS`) and lists only the slots it changes. Shaded metals carry explicit (light, shadow, base) ramps there, because the atlas's default metal ramp washes up-facing bevels to white.

Every catalog weapon follows this guide. File names, recipe keys and game folders name the weapon a design actually is, such as `mace_gilded` in `mace/`, `claymore_iron` in `greatsword/` and `dagger_dirk`. When a redesign changes what a weapon is, rename its recipe, export, scene and item resource together, rewire loot tables and docs, and bump `SaveGame.CurrentVersion`, because saves store item paths.
