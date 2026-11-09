# Monster action architecture

Status: slices 1–4 landed on PR #95, replacing the action layer that PR first introduced; the deferred items below remain. [Monster authoring](../monster-authoring-design.md), [combat](../combat-system.md) and [weapon attacks](../weapon-attack-system.md) describe the implemented foundation. The [roadmap](../development-roadmap.md) owns delivery.

## Goal

A new monster is a scene plus Resources. A new kind of behavior is one new Resource class. Designers tune and playtest monsters by changing attributes, conditions and strategies in the Inspector. They never write per-monster code and never edit the behavior host, the attack executor or the validator to add an action type.

Keep what PR #95 got right:
- actor-neutral buffs (`IBuffTarget`) and the shared `StatModifierLedger`;
- `SpellCastContext`, with `SpellCaster` serving weapon, innate and item casts;
- per-actor cooldowns and repeat penalty, plus the seeded `combat.action` stream;
- cooldowns that start only when an action actually starts;
- on-hit statuses that apply only after defenses resolve.

This plan does not add a behavior tree, a utility-AI framework or a global registry. The FSM, `EnemyContext`, movement/tactical strategies and the action gate stay.

## Problems this replaces

| PR #95 shape | Problem | Replacement |
| --- | --- | --- |
| `EnemyBehaviorComponent` branches on `EnemyRushProfile`/`EnemySpellProfile`/`EnemyAttackProfile`; `EnemyAction` gains `SpellCast`, `Rushing` | Every action kind edits the host | Polymorphic `MonsterAction.TryStart` returns a `MonsterActionRun`; the host holds one run |
| `AttackController` casts `release.Source as EnemySpellProfile` | Shared executor depends on enemy AI | `SpellRelease.Revalidate` callback |
| Rush inside `MovementComponent` (≈200 lines) | Mover owns an action, gate lease and animation | `DashComponent` sibling, like `DodgeComponent` |
| Phases inside the behavior host; `EnemyDefenseController` calls `PrepareDecision()`; full combat profile per phase; two-transition cap | Coupling, duplication, arbitrary limit | Optional `HealthPhaseComponent` + `HealthPhaseCondition`; no cap |
| Eligibility fields (`MinimumDistance`, `ClosePressure*`, `RequiresHostileTarget`, `HealingHealthThreshold`) and effect type sniffing | Every new "only when…" needs code | Target selectors + composable condition Resources + weight modifiers |
| `ApproachAction` duplicates an `Actions` entry | Two sources of truth | The first offensive action is the primary one |
| `EnemyActionChoiceProfile` wrapper; `EnemyActionSlotKind` mirrors `AttackSlotKind` | Extra depth, repeated enum mapping | Weight lives on the action; slot kind is derived |
| `ExecuteTactics` runs `AllowAttack \|\| HasEligibleAction()` | Retreating and fleeing monsters still attack | Offensive actions only when the tactical decision allows them |
| `MonsterSceneValidator` sniffs script paths and reads fields by name; validation also runs every frame | New strategies need validator edits | Monster Resources are `[Tool]` and validate themselves once |
| `ActorStatsComponent` resets armor to 0; `EnemyBase` re-assigns wired exports | Monster armor can't be authored | `MonsterDefinition.Armor`; stats component is the only writer of base + modifiers |
| Camera shake fields on `GroundImpactProfile` and `AreaDamageDefinition`; `GroundImpactProfile` has no content | Duplication, dead feature | One `CameraShakeProfile`; remove `GroundImpactProfile` |
| `OnHitStatusProfile.StackKey` must equal the buff path; `PreparedHitStatus` duplicates the buff per attack | Dead config, allocation | Refresh identity is the buff's resource path; share the definition |
| `BuffController` implements `IBuffTarget` and returns itself | Circular contract | Private fallback adapter |
| Potions/scrolls carry both `Buff` and `Spell` | Two sources of truth | One effect source per item |

## Contracts

All authored types below are `[Tool] [GlobalClass]` Resources, one per file, so the editor's **Validate monster scene** command sees real instances instead of placeholders.

### Actions (`scenes/enemies/actions/`)

```text
abstract MonsterAction : Resource
  [Selection]  int Priority; float Weight = 1; float Cooldown = 1
  [Position]   float MinimumRange; float MaximumRange = 3; float MaximumStartAngle = 180
  [Targeting]  MonsterTargetSelector Target   // null = current hostile target, sight required
               bool UsesAttackSlot = true; bool RiposteEligible
  [Conditions] Array<MonsterActionCondition> Conditions = []
               Array<MonsterActionWeightModifier> WeightModifiers = []
  bool IsOffensive                       // the target selector is hostile
  virtual float Reach                    // executor reach; EffectiveMaximumRange = min(MaximumRange, Reach)
  virtual AttackSlotKind? SlotKind       // null = never reserves a slot
  virtual bool IsAvailable(EnemyContext)               // components present, mana affordable
  virtual bool IsUsefulOn(EnemyContext, Node3D target) // e.g. spell would change something
  abstract ActorActionRequestResult TryStart(EnemyContext, Node3D target, out MonsterActionRun run)
  virtual void Validate(MonsterValidationContext)

abstract MonsterTimedAction : MonsterAction   // runs on the actor's AttackController clock
  StringName Style; float Duration; float MinDamage; float MaxDamage; float CritChance
MonsterAttackAction : MonsterTimedAction      // AttackDefinition Attack
MonsterSpellAction  : MonsterTimedAction      // SpellDefinition Spell; int Level; bool PaysMana
MonsterDashAction   : MonsterAction           // DashProfile Dash; MonsterDashDirection Direction (Toward, Away)

abstract MonsterTargetSelector : Resource
  bool IsHostile; SpellTargetIntent Intent
  void CollectCandidates(EnemyContext, MonsterAction, List<Node3D> into)   // preference order
HostileTargetSelector (bool RequireLineOfSight = true) · SelfTargetSelector · AllyTargetSelector (bool IncludeSelf = true; most injured first)

abstract MonsterActionCondition : Resource
  bool IsMet(EnemyContext, Node3D target)
  virtual void Tick(EnemyContext, double delta)   // per-actor state lives in the context, keyed by condition
HealthBelowCondition (Subject: Actor | Target; float Fraction)
TargetPressureCondition (float Range; float Duration)    // target visible within Range for Duration
HealthPhaseCondition (int MinimumPhase; int MaximumPhase = -1)

MonsterActionWeightModifier : Resource   // MonsterActionCondition Condition; float Multiplier
```

`TryStart` either starts the executor and returns a run, or returns a failure and leaves no side effects (no slot, mana, cooldown or animation). Range is measured to the action's selected target, so it means the same thing for attacks, self spells and ally spells.

```text
abstract class MonsterActionRun          // plain C#, one per started action
  MonsterAction Action; Node3D Target; AttackSlotsComponent HeldSlots
  abstract bool IsFinished
  virtual void Tick(EnemyContext, double delta)   // facing through wind-up, dash steering
  abstract void Cancel()
```

### Runtime (`scenes/enemies/ai/MonsterActionRuntime.cs`, replaces `EnemyActionSelector`)

One per actor, owned by `EnemyContext`. It holds cooldowns, the retry delay, repeat history, condition state and a per-physics-frame evaluation cache.

```text
void ConfigureRandom(ulong seed)
void Tick(double delta)                          // cooldowns, retry, condition ticks (once per distinct condition)
Node3D EvaluateTarget(MonsterAction)             // cached per frame: first candidate in position, conditions met, useful
MonsterAction PrimaryOffense                     // first offensive action of the combat profile
bool CanEngage                                   // primary in position and its slot available; ignores cooldown
bool IsPrimaryInReach                            // primary range and sight only
(MonsterAction, Node3D)? Select(MonsterActionFilter)  // cooldown, slot, top priority tier, weights × modifiers × repeat
void Accepted(MonsterAction); void Rejected()
```

`MonsterActionFilter` is `All`, `SupportOnly`, `Attacks` (harness and tests trigger an attack) or `Riposte` (riposte-eligible attack actions only).

### Combat profile and phases

```text
EnemyCombatProfile : Resource
  EnemyTacticalStrategy TacticalStrategy; EnemyMovementStrategy MovementStrategy
  EnemyDefenseStrategy DefenseStrategy; EnemyRetreatCondition RetreatCondition
  Array<MonsterAction> Actions = []     // first offensive action is primary
  float RepeatWeightMultiplier = 0.5

EnemyBehaviorProfile : Resource         // perception/lifecycle + Combat; no phases

HealthPhaseComponent : Node             // optional, any monster
  [Export] HealthComponent Health; [Export] float[] EnterHealthFractions = []   // strictly descending in (0, 1)
  int CurrentPhase                      // latched; healing never lowers it
  [Signal] PhaseChanged(int previous, int current)
```

Phases only feed conditions and weight modifiers, which are read when an action is selected. Running actions are never interrupted, so no "safe boundary" hook is needed. Movement or defense overrides per phase are deferred until content needs them.

### Displacement (`scenes/combat/DashComponent.cs`)

```text
DashProfile : Resource      // StringName Style; WindUpDuration; Distance; TravelDuration; RecoveryDuration
DashComponent : Node        // [Export] Actor, MovementComponent, ActionGate, AnimationComponent
  ActorActionRequestResult TryDash(DashProfile, Vector3 direction, out AttackIdentity execution)
  bool UpdateDirection(AttackIdentity execution, Vector3 direction)   // wind-up only
  bool IsRunning(AttackIdentity execution)
  void Cancel()
```

Mirrors `DodgeComponent`: acquires `ActorActionKind.Dash`, drives `MovementComponent.SetLocomotionOverride`/`SetFixedFacing`, samples poses through `CombatAnimationComponent`, and stops travel when collision blocks it. It grants no immunity, pass-through or contact damage. `MovementComponent` returns to locomotion, overrides, push and lunge.

### Shared spells, stats and payloads

```text
ICombatSpell.IsUseful(in SpellCastContext) => CanCast(in context)   // default interface method
SpellRelease(ICombatSpell Spell, SpellCastContext Context, Func<SpellCastContext, bool> Revalidate = null)
MonsterDefinition.Armor : float
ActorStatsComponent.Initialize(MonsterDefinition)   // writes health (full), speed, armor; modifiers re-resolve from it
CameraShakeProfile : Resource   // Strength; Radius; GrowthTime; HoldTime; DecayTime
AreaDamageDefinition.CameraShake : CameraShakeProfile
OnHitStatusProfile              // Effect; Chance; RefreshKey = the buff's resource path when it refreshes
```

### Validation

`MonsterValidationContext` collects errors under a label and exposes the actor's animation bindings, clips, attack controller wiring and definition. `MonsterDefinition`, `EnemyBehaviorProfile`, `EnemyCombatProfile`, the strategies, actions, selectors and conditions implement `Validate(MonsterValidationContext)`. `MonsterSceneValidator` keeps node-wiring checks and walks the Resource graph. Validation runs once per scene and definition at spawn (cached as today) and in the editor command, never per frame.

## Orchestration

```text
EnemyBehaviorComponent._PhysicsProcess (Steering)
├─ EnemyContext.TickThrottles → MonsterActionRuntime.Tick (cooldowns, retry, condition state)
├─ gate disabled, or gate busy without a run → stop
├─ run active → run.Tick → finished: release held slots, CurrentAction = None
└─ idle, AI enabled
   ├─ no hostile target → TryRequestAction(SupportOnly)          // self/ally support while idle
   └─ state machine → Chasing/Fleeing → EnemyContext.ExecuteTactics
      ├─ movement strategy → intent (Goal, AllowAttack) using runtime.CanEngage / IsPrimaryInReach
      ├─ tactical selector → decision (retreat bounds, fallbacks)
      ├─ navigation for the goal
      └─ RequestAction(AllowAttack ? All : SupportOnly)        // invariant: retreat/flee/encircle never attack
         └─ TryRequestAction(filter)
            ├─ runtime.Select → (action, target)            // highest priority tier, weighted draw
            ├─ action.TryStart → run | failure (no side effects)
            └─ Accepted → cooldown + history | Rejected → retry delay
EnemyDefenseController.TryExecuteRiposte → reach from riposte actions' EffectiveMaximumRange → TryRequestAction(Riposte)
HealthComponent.HealthChanged → HealthPhaseComponent latches phase → PhaseChanged
AttackController release → SpellRelease.Revalidate (target alive, in range, visible, still useful) → spell.Cast
```

Strategy rules for `AllowAttack`: `CloseDistanceStrategy` allows attacks while approaching (so a secondary gap-closer can fire en route) and while holding. `AttackThenRetreatStrategy` retreat/wait, `EngagementRingStrategy` encircle, `MaintainDistanceStrategy` retreat and the low-health retreat condition do not. Support actions stay available in every chase state.

## Authoring examples

- **Plague Rat:** bite (`MonsterAttackAction`, primary) with venom on-hit; rush (`MonsterDashAction`, range 2–4.5, cooldown 4, no slot). Approach stops at bite reach; the dash fires en route when ready.
- **Skeleton Mage:** fireball (primary, 3–12 m); regenerate on the most injured ally below 75% (`AllyTargetSelector` + `HealthBelowCondition(Target, 0.75)`, priority 2); instant heal on self below 35% (priority 4); Rage on an ally (priority 1; the spell's usefulness rejects already-raged allies).
- **Forsaken Warden:** `HealthPhaseComponent` at 65% and 30%. Slash (primary) is down-weighted in phase 2; overhead and Tremor gain weight through `HealthPhaseCondition` modifiers; Tremor also needs phase ≥ 1 and `TargetPressureCondition(2.4 m, 0.85 s)`.

Monster-only actions are sub-resources inside the monster's `*_combat.tres`. Only shared strategies, spells, buffs and contact profiles are separate files.

## Affected paths

```text
+ docs/specs/monster-action-architecture-plan.md
~ docs/monster-authoring-design.md, combat-system.md, weapon-attack-system.md, stats-and-itemization.md, README.md
+ scenes/enemies/actions/{MonsterAction,MonsterTimedAction,MonsterAttackAction,MonsterSpellAction,MonsterDashAction}.cs
+ scenes/enemies/actions/{MonsterActionRun,MonsterActionWeightModifier}.cs
+ scenes/enemies/actions/{MonsterTargetSelector,HostileTargetSelector,SelfTargetSelector,AllyTargetSelector}.cs
+ scenes/enemies/actions/{MonsterActionCondition,HealthBelowCondition,TargetPressureCondition,HealthPhaseCondition}.cs
- scenes/enemies/actions/Enemy{Action,Attack,Spell,Rush,ActionChoice}Profile.cs
+ scenes/enemies/ai/MonsterActionRuntime.cs; - EnemyActionSelector.cs
~ scenes/enemies/ai/{EnemyContext,FleeingState}.cs; strategies/*Strategy.cs (Validate, AllowAttack rules)
~ scenes/enemies/{EnemyBehaviorComponent,EnemyBehaviorProfile,EnemyCombatProfile,EnemyBase,EnemyDefenseController}.cs
~ scenes/enemies/{MonsterDefinition,ActorStatsComponent,MonsterSceneValidator}.cs; + MonsterValidationContext.cs
+ scenes/enemies/HealthPhaseComponent.cs; - BossPhaseProfile.cs
+ scenes/combat/{DashComponent,DashProfile}.cs; - scenes/components/RushProfile.cs
~ scenes/components/MovementComponent.cs (rush removed); common/actions/ActorActionLease.cs (Rush → Dash)
~ scenes/actions/ActorActionGateComponent.cs (Dash rules)
~ scenes/attacks/{AttackController,AttackDefinition,PreparedAttack,PreparedHitStatus,OnHitStatusProfile}.cs; - GroundImpactProfile.cs
~ common/interfaces/ICombatSpell.cs; scenes/magic/** IsUseful boilerplate removed; scenes/components/SpellCaster.cs
+ scenes/effects/combat/CameraShakeProfile.cs; ~ CombatCameraFeedbackController.cs, common/combat/CameraImpulseRequest.cs
~ scenes/magic/shared/AreaDamage{Definition,Spell}.cs; scenes/magic/earth/tremor*_combat.tres
~ scenes/player/BuffController.cs; scenes/items/{potions/potion_healing_small,scrolls/scroll_rage}.tres
~ scenes/enemies/{rat,skeleton,forsaken_warden}/* actor scenes and combat Resources; enemy_base.tscn; templates
~ scenes/debug/combat/CombatHarness.cs (+ AI tuning panel, respawn)
~ tests: roster, authoring, Warden, combat integration, animation, melee contact, realm encounter
+ tests/MonsterActionTest.cs (selection, retreat never attacks, phases, dash)
```

## Slices

1. **Action model and host.** Actions, selectors, conditions, runtime, runs, host and tactics changes, riposte reach, validation walk, and every monster/template/test migrated together (the old types are removed, no aliases). Verify: build, roster/authoring/Warden/defense suites, new action tests, headless `main.tscn` smoke.
2. **Dash and phases.** `DashComponent` + rat migration; `HealthPhaseComponent` + Warden. Verify: dash bounds/blocking/gate release, phase latching and gated selection.
3. **Shared payload cleanups.** Stats/armor, spell revalidation, `IsUseful` default, single-source consumables, on-hit status identity, camera shake profile, buff target adapter. Verify: spell, buff, status and camera suites; save round-trip with venom.
4. **Tuning loop.** Combat harness panel listing each action's target, cooldown, weight and the last selection reason, plus a respawn button that keeps live Resource edits. Verify in the windowed harness.
5. **Docs.** Fold implemented behavior into the feature documents; trim this plan to remaining work.

Each slice runs `dotnet build "Rogue Gauntlet.sln"`, the targeted GdUnit4 suites with `GODOT_BIN` and an isolated `TMPDIR`, a Godot `--import`, and `dotnet build "Rogue Gauntlet.csproj" -c ExportRelease` before commit.

## Persistence

Surviving monsters already reset on floor reconstruction, so phase, cooldowns and dash state are not saved. A refreshing buff's saved key stays its resource path, so the player save schema is unchanged.

## Deferred and open

- `DodgeComponent` keeps its own displacement (immunity, charges, steering, pass-through). Moving its travel onto `DashComponent` is a later refactor once both are stable.
- Scene-tree configuration badges for monsters need `EnemyBase` to become `[Tool]`, which would run gameplay setters against editor placeholders; the Tools menu command stays the editor surface.
- Phase entry actions (roar, brief immunity) and per-phase movement/defense overrides wait for content that needs them.
- Plain buff potions and scrolls keep `BuffedItem.ApplyBuff`; an item moves to a spell only when a monster needs the same effect.
- The Warden's Tremor now reuses the player's Tremor spell (its duplicate existed only for a longer camera decay); both shake with `tremor_shake.tres` (0.32 s decay). Review the feel in playtest.
- Warden tuning that changed with the migration: one repeat penalty (0.35) for all phases instead of 0.25 in phase 3; the Tremor takes no attack slot, like the boss's swings; close pressure counts from the start of the fight, so a player already hugging the Warden can draw the stomp as soon as phase 2 begins. Review in playtest.
- A combat profile's action list is read when the monster spawns. Respawn to apply edits; live edits to an already spawned monster's list are not picked up.
