# Attack Status Effects — Program Design

Status: **proposed**. Covers statuses applied by attacks: monster attacks that poison the player (venomous bites, poisoned bolts) and player weapons that poison monsters. It builds on [combat actions and defense](../combat-system.md), [weapon attacks](../weapon-attack-system.md), [stats and itemization](../stats-and-itemization.md) and [monster design and authoring](../monster-authoring-design.md).

**New foundation:** [actor status integration](actor-status-integration-plan.md) adds actor-local, source-owned timed conditions and a frozen status on players and monsters. The proposed buff generalization below must build on that controller and its existing scene wiring; periodic and stat buffs remain player-specific, and attack payloads are not yet implemented.

**Foundation:** this plan builds on the implemented [status conditions](../stats-and-itemization.md#buffs-and-status-conditions) and [screen effects](../screen-effects.md) and uses their contracts unchanged:

- `StatusCondition`, whose identity is the external `.tres` (`scenes/buffs/conditions/poisoned.tres`).
- `Buff.Condition` and `Buff.CuredConditions`.
- On `BuffController`: `HasCondition`, `ClearBuffs`, and one `BuffsChanged` notification per application, removal, expiry sweep or clear, after which observers re-read what they need.

Any buff carrying `poisoned.tres` already turns on the player's poison overlay, so poisoning the player needs no presentation work.

**World-VFX integration dependency:** [shared actor surface ownership](../world-effects.md#surface-ownership-and-visibility) now exists. Before slice 3, revise the tint-rendering contract below to submit tint intent through `SurfaceAppearanceController`; its direct `MaterialOverlay` writer and independent material capture are superseded. Preserve cloak/skin priority, helper exclusion and terminal death handoff. The current controller supports cloak/skin profiles; adding tint composition belongs to that slice, not this VFX delivery. Status application and buff generalization remain separate from this rendering adjustment.

## Target behavior

| Source | Target | First content | Presentation |
| --- | --- | --- | --- |
| Monster melee attack | Player | Plague Rat bite: 35% chance of venom | Existing poison screen overlay |
| Monster projectile | Player | Supported path; no roster change in this plan | Existing poison screen overlay |
| Player weapon, any attack made with it | Monster | Assassin's Dagger: 30% chance of venom | Green tint on the monster (slice 3) |

- A status can only apply when the contact resolves to `DamageOutcome.Damaged` and the target is still alive afterwards. Dodged, parried, blocked (even with chip damage), evaded, absorbed, invulnerable and filtered contacts apply nothing.
- Each status rolls its chance once per qualifying contact. Splash damage from area impacts applies no status.
- When the target already has a venom applied by an attack, a new application restarts its timer instead of adding a second stack.
- Poison ticks on a monster bypass armor and active defenses and do not stagger it, which matches the player today. A monster killed by poison drops loot and grants XP like any other death.
- Death clears an actor's buffs, so a corpse stops ticking and loses its tint before it fades out.

## Current seams

- `DamageContext` (`common/combat/DamageContext.cs`) carries execution identity, source, owner, faction, amount, accuracy, an explicit `Random`, direction, defense traits and delivery kind. It has no status field.
- **Monster attacks.** `EnemyAttackStrategy.Attack` holds an embedded `AttackDefinition`. `EnemyBehaviorComponent.TryAttack` calls `AttackController.TryPrepareAttack`, which stores a shallow `Duplicate()` in a `PreparedAttack`, and then `StartAttack`. `ApplyMeleeContact` rolls damage and crit on the controller's `_random`, then builds the `DamageContext`. `SpawnProjectile` and `SpawnAreaDrop` pass values into `Projectile.Initialize`, which seeds its own RNG from `_random.Randi()`. That RNG builds the direct context and a separate `AreaImpact` context for each splash target.
- **Player weapons.** Only staffs and wands author `Weapon.CustomAttackDefinition`. All other weapons get a definition built per attack by `PlayerAttackController.CreateDefaultDefinition`, so a weapon-wide property cannot live on `AttackDefinition` alone. `Weapon.TryPrepare`, `MartialTechniqueAction` and `UnarmedAttackAction` all go through `TryPrepareWeaponAttack`. `tests/QuickItemTest.cs` constructs `new PreparedAttack(null, null, 0.2)`.
- **Resolution.** `HurtBoxComponent.ResolveDamageInternal` runs in this order: faction filter, death, active defense (`IDefenseEvaluator`), debug invulnerability, evasion against accuracy, armor, `DamageTaken`, `HealthComponent.TakeDamage`, hit stun/push/effect, `Resolved`. Every roll uses `contact.Random` and falls back to the global RNG only when no RNG is supplied.
- **Combat RNG.** `EnemyBase.ConfigureCombatRandom` derives `combat.attack` and `combat.defense` from the level seed and the spawn's persistent ID. `Player._Ready` derives `combat.attack` from the run seed. The streams are local to each actor and outside generation isolation (see [randomness and seeds](../randomness-and-seeds.md#gameplay-time-randomness)).
- **Buffs.** These are player-only.
  - `Buff.OnApply/OnRemove(ActiveBuff)` take the activation, which stores the player (`ActiveBuff.Initialize(Player, Buff)`); stat modifiers register under the activation. `PeriodicBuff.OnTick(Player)` takes a player, and `BuffController` exports `Player` with a `GetOwner<Player>()` fallback.
  - `PoisonBuff` and `HealingBuff` only use the player's `HealthComponent`, and `StatModifierBuff` uses `Player.Stats`.
  - A plain `Buff` carrying only a condition (blinded, confused) and `TimeScaleBuff` have no callbacks. Player components that wire `Buffs` in the Inspector read them: `ConfusionComponent`, `TimeScaleComponent` and the screen drivers.
  - `ApplyBuff` first ends the conditions in `CuredConditions`, then always adds a new instance, fires `BuffsChanged` and returns nothing.
  - Only `player.tscn` has a `BuffController`.
- **Enemy scenes.** `enemy_base.tscn` is inherited by the rat, minion, warrior and Warden. `skeleton_crossbow.tscn` and the three templates (`melee_enemy`, `projectile_enemy`, `mobile_enemy`) are standalone roots, so each needs its own wiring. `MonsterSceneValidator` reads stored properties, which is safe for editor placeholders, and `EnemyBase._EnterTree` disables an actor that fails validation.
- **Hit reactions.** A monster reacts to hits through the hurtbox path (`IsHit`, push), not through `HealthChanged`. Periodic damage through `HealthComponent.TakeDamage` therefore never staggers it. `DeathComponent` grants XP on `HealthComponent.Died` whatever the killing source. `FadeOutComponent` replaces the corpse's `MaterialOverride`. No monster mesh uses `MaterialOverlay`.
- **Persistence.** Live monster health is not saved; only cleared spawn IDs are. The player's timed buffs are not saved either: loading calls `ClearBuffs`.

## Design decisions

1. **The payload is a buff plus a chance.** A new `OnHitStatus` Resource references a `Buff` and a `Chance`. Duration and strength stay on the `Buff` (`Buff.Duration`, `PoisonBuff.DamagePointsPerTick`), so an attack with a weaker or shorter venom references a different buff `.tres`. Chance belongs to the payload: the same venom can be certain on a bite and occasional on a bolt.
   - Rejected: per-attack `Duration`/`Strength` overrides. Buff subclasses have different strength fields, so a generic "strength" would need per-class scaling rules. Overrides would also mean duplicating the buff on every hit, which breaks the identity that refresh and conditions depend on.
   - Rejected: a `Buff` exported directly on `AttackDefinition` with no wrapper. It has nowhere to put the chance, and it allows only one status per attack.
2. **Two authoring surfaces feed one runtime list.** `AttackDefinition.OnHitStatuses` holds statuses specific to one attack: monster strategies and custom weapon definitions. `Weapon.OnHitStatuses` holds statuses for every attack made with that weapon (primary, secondary technique, ranged), because most weapons have no authored definition. Preparation concatenates them, definition first, into `PreparedAttack.OnHitStatuses`. It reads them from the authored sources rather than from the duplicated copy, so the entries are the authored instances.
3. **Only direct contacts carry statuses.** Melee contacts and a projectile's direct hit, including area-drop projectiles, carry the list. A projectile's splash contexts and the legacy `IDamageable.TakeDamage` path carry none. If a cloud or splash status is needed later, it gets its own authored list rather than reusing this one.
4. **The defender's hurtbox applies statuses after resolution.** `HurtBoxComponent` gets an optional exported `BuffController Buffs`. Unassigned means the actor ignores statuses. The hurtbox already owns the target's health and the contact's RNG and ordering, and it knows the final outcome.
   - Rejected: the attacker applying statuses after `ResolveDamage` returns. It would have to discover the target's `BuffController` by node lookup.
   - Rejected: a separate receiver component fed by a C# event from the hurtbox. That is one more hop, and the receiver would still need the contact RNG and the death ordering.
5. **Only `Damaged` qualifies.** A blocked hit applies no status even when chip damage gets through, so guarding counters venom. A killing blow applies nothing.
6. **The chance roll uses the attacker's stream.** It draws from `contact.Random`: the attacker's `combat.attack` stream for melee, or the projectile's seeded RNG. This matches how crit, evasion and armor are already rolled. Each status in list order makes one draw (`Randf() < Chance`), after the armor roll and only on a qualifying contact. Nothing is drawn when `Chance` is 1, when the outcome does not qualify, or when the hurtbox has no `Buffs`. The roll happens before `ApplyBuff`, so the draw count does not depend on whether the buff is accepted. Adding a payload shifts that attacker's later rolls. That is acceptable, since reproducibility is only promised for the same build and content. No generation stream or `GameSession` loot counter is touched.
   - Rejected: a separate `combat.status` stream for each actor. It would need another seed in the actor and in projectile plumbing, for no current benefit.
   - Rejected: an RNG owned by the target. The outcome would then depend on the victim's history across several attackers.
7. **Stacking is authored on the buff.** `Buff.Stacking` is `Stack`, the default, or `Refresh`. `Stack` keeps today's behavior for potions and food. With `Refresh`, applying a buff instance that is already active resets its remaining duration without a second `OnApply`. It still fires `BuffsChanged`, like any application. Venoms use `Refresh`. Matching is by reference, and attack payloads reference shared external `.tres` buffs, so bites from several rats refresh one venom. Item buffs are per-instance copies from `ItemFactory`, so they never refresh each other.
8. **Buffs target any actor through `IBuffTarget`.** `IBuffTarget` exposes `HealthComponent Health` and `PlayerStats Stats` (null when the actor has no stat block). `BuffController` implements it. Buff callbacks take an `IBuffTarget` instead of a `Player`. `Buff.CanApplyTo` lets `StatModifierBuff` reject actors without stats; `ApplyBuff` then reports `Rejected` and has no side effects. Plain condition buffs and `TimeScaleBuff` are accepted on monsters but do nothing there, because no monster component reads them. `BuffController` replaces its `Player` export with an Inspector-wired `Health`. `Player` injects its runtime `Stats` copy as runtime context.
   - Rejected: giving monsters a `PlayerStats`. Nothing reads monster stats today.
   - Rejected: `OnApply(Node actor)` with lookups inside buffs. That discovers dependencies by name.
9. **Death clears buffs.** `BuffController` subscribes to `Health.Died` and calls `ClearBuffs` deferred. A poison tick that kills the actor therefore never removes the `ActiveBuff` that is still inside its own `_PhysicsProcess`. `ApplyBuff` rejects a dead actor. Player death clears too; it is terminal today.
10. **One tint presenter per monster owns the overlay slot.** `StatusTintPresenter` maps conditions to tints. It holds an ordered list of `StatusTint` entries (condition and color), and the first active entry wins. It is the only writer of `MaterialOverlay` on the meshes it collects, and it sets the overlay only while a tint is visible.
    - Rejected: one presenter per condition. Two presenters would fight over the single overlay slot.
    - Rejected: a buff referencing its own visual. As with screen effects, status sources never reference presentation.
    - Rejected: swapping `MaterialOverride`. That conflicts with `FadeOutComponent` and loses the model's albedo.
11. **Invalid payloads fail loudly.** `OnHitStatus.IsValid` requires a non-null `Buff`, `Buff.Duration > 0` (on-hit statuses must expire) and `0 < Chance ≤ 1`. `TryPrepareAttack` rejects an attack with an invalid entry, as it already does for an invalid contact profile. `MonsterSceneValidator` reports it, so the monster is disabled with an error at spawn instead of silently never attacking. Tests cover weapons and monsters as content. On-hit buffs and their conditions are external `.tres` files, never embedded.

## Contracts

Proposed names are marked `+`; `~` marks changes to existing types.

```csharp
// + scenes/attacks/OnHitStatus.cs
/// A buff an attack may apply when one of its direct contacts deals unprotected damage. Duration and strength
/// belong to the Buff; reference a different Buff for a weaker or shorter status.
[GlobalClass]
public partial class OnHitStatus : Resource
{
	[Export] public Buff Buff { get; set; }                                         // required, external .tres, Duration > 0
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1f; // per qualifying contact
	public bool IsValid { get; }
}

// ~ scenes/attacks/AttackDefinition.cs
/// Statuses applied by this attack's direct melee or projectile contacts; splash applies none.
[Export] public OnHitStatus[] OnHitStatuses { get; set; } = [];

// ~ scenes/items/Weapon.cs
/// Statuses applied by every attack made with this weapon, after the attack definition's own.
[Export] public OnHitStatus[] OnHitStatuses { get; set; } = [];

// ~ scenes/attacks/PreparedAttack.cs
public sealed record PreparedAttack(AttackDefinition Definition, AttackPlayback Playback, double Duration,
	IReadOnlyList<OnHitStatus> OnHitStatuses = null); // null or empty: none

// ~ scenes/attacks/AttackController.cs
/// Also rejects (returns false, starts nothing) when any definition or weapon status is invalid.
public bool TryPrepareAttack(AttackDefinition definition, StringName weaponStyle, double duration,
	out PreparedAttack attack, MeleeAttackProfile weaponContact = null, IReadOnlyList<OnHitStatus> weaponStatuses = null);

// ~ scenes/attacks/Projectile.cs — new trailing parameter; cleared in OnDespawnedToPool
public void Initialize(/* existing parameters */, AttackIdentity parentExecution = default,
	IReadOnlyList<OnHitStatus> onHitStatuses = null);

// ~ common/combat/DamageContext.cs
/// Statuses to roll if this contact resolves to Damaged; null for none.
public IReadOnlyList<OnHitStatus> OnHitStatuses { get; init; }

// ~ scenes/components/HurtBoxComponent.cs
/// Receives on-hit statuses; unassigned means this actor ignores them.
[Export] public BuffController Buffs { get; set; }
```

`PlayerAttackController.TryPrepareWeaponAttack` passes `weapon?.OnHitStatuses`. `EnemyBehaviorComponent.TryAttack` is unchanged, because the strategy's definition already carries its list.

```csharp
// + common/interfaces/IBuffTarget.cs
/// The actor state a buff acts on. BuffController implements it for players and monsters.
public interface IBuffTarget
{
	HealthComponent Health { get; }
	/// Stat block of actors that have one (the player); null for monsters.
	PlayerStats Stats { get; }
}

// ~ scenes/buffs/Buff.cs
public enum BuffStacking { Stack, Refresh }
[Export] public BuffStacking Stacking { get; set; } = BuffStacking.Stack;
public virtual bool CanApplyTo(IBuffTarget target) => true;
public virtual void OnApply(ActiveBuff activation);   // unchanged; reads activation.Target
public virtual void OnRemove(ActiveBuff activation);
// ~ PeriodicBuff.OnTick(IBuffTarget target); PoisonBuff and HealingBuff use target.Health
// ~ StatModifierBuff.CanApplyTo => target.Stats != null; OnApply/OnRemove use activation.Target.Stats, keyed by the activation

// ~ scenes/player/ActiveBuff.cs
public IBuffTarget Target { get; private set; }  // replaces Player
public void Initialize(IBuffTarget target, Buff buff);
/// Restarts the remaining duration; keeps the tick phase. No-op for permanent buffs.
public void Refresh();

// ~ scenes/player/BuffController.cs (path kept; it is no longer player-specific)
public enum BuffApplication { Applied, Refreshed, Rejected }
public partial class BuffController : Node, IBuffTarget
{
	[Export] public HealthComponent Health { get; set; } // required; replaces the Player export and GetOwner fallback
	public PlayerStats Stats { get; set; }              // runtime context injected by Player; null on monsters
	public BuffApplication ApplyBuff(Buff buff);
}
```

`ApplyBuff` contract:

- It returns `Rejected` when `buff` is null (with `PushError`), when `Health` is dead (from slice 2, when `Health` exists), or when `buff.CanApplyTo(this)` is false. A rejection cures nothing, creates no `ActiveBuff`, calls no callbacks and fires no `BuffsChanged`.
- With `Refresh` stacking, if an `ActiveBuff` holds the same instance and has not yet expired, it runs the existing cure step, calls `Refresh`, fires `BuffsChanged` and returns `Refreshed`.
- Otherwise it returns `Applied`, with the existing sequence: cure, `OnApply`, listing, `BuffsChanged`. An expired instance waiting for the sweep does not count as active. The new instance is listed before the sweep removes the old one, so `HasCondition` stays true throughout.

The `Player.ApplyBuff` wrapper and the item callers ignore the result. `Player._EnterTree` assigns `BuffController.Stats` immediately after `Stats.CreateRuntimeCopy()`, before any child `_Ready` and before `AutoEquipItems` can apply an equipment buff. `ActiveBuff` stops its tick loop as soon as `Deactivate` has run.

```csharp
// + scenes/effects/status/StatusTint.cs
[GlobalClass]
public partial class StatusTint : Resource
{
	[Export] public StatusCondition Condition { get; set; } // external, e.g. poisoned.tres
	[Export] public Color Color { get; set; }               // alpha = strength at full intensity
}

// + scenes/effects/status/StatusTintPresenter.cs
/// Tints an actor's meshes while its BuffController has one of the listed conditions; the first active entry wins.
[GlobalClass]
public partial class StatusTintPresenter : Node
{
	[Export] public BuffController Buffs { get; set; }        // required, same actor
	[Export] public Node3D VisualRoot { get; set; }           // required; GeometryInstance3D descendants collected in _Ready
	[Export] public ShaderMaterial TintMaterial { get; set; } // required shared definition; duplicated once per presenter
	[Export] public Array<StatusTint> Tints { get; set; } = [];
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float FadeInTime { get; set; } = 0.25f;
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float FadeOutTime { get; set; } = 0.5f;
}
```

The presenter mirrors `StatusScreenEffectDriver`. It subscribes to `BuffsChanged` until it exits the tree, and on each notification and once at start it re-reads `HasCondition` for every entry. It tweens the duplicated material's `tint` uniform. It assigns `MaterialOverlay` when fading toward a visible tint and clears it when a fade-out completes. A missing required export produces `PushError` in `_Ready`, and the presenter stays idle. `FadeOutTime` must stay below `DeathComponent.Delay` (2 s), so the tint is gone before the corpse fades. Meshes are collected in the presenter's `_Ready`, which runs before `EnemyBase._Ready` adds the x-ray silhouettes, so the silhouettes are never tinted.

## Scene and resource wiring

```
player.tscn
├─ HurtBoxComponent      + Buffs = ../BuffController                 (slice 1)
└─ BuffController        ~ Health = ../HealthComponent (replaces Player) (slice 2)

enemy_base.tscn (inherited by rat, minion, warrior, Warden)
├─ HurtBoxComponent      + Buffs = ../BuffController                 (slice 2)
├─ + BuffController        Health = ../HealthComponent                (slice 2)
└─ + StatusTintPresenter   Buffs = ../BuffController, VisualRoot = ../Pivot,
                           TintMaterial = status_tint_material.tres, Tints = [poisoned_tint.tres] (slice 3)
```

`skeleton_crossbow.tscn`, `melee_enemy.tscn`, `projectile_enemy.tscn` and `mobile_enemy.tscn` get the same nodes and assignments. `MonsterSceneValidator` checks the following:

- `HurtBoxComponent.Buffs`, when assigned, is local and its `Health` is the actor's `HealthComponent`.
- A `StatusTintPresenter`, when present, has a local `Buffs`, a `VisualRoot` and a material.
- Every `OnHitStatus` in `Behavior.AttackStrategy.Attack` is valid.

Content:

- `+ scenes/enemies/rat/rat_venom.tres`: `PoisonBuff`, Name "Plague venom", 1 damage per tick, 1 tick/s, 4 s, `Stacking = Refresh`, `Condition = poisoned.tres`.
- `~ scenes/enemies/rat/rat_attack.tres`: the `bite` definition gets `OnHitStatuses = [OnHitStatus { Buff = rat_venom.tres, Chance = 0.35 }]`. The `OnHitStatus` is an embedded sub-resource; the buff is external.
- `+ scenes/items/weapons/assassin_venom.tres`: `PoisonBuff`, Name "Assassin's venom", 2 per tick, 1 tick/s, 5 s, `Refresh`, `poisoned`.
- `~ scenes/items/weapons/dagger_rare.tres` (Assassin's Dagger, tier 4): `OnHitStatuses = [Chance 0.3, assassin_venom.tres]`.
- `+ scenes/effects/status/status_tint.gdshader` (spatial, unshaded, alpha-blended, rim-weighted `uniform vec4 tint`), `status_tint_material.tres` and `poisoned_tint.tres` (`StatusTint`, `poisoned`, green).

All values are starting tuning for playtests.

## Call stacks

Monster melee bite poisons the player:

```
EnemyBehaviorComponent.TryAttack
├─ AttackController.TryPrepareAttack(policy.Attack, style, duration)
│  ├─ + any invalid OnHitStatus → false; nothing starts, no cooldown       ← validation before acceptance
│  └─ PreparedAttack(copy, playback, duration, + authored definition statuses)
├─ AttackController.StartAttack(attack, …)                                 + keeps attack.OnHitStatuses
└─ AttackController._PhysicsProcess → ApplyMeleeContact(target)
   ├─ damage and crit rolls on _random                                     (existing)
   ├─ DamageContext { …, Random = _random, + OnHitStatuses }
   └─ HurtBoxComponent.ResolveDamage(in context)
      ├─ filter · death · active defense · invulnerable                    (existing; early return → no status, no draw)
      ├─ evasion · accuracy · armor rolls on contact.Random                 (existing; Evaded/Absorbed/Blocked → no status)
      ├─ DamageTaken · Health.TakeDamage(final) · hit stun/push/effect      (existing)
      ├─ + Damaged, Health alive, Buffs assigned → for each status in order:
      │     Chance < 1 ? draw Randf() : no draw;  draw ≥ Chance → trace chance_failed
      │     else Buffs.ApplyBuff(buff) → Applied | Refreshed | Rejected     → trace status.resolved
      │       Applied → cure · OnApply · listed · BuffsChanged
      │                 └─ PoisonDriver re-reads HasCondition(poisoned) and fades the overlay in   (existing)
      └─ Resolved(Damaged) → damage.resolved trace · DamageResolved signal  ← observers already see the status
```

Projectile (direct hits only):

```
AttackController.SpawnProjectile
└─ Projectile.Initialize(…, _random.Randi(), CurrentExecutionId, + prepared.OnHitStatuses)
Projectile.OnHitDetected
├─ damage and crit on the projectile RNG                                   (existing)
├─ ResolveDamage(DamageContext { DeliveryKind = Projectile, Random = projectile RNG, + OnHitStatuses })
└─ ApplyImpactRadiusDamage(excluding the direct target)
   └─ AreaImpact contexts: OnHitStatuses = null                            ← splash never applies statuses
Projectile.OnDespawnedToPool → + clears its statuses                        ← pooled reuse cannot inherit them
```

Player weapon:

```
Weapon.TryPrepare / MartialTechniqueAction / UnarmedAttackAction
└─ PlayerAttackController.TryPrepareWeaponAttack(weapon, …)
   └─ TryPrepareAttack(definition, style, duration, contact, + weapon?.OnHitStatuses)
      └─ PreparedAttack.OnHitStatuses = definition's ++ weapon's            (unarmed: none)
```

Poison on a monster, through to death:

```
monster HurtBoxComponent → Buffs.ApplyBuff(assassin_venom) → ActiveBuff.Initialize(controller, buff)
ActiveBuff._PhysicsProcess → PoisonBuff.OnTick(target) → target.Health.TakeDamage(n)   (no armor, defense or stagger)
└─ HealthComponent.Died
   ├─ DeathComponent: clear spawn ID, loot, XP                                         (existing)
   ├─ EnemyBase: ActionGate.Disable                                                     (existing)
   └─ + BuffController: CallDeferred(ClearBuffs) → OnRemove for each · one BuffsChanged
      └─ StatusTintPresenter re-reads, fades out within FadeOutTime < DeathComponent.Delay
```

## Affected files

```
+ scenes/attacks/OnHitStatus.cs                                    1
~ scenes/attacks/AttackDefinition.cs, PreparedAttack.cs            1
~ scenes/attacks/AttackController.cs, Projectile.cs               1  prepare validation, contexts, projectile plumbing
~ common/combat/DamageContext.cs                                   1
~ scenes/components/HurtBoxComponent.cs                            1  Buffs export, application, status.resolved trace
~ scenes/buffs/Buff.cs                                             1  Stacking; 2  IBuffTarget callbacks, CanApplyTo
~ scenes/player/ActiveBuff.cs                                      1  Refresh; 2  IBuffTarget, tick-loop guard
~ scenes/player/BuffController.cs                                  1  BuffApplication, refresh; 2  IBuffTarget, Health, Stats, death clearing
~ scenes/player/player.tscn                                        1  HurtBox.Buffs; 2  BuffController.Health
+ scenes/enemies/rat/rat_venom.tres                                1
~ scenes/enemies/rat/rat_attack.tres                               1
~ scenes/enemies/MonsterSceneValidator.cs                          1  payload; 2  BuffController wiring; 3  presenter
+ common/interfaces/IBuffTarget.cs                                 2
~ scenes/buffs/PeriodicBuff.cs, PoisonBuff.cs, HealingBuff.cs, StatModifierBuff.cs   2
~ scenes/player/Player.cs                                          2  inject Stats in _EnterTree
~ scenes/items/Weapon.cs, scenes/player/PlayerAttackController.cs  2
~ scenes/enemies/enemy_base.tscn, skeleton/skeleton_crossbow.tscn,
  templates/{melee,projectile,mobile}_enemy.tscn                   2  BuffController; 3  presenter
+ scenes/items/weapons/assassin_venom.tres                         2
~ scenes/items/weapons/dagger_rare.tres                            2
~ scenes/debug/combat_harness.tscn                                 2  add the Assassin's Dagger to Weapons
+ scenes/effects/status/StatusTint.cs, StatusTintPresenter.cs      3
+ scenes/effects/status/status_tint.gdshader, status_tint_material.tres, poisoned_tint.tres   3
+ tests/OnHitStatusTest.cs                                         1, extended in 2
+ tests/CombatTestActors.cs, ~ tests/CombatDefenseTest.cs         1  shared dodge/guard actor builders
~ tests/CombatIntegrationTest.cs                                   1, 2
~ tests/ItemCatalogTest.cs                                         1  poison content check covers on-hit statuses; 2  weapon payloads
~ tests/ActiveBuffTest.cs, tests/StatusConditionTest.cs            2  IBuffTarget signatures
~ tests/MonsterAuthoringTest.cs                                    1, 2
+ tests/StatusTintPresenterTest.cs                                 3
~ docs/combat-system.md, docs/combat-harness.md                    1
~ docs/stats-and-itemization.md                                    1, 2
~ docs/monster-authoring-design.md, docs/monster-catalog/README.md 1, 3
~ scenes/enemies/templates/README.md                               2, 3
~ docs/weapon-attack-system.md, docs/item-catalog.md, ARCHITECTURE.md  2  buffs no longer player-only
~ docs/README.md, docs/development-roadmap.md                      each slice: plan status
```

## Slices

Delivery is vertical: each slice ends with a status that can be played in the combat harness and in gameplay. Slice 1 needs only items 1 and 2 of the task (payload and application), because the player already has a `BuffController`.

1. **Monster attacks poison the player.** Adds the payload, prepare validation, the melee and projectile context plumbing, and application in the hurtbox. It also adds `Buff.Stacking`, `ActiveBuff.Refresh` and the `BuffApplication` result, wires the player's `HurtBoxComponent.Buffs`, adds the `status.resolved` trace event, validates payloads in `MonsterSceneValidator`, and authors the Plague Rat venom.
   - Exercise: in the combat harness, let a rat bite the hero repeatedly. Some bites poison: the overlay fades in and health ticks down. Further bites restart the timer instead of stacking. Bites that are dodged, parried or blocked never poison.
   - Verify:
     - `dotnet build`.
     - `dotnet test`, with the tests below.
     - Import, then `inspect_resource.gd` on `rat_venom.tres` and `rat_attack.tres`.
     - A headless `main.tscn` smoke run (`Map generated.`, no validator errors).
   - Limitation: monsters cannot receive statuses, and no weapon carries any.
2. **Player weapons poison monsters.** Adds `IBuffTarget`, the buff signature change, `Health`/`Stats` on `BuffController`, the `Player` stats injection, death clearing and the tick-loop guard. It also adds `Weapon.OnHitStatuses` and its merge, the `BuffController` and `HurtBox.Buffs` wiring in the five enemy roots, the validator wiring checks, and the Assassin's Dagger venom.
   - Exercise: equip the Assassin's Dagger from the debug menu or from the harness `Weapons` list. Some hits poison a rat or skeleton: its health bar keeps dropping between hits, and a poison kill drops loot and grants XP. Health, regeneration and strength potions still affect the player exactly as before.
   - Verify:
     - `dotnet build` and `dotnet test`.
     - `dotnet build -c ExportRelease`, which keeps test sources out of exports.
     - A resource check of the edited weapon and buff files.
     - A headless smoke run.
     - The editor's *Validate monster scene* on each of the five monsters and the three templates.
   - Limitation: a poisoned monster looks unchanged apart from its health bar.
3. **Monster status tint.** Adds `StatusTint`, `StatusTintPresenter`, the shader and material, the `poisoned` tint entry, the presenter in the five enemy roots, and the validator check.
   - Exercise: poison a monster: it gains a green tint that fades out when the venom ends or the monster dies, before the corpse fades.
   - Verify:
     - `dotnet test`.
     - A windowed screenshot through a temporary preview scene or the windowed combat harness, then reading the PNG. Headless renders are blank.
     - The editor acceptance exercise below.
   - Limitation: one tint at a time, chosen by list order. There are no HUD icons.

No slice depends on a later one, and the condition foundation is already on `main`.

## Tests

`OnHitStatusTest`, `[RequireGodotRuntime]` (hurtbox and controller are Nodes). It reuses the actor builders from `CombatDefenseTest`, moved into a shared test helper.

- A `Damaged` contact with `Chance = 1` applies the buff once. Dodged, parried, blocked with chip damage, evaded, absorbed, invulnerable and filtered contacts apply nothing.
- A killing blow applies nothing. A hurtbox without `Buffs` applies nothing.
- Draws, compared by RNG state with an identical contact without a payload:
  - A non-qualifying outcome, a missing `Buffs` and `Chance = 1` each draw nothing.
  - `Chance < 1` draws exactly once per status, and a fixed seed reproduces the same result.
- `Refresh` keeps one `ActiveBuff` with its full duration restored, and `Stack` adds a second instance. `ApplyBuff` results cover null, dead target, refresh and apply.
- `TryPrepareAttack` rejects null buffs, permanent buffs and chances of 0 or above 1. `PreparedAttack.OnHitStatuses` holds the authored instances.
- Slice 2 adds the following:
  - Weapon statuses are merged for primary, technique and ranged attacks, and unarmed attacks have none.
  - On an actor without stats, `PoisonBuff` ticks its `HealthComponent` and `StatModifierBuff` is `Rejected` with no signals.
  - A poison tick that kills clears every buff, and no buff ticks or reverses twice.
  - A freshly spawned player still accepts stat buffs.

`CombatIntegrationTest`:

- Slice 1: a projectile carrying a status poisons the hero on a direct hit. A wall impact whose splash damages a nearby target applies no status. The trace contains `status.resolved` correlated with the execution.
- Slice 2: the hero with a certain-venom weapon poisons the harness monster, which loses health over time and dies.

Content checks:

- `ItemCatalogTest.EveryAuthoredPoisonCarriesThePoisonedCondition` also walks `OnHitStatuses` on weapons under `scenes/items` and on attack strategies under `scenes/enemies`. Every on-hit `PoisonBuff` must carry `poisoned`, be an external file and be timed.
- `ItemCatalogTest` checks that every weapon's statuses are valid.
- `MonsterAuthoringTest` checks that an invalid payload or broken `Buffs` wiring is reported, and that the five monsters and three templates validate.

`StatusTintPresenterTest` (slice 3):

- Syncing to a controller that already has the condition assigns the overlay. Losing the condition clears it once the fade completes, with the fade times set to 0.
- Priority order picks the first active entry.
- Missing exports leave the presenter idle.
- The authored material is never mutated.

Editor acceptance (slice 2 and 3): open `rat.tscn` and assign a different venom `.tres` to the bite's `OnHitStatus`. Duplicate `melee_enemy.tscn`, remove its `BuffController`, and confirm that *Validate monster scene* reports the dangling `HurtBoxComponent.Buffs`. Run the harness and confirm that the runtime `ActiveBuff` references the authored buff.

Not automated: navmesh-dependent approach (see `AGENTS.md`) and the tint's look.

## Persistence

No save data changes, and `SaveGame.CurrentVersion` (currently 14) stays unchanged. Payloads are authored on definitions, and weapons are rebuilt from their definition path, so authored `OnHitStatuses` come back on load. Timed buffs are not saved on any actor, and live monster health is not saved either. A save taken while something is poisoned loses the remaining ticks, as player poison does today. Nothing changes generation or loot draws.

## Open decisions

1. **Undead and poison.** Three of the five first-realm monsters are skeletons. Making them immune would be thematic but would leave the dagger with little to poison on early floors. The recommendation is to add no immunities now. If they are wanted, an authored immunity list on `BuffController` becomes one more `Rejected` rule.
2. **Poisoned bolts on the crossbowman.** The projectile path is covered by tests. The recommendation is to keep the existing crossbowman's shots plain for readability and to give poisoned bolts to a later variant.
3. **Tuning.** The chances, tick damage and durations above are starting values for a playtest at depths 2–3.

## Not covered

- Coatings applied to a single weapon at runtime (a poison vial used on a blade) need item save fields. Rolled affixes such as "of Venom" would need `RolledAffix` to carry statuses. Both extend the weapon surface from decision 2.
- Stat-changing statuses on monsters, such as slow or weaken, stay `Rejected` until monsters have a stat block. Monster AI reacting to `blinded` or `confused` would read `HasCondition` on the monster's `BuffController`.
- Cure effects, resistances, HUD or health-bar status icons, player log lines (see the [logging plan](logging-plan.md)), and weapon tooltip lines. No weapon stat UI exists today.
- Poison darts and other traps. `FloorTrap` uses the legacy damage path, but it could pass a payload through `DamageContext` later.
