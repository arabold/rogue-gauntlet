---
name: assets
description: >
  Use when discovering, downloading, or adding new 3D/2D/audio assets to this game from
  curated free sources (KayKit, Quaternius, Kenney, Poly Pizza) or generating our own via
  image-to-3D. Covers the two automation channels (KayKit GitHub + Poly Pizza API), the
  download->manifest->import workflow, licensing/style rules, and the experimental AI
  authoring path. Policy and the asset manifest live in assets/AGENTS.md; this skill is the
  how-to and the scripts. Not needed for editing assets already in the repo.
---

# Asset acquisition for Rogue Gauntlet

Find, fetch, and register third-party (or self-authored) assets while staying license-clean
and visually cohesive. **`assets/AGENTS.md` is the policy + manifest source of truth**
(the open-source free-download rule, the KayKit cohesion rule, the licensing rules, and the
`ATTRIBUTIONS.json` schema). This skill is the mechanics: which script, what command, and how
the pieces fit. Read `assets/AGENTS.md` before adding anything.

## Secrets

Keys live in the repo `.env` (gitignored): `POLYPIZZA_KEY`, `HF_TOKEN`. Load them per shell:

```bash
set -a; . /Users/andrerabold/Projects/Personal/rogue-gauntlet/.env; set +a
```

Never print key values or pass them on a command line that gets logged.

## The two automation channels

### 1. KayKit -> GitHub (no key) — cohesive whole packs
Our primary 3D style. Public repos under `github.com/KayKit-Game-Assets`, all CC0.

```bash
python3 scripts/fetch_kaykit.py KayKit-Game-Assets/KayKit-Halloween-Bits-1.0 assets/kaykit-halloween-bits --dry-run
python3 scripts/fetch_kaykit.py KayKit-Game-Assets/KayKit-Halloween-Bits-1.0 assets/kaykit-halloween-bits
```
Pulls the `gltf` set (`.gltf`+`.bin`+shared texture) + LICENSE, flattened to match existing
`assets/kaykit-*` dirs. Stdlib only. (`GH_TOKEN` optional, only to lift the unauth rate limit.)

### 2. Poly Pizza -> API (`POLYPIZZA_KEY`) — discovery + single models
Indexes Quaternius, Kenney, and many others. Mixed CC0 / CC-BY.

```bash
# discover: see tris (low-poly?), licence, and Animated before committing
python3 scripts/fetch_polypizza.py search goblin --limit 25 --cc0 --max-tris 3000
# download one + get a ready-to-paste manifest entry (CC-BY attribution captured)
python3 scripts/fetch_polypizza.py get <ID> assets/<id>
```
Prefer `--cc0` to avoid attribution debt; if you take a CC-BY model, the printed
`attribution_text` MUST go in the manifest and the shipped credits.

## Add-an-asset workflow

1. **Discover / fetch** with the right channel above.
2. **Register** in `assets/ATTRIBUTIONS.json` — the Poly Pizza `get` prints a near-complete
   entry; for KayKit, copy an existing `kaykit-*` entry and adjust. Set the right
   `compatibility_group` (`kaykit` for KayKit; for Poly Pizza models triage out of
   `polypizza-unsorted` once you've judged style fit).
3. **Import into Godot** (generates import caches + `.uid` sidecars), from the repo root:
   ```bash
   .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --import
   ```
4. **Verify**: `/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"`, and validate a
   sample model loads (`.agents/skills/godot-mcp/scripts/inspect_resource.gd`).
5. **New creator/style only**: do the in-engine visual side-by-side (see `assets/AGENTS.md`).

## Turning a downloaded character into an enemy (recipe)

Validated on the Quaternius monsters. Enemies instance `scenes/enemies/enemy_base.tscn` and
override the model + an AnimationTree state machine + tuning resources (see the skeleton
variants in `scenes/enemies/skeleton/` as the reference). Steps:

0. **Pick a *coherent* set, not just same-creator.** One creator's "monster pack" often spans
   several visual sub-series — e.g. Quaternius has a textured-atlas `EnemyArmature` series (goblin,
   zombie) AND a vertex-colored series (orc, demon, ghost, yeti, slime, bat, spider) that do **not**
   match. Probe candidates and group by look (textured vs vertex-color via the material check) before
   committing; mixing sub-series reads as broken even though it's "all Quaternius". Within one
   coherent set, rigs still differ per body type (`CharacterArmature`/`MonsterArmature`/`BatArmature`/
   `SpiderArmature`) — that's fine, just map clips per model.
1. **Probe the model's animations.** Load the GLB and list its `AnimationPlayer` clips — names vary
   per model and rig: humanoids use `Idle`/`Walk` + `Punch`|`Bite_Front` + `HitReact`|`HitRecieve` +
   `Death`; the bat flies (`Bat_Flying` for idle+move, `Bat_Hit`); the spider has no hit clip (reuse
   `Spider_Idle` for the Hit state). The state machine must reference the exact strings. Flyers
   (bat/ghost) get a model `transform` Y-offset to hover; true flight needs a movement/behavior change.
2. **Fix looping in the `.glb.import`.** Fresh imports have `_subresources={}`, so Idle/Walk/Run
   play once and freeze. Add `"animations": { "<clip>": { "settings/loop_mode": 1 } }` for the
   looping clips (leave Attack/Hit/Death at 0 so their at-end transitions fire), then reimport.
3. **Normalize scale — by eye, not by measurement.** Downloaded characters arrive at arbitrary
   sizes. Set a uniform `transform` scale on the model node under `Pivot`, scaling about the origin
   so the feet stay on the ground and the pivot is intact. **Confirm the size visually in the
   editor against an existing actor** — headless height measurement of GPU-skinned GLBs is
   unreliable (bind-pose AABBs and bone spans both mislead and disagree with each other), so the
   editor eyeball is authoritative. As a starting point, Quaternius monsters sit at ≈ **0.75** to
   read right next to the KayKit skeleton; expect to nudge per model.
4. **Match the collision shapes to the reference enemy.** Use the same capsule sizing the skeleton
   variants use — `radius 0.75`, `height 2.0`, body `CollisionShape3D` at `y = 1.2`, plus the
   hurtbox `CollisionShape3D`. Don't shrink them to a mis-scaled model; size the model to them.
5. **Author the scene** (`scenes/enemies/<creature>/<name>.tscn`, one folder per creature family like
   `skeleton/` — not per author): instance `enemy_base.tscn`; add the
   model under `Pivot`; add an `AnimationTree` under `EnemyBehaviorComponent` with `root_node` →
   the model, `anim_player` → the model's `AnimationPlayer`, `advance_expression_base_node` → `..`,
   and a `tree_root` state machine (Idle/Walking/Attack/Hit/Dying/Dead) whose transitions key off
   the behavior flags `IsMoving`/`IsAttacking`/`IsHit`/`IsDead`. Add the two `CollisionShape3D`s
   (hurtbox child + body). Set `HealthComponent`, `DeathComponent.Xp`, `HurtBoxComponent.Evasion`,
   `LootTableComponent.Table`, and `EnemyBehaviorComponent.Profile`.
6. **Author a profile** co-located in the creature folder (`scenes/enemies/<creature>/<name>_behavior.tres`,
   script `EnemyBehaviorProfile`) — each creature folder is self-contained (scene + profile). If the
   model has no spawn clip, set `InitialAction = 0` (Idle), not Spawning.
7. **Register** the scene path in `scenes/levels/dungeon/dungeon_mob_factory.tres`.
8. **Verify**: reimport, `dotnet build`, then a headless probe that instantiates the scene and
   asserts the AnimationTree resolves and every state maps to a clip the player has; finally boot
   `res://scenes/main/main.tscn` and confirm the monster spawns. The cohesion/look call is the
   maintainer's — eyeball it next to existing actors.

## Visual catalog (model screenshots)

`scripts/render_catalog.gd` renders a transparent-background PNG of every `.glb`/`.gltf` in a
directory (posed in Idle, 3/4 camera) — used to build `docs/monster-catalog/`.

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
  --script .agents/skills/assets/scripts/render_catalog.gd -- docs/monster-catalog res://assets/quaternius-monsters
```

`scripts/render_item_catalog.gd` does the equivalent for items: loads each item `.tres`,
instances its `Scene`, auto-centers/auto-rotates it (mirroring `Preview.cs`'s in-game inventory
preview framing) and screenshots it alone against a transparent background — used to build
`docs/item-catalog/`.

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
  --script .agents/skills/assets/scripts/render_item_catalog.gd -- docs/item-catalog res://scenes/items/weapons/axe_common.tres [more...]
```

- **Run WINDOWED — not `--headless`.** Headless uses the dummy renderer: viewport captures come
  back blank and the script hangs. A real window opens briefly and renders on the GPU. (`timeout`
  isn't on macOS — rely on the runner's own time cap if you need a guard.)
- Pass the dir to `render_catalog.gd` and let it scan (`DirAccess`); don't word-split a file list
  on the shell — zsh doesn't split unquoted `$VARS` like bash, so only one path reaches the
  script. `render_item_catalog.gd` takes explicit item paths instead (items live scattered
  across `scenes/items/<category>/`, not one flat asset folder).
- After regenerating, update the table in `docs/monster-catalog/README.md` or
  `docs/item-catalog/README.md`.

## In-hand weapon/shield verification (does NOT catch everything the catalog does)

**The floating catalog render above is not sufficient to validate a new weapon or shield.**
It auto-frames and auto-rotates for a nice thumbnail, which can hide exactly the two defects
that only show up once a model is actually bone-attached to the player:

1. **Wrong local-axis convention.** `BoneAttachmentManager` parents the item's `Scene` directly
   under a `BoneAttachment3D` with zero corrective transform, so the model's own local axes must
   already match what that bone's rest pose expects. Different weapon-hand bones expect
   different conventions (a melee bone expects "extends away from grip" along local **+Y**; a
   raw bow/crossbow-style asset instead puts its aim along local **Z** with limbs along X) — get
   it wrong and the model can end up thin-edge-on to every normal camera angle, reading as fully
   invisible, not just "rotated a bit wrong."
2. **Cross-tier scale inconsistency.** A raw downloaded model isn't guaranteed to be scaled
   consistently with an existing baked-mesh sibling in the same item ladder (e.g. a new tier-2
   `.glb` coming in at half the height of the tier-1 item it's supposed to visually escalate from).

`scripts/render_held_items.gd` equips a sequence of items on a standalone player (no
GameSession/class active — the authored Barbarian rig) and screenshots the character from three
angles per item (front, weapon-hand 3/4, shield-hand 3/4), so whichever hand an item lands in is
covered without needing to guess the right camera angle up front:

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
  --script .agents/skills/assets/scripts/render_held_items.gd -- /tmp/held_test \
  res://scenes/items/weapons/axe_rare.tres res://scenes/items/armor/shield_rare.tres [more...]
```

- **Windowed only**, same reason as above.
- Requires a floor: the player is a physics-driven `CharacterBody3D` and free-falls under gravity
  in an otherwise-empty scene (for however many physics ticks elapse before the first idle frame
  — decoupled from frame count and inflated by first-load shader/asset stalls), landing at an
  unpredictable height by the time you'd screenshot. The script adds a `StaticBody3D` floor and
  waits `SETTLE_FRAMES` before measuring/framing; don't drop this if you fork the script.
- If an item is invisible in ALL three angles (not just badly positioned in one), suspect the
  local-axis convention, not scale/position. Diagnose by rendering the raw `.glb` alone at
  identity transform with RGB axis gizmos (R=+X, G=+Y, B=-Z), then compare against a **sibling
  weapon on the same bone that's known to render correctly** rendered the same way — the
  direction its long axis points tells you which local axis needs to land on which. Fix with a
  thin corrective wrapper `.tscn` (`rotation_degrees` on the instanced child); see the
  `godot-mcp` skill's `tres-authoring.md` for the pattern.
- `IsTwoHanded` on a `Weapon` changes which `BoneAttachment3D` it resolves to (see
  `docs/character-classes.md`), so re-verify in-hand appearance after changing it — a weapon
  that looked right on one bone isn't guaranteed to look right on another.

## Authoring our own 3D items (End-to-End Pipeline)

For static equipment, consumables, and jewelry (`weapons`, `armor`, `potions`, `scrolls`, `jewelry`), `scripts/build_item_asset.py` runs the full pipeline end-to-end — completely locally with `--backend contour` (or from a concept image) to produce a KayKit-palette low-poly `.glb`, Godot `.tscn` + `.tres`, headless import/validation, and visual verification previews:

```bash
python3 .agents/skills/assets/scripts/build_item_asset.py \
  --id ring_gold_ruby \
  --name "Gold Ruby Ring" \
  --archetype ring \
  --palette "brass,amber,gold,magenta,crimson,rose,obsidian,ivory" \
  --tier 3 \
  --backend contour
```

### What the 5-step pipeline does
1. **`[Step 1/5]` Concept Image:** Uses `--image <path.png>` (or synthesizes a clean silhouette concept image locally when `--backend contour` is used).
2. **`[Step 2/5]` 3D Mesh Reconstruction:** Runs locally via the built-in parametric low-poly builder (`--backend contour`), or queries `stabilityai/TripoSR` via `scripts/hf_image_to_3d.py` when `--backend hf` is selected.
3. **`[Step 3/5]` KayKit Low-Poly Stylization, Palette UV Remapping, & Grip Alignment (`scripts/stylize_and_align.py`):**
   - Builds or decimates meshes to flat-shaded low-poly (~150–600 tris) with unwelded per-face normals.
   - Aligns local principal axes, scale, and grip/floor origin `(0,0,0)` to match the exact measured KayKit AABB for `--archetype` (`battleaxe`, `axe`, `sword`, `broadsword`, `dagger`, `hammer`, `warhammer`, `staff`, `wand`, `crossbow`, `shield`, `helmet`, `chestplate`, `potion_small`, `potion_medium`, `potion_large`, `magic_scroll`, `ring`, `amulet`).
   - Maps every face's color onto the $8 \times 4$ swatches of `assets/kaykit-dungeon-remastered/dungeon_texture.png` (`steel`, `dark_iron`, `wood`, `gold`, `amber`, `brass`, `copper`, `crimson`, `teal`, `silver`, etc.) and embeds the 17 KB palette PNG into `assets/custom-items/<category>/<archetype>/<id>.glb`.
   - Saves an instant 3-angle (`Front`, `3/4 Isometric`, `Profile`) shaded preview with a green `(0,0,0)` origin crosshair to `<verify-dir>/<id>_3angle.png`, plus a **Frontal + Side View of the playable character** (`<verify-dir>/<id>_player_worn.png` — equipped in-hand for weapons/armor, or standing beside the `2x` world-scale drop on the ground for potions, scrolls, rings, and amulets).
4. **`[Step 4/5]` Godot Integration & Validation:**
   - Ensures `assets/custom-items` is registered in `assets/ATTRIBUTIONS.json`.
   - Writes `scenes/items/<category>/<archetype>/<id>.tscn` (applying any archetype wrapper transforms such as `crossbow`'s `-90°` X rotation or `2x` world scale for `potion_*`, `magic_scroll`, `ring`, and `amulet`) and `scenes/items/<category>/<archetype>/<id>.tres`.
   - Runs headless `godot.sh --headless --path "$PWD" --import` and `inspect_resource.gd`.
5. **`[Step 5/5]` Visual Verification:**
   - Optionally runs `render_item_catalog.gd` and `render_held_items.gd` (or pass `--skip-renders` to rely on the fast `<verify-dir>/<id>_3angle.png` and `<id>_player_worn.png` previews).

## Authoring rigged humanoid monsters (concept image -> KayKit-rigged GLB)

`scripts/build_monster.py` turns a one-line description into a rigged, animated monster on the shared KayKit
skeleton (`Skeleton_Warrior.glb`, all 95 clips). It is generic: no per-monster code, every decision lives in
editable files under the monster's `src/`. Run it with the repo venv (`numpy`, `pillow`, `scipy`,
`fast-simplification`, `google-genai`, `openai`, `gradio_client`; see `requirements.txt` next to this skill). Keys
come from `.env` (`GEMINI_API_KEY` or `OPENAI_API_KEY`; `HF_TOKEN` for image-to-3D heads).

```bash
.venv/bin/python .agents/skills/assets/scripts/build_monster.py all mage/necromancer --prompt "Evil female necromancer in a long robe with skull belt and scythe in hand. Horned crown."
# or stage by stage (paid stages skip existing outputs unless --force):
.venv/bin/python .agents/skills/assets/scripts/build_monster.py concept mage/necromancer --prompt "..."
.venv/bin/python .agents/skills/assets/scripts/build_monster.py analyze mage/necromancer
.venv/bin/python .agents/skills/assets/scripts/build_monster.py split   mage/necromancer [--only body,belt] [--force]
.venv/bin/python .agents/skills/assets/scripts/build_monster.py build   mage/necromancer   # local, free, ~10 s
```

| Stage | What it does | Cost |
|---|---|---|
| `concept` | A text LLM expands the request into a KayKit-ready brief (`src/design.json`, editable; reused while the request is unchanged), then the image model draws a T-pose front/side/back turnaround **in the style of real KayKit renders** (`style-reference/*.png`, made with `scripts/render_turnaround.gd`) | 1 LLM + 1 image call |
| `analyze` | Vision LLM -> `src/manifest.json`: core parts + up to 6 accessories, each with a front-view box, a `build` kind, host, mount, style/count, stature | 1 LLM call |
| `split` | `parts/body.png` = the concept redrawn with every accessory removed (hair stays); `parts/head.png` = that bare head alone as its own turnaround (kept only if it lines up with the body's head, else retried); for a head with `"shape": "image3d"`, `head.glb` (TRELLIS.2 on this Mac when the local port is installed, else TRELLIS on a Hugging Face Space with a free daily quota; see below) plus, with the manifest's `hair`, `head_hair.png` (that hair drawn on) and `head_hair.glb`; `landmarks.json` = the bare body's chin row (vision model over numbered guide lines); one eye-level image per accessory (hair needs none) | 2-3 + N image calls |
| `build` | Smooth body from the bare body, head from the head drawing, accessories by kind, flat palette, skinning, height calibration, GLB, previews | free |

Outputs: `assets/custom-monsters/<family>/<id>/<id>.glb` and `src/` (Godot-ignored via `.gdignore`): design,
concept, manifest, part images, `texture.png`, `stages.png` (flat label maps per view - check these first when
colors look wrong) and `preview.png` (windowed Godot render via `scripts/render_monster.gd`; `close` mode for
detail shots).

How `build` works (`scripts/monster_body.py` for the smooth body, `scripts/monster_mesh.py` for views, head,
color and wraps, `scripts/monster_shapes.py` for the hand-made accessory primitives):
- **Organic parts are automatic, accessories hand-made.** The bare body is one smooth signed-distance field of
  simple forms measured from the drawing (`monster_body.fit_body`, after img2threejs's humanoid): three
  superellipsoids least-squares fitted to the torso's front widths and side depths, round-cone chains for neck,
  arms and legs (legs with elliptic sections), ending in prefab fists and shoes, and for brutes whose shoulders rise
  beside the jaw a sloped trapezius yoke. Smooth unions turn joints into deltoids and hips; the drawing's
  muscles never become bumps. It is polygonized (surface nets), decimated (`style.body_tris`, 2600) and skinned
  as one mesh, each region to its own bones and blended where regions meet.
- **The rig fixes the bone positions** (every clip keys every bone's translation): the whole figure is mapped at
  ONE uniform scale anchored on the drawn shoulder line (the rig's arm bones) and the soles (the floor), so the
  drawing's proportions survive; the hip joint simply sits wherever the drawn thighs put it. (Fitting the drawn
  crotch onto the rig's hips squashed long legs and stretched the chest.) Arms are stretched to the rig's fists
  and straightened onto the shoulder line; neck and head land wherever the drawing puts them. Textures are projected back through the same maps, blended across region borders.
- **The side view is often not a T-pose** (the arm reaching forward, a fist in front of the chest), so torso
  depth is read only outside the arm band and below the chin; the fitted forms bridge the gap. The concept
  prompt asks for the arm end-on.
- **The head comes from its own drawing.** `parts/head.png` shows the whole head down to the jaw, which the
  body drawing often hides (a brute's chin rests on its shoulders). It is placed on the body drawing by masked
  cross-correlation of the faces (`place_head`), which also gives the chin row; without a usable head drawing
  the head is cut from the body at its neck landmark. Two head builds:
  - `"shape": "image3d"` (`monster_image3d`): TRELLIS reconstructs the bald head drawing with its ears. The mesh
    is welded (TRELLIS splits vertices at texture seams, which a decimator tears open) and turned to face +z
    (TRELLIS faces its first view toward +x), then **tilted upright**: TRELLIS returns heads tilted back by
    10-15 degrees, which keeps the outline but lifts every sculpted feature off its drawn place, so the tilt
    whose render of the reconstruction's own colors (painted exactly on its sculpt) best matches the drawing
    wins. It is fitted onto the drawing's silhouette, the neck stump cut, decimated
    (`style.head_tris`, 1600), and the drawing is registered to the reconstruction's painted features with a smooth warp
    (`align_view`) so painted brows, eyes, nose and tusks land on the sculpted ones. Colors: one flat palette
    color per face, boundaries snapped onto the drawn curves (up to `style.head_face_tris`, 3500; hair from
    `style.hair_tris`, 3000), painted shading flattened. Every part is colored this way; the GLB's texture is just
    a strip of palette swatches.
  - **Hair is its own mesh** (`"hair": "thick dreadlocks ..."` on the head part): the head is also
    reconstructed with that hair drawn on and matched to the bald head on the face; the hair is the volume the
    head with hair has beyond the bald skull (both voxelized, the skull grown by a small margin), minus the face
    and minus what the hair drawing paints in non-hair colors close to the skull (the two reconstructions' ears
    never coincide and would leave hair-colored ear shells), polygonized as solid locks and colored flat per face;
    the scalp under it takes the hair's colors. It is rigid on the head bone; crowns fit around head and hair
    together (slices with the ears opened away, so a crown sits on the hair instead of becoming a brim).
    `style.hair_mode: "together"` uses the head-with-hair reconstruction as the head instead (one mesh). Tested on
    the orc king and wraith: equal on short-to-medium hair, much worse for tall hair (TRELLIS spends its grid on
    the hair, so the face comes out coarse, and one mesh blends skin and hair colors along the hairline), so
    `"separate"` stays the default.
  - **Tusks and fangs are separate parts** (img2threejs's head construction): small, pointed, light regions in
    the lower face of the head drawing are erased from its texture and rebuilt as curved low-poly horns anchored
    in the jaw, tip at the narrow end (orc tusks up, fangs down).
  - The head sits on the drawn chin (`landmarks.json`) when the head drawing's jaw comes out longer than the
    body drawing's; the torso still starts where the shoulders emerge beside the face-aligned head.
  - otherwise a rounded superellipsoid (`HeadPart`) fitted with a robust outline; ears are add-ons (`ears`
    primitive), declared by the analysis or detected as what sticks out of the outline, and trimmed from its
    texture.
- **Hands and feet are KayKit prefabs** (`monster_prefabs`): the Rogue's mitten fists and rounded shoes, cut out
  of its arm and leg meshes by the joints that drive them, scaled to the drawn fist and foot, colored with the
  drawing's color there, and skinned with KayKit's own weights. Shins end inside the shoes; soles are flat.
- **Accessories by `build` kind.** The hand-made primitives read cleanest: `band` (plain ring), `crown` (band
  with spikes; a style mentioning horns adds a curved pair), `belt` (tube with a buckle), `horns`, `dome` (shoulder pads, round helmets), `skirt` (straight A-line robe that clears the legs
  and the hanging arms). Each is sized and placed from its concept box and colored with one palette color from
  its drawing. `wrap` keeps a band's drawn outline and pattern (the mummy's collar and apron belt); `solid`
  rebuilds free-form items from their drawings (buckles, emblems).
- **Weapons are existing items, not generated, baked into the GLB** (monsters never switch weapons). The design
  and analysis steps pick the closest weapon from the game's catalog (`scenes/items/weapons/**/*.tres`); the
  manifest part gets `build: item` and the item's path. `dump_item_mesh.gd` exports the item's colored
  triangles, which are skinned rigidly to `handslot.r` exactly as the player's BoneAttachmentManager attaches
  items (turned 180 degrees, 0.033 forward), at true human size whatever the rig scale.
- **Placement** maps concept boxes onto the body drawing by anatomy (neck row, feet, torso centre), not crop
  edges, so tall headgear doesn't push everything down. The vision model's boxes are loose, so each is first
  tightened to the item's own colors in the concept (plain bands and crowns use their typical lower edge, not
  a front piece dipping between the brows).
- **Weapons** go to the right hand the KayKit way: grip on `handslot.r`, pointing forward (+Z) in the T-pose,
  which keeps them forward in Idle and matches the attack animations. Small monsters keep human-sized weapons.
- **Flat KayKit color:** one palette clustered from all images (`style.palette_size`; `style.l_weight` 0.8 keeps
  drawn lines such as bandage edges as their own color; rare but distinct accents like glowing eyes survive),
  despeckled label maps (`style.simplify`), then painted lighting flattened (`merge_shades`, `style.flat_shading`):
  broad patches in a lighter or darker shade of a material become that material, thin drawn lines stay. Each
  face is projected from the front or back view into a flat-color atlas. The side view sizes the simple forms (head depth, belly, feet; `style.side_depth` picks the parts)
  but never colors them (that duplicated facial features) and never shapes a face (a drawn profile made a
  muzzle). Bands are solid from the body surface out; torso bands may rise behind the head, stay straight above
  the shoulders and sit below the chin at the front. Faces get extra care: the head adds its own small palette
  (nose, mouth, eye shadow would otherwise fold into the skin) and is despeckled lightly so thin brows survive.
  Check faces with `render_monster.gd ... portrait`.
- **Size calibration:** `stature` 1.0 = the KayKit adventurers' bare-head height (2.17); the rig scale is
  computed from the monster's measured height, so a 1.8 troll or 0.6 kobold lands exactly there. Scale the
  enemy scene's capsule by `stature`.
- **Side-view facing is detected, not trusted:** image models often draw the side view facing left; the feet
  tell, and the view is mirrored.

**Local image-to-3D (recommended):** TRELLIS.2 runs on Apple Silicon through the community port
[`shivampkumar/trellis-mac`](https://github.com/shivampkumar/trellis-mac), installed beside the repo at
`~/Projects/Personal/trellis-mac` (override with `TRELLIS_MAC_DIR`; `MONSTER_3D=space` forces the Space). Setup
notes for this machine: full Xcode plus `xcodebuild -downloadComponent MetalToolchain`; the Metal packages in
`deps/` need `-std=c++20` for current PyTorch and `o-voxel` needs its Eigen submodule
(`git submodule update --init` in `deps/trellis2-apple`); install them with
`uv pip install --python .venv/bin/python --no-build-isolation deps/<pkg>` (setup's own `uv` call can target the
wrong environment). Hugging Face needs access to the gated DINOv3 weights; the background remover RMBG-2.0 is
non-commercial and never used (`trellis_local_runner.py` feeds RGBA drawings with our own masks and stubs it out).
The port's texture bakers (Metal and KDTree alike) scramble the UV atlas on this machine while TRELLIS.2's own
voxel colors are clean, so the runner skips baking: each GLB is the reconstruction simplified to 200k faces with
vertex colors (`COLOR_0`, linear): every raw vertex takes its own voxel's base color, and each simplified vertex
averages the raw vertices that collapsed into it. Looking voxels up at simplified positions instead speckles
the surface: TRELLIS.2's sheets are non-manifold and partly doubled, with differently colored voxels on both
sides. `monster_image3d.load_glb_textured` reads vertex colors or a texture (the Space's GLBs). Godot's runtime
glTF loader shows such a GLB white; inspect it with `monster_image3d._rasterize` instead.
Cost: ~80 s model load per `split`, then ~1 min per head; front view only (the back is inferred). Its output has
holes and non-manifold seams, so every reconstruction is rebuilt as one clean solid (`monster_image3d.remesh_solid`:
voxelized, holes closed by the narrowest dilation that lets the inside fill, blurred by `style.head_smooth` voxels,
default 1.5 — raise it for smoother, rounder heads) before it is decimated and colored.

Pipeline rules learned the hard way (each was a bug):
- **Drawings are flat-colored** (`build_monster.FLAT`, in the body, head, hair and accessory prompts): no shading,
  ambient occlusion or cast shadows, designed details (face, muscle lines, seams) as crisp shapes, nothing pure
  white. Lit drawings quantize their shadows into palette colors of their own (a wraith's jaw shadow into its hair
  color) that the per-face coloring paints onto the mesh, and no rule can tell a shadow from a material of similar
  color. TRELLIS reconstructs flat heads just as well. Caveat: restyling in every split edit drifts the design
  (ears, hair, face), so the next step is a flat concept, with the edits only isolating. TRELLIS's own colors are
  no substitute: clean on the orc, noisy and dark on the wraith.
- **Paper-white holes are background:** near-white regions that are mostly pure white are the background seen
  through a chain link or a crown's opening, enclosed or not (`foreground_mask`); counted as the item, they
  became white "gems" and buckles.
- **Per-face colors follow the drawn curves** (`monster_image3d.color_faces`): faces the drawing colors split at
  their midpoints down to `edge` x the part's size (0.03 heads, 0.02 everything else) within 1.25 x the budget,
  each face takes its color, the vertices between two colors slide onto the drawn boundary (the zero of the two
  colors' blurred masks, `snap_to_colors`), and the regions of one color are decimated back with their borders
  kept (`simplify_within_colors`). Cutting edges where the color changes instead stair-stepped every curve inside
  a big face (blocky eyes), and more budget didn't help. Boundary vertices can't be simplified away, so they set
  the face count: the cap keeps busy patterns (bandages, Egyptian belts) near their budgets. Every head gets
  `style.head_face_tris` (3500).
- **Accessory placement:** concept boxes map onto the bare-body drawing anchored on the T-pose shoulder line and the
  feet (headgear on the neck, then moved with the head when it is raised onto the drawn chin). Heights then go
  through the rig's own maps: torso scale above the crotch, the legs' stretched map below it, so hems land where
  the legs are.
- **Shading:** `merge_shades` folds only *darker* same-hue tones into a material (a lighter tone of a similar hue is
  often its own material: tusks, teeth, bone) and never merges one material's main color into another. Drawn
  creases (pecs, belly, collarbone) are too subtle for the palette, so body parts repaint them as lines in the
  skin's shadow tone (`creases`), and the body's color cuts go fine enough to keep them.
- **Belts and crowns are programmatic:** `belt` = a tube plus a buckle plate at the front (a disc for round
  buckles) in the drawing's second color; `crown` = a straight tube resting on the head, flat pyramid points, and
  cut gems on the band when the drawing shows a second color (`_accent_label`). `wrap` stays only for drawn
  patterns (patterned collars, aprons).
- **Shoulder pads** (`dome` on the arm) are pauldrons: a flattened cap tilted outward over the shoulder, its top on
  the drawn top edge, skinned half to the chest and half to the upper arm (on the chest alone, like KayKit's
  Knight, they float off the shoulder when the arms hang; on the arm alone they swing down to the elbow). **Skirts** take the typical lower edge of their drawing as the hem, so a back flap showing
  between the legs doesn't lengthen the whole skirt.
- **Teeth:** on a 3D-reconstructed head the sculpted tusks keep their paint (protected from merging); only simple
  rounded heads get drawn tusks rebuilt as jaw-anchored horns.
- **Feature alignment:** a folding warp is retried stiffer, then its global fit alone; a head whose bake already
  matches (correlation >= 0.8) is left as is, and a failed registration is printed as a WARNING.
- **Reconstruction failures:** a TRELLIS error (free Hugging Face quota) skips that mesh with a message. The build
  falls back to a rounded head with a WARNING, but refuses to build a head whose `hair` has no reconstruction rather
  than produce a bald monster.

Known limits: skirts are skinned to the thighs, so a long stride opens the robe on the stepping leg; emissive
eyes are plain color; the head drawing may draw the jaw a little longer than the body drawing (the head then
sits slightly lower over the shoulders); a painted ear stays visible on the head's side next to the ear add-on. The older
`build_humanoid_monster.py` / `build_golem_monsters.py` / `build_wave2_monsters.py` scripts hand-code geometry
per monster and are superseded; `build_monster.py` still reuses `build_humanoid_monster.py` for the skeleton
template and GLB packer.

## Scripted character kit (replacing KayKit's characters and the reconstructed monsters)

Script-built pieces read better than reconstructions, so characters are moving to a kit in KayKit's own
vocabulary: separate, overlapping, closed pieces per bone (KayKit's `Knight.glb` head is one cranium plus separate
eye, brow, nose and ear shells), one flat color per material slot, colors picked from the concept. Every piece
stays its own named mesh with a stable host, so equipment can later dress and undress a character (a helmet hides
the hair). Package `scripts/accessories/`:

- `kit.py`: the building blocks (`Piece` with per-face slots; hulls, chamfered boxes, sweeps, tubes along
  splines, caps, host bands that follow a host's cross-sections).
- `belts.py`, `crowns.py`, `pauldrons.py`: accessories with `VARIANTS`, fitted to any host's `section(y)`.
- `heads.py`: `Head`, a superellipsoid cranium (jaw, chin, brow ridge, crown sliders) that is also a host, so
  crowns and wraps fit it; face pieces modelled flat and bent onto it (`Head.conform`); recipes (`FORMS`,
  `ROSTER`). Features: eyes (KayKit dots or glowing irises, plus shadowed sockets, eyeliner with lashes, a glow
  halo), brows (bars or tapered arcs), noses, mouths, ears, scars, hair caps, a topknot, mummy wraps, and a `jaw`:
  a smooth patch grown out of the lower face that sinks back into it at its sides and bottom, its mouth line
  drooping at the corners, its `tusks` optional and varied per tooth (pairs, an odd small one, a broken one): an
  orc's with them, a golem's without.
- **Face paint and painted scars** (`paint` strokes: a path, widths, a slot) are thin ribbons projected onto the
  built skin straight from the front (cranium, jaw and nose; lifted along the face each ray hits), so they follow
  the nose and the jaw. Paint stays geometry: one flat color per slot, no texture or UVs, no cutting the cranium.
  `WARPAINT` has examples (a stripe down the face, under the eyes, a mask).
- `sculpts.py`: hairdos kept from image-to-3D builds (`hair_spiky`, `hair_dreadlocks`) in
  `.agents/skills/assets/sculpts/*.npz`, recolored by slot and refitted to any head: each hair point keeps its
  direction (in the head's proportions) and its height over the old head's real surface. The scalp under a
  sculpted hairdo is painted in its color (the old head's painted scalp, plus gaps the hair covers), never given a
  scripted cap as well: the two don't read as one hairdo. Heads stay scripted (a reconstructed orc head did not
  fit the kit).
- `body.py`: KayKit's split on the rig's joints (`JOINTS`): `Body` (a `Trunk`, bean-shaped like KayKit's torsos:
  `TORSOS` kaykit, hero, brute, round, skinny), `ArmLeft`/`ArmRight` with the hand (`ARMS` normal, thin, muscular,
  bony; hands mitten, fist, claw, bony), `LegLeft`/`LegRight` with the foot (`LEGS` normal, stocky, thin; feet
  boot, bare, paw). Torso and limbs are hosts too; a limb is modelled along its own vertical axis and placed by its
  frame, so a bracer or a boot is a band on it.
- `clothes.py`: garments as shells a `thickness` off a host, flaring at either end (tunics, breastplates, sleeves,
  boot shafts), a `robe` running on into a bell skirt (straight or `ragged` hem), shorts and pants, cuffs and
  bracers, collars (studded, Egyptian broad collar), an apron, a mummy's wraps, shackles with a broken chain.
- `armor.py`: torso armor from separate pieces (each its own mesh), fitted over whatever the torso already wears:
  plates with raised trim and rivets whose relief is chiseled from separate chunky pieces (`figure`: male pec slabs
  and a six-pack of blocks, a brute's bigger `muscle`, `female` faceted spheres, a `plain` ridge, the back's blade
  slabs and spine) — KayKit models shape with distinct hard-edged pieces, never smooth bulges on a dense grid
  (`SCULPTS` keeps the smooth reliefs for quilting and the like), `lames` (a fauld or a
  whole segmented cuirass), `mail` (ridged rows, scalloped hem), `scales`, `quilted` (a gambeson), `tabard`,
  `straps` with a ring boss, `fur_collar`, `emblem` (sunburst, cross, skull, gem) and `spikes`; a two-part
  `knight` breastplate (V neckline, chevron seam, faceted plates on coarse grids), `faulds` (a plate skirt),
  `limb_plate` (vambraces, rerebraces, greaves with gold bands and straps) and `cop` (elbow and knee caps). `VARIANTS` combine
  them: knight, paladin, maiden, segmented, crusader, scale, gambeson, barbarian, brigandine, dark knight.
- **Bare torsos are slimmed** (`body.SLIM`, 0.86 across x 0.92 deep): the presets were shaped like KayKit's
  dressed bodies, and the kit dresses them with real garments and armor on top. A recipe's torso sets its own
  `slim` (a number or [across, depth]) for a bulkier or skinnier type.
- **Belts are pulled taut** round what the torso really wears at their height (`clothes.Fitted`: the convex hull
  of the body and the garments so far, used by `kit.ring_points` when a host has an `outline`), not round the
  worst-case layer stack `Worn` records, so they never float.
- **Torsos keep KayKit's shape:** a rounded box about as wide at the shoulders as at the hips with a flat belly
  (`TORSOS`); a pear widest at the belly reads as overweight. Only `round` is meant to look fat.
- **Exaggerate accessories like the concepts do:** buckles, skulls, crown points, pauldron spikes, studs and rivets
  are drawn larger than life (`buckle_scale`, `point_scale`, `spike_height`, `stud_radius`); realistic sizes look
  timid next to KayKit-proportioned characters.
- `cuirass.py`: plate armor rebuilt after concept designs (`parts/breastplate_*`): each plate one closed shell
  whose edges roll up into rounded rims colored as trim (its own faces), sculpted by displacing the same surface,
  riveted along the rims' crests; overlapping plates (a chest plate over a belly plate) are separate shells;
  raised seams (`ridge`) lift the surface and recolor it. Variants `knight` (chest plate with keel and chevron,
  belly plate, back plate, a flared skirt with a front notch; mail shows at the sides), `corset` (cups, pointed
  waist, seams under the cups and down the panels, laced sides, skull clasp), `orc` (uneven bolted iron plates,
  jagged edges, skull boss, straps over the shoulders). Garment kind `cuirass`. The older `armor.plate` still lays
  trim bands on top: move remaining users to `cuirass.plate`.
- `helmets.py`: helmets as separate pieces over a head: `bear` (KayKit's barbarian hood: ears, snout, nose, a
  zigzag of teeth round the rim), `horned` (a riveted viking helm with a nose guard; `horns=0` an iron half-helm,
  `guard_upright` keeps the guard plumb under a heavy brow), `headdress` (a shaman's war bonnet: horned animal skull,
  a fan of two-toned feathers splaying out toward the temples) and `wizard` (a crooked pointed hat, drooping brim,
  a band with rune plates).
- **KayKit-level detail, kit-wide:** organic parts are rounded, never boxy: `kit.rounded_box` (a superellipsoid)
  for fists, boots and flat noses, finer `kit.lump`s, heads at 32x16 and bodies at 28x16 segments. Noses include
  `teardrop` (KayKit's knight) and `flat` (its barbarian); `round` ears are cups with a darker inner fold; `bar`
  brows are soft rounded bars. Crowns have rims, rivets, finials and set gems; belts can `swell` taller at the
  front behind a stepped octagonal `boss`. `kit.extrude` builds any outline, concave included (bat wings: one
  membrane with scalloped edges between finger bones).
- **Heads are one smooth form** (`heads.Head`): `bite` pushes the face below the `mouth` line forward in a step
  `edge` tall (an underbite that is part of the head, never a bolted-on jaw block), `relief` sculpts mirrored
  bumps and dents (brow ridge, sunken sockets, cheekbones, muzzle), `shade` adds KayKit's gradient as flat bands
  (a lighter crown, darker under the jaw; edges follow mesh rings). Reusable face parts: `chevron` nose (rounded
  cross-sections deepening to a broad V-shaped tip: a solid wedge in profile, no separate nostril balls), `bushy`
  brows (ragged top, arch; keep them shallow, deep brows read as a cap's peak),
  two-tone `tusks` (`tip`, `split`), `tuft` hair (stacked faceted clumps), ears lofted as rounded leaves (`heads._ear_shape`: soft rims,
  thinning to the tip, the inside a sunken bowl colored by whole quads, never a patch on top) with `rings` through
  the lower rim and an optional rounded `notch` in the upper rim of one or both ears, and `WARPAINT` patterns any head can wear by name (`paint={"strokes":
  "war"}`, mixable with own strokes). Hair, brows, fur, feathers and cloth export matte (`export.MATERIALS`).
- **Never fake an inset or a color by laying a shape on top of a mesh.** Colors are the mesh's own faces, cut
  along the outline (`kit.paint_cut`; `kit.carve` also sinks them into a real recess); a socket, a mouth or a
  nostril is a dent in the surface, never a dark plate on it. Separate pieces are for things that are separate
  objects (eyeballs, teeth, brows, buckles, rings), not for coloring or for holes.
- Applied kit-wide: open mouths are carved recesses with their lips cut into the skin; line mouths are creases cut
  in; eyes are one eyeball shell with iris, pupil and glint cut into its own faces, sockets/halos/eyeliner cut
  into the skin around them; `wide`/`snub` nostrils are carved dents; the lower lip on an underbite is a rounded
  band cut into the step (`lip`); the Egyptian apron is one plate with cut panels. Teeth and tusks are two-tone by
  default (`tusk` root, `tusk_tip` point). `q_low` squares off a head's lower half (a blunt chin). The golem's
  moss stays an overlay: it reads as a real layer of growth with depth (the user's call).
- **Moving things on a face:** every feature takes `move` = [dx, dy] (face shares; paired features move apart for
  dx > 0), and `heads.layout` spaces features automatically before building (`LAYOUT`: a mouth gives way below the
  eyes and the nose); what it moved is on `head.layout`. Brows may overlap the eyes and a hooked nose may hang over
  the mouth on purpose, so those pairs don't count.
- **Attached parts sink into the skull:** a nose's back, an ear's root, a horn's base and a bun run on into the
  head (`nose(embed=...)`), so the rounded skull never shows a gap around them.
- **Paint and scars are cut into the skin, never raised:** `paint` strokes and `scar`s become outlines that
  `kit.paint_cut` cuts into the cranium, jaw and nose, recoloring the faces inside (a jaw's mouth crease too).
- **Skin under snug clothing is removed** (`characters.hide_covered`): skin and cloth are skinned slightly
  differently, so leftover skin pokes through in deep poses. Garments take their weights from the character's own
  skin right under them (`rig.body_skin`), so cloth moves with what it covers. Pants close at the crotch.
- Faces: `mouth(style="open")` is a sculpted mouth: a dark opening tapering shut at the corners, raised lips, and
  `upper`/`lower` teeth fitted inside it, following its curve (`row`: one white band notched into teeth, never
  separate blocks; `fang`, `needle`, capped in width; `crooked`): an imp's grin, a ghoul's needles, a zombie's
  gappy jaw. Eyes take `cut="triangle"` (slanted top meeting a straight lower edge in a sharp inner corner,
  a devil's) and `pupil="slit"`.
- Monster parts added for the roster: head `horns`, `uneven` eyes, line-mouth `teeth`, `hooked` nose, `leaf` ears with
  an earring; `ribs`, `necklace` (fangs and beads), bat `wings` and a `tail` (rigid on the chest and hips: the rig
  has no bones for them), a robe hem `trim`, a `high` wizard collar, pauldrons and boots on one `side` only. Heads also grow a `beard`
  (sideburns to the temples, tufts, a jagged point, a mustache), part of the base like hair.
- `characters.py`: whole characters, each a recipe in its own folder (`characters/<family>/<name>/character.json`:
  head preset or recipe + body recipe + outfit, a list of garments and accessories by `kind`, `GARMENTS`), built
  as KayKit's named pieces plus one piece per garment, so a character can be dressed and undressed. Garments are
  worn in order: each one grows its host (`clothes.Worn`, layers accumulate), so a belt goes over the robe and a
  cape over a backplate instead of z-fighting them; list base layers first. A recipe can extend another (`base`):
  `heroes/barbarian_base` (bearded head, body with bare feet) is dressed as `heroes/barbarian` (hood, cape, tunic,
  fur trim, baldric, belt, bracers, pants, boots) and as `heroes/knight` (mail, plates, horned helm).
- **Design by drawing, build by script, check by silhouette** (`match.py`): render a character (or some of its
  pieces) front and side on white, have the image model redraw it as fantasy game art with the same camera and
  framing, redesigning only the part in `--brief` (one paid call; reuse it with `--reference`), then rebuild what
  the drawing shows as script features and compare: the overlay is red where only the drawing has shape (grow
  there) and blue where only ours does (shrink there), with the overlap per view. `match.tune()` then searches the
  piece's numeric parameters for the best overlap (the horned helm's horns went from 81% to 92% this way).
  `--regions` also matches the inside: the drawing's flat colors clustered and each joined to the slot covering
  most of it, scored per slot. Whole-character drawings drift (the model shrinks heads, lowers arms despite the
  prompt's locked T-pose), so for full outfits compare side by side by eye and keep silhouette scores for single
  parts. Image models design well; the script keeps the result clean, low-poly and dressable.
- **Another hairdo = a generator job:** build a throwaway character whose head part is `image3d` with that `hair`
  (separate hair mode, flat drawing), then `sculpts.harvest()` its `<Name>_HeadDetails` mesh (cut by face color,
  with `<Name>_Head` as the head it was made on; `reach` trims leftovers of the old head's ears). Script a hairdo
  only when it is a simple shape (a cap, a bun).

```bash
cd .agents/skills/assets/scripts
python accessories/catalog.py belts crowns pauldrons -o accessories.png       # contact sheet, one view
python accessories/catalog.py heads sculpts -o heads.png --yaws 0,-40,-90,180  # four views per variant
python accessories/catalog.py body characters -o characters.png --yaws 0,-35,-90,180
python accessories/match.py heroes/knight --pieces Head,Helmet --brief "the horned viking helmet" -o out/
```

`kit.Frame` refuses a left-handed (mirroring) frame: it would turn every piece placed with it inside out; mirror a
piece with `Piece.mirrored()` instead.

### Character pipeline (`character_kit.py`): sources, export, import

Every character is a folder of sources next to this skill, outside Godot's asset tree (Godot never scans
dot-folders), and exports into the game's assets, one folder per family like the old monsters:

```
.agents/skills/assets/characters/<family>/<name>/
    character.json   recipe + export settings (template, scale, weapons, base, concept)
    reference.png    the concept drawing (`reference`: KayKit's mannequin in the idle pose, redrawn; a paid call)
    preview.png      the drawing, then the build in KayKit's idle pose (front, side) and in the T-pose
    lineup.png       the exported GLB in Godot beside the KayKit model it replaces, in three of the game's clips
    weapons.json     cached meshes of baked weapons (dumped from the game's items; delete to refresh)
-> assets/custom-heroes/<name>.glb               (family `heroes`)
-> assets/custom-monsters/<family>/<name>.glb    (every other family)
```

```bash
cd .agents/skills/assets/scripts
python character_kit.py list                                   # characters, templates, references, exported?
python character_kit.py new heroes/elf --base heroes/barbarian_base
python character_kit.py reference goblin/goblin --brief "turn this mannequin into a sneaky goblin with ..."
python character_kit.py preview goblin/goblin                  # iterate on character.json against the drawing
python character_kit.py export goblin/goblin heroes/barbarian  # GLB + .import, Godot import, structure check
python character_kit.py lineup goblin/goblin                   # windowed Godot render beside the template
```

**An export is a KayKit drop-in, built on a real KayKit GLB** (`accessories/export.py`, `TEMPLATES`): the game
plays clips baked into the model's own AnimationPlayer (`player.tscn` and the enemy scenes' AnimationTrees name
KayKit clips: `Idle`, `Walking_B`, `Idle_Combat`, `Walking_D_Skeletons`...), finds `Rig/Skeleton3D`, and the class
resources look props up by name (`1H_Axe`, `Barbarian_Hat`...). So the template's `Rig`, 41 joints, skin and clips
are copied unchanged and only the meshes are ours: pieces skinned with the mannequin's weights in the rig's bind
space. Clothing is **soft or hard**. Anything worn round the body is soft and blends like skin, its copied weights
smoothed across its surface (`rig.smooth_weights`: vertex-by-vertex copies leave seams where neighbours took the
hips and a thigh, and the cloth tears there); a rigid belt would part from the waist when it bends. Hard things
never bend, since even one shared blend of two bones shears a rigid shape: things fixed at one place
(`rig.HARD`: helmets, wings, tails) and the bits hung on a band (`rig.TRIMMED`: a belt's buckle and studs, a
necklace's charms) ride the single bone that carries most of them; rings on a limb (`rig.LIMB_HARD`: fur cuffs,
bracers) ride one bone per side. Garments take their weights from the body part they belong to
(`rig.WEIGHT_SOURCES`; a limb garment is named `Arm…`/`Leg…` and weighted to the limbs alone). (size and orientation match KayKit exactly), headgear and capes in the template's hat and cape prop nodes
(rigid on `head` / `chest`), the template's other props emptied to a speck (Godot only makes a named
`BoneAttachment3D` for a prop node with a mesh; the game strips them anyway), weapons in new nodes on the hand
slots, `scale` on the `Rig` node. Templates: `skeleton` (Skeleton_Warrior, 95 clips) for monsters,
`barbarian`/`knight`/`mage`/`rogue` for heroes (76 clips, that class's prop names; monster templates' props are
renamed after the character). Metal and glowing slots get their own materials (`export.MATERIALS`, a recipe's `materials` overrides: gold, metal,
rivets, armor shine; glow, halo, crystal emit), and baked weapons keep each surface's metallic and roughness
(`dump_item_mesh.gd` records them), so a gold warhammer still reads as gold. The `.import` copies the template's
parameters: `gltf/naming_version=1` (with 2,
Godot turns props into plain children of a bone attachment named after the bone, and the classes lose their
attachment points) and the template's looping clips (glTF has no loop flag). `inspect_character.gd` checks an
import; `render_character_lineup.gd` renders one under studio lighting (a sky to light and reflect, a shadowing key
light, fill and rim lights, filmic tonemapping, ambient occlusion; rendered transparent and laid over white, since
tonemapping greys a white backdrop): flat lighting hides metal.

The KayKit Character Animations pack (`assets/kaykit-character-animations/`, its `Rig_Medium` armature and renamed
clips) is for previews and posing (`rig.py`): the game doesn't use it yet. `Rig_Large` characters (golems) have no
KayKit template with the game's clips, so they preview but don't export yet. Old monsters at the same paths
(`orc/orc_king`, `undead/mummy`, `undead/wraith`) are overwritten by an export: re-export those deliberately.

**Large creatures (`accessories/golems.py`, `accessories/rig.py`).** Golems sit on `Rig_Large`, read straight from
the animation pack by `rig.Rig` (rest transforms, clips sampled at a time). Every boulder is bound rigidly to one
bone: stone doesn't bend, so there are no weights to paint. A golem is **authored in the rig's idle pose**
(`Idle_A`, frame 0), where a concept drawing maps directly onto it, then moved back to the rest pose bone by bone
(`Rig.pose(..., inverse=True)`); the right side is the left mirrored *in the rest pose* (the idle isn't symmetric).
Shoulder slabs ride a helper bone turned half as far as the upper arm (`HELPERS`), so they stay upright beside the
head. Boulders are `rock()` (chamfered, jittered, slightly bulged blocks); moss is a thin overlay of noise-contour
blobs (`moss()`), so the boulders stay low-poly (~4k tris for the whole golem). Compare against the drawing in the
idle pose, not the T-pose: concept drawings never hold a T-pose.

**Small creatures (goblins, kobolds).** The pack has no small rig: only `Rig_Medium` and `Rig_Large`. Small
creatures are kit characters on `Rig_Medium`, scaled down at export like the existing goblins (`rig_scale = 0.74`),
so all of the medium clips play. To compare a kit character with an idle-pose drawing, `rig.skinned()` skins its
pieces with weights copied from KayKit's own mannequin (nearest mannequin vertex of the same piece, `transfer_weights`;
garments search the whole mannequin) and poses them. Draw the reference over the mannequin in the idle pose
(`character_kit.py reference`). `characters/goblin/goblin` is the example: `leaf` ears, a `hooked` nose (a wedge set rigidly on the face: features that stick out far must not
be bent onto it with `conform`), a grin with `teeth`, the `goblin` pot-belly torso, a `loincloth`.

**Weapons are baked into the exported monster GLB**, rigidly on the `handslot.l`/`handslot.r` bone, following the
held-equipment rule in Gotchas: small creatures keep **full-size** weapons (counter-scaled by `1 / rig_scale`, a
goblin's sword reads a little comically big, on purpose); huge creatures (an ogre, a golem) get **custom-made large
weapons** fitted to their hands, never standard weapons scaled up.

## Gotchas

- **Binary repo files may be Git-LFS** — fetch via `raw.githubusercontent.com` (the scripts do);
  it resolves LFS transparently. `?download=true` resolves HF LFS similarly.
- **Triangle count is the quick low-poly filter** on Poly Pizza; KayKit-grade props are roughly
  hundreds-to-low-thousands of tris. A 70k-tri "skeleton" will not match.
- **CC-BY is not free of obligations** — attribution is legally required in shipped builds.
  Prefer CC0; record attribution when you don't.
- **Tiny palette-atlas textures need lossless + no mipmaps.** Many low-poly models (Quaternius is
  32×32) color faces by sampling flat cells from a minuscule atlas. Godot's default 3D texture
  import (`compress/mode=2` VRAM/BC + mipmaps) wrecks these — BC's 4×4 blocks straddle palette
  cells and mipmaps blend neighbours, so the "skin" comes out muddy/wrong. Set `compress/mode=0`
  and `mipmaps/generate=false` on the atlas `.import` and reimport. (Large UV textures like
  KayKit's 1024² are fine with the default.)
- **Don't commit the scratch venv or downloaded archives.** Keep `.venv/` and any zips out of git.
- **Never scale held weapons or shields with monster stature (`build_humanoid_monster.py`).** When authoring smaller monsters (like Goblins at `rig_scale = 0.74`) on the shared KayKit `Rig`, always counter-scale held weapons around the hand grip origin `(0, 0, 0)` by `1.0 / rig_scale` so they remain at **true `1.0x` full world-space size** (avoiding the "shrunk-down big monster" look). Only helmets/headgear are fitted to the creature's head; huge/boss monsters must use custom-authored large weapons rather than scaled-up standard weapons.
- For Godot import/scene/resource specifics, defer to the `godot-mcp` skill.

