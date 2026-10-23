# Combat harness and machine traces

Open `scenes/debug/combat_harness.tscn` and run it with F6. The arena uses production equipment, action slots, movement, damage resolution and enemy defense. Choose a hero weapon and use attack, guard or dodge. Spawn a monster in the Monster section; its authored defense profile is selected by default, with overrides available in the Defense selector. The status panel shows current actions and health, with display/simulation controls and combat logging grouped below. Hit-volume/hurtbox overlays, combat effects, slow motion and a moving practice target remain available. Monster attacks preserve the current geometry; they do not teleport the target into range. Arena monsters suppress dungeon loot and experience rewards.

Checkbox defaults apply immediately on startup and to newly spawned monsters. `--combat-console` also checks the console logging option.

Hitboxes appear only during an attack’s contact window; use slow motion to inspect them. Hurtboxes remain visible while their toggle is enabled.

The harness replaces the former animation playground. Animation binding tests remain useful, but the scene is now a combat simulation and diagnostics surface.

## Capture a scripted run

From the repository root on this machine:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --log-file /tmp/combat-engine.log --path "$PWD" \
  res://scenes/debug/combat_harness.tscn -- \
  --combat-seed=42 \
  --combat-log=/tmp/combat.jsonl \
  --combat-script="$PWD/scenes/debug/scenarios/combat_smoke.json" \
  --combat-frames=420

python3 scripts/analyze_combat_trace.py /tmp/combat.jsonl --require-complete
```

The supplied scenario covers melee, parry/stagger, counterattack, held guard/release, dodge, equipment changes and projectiles. It also intentionally requests guard while staggered to capture a rejected action. This is a regression exercise, not a promise that every requested attack will hit: geometry, timing, defense and passive evasion remain active.

`--combat-console` additionally echoes records with the `COMBAT ` prefix. `--combat-log` accepts an absolute path or `user://` path and creates a fresh file, flushing each entry. The destination directory must exist. Without a path the recorder keeps the most recent 2048 records in memory, exposed as `Recorder.RecentRecordsJson`; a file capture retains the complete run. Inspector `Seed`, `TracePath`, recorder `CaptureFrames` and `EchoToConsole` configure interactive sessions. Console/frame-capture checkboxes are available in the arena. Engine errors remain in the separate Godot log, so retain both files when investigating a failure.

## Commands

Scripts are JSON arrays, ordered by `Frame` (physics-frame offset), retaining file order for commands on the same frame. Frame zero executes on the first available tick. `Command` names are lowercase. `Value` is an integer selection; `X` and `Z` form a world-space vector. Script playback disables physical player input so keyboard state cannot perturb the run. Buttons and scripts share `ExecuteCommand(string, int, Vector3)` and acceptance is recorded.

| Command | Arguments |
| --- | --- |
| `equip` | `Value`: weapon index, shown in the weapon selector |
| `spawn` | `Value`: monster index (0 yeti, 1 skeleton, 2 Warden) |
| `defense` | `Value`: 0 disabled, 1 invulnerable, 2 profile, 3 forced dodge, 4 forced guard/parry |
| `attack`, `secondary`, `monster_attack` | No arguments; same action ownership as gameplay |
| `guard`, `release_guard` | Set or release the hero's held guard intention |
| `dodge` | `X`, `Z`: direction; zero uses the player's movement/backstep choice |
| `monster_guard`, `monster_dodge` | `X`, `Z`: facing/direction |
| `move` | `X`, `Z`: movement input; zero stops input |
| `position_player`, `position_monster` | `X`, `Z`: explicit arena placement; height is retained |
| `reset` | Restore hero/practice-target health and cancel hero commitment; heal a living monster |

Spawn a fresh monster after its death. Forced defense modes retain real cooldowns, visibility, delay, gate transitions and collision constraints. A seed isolates combat RNG, but does not promise deterministic replay across builds, different inputs or engine physics configurations.

## JSONL schema 1

Each line is one JSON object with `schema`, `run`, monotonically increasing `sequence`, relative physics `frame`, scaled gameplay `time`, `event`, source `actor` path, and a typed `data` object. Numbers use JSON's culture-independent representation; vectors are `[x,y,z]`. UUIDs and Godot instance IDs identify a run/execution rather than defining reproducibility. Use actor paths plus captured source/execution identities to correlate events.

| Event | Important data |
| --- | --- |
| `run.started`, `run.ended` | Seed, engine version, physics tick rate, scene; a closing record distinguishes a complete capture |
| `harness.command` | Command, arguments and acceptance, including rejected requests |
| `harness.equipped`, `harness.spawned` | Authored weapon/monster path, style and seed |
| `action.acquired`, `action.phase`, `action.released`, `action.interrupted` | Kind, phase, lease or interruption reason |
| `attack.started`, `attack.contact`, `attack.ended` | Execution/source, style, contact timing, duration, target, base damage, crit, interruption and contact count |
| `defense.decision`, `defense.observed`, `defense.request`, `defense.started`, `defense.riposte` | Policy chances/rolls, source observation, delay/distance, selected defense, accepted timing/sector/chip settings and actual request result; riposte acceptance or rejection reason, current distance and reach |
| `projectile.spawned`, `projectile.contact`, `projectile.world_impact`, `projectile.expired` | Projectile identity, parent attack identity, owner, origin/direction, speed/range, seed, defensive flags, contact and impact |
| `damage.resolved` | Source/owner/execution, delivery/eligibility, incoming direction, raw amount, accuracy/evasion/armor and their rolls, final outcome, damage and health |
| `simulation.tick`, `actor.state` | Delta/time scale; actor position, velocity, facing, health, action/phase, attack identity, stagger and defense timers, parry-window eligibility and dodge immunity |

The analyzer validates schema, run identity, record sequence and monotonic clocks. It summarizes event counts, outcomes, interrupted attacks, damage per target and rejected commands. `--require-complete` rejects a truncated capture. Keep raw records for contact-by-contact analysis; aggregates alone cannot explain timing or geometry.

`CombatTrace` is disabled without subscribers. Its scene recorder owns subscription, scope, file and bounded history; logging holds no gameplay registry. A failing sink is detached so diagnostics cannot abort combat. The wider Player/Internal/Debug channels, settings and in-game feed remain a separate [logging proposal](specs/logging-plan.md).
