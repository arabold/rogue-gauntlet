# Modular tile families

Floor, wall and rug tiles built procedurally in the KayKit dungeon style, on the KayKit footprints, so they extend the existing dungeon set. Each recipe here is one **family**: one material with every piece and variant it needs. The family builds, validates and is reviewed as a whole because its tiles must join one another.

The studio owns the art only. Mesh libraries, collision, navigation, realm palettes and generator rules (which family a room uses, where grates go) belong to game-side wiring and are not generated here.

## Footprints

| Kind | Piece | Footprint (Godot units) | Matches |
| --- | --- | --- | --- |
| floor | `large` | x, z in [-2, 2]; slab y -0.1 to top 0.05 | `floor_*_large` |
| floor | `small` | x, z in [-1, 1]; same slab | `floor_*_small` |
| wall | `wall` | x in [-2, 2], y in [0, 4], z in [-0.5, 0.5] | `wall` |
| wall | `wall_half` | x in [0, 2] | `wall_half` |
| wall | `wall_corner` | the L: x in [-2, 0.5], z in [-0.5, 2] | `wall_corner` |
| wall (cave only) | `wall_Tsplit`, `wall_crossing`, `wall_doorway`, `wall_gated`, `wall_half_endcap`, `pillar`, `gate_leaf` | KayKit's T-split (x in [-2, 2], z in [-0.5, 2]), crossing (x, z in [-2, 2]), doorway and gate (wall footprint; KayKit's 2 by 2.8 door opening at x in [-1, 1]), half endcap (x in [-2, 0]); pillar x, z in [-0.75, 0.75]; gate leaf x in [-0.16, 1.8] about its hinge | `wall_Tsplit`, `wall_crossing`, `wall_doorway`, `wall_gated`, `wall_half_endcap` |
| overlay | `center`, `edge`, `corner`, `runner`, `runner_end` | x, z in [-2, 2], resting on a floor top (y 0.05) | none; rugs are new |

Builds fail when a tile leaves its footprint, is not a closed solid, or exceeds the recipe's triangle budget. Two wall styles relax this on the faces only, never on the ends: cave boulders may sit up to 0.25 inside the footprint, and dressing on KayKit walls may stand up to 0.15 proud of the faces and 0.25 above the cap. Dressed walls keep KayKit's open panel meshes (a carving may remove a few open accent bricks, never add defects); everything added must be closed.

## The seam contract

A floor pattern repeats every **2 units** (the small tile), in pattern space anchored at large-tile centers. That is what lets the generator mix 4x4 and 2x2 tiles, and any variants, in one room:

- Cells wholly inside a tile (boards, slabs, bricks, bumps, stones) vary freely per variant.
- Cells crossing a tile edge are cut flush there and take their tone and height from their periodic class only, so the neighbor tile continues the very same board or brick. Plank joints never fall on a tile edge, so the old repeating stair-step of KayKit's wood floor cannot appear.
- Ground (dirt, cave) is a height field whose variant relief fades to zero value and slope at the edge, with normals taken from the field itself, so lighting is continuous.
- Shading uses a fixed height range per kind, so neighbors land on the same palette ramp whatever their own relief.
- Tiles share KayKit's material (no metal, roughness 0.45). Every face drawn from a KayKit texture keeps its swatch, its position on that swatch's gradient and the swatch's exact gradient, copied into our palette; KayKit props with flat material colors keep those colors and their roughness and metal. Measured against the originals under the same light, walls, props, the skull and shelf boards match within a few RGB values. Floors that sit beside KayKit's use its floor-stone swatch and seam in light bevel rims rather than dark grout. The one remaining difference is KayKit's faint texture grain.

Tiles assume **one orientation** (only whole-room quarter turns), like the wood floors today. Variety comes from more variants, not from random rotation. Wall ends are symmetric front to back, so wall pieces still turn freely. Rug pieces turn by design: their field motif survives quarter turns and their border motifs are mirror-symmetric.

Walls stitch the same way along their length. Masonry stones crossing an end are cut flush with properties from their course only. Cave walls put a mirror-symmetric timber post (splits included) on every end, so each piece carries half of one post and boulders never cross an end. Dressed walls are original KayKit walls whose dressing keeps clear of the ends.

`tile build` checks the contract geometrically. It samples every tile edge and requires floor edges to repeat one family cross-section, wall ends to match the straight wall's end and be mirror-symmetric, rug edges to agree by role (field, border run, open end), and dressed walls' ends to be identical to their KayKit source. Colors are continuous by construction; review the lineup for them.

## Families

| Family | Kind / style | Use |
| --- | --- | --- |
| `floor_planks`, `floor_planks_dark` | floor / planks | Halls, barracks; staggered boards in oak or dark stain |
| `floor_planks_rotten` | floor / planks | Ruined garrison: weathered boards, some missing (real pits) |
| `floor_parquet` | floor / herringbone | Libraries, halls of rank |
| `floor_flagstones` | floor / flagstones (`random`) | General dungeon stone, in KayKit's floor-stone swatch |
| `floor_flagstones_ruined` | floor / flagstones | Ruins: chips, cracks, missing slabs with rubble |
| `floor_checker` | floor / flagstones (`grid`, `checker`) | Temples, audience halls |
| `floor_bricks` | floor / flagstones (`running`) | Sewers, cellars |
| `floor_earth` | floor / ground | Packed earth, pebbles and clods (named apart from KayKit's `floor_dirt_*` tiles, which share the mesh libraries) |
| `floor_cave` | floor / ground | Cave ground in KayKit's floor swatch (as its rocky floors), stones in its rubble color |
| `wall_ashlar` | wall / masonry | The one well-masoned wall, for royal and noble settings |
| `wall_mossy` | wall / dressed | Jungle: KayKit dungeon wall with moss over the cap, hanging roots and leafy vines, ferns and grass |
| `wall_overgrown` | wall / dressed | Deep jungle: the same wall swallowed by moss mats, curtains of roots and vines, dense ferns (heavy: up to 18k triangles) |
| `wall_slimy` | wall / dressed | Sewer: KayKit dungeon wall with a drain pipe run through the wall leaking into a puddle, slime runs and algae |
| `wall_prison` | wall / dressed | KayKit dungeon wall with pairs of iron manacles on chains bolted to the face |
| `wall_crypt` | wall / dressed | KayKit dungeon wall with a shelved alcove holding a single KayKit skull among candles, coins and a book |
| `wall_alcove` | wall / dressed | KayKit dungeon wall with one deep alcove in KayKit's proportions: books and bottles, keys and coins (a key ring hangs beside it), candles, bare shelves, or empty; each variant shows the next kind |
| `wall_cave` | wall / cave | Big stacked boulders and cracked rubble between split timber posts on stone footings, open rock top; rock matches `rubble_large`. Also builds a T-split, crossing, timber-framed doorway and gate, a wall end slumping into rubble, and a free-standing boulder pillar with a bolted collar |
| `carpet_red`, `carpet_blue` | overlay / carpet | Rugs of any size and runners of any length |

Floor and wall families build four variants of each main piece by default; earth and cave floors build six because loose stones make repeats easier to spot. Add letters to `pieces` for more. Dressed walls derive from `assets/kaykit-dungeon-remastered` (CC0), so their exports carry that provenance. A breach is KayKit's own `wall_broken`. Their dressings follow `dungeon_walls.reference.png`, an AI concept sheet.

## Opening gates

`gate_leaf` is the cave gate's leaf as its own model, built like KayKit's door leaf (`wall_doorway_door`): its origin is the hinge axis and it closes along +x. Hang it in `wall_cave_doorway` at x = -0.82 from the doorway's center, as KayKit's doorway hangs its door, and the game's `Door` swings it 90 degrees around Y with no code changes. `wall_cave_gated` is the same doorway with the same leaf fused in closed, for openings that never open.

## Assembling rugs

Authored borders face local -z (`edge`), -z and -x (`corner`), both z sides (`runner`, which runs along x) and both z sides plus -x (`runner_end`, fringed). With Godot's Y rotation, a 3x3 rug is: corners at 0 (north-west), -90 (north-east), 180 (south-east) and 90 (south-west) degrees; edges at 0 (north), -90 (east), 180 (south) and 90 (west); centers anywhere. A runner is `runner_end` at 0, any number of `runner`, and `runner_end` at 180. Rugs lie on the floor layer's top; they do not replace the floor.

## Recipe

```json
{
  "version": 1,
  "name": "Oak plank floor",
  "kind": "floor",
  "style": "planks",
  "design": {"plank_width": 0.4, "min_length": 0.9, "max_length": 2.4, "margin": 0.45},
  "colors": {"plank": [150, 92, 60], "plank_light": [162, 102, 66], "plank_dark": [134, 80, 52],
             "plank_warm": [156, 88, 54], "grout": [62, 40, 30]},
  "pieces": {"large": ["A", "B", "C", "D"], "small": ["A", "B", "C", "D"]},
  "budget": {"triangles": 1200},
  "export": {"folder": "res://assets/custom-tiles/dungeon"}
}
```

A recipe may name its selected concept image (`reference`, beside the recipe, with its exact prompt beside it (`wall_cave.reference.prompt.txt`)); concepts guide the design and are not geometry. `wall_cave.reference.png` is the AI concept sheet the cave pieces follow.

Every slot a builder uses needs a color; `materials` overrides roughness, crease or the exact `[light, shadow]` ramp per slot (the cave rock uses KayKit swatch 07, the rubble color), and the recipe-level `ramp` softens or strengthens the default light/shadow factors. Tiles are named after the family and piece, KayKit style: `floor_planks_large_A`, `wall_ashlar_half_B`, `carpet_red_corner_A`. `studio.py tile schema` prints the full schema.

Main `design` parameters per style (see the builders for all):

- `planks` (`tiles/floors.py`): `plank_width` (must divide 2), `min_length`/`max_length`, `margin` (joint distance from a tile edge), `stagger`, `missing`.
- `herringbone`: `brick_width` 0.5 or 0.25, the only widths whose lattice repeats every 2 units.
- `flagstones`: `layout` `random` (`smallest`, `largest`, `grid`), `grid` (`cell`, optional `checker` with four `tones`) or `running` (`course`, `length`); `chips`, `cracks`, `missing`.
- `ground`: `amplitude`, `bumps`, `stones`, `stone_size`, `clods`, `clod_size`.
- `masonry` (`tiles/walls.py`): `courses`, `min_length`/`max_length`, `quoin` (long/short corner stones), `jitter`, `tilt`, `cap_drop`.
- `dressed` (`tiles/dressing.py`): densities per 4 units of wall for `patches`, `cap_moss`, `vines`, `roots`, `grass`, `ferns`, `slime`, `pools`, `pipes`, `chains`; `alcove` (`chance`, `contents` from `books`, `keys`, `skulls`, `shelves`, `candles`, `empty`, and overrides of the measured `ALCOVE` shape) on straight pieces, where `pipes` also run through both faces; `moss_tones`, `thickness`; `source` maps a piece to another KayKit wall. Alcoves follow KayKit's own alcove walls, measured on its 4-unit wall: about 2 wide, from just above the plinth to just under the cap with a flattened head, 0.4 deep, with 0.19-thick boards in KayKit's plank swatch 0.7 apart. Their contents are the KayKit decor prefabs (`prefabs/decor.py`), a few different things per shelf from hand-composed `LAYOUTS`, shrunk only where a shelf or the arch leaves too little room; KayKit's own accent bricks frame the arch and line its back. Placed decor keeps KayKit's authored topology (the open book, spellbook, mug and coin stack have open or shared edges), and validation allows exactly those. Alcoves and pipe bores are real openings cut into KayKit's face panel, lined in the wall's own hue a shade darker (`recess`, `recess_side`); fixtures are sized like KayKit's exaggerated props.
- `cave` (`tiles/caves.py`): `rows` of `[height, boulder size]`, `post` half width and depth, `splits` per post face, `collar` height on pillars, `core`. Pieces are arms in `caves.PIECES`: open ends carry seam posts, arms meeting at the origin share a hub post.
- `carpet` (`tiles/overlays.py`): `inset`, `band`, `stripe`, `motif`, fringe spacing.

## Commands

```bash
./.venv/bin/python asset-studio/studio.py tile list
./.venv/bin/python asset-studio/studio.py tile build dungeon/floor_planks      # or --all
./.venv/bin/python asset-studio/studio.py tile lineup dungeon/floor_planks     # windowed Godot review
./.venv/bin/python asset-studio/studio.py tile publish dungeon/floor_planks    # after inspecting lineup.png
./.venv/bin/python asset-studio/studio.py tile export dungeon/floor_planks     # GLBs to assets/custom-tiles/
./.venv/bin/python asset-studio/studio.py tile showcase                       # sample rooms with KayKit pieces
```

`build` stages GLBs plus `validation.json` in OS temporary storage. `lineup` renders the family on the game grid (random variants, mixed tile sizes, gameplay camera, a seam close-up and every variant) in the real Godot renderer. `publish` copies only the reviewed `lineup.png` and a manifest of the reviewed GLB hashes to `catalogs/tiles/`. `showcase` renders sample rooms from the current builds mixed with KayKit's floors, walls, doorway and props (a hall, a cave, and the hall by torchlight; layout in `tiles/workflow.py` `SHOWCASE`), to check that the sets combine. `export` copies the current build into the game tree and has Godot import each file; `--no-import` defers that. Exported files are art only, and the first export to `assets/custom-tiles/` needs its `assets/ATTRIBUTIONS.json` entry.
