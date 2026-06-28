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
- **Headless navmesh bake error is expected.** A headless `main.tscn` run prints an error +
  C# backtrace from `NavigationRegion3D.BakeNavigationMesh` (`MapGenerator.BakeNavigationMesh`);
  generation continues and logs `Map generated.` It is a NavigationServer-in-headless
  limitation, not a real failure. Confirm `Map generated.` and that the player spawns; bake
  navmesh visually in the editor when it actually matters.

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

## Authoring `.tres` / `.tscn` by text

Before creating or editing a `.tres`/`.tscn` in a text editor (rather than the Godot editor),
**read `references/tres-authoring.md`** — it covers `load_steps` counting, enum-as-int and
array serialization, sub-resource layout, the one-`[GlobalClass]`-per-file rule, and UID
sidecars. Always validate the result with `inspect_resource.gd`.

## Rules

- Do not replace `dotnet build` with Godot's C# solution build on this machine; the Godot CLI
  solution build times out with engine shutdown errors. Use `dotnet`.
- Do not commit temporary probe scenes/scripts. Put one-offs in the scratchpad; if a
  `res://tmp_*` file is unavoidable, delete it before finishing.
- Do not edit `.tscn`/`.tres` blindly when a visual result matters; launch the editor or build
  a focused test scene.
- Prefer deterministic checks: run a specific scene and seed when debugging generation.
- Keep this skill focused on Godot-specific validation; do not use it for general file search
  or ordinary C# refactors.
