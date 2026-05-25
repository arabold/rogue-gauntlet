# Agent Notes

## Project Shape
- Godot 4 C# game; open/run `project.godot`, main scene is `res://scenes/main/main.tscn`.
- C# project is `Rogue Gauntlet.csproj` using `Godot.NET.Sdk/4.6.3`, `net9.0`, `LangVersion=preview`, root namespace `RogueGauntlet`.
- There are no test projects or CI workflows in this repo currently; use build plus editor/game smoke checks.

## Commands
- Fast compile check: `dotnet build "Rogue Gauntlet.sln"`.
- VS Code task config points to `/Applications/Godot.app/Contents/MacOS/Godot --build-solutions ...`, but this machine has `/Applications/Godot_mono.app/Contents/MacOS/Godot` instead.
- On this machine, the Godot CLI solution build with Godot 4.6.3 currently prints engine shutdown errors and times out; prefer `dotnet build "Rogue Gauntlet.sln"` for agent verification unless debugging Godot/editor behavior.

## Architecture
- Autoload singletons are declared in `project.godot`: `SignalBus` (`scripts/SignalBus.cs`), `GameManager` (`scripts/GameManager.cs`), and Phantom Camera manager.
- `Main.cs` wires camera and inventory UI; `Level.cs` runs `MapGenerator.GenerateMap()` on ready; `MapGenerator.cs` owns room layout, corridor connection, gridmap merge, navigation bake, and spawn-point creation.
- `GameManager` tracks live nodes by Godot groups (`player`, `enemy`, `damageable`); update scene groups when adding actors that should be discoverable.
- Decoupled game/UI updates go through `SignalBus` static emit helpers. Add new bus events there instead of introducing direct cross-system references when reacting to gameplay state.
- Player behavior is component-scene based: `Player.cs` expects named child nodes such as `MovementComponent`, `InputComponent`, `HealthComponent`, `HurtBoxComponent`, `ActionManager`, and attack nodes.
- Data authored in Godot resources matters: items, player stats, dungeon factories, mesh libraries, and rooms are `.tres`/`.tscn` assets under `scenes/`; C# changes often need matching exported properties/resources in scenes.

## Godot/C# Gotchas
- Several editor-time classes use `[Tool]` and exported properties that regenerate data in setters; avoid side effects that break editor instantiation.
- Godot C# partial classes are in the global namespace in existing code despite the csproj root namespace; follow the current pattern unless doing a deliberate namespace migration.
- Keep scene/resource paths lowercase as authored (`scenes/main/main.tscn`); there is at least one stale mixed-case reference in code (`Main.tscn`) that should not be copied.
- Phantom Camera is GDScript-only here; `Main.cs` accesses it via `Node3D.Call("set_follow_target", player)` and property strings.
- `.editorconfig` sets tabs for `*.cs`, CRLF, and no final newline; match local formatting when editing C#.

## Existing Guidance
- `.clinerules` asks to keep `ARCHITECTURE.md` high-level and update it only for architecture changes; do not mirror implementation details there.
- `.clinerules` also asks for useful docs on classes/methods/functions, focused on functionality/reasoning rather than obvious parameter docs.
