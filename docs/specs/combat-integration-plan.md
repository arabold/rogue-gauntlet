# Combat integration and harness

Scope: repair the reviewed active-defense integration and replace the animation playground with a combat harness. See [actor actions](../actor-actions-and-interactions.md), [weapon attacks](../weapon-attack-system.md), and the existing [action plan](actor-actions-and-interactions-plan.md). The wider [logging proposal](logging-plan.md) remains separate.

## Ownership and contracts

- `AttackController.StartAttack(..., ActorActionLease lease = default)` adopts a valid player preparation lease or acquires an enemy/direct attack lease. Its clock reports WindUp, Active, Recovery and releases on every completion/interruption. Rejection leaves the current action untouched.
- `ActorActionGateComponent` owns stagger duration and terminal disabled state. Movement reads the gate; seconds are never passed as impulse strength. Death cancels every owner. Save restoration converts remaining committed combat time to a neutral stagger lockout without immunity.
- `CombatAnimationComponent` is the sole rig pose owner, using owner tokens. Attack and defense clocks sample poses without modifying shared Animation resources. Exported Node references and defense profiles provide bindings; no rig-name search.
- Dodge/guard duplicate accepted definitions. Explicit guard rebind cancels active defense and preserves cooldowns. Hurtboxes retain passive evasion and use the contact's explicitly supplied RNG.
- Physics stages: input sampling, defense intentions, defense clocks, AI/clearance steering, movement, damage executors, interaction completion, trace snapshots. Priorities establish cross-actor order.
- Enemy defense uses a perceived target (or an explicitly injected harness opponent), one decision per execution/projectile, and a source-derived defense RNG. Debug forcing changes probability only, never cooldowns.
- `CombatTrace` is a plain C# structured diagnostic event source, off without listeners. The harness owns subscription/file lifecycle and filters its subtree. Records carry a schema version, event, physics frame, actor, execution/source identity and typed fields. It is not a gameplay registry or bus.
- `CombatHarness` replaces the former animation harness and its scene. It exposes production combat commands, a fixed seed, optional per-frame snapshots and JSONL output. Command scripts use explicit physics-frame offsets; tests exercise the same entry points as buttons.

## Call order

```text
Input / harness command / enemy defense intention
  -> gate acquisition (validate before mutation; synchronously cancel previous owner)
  -> executor snapshots definition and owns lease / animation token
Defense clocks -> steering -> MovementComponent (gate constrains voluntary motion)
Attack/projectile clock
  -> canonical hurtbox -> ordered defense evaluators
  -> matching-execution parry interruption OR evasion / armor / health
  -> structured resolution with rolls and identities
Interaction/item completion -> harness snapshot
Save -> actual remaining lockout; load -> cancel transient actions, restore timers and neutral lockout
```

## Delivery and verification

Hybrid slices: (1) ownership, clocks, movement and damage, with focused regressions; (2) shared presentation, authored bindings, enemy policy and persistence, with engine scene tests; (3) combat harness, trace schema, scripted commands, analyzer and documentation, with JSON parsing and a headless scenario. Keep all callers/resources loadable between slices. No save migration; bump the version for the corrected combat/save contract. Validate Debug and ExportRelease builds, focused GdUnit4 tests, scene imports and a gameplay smoke. Full-suite failures must be reported separately from targeted evidence.

Implementation status: the three slices are implemented. See [combat actions](../combat-system.md) and [combat harness](../combat-harness.md) for current behavior and reproducible verification commands.
