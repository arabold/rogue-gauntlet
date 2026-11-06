# Combat harness and machine traces

Open `scenes/debug/combat/combat_harness.tscn` and run it with F6. The harness uses production player equipment, action slots, movement, damage resolution, and monster defense components. It starts with a passive movable dummy, or you can select any valid authored monster scene discovered under `scenes/enemies`. **Monster AI (walk + attack)** is off by default; turn it on to let the selected monster move, chase, and attack using its normal behavior. With AI off, monsters still react to player hits and defend themselves. The defense selector offers profile default, no defense, always dodge, and always guard/parry. Target invulnerability is a separate setting. Arena monsters suppress dungeon loot and experience rewards.

The debug controls cover slow motion, optional hit-volume and hurtbox drawing, combat feedback, and camera shake. The target can be made invulnerable without changing the monster's defense mode. The dummy uses the same health, hurtbox, movement, poise, action gate, damage numbers, and feedback components as a monster, so knockback and defensive responses can be inspected without loading a dungeon.

The `monster_attack` scenario command requests one attack through the spawned monster's production behavior while AI remains off. Use it to inspect that monster's warning and attack timing without enabling autonomous movement.

The control panel is mouse-operated so keypad, movement keys, and Space stay available to control the hero.

Checkbox defaults apply immediately on startup and to newly spawned monsters. `--combat-console` also checks the console logging option.

For ranged warning practice, choose **Skeleton Crossbow** and spawn it. During aiming, its translucent trapezoid starts at one-third range and grows and brightens as the cone narrows toward the projectile lane. At commitment, it becomes the existing bar and extends to the clear attack range past the player. The same reusable indicator handles other weapon attacks; the component's Inspector preview can show the cone and its narrowing. Move sideways or dodge on commitment. Slow motion helps inspect the phases.

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

Use `scenes/debug/combat/scenarios/projectile_practice.json` with `--combat-frames=240` for repeated crossbow shots, steering-directed dodges and aim-lock records. `magic_missile_practice.json` with `--combat-frames=320` fires the tier-1 wand's missile volleys at a minion, then the tier-5 wand's at a guarding warrior and a rat; both carry Fireball, so both volleys are fire. `combo_buffer.json` with `--combat-frames=240` chains a sword combo from buffered presses, buffers a dodge out of the finisher's contact, spends both dodge charges and switches to the axe combo.

`--combat-console` additionally echoes records with the `COMBAT ` prefix. `--combat-log` accepts an absolute path or `user://` path and creates a fresh file, flushing each entry. The destination directory must exist. Without a path the recorder keeps the most recent 2048 records in memory, exposed as `Recorder.RecentRecordsJson`; a file capture retains the complete run. Inspector `Seed`, `TracePath`, recorder `CaptureFrames` and `EchoToConsole` configure interactive sessions. Console/frame-capture checkboxes are available in the arena. Engine errors remain in the separate Godot log, so retain both files when investigating a failure.

Effect events are echoed to Godot's Output panel by default, even when `--combat-console` is off. Filter for `COMBAT {"schema"` or `"event":"effect.` to see the effects and their values without per-frame noise. Events include `effect.hit_stop` (requested and applied duration), `effect.knockback` (direction and strength), `effect.lunge` (distance, duration and speed), `effect.camera_shake` (requested impulse and resulting noise amplitude), `effect.hit_flash` (amount and duration), `effect.visual_recoil` (distance and duration), `effect.melee_swoosh` (scene, reach, blade-follow status, sample count and generated geometry), `effect.particles` (cue, scene and particle amount), and `effect.damage_number` (kind and amount). Each includes `triggered`; suppressed or unconfigured effects include a reason. Use `--combat-console` to echo the full trace, or `--combat-log=/tmp/combat.jsonl` to save all records for later inspection.

## Commands

Scripts are JSON arrays, ordered by `Frame` (physics-frame offset), retaining file order for commands on the same frame. Frame zero executes on the first available tick. `Command` names are lowercase. `Value` is an integer selection; `X` and `Z` form a world-space vector. Script playback turns off device input (`InputComponent.DeviceEnabled`) so keyboard state cannot perturb the run; the player's input controller keeps running, so scripted presses use the real buffer and recovery cancels. Buttons and scripts share `ExecuteCommand(string, int, Vector3)` and acceptance is recorded.

| Command | Arguments |
| --- | --- |
| `equip` | `Value`: weapon index, shown in the weapon selector |
| `spell` | `Value`: spell index, shown in the spell selector; an equipped wand or staff takes it for its secondary |
| `spawn` | `Value`: 0 practice dummy; 1 onward follows the locally discovered monster list shown by the selector |
| `defense` | `Value`: 0 profile default, 1 no defense, 2 always dodge, 3 always guard/parry |
| `automation` | `Value`: 0 disable monster AI, 1 enable monster AI (movement and attacks) |
| `attack`, `secondary`, `monster_attack` | No arguments; same action ownership as gameplay |
| `guard`, `release_guard` | Set or release the hero's held guard intention |
| `dodge` | `X`, `Z`: direction; zero uses the player's movement/backstep choice |
| `press_primary`, `press_secondary`, `press_dodge` | No arguments; buffers a fresh press in the input controller, like a button press (acceptance means buffered). A buffered dodge uses the current `move` steering |
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
