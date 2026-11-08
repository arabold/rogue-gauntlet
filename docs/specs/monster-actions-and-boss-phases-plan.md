# Monster actions and boss phases

Status: proposed. [Monster authoring](../monster-authoring-design.md), [combat](../combat-system.md) and [weapon attacks](../weapon-attack-system.md) describe the implemented foundation. The [shared spell plan](shared-spell-activation-plan.md) owns common effects/casting; the [status plan](attack-status-effects-plan.md) owns poison and periodic-buff persistence. The [roadmap](../development-roadmap.md) owns delivery.

## Goal and boundaries

Compose melee, projectiles, spells, self/ally healing, empowerment, poison, rush movement and distance keeping. Ordinary monsters can choose several actions. Bosses support **one to three phases**, with at most two health-triggered transitions. The agreed Warden has thresholds at 65% and 30%.

Player input and AI are request sources for the same capabilities. Keep existing attack, movement, spell, buff, defense and item-use owners. Spell availability is authored on items/pools/action lists; player-only or monster-only content does not need an alternate effect implementation. Venom and poison are the same periodic-damage mechanic.

First delivery includes a distinct overhead, stationary stomp, rush followed by an ordinary attack opportunity, offensive magic, healing, Rage and venom. Damaging charge contact, travelling jumps/shockwaves, summons, weapon switching and cinematic transitions remain subsequent mechanics.

## Reuse and ownership

| Responsibility | Owner retained or extended |
| --- | --- |
| Awareness, navigation, distance keeping and bounded retreat | Existing FSM, `EnemyContext`, movement/tactical strategies and `EnemyTacticalSelector` |
| Choose one of several actions | New plain `EnemyActionSelector`; uses existing `Cooldown`, cached context facts and `Weighted.Pick` |
| Health phases and effective combat configuration | Existing `EnemyBehaviorComponent`, health notifications and shared idle decision entry |
| Attack timing, leases, contact, projectiles and spell release | Existing `AttackController`, `PreparedAttack`, `SpellRelease`, `AttackExecution` and action gate |
| Rush displacement and phases | Existing `MovementComponent`, using an internal `AttackExecution` clock |
| Shared spell preparation, paid starts and runtime effect ownership | Existing `SpellCaster`; no additional casting coordinator |
| Healing, poison, Rage, cures and expiry | Existing buff runtime extended to actor-neutral targets; same authored effects as items |
| World and camera feedback | Existing `WorldEffectHost`, surface owner and `CombatCameraFeedbackController` |

The additional action/phase Resources express real authored variation. They do not introduce another behavior tree, navigation service, gameplay registry or action gate. A small shared modifier adapter is justified because monster hits currently lack the modifier capability already used by player buffs; it reuses the player's arithmetic and existing base values.

## 1. Action choices

`EnemyBehaviorProfile.AttackStrategy` currently supplies one attack. Its range/slot assumptions also occur in `EnemyContext`, movement strategies, ripostes and validation. Migrate these consumers together.

Add `EnemyCombatProfile` for the switchable combat settings: movement, tactics, defense, optional low-health retreat, weighted action choices and a typed `ApproachAction`. `EnemyBehaviorProfile` keeps initial perception/lifecycle settings and exports the base `Combat` plus optional `Phases`. This separates phase changes from spawning, awareness and detection without copying an entire actor configuration.

Use three selectable Resource strategies under `EnemyActionProfile`:

| Action Resource | Payload | Existing executor |
| --- | --- | --- |
| `EnemyAttackProfile` | `AttackDefinition`, style/duration, damage range and crit | `AttackController` |
| `EnemySpellProfile` | Existing `SpellDefinition`, level/hit power, targeting and explicit mana policy | `SpellCaster` plus `AttackController` |
| `EnemyRushProfile` | `RushProfile` | `MovementComponent` |

Common eligibility fields cover priority, cooldown, hostile-target requirement, distance/facing, optional close-pressure duration, slot category (`None`, `Melee`, `Ranged`) and riposte eligibility. Keep type-specific fields on their corresponding Resource. Validate static configuration/animation mappings once with the existing monster validator; prepare/clone execution data only for the selected action, not every candidate. Missing capabilities reject the action; nothing infers an attack from a species or weapon name.

`EnemyActionChoiceProfile` contains only `Action` and `Weight`. Combat profiles reference the **same canonical action Resource** with different weights, so phases can favor a slam without copying its damage/cooldown or inventing matching string IDs. `ApproachAction : EnemyActionProfile` must reference an available offensive choice in that profile. These are typed Inspector dependencies. Each array defaults to `[]`; each GlobalClass has its own file.

`EnemyActionSelector` keeps per-actor cooldowns keyed by canonical action Resource, last accepted action, bounded decision retry and pressure state. Use the existing `Cooldown` helper rather than another timer implementation. Preserve this runtime across phases. Authored Resources remain immutable; capture execution settings at acceptance.

At a free decision opportunity:

1. Build eligible choices from the effective combat profile using the context's existing cached sight, distance, reachability and navigation facts. Support adds useful-recipient checks. Self-healing does not require an ally or hostile; sleeping/spawning, disabled, pushed or committed actors cannot start it.
2. Keep the highest eligible priority tier, apply the authored repeat-weight multiplier, then use existing `Weighted.Pick` once on a nonempty eligible list in authored order. Use a reused candidate buffer and computed weights; do not rerun predicates inside the weight callback. A single eligible action remains usable.
3. Attempt the selected action immediately. On failure, wait for the bounded retry opportunity instead of rerolling/traversing the whole list each frame. Start its cooldown/history only on acceptance; interruption does not refund it.
4. Reserve only the selected offensive action's target slot immediately before execution. Rejection returns a provisional reservation; matching completion/interruption returns a held one once. Support and the first movement-only rush take no offensive slot.

Keep approach/ring movement anchored to `ApproachAction`, while other in-range actions remain selectable. Preserve maintain-distance hysteresis, retreat deadlines/retry, blocked-escape fallback, doorway commitment and large-actor room/clearance rules. Ripostes use only eligible attack choices after guard recovery, with range/sight/slots checked again. Rat retreat still follows normal completion of its accepted bite, not an interrupted/rejected action.

Replace the old attack-policy types and migrate all actor/template/harness/test Resources directly; keep their existing `AttackDefinition`, contact and projectile assets. Do not retain legacy aliases.

## 2. Boss phases in the behavior owner

`EnemyBehaviorProfile.Phases` contains zero to two `BossPhaseProfile` entries, each with `EnterHealthFraction` and a complete `EnemyCombatProfile`. The base combat profile is phase zero. Validation requires strictly descending thresholds in `(0, 1)` and rejects a fourth phase.

`EnemyBehaviorComponent` already observes health and owns the AI decision boundary. It latches the deepest crossed phase on `HealthChanged`; healing never moves backward. A hit crossing both thresholds chooses the deepest phase directly. Health at or below zero cancels pending work; death wins even though the health signal precedes `Died`.

Apply a pending phase through one `PrepareDecision` entry shared by ordinary behavior and `EnemyDefenseController` before they read effective policies. It may apply only when the actor is alive, outside lifecycle/hit actions, its gate is idle and no forced/committed movement remains. Ordinary walking does not delay a phase. Preserve the entire committed action, stagger and recovery. Gate release can mark eligibility, but never switch profiles synchronously inside lease replacement or a damage callback. Use existing physics priorities and decision ticks; add no phase Node or new global physics stage. This preparation method does not grant/deny actions: defense keeps its existing ability to replace an eligible attack startup even when no phase can apply.

Changing the effective profile preserves target, awareness, health, poise, action cooldowns, RNG and defense charges/cooldowns. Recreate only tactical runtime whose configuration changes, while idle. Emit a local `EnemyBehaviorComponent.PhaseChanged(previous, current)` after the profile changes. No transition animation, immunity or entry-action machinery is required.

### Warden reference encounter

| Phase | Health | Repertoire |
| --- | --- | --- |
| I | Above 65% | Existing Normal wide slash, occasional slower Heavy overhead |
| II | 65% or below | Keep both swings; enable stomp under sustained close pressure |
| III | 30% or below | Favor overhead/stomp more, retaining cooldowns and full recovery |

- **Slash:** retain block/counter rhythm.
- **Overhead:** narrower box/capsule contact, greater damage/poise, longer wind-up, `BreaksGuard` plus Dodgeable/Guardable/Parryable. Use the shared Heavy/orange warning and minimum wind-up; a distinct clip/contact binding is required. Author cooldown to avoid a slam loop.
- **Stomp:** existing radial melee contact with `ForcePushback` and knockback. Start Dodgeable only (Unblockable/red), with the existing minimum telegraph and substantial recovery/cooldown. Close pressure is visible hostile proximity sustained for an authored duration. It becomes an option on the next free decision, never cancels an existing punish window. Hyper-armor/push resistance still apply.
- Warning and damage derive from the same contact footprint. The initial stomp is a stationary contact window; dust/cracks/ground pulse add no second damage. A later visual hop does not change gameplay height or grant immunity. Do not give this Warden healing by default.

An optional `GroundImpactProfile` on `AttackDefinition` groups an existing `WorldEffectDefinition` with camera strength/radius. Capture it with the existing prepared attack and trigger once when contact opens, including on a miss. Use `WorldEffectHost.PlayBurstAt`; route its decoupled camera notification into the existing camera controller's impulse arbitration. Add no separate shake/VFX clock or fake damage contact. Disabling presentation leaves gameplay unchanged. Sample actual clips before accepting overhead/stomp authoring.

## 3. Rush in movement

Add `MovementComponent.TryRush(RushProfile, direction)`. The profile authors wind-up, distance, travel duration, recovery and animation. The existing mover owns its snapshot, action lease, override and cleanup. `ActorActionKind.Rush` follows attack-style startup cancellation, committed travel and the actor's recovery-cancellation rules. Required animation wiring must be explicit; missing binding makes rush unavailable.

Reuse `AttackExecution` internally with the active interval representing travel and no hit-stop requests. Advance it once in the existing movement step, use its previous/current interval for partial-frame displacement, and call the existing body mover once. No second rush clock implementation or extra physics priority is needed.

Capture horizontal heading at travel commitment. Collision stops travel without extending it; latch that stop for the execution. Ordinary locomotion `Stop()` cannot overwrite rush. Death, status locks, stagger/interrupting knockback, explicit cancellation, reset and tree exit clear only this execution's pose/facing/motion/lease. Preserve the existing forced-push exception while status-locked. Stale cleanup cannot clear a newer dodge/action. Local rush signals carry execution identity for AI/presentation.

AI navigation checks supported corridor/destination, room confinement and clearance before requesting movement; physics remains collision-authoritative. Rush grants no dodge immunity, actor pass-through or contact damage. After recovery, an ordinary attack opportunity revalidates range/sight/slots. `DodgeComponent` keeps defensive immunity, charges, steering and pass-through, with physical displacement still in movement. Damage remains in the attack executor.

## 4. Shared spells, healing and empowerment

The [shared spell plan](shared-spell-activation-plan.md) defines this boundary once. Reuse `SpellDefinition`, `MagicStaff.BoundSpell`, existing pools and spell paths. Extend `SpellCaster`; keep combat timing in `AttackController` and item commitment in `PlayerInventoryController`. Extend existing `SpellRelease` with one actor-neutral context instead of another request/preparation/result framework.

Existing combat scene spells and generalized utility-effect strategies enter the same effect runtime. Wands/staves, scrolls, potions and innate actions supply power, target intent, timing and payment through their existing source data. There is no monster-specific heal or Rage implementation, duplicate mana charge or nested item/attack lease.

Healing includes immediate heal and the existing `HealingBuff` regeneration. Start the mage with the same regeneration definition as the current healing potion, then exercise instant healing too. Preserve potion amounts/duration/cures. Suggested instant-heal tuning: self below 35% or ally below 60%, 25% max health, roughly one-second preparation, ten-second cooldown and paid mana. Source decision thresholds do not redefine the shared effect. Release revalidates living/range/sight; self needs no sight to itself. Never resurrect, exceed max health or reverse a latched phase.

Empowerment uses the same external Rage `StatModifierBuff` as the scroll: current +25% min/max damage for 30 seconds and its authored stacking. Shared modifier interfaces/ledger let player stats and a small monster adapter apply/remove exactly the same bonus. Reuse existing monster definition/action/component base values; do not add another stat sheet. Reject a whole buff if required stats are unsupported. Resolve each hit once; no extra multiplier in `AttackController`.

Support usefulness is advisory for AI. Use existing throttles and one bounded actor query per decision; stop once readiness is established and share facts with preparation. Select a best injured ally in one pass, with distance/path tie-breaks; do not count/sort all allies for every candidate. Requery on release for moving targets. A player can still drink an unidentified/full-health potion. Inventory-targeted spells keep explicit slot selection rather than entering spatial target queries.

## 5. Poison/venom

The [status plan](attack-status-effects-plan.md) owns implementation details. `OnHitStatusProfile` adds optional chance-bearing payloads to direct melee/projectile contacts. Apply only after surviving `Damaged` contact; defended/absorbed/filtered/killing hits and splash/ticks apply none. Use the existing attack/projectile RNG.

Shared `PoisonBuff` defines duration, tick rate and damage. Same-venom applications refresh one activation without resetting tick progress; several rats cannot multiply its tick damage. Ticks use target health directly, bypass armor/defense and cause no hit reaction or recursive payload. Cure/death stops later ticks; attacker death does not end applied poison.

Start with the rat's proposed 35% venom bite, one damage/second for four seconds. Verify projectile capability with a harness variant while ordinary crossbow bolts stay plain. The same payload must work from a player attack against a monster. Preserve player duration, tick phase and refresh identity through saves/stairs without enemy Node references; surviving monster effects retain the current reconstruction reset policy.

## Contracts and orchestration

Proposed additions use exported properties for tunable data and explicit dependencies:

```text
EnemyBehaviorProfile.Combat : EnemyCombatProfile
EnemyBehaviorProfile.Phases : Array<BossPhaseProfile> // at most two transitions
EnemyCombatProfile.Actions : Array<EnemyActionChoiceProfile>
EnemyCombatProfile.ApproachAction : EnemyActionProfile
EnemyActionChoiceProfile.Action : EnemyActionProfile
EnemyActionChoiceProfile.Weight : float
BossPhaseProfile.EnterHealthFraction : float
BossPhaseProfile.Combat : EnemyCombatProfile

EnemyBehaviorComponent.PrepareDecision() -> void // apply phase only if safely idle
EnemyBehaviorComponent.TryAction(EnemyActionProfile action) -> ActorActionRequestResult
EnemyBehaviorComponent.PhaseChanged(int previous, int current) // Godot signal
MovementComponent.TryRush(RushProfile profile, Vector3 direction) -> ActorActionRequestResult
```

Reuse existing `ActorActionRequestResult` and bool contracts. A rejected start mutates no cost/cooldown/history; release provisional ownership. Attack identity comes from `AttackController`; rush has a sequence scoped to `MovementComponent`. Match completion before releasing any held slot or newer action state.

```text
HealthChanged -> behavior latches deepest phase (no immediate profile change)
Existing behavior/defense decision entry
  PrepareDecision -> alive/free/lifecycle checks -> apply pending combat profile
  existing context/tactics -> cached facts + bounded useful-target query
  selector -> highest priority + Weighted.Pick -> selected action
  reserve only selected offensive slot
    attack -> existing attack preparation/start
    spell  -> existing SpellCaster preparation/start -> attack clock
    rush   -> movement's AttackExecution-backed mode
  rejected -> release provisional slot; accepted -> start one Cooldown
Existing executor completion -> matching cleanup/slot release -> tactical feedback
```

## Affected paths

Each GlobalClass gets its own matching file and import-generated UID. Related spell/status changes are listed only in their owning plans.

```text
+ scenes/enemies/EnemyCombatProfile.cs, BossPhaseProfile.cs
+ scenes/enemies/actions/Enemy{Action,Attack,Spell,Rush,ActionChoice}Profile.cs
+ scenes/enemies/ai/EnemyActionSelector.cs
~ scenes/enemies/{EnemyBehaviorProfile,EnemyBehaviorComponent,EnemyBase,EnemyDefenseController,MonsterSceneValidator}.cs
~ scenes/enemies/ai/EnemyContext.cs and existing movement strategy consumers
- scenes/enemies/strategies/{Enemy,Melee,Projectile}AttackStrategy.cs
+ scenes/components/RushProfile.cs
~ scenes/components/MovementComponent.cs, common/actions/ActorActionLease.cs
+ scenes/attacks/GroundImpactProfile.cs
~ scenes/attacks/{AttackDefinition,PreparedAttack,AttackController}.cs
~ scripts/SignalBus.cs, scenes/effects/combat/CombatCameraFeedbackController.cs
~ scenes/enemies/*/ action/combat Resources and actor scenes; templates and harness fixtures
+ scenes/enemies/forsaken_warden/ overhead, stomp, two transition and combat-profile Resources
~ scenes/attacks/animation_profiles/forsaken_warden.tres
+ tests/EnemyActionSelectionTest.cs, BossPhaseTest.cs
~ tests/{MovementPhysics,ActorActionGate,CombatDefense,MonsterAuthoring,MonsterRoster,Warden,EnemyDefense}Test.cs
~ docs/ current-system/authoring/harness documents as each slice lands
```

## Delivery and acceptance

1. **Multiple actions and Warden overhead.** Land the profile/selector changes with every single-policy consumer, roster/template/harness/test migration and a valid distinct overhead clip. One-action monsters retain their behavior. Verify per-action cooldown/slots, riposte filtering, accepted-bite retreat, seeded choice and independent actors.
2. **Stomp and three phases.** Extend the existing behavior owner, author the Warden phases/stomp and release feedback. Verify threshold jumps, healing, death-before-application, recovery/defense ordering, cooldown continuity and fourth-phase rejection. Review telegraph/contact alignment, missed-slam presentation, knockback and punish windows in the harness.
3. **Venom contacts and saves.** Deliver the status plan's actor-neutral periodic targets, payloads, refresh identity and player save support together. Exercise player/monster recipients, projectile reuse, defended contacts, cure/expiry/death and a save followed by another bite after load. Independent of casting/rush.
4. **Rush and ranged pressure.** Extend movement using the existing phase clock. Test bounded travel, blocked/partial-frame motion, one body move, rush/dodge ownership and stale cleanup. Review doors, corners, boss-room confinement and impassable water alongside existing distance-keeping shooters.
5. **Shared casting and healing.** Extend existing caster/release contracts, prove offensive/regeneration/instant-heal effects from player focus, innate monster and one-use item. Preserve pools, spell paths, mana, ward and item-use behavior. Verify costs, cancellation, target validity, full-health/unknown potions and transient-effect lifetime.
6. **Shared buffs and utility effects.** Reuse the modifier ledger for Rage on both actors; adapt utility scroll strategies through the shared host without duplicating algorithms. Migrate all intended scroll/potion activation routes in this independently verifiable slice. Test equal effect results at equal power/targets/RNG, then their different source costs/timing. Review mixed encounters before assigning new variants to pools/threat/rewards.

C# implementation slices run the solution build, targeted GdUnit4 suites with `GODOT_BIN` and an isolated short `TMPDIR`, and ExportRelease build. Use `TestFactories`, Godot import/resource validation and a headless `scenes/main/main.tscn` smoke. Navigation, clip readability, ground feedback and feel require windowed harness/editor checks; headless runs cannot certify them. Designer acceptance includes typed action/phase assignment, optional capability removal, save/reopen and two independent instances.

## RNG and persistence

Derive `combat.choice` from level seed and persistent spawn identity via `EnemyBase.ConfigureCombatRandom`; keep it separate from attack/defense, generation and loot. Authored choice order is deterministic within the same build/content/inputs, without promising physics replay.

Keep surviving-enemy reconstruction semantics: live health, position, phase and AI cooldowns reset; cleared enemies/boss defeat stay cleared. Preserve existing player mana, cooldown, buff and ward restoration. Keep existing spell-pool/save identity; source adapters do not require a wrapper/save-identity migration. Poison refresh identity requires a real save-schema version bump when implemented. Any other incompatible schema/content change also bumps `SaveGame.CurrentVersion`; reject old/invalid saves without conversion or overwriting them.

This review changes documentation only. Damage, weights, healing thresholds/mana and crowd poison pacing remain playtest tuning; the three-phase cap and shared execution model are settled.
