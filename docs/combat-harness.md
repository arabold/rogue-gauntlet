# Combat harness and machine traces

Open `scenes/debug/combat/combat_harness.tscn` and run it with F6. The arena is a `DebugArena` with the production player, HUD, game camera and lighting on the dungeon's stone floor, so it looks and plays like a dungeon floor: production equipment, action slots, movement, damage resolution, mana and monster defense components. The camera follows the hero at the game's angle and turns and zooms with the game's bindings. It starts with a passive movable dummy.

Items, spells, monsters and display settings come from the HUD's [debug menu](../scenes/debug/README.md#in-game-debug-menu), the same one the game has, so every authored weapon, wand, staff, shield, piece of jewelry and spell is available. In the arena, **Items** equips what you pick at once. **Monsters → Spawn** lists the dummy and every valid monster scene under `scenes/enemies`; it adds to the arena, so use **Clear spawned** first to replace the target. **Monster AI** is off by default; turn it on to let spawned monsters move, chase and attack using their normal behavior. With AI off, monsters still react to player hits and defend themselves. **Defense** offers monster default, no defense, always dodge and always guard/parry; **Invulnerable** is a separate setting. Arena monsters suppress dungeon loot and experience rewards unless **Rewards** is checked.

**Combat** covers slow motion, hit-volume and hurtbox drawing, hit effects and camera shake. The dummy uses the same health, hurtbox, movement, poise, action gate, damage numbers and feedback components as a monster, so knockback and defensive responses can be inspected without loading a dungeon.

The `monster_attack` scenario command requests one attack through the spawned monster's production behavior while AI remains off. Use it to inspect that monster's warning and attack timing without enabling autonomous movement.

The harness panel on the right keeps what only the arena has: hero attack, guard and dodge buttons, **Restore health**, the status line, the monster's action list and the combat log. It is mouse-operated so keypad, movement keys and Space stay available to control the hero.

The monster section lists the spawned monster's actions for tuning: its health phase, current tactical decision and running action, then each action's priority, effective weight (after weight modifiers) and whether it is ready, cooling down or out of position, followed by the last choice or rejection. Spawning re-creates the monster from its scene, so Resource edits made through the Remote inspector apply to the new instance. Trace events `ai.action_attempt`, `dash.started`, `dash.blocked`, `dash.finished` and `enemy.phase_changed` record the same decisions.

For player dodges, current processed movement input selects the heading; a released direction remains available for 0.08 seconds, then the player's facing is used. Space starts the dodge immediately. A new direction can correct an input-driven dodge during its first 0.04 seconds while the captured facing stays in place, making the correction a sidestep. Use slow motion to inspect timing, then review the curved path at normal speed.

The display settings authored on the arena's `CombatDisplayTool` apply on startup and to every monster spawned later.

For ranged warning practice, spawn the **Skeleton Crossbowman**. During aiming, its translucent trapezoid starts at one-third range and grows and brightens as the cone narrows toward the projectile lane. At commitment, it becomes the existing bar and extends to the clear attack range past the player. The same reusable indicator handles other weapon attacks; the component's Inspector preview can show the cone and its narrowing. Move sideways or dodge on commitment. Slow motion helps inspect the phases.

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

`--combat-console` additionally echoes records with the `COMBAT ` prefix. `--combat-log` accepts an absolute path or `user://` path and creates a fresh file, flushing each entry. The destination directory must exist. Without a path the recorder keeps the most recent 2048 records in memory, exposed as `Recorder.RecentRecordsJson`; a file capture retains the complete run. Inspector `Seed`, `TracePath`, recorder `CaptureFrames` and `EchoToConsole` configure interactive sessions. Engine errors remain in the separate Godot log, so retain both files when investigating a failure.

Effect events are echoed to Godot's Output panel by default, even when `--combat-console` is off. Filter for `COMBAT {"schema"` or `"event":"effect.` to see the effects and their values without per-frame noise. Events include `effect.hit_stop` (requested and applied duration), `effect.knockback` (direction and strength), `effect.lunge` (distance, duration and speed), `effect.camera_shake` (requested impulse and resulting noise amplitude), `effect.hit_flash` (amount and duration), `effect.visual_recoil` (distance and duration), `effect.melee_swoosh` (scene, reach, blade-follow status, sample count and generated geometry), `effect.particles` (cue, scene and particle amount), and `effect.damage_number` (kind and amount). Each includes `triggered`; suppressed or unconfigured effects include a reason. Use `--combat-console` to echo the full trace, or `--combat-log=/tmp/combat.jsonl` to save all records for later inspection.

## Commands

Scripts are JSON arrays, ordered by `Frame` (physics-frame offset), retaining file order for commands on the same frame. Frame zero executes on the first available tick, after the dummy is placed. `Command` names are lowercase. `Path` names a resource by its `res://` path, so adding content never changes what a script uses; `Value` is an integer; `X` and `Z` form a world-space vector. Script playback turns off device input (`InputComponent.DeviceEnabled`) so keyboard state cannot perturb the run; the player's input controller keeps running, so scripted presses use the real buffer and recovery cancels. Buttons and scripts share `ExecuteCommand(string command, string path, int value, Vector3 direction)` and acceptance is recorded.

| Command | Arguments |
| --- | --- |
| `equip` | `Path`: item definition; equips an exact authored copy, replacing the one the previous `equip` gave |
| `spell` | `Path`: spell definition; the equipped wand or staff, and any equipped later, takes it for its secondary |
| `spawn` | `Path`: monster scene, empty for the practice dummy; replaces the arena's targets. `Value`: distance ahead of the hero in metres, 0 for the monster's preferred distance |
| `build` | `Path`: `HeroBuildProfile`; replaces the hero's attributes and inventory with the build's exact items (see `scenes/debug/hero_build_profiles/`) |
| `defense` | `Value`: 0 profile default, 1 no defense, 2 always dodge, 3 always guard/parry |
| `automation` | `Value`: 0 disable monster AI, 1 enable monster AI (movement and attacks) |
| `attack`, `secondary`, `monster_attack` | No arguments; same action ownership as gameplay |
| `guard`, `release_guard` | Set or release the hero's held guard intention |
| `dodge` | `X`, `Z`: direction; zero resolves current/recent input, then facing. This direct command stays locked after it starts |
| `press_primary`, `press_secondary`, `press_dodge` | No arguments; buffers a fresh press in the input controller, like a button press (acceptance means buffered). A buffered dodge resolves the latest current/recent input when accepted and allows steering correction during its first 0.04 s |
| `monster_guard`, `monster_dodge` | `X`, `Z`: facing/direction |
| `move` | `X`, `Z`: movement input; zero stops input |
| `position_player`, `position_monster` | `X`, `Z`: explicit arena placement; height is retained |
| `reset` | Restore the hero's health, mana and poise and cancel their commitment; restore the practice target; heal a living monster |

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
