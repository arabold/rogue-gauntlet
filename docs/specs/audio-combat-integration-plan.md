# Audio integration with current combat

Status: proposed rework of the audio prototype in [PR #96](https://github.com/arabold/rogue-gauntlet/pull/96). Reviewed on 2026-11-10 against `origin/main` at `7641f9ce` and `codex/audio-system` at `6cdbf456`. This is a source review and implementation plan; the updated integration has not been exercised in Godot.

The [audio concept](audio-system-plan.md) owns sound direction, mixing, assets and remaining content. Current [weapon attacks](../weapon-attack-system.md), [combat rules](../combat-system.md), [monster authoring](../monster-authoring-design.md) and the [combat harness](../combat-harness.md) own gameplay. This plan replaces the prototype's combat integration decisions where they conflict with those systems.

## Findings from main

| Current seam | Consequence for audio |
| --- | --- |
| `EnemyCombatProfile.Actions` contains polymorphic `MonsterAction` Resources. `MonsterAttackAction` and `MonsterSpellAction` use `AttackController`; `MonsterDashAction` uses `DashComponent`. | Observe the executors. AI selection, cooldown checks, target tracking and action weights must not emit attack sounds. |
| Monster-only definitions are now sub-resources in `*_combat.tres`. The Warden has slash, overhead and Tremor; the Mage has a missile volley, Fireball, regeneration, self healing and Rage. | Port cues to those actual definitions/actions. The prototype's standalone Warden/crossbow attack files must not be resurrected during integration. |
| `PreparedAttack` captures timing, resolved animation, feedback, statuses and optional `SpellRelease`. | Resolve sound choices during preparation and use the accepted snapshot for playback. Re-reading current equipment at release can select the wrong focus. |
| `AttackExecution` owns timing, skipped contact intervals and hit-stop. | Its existing release branches supply sound timing; animation callbacks, visual trails and independent timers are unsuitable authorities. |
| Paired melee uses the main weapon's attack and a dual-wield animation binding, with an off-hand damage contribution. It resolves one combined contact per target. | Play one swing cue for the execution and one impact per resolved target contact. The off-hand weapon adds neither another swing event nor another impact. |
| `SpellCastAction` identifies the focus and its hand. Wands have independent cooldowns; another focus can replace a released cast's recovery. | Both hands use the same preparation path. Capture the chosen focus's cue before execution; a handoff must not replay the first release or stop its released effects. |
| `ReleaseSpell` can report `Fizzled` after target revalidation or `Cast` fails. The prototype emits its release signal before this result. | Move successful spell-release notification after the existing result is known. Failed casts must not sound successful. |
| `CombatContactResolved` and target `ImpactSurfaceProfile` still provide the contact seam. | Retain centralized outcome/material/element impact selection. Voices and swing sounds remain separate from impacts. |
| `GameCameraRig` now owns following/rotation/zoom and is shared by gameplay and `DebugArena`. `Main` has become a small scene coordinator. | Move the player-centered listener into that rig. Replace the old harness camera/listener patch with the shared scene wiring. |

The prototype also has two local coupling problems: its actor-wide release fallback can turn any unauthored spell into a physical swish, and its required movement validation can disable attack audio on an actor without locomotion. Both are addressed below.

## Ownership and authoring

Keep `AudioController` as the playback coordinator: buses, pooled voices, admission, variation, music and preferences. Keep `ActorAudioComponent` as the actor's audio capability: subscribe to local execution/movement/defense events and emit world cue requests. The existing `SignalBus` presentation events connect them. Audio never participates in action acceptance, mana, AI or damage resolution.

Existing profiles have distinct jobs:

| Authoring surface | Audio responsibility |
| --- | --- |
| `AttackDefinition.WindUpCue` / `ReleaseCue` | Mechanical preparation and release of that move, including a spell's `SpellDefinition.Cast`. |
| `Weapon.PrimaryReleaseCue` / `SecondaryReleaseCue` | Explicit fallback for generated weapon attacks or a bound spell whose cast definition has no release cue. |
| Proposed `MonsterTimedAction.VoiceCue` | Optional creature vocal at accepted start of this particular attack or spell. Assigning it to a support cast is an explicit authoring choice. |
| `MonsterDefinition.DeathCue` -> `DeathComponent` | Creature death, once at actual death. |
| `ImpactSurfaceProfile` | Confirmed damage, block, parry, guard break and elemental impact sounds on the defender. |
| `SoundCueProfile` | Clip variants, pitch range, gain, spatial settings and voice limits. |
| `AttackAnimationProfile`, `MeleeAttackProfile`, `CombatFeedbackProfile` | Their existing rig/timing, contact geometry and source feedback responsibilities. Sound selection does not depend on animation-name parsing or contact geometry. |

The existing optional cue fields are enough for this rework. Null means deliberate silence after the applicable weapon fallback is resolved. Invalid sound content warns and skips playback; it cannot reject a valid gameplay action.

Remove `ActorAudioComponent.AttackReleaseCue`, `ActorAudioComponent.AttackVoiceCue` and `MonsterDefinition.AttackVoiceCue`. Migrate creature vocals to the timed actions that should use them, and remove the corresponding `EnemyBase` injection. Keep death injection in `_EnterTree()` so children are configured before readiness. Reusable creature cue Resources can be assigned to several actions without duplicating recordings.

For unarmed attacks, add explicit `PlayerAttackController.UnarmedPrimaryReleaseCue` and `UnarmedSecondaryReleaseCue` exports. `CreateDefaultDefinition` places the selected cue on the generated unarmed definition. This preserves unarmed audio without giving every actor or spell an implicit swish.

`ActorAudioComponent.Actor` is required. Its attack, movement, dodge, dash and guard dependencies are independently optional, explicitly wired exports. Validate step cadence only for the assigned locomotion capability; failure there must not prevent attack subscriptions. Sample resolved movement at `CombatPhysics.Commit`, after the movement component. Use `SubscribeUntilExit` for sibling/ancestor emitters and reset pending movement cues on exit/re-entry.

## Preparation and execution contracts

Retain `AttackDefinition.WindUpCue` and `ReleaseCue`. Add only the following cross-boundary contracts; names marked here are proposed additions/changes:

```csharp
// MonsterTimedAction: inherited by attacks and shared spell casts.
[Export] public SoundCueProfile VoiceCue { get; set; }

// PreparedAttack: append an optional positional record parameter.
SoundCueProfile VoiceCue = null

// AttackController: local Godot signals; payloads describe this execution.
[Signal] public delegate void AttackStartedEventHandler(
    AttackDefinition definition, SoundCueProfile voiceCue);
[Signal] public delegate void AttackReleasedEventHandler(AttackDefinition definition);

// AttackController: return the result it already computes, without another cast.
private bool ReleaseSpell(SpellRelease release);
```

`MonsterAttackAction.TryStart` and `MonsterSpellAction.TryStart` copy their `VoiceCue` into the prepared record before calling `StartAttack`. This is presentation data; shared action Resources retain no execution state. `AttackStarted` fires once after successful action/clock/animation setup. It supplies the accepted definition and voice, so the adapter does not query mutable `CurrentAttack` to discover what started.

Resolve player release cues with this precedence:

1. The selected custom/generated attack's `ReleaseCue`, including `SpellDefinition.Cast.ReleaseCue`.
2. The actual acting weapon's primary or secondary cue, as applicable. `SpellCastAction` uses its captured `_focus` for either hand.
3. The explicit unarmed default when preparing an unarmed attack; otherwise silence.

Do this on the existing copied definition during preparation. Preserve main's combo step, `DualWieldStyle`, contact, feedback, on-hit status and spell capture logic. `SpellCastAction` must retain its hand validation, shared `SpellCaster.TryPrepare`, mana checks and recovery-handoff rules. Preparation emits no sounds and modifies no authored Resources. Cue Resources are shared immutable configuration; random variation, concurrency and cooldown state remain in playback.

Release notification rules:

- **Melee:** once when the execution first overlaps the contact interval, before sampling targets. A miss still swishes. A tick skipping the whole interval still releases once. Hit-stop cannot retrigger it.
- **Projectile volley:** once after the first projectile has been initialized successfully. Later staggered projectiles use the same release and do not replay the cast cue. Reuse the execution's release counters; missing projectile content cannot announce a successful shot.
- **Spell:** once only when the existing `ReleaseSpell` operation returns true. Revalidation, `Cast`, RNG draws and mana spending each keep their existing single invocation and ordering. A fizzle remains silent at release; its accepted wind-up may already have sounded.
- **Cancellation:** before release, no release sound. After release, recovery cancellation does not replay or revoke it. A played one-shot may finish its short tail; there is no delayed callback waiting to fire after cancellation.

An instantaneous spell can report target contacts inside `Cast` before it returns true. Its release notification therefore follows those contacts in the same simulation tick. Accept that ordering; contact playback does not wait for a release event. A separate preflight/commit protocol for spell audio is unnecessary.

Keep a local reference to the accepted definition/execution around release operations. A synchronous contact or callback can interrupt the caster: a committed cast still reports its original release, and subsequent processing must check that the execution is still current. Reading `_currentAttack` after `Cast` could otherwise select null or a replacement attack.

Attack-start and release events are independent of `EffectsEnabled`, visual trails, warning visibility and debug overlays. Presentation must not alter gameplay RNG draws or execution phase boundaries.

## Dual wield and positional sound

For paired melee, the main weapon chooses the cue as it chooses the move. Combo steps and primary/secondary techniques each remain ordinary executions. The off-hand contribution does not create a second audio layer. If listening later calls for a fuller paired swing, author a suitable main cue without changing contact semantics.

For two wands, resolve K from the main focus and L from the off-hand focus. Even two copies of the same spell keep their own cooldowns and spell ownership. A recovery handoff starts a new execution with the new captured cues; the first cast's world effects, projectiles and admitted sound tails keep their existing lifetimes. J remains the main weapon's free missile volley.

Personal swing/cast cues stay centered or narrowly panned at the actor. Do not map keyboard inputs or inventory hands to hard left/right audio. Enemy and target contact sounds retain world positioning, attenuation and exploration gating.

Add an exported `AudioListener3D` and tunable listening height to `GameCameraRig`, wired in `game_camera.tscn`. Its follow/update path keeps the listener at the player's position and camera orientation and makes it current for the active rig. Rotation changes stereo direction; zoom does not change world loudness. Both gameplay and the combat arena receive this through their existing rig instance. The dedicated audio harness retains its explicit diagnostic listener.

## Monster dashes and contact feedback

Rat rush and retreat are `DashComponent` displacement, not attacks or player dodges. Add optional `DashProfile.TravelCue` plus local `DashStarted(DashProfile profile)` and `DashEnded()` Godot signals to that component. Emit start only after `TryDash` succeeds; emit end once for completion, interruption or exit. The actor audio adapter captures the cue on start, plays it once after actual resolved override displacement, and clears pending state on end. A dash blocked before travel stays silent. It produces no attack release or damage impact.

Port the existing contact audio fields and mappings onto main's `ImpactSurfaceProfile`. `AudioController` remains the sole contact-audio consumer: one outcome cue per resolved contact, at `feedback.Position`. A paired hit is still one contact per defender; projectile and area hits on different defenders remain distinct. Dead/rejected/evaded/absorbed contacts stay silent. Existing voice limits bound volleys and area spells; do not collapse legitimate contacts globally by execution ID. Death comes from `DeathComponent`, independently of the material impact.

## Control flow and invariants

```text
Player action slot -> actual Weapon / SpellCastAction(_focus, _hand)
  validate and prepare using main's combo / paired-style / shared-spell logic
  resolve release fallback on copied definition -> PreparedAttack
  StartAttack accepted -> AttackStarted(snapshot definition, captured voice)
    ActorAudioComponent -> wind-up / optional vocal -> WorldSoundEmitted

EnemyCombatProfile.Actions -> MonsterActionRuntime -> selected action.TryStart
  MonsterAttackAction / MonsterSpellAction
    prepare -> attach action VoiceCue -> same AttackController start/release path
  MonsterDashAction -> DashComponent.TryDash accepted -> DashStarted
    actual displacement -> ActorAudioComponent -> one travel cue
    DashEnded -> discard pending travel cue

AttackExecution contact/release boundary
  melee overlap / first initialized projectile / successful spell cast
    AttackReleased(snapshot definition) -> ActorAudioComponent -> release cue
  hurtbox resolves real contact -> CombatContactResolved
    AudioController -> target surface + outcome + element -> impact cue
  health death -> DeathComponent -> death cue

World cue -> existing admission / variation / pooled positional playback
  missing or rejected cue changes presentation only
```

No AI action selection or repeated aiming update emits an attack cue. No equipment lookup at release can change the prepared sound. A restored actor/item/ward and a floor rebind do not synthesize start, release or death events.

## Implementation slices

### 1. Reconcile main and establish the shared execution contract

Rebase the audio branch onto the reviewed main, resolving against main's current implementations and authored assemblies. Preserve its shared spells, dual wield, on-hit statuses, consolidated monster resources, debug toolkit and resource-cache guard. Keep its current save schema/version; audio preferences remain separate from run saves.

Port the existing first-player-acquisition notification through the current `EnemyContext.SetTarget` path while preserving the constructor's new `requestAction` delegate. `RealmEncounterController` keeps its one-shot screen-entry/acquisition guard and death transition; repeated aiming updates do not retrigger boss music.

Implement the preparation, signals, release-success rules, optional actor dependencies and shared-rig listener above. Migrate existing monster vocals to eligible timed actions, existing Warden/crossbow cues into their current combat resources, and the generic player fallback to explicit weapon/unarmed settings. Every caller and scene must be coherent in this slice. Existing music, UI, floor-step, prop and inventory cues remain on their established paths.

**Exercise:** menu -> real floor and combat arena; single sword, paired melee, both wand inputs and a three-dart primary; cancel before release; replace a released cast's recovery. Check that a shared instant heal that becomes unnecessary before release produces no successful cast cue. Unauditioned new moves may be explicitly silent at this point.

**Verify:** Debug and ExportRelease builds; Godot import/resource validation of the controller, shared camera rig, player, monster/template scenes, combat arena and audio harness. Add a focused `tests/CombatAudioTest.cs` observing emitted cue requests rather than speakers: accepted/rejected start, missed melee, skipped interval/hit-stop, fizzle, synchronous caster interruption after a committed cast, per-focus fallback, combined paired contact and recovery handoff. Include an attack-only adapter without a movement dependency. Tests use `TestFactories`, fixed deltas and isolated `TMPDIR` per repository rules.

### 2. Author the multi-action roster and dash capability

Implement dash signals/cue authoring. Assign intentional sounds or deliberate silence to each active timed action. Start with Warden slash/overhead/Tremor, Mage volley/Fireball/healing/Rage and rat bite/rush/retreat, then review the other skeletons. Shared spell cast definitions own spell sounds; a monster action supplies only its optional vocal. Use the acquired packs first and keep provisional mappings clearly identified in the audio concept's content coverage.

**Exercise:** the Warden changes moves across all three health phases; Mage offense and support sound distinct; rat travel sounds follow actual dash movement. Trigger support spells with injured allies/self and then invalidate their targets before release. Confirm defense contacts still produce one plate/tin impact, and death plays once.

**Verify:** extend the focused audio suite for multiple actions sharing one executor, support/fizzle, volley-once and blocked/canceled dash travel. Reuse existing `MonsterActionTest`, `WardenTest`, `SpellWandTest` and contact regression cases where affected. Monster validation must accept unassigned optional cues. Save/reopen an authored action and spawn two actors to check independent playback state.

### 3. Accept timing and consolidate the audio documentation

Use the production combat arena and its shared item/spell/monster debug menu for synchronization review. Its action diagnostics and normal AI expose the chosen move; configure health, phase and targets to exercise the roster. The audio harness remains the place to audition individual clips, stereo, distance and voice limits. The first pass needs no duplicate weapon/monster selector in that harness.

**Exercise:** primary combos, both paired techniques, two different wands, two wands with the same spell, mixed melee/wand, two-handed staff and crossbow. Check interruptions from guard, dodge, stagger, death and equipment removal; turn VFX off; rotate/zoom the camera; pause; change floors; save/load with an active ward. Listen at normal speed after using slow motion to inspect timing. Confirm Music and Effects/UI volume settings, mono, first-seen/first-target-acquisition boss music and scene-exit cleanup still work.

**Verify:** run the targeted affected suites once after integration and broader regression checks only for new failures or concerns. Human listening establishes audibility and mix quality; headless tests establish event counts, ordering and resource wiring. Consolidate implemented ownership/authoring into `docs/audio-system.md`, leave remaining content work in the original concept, and update the index/roadmap to the actual delivered state.

## Affected paths

```text
~ scenes/attacks/{AttackDefinition,PreparedAttack,AttackController}.cs
~ scenes/player/{PlayerAttackController.cs,player.tscn}
~ scenes/player/actions/SpellCastAction.cs
~ scenes/items/Weapon.cs and selected weapon Resources
~ scenes/audio/ActorAudioComponent.cs
~ scenes/audio/AudioController.cs                  # retain resolved-contact routing
~ scenes/enemies/actions/{MonsterTimedAction,MonsterAttackAction,MonsterSpellAction}.cs
~ scenes/enemies/{MonsterDefinition,EnemyBase}.cs  # migrate global attack voice
~ scenes/enemies/EnemyBehaviorComponent.cs, scenes/enemies/ai/EnemyContext.cs
~ scenes/enemies/**/{*_combat.tres,*_definition.tres,*.tscn}
~ scenes/levels/encounters/RealmEncounterController.cs
~ scenes/combat/{DashComponent,DashProfile}.cs
~ scenes/effects/combat/ImpactSurfaceProfile.cs and authored surface Resources
~ scenes/magic/spells/*.tres and their cast definitions, selected in slice 2
~ scenes/main/camera/{GameCameraRig.cs,game_camera.tscn}
~ scenes/main/{Main.cs,main.tscn}                  # listener ownership moves to shared rig
~ scenes/debug/combat/{CombatHarness.cs,combat_harness.tscn}
~ project.godot, scripts/SignalBus.cs, session/audio scenes as integration requires
+ tests/CombatAudioTest.cs
~ docs/specs/audio-system-plan.md, docs/README.md, docs/development-roadmap.md
+ docs/audio-system.md                            # implemented rules after acceptance
```

All changes migrate current callers/assets directly. Remaining creative work is cue auditioning and mix tuning; extra music layers, sustained spell-loop management and complete item-use/ambient coverage remain with the original audio concept.
