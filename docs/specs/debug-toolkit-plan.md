# Shared debug toolkit

Status: slice 1 landed: the in-game debug menu hosts six tools ([guide](../../scenes/debug/README.md#in-game-debug-menu)). Slices 2–5 remain. Before this plan, the debug menu, the [combat harness](../combat-harness.md) and the [debug harnesses](../../scenes/debug/README.md) each implemented their own item, spell, monster and display controls. This plan moves those controls into one toolkit. The game and every harness host it, so a harness only stages a scene and adds the readouts specific to it.

Goal: every debug action exists once, as a typed method that runs through production code. Menu clicks, harness scenarios and tests all call that same method. Adding an item, monster, spell or class needs no harness edit.

## Current duplication

| Capability | Debug menu (game) | Combat harness | Elsewhere |
| --- | --- | --- | --- |
| Items | Every `.tres` under category folders; quality Random/forced, Cursed; wands and staves per spell | 13 hand-listed weapons in `combat_harness.tscn`; no armor, jewelry, quality or curses | `ItemLifecycleHarness`: hand-listed definitions, spawn and fill backpack |
| Spells | `Cast spell` at a chosen level via `SpellCaster.CastImmediate` | Own spell nodes instanced from a pool, seeded per spell, cast queue | Four elemental previews re-stage spell visuals by hand (written before spells were playable) |
| Monsters | Kill all | Discovery with `MonsterSceneValidator`, practice dummy, AI/defense/invulnerable/rewards settings, arena navigation | — |
| Display | Debug meshes, navigation, combat/AI logs, x-rays, dark level | Hit volumes, hurtboxes, effects, slow motion, camera shake | Spell previews: dark lighting, wall arrangements |
| Hero | Kill player | Reset health, guard, dodge, attack | No way to set a class or a build in any scene |

The harness picks things by list position (`equip 6`, `spawn 1`), so scenarios and tests break whenever a weapon or monster is added. `TestFactories.SpawnHarnessMonster` works around this by replacing the roster.

## Design

### Tools

`DebugMenu` becomes a host. Each capability is a `[GlobalClass]` child node, `DebugTool`, wired into the menu's `Tools` export. A tool owns one submenu and exposes typed methods. Its menu entries only call those methods.

```csharp
// scenes/ui/debug/DebugTool.cs
[GlobalClass] public abstract partial class DebugTool : Node
{
	[Export] public string Title { get; set; }                 // submenu label
	public DebugMenu Menu { get; internal set; }               // runtime: set by the host before BuildMenu
	public virtual bool IsAvailable => true;                   // false hides the submenu (missing export, no level)
	public abstract void BuildMenu(PopupMenu menu);            // once, after Menu is set
	public virtual void RefreshMenu(PopupMenu menu) { }        // each time the debug menu opens: checks, disabled items
}

// scenes/ui/debug/DebugMenu.cs (moved from scenes/ui/)
public partial class DebugMenu : HBoxContainer
{
	[Export] public DebugTool[] Tools { get; set; } = [];
	[Export] public Node World { get; set; }                   // must implement IDebugWorld; replaces ActiveLevel
	public IDebugWorld DebugWorld { get; }                     // null when World is unset or wrong: world tools unavailable
	public Level Level => World as Level;                      // level-only actions (reveal, x-rays, restart, seeds)
	public Player Player { get; }                              // live "player" group lookup; null when dead or respawning
	public T GetTool<T>() where T : DebugTool;                 // throws when the scene does not wire T
}
```

- Each tool owns its own `PopupMenu`, so item ids only need to be unique within that tool.
- Outside debug builds the menu stays hidden and builds no submenus. Tool methods still work, because harnesses and tests call them directly.
- A failed call returns `false` or `null` and changes nothing; the reason is reported with `GD.PrintErr`. Menu clicks show the same message.

| Proposed tool | Absorbs | Public surface |
| --- | --- | --- |
| `ItemSpawnTool` | Spawn item, Spawn quality, Cursed, wand/staff spell submenus, lifecycle harness fill | `Quality` (`ItemQuality?`, null = roll like a drop), `Cursed`, `Item Create(Item definition, SpellDefinition spell = null)`, `bool Drop(Item)`, `bool Give(Item, bool equip)`, `void FillBackpack(Item)`; a Drop/Backpack/Equip delivery choice in the menu |
| `SpellCastTool` | Cast spell (menu), harness spell list and cast queue | `bool Cast(SpellDefinition, int level)`, `ICombatSpell Resolve(SpellDefinition)` (the innate instance, for tests) |
| `MonsterTool` | Harness discovery, dummy, AI/defense/invulnerable/rewards, Kill all enemies | `AIEnabled`, `DefenseMode` (`EnemyDefenseMode`), `Invulnerable`, `RewardsEnabled` (default off), `Count` (ring of 1/3/5), `Node3D Spawn(PackedScene scene, float distance)`, `void Clear()`, `void KillAll()`; `PracticeTargetScene` export listed first when set |
| `HeroTool` | Kill player, harness reset | `void Reset()`, `void Kill()`, `bool ApplyBuild(HeroBuildProfile)`, `bool ChangeClass(CharacterClass)`; build presets and class picker submenus |
| `CombatDisplayTool` | Debug meshes, combat/AI logs, harness volumes, hurtboxes, effects, slow motion, camera shake | One `bool` property per toggle; applies to existing and later-spawned actors |
| `LevelTool` | Navigation, x-rays, dark, reveal map, restart, copy seed, replay run | Methods per entry; only `SetDark` works without a `Level` |

Spawned monsters keep today's harness setup: AI decisions are off unless `AIEnabled`, riposte is off, and they face the player. They get `DebugWorld.Navigation`. They carry no persistent id, so floors never save them. Slice 2 verifies that a kill does not record a cleared entity.

### World contract

Tools never assume a generated floor. The scene's world provides placement, navigation, depth and darkness:

```csharp
// scenes/ui/debug/IDebugWorld.cs
public interface IDebugWorld
{
	uint Depth { get; }                                          // loot depth for rolls
	ulong RunSeed { get; }                                       // added with hero builds: seeds their rolls
	FloorNavigation Navigation { get; }
	bool IsDark { get; }
	void SetDark(bool dark);
	void AddWorldNode(Node3D node, Vector3 globalPosition);
	Vector3 FindFreePositionNear(Vector3 origin);
	Vector3 FindDropLanding(Vector3 source, Vector3 direction, float distance);
}
```

`Level` implements it with its existing members, plus thin forwards to `Run` and `Floor`. The proposed `DebugArena` (`scenes/debug/stage/DebugArena.cs`) is the root of harness arenas. It implements it over a `DebugStage`, building the `ClearanceGrid` from the active arrangement's walls. The grid is rebuilt on `ArrangementChanged`; today `CombatHarness.CreateArenaNavigation` builds it once. Darkness lowers the arena's ambient and key light, like the previews' dark-lighting toggle. The arena's default arrangement must keep today's open 40 m floor, so harness tests keep their geometry. It also adds the walled arrangements that the spell previews offer.

### Catalogs

A plain static `DebugCatalog` (`scenes/ui/debug/DebugCatalog.cs`) is the only discovery code:

- `IReadOnlyList<(Item Item, string Path)> Items(string directory)` loads every `Item` `.tres` under a folder, recursively. It is sorted by name, tier and path, as the menu sorts today.
- `IReadOnlyList<PackedScene> Monsters(string directory)` returns every valid scene under a folder. It skips `templates/` and runs `MonsterSceneValidator`, as `CombatHarness.ScanMonsters` does.
- Both cache per folder for the process. Discovery settles `ResourceCacheGuard` first. The cache keeps the resources alive, which also stops runtime suites from reloading them.

### Hero builds

A build is an authored preset that tools, scenarios and tests apply by path. Presets live in `scenes/debug/hero_build_profiles/`, starting with one per class, e.g. `knight_sword_shield.tres` and `mage_fire_staff.tres`.

```csharp
// scenes/player/classes/HeroBuildProfile.cs
[Tool, GlobalClass] public partial class HeroBuildProfile : Resource
{
	[Export] public string DisplayName { get; set; }
	[Export] public CharacterClass CharacterClass { get; set; }   // null keeps the current class
	[Export] public bool OverrideAttributes { get; set; }         // false uses the class's attributes
	[Export] public float Strength { get; set; } = 10;            // hidden unless OverrideAttributes (_ValidateProperty)
	[Export] public float Dexterity { get; set; } = 10;
	[Export] public float Vitality { get; set; } = 10;
	[Export] public float Intelligence { get; set; } = 10;
	[Export] public HeroBuildItem[] Equipment { get; set; } = [];
	[Export] public HeroBuildItem[] Backpack { get; set; } = [];
}

// scenes/player/classes/HeroBuildItem.cs
[GlobalClass] public partial class HeroBuildItem : Resource
{
	[Export] public Item Definition { get; set; }
	[Export] public int Quantity { get; set; } = 1;
	[Export] public ItemQuality Quality { get; set; } = ItemQuality.Normal;   // equipment with a roll table
	[Export] public bool Cursed { get; set; }
	[Export] public SpellDefinition Spell { get; set; }                       // wands and staves
	[Export] public EquipmentSlot Slot { get; set; } = EquipmentSlot.WeaponHand; // Equipment entries only
}
```

A build replaces the inventory and attributes. Quality is explicit: builds have no Random. Affixes roll from a stream derived from `DebugWorld.RunSeed`, the build's path and the entry index. The same build therefore yields the same items for the same seed. XP level has no gameplay effect yet, so builds leave it out. Fixed affixes are deferred until a bug needs an exact item.

**Class changes respawn the player.** `Player._EnterTree` applies the class, and the model swap only works before `_Ready`, so a build with another class replaces the player. The class is set on the new player node, never on `GameSession`, because autoload state would leak into later test suites. `Player` gains `[Export] public CharacterClass CharacterClass`; `_EnterTree` resolves it as `CharacterClass ??= GameSession.Instance?.ActiveCharacterClass` and applies it as today, which also lets an arena author its hero's class. A class change is a debug convenience: the run's class stays the session's, so saving or taking the stairs restores it. Class switching is the last and optional part of slice 4; builds that keep the current class do not need it.

```
HeroTool.ApplyBuild(build)
├─ validate everything first: definitions load, spells bind (MagicStaff.CanBind), slots valid
│  for each item, backpack fits capacity → on failure return false, player untouched
├─ build.CharacterClass differs from Player.CharacterClass → ChangeClass
│  ├─ instance Player.SceneFilePath with CharacterClass set, same parent and transform
│  ├─ free the old player, then SignalBus.EmitPlayerSpawned(new player)
│  └─ Main (camera, screen effects), Hud, DebugMenu and CombatHarness rebind on PlayerSpawned;
│     invariant: nothing keeps the freed player (IsInstanceValid on every held reference)
├─ apply attributes: class values, or the build's when OverrideAttributes; refill health and mana
├─ replace the inventory: ItemSpawnTool.Create per entry (forced quality, curse, spell),
│  Inventory.Equip(entry, Slot) for Equipment, TryAdd for Backpack
└─ SpellCaster rebinds the equipped focus through the normal equip path
```

### Combat harness after the change

`combat_harness.tscn` becomes a `DebugArena` with the production player and `hud.tscn`. The HUD's debug menu wires every tool, with `World` set to the arena. That gives the arena the mana bar, the hand panels and every item, class and build. `CombatHarness` keeps only what is specific to it:

- the hero action buttons (Attack, Guard, Dodge), because the mouse panel leaves the keyboard to the hero;
- the status line, the monster action tuning panel and the feedback log;
- the trace recorder, the CLI arguments and the scenario runner.

It drops `Weapons`, `SpellPool`, spell instancing, monster discovery and its toggles. It exports the tools it drives (`ItemSpawnTool`, `MonsterTool`, `SpellCastTool`, `HeroTool`, `CombatDisplayTool`) instead of finding them.

Commands name resources by path. The index forms are removed, with no fallback:

```csharp
public bool ExecuteCommand(string command, string path = "", int value = 0, Vector3 direction = default);
```

| Command | Change |
| --- | --- |
| `equip` | `Path`: item definition; `ItemSpawnTool.Create` then `Give(equip: true)` |
| `spell` | `Path`: spell definition; binds it to the equipped wand or staff |
| `spawn` | `Path`: monster scene, empty for the practice dummy; `MonsterTool.Clear` then `Spawn` |
| `build` | New. `Path`: `HeroBuildProfile`; `HeroTool.ApplyBuild` |
| `defense`, `automation` | Set `MonsterTool.DefenseMode` / `AIEnabled`; values unchanged |
| others | Unchanged |

Scenario files gain a `Path` field. `TestFactories` keeps the signatures of `SpawnHarnessMonster`, `GetSpell` and `CastSpell`; their bodies move to `MonsterTool.Spawn` and `SpellCastTool.Resolve`. The roster workaround goes away.

### What stays and what goes

- **Removed:** `ItemLifecycleHarness` (now the game with `ItemSpawnTool`). The fire, earth, air and water spell previews are replaced by the arena, using `SpellCastTool`, a dummy ring, walled arrangements, darkness and real guards against AI shooters. No test loads any of them.
- **Kept:** the room generation harness (it has no actors); the world, actor/item and scroll workbenches (posed subjects and an orbit camera with no gameplay equivalent); the movement preview (`RibbonTrailTest` uses it).
- **Open:** the screen-effect harness. It could become a `ScreenEffectTool` (one toggle per effect, health presets on `HeroTool`), but its intensity sliders do not fit a popup menu.

## Affected files

```
+ scenes/ui/debug/DebugTool.cs, IDebugWorld.cs, DebugCatalog.cs
+ scenes/ui/debug/ItemSpawnTool.cs, SpellCastTool.cs, MonsterTool.cs, HeroTool.cs, CombatDisplayTool.cs, LevelTool.cs
~ scenes/ui/DebugMenu.cs, debug_menu.tscn → scenes/ui/debug/   host only; tools as children; World replaces ActiveLevel
~ scenes/ui/hud.tscn, scenes/main/main.tscn                     new menu path; World = Level
~ scenes/levels/Level.cs                                         implements IDebugWorld
+ scenes/debug/stage/DebugArena.cs                               arena world: stage, navigation, darkness
+ scenes/player/classes/HeroBuildProfile.cs, HeroBuildItem.cs
+ scenes/debug/hero_build_profiles/*.tres                        one preset per class to start
~ scenes/player/Player.cs                                        CharacterClass export, set on a respawned player
~ scenes/debug/combat/CombatHarness.cs, combat_harness.tscn      arena + HUD; harness-only panel; path commands
~ scenes/debug/combat/scenarios/*.json                           paths instead of indices
- scenes/debug/items/                                            item lifecycle harness
- scenes/debug/effects/{Fireball,Earth,Air,Water}SpellPreview.cs, ShowerPreview.cs, FrozenPreview.cs, PreviewTargets.cs, *_spell_preview.tscn, fireball_preview.tscn
- scenes/magic/shared/{Preview,Direct,Chain}TargetStrategy.cs    only the air and water previews use them
~ tests/TestFactories.cs, DarkLevelTest.cs, CombatIntegrationTest.cs, PlayerComboTest.cs, CombatAnimationHandoffTest.cs, SpellWandTest.cs, MeleeContactTest.cs
~ docs/combat-harness.md, scenes/debug/README.md, docs/stats-and-itemization.md, AGENTS.md (harness picker-index note)
```

`EffectPreviewStage`, `EffectPreviewControls` and `DebugStage` stay, because the workbenches and the arena still use them. Slice 5 rechecks every deleted file for other users first.

## Slices

1. **Toolkit in the game.** Add `DebugTool`, the host, `DebugCatalog` and `IDebugWorld` on `Level`. Split today's menu into `ItemSpawnTool`, `SpellCastTool`, `LevelTool`, `HeroTool` (kill only) and `CombatDisplayTool` (debug meshes, logs). Behavior is unchanged; entries move into tool submenus. Verify with `dotnet build`, `DarkLevelTest` (it presses the entry inside `LevelTool`'s submenu), and one test that a forced-quality wand created by `ItemSpawnTool` keeps its picked spell.
2. **Monsters and combat display in the game.** Add `MonsterTool` and move the harness toggles into `CombatDisplayTool`. Verify with a test that a monster spawned on a generated floor, with AI on, reaches the player on the floor's navigation, and that killing it records no cleared entity.
3. **Arena on the toolkit.** Add `DebugArena`, the HUD in `combat_harness.tscn`, the slimmed `CombatHarness`, path commands, the migrated scenarios and `TestFactories`, and the docs. Verify with the harness suites, then capture each scenario and run `scripts/analyze_combat_trace.py --require-complete`.
4. **Hero builds.** Add the profiles, `HeroTool.ApplyBuild`, the `build` command and the presets. Verify with tests that a build equips its items at the authored quality, curse and spell, and that the same seed gives the same affixes. Optionally add `ChangeClass` with the `Player.CharacterClass` export last, verified by a respawn with the new model while the camera, HUD and harness follow.
5. **Retire duplicates.** Delete the item lifecycle harness and the elemental previews, then update `scenes/debug/README.md` and `docs/README.md`. Before deleting, cast every spell in the arena at levels 1, 3 and 5, against the dummy ring with walls and in darkness, to confirm nothing a preview showed is lost.

Slices 1 and 2 already help in play: monster spawning in a real dungeon. Slice 3 is the riskiest, because 37 test files use the harness. Most only read `harness.Player`, which keeps working.

## Open decisions

- **Screen-effect harness:** fold it into a tool and lose the sliders, or keep it.
- **Capturing builds:** "save the current hero as a build" needs fixed affixes on `HeroBuildItem`; deferred until a bug report needs an exact item.
- **Debug menu ergonomics in the arena:** a popup menu takes more clicks than today's always-visible pickers. If spawning and equipping prove slow, add a "recent" section to `ItemSpawnTool` and `MonsterTool` rather than a second set of controls.
