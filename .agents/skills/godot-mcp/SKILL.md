---
name: godot-mcp
description: >
  Use when working on this Godot 4.7 C# project's scenes, GridMaps, mesh libraries, or
  `.tres`/`.tscn` resources — including hand-authoring or validating resources, fixing
  UID/import issues, and editor or headless game smoke checks. Covers this machine's Godot
  and dotnet binary paths, the build->import->validate workflow, and a headless GDScript
  validation harness. Not needed for pure C# logic changes where `dotnet build` alone
  verifies the work.
---

# Godot (MCP + headless) for Rogue Gauntlet

## Purpose

Validate real Godot behavior — scene/resource loading, UID resolution, mesh-library
alignment, editor-authored data, runtime smoke checks — and author/edit `.tres`/`.tscn`
correctly. Keep ordinary C# verification on `dotnet build`; reach for Godot only when the
task depends on scenes, resources, or runtime behavior.

## Project setup

- Project root: the repo root. In a git **worktree** the root is the worktree directory.
- Gameplay scene: `res://scenes/main/main.tscn` — run this for level-generation/gameplay checks.
- Project main scene: `res://scenes/menu/main_menu.tscn`.
- Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (the mono/C# build; **not** `Godot.app`). Use `scripts/godot.sh` to resolve it automatically.
- `dotnet` binary: `/usr/local/share/dotnet/dotnet` — it is **not** on the agent shell's PATH.

## Gotchas

Concrete corrections — each is a mistake that happens without being told otherwise:

- **`dotnet` is not on PATH.** A bare `dotnet build` fails with "command not found". Use
  `/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"`.
- **Import before headless-loading any resource.** A fresh or worktree checkout has no
  `.godot/imported/` cache, so loading a `.tres`/scene fails with
  `Unable to open file: res://.godot/imported/...ctex|.scn` plus cascading parse errors that
  look real but are not. Run the import step (below) once first.
- **Wrong Godot binary.** The MCP server may default to `/Applications/Godot.app/...` and
  report `spawn ... ENOENT`. Fall back to the local binary via `scripts/godot.sh`.
- **Worktree path discipline.** When in a worktree, the project root is the worktree dir, not
  the main checkout. Pass `--path "<worktree>"` to Godot and use absolute worktree paths when
  editing, or builds/edits silently hit the wrong tree.
- **One `[GlobalClass]` Resource per `.cs` file.** A `.tres` references a script by file, so
  multiple resource classes in one file make sub-resource typing ambiguous and break loads. A
  plain `enum` may share a file. (Detail: `references/tres-authoring.md`.)
- **New C# scripts need a `.cs.uid` sidecar** that `.tres` files reference; the import step
  generates it. (Detail: `references/tres-authoring.md`.)
- **GDScript can validate C# without a test project.** A loaded resource exposes public C#
  *properties* via `obj.get("PropName")` — even computed, non-`[Export]` ones — and public
  *methods* via `obj.call("Method", args)`, but only when the signature is Godot-marshalable.
  Methods taking `object` or generic `IEnumerable<T>` are not exposed (`has_method` returns
  false); give logic you want to probe simple/Godot-typed signatures.
- **Shutdown noise.** Headless leak/ObjectDB "resources still in use / leaked at exit" messages
  on forced quit are noise unless preceded by real load/script/resource errors during the run.
- **Bulk-generating files via Bash bypasses the Read-before-Write safety check.** Writing many
  new files at once (e.g. a script/heredoc loop creating a batch of wrapper `.tscn`s) is easy to
  reach for over N individual Write calls, but it skips the "must Read before overwrite"
  protection Write gets — a filename collision with an existing (even orphaned/dead) file
  silently clobbers it with no diff shown. Check `git status`/existence for every target
  filename first, or use Write per-file when the count is manageable.
- **A model that renders fine in the item catalog can still be broken when equipped.** See
  "Visual verification: in-hand items" below — floating-and-auto-framed vs. bone-attached are
  different enough contexts that one passing does not imply the other passes.
- **Scenes outside `scenes/debug/` get the standalone bootstrap.** `SceneDebugBootstrap` adds its
  own floor, light and camera to any scene run on its own outside `scenes/debug/`, the main menu
  and `main.tscn`, so a `res://tmp_*` capture never looks like the game. Put temporary capture
  scenes under `scenes/debug/` (and delete them), or instance a scene that already lights like
  the game: the combat arena, a workbench or `main.tscn`.
- **`_ready()` on freshly-added nodes is deferred, not synchronous.** In a `--script` main-loop
  script, `some_node.add_child(x)` does NOT run `x`'s (or an autoload's) `_ready()` before the
  next call returns — it fires on a later frame. Calling logic that depends on a node's own
  `_ready()`-wired references (e.g. `MapGenerator.GenerateMap()`, which needs its GridMap child
  refs) immediately after `add_child` runs against not-yet-initialized state and silently
  produces empty/wrong results, no error printed. Wait at least one `_process()` tick after
  adding the node before calling into it (see `render_level_topdown.gd`'s state machine).

## Core workflow

1. **Compile:** `/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"` (the default check
   for any C# change; do this before touching Godot).
2. **Import** (only after adding/changing scripts or resources, or on a fresh/worktree checkout):
   `scripts/godot.sh --headless --path "$PWD" --import`. This builds the import cache and
   generates `.cs.uid` sidecars.
3. **Validate / smoke-test** the specific thing that changed — a resource load, a scene boot,
   or a logic probe (below). Prefer a targeted scene + seed over the whole game.

## Commands

```bash
# Compile (default verification)
/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"

# Build the import cache + UIDs (run from the project/worktree root)
.agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --import

# Headless gameplay smoke (spawns player + level, then quits after N frames)
.agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" res://scenes/main/main.tscn --quit-after 150

# Launch the editor for visual checks (GridMaps, tiles, lighting, UI)
.agents/skills/godot-mcp/scripts/godot.sh --editor --path "$PWD"
```

## Headless validation harness

Run a GDScript file as the main loop to exercise the project without the editor:
`scripts/godot.sh --headless --path "$PWD" --script <file.gd> -- <args...>`. The script
`extends SceneTree`, does its work in `_init()`, reads args after `--` via
`OS.get_cmdline_user_args()`, and calls `quit()`.

- **Validate a resource loads and reads back correctly** (use after hand-authoring a `.tres`):
  ```bash
  .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --script \
    .agents/skills/godot-mcp/scripts/inspect_resource.gd -- res://path/to/new.tres
  ```
- **Probe C# logic** for a one-off check: write a small `extends SceneTree` script in the
  scratchpad that loads a resource and calls its marshalable methods / reads its properties
  (see the GDScript gotcha above), then run it with `--script`. Do not commit one-offs.

## Gameplay checks in the combat arena

`scenes/debug/combat/combat_harness.tscn` is the game's player, HUD, camera, lighting and stone
floor on an open arena, hosting the same debug menu as the game (see
[`scenes/debug/README.md`](../../../scenes/debug/README.md) and the
[harness guide](../../../docs/combat-harness.md)). Stage gameplay there instead of building a scene:

- **Scripted runs:** JSON scenarios name items, spells and monsters by `res://` path. Run one
  headless with a trace, then check it:
  ```bash
  /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path "$PWD"     res://scenes/debug/combat/combat_harness.tscn -- --combat-seed=42     --combat-log=/tmp/run.jsonl --combat-script="$PWD/scenes/debug/combat/scenarios/combat_smoke.json" --combat-frames=420
  python3 scripts/analyze_combat_trace.py /tmp/run.jsonl --require-complete
  ```
- **Recordings:** add `--fixed-fps 30 --write-movie <dir>/frame.png --quit-after <frames>` before
  `--` on a windowed run to record a scenario, then stitch with `scripts/frames_to_gif.py`.
- **Probes:** from a temporary script, call the harness's `ExecuteCommand("spawn", path, distance,
  Vector3.ZERO)` or the debug tools under `UI/HUD/DebugMenu` (`SpellCastTool.Cast`,
  `LevelTool.SetWalls`, `LevelTool.SetDark`, `MonsterTool.Count`).

## Visual verification: in-hand items

The floating item catalog auto-frames and auto-rotates, which hides two defects that only show
once a model is bone-attached: a local-axis convention the hand's bone does not expect (melee
bones expect the blade along local +Y; raw bow/crossbow assets aim along Z), which can leave the
model edge-on and invisible, and a scale inconsistent with sibling tiers. Verify new weapons and
shields on the rig: `asset-studio/godot/render_held_items.gd` equips each item on a standalone
player and screenshots it from several angles (windowed), and the combat arena equips any item
from **Items** at the game's camera. Each class names its own attachment bones
(`docs/character-classes.md`), so re-check after changing them.

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
  --script asset-studio/godot/render_held_items.gd -- <outdir> res://scenes/items/weapons/axe_rare.tres [more...]
```

## Visual verification: top-down level/room screenshots

Reasoning about tile coordinates can't confirm what a generated level or room actually looks like.
`scripts/render_level_topdown.gd` renders a real top-down screenshot of a generated level instead,
with an overlay drawn straight from `MapGenerator.GetConnectorDebugInfo()` (Godot-native return
types, since `MapData` itself can't marshal to GDScript) showing every doorway connector (yellow)
and its per-direction open (green) / sealed (red) status. This must run **windowed**, not
headless — the headless renderer produces blank images:

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" --script \
  .agents/skills/godot-mcp/scripts/render_level_topdown.gd -- <outfile.png> [seed] [image_size] [depth]
```

The optional `depth` (default 1) picks the realm from `level.tscn`'s realm list, so `5` renders a
Sewer floor and `8` a Jungle one. Single rooms of any realm render through the room generation
harness (`scenes/debug/rooms/room_generation_harness.tscn`): instance it in a windowed `--script`,
call `SelectRealm`, `SelectRecipe` and `Regenerate`, hide its `UI` node and save the viewport.

Then `Read` the resulting PNG to inspect it. Use this whenever a change to room layout, doorway
placement, wall generation, or rotation needs a visual sanity check instead of (or in addition to)
numeric probes/tests — e.g. confirming a doorway's connector tile is flush with its wall gap, or
that a rotated room's doors still stand in their doorways.

## Visual verification: animated GIFs

For motion (tweens, VFX, pickups, animations), record a GIF instead of single screenshots.
`scripts/record_gif.sh` runs a scene **windowed** with Godot's Movie Maker (`--write-movie`), which
forces a fixed frame rate, so tweens play at their true timing however slow the render, then
stitches the frames with `scripts/frames_to_gif.py` (needs Pillow, which the project's `.venv`
has; the script prefers it):

```bash
.agents/skills/godot-mcp/scripts/record_gif.sh res://scenes/debug/tmp_preview.tscn <out.gif> [seconds=2] [fps=30] [resolution=960x640] \
  -- [--skip N] [--width 640] [--crop x,y,w,h]
```

`--crop` takes fractions (0-1) of the frame. Stage the moment in a temporary scene under
`scenes/debug/` (see the bootstrap gotcha above) that instances the combat arena or a workbench,
so it lights like the game, and triggers the action from `_process()` after a short delay (no
self-screenshot or `quit()` needed: `record_gif.sh` stops after the given seconds). A gameplay
moment needs no scene of its own: record a scenario in the arena (see above). Write the GIF
to the scratchpad or a temp dir, never into the project (the script refuses an in-project path), and
send it to the user. To inspect it yourself, `Read` only shows the first frame:
tile a few frames into a PNG contact sheet with Pillow first. Delete the temporary scene afterwards.

## MCP tools

When the MCP server is correctly configured, prefer it for editor/runtime interaction:
`godot_get_project_info` (open check), `godot_launch_editor`, `godot_run_project` +
`godot_get_debug_output` + `godot_stop_project` (run and inspect), `godot_get_uid` /
`godot_update_project_uids` (UID issues), `godot_export_mesh_library` (intentional library
changes). If MCP reports `ENOENT` on `Godot.app`, use `scripts/godot.sh` via Bash instead.

## Bundled scripts

- `scripts/godot.sh` — resolves the working Godot binary and runs it with the given args.
- `scripts/inspect_resource.gd` — loads one or more `res://` resources and prints their script
  properties; reports `OK`/`FAIL`/`MISSING` per path. The reusable resource validator.
- `scripts/render_level_topdown.gd` — renders a windowed top-down screenshot of a generated
  level with a connector/doorway overlay. See "Visual verification" above.
- `scripts/sample_attack_clip.gd` — prints a bone's height, reach and speed through an attack
  clip (headless) to place an attack profile's `ContactStart`/`ContactEnd` on the visible strike.
- `scripts/record_gif.sh` + `scripts/frames_to_gif.py` — record a scene to an animated GIF.
  See "Visual verification: animated GIFs" above.

## Authoring `.tres` / `.tscn` by text

Before creating or editing a `.tres`/`.tscn` in a text editor (rather than the Godot editor),
**read `references/tres-authoring.md`** — it covers `load_steps` counting, enum-as-int and
array serialization, sub-resource layout, the one-`[GlobalClass]`-per-file rule, UID sidecars,
the missing-`res://`-prefix trap, and the wrapper-`.tscn` pattern for correcting a raw model's
grip orientation or scale. Always validate the result with `inspect_resource.gd`.

## Rules

- Do not replace `dotnet build` with Godot's C# solution build on this machine; the Godot CLI
  solution build times out with engine shutdown errors. Use `dotnet`.
- Do not commit temporary probe scenes/scripts. Put one-offs in the scratchpad; if a scene in
  the project is unavoidable, put it under `scenes/debug/` and delete it before finishing.
- Do not edit `.tscn`/`.tres` blindly when a visual result matters; launch the editor or build
  a focused test scene.
- Prefer deterministic checks: run a specific scene and seed when debugging generation.
- Keep this skill focused on Godot-specific validation; do not use it for general file search
  or ordinary C# refactors.
