# Game Logging and In-Game Log Feed — Program Design

Status: **proposed**. The unified logging API/feed below is not implemented. The existing [combat harness trace](../combat-harness.md) supplies scene-scoped structured combat JSONL independently; a future unified logging implementation should adapt that event source rather than introduce a second combat trace.

Scope: one logging API for gameplay messages, developer/QA diagnostics and machine-readable traces. Output goes to an optional in-game feed, the Godot console, and an optional JSONL file. Everything is disabled by default.

## Goals and audiences

| Channel | Audience | Example | Shape |
| --- | --- | --- | --- |
| **Player** | Players | `Picked up Longsword +5` | Short, friendly verb phrases without abbreviations (see [Player message style](#player-message-style)); names match what the UI shows |
| **Internal** | Developers, QA | `Skeleton2 lost sight of Player; searching for 4s` | Readable but terse narration of system decisions (AI, combat, doors, generation, saves); node names and units are fine |
| **Debug** | Automated tests, AI agents | `ai.target_lost actor=Skeleton2 dist=7.42 los=false` | A stable event ID plus `key=value` fields, invariant culture, one event per line |

Requirements carried from the request:

- All channels are off by default. Each channel has its own toggle; channels are not ordered verbosity levels. You can enable Player without Internal, for example.
- When enabled, entries appear in an in-game feed: one per line, new lines at the bottom so older lines move up, color-coded by channel/severity, fading out after about 5 s.
- Every entry shown in the feed is also printed to the Godot console.

## Current seams

- [`GameDebug`](../../scripts/GameDebug.cs) is a static switchboard with `CombatLogsEnabled`/`AiLogsEnabled` and `Combat()`/`Ai()` helpers that `GD.Print` when enabled. Callers: `AttackController`, `Projectile`, `EnemyBehaviorComponent`, `SearchingState`, `EnemyContext`. It also owns the x-ray flags. Those flags are unrelated to logging and stay where they are.
- [`DebugMenu`](../../scenes/ui/DebugMenu.cs) (debug builds only, child of `hud.tscn`) has "Combat logs" and "AI logs" check items that set the `GameDebug` flags.
- About 73 unconditional `GD.Print` calls, for example `HealthComponent` damage/heal, `Door` open/close, `TriggerComponent`, `BoneAttachmentManager`, `GameSession` transitions/saves. They are always noisy and cannot be turned off. About 80 `GD.PrintErr`/`PushError`/`PushWarning` calls report real failures.
- [`SignalBus`](../../scripts/SignalBus.cs) already broadcasts most player-relevant events: `ItemConsumed/Equipped/Unequipped/Dropped/Destroyed/Identified`, `XpRewarded`, `LevelChanged`, `PlayerDeath`. There is **no pickup event**: [`PlayerInventoryController.Acquire`](../../scenes/player/PlayerInventoryController.cs) commits pickups without a bus notification. `ItemIdentified` carries only a `typeId` and fires after identification, so the disguise name is no longer available to observers.
- Display names that respect identification come from `IdentificationService.GetDisplayName` through `ItemIdentity`.
- No user settings persistence exists yet (only `user://saves`). There is no options screen; [`PauseMenu`](../../scenes/ui/PauseMenu.cs) has only resume, save and quit buttons.

## Design decisions

1. **A static `GameLog` facade in plain C#, with no autoload or Node.** Logging is cross-cutting and holds no gameplay state. It must work in plain-C# GdUnit tests, in `[Tool]` code, and before the scene tree exists. It replaces the existing static `GameDebug` log helpers, so no new global pattern is introduced. Outputs are pluggable sinks. The UI feed is a sink that registers on `_EnterTree` and unregisters on `_ExitTree`, so `GameLog` never references scenes.
2. **Not routed through `SignalBus`.** `LogEntry` is a C# record with structured fields, not a Variant. Routing logs through the bus would also make the bus noisy. `SignalBus` stays the *source* for player messages (decision 4), not the transport.
3. **Channel + category + severity.**
   - `LogChannel` (Player/Internal/Debug) is the audience and has a toggle.
   - `LogCategory` (Combat, Ai, Items, Level, Save, Ui, General, …) is a secondary filter. It replaces the bespoke Combat/AI flags. All categories are enabled by default once a channel is on.
   - `LogSeverity` (Info/Warning/Error) affects color only. Warnings and errors **always** reach the console (through `GD.PushWarning`/`PushError`, so they keep editor stack traces), even when every channel is off. They show in the feed only when the Internal channel is on.
4. **Player messages are derived from bus events by a narrator node, not written inline in gameplay code.** `PlayerLogNarrator` subscribes to `SignalBus` and formats prose. This keeps strings in one place (future localization), keeps names identification-aware, and keeps gameplay components free of UI text. Internal and Debug entries are logged inline at the source, because they describe internal decisions that have no bus event.
5. **No cost when disabled.** `Player`/`Internal` take a custom `[InterpolatedStringHandler]`, so `$"..."` arguments are not formatted when the channel or category is off. Debug fields use a small typed `LogField` (string/long/double/bool/Vector3) and are formatted only when emitted. In hot paths, guard with `GameLog.IsEnabled(...)` before building fields.
6. **Machine-friendly debug format.** Each line has a fixed prefix, a stable dot-separated event ID, `key=value` pairs in invariant culture with fixed float precision (2 decimals unless the field specifies otherwise), the physics frame and game time. Console output stays plain text without BBCode/ANSI, so `grep` and agent parsing stay reliable.
7. **Configurable without the UI.** Godot user command-line args (`OS.GetCmdlineUserArgs()`) let QA and AI agents enable channels in headless or scripted runs.
8. **Toggles persist in user settings.** Channel and category toggles are saved to `user://settings.cfg` (a Godot `ConfigFile`) and restored at startup. Command-line flags override them for that session only and are never written back, so an agent run does not change a developer's or player's saved preferences.
9. **All channels in all builds, for now.** Internal, Debug and the JSONL file are available in exported builds as well, so QA can capture logs from export candidates. Restricting them in release builds is a [pre-release follow-up](#follow-ups-before-public-release), not part of this plan.
10. **Three voices.** Player entries are short, friendly message-log lines, held to the [Player message style](#player-message-style) rules. Internal entries are short developer sentences, where node names, units and abbreviations are acceptable. Debug entries are machine records. The narrator alone writes Player text. Gameplay code never writes Player prose inline.

## Contracts (abbreviated, proposed names)

```csharp
[Flags] public enum LogChannel { None = 0, Player = 1, Internal = 2, Debug = 4 }
public enum LogSeverity { Info, Warning, Error }
public enum LogCategory { General, Combat, Ai, Items, Level, Save, Ui }

/// Typed key/value pair; formatted lazily with invariant culture.
public readonly struct LogField
{
	public static LogField Of(string key, string value);
	public static LogField Of(string key, long value);
	public static LogField Of(string key, double value, int decimals = 2);
	public static LogField Of(string key, bool value);
	public static LogField Of(string key, Vector3 value, int decimals = 2);
}

public sealed record LogEntry(
	LogChannel Channel, LogSeverity Severity, LogCategory Category,
	string Message,          // human text; for Debug, the rendered "event k=v ..." line
	string EventId,          // Debug only, e.g. "ai.target_lost"; null otherwise
	IReadOnlyList<LogField> Fields,
	ulong PhysicsFrame, double GameTimeSeconds);

public interface ILogSink { void Write(LogEntry entry); }

public static class GameLog
{
	public static LogChannel EnabledChannels { get; set; }          // default None
	public static bool IsCategoryEnabled(LogCategory category);
	public static void SetCategoryEnabled(LogCategory category, bool enabled);
	public static bool IsEnabled(LogChannel channel, LogCategory category);
	public static event Action SettingsChanged;                      // DebugMenu/feed refresh

	public static void AddSink(ILogSink sink);
	public static void RemoveSink(ILogSink sink);

	public static void Player(LogCategory category, [InterpolatedStringHandlerArgument("category")] ref PlayerLogHandler message);
	public static void Internal(LogCategory category, [InterpolatedStringHandlerArgument("category")] ref InternalLogHandler message);
	public static void Debug(LogCategory category, string eventId, params ReadOnlySpan<LogField> fields);
	public static void Warning(LogCategory category, string message);  // console always
	public static void Error(LogCategory category, string message);    // console always

	/// Applies --log / --log-categories / --log-file from user args. Session-only; never persisted.
	public static void ConfigureFromCommandLine(IReadOnlyList<string> userArgs);
	/// Test helper: restores defaults and removes non-console sinks.
	internal static void ResetForTests();
}

/// Loads/saves user preferences in user://settings.cfg. Plain C#; logging is its first consumer.
public static class UserSettings
{
	public static void Load();   // missing file → defaults (all logging off); malformed values → default + one warning
	public static void Save();   // called by the toggles after a change; failure → GD.PushWarning, in-memory state kept
	public static LogChannel LogChannels { get; set; }
	public static IReadOnlySet<LogCategory> DisabledLogCategories { get; set; }
	public static bool LogToFile { get; set; }
}
```

Startup order: `UserSettings.Load()` applies the saved toggles to `GameLog`, then `ConfigureFromCommandLine` overrides them for the session. Both run from a static initializer on the first `GameLog` access, so no autoload is touched. Toggles in the pause menu and debug menu set `GameLog` and then call `UserSettings.Save()`. If command-line overrides are active, a toggle saves only the value the user actually changed, never the override.

Error contract: logging never throws into callers. A sink exception is caught, reported once with `GD.PushError`, and that sink is removed. Calls from a non-main thread are queued and delivered on the main thread by the feed. The console sink is thread-safe through `GD.Print`.

### Sinks

- `ConsoleLogSink`: always registered. Line format: `[PLR] Picked up Longsword +5`, `[INT][ai] Skeleton2 lost sight of Player`, `[DBG][ai] f=1234 t=20.51 ai.target_lost actor=Skeleton2 dist=7.42`. Warnings and errors use `GD.PushWarning`/`PushError`; everything else uses `GD.Print`.
- `JsonlFileLogSink`: available in all builds. Enabled by `--log-file[=path]` or the debug-menu "Log to file" toggle (persisted). Default path `user://logs/<yyyyMMdd-HHmmss>.jsonl`. One JSON object per entry, flushed every frame and on exit. The resolved absolute path is printed at startup so agents can find it.
- `LogFeed` (UI): see below.

### Command-line configuration

```
godot --path . res://scenes/main/main.tscn -- --log=player,internal,debug --log-categories=ai,combat --log-file
```

`--log=all` turns on every channel and `--log=none` forces all off. Unknown tokens produce one warning and are otherwise ignored. All channels are honored in every build for now.

## In-game feed

`LogFeed` (proposed) is a `Control` scene, `scenes/ui/log_feed.tscn`, instanced in `hud.tscn`. It is anchored bottom-left above the action bar; the exact anchor is settled in the editor during slice 2.

- `VBoxContainer` with `alignment = End`. New lines are appended at the bottom, so older lines move up. `MouseFilter = Ignore` on everything so the feed never blocks clicks.
- One `Label` per entry, colored by channel/severity. Exported defaults:

  | Kind | Color |
  | --- | --- |
  | Player | warm white `#F2EBD9` |
  | Internal | cyan `#7FD4E6` |
  | Debug | muted green `#8FBF8F`, monospace font |
  | Warning | amber `#F2C14E` |
  | Error | red `#E5534B` |

- Exports: `LineLifetime = 5.0`, `FadeDuration = 0.6`, `MaxLines = 8`, `MaxLineWidth = 520` px, `Font`/`FontSize`, and the colors above. When a line is added past `MaxLines`, the oldest line starts fading immediately.
- Player lines are short, but long item names and Internal entries can exceed the width, so labels use `AutowrapMode = WordSmart` within `MaxLineWidth`. A wrapped entry counts as one line for `MaxLines` and fades as a unit.
- **Coalescing:** an entry identical to the newest visible line resets that line's timer and shows `×N` instead of adding a new line. This keeps Debug floods (per-frame events) readable.
- Lifetimes count down only while the tree is unpaused (`ProcessMode = Pausable`), so messages stay readable under the pause menu.
- The feed renders whatever `GameLog` delivers. It has no channel logic of its own. If every channel is off, it is empty and costs nothing.

## Player narrator

`PlayerLogNarrator` (proposed) is a `Node` child of `hud.tscn`. It subscribes to bus events in `_Ready` and emits `GameLog.Player(...)` with names from the identification-aware display path, so a disguised potion is called what the inventory calls it.

### Player message style

Player entries are short, readable message-log lines: a past-tense verb phrase with no subject, like a compact roguelike message log. Rules:

- **Short verb phrases.** Past tense with no "You" and no trailing period: `Picked up Longsword +5`, `Drank Murky Potion`. Only the first letter is capitalized; item names keep their own casing. Keep one event per line, with no follow-up clauses.
- **Plain words, no abbreviations or codes.** Write "experience", not "XP"; "gold", not "g"; "depth 3", not "D3". No internal IDs, node names or raw floats. Numbers appear only as counts ("3 rations", "25 gold", "15 experience") and as enchantment bonuses that are already part of the item's name ("Longsword +5").
- **No articles; simple plurals.** `Picked up Iron Ring`, not "Picked up an Iron Ring". Quantities above one get a count and a plural: `Picked up 3 rations`.
- **Fitting verbs.** The verb comes from the item kind and slot: *wielded* a weapon, *put on* a ring or amulet, *wore* armor, *drank* a potion or drink, *ate* food, *read* a scroll. Unknown kinds fall back to *used*.
- **Name what the player sees.** Unidentified items keep their disguise names (`Drank Murky Potion`, `Read scroll labeled ZELGO MER`) until identified.

The text helpers (verb selection, quantity/plural formatting) live in one plain-C# class, `PlayerMessageText`, so they are unit-testable and replaceable when localization arrives. English only for now; no localization framework is introduced.

### Initial message set

| Bus event | Example message |
| --- | --- |
| `ItemPickedUp` (**new**) | `Picked up Longsword +5` / `Picked up 3 rations` / `Found 25 gold` |
| `ItemEquipped` | `Wielded Longsword +5` / `Put on Iron Ring` / `Wore Leather Cap` |
| `ItemUnequipped` | `Put away Longsword +5` / `Took off Iron Ring` |
| `ItemConsumed` | `Drank Murky Potion` / `Ate ration` / `Read scroll labeled ZELGO MER` |
| `ItemDropped` | `Dropped Longsword +5` / `Dropped 2 rations` |
| `ItemDestroyed` | `Wooden Shield broke` |
| `ItemIdentified` (**extended**) | `Murky Potion is a Potion of Healing` |
| `XpRewarded` | `Gained 15 experience` |
| `LevelChanged` | `Descended to depth 3` / `Climbed up to depth 2` |
| `PlayerDeath` | `Died` |

`ItemIdentified` is extended to `ItemIdentified(string typeId, string unidentifiedName, string trueName)`. The emitter captures the disguise name before calling `Identify`, so observers can phrase the reveal. Slice 3 updates its callers.

Later candidates, once bus events exist: monster slain (`Slew skeleton archer`), level up (`Reached level 4`), trap triggered (`Triggered a spike trap`), locked door (`Door is locked`).

## Call order

```
LootableItem (player overlaps / interacts)
└─ PlayerInventoryController.TryPickup → Acquire
   ├─ Inventory.TryAdd / source.CommitCollected / item.OnPickup     (ownership moves first)
   ├─ GameLog.Debug(Items, "item.pickup", id, qty, slot)            (NEW, inline; after commit)
   └─ SignalBus.EmitItemPickedUp(player, item, qty)                 (NEW; only on success)
      └─ PlayerLogNarrator.OnItemPickedUp
         └─ GameLog.Player(Items, $"Picked up {PlayerMessageText.WithQuantity(displayName, qty)}")
            ├─ gate: EnabledChannels has Player && category enabled  (else: no formatting, return)
            ├─ build LogEntry (frame, game time)
            └─ for each sink: Write(entry)
               ├─ ConsoleLogSink → GD.Print("[PLR] Picked up …")
               ├─ JsonlFileLogSink → append line (if active)
               └─ LogFeed → enqueue; _Process adds/coalesces Label, ages, fades, frees
```

Invariant: notifications (bus and log) fire only after the operation has succeeded. Failed pickups produce no Player entry. They may produce an Internal entry such as `Pickup rejected: inventory full`.

## Affected files

```
+ scripts/logging/GameLog.cs                 facade, gating, sink fan-out, CLI parsing
+ scripts/logging/LogTypes.cs                LogChannel, LogSeverity, LogCategory, LogField, LogEntry, ILogSink
+ scripts/logging/LogMessageHandlers.cs      Player/Internal interpolated string handlers
+ scripts/logging/ConsoleLogSink.cs
+ scripts/logging/JsonlFileLogSink.cs        all builds (release gating is a pre-release follow-up)
+ scripts/UserSettings.cs                    user://settings.cfg load/save; log toggles are the first settings
~ scripts/GameDebug.cs                       remove CombatLogsEnabled/AiLogsEnabled/Combat()/Ai(); keep x-ray flags
~ scripts/SignalBus.cs                       + ItemPickedUp(Player, Item, int); ItemIdentified gains unidentifiedName/trueName
~ scenes/player/PlayerInventoryController.cs emit ItemPickedUp + item.pickup debug event after commit
~ ItemIdentified emitters (GameSession and identification callers)
                                             capture the disguise name before Identify
~ scenes/attacks/AttackController.cs, Projectile.cs, scenes/enemies/EnemyBehaviorComponent.cs,
  scenes/enemies/ai/SearchingState.cs, EnemyContext.cs
                                             GameDebug.Combat/Ai → GameLog.Internal (+ Debug events where useful)
~ noisy GD.Print callers (HealthComponent, Door, TriggerComponent, DeathComponent, BreakableItem,
  BoneAttachmentManager, GameSession transitions/saves, Hud, Preview, …)
                                             → GameLog.Internal / Debug with a category
~ scenes/ui/DebugMenu.cs                     replace Combat/AI log items with Player/Internal/Debug + category submenu + "Log to file"
+ scenes/ui/LogFeed.cs, scenes/ui/log_feed.tscn
+ scenes/ui/PlayerLogNarrator.cs
+ scenes/ui/PlayerMessageText.cs             plain C#: articles, plurals, verbs by item kind/slot
~ scenes/ui/hud.tscn                         instance LogFeed, add PlayerLogNarrator
~ scenes/ui/PauseMenu.cs, pause_menu.tscn    "Show message log" toggle (Player channel, persisted)
+ tests/GameLogTest.cs                       plain C#
+ tests/UserSettingsTest.cs                  round trip, missing/malformed file, CLI override not persisted
+ tests/PlayerMessageTextTest.cs             plain C#: style rules
+ tests/LogFeedTest.cs                       [RequireGodotRuntime]
+ docs/logging.md                            current-system doc once implemented
~ docs/README.md, ARCHITECTURE.md, AGENTS.md (logging conventions + CLI flags for agents)
```

Existing `GD.PrintErr`/`PushError` failure reports stay as they are. They already reach the console with stack traces. Convert one to `GameLog.Error` only when it should also appear in the feed.

## Delivery slices

Hybrid delivery: slice 1 establishes the shared contract horizontally, because every later slice depends on it. Each later slice is a vertical, playable increment.

### Slice 1 — Core API, console, settings, migration of existing toggles

- `GameLog`, the log types, the handlers, `ConsoleLogSink`, CLI parsing, and `UserSettings` persistence of the toggles.
- Migrate `GameDebug.Combat/Ai` callers to `GameLog.Internal`. Replace the debug-menu Combat/AI items with channel toggles and a category submenu.
- Move the noisy unconditional `GD.Print` calls to Internal/Debug, so the default console becomes quiet.
- **Exercise:** run `main.tscn`, toggle Internal + Ai in the debug menu, and watch AI narration in the Godot output. Restart and the toggles are still on. Run with `-- --log=none`: logging is off for that session, and the saved setting is unchanged on the next normal launch.
- **Verify:** `dotnet build`. `GameLogTest`: gating per channel/category, no handler formatting when disabled (a side-effecting `ToString` is not called), invariant-culture field rendering, CLI parsing including `all`/`none` and unknown tokens, warnings reaching the console with channels off, sink-exception isolation. `UserSettingsTest`: save/load round trip, missing file gives all-off defaults, a malformed value falls back to default with a warning, and a CLI override is not written back.
- **Limitation:** no in-game feed yet.

### Slice 2 — In-game feed

- `LogFeed` scene in the HUD with colors, coalescing, `MaxLines`, fade, and pause behavior.
- **Exercise:** enable Internal and fight a monster. Lines scroll up and vanish after about 5 s. Spam the same event and see `×N`.
- **Verify:** `LogFeedTest` uses the scene runner: write entries through `GameLog`, assert line count, color per channel, coalescing, removal after `LineLifetime + FadeDuration` of simulated frames, and `MaxLines` eviction. Then a `godot-mcp` headless smoke boot of `main.tscn`.
- **Limitation:** the Player channel has nothing to say yet.

### Slice 3 — Player channel

- `ItemPickedUp` bus event, the extended `ItemIdentified`, `PlayerMessageText`, `PlayerLogNarrator`, and the persisted pause-menu "Show message log" toggle.
- **Exercise:** enable the toggle, pick up, equip, drink an unidentified potion, descend stairs. Every line is a short, plain-language verb phrase. Messages use the disguised names until the item is identified, then the reveal line appears.
- **Verify:** `PlayerMessageTextTest` covers quantity/plural formatting, gold phrasing and verbs by kind/slot. A style test runs every narrator template through representative items and asserts each message starts with a capital, has no trailing period, does not start with "You", and contains none of a banned-token list (`XP`, `HP`, `+N` deltas outside item names, `_`, node-style names). Narrator tests with a capture sink assert the exact text for identified and unidentified items. Pickup failure (full inventory) emits no Player entry. Existing `ItemLifecycleIntegrationTest` still passes.

### Slice 4 — Machine traces for QA and agents

- `JsonlFileLogSink`, a stable event-ID catalog (`ai.*`, `combat.*`, `item.*`, `level.*`, `save.*`) documented in `docs/logging.md`, and Debug events at key decision points: AI state transitions with distances, damage rolls with base/crit/defense numbers, generation seeds, save/load.
- `AGENTS.md` gets the recommended agent invocation: CLI flags plus where the JSONL file lands.
- **Exercise:** headless run with `--log=debug --log-file` produces parseable JSONL.
- **Verify:** a test attaches a capture sink and asserts that an `EnemyStateMachine` transition emits `ai.state_changed` with the expected fields. This uses the plain-class AI layer, consistent with the navmesh testing limits in `AGENTS.md`.

## Resolved decisions

- **Persistence:** channel/category toggles and "Log to file" are saved in `user://settings.cfg`. Command-line flags are session-only overrides.
- **Build availability:** all channels and the file sink work in every build for now. The debug menu stays hidden in exports, so in exported builds QA turns on Internal/Debug through the CLI flags or by editing `settings.cfg`.
- **Categories:** a fixed `LogCategory` enum. Per-actor filtering is not planned; Debug events carry an `actor=` field instead.
- **Voices:** Player text follows the [Player message style](#player-message-style). Internal and Debug keep developer and machine formats.
- **Startup hook:** a static initializer on the first `GameLog` access loads settings and applies CLI flags, with no autoload change.

## Follow-ups before public release

- Decide whether Internal, Debug and the file sink should be hidden, CLI-only, or compiled out in release exports. If gated, change the default `UserSettings` handling so a stale developer setting cannot enable them for players.
- A scrollable message history panel (classic "previous messages" view) can reuse a bounded ring-buffer sink. It is out of scope here.
- Localization of Player text replaces `PlayerMessageText` templates when a localization approach is chosen.
