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

## Authoring our own (image-to-3D) — experimental

`scripts/hf_image_to_3d.py` runs image-to-3D on HF hardware (TripoSR Space) via `gradio_client`.
Honest scope before you invest:
- Output is **high-poly PBR + unrigged** — needs a manual Blender low-poly/palette pass and
  skinning to the KayKit rig. HF solves compute, not the style/rig gap.
- The GPU step needs **ZeroGPU quota**; a free token usually hits the wall (validated). Reliable
  routes: HF Pro, or duplicate the Space to a dedicated GPU.
- Needs `gradio_client`: `python3 -m venv .venv && .venv/bin/pip install gradio_client`.

```bash
.venv/bin/python scripts/hf_image_to_3d.py concept.png monster.glb 256
```

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
- For Godot import/scene/resource specifics, defer to the `godot-mcp` skill.
