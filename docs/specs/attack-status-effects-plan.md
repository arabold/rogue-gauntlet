# Attack status effects

Status: proposed; no on-hit status payload is implemented by this document. The [monster actions and boss phases plan](monster-actions-and-boss-phases-plan.md) includes poison/venom as an early delivery slice. This document owns its payload, periodic-buff, refresh and save contracts. [Combat](../combat-system.md), [weapon attacks](../weapon-attack-system.md), [status conditions](../stats-and-itemization.md#buffs-and-status-conditions), [screen effects](../screen-effects.md) and [world effects](../world-effects.md) own the existing systems.

## Current foundation

- Players and monsters already have actor-local `BuffController` instances and source-owned timed conditions. Reuse their existing wiring and `IStatusActor`; do not add a second status registry or recreate those nodes.
- `ActiveBuff` still stores a `Player`, `PeriodicBuff.OnTick` takes a player, and `StatModifierBuff` accesses player stats. `PoisonBuff` deals periodic damage directly to player health; `HealingBuff` heals it. These callbacks need an actor-neutral target for shared poison.
- `AttackDefinition`, `PreparedAttack`, `DamageContext` and projectile direct contacts do not carry on-hit payloads yet. Player weapon defaults are constructed at runtime, so weapon-wide payloads need a `Weapon` authoring surface too. The [shared spell plan](shared-spell-activation-plan.md) routes spell/item poison through these same `PoisonBuff` activations; a contact payload and a poison potion are different application mechanisms for one periodic effect.
- The hurtbox already resolves active defense, armor, shields, health and reactions. Confirmed contact has no passive accuracy/evasion roll. Add payload application to that existing resolution boundary.
- Player activations **already save** through `BuffSaveCodec`, including poison/healing amount, remaining duration and tick progress. Node-owned sources must currently be restorable with the player. An enemy Node cannot serve as the source of a saved poison activation.
- The existing `poisoned.tres` condition drives the player's poison overlay. Shared surface ownership already exists; later monster tinting must submit appearance intent through `SurfaceAppearanceController`, rather than write materials independently.

## Gameplay contract

Venom and poison use the same mechanic and `poisoned` condition. A bite, poisoned bolt or player weapon selects a poison payload through authored data; species and weapon names do not implicitly grant it.

- Apply a payload only when a **direct** contact resolves to `DamageOutcome.Damaged` and its target remains alive. Dodged, parried, blocked (including chip), guard-broken, absorbed, invulnerable and filtered contacts apply none. A killing contact applies none.
- First delivery covers direct melee and projectile hits. Splash, reflection, poison ticks and legacy untyped damage carry no payload, preventing repeated poison chains.
- Author application chance on `OnHitStatusProfile`; duration, tick rate and damage belong to an external `PoisonBuff`. Different strengths reference different definitions. A chance must be finite and in `(0, 1]`; attack poison must be timed, with positive finite tick rate and damage.
- Each qualifying contact rolls each payload once in authored list order, using `contact.Random` (the attack/projectile stream). Chance 1 draws nothing. Non-qualifying contacts and missing receivers draw nothing. Do not use generation, loot or AI-choice RNG. A rejected application after a chance roll does not undo that draw.
- Reapplying the **same venom definition** refreshes one activation's remaining duration, without stacking another instance or resetting its tick progress. Identical venom from several rats therefore cannot multiply its damage per second or postpone every tick. Distinct venom definitions are separate authored effects; the initial roster uses one attack venom.
- Poison ticks bypass armor and active defense and cause no hit reaction, poise damage, push or attack payload. Preserve the existing direct-health periodic path. Death and a successful cure prevent later ticks; attacker death does not remove an applied finite poison.
- Healing spells restore health without curing poison by default. Existing potion cures continue to remove the conditions in their authored `CuredConditions`.

Starting content: the Plague Rat bite has a 35% chance to apply `rat_venom.tres`: one damage per second for four seconds, refreshing on reapplication. Verify the projectile path with a harness variant; keep ordinary crossbow bolts plain. A player weapon using the same payload must work against a monster in tests. Assassin's Dagger venom remains optional content after that shared path passes acceptance.

## Shared actor targets and runtime ownership

Add `IBuffTarget` with `HealthComponent Health` and optional `IStatModifierTarget Modifiers`. `BuffController` implements it using its existing exported health reference; the player injects its runtime `PlayerStats` modifier target during `_EnterTree`, while monsters can wire an `ActorStatsComponent`. The [shared spell plan](shared-spell-activation-plan.md) owns that common modifier interface/ledger and the existing source adapters; monsters need no player progression state.

Replace `ActiveBuff.Player` with `Target : IBuffTarget` and change `PeriodicBuff.OnTick` to take that target. `PoisonBuff`/`HealingBuff` use `Target.Health`. Stat modifiers use `Target.Modifiers` and reject a target without the required supported stats through `Buff.CanApplyTo` before cures or activation side effects. Migrate all current buff callbacks, consumable/equipment callers and tests directly; retain no compatibility overload or player-only fallback.

`BuffController.ApplyBuff` returns `Applied`, `Refreshed` or `Rejected`. Reject null/invalid buffs, dead targets and unsupported target capabilities before mutation. Preserve `ApplyStatus`, `SetPersistentStatus`, source removal and their independent condition lifetimes. One controller operation emits one `BuffsChanged` after the final state is visible.

Add `Buff.Stacking` (`Stack` by default, `Refresh` for attack venom). Existing food, potion and equipment overlap keeps its authored behavior. Refresh matching for attack payloads uses a canonical external buff path passed as a stable `stackKey`; ordinary stackable activations use an empty key. Keys identify the effect, never the attacking monster or an attack execution.

Capture chance, key and a shallow runtime copy of the buff at attack acceptance in an immutable `PreparedHitStatus`. Preserve the authored condition reference and snapshot its numeric settings. Generalize the existing `ActiveBuff.RefreshStatus` into `Refresh(duration)` and migrate its current condition call sites; do not add a parallel refresh path. Keep timed/permanent duration mode on the activation so refresh never mutates a shared `Buff.Duration`. Preserve tick progress without repeating `OnApply` or cures, including the existing timed-to-persistent condition transition.

Keep the existing deferred death clear. The periodic loop must stop if its target dies or its activation is removed during a callback, even when a large delta covers more ticks. Expiry, cure and death reverse each activation at most once. This retains the current per-activation ownership of reversible stat modifiers.

## Payload and resolution boundaries

Proposed types/properties; configurable values are `[Export]` properties and arrays default to `[]`:

```text
OnHitStatusProfile : Resource
  Buff : Buff                  // required external timed definition
  Chance : float               // finite, 0 < chance <= 1
Buff.Stacking : BuffStacking

AttackDefinition.OnHitStatuses : OnHitStatusProfile[]
Weapon.OnHitStatuses : OnHitStatusProfile[]
PreparedHitStatus(Buff snapshot, float chance, string stackKey)
PreparedAttack.OnHitStatuses : IReadOnlyList<PreparedHitStatus>
DamageContext.OnHitStatuses : IReadOnlyList<PreparedHitStatus>
HurtBoxComponent.Buffs : BuffController // explicit optional local receiver

IBuffTarget.Health : HealthComponent
IBuffTarget.Modifiers : IStatModifierTarget // absent without a modifier capability
ActiveBuff.Target : IBuffTarget
ActiveBuff.StackKey : string            // runtime-only identity observation
ActiveBuff.Refresh(float duration) -> void
Buff.CanApplyTo(IBuffTarget target) -> bool
BuffController.ApplyBuff(Buff buff, string stackKey = "") -> BuffApplication
PeriodicBuff.OnTick(IBuffTarget target) -> void

ActiveBuffSaveData.StackKey : string
ActiveBuffSaveData.Stacking : BuffStacking
```

`PlayerAttackController.TryPrepareWeaponAttack` merges definition payloads first, then weapon payloads. Monster preparation uses its selected `EnemyAttackProfile` definition. Existing spells do not inherit weapon contact payloads automatically. Validate and capture before execution starts; invalid entries reject the attack rather than silently skip venom.

Projectile initialization receives the prepared payload list. Only its direct hit forwards it; splash contexts omit it. Clear the list on pool reset/despawn so another shooter cannot inherit it. The hurtbox owns application after damage/health/reactions are resolved and before publishing the final `Damaged` notification; no attacker-side receiver lookup is needed.

```text
AttackController preparation
  validate payloads -> capture chance, canonical key and runtime buff settings
  start accepted execution -> carry immutable prepared list

Direct melee contact / projectile direct hit
  DamageContext with payloads + explicit attack/projectile RNG
  HurtBoxComponent resolves current defense/armor/shield rules
    no surviving Damaged outcome -> no payload and no chance draw
    surviving Damaged -> roll -> local Buffs.ApplyBuff(snapshot, stackKey)
      Applied -> one activation; Refreshed -> existing duration only
      Rejected -> no target mutation
    publish damage/status feedback after state is settled

ActiveBuff periodic tick
  PoisonBuff.OnTick(target) -> target.Health.TakeDamage
    no contact resolution, armor, defense, poise or status application
    target death -> existing deferred clear; stop further due ticks immediately

Save/load or stairs
  BuffSaveCodec captures/restores remaining duration + tick progress + stack key/mode
    validate all records before replacing runtime activations
    restore without replaying ticks, cures or OnApply effects twice
    later application with the same canonical key refreshes the restored activation
```

Trace `status.resolved` with attack identity, canonical buff key, chance outcome and application result through the existing optional combat trace. Observability must not change draws or execution.

## Persistence

Extend `ActiveBuffSaveData` and `BuffSaveCodec` with `StackKey` and `Stacking`. The codec currently reconstructs buff objects, so reference equality alone would lose refresh identity after load. A nonempty key must resolve to a compatible external buff definition and be paired with Refresh; retain the saved numeric snapshot and clocks rather than restarting it from the authored duration.

Attack-applied poison has no Node `Source`. The stable key, snapshot and target-owned duration are sufficient, including after its attacker dies. Preserve existing player-authored Node sources for other conditions and their validation. Equipment restores its own activations as today. Test save/load and stairs with a poisoned player, then apply the same venom again and verify there is still exactly one activation with preserved tick phase.

Bump `SaveGame.CurrentVersion` when this schema change lands; do not add old-format adapters or fallback values for older saves. Reject incompatible or invalid saves without overwriting them. Surviving monsters and their active effects still reset on floor reconstruction; cleared deaths, loot and XP follow the existing rules. The documentation edit itself changes no save version.

## Affected files and authoring

```text
+ scenes/attacks/OnHitStatusProfile.cs, PreparedHitStatus.cs
~ scenes/attacks/{AttackDefinition,PreparedAttack,AttackController,Projectile}.cs
~ common/combat/DamageContext.cs
~ scenes/components/HurtBoxComponent.cs
+ common/interfaces/IBuffTarget.cs, IStatModifierTarget.cs
~ scenes/player/resources/PlayerStats.cs  implement modifier interface via existing methods
~ scenes/buffs/{Buff,PeriodicBuff,PoisonBuff,HealingBuff,StatModifierBuff}.cs
~ scenes/player/{ActiveBuff,BuffController,Player,PlayerAttackController}.cs
~ scenes/items/Weapon.cs
~ scripts/saves/{SaveGame,BuffSaveCodec}.cs
~ scenes/player/player.tscn  local receiver and runtime stats wiring
~ scenes/enemies/MonsterSceneValidator.cs  every action/phase payload and receiver
~ scenes/enemies/enemy_base.tscn, skeleton/skeleton_crossbow.tscn
~ scenes/enemies/templates/{melee,projectile,mobile}_enemy.tscn
+ scenes/enemies/rat/rat_venom.tres
~ scenes/enemies/rat/rat_attack.tres
+ tests/OnHitStatusTest.cs
~ tests/{ActiveBuff,StatusCondition,CombatIntegration,MonsterAuthoring,PlayerSaveCodec}Test.cs
~ docs/{monster-authoring-design,weapon-attack-system,combat-system,stats-and-itemization}.md
```

Existing status controller nodes stay in place. Wire `HurtBoxComponent.Buffs` to the same actor's controller and health, validate local ownership and every payload on every phase, and verify Inspector-created Resources after save/reopen. Adapt remaining buff subclasses/test overrides to the actor-neutral target; unsupported stat targets must report rejection.

## Delivery and verification

1. **Shared poison contacts and persistence.** Land the periodic target change, payload capture/direct-hit plumbing, refresh identity, save schema and rat venom together. Exercise poison on the player and a test monster using identical authored payloads. No healing-caster or rush dependency. Current monster attack authoring may land before the multi-action refactor if necessary, but the integration must validate the complete selected-action repertoire when that refactor is present.
2. **Optional weapon content and monster feedback.** After the shared path passes, author the Assassin's Dagger venom if desired. A monster poison visual uses the existing world/surface presentation owner and its visibility/death rules; do not introduce a separate material writer. Specific visual tuning is not a dependency for poisoning the player.

Verification covers: melee/projectile direct hits; zero status on each defended/filtered/absorbed/killing outcome and splash; exact chance draw behavior; pooled reuse; repeated bites retaining one activation/tick phase; independent actors; cure/expiry/death idempotence; source death; large-delta death stopping further ticks; player stat/potion regressions; unsupported stat buffs on monsters; and poison save/load/stairs followed by reapplication. Player overlay uses the existing condition.

Use `dotnet build "Rogue Gauntlet.sln"`, targeted GdUnit4 tests with `GODOT_BIN` and an isolated short `TMPDIR`, and `dotnet build "Rogue Gauntlet.csproj" -c ExportRelease`. Use `TestFactories` for runtime resources/scene attachment. Import new scripts/resources, validate every monster/template, smoke `scenes/main/main.tscn` and review a rat bite in the windowed combat harness.

Poison resistance/immunity, coatings, rolled venom affixes, poison clouds and extra status families remain separate extensions. Tune application chance, tick damage and duration against early-floor supplies and crowd pressure; adding poison to the entire roster is not implied.
