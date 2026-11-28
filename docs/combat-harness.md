# Combat harness and machine traces

Open `scenes/debug/combat/combat_harness.tscn` and run it with F6. The arena is a `DebugArena` with the production player, HUD, game camera and lighting on the dungeon's stone floor, so it plays like a dungeon floor: production equipment, action slots, movement, damage resolution, mana and monster defense components. Instead of a floor's wall torches, four of the game's braziers light the fight area under a dungeon floor's ambient light; **Dark level** puts them out. The camera follows the hero at the game's angle with the game's turn and zoom bindings. The arena starts open with a passive practice dummy, which uses a monster's health, hurtbox, movement, poise, action gate, damage numbers and feedback components, so knockback and defensive responses can be inspected without a dungeon. Rooms and trap gauntlets play in the [room harness](../scenes/debug/README.md#room-generation) instead.

## Debug menu in the arena

Items, spells, monsters, the hero and display settings come from the HUD's [debug menu](../scenes/debug/README.md#in-game-debug-menu), the same one the game has, with a few arena differences:

- **Items** equips what you pick at once.
- **Monsters → Spawn** lists the dummy and every valid monster scene under `scenes/enemies/` and scatters them at random free spots within the Monsters tool's `ScatterRadius` of the hero, each at least its `ScatterSeparation` from the hero and every other target, so they spawn in view; use **Clear spawned** first to replace the target. Scripted `spawn` commands place straight ahead of the hero.
- **Monster AI**, **Riposte** and **Rewards** start off. With AI off, monsters still react to hits and defend themselves; turn it on to let them move, chase and attack. **Defense** forces monster default, no defense, always dodge or always guard/parry, and **Invulnerable** is a separate switch. With **Rewards** on, kills award experience; loot still needs a dungeon floor to land on.
- **Hero → Builds** equips one of the class presets in `scenes/debug/hero_build_profiles/`.
- **Level → Walls** switches the stage's wall arrangements and **Level → Dark level** darkens the arena as a dark floor does.
- **Combat** covers slow motion, hit-volume and hurtbox drawing, hit effects, movement dust and dash trails, camera shake and logs. The settings authored on the arena's `CombatDisplayTool` apply on startup and to every monster spawned later. Hit volumes appear only during an attack's contact window, so use slow motion to inspect them.

The harness panel on the right keeps what only the arena has: hero attack, guard and dodge buttons, **Restore health**, the status line, the spawned monster's action list and the combat log. It is mouse-operated, so the keys stay free to control the hero.

The monster section lists the spawned monster's health phase, current tactical decision and running action, then each action's priority, effective weight (after weight modifiers) and whether it is ready, cooling down or out of position, followed by the last choice or rejection. Spawning re-creates the monster from its scene, so Resource edits made through the Remote inspector apply to the new instance. The trace events `ai.action_attempt`, `ai.tactical_decision`, `dash.*` and `enemy.phase_changed` record the same decisions; `ai.tactical_decision` fires when the selected movement goal or reason changes, which tells a blocked escape, an elapsed retreat, a required shot and a stand-and-fight choice apart.

For ranged warning practice, spawn the **Skeleton Crossbowman**, move sideways or dodge as its [warning](combat-system.md#projectile-warnings) commits, and use slow motion to inspect the phases. The `monster_attack` scenario command requests one attack through a monster's production behavior while AI stays off, to inspect its warning and timing without autonomous movement.

## Capture a scripted run

From the repository root on this machine:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --log-file /tmp/combat-engine.log --path "$PWD" \
  res://scenes/debug/combat/combat_harness.tscn -- \
  --combat-seed=42 \
  --combat-log=/tmp/combat.jsonl \
  --combat-script="$PWD/scenes/debug/combat/scenarios/combat_smoke.json" \
  --combat-frames=420

python3 scripts/analyze_combat_trace.py /tmp/combat.jsonl --require-complete
```

Scenarios live in `scenes/debug/combat/scenarios/`:

| Scenario | Frames | Exercises |
| --- | --- | --- |
| `combat_smoke.json` | 420 | Melee, parry and stagger, counterattack, held guard and release, dodge, equipment changes, projectiles, and a guard requested while staggered to capture a rejection |
| `projectile_practice.json` | 240 | Repeated crossbow shots, steering-directed dodges and aim locks |
| `magic_missile_practice.json` | 320 | Low- and high-tier wand volleys at a minion, a guarding warrior and a rat |
| `movement_feedback.json` | 150 | Run, stop and dash for movement dust and the dash trail |
| `combo_buffer.json` | 240 | A sword combo from buffered presses, a dodge buffered out of the finisher, both dodge charges and a switch to the axe combo |

A scenario is a regression exercise, not a promise that every requested attack hits: geometry, timing and active defense stay live.

`--combat-console` also echoes every record with the `COMBAT ` prefix. `--combat-log` takes an absolute or `user://` path whose directory must exist, creates a fresh file and flushes each entry. Without a path the recorder keeps the most recent 2048 records in memory (`Recorder.RecentRecordsJson`); a file keeps the whole run. Inspector `Seed`, `TracePath` and the recorder's `CaptureFrames`, `EchoToConsole` and `IgnoredEventPrefixes` configure interactive sessions. Engine errors stay in the separate Godot log, so keep both files when investigating a failure.

Effect events are echoed to Godot's Output panel by default (`EchoEffectsToConsole`), even without `--combat-console`; filter for `"event":"effect.` to see them without per-frame noise. Each `effect.*` event (hit-stop, knockback, lunge, camera shake, hit flash, visual recoil, melee swoosh, particles, damage number) carries its values and `triggered`; suppressed or unconfigured effects include a reason.

## Commands

Scripts are JSON arrays ordered by `Frame` (physics-frame offset), keeping file order within a frame. Frame zero runs on the first available tick, after the dummy is placed. `Command` names are lowercase. `Path` names a resource by its `res://` path, so adding content never changes what a script uses; `Value` is an integer; `X` and `Z` form a world-space vector. Playback turns off device input (`InputComponent.DeviceEnabled`) so keyboard state cannot perturb the run, while the player's input controller keeps running, so scripted presses use the real buffer and recovery cancels. Buttons and scripts share `ExecuteCommand(string command, string path, int value, Vector3 direction)`, and acceptance is recorded.

| Command | Arguments |
| --- | --- |
| `equip` | `Path`: item definition; equips an exact authored copy, replacing the one the previous `equip` gave |
| `spell` | `Path`: spell definition; the equipped wand or staff, and any equipped later, binds it |
| `spawn` | `Path`: monster scene, empty for the practice dummy; replaces the arena's targets. `Value`: distance ahead of the hero in metres, 0 for the monster's preferred distance |
| `build` | `Path`: `HeroBuildProfile`; replaces the hero's attributes and inventory with the build's exact items |
| `defense` | `Value`: 0 profile default, 1 no defense, 2 always dodge, 3 always guard/parry |
| `automation` | `Value`: 0 disables monster AI, 1 enables it (movement and attacks) |
| `attack`, `secondary`, `monster_attack` | No arguments; same action ownership as gameplay |
| `guard`, `release_guard` | Set or release the hero's held guard intention |
| `dodge` | `X`, `Z`: direction; zero resolves current or recent input, then facing. This direct command stays locked after it starts |
| `press_primary`, `press_secondary`, `press_dodge` | No arguments; buffers a fresh press in the input controller like a button press (acceptance means buffered). A buffered dodge resolves the latest input when accepted and allows its steering correction |
| `monster_guard`, `monster_dodge` | `X`, `Z`: facing or direction |
| `move` | `X`, `Z`: movement input; zero stops input |
| `position_player`, `position_monster` | `X`, `Z`: explicit arena placement; height is kept |
| `reset` | Restore the hero's health, mana and poise and cancel their commitment; restore the practice target; heal a living monster |

Spawn a fresh monster after its death. Forced defense modes keep real cooldowns, visibility, delay, gate transitions and collision constraints. A seed isolates combat randomness but does not promise deterministic replay across builds, different inputs or physics configurations.

## JSONL schema 1

Each line is one JSON object with `schema`, `run`, a monotonically increasing `sequence`, the relative physics `frame`, scaled gameplay `time`, `event`, the source `actor` path and a typed `data` object. Numbers use JSON's culture-independent form; vectors are `[x,y,z]`. UUIDs and Godot instance IDs identify a run or execution rather than defining reproducibility; correlate events by actor path plus the captured source and execution identities.

| Event | Important data |
| --- | --- |
| `run.started`, `run.ended` | Seed, engine version, physics tick rate, scene; the closing record marks a complete capture |
| `harness.command`, `harness.automatic_action` | Command, arguments and acceptance, rejections included; accepted repeated behavior |
| `harness.equipped`, `harness.spawned` | Authored item or monster path, style and seed |
| `action.acquired`, `action.phase`, `action.released`, `action.interrupted`, `action.hyper_armor` | Kind, phase, lease, interruption reason or armored state |
| `attack.started`, `attack.contact`, `attack.ended`, `attack.wall_impact` | Execution and source, style, delivery and homing, contact timing, duration, target, base damage, crit, interruption and contact count |
| `attack.spell_released`, `attack.combo_step` | Spell, level and whether it cast; the primary combo step played |
| `attack.aim_locked`, `attack.facing_committed` | Captured origin and direction and time to release; elapsed time and committed melee facing |
| `poise.broken` | Break stagger duration |
| `defense.decision`, `defense.observed`, `defense.request`, `defense.started`, `defense.riposte` | Policy chances and rolls, whether the observed attack was safe to guard, delay and distance, selected defense, accepted timing and request result; riposte acceptance or rejection reason, distance and reach |
| `defense.out_of_sector`, `defense.guard_broken`, `defense.pass_through_exit`, `defense.perfect_dodge`, `defense.redirected` | Attacker outside the guard sector; guard-break damage and stagger; time leaving a pass-through body; perfect-dodge refund; a dodge's steering correction |
| `slots.reserved` | Attacker, slot kind and slots now held |
| `projectile.spawned`, `projectile.contact`, `projectile.world_impact`, `projectile.expired`, `projectile.homing_lost`, `projectile.lobbed`, `projectile.landed` | Projectile and parent attack identity, owner, origin and direction, speed and range, seed, defensive flags, contact, impact, lost lock and lob landing |
| `damage.resolved`, `status.resolved` | Source, owner and execution, the parent attack, delivery and eligibility, incoming direction, raw amount, armor and its roll, poise damage, hit reaction and remaining poise, hit zone, sneak attack, critical roll, outcome, damage and health; statuses applied on hit |
| `simulation.tick`, `actor.state` | Delta and time scale; actor position, velocity, facing, health, action and phase, attack identity, stagger and defense timers, parry eligibility and dodge immunity |

Monster capabilities and health changes add their own events in the same envelope (`blink.*`, `theft.*`, `ambush.woke`, `actor.collapsed`, `actor.revived`, `actor.died`, `retaliation.applied`, `health.healed`, `poison.tick`), and the playtest recorder writes run records (`floor.entered`, `hero.*`, `item.*`) through the same trace.

The analyzer validates schema, run identity, record sequence and monotonic clocks, and summarizes event counts, outcomes, interrupted attacks, damage per target and rejected commands. `--require-complete` rejects a truncated capture. Keep raw records for contact-by-contact analysis; aggregates alone cannot explain timing or geometry.

`CombatTrace` is disabled without subscribers. Its scene recorder owns subscription, scope, file and bounded history; logging holds no gameplay registry, and a failing sink is detached so diagnostics cannot abort combat. The wider player-facing channels and in-game feed remain a separate [logging proposal](specs/logging-plan.md).
