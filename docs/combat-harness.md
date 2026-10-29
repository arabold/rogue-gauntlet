# Combat harness and machine traces

Open `scenes/debug/combat/combat_harness.tscn` and run it with F6. The arena uses production equipment, action slots, movement, damage resolution and enemy defense. Choose a hero weapon and use attack, guard or dodge. Spawn a monster in the Monster section; its authored defense profile is selected by default, with overrides available in the Defense selector. The status panel shows current actions and health, with display/simulation controls and combat logging grouped below. Hit-volume/hurtbox overlays, combat effects, slow motion and a moving practice target remain available. Monster attacks preserve the current geometry; they do not teleport the target into range. Arena monsters suppress dungeon loot and experience rewards.

Select one of the 20 spells and a level, then press **Cast spell**. Frost Nova freezes, Gust pushes, Hailstorm pelts and slows, Ice Wall repels, Solar Flares ignites, and guards absorb and reflect incoming hits. **Spawn / clear crowd** toggles three stationary monsters around the player for area tests; select an automated monster attack to exercise guards. The selector uses explicitly assigned spell nodes. These casts exercise spell components directly; learning, mana, staff ownership and guard save/restore are not connected. Tremor's wall dust and camera shake remain in the visual workbench.

Checkbox defaults apply immediately on startup and to newly spawned monsters. `--combat-console` also checks the console logging option.

For arrow practice, choose **Skeleton Crossbow**, set **Spawn distance** to 6, spawn it, and select **Repeat attack**. The dashed guide tracks you, then becomes a solid arrow with a short sound before the shot. Move sideways or dodge on commitment; enable **Hero invulnerable (practice)** to learn the timing without dying. Slow motion helps inspect both phases. Protection outcomes still resolve before debug invulnerability.

**Automatic behavior** also supports repeating dodge or guard on monsters that have those capabilities. A guard cycle holds for 0.6 seconds before release. The minimum request interval uses gameplay time; normal cooldowns, action ownership, range and sight still apply. Dodge alternates lateral directions. Manual/reactive mode and Repeat attack retain the existing Defense selector; repeating dodge/guard suspends reactive decisions so the two controllers cannot compete. Changing modes changes future requests and lets accepted actions finish. Unsupported modes report unavailability instead of adding components. Spawn distance affects the next spawn; repeat requests never reposition actors. The arena is deliberately wider for movement practice and still hosts one selected monster at a time.

Hitboxes appear only during an attack’s contact window; use slow motion to inspect them. Hurtboxes remain visible while their toggle is enabled.

The harness replaces the former animation playground. Animation binding tests remain useful, but the scene is now a combat simulation and diagnostics surface.

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

The supplied scenario covers melee, parry/stagger, counterattack, held guard/release, dodge, equipment changes and projectiles. It also intentionally requests guard while staggered to capture a rejected action. This is a regression exercise, not a promise that every requested attack will hit: geometry, timing and active defense remain active.

Use `scenes/debug/combat/scenarios/projectile_practice.json` with `--combat-frames=240` for repeated crossbow shots, steering-directed dodges and aim-lock records. `combo_buffer.json` with `--combat-frames=240` chains a sword combo from buffered presses, buffers a dodge out of the finisher's contact, spends both dodge charges and switches to the axe combo.

`--combat-console` additionally echoes records with the `COMBAT ` prefix. `--combat-log` accepts an absolute path or `user://` path and creates a fresh file, flushing each entry. The destination directory must exist. Without a path the recorder keeps the most recent 2048 records in memory, exposed as `Recorder.RecentRecordsJson`; a file capture retains the complete run. Inspector `Seed`, `TracePath`, recorder `CaptureFrames` and `EchoToConsole` configure interactive sessions. Console/frame-capture checkboxes are available in the arena. Engine errors remain in the separate Godot log, so retain both files when investigating a failure.

## Commands

Scripts are JSON arrays, ordered by `Frame` (physics-frame offset), retaining file order for commands on the same frame. Frame zero executes on the first available tick. `Command` names are lowercase. `Value` is an integer selection; `X` and `Z` form a world-space vector. Script playback turns off device input (`InputComponent.DeviceEnabled`) so keyboard state cannot perturb the run; the player's input controller keeps running, so scripted presses use the real buffer and recovery cancels. Buttons and scripts share `ExecuteCommand(string, int, Vector3)` and acceptance is recorded.

| Command | Arguments |
| --- | --- |
| `equip` | `Value`: weapon index, shown in the weapon selector |
| `spawn` | `Value`: monster index (0 minion, 1 warrior, 2 Warden, 3 crossbowman, 4 rat) |
| `defense` | `Value`: 0 disabled, 1 invulnerable, 2 profile, 3 forced dodge, 4 forced guard/parry |
| `attack`, `secondary`, `monster_attack` | No arguments; same action ownership as gameplay |
| `guard`, `release_guard` | Set or release the hero's held guard intention |
| `dodge` | `X`, `Z`: direction; zero uses the player's movement/backstep choice |
| `press_primary`, `press_secondary`, `press_dodge` | No arguments; buffers a fresh press in the input controller, like a button press (acceptance means buffered). A buffered dodge uses the current `move` steering |
| `monster_guard`, `monster_dodge` | `X`, `Z`: facing/direction |
| `automation` | `Value`: 0 manual/reactive, 1 repeat attack, 2 repeat dodge, 3 repeat guard; unsupported capabilities reject the command |
| `spawn_distance` | `Value`: distance 1–12 for the next monster spawn |
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
| `action.acquired`, `action.phase`, `action.released`, `action.interrupted`, `action.hyper_armor` | Kind, phase, lease, interruption reason or armored state |
| `attack.started`, `attack.contact`, `attack.ended` | Execution/source, style, contact timing, duration, target, base damage, crit, interruption and contact count |
| `attack.combo_step` | Execution and the primary combo step it plays |
| `attack.aim_locked`, `attack.facing_committed` | Execution, captured origin/direction and time until projectile release; elapsed time and committed melee facing |
| `poise.broken` | Break stagger duration |
| `harness.automatic_action` | Accepted repeated behavior; ordinary executor events describe the resulting action |
| `defense.decision`, `defense.observed`, `defense.request`, `defense.started`, `defense.riposte` | Policy chances/rolls, whether the observed attack was safe to guard, source observation, delay/distance, selected defense, accepted timing/sector/chip settings and actual request result; riposte acceptance or rejection reason, current distance and reach |
| `defense.out_of_sector`, `defense.guard_broken`, `defense.pass_through_exit`, `defense.perfect_dodge` | Facing and threat direction of an attacker outside the guard sector; guard-break damage and stagger; time spent leaving a pass-through body; dodge time and charges after a perfect-dodge refund |
| `slots.reserved` | Attacker, slot kind and slots now held |
| `projectile.spawned`, `projectile.contact`, `projectile.world_impact`, `projectile.expired` | Projectile identity, parent attack identity, owner, origin/direction, speed/range, seed, defensive flags, contact and impact |
| `damage.resolved` | Source/owner/execution, delivery/eligibility, incoming direction, raw amount, armor and its roll, poise damage, hit reaction and remaining poise, hit zone, sneak attack, critical roll, final outcome, damage and health |
| `simulation.tick`, `actor.state` | Delta/time scale; actor position, velocity, facing, health, action/phase, attack identity, stagger and defense timers, parry-window eligibility and dodge immunity |

The analyzer validates schema, run identity, record sequence and monotonic clocks. It summarizes event counts, outcomes, interrupted attacks, damage per target and rejected commands. `--require-complete` rejects a truncated capture. Keep raw records for contact-by-contact analysis; aggregates alone cannot explain timing or geometry.

`CombatTrace` is disabled without subscribers. Its scene recorder owns subscription, scope, file and bounded history; logging holds no gameplay registry. A failing sink is detached so diagnostics cannot abort combat. The wider Player/Internal/Debug channels, settings and in-game feed remain a separate [logging proposal](specs/logging-plan.md).

Tactical engagement emits `ai.tactical_decision` when the selected movement goal/reason changes, including `allow_attack`. Use it with attack acceptance/completion and defense events to distinguish a blocked escape, elapsed retreat deadline, required shot opportunity and close-pressure stand-and-fight choice. The selector does not bypass executor rejection or action commitment.
