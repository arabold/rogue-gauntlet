# Monster authoring and first-realm rollout

Status: slice 1 implemented, October 23, 2026; slices 2–4 remain proposed. Verification and remaining editor/playtest limits are recorded below. [Monster design and authoring](../monster-authoring-design.md) owns agreed gameplay/authoring decisions; [Level design](../level-design.md), [Combat](../combat-system.md) and [Randomness](../randomness-and-seeds.md) describe existing systems.

## Scope and current seams

Deliver editable Godot monster assemblies and basic tactical strategies, retain the three skeletons, move the Warden into its own gameplay folder, and introduce the Studio rat. Retire old Quaternius gameplay monsters and their unused game exports. Preserve approved Studio recipes, source geometry, technical rigs, references and provenance, including adopted sources. Do not recreate visuals. Later Studio monsters, realm reordering, flanking, weapon switching and boss phases are excluded.

Current code to reuse:

- `EnemyBase` resolves authored children, registers defenses, initializes combat RNG, and currently creates a missing action gate at runtime.
- `EnemyBehaviorComponent` and `EnemyContext` host the state machine and attack requests. `ChasingState` currently mixes movement with ranged-to-melee fallback. `FleeingState` has no automatic trigger and uses direct away steering.
- `EnemyBehaviorProfile` mixes perception, attack numbers/definitions, action durations and defense probabilities. `AttackController` owns prepared attack execution; `CombatAnimationComponent` owns pose tokens. Preserve that execution path.
- `EnemyDefenseController` observes threats, rolls responses, maintains guard pressure and requests melee ripostes. Keep its reaction execution and checks; extract its authored policy and allow its riposte to request the single configured attack.
- `HealthComponent`, `MovementComponent`, `DeathComponent` and `LootTableComponent` currently own scattered defaults. Death emits XP through `SignalBus` and records cleared spawn IDs.
- `RealmEncounterFactory` selects `MobSpawnEntry` scene paths by threat/weight/minimum depth. `DungeonMobFactory` contains legacy fallback paths. `SpawnPoint` instantiates scenes and skips cleared identities; `RealmEncounterController` substitutes finite nests and places the Warden.
- Survivor health/position/AI reset on floor reconstruction today. This delivery retains that rule; cleared enemies, summon slots and claimed rewards remain persistent.

## Ownership and authored files

New names below are proposed. Retain existing component names and runtime execution boundaries.

| Type | Kind and owner | Responsibility |
| --- | --- | --- |
| `MonsterDefinition` | `[GlobalClass]` Resource, assigned on `EnemyBase` | Stable ID/name, starting max health, movement speed, XP, loot table and `EnemyBehaviorProfile` reference |
| `EnemyMovementStrategy` | Abstract Resource base | Return movement intent from supplied context/runtime state |
| `CloseDistanceStrategy`, `MaintainDistanceStrategy`, `AttackThenRetreatStrategy` | Separate `[GlobalClass]` Resources | Inspector-tuned movement choices |
| `EnemyAttackStrategy` | Abstract Resource base | One attack's definition, damage/accuracy/crit/duration/range and eligibility decision |
| `MeleeAttackStrategy`, `ProjectileAttackStrategy` | Separate `[GlobalClass]` Resources | Melee reach or clear-sight projectile policy, without fallback delivery |
| `EnemyDefenseStrategy` | `[GlobalClass]` Resource | Guard/dodge probabilities, conditional riposte and current defense reaction/pressure tuning |
| `EnemyRetreatCondition` | `[GlobalClass]` Resource | Optional low-health trigger, hysteresis, duration and distance |
| `EnemyTacticsState` | Plain C# object owned by each `EnemyContext` | Retreat phase/timers, low-health latch and accepted execution identity |
| `MonsterSceneValidator` | Ordinary C# helper | Read-only definition/scene checks via stored Godot properties, used by tests and a local editor validation command |
| `MonsterAuthoringInspector` | Small editor plugin | Validate selected monster scene; show node/resource paths and actionable errors; no scene generation |

`EnemyBehaviorProfile` retains perception, patrol/search and non-attack lifecycle timing. Add exported `MovementStrategy`, `AttackStrategy`, `DefenseStrategy` and nullable `RetreatCondition`. Move attack/defense tuning into their strategy Resources and remove superseded exported fields after migrating their retained scenes and harness callers; remove all remaining legacy fields when the retired roster is deleted in slice 3. During the bounded transition, actors with a `MonsterDefinition` use only the new policy fields; unmigrated legacy actors use only the existing path until removal. Never merge values from both. The final system has no legacy branch, parallel authorities or default attack definitions synthesized in code.

`MonsterDefinition` does not reference its actor scene: the scene references the definition, and encounter entries reference scene paths. Preserve lazy scene loading. No global monster registry/catalog singleton is needed.

Health, speed, XP and loot defaults have one home in the definition. Apply them once before child `_Ready` methods subscribe or initialize behavior, using an `EnemyBase._EnterTree` initialization boundary with explicitly assigned child references. Definition and child dependencies must already be present in the authored scene; no generated gameplay child nodes. Prove inherited/instanced Godot wiring order in slice 1 before migrating content. Preserve unrelated component defaults for non-monster users. A scene with a monster definition does not also author conflicting component defaults.

Shared Resources are immutable during play. Components hold current health, effective speed and cooldowns; `EnemyContext` creates/resets/disposes its own tactics state with scene lifetime. Remove local completion-event subscriptions on exit; a stale completion identity cannot change a newer attack or retreat phase. Strategies receive state/context explicitly and never store targets, timers or RNGs. Existing prepared attacks/defenses retain their per-execution snapshots. Editing a Resource in the editor affects future scene instances; arbitrary live edits are unsupported. No gameplay work runs in Resource constructors or exported setters.

## Abbreviated contracts

These are signatures/ownership contracts, not compilable implementations. One concrete editor-created Resource per script file. Resource strategy bases are C# types; no custom records cross a Variant/GDScript boundary.

```csharp
// Proposed MonsterDefinition exports on a shared Resource.
string Id { get; set; }
string DisplayName { get; set; }
float MaxHealth { get; set; }
float MoveSpeed { get; set; }
int XpReward { get; set; }
LootTable LootTable { get; set; }
EnemyBehaviorProfile Behavior { get; set; }

// Proposed EnemyBase exports: definition and typed authored component references.
MonsterDefinition Definition { get; set; }
// Explicit references include HealthComponent, MovementComponent,
// DeathComponent, LootTableComponent, EnemyBehaviorComponent and ActionGate.

// Proposed Resource methods; context/state and decisions are ordinary C# helpers.
EnemyMovementIntent EnemyMovementStrategy.Evaluate(
    EnemyContext context); // slice 1 is stateless; retreat state arrives with slice 2
bool EnemyAttackStrategy.CanAttack(EnemyContext context);
EnemyDefenseDecision EnemyDefenseStrategy.Choose(
    bool canGuard, bool canDodge, float roll); // controller owns and draws its RNG
bool EnemyRetreatCondition.ShouldEnter(float healthFraction, EnemyTacticsState state);

// Existing TryMeleeAttack callers transition to this single configured path.
bool EnemyBehaviorComponent.TryAttack();
// Proposed local execution completion event; identity and interruption are mandatory.
// AttackController emits exactly once per accepted execution, after releasing ownership.
event Action<AttackIdentity, bool> AttackFinished; // bool means interrupted

// Proposed validator; errors do not mutate the inspected scene.
string[] MonsterSceneValidator.Validate(Node actor);
// Editor plugin invokes Validate on the selected scene and renders issues.
```

Reuse `scenes/components/LootTable.cs`. Add `LootTableComponent.ConfigureTable(LootTable table)` for definition initialization before readiness; it assigns the table without rolling drops or resetting claimed state. Drop chance/count, creation policy and source identity remain authored on the loot component, since they describe source delivery rather than monster identity. Do not duplicate those fields in the definition. The inspector command is for edit-time authored scene inspection, not a way to change running actors. Runtime validation logs the same errors and disables that invalid monster's AI/action processing rather than creating missing capabilities. Encounter validation must catch invalid candidates before generation; a bad required boss fails floor setup visibly and never unlocks descent.

Required checks: nonempty stable/unique definition IDs in active pools, positive health, nonnegative speed/XP, complete required component references, valid attack/projectile/contact/animation/socket bindings, guard/dodge capability and presentation for nonzero probabilities, valid distance bands, valid retreat thresholds/durations and encounter depth ranges. Optional defense/retreat Resources may be null. Guard/dodge components may exist with zero response probability for manual harness exercises. Missing required attack/movement strategy is an error.

Movement intents carry a goal (hold, approach or retreat), desired separation and whether ordinary attacks are allowed; they do not move Nodes themselves. The context/host resolves those intents through navigation. Successful retreat movement suppresses ordinary attacks until the retreat/hold phase ends; blocked retreat permits the configured attack as described below. Defense remains independently eligible.

`TryAttack` returns false without changing action ownership, consuming damage RNG or starting a retreat cycle when dead, blocked by the gate, out of reach/sight, or invalid. It prepares and starts through the existing `AttackController`. Riposte success notifications occur only after acceptance. Riposte uses the same configured attack and range/sight checks; it never bypasses guard release recovery. Replace old melee-specific riposte range tuning with the configured attack's engagement contract.

## Strategy behavior contracts

- **Close:** approach until within the attack's stop distance; hold when eligible. Preserve throttled repathing, reachability-based chase retention and doorway crossing commitment.
- **Maintain distance:** author `MinimumDistance < PreferredDistance < MaximumDistance`, bounded by attack engagement range. Approach when too far; back away when too close; hold within the band when a shot is clear. Without sight, use ordinary navigation toward the last known target position to regain a shot. No cover search or flanking.
- **Attack then retreat:** close/attack using the single attack policy, then retreat after normal recovery completion. Subscribe to accepted execution identity, not an estimated timer or hit event. A rejected/interrupted attack never triggers retreat. Stop retreat at desired separation or maximum duration, wait the authored re-engagement delay, then approach again. If blocked, hold and use eligible attack/defense; elapsed retreat time still advances.
- **Low health:** optional priority over normal tactics after the current action permits movement. Defaults: enter at 25%, rearm only after recovery above 40%; maximum duration and desired distance must be positive. A consumed low-health retreat stays latched through its timeout until health exceeds the exit fraction. Loss of target ends active retreat but does not rearm that latch. Death ends all state. No regeneration is added.
- **Destination feasibility:** extend `NavigationComponent` with `TrySetRetreatDestination(Vector3 threatPosition, float distance)` returning false without changing the current destination when no candidate is usable. Try a fixed bounded set along the away direction; project/check reachability, physical clearance, floor support and meaningful increased separation. Reuse existing collision/large-actor rules. Preserve doorway crossing before choosing a new destination. This is a local feasibility check, not route planning to shelter. Actual navigation acceptance needs an editor playtest.
- **Defense:** one categorical draw per observed execution: dodge probability, guard probability, remaining probability none. Require each probability in [0,1] and their sum <= 1; an unavailable capability contributes to none, not another reroll. Existing dodge-first independent rolls are intentionally replaced. Conditional riposte gets one subsequent roll after a successful defense sequence. Preserve threat revalidation, pressure/maximum hold, cooldowns, directionality and harness forced modes. No new melee-only restriction on shields.

## Scene templates and manual authoring

Add `scenes/enemies/templates/mobile_enemy.tscn` as a small copyable assembly and `melee_enemy.tscn` / `projectile_enemy.tscn` as independent example copies using shared component instances. No shield-specific actor hierarchy. Include the shared action gate explicitly; remove `EnemyBase`'s runtime fallback for migrated actors. Guard, dodge and their reaction controller are optional authored child nodes with exported references. Remove optional nodes freely and update references/Resource probabilities; validation explains mismatches. Guard alone or dodge alone is valid.

Retain existing core component scenes; extract a reusable scene only when missing and actually shared. Keep model and collision/animation configuration local to each monster; different rigs must not be forced into humanoid paths. Reuse the existing combat/defense animation Resources. Verify the rat's actual clips/contact before selecting tactics; do not invent dodge/guard clips for it.

Monster folders own scene, definition and customized policy Resources. Shared generic policies/templates live under `scenes/enemies/strategies/` and `templates/`; generic attack/defense definitions retain their existing homes. The Warden gameplay scene, definition, fit and behavior Resources move from `scenes/enemies/bosses/` to `scenes/enemies/forsaken_warden/`. Keep the already dedicated Studio authoring folder `characters/bosses/forsaken_warden/`; its family category is not a gameplay responsibility. Export the Warden into `assets/custom-monsters/forsaken_warden/`, updating recipe destination and references together.

## Encounter, RNG and persistence contracts

Extend `MobSpawnEntry` with exported `MaximumDepth` (zero = no upper bound); retain positive threat/weight and inclusive `MinimumDepth`. Realm pool membership is the realm filter. Validate bounds and that each shipped floor can select an affordable eligible entry. No monster-level field or automatic stat scaling. Ordinary XP comes from `MonsterDefinition`; preserve source-destruction suppression for nest-bound survivors.

Keep `MobFactory`, `RealmEncounterFactory`, `SpawnPoint` and the level-local controller as the selection/instantiation boundary. Factories select complete authored scenes, not component lists or JSON gameplay recipes. Replace the legacy fallback pool with retained authored monsters before deleting referenced content; unconfigured later depths may use this explicit development fallback without implying finished realms. Do not infer realm membership from folder names.

```text
Level.ConfigureGeneration -> existing realm factory / MapGenerator
  -> seeded encounter selection (stable ordered entries; independent monster stream)
  -> clearance/placement -> SpawnPoint with concrete scene + stable PersistentId
  -> existing nest/boss substitution and cleared-ID checks
  -> instantiate authored actor; source metadata and dependencies before readiness
     -> EnemyBase initialization: validate definition, apply initial defaults
     -> child initialization -> local tactics state, combat streams
     -> normal perception/state-machine update
        -> honor doorway crossing and action ownership
        -> low-health override or movement strategy -> NavigationComponent
        -> attack eligibility -> TryAttack -> AttackController/action gate
           -> normal completion -> matching tactics retreat phase
        -> EnemyDefenseController -> defense strategy -> guard/dodge
           -> successful defense/release -> conditional same-policy TryAttack
  -> death -> existing clear-ID + source loot + XP notification
```

Preserve separate source-derived attack/defense streams. Basic movement/attack eligibility is deterministic and needs no random draw. Patrol randomness stays outside encounter generation; do not introduce global RNG consumption for new tactics. Resource validation/editor inspection consumes no RNG. Tests compare concrete selected definitions, positions and rewards for the same build/seed, including after different combat decisions. Do not promise historical seed outputs or physics replay.

No new survivor snapshots: health, position and tactical phase reset on revisit/load as documented today. Clear transient tactics/retreat state when an actor is recreated; it cannot grant immunity or extra rewards. Keep current player defense/commitment persistence unchanged. Bump `SaveGame.CurrentVersion` when the roster/generation changes invalidate saved deltas; reject mismatched/missing versions without conversion or overwriting the old slot. Verify current-version cleared IDs, nest slots, Warden unlock and source rewards survive save/load.

## Affected files

Paths below are relative to repository root; globs are bounded by the stated selection rules.

```text
+ scenes/enemies/MonsterDefinition.cs
+ scenes/enemies/MonsterSceneValidator.cs
+ addons/monster_authoring/{plugin.cfg,MonsterAuthoringInspector.cs}
+ scenes/enemies/strategies/{EnemyMovementStrategy,CloseDistanceStrategy,MaintainDistanceStrategy,AttackThenRetreatStrategy}.cs
+ scenes/enemies/strategies/{EnemyAttackStrategy,MeleeAttackStrategy,ProjectileAttackStrategy,EnemyDefenseStrategy,EnemyRetreatCondition}.cs
+ scenes/enemies/ai/EnemyTacticsState.cs
+ scenes/enemies/templates/{mobile_enemy,melee_enemy,projectile_enemy}.tscn
~ scenes/enemies/{EnemyBase,EnemyBehaviorComponent,EnemyBehaviorProfile,EnemyDefenseController,NavigationComponent}.cs
~ scenes/enemies/ai/{EnemyContext,ChasingState,FleeingState}.cs
~ scenes/attacks/AttackController.cs                       execution completion notification
~ scenes/components/LootTableComponent.cs                 explicit table configuration before readiness
~ scenes/enemies/skeleton/*                              retain all three, migrate authored config
+ scenes/enemies/rat/*                                   Studio rat, definition and policies
+ scenes/enemies/forsaken_warden/*                       relocated boss and migrated config
- scenes/enemies/bosses/forsaken_warden*                  after all references move
- scenes/enemies/{bat,cactoro,demon,dino,dragon,ghost,mushroom_king,orc,slime,spider,squidle,wizard,yeti}/
~ scenes/levels/encounters/{MobSpawnEntry.cs,sunken_garrison.tres}
~ scenes/levels/dungeon/dungeon_mob_factory.tres          safe development fallback
~ scenes/levels/encounters/bone_nest.tscn                 rat slots only once rat is ready
~ scenes/debug/{CombatHarness.cs,combat_harness.tscn,scenarios/*}             single-attack policy/config selection
~ project.godot                                         editor plugin registration
~ asset-studio/characters/bosses/forsaken_warden/character.json  art destination only
~ assets/custom-monsters/{beasts/rat*,forsaken_warden/}   verified Studio exports
- assets/custom-monsters/bosses/forsaken_warden*          after destination change
- assets/quaternius-monsters/                            only after reverse-reference audit
~ scenes/attacks/{animation_profiles,melee_profiles}/*    remove only unreferenced retired profiles
~ assets/ATTRIBUTIONS.json                               preserve accurate Studio-source provenance
~ scripts/saves/SaveGame.cs                              version bump when content changes land
+ tests/{MonsterAuthoringTest,EnemyTacticsTest}.cs
~ tests/{EnemyStateMachineTest,EnemyDefenseTest,RealmEncounterTest,WardenTest,MainSceneSmokeTest}.cs
~ tests/*                                               remaining old profile/caller references
~ docs/{monster-catalog/README.md,level-design.md,combat-system.md,development-roadmap.md}
~ README.md / ARCHITECTURE.md / asset-studio/README.md    authoring links/status as work lands
```

Inspect reverse references across text paths, UIDs, scripts, Resources, debug scenes, tests, catalog images and export receipts before removal. Update callers first. Studio sources and rigs must build without the deleted game exports; adopted third-party source provenance remains accurate. Remove only unreferenced catalog images/profiles, not shared attack implementations or skeleton assets. Re-export retained approved rat/Warden visuals through normal Studio commands and validate current source/export fidelity.

## Delivery slices

Hybrid delivery: prove initialization/editor composition first, then deliver vertical playable monster/strategy slices, then remove obsolete content and tune the complete first realm. Every slice leaves the default game and harness loadable.

1. **Editable skeleton foundation — implemented.** Implement definition/default initialization, validation/Inspector command, templates and close/melee strategies. Migrate minion and warrior, including defense policy extraction and all their callers. Keep other enemies on their existing path only until their own migration/removal; do not build adapters or dual-format data. Exercise two skeleton instances with independent health/timers, delete/reassign optional guard/dodge, and change stats/loot in the Inspector. Verify initialization before child readiness, same-policy riposte, no missing-node generation and no shared Resource mutation. Existing legacy roster remains temporarily available.
2. **Crossbow and basic tactics.** Migrate the crossbowman to projectile-only/maintain-distance; remove its inherited melee fallback. Add attack-then-retreat and optional low-health override with execution completion identity. Retain current legacy profile fields solely for unmigrated content until slice 3, then remove them. Test categorical defense, blocked retreat/timeouts/latch reset, failed/interrupted attack behavior and ranged ripostes. A temporary harness variant proves shooter-plus-shield composition without adding a new shipped monster. Test real retreat navigation in the editor; no flanking or automatic weapon swap.
3. **Studio rat, Warden and retirement.** Export approved visuals, prove rat clips/contact, compose rat with melee/attack-then-retreat and optional low-health retreat. Migrate/move Warden with existing single-phase attack and clearance behavior. Update pools, nests, fallback, harness/tests and source/UID references, then delete retired monsters, unreferenced profiles and old catalog images. Remove superseded profile fields/default generation and bump save version for changed content. Verify Studio still builds from its own sources. This delivers the full small roster; later monsters remain unintegrated.
4. **Three-floor tuning and authoring acceptance.** Set depth bounds/weights/threat, tune role mixtures, recovery supplies, XP and Warden pacing. Complete the manual creation exercise below, generation/save checks and multi-seed playtests. Update current-system docs and catalog from actual shipped Resources. Record unresolved balance findings instead of claiming fun from automated tests. No dependency on later realm order, shops, boss phases or the separate room/access implementation plan.

## Verification and acceptance

For C# slices run `dotnet build "Rogue Gauntlet.sln"`, targeted GdUnit4 suites using `GODOT_BIN` and `.runsettings`, and an ExportRelease build when validating editor/test exclusion. Keep pure decision/timer/probability tests outside Godot runtime where practical. Engine tests cover actual scene composition, references, attacks/projectiles, guard/dodge, reward emission and runtime-instance isolation. The editor plugin must be editor-only and not run in exports.

Use the repository's `godot-mcp` skill for implementation-time import, UID/resource verification and main-scene smoke checks. Use Studio's normal rat/Warden export/audit and GPU lineup workflow; inspect the existing approved visuals in motion rather than remodeling. Headless navigation cannot certify retreat pathing: exercise close/keep-distance/retreat through doors, walls, corners and boss-clearance spaces in the editor. Ranged shots must respect sight; blocked shooters do not bite.

Compare actual encounter identities/positions and source rewards across same-seed generations, different combat histories and clear-ID restoration. Validate inclusive depth bounds, no affordable candidates, invalid scenes, finite nests, cleanup without rewards and Warden descent/save restoration. Old saves require a new run without silent overwrite.

Editor acceptance: duplicate the projectile example into a temporary review scene, assign the skeleton crossbow art/animation and a unique definition, change health/XP/range in the Inspector, add guard with its Resource, remove it, add dodge with its Resource, and run validation after each edit. The configured tactics and defenses must work through production components. Spawn two copies to demonstrate independent runtime state. Save/reopen the scene and confirm all edits persist without C# or Studio edits; remove the temporary fixture afterwards.

Play depths 1–3 with at least two contrasting classes and three seeds, including a mid-run save/resume and floor revisit. Record unfair attacks, crowd/doorway congestion, time spent chasing, useful escape/defense choices, supply pressure and boss recovery windows. Balance values remain in Godot Resources; catalog tables are generated/reference summaries. Full player XP progression design is deferred.

## Slice 1 delivery notes

Minion and warrior scenes now reference `MonsterDefinition` defaults and close-distance/melee strategies. The warrior uses a separate categorical defense Resource. Defaults apply in the actor's `_EnterTree` before children become ready; required component references and the action gate are authored in scenes. The crossbow scene explicitly retains its old defaults/profile until slice 2, avoiding accidental inheritance of the minion definition. The rest of the roster remains on the existing path until migration/removal.

`templates/mobile_enemy.tscn` is an unfinished assembly; `templates/melee_enemy.tscn` is a complete independent actor root with reusable component/model instances. Its [authoring instructions](../../scenes/enemies/templates/README.md) explain duplication, unique Resources and optional capabilities. The projectile example is deferred to slice 2 rather than shipping an example without its intended policy.

Project > Tools > Validate monster scene inspects the open scene without changing it. Validation reads stored Godot properties because non-tool C# gameplay components are native placeholders in the editor; it does not mark gameplay scripts as tools or execute them. A headless editor-mode probe successfully loaded and validated both skeletons and the independent melee example. Runtime tests establish actual initialization order, per-instance health isolation, invalid-value/missing-reference diagnostics, guard removal and categorical defense intervals. Final verification passed: Debug and ExportRelease builds, Godot import/UID checks, editor-mode validation of the three complete examples, and 43 targeted tests spanning authoring, combat integration, defense, encounters, seeded generation and main-scene smoke. Build warnings are confined to existing Phantom Camera code; headless shutdown/orphan diagnostics remain the known engine/test-runner noise.

Remaining acceptance: interactive authoring/visual review and navigation/balance playtests. Encounter-wide unique-ID/depth validation, projectile-only crossbow behavior, retreat strategies, Studio rat/Warden rollout and old-roster removal remain in later slices. The current slice preserves the existing seeded roster and save format.

The slice 1 wiring follow-up makes actor-facing attack/defense references and controller-to-capability references explicit in scenes. Guard/dodge components retain their authored actor, movement, gate and animation assignments; neither they nor the defense controller search for missing dependencies during readiness/processing. Validation inspects the assigned references rather than inferring capabilities from node names. Regression coverage includes renamed components, missing assignments despite existing nodes, and cleared controller references remaining unavailable before/after readiness.

Explicit-wiring verification: the warrior and templates save their component/gate assignments; controller references stay empty when cleared. The targeted authoring, combat, defense, encounter, seeded-generation and main-scene smoke suites cover this follow-up. Existing legacy actor migration remains bounded by the rollout slices.
