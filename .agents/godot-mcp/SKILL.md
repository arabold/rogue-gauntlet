---
name: godot-mcp
description: >
  Use when working on Godot scenes, GridMaps, mesh libraries, resources, visual
  debugging, or editor/game smoke checks in this project. Do not use for pure C#
  compile-only changes where `dotnet build "Rogue Gauntlet.sln"` is sufficient.
---

# Godot MCP

## Purpose

Use the Godot MCP tools to inspect and validate actual Godot project behavior: scene loading, resource references, mesh-library alignment, editor-authored data, and runtime smoke checks. Keep ordinary C# verification on `dotnet build "Rogue Gauntlet.sln"` unless the task depends on Godot scenes/resources.

## Project Details

- Project path: `/Users/andrerabold/Projects/Personal/rogue-gauntlet`
- Main gameplay scene: `res://scenes/main/main.tscn`
- Project main scene: `res://scenes/menu/main_menu.tscn`
- Local working Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot`
- The MCP server may default to `/Applications/Godot.app/Contents/MacOS/Godot`, which is not installed on this machine.

## When To Use MCP

- Inspect project metadata, scenes, UIDs, or resource references.
- Launch the editor for visual validation of scenes, GridMaps, tile alignment, lighting, camera, or UI.
- Run the project or a specific scene and capture Godot output.
- Create small temporary validation scenes for visual debugging.
- Resave resources or update UIDs after Godot resource/scene changes.
- Export a scene as a mesh library when intentionally changing authored tile libraries.

## Verification Workflow

1. Run `dotnet build "Rogue Gauntlet.sln"` first for C# compile checks.
2. For scene/resource work, use Godot MCP if the configured server path works.
3. If MCP reports `spawn /Applications/Godot.app/Contents/MacOS/Godot ENOENT`, use the local Godot binary through Bash instead.
4. For gameplay smoke checks, run `res://scenes/main/main.tscn` rather than the project main menu when the task needs level generation.
5. Treat headless Godot leak messages on forced quit as shutdown noise unless accompanied by load, script, resource, or generation errors before shutdown.

## Useful Commands

- Compile: `dotnet build "Rogue Gauntlet.sln"`
- Headless gameplay smoke: `"/Applications/Godot_mono.app/Contents/MacOS/Godot" --headless --path "/Users/andrerabold/Projects/Personal/rogue-gauntlet" "res://scenes/main/main.tscn" --quit-after 8`
- Headless project smoke: `"/Applications/Godot_mono.app/Contents/MacOS/Godot" --headless --path "/Users/andrerabold/Projects/Personal/rogue-gauntlet" --quit-after 5`
- Launch editor fallback: `"/Applications/Godot_mono.app/Contents/MacOS/Godot" --editor --path "/Users/andrerabold/Projects/Personal/rogue-gauntlet"`

## MCP Tool Usage

- `godot_get_project_info`: quick check that the MCP server can open the project.
- `godot_launch_editor`: open the editor for visual checks after scene/resource changes.
- `godot_run_project`: run the project or a scene when MCP is correctly configured.
- `godot_get_debug_output`: inspect runtime errors after `godot_run_project`.
- `godot_stop_project`: stop a running MCP-launched game after collecting output.
- `godot_get_uid` and `godot_update_project_uids`: use only when resource UID issues are part of the task.
- `godot_create_scene`, `godot_add_node`, and `godot_save_scene`: useful for small test labs, but prefer existing authored scenes for production content.
- `godot_export_mesh_library`: use only for intentional mesh-library authoring changes.

## Rules

- Do not replace `dotnet build` with Godot's C# solution build on this machine; the Godot CLI solution build is known to time out with engine shutdown errors.
- Do not commit temporary probe scenes/scripts. If a temporary `res://tmp_*` file is needed, delete it before finishing.
- Do not edit `.tscn` or `.tres` blindly when a visual/editor check is needed; launch Godot or create a focused test scene.
- Prefer deterministic smoke checks by running a specific scene and seed when debugging generation.
- Keep MCP work focused on Godot-specific validation; do not use it for general file search or ordinary C# refactors.

## Example: GridMap Wall Debugging

1. Build with `dotnet build "Rogue Gauntlet.sln"`.
2. Inspect relevant mesh-library item names and orientations through a temporary Godot script or editor scene.
3. Run `res://scenes/main/main.tscn` headless and check for load/script errors.
4. Launch the editor for visual alignment if the change affects tile placement.
5. Remove any temporary probe files before finalizing.
