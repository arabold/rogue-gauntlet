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

## Architecture Guidelines
- Prefer Godot composition over inheritance: build actors from focused child nodes/components and authored scenes.
- Keep scenes self-contained. If a child needs outside context, inject it from the parent via exported Node/Resource references, signals, or explicit initialization.
- Use `Resource`/`.tres` for designer-authored data, strategies, factories, item definitions, stats, buffs, loot tables, and other non-visual configuration.
- Use scenes for game-specific runtime objects with node hierarchies, visuals, physics, UI, animation, or editor-authored composition.
- Avoid adding autoload responsibilities by default. `SignalBus` is for broad notifications; `GameManager` is for high-level state/lookup, not feature logic.
- Prefer local signals/resource observation for parent-child or same-feature communication; use `SignalBus` only when sender and receiver are intentionally decoupled.
- Avoid service-locator creep: do not reach into `GameManager.Instance` from reusable components when an exported dependency or parent initialization is practical.
- Avoid hardcoded scene/resource paths in gameplay logic; prefer exported `PackedScene`/`Resource` fields or factory resources.
- Split a class/component when it owns multiple independent reasons to change, e.g. input plus inventory plus combat plus UI signaling.
- Do not split just to add patterns. Keep small behavior in one script until variation/reuse/testing pressure is real.
- Components should own one capability: health, movement, interaction, hit detection, loot dropping, action timing, AI perception, AI navigation, etc.
- Actor root scripts should act as thin facades/mediators for their authored scene, not as the permanent home for every feature.
- Shared authored resources should be treated as definitions. Duplicate/reset them before runtime mutation when state must be instance-specific.
- Prefer C# interfaces for type-safe gameplay contracts (`IDamageable`, `IInteractive`, `IPlayerAction`) where Godot groups alone would imply hidden behavior.
- Keep group usage documented and consistent; group membership is part of discoverability for `GameManager` and gameplay queries.

## Godot/C# Gotchas
- Several editor-time classes use `[Tool]` and exported properties that regenerate data in setters; avoid side effects that break editor instantiation.
- Godot C# partial classes are in the global namespace in existing code despite the csproj root namespace; follow the current pattern unless doing a deliberate namespace migration.
- Keep scene/resource paths lowercase as authored (`scenes/main/main.tscn`); there is at least one stale mixed-case reference in code (`Main.tscn`) that should not be copied.
- Phantom Camera is GDScript-only here; `Main.cs` accesses it via `Node3D.Call("set_follow_target", player)` and property strings.
- `.editorconfig` sets tabs for `*.cs`, CRLF, and no final newline; match local formatting when editing C#.

## Existing Guidance
- `.clinerules` asks to keep `ARCHITECTURE.md` high-level and update it only for architecture changes; do not mirror implementation details there.
- `.clinerules` also asks for useful docs on classes/methods/functions, focused on functionality/reasoning rather than obvious parameter docs.
