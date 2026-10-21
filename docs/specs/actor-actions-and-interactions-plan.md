# Actor Actions and Interactions Implementation Plan

Status: in progress (Slices 1 & 2 complete), October 21, 2026. [Actor Actions and World Interactions](../actor-actions-and-interactions.md) owns gameplay rules and initial tuning. Follow [AGENTS.md](../../AGENTS.md) and the [program-design skill](../../.agents/skills/program-design/SKILL.md). Existing [weapon attacks](../weapon-attack-system.md), [stats](../stats-and-itemization.md), and [RNG/save behavior](../randomness-and-seeds.md) remain the baseline.

## Current seams and main risk

`PlayerInputController` requests `ActionManager.TryPerformAction`, and player-specific `ActionSlot` owns asynchronous preparation/cooldown. Weapon execution runs through `PlayerAttackController` and shared `AttackController`; `AttackAnimationComponent` owns its combat-clock pose. `EnemyBehaviorComponent` has a separate timed action layer over the behavior state machine. It asks the same attack executor to attack and uses `MovementComponent.FaceDirection` during wind-up. `MovementComponent` alone performs movement and collision.

`MeleeHitDetector` queries live overlaps during contact, including a body footprint, and `AttackController` de-duplicates owners before `HurtBoxComponent.ResolveDamage`. The hurtbox currently resolves faction, random evasion, debug immunity, armor, health and knockback. `Projectile` instead calls `IDamageable.TakeDamage`, including an area-impact path. The current `DamageResolution` is an outcome and amount only. No active-defense owner or shared action gate exists. `Invulnerable` is a general debug flag, not a dodge implementation.

The current input conflict is concrete: `InputComponent.IsInteractPressed()` reads `action_1`, while PlayerInputController also requests slot zero from that action. `PlayerInventoryController.OnItemEquipped` adds every equipped IPlayerAction (including Weapon) to the first available slot. Slot count therefore mixes core attack with item use. InteractionArea/PlayerInteractionController use player-specific IInteractive callbacks; TryInteract chooses the most recently entered target and invokes it immediately. InteractiveComponent.Interacted(Player) drives Door/Chest mutation without an action boundary. Inventory TryUse also applies/consumes immediately; it cannot simply be assigned to an async quick slot without risking duplicate consumption.

The main risk is multiple owners accepting conflicting actions, target commits, or stale callbacks. Unify accepted action ownership across attack, item use, defense and interactions before adding timings. Keep world authority on target components and inventory consumption in the existing inventory transaction. A second risk is resolving parry synchronously while iterating melee contacts: cancelling the attack can invalidate its execution. Preserve the existing execution-reference check after each resolved target.

## Proposed types and ownership

All names below are proposed. The gate covers attacks, abilities, defense, interactions and forced reactions; ordinary locomotion remains separate. Resource definitions are immutable; accepted actions copy their numeric settings and presentation binding into per-execution snapshots. One `[GlobalClass]` Resource per file.

| Type / home | Kind and contract |
| --- | --- |
| `scenes/actions/ActorActionGateComponent.cs` | Actor-local Node; exclusive committed-action ownership and allowed transitions, generation-stamped leases; no independent animation or duration clock |
| `scenes/combat/DefenseComponent.cs` | Actor-local Node; dodge/guard/parry and parry-stagger phase, facing/direction snapshots, cooldown/rearm/recovery clocks and accepted-defense notifications |
| `scenes/combat/DodgeDefinition.cs` | GlobalClass Resource; distance, travel/recovery duration, immunity start/end, cooldown; nonnegative finite values, immunity contained in travel |
| `scenes/combat/GuardDefinition.cs` | GlobalClass Resource; raise/parry/release timings, rearm/minimum-release intervals, facing sector, movement multiplier, damage multiplier, parry stagger duration; parry can be disabled |
| `scenes/actions/ActorCapabilities.cs` | GlobalClass Resource on actor; innate dodge/guard definitions, explicit world-interaction permissions and actor-specific defense presentation profile; null capability means unavailable |
| `scenes/combat/DefenseAnimationProfile.cs` | GlobalClass Resource mapping dodge, guard raise/hold/release, parry and stagger to valid rig clips or explicit procedural/VFX fallbacks |
| `common/actions/ActorActionLease.cs` | Ordinary C# token, action kind and phase enums; stale lease releases/notifications cannot affect a newer action |
| `common/combat/DamageContext.cs` | Ordinary C# immutable record: execution identity, source/owner/faction, amount/accuracy, incoming XZ direction, authored defense traits and delivery kind |
| `common/interfaces/ICombatDamageable.cs` | Optional typed contract returning `DamageResolution` for a `DamageContext`; hurtboxes implement it; props can retain `IDamageable` |
| `scenes/enemies/EnemyDefenseController.cs` | Actor-local observer/decision Node; reaction timer, per-threat decision and runtime RNG; requests the same `DefenseComponent` commands |
| `scenes/enemies/EnemyDefensePolicy.cs` | GlobalClass Resource; enabled choices, probability per observed threat, reaction range/delay, consideration distance and safe-destination rules; no mutable RNG or remembered threats |
| `scenes/interactions/ActorInteractionComponent.cs` | Actor-local Node; stationary channel clock, target/verb snapshot and reservation, cancellation and progress; player and AI invoke the same executor |
| `scenes/interactions/InteractionDefinition.cs` | GlobalClass Resource; verb ID/label, nonnegative duration, interaction range, displacement tolerance and required permission; no runtime progress |
| `common/interactions/{ActorInteractionContext,InteractionOffer,InteractionReservation}.cs` | Ordinary C# snapshots/tokens identifying actor, target state revision, operation, duration and prerequisites; not saved Nodes |
| `common/interfaces/IInteractionTarget.cs` | Actor-neutral offer/reserve/commit/release contract on InteractiveComponent; no Player-only mutation callback |
| `scenes/player/PlayerQuickItemController.cs` | Four binding records, pickup auto-assignment, item identity/count display, target selection and use requests; no independent effect/consumption path |
| `common/actions/QuickItemBinding.cs` | Ordinary data: Unassigned, Variety(definition identity), or Instance(inventory InstanceId plus definition identity for depleted display); assigned-but-depleted differs from Unassigned |
| `common/interfaces/ICombatAttackAction.cs` | Player action preparation/start adapter for Weapon and an unarmed fallback; keeps ActionSlot's existing prepared-attack path independent of quick items |
| `scenes/player/actions/UnarmedAttackAction.cs` | GlobalClass Resource using PlayerAttackController.TryPrepareWeaponAttack(null); no duplicate damage executor |


Add `GuardDefinition` reference to `EquipableItem`, not to `Weapon` alone: shields currently use the common equipment family. It is a capability, not an armor-number inference. `PlayerInventoryController` observes existing local equip/unequip events and recomputes effective guard using the concept's offhand/main-hand/body precedence. Character model replacement rebinds presentation. Replacing a guard source immediately ends guard, retains all defense cooldowns/rearm, and validates the replacement before advertising it. Capability absence is valid; malformed assigned data is an authoring error.

`DefenseComponent` exports explicit Actor, Movement, HurtBox, ActionGate and presentation dependencies. `AttackController`, player item-use slots, and ActorInteractionComponent export the same gate. ActorCapabilities also exports interaction permissions (OpenDoors, OpenContainers, UseMechanisms, PickMechanicalLocks); null/absent permissions grant nothing. Runtime tools/inventory are queried through the actor context, not by downcasting every actor to Player. Player/Enemy hosts compose and initialize them, including life/stun callbacks. The gate does not query autoloads or know about inventory, player input, AI states or world registries. No player-only `IPlayerAction` implementation becomes the shared defense or interaction executor.

## Abbreviated behavioral APIs

These are design signatures, not compilable files or a promise that ordinary C# records cross Godot Variant boundaries.

```csharp
// Gate: atomic ownership, not another timer.
ActorActionRequestResult TryAcquire(ActorActionKind kind, ActorActionPhase phase,
    Action<ActionInterruptReason> onInterrupted, out ActorActionLease lease);
void ReportPhase(ActorActionLease lease, ActorActionPhase phase);
void Release(ActorActionLease lease);

// DefenseComponent: gameplay and AI use identical entry points.
DefenseRequestResult TryDodge(Vector3 direction);
DefenseRequestResult TryBeginGuard(Vector3 facing);
void EndGuard();
void Cancel(ActionInterruptReason reason);
bool TryStagger(float duration);
DefenseEvaluation Evaluate(in DamageContext contact);
void ApplyLoadout(ActorCapabilities loadout, GuardDefinition equippedGuard);

// AttackController: identity-checked reaction entry, composed with DefenseComponent.
bool TryInterruptForParry(AttackIdentity execution, float staggerDuration);

// HurtBoxComponent / ICombatDamageable:
DamageResolution ResolveDamage(in DamageContext contact);

// EnemyDefenseController:
void Initialize(RandomNumberGenerator gameplayRandom);

// Debug harness uses Variant-compatible wrappers.
bool RequestDodge(Vector3 direction);
bool RequestGuard(Vector3 facing);
void ReleaseGuard();
```

`TryAcquire` validates the current kind/phase before replacing it. Every action owner registers a cancellation callback. On an accepted replacement, invalidate the old lease, synchronously cancel its executor/preparation, then publish the new lease. The supplied callback belongs to the requested new owner and is retained for its later interruption. Old completion callbacks are ignored. Reentrant requests during a replacement are rejected as busy; notifications are published only after the new owner is installed. The caller validates its capability, target/direction and presentation before requesting ownership. AI additionally validates its proposed destination; a player dash aimed into an obstacle remains an accepted blocked dash with its normal cooldown. A rejected request has a reason (`Unavailable`, `Busy`, `Cooldown`, `Disabled`, `InvalidDefinition`, `NoDirection`) and changes nothing. Accepted actions notify local listeners only after ownership and snapshot installation succeed.

Gate phases are reported by the executor that already owns the clock: AttackController derives wind-up/contact/recovery from its prepared execution; ActionSlot reports its preparation delay as attack wind-up; DefenseComponent reports its own travel/guard/recovery. Gate state cannot independently expire. Death/stagger has priority over defense, and defense may replace only interruptible attack startup or ordinary guard. The complete allowed-transition table is in the concept. Existing non-attack slots remain exclusive and deny defenses until completion; no generic cancellation of consumable side effects.

## Dedicated weapon actions and item quick actions

Add an ActionSlot role (PrimaryAttack, SecondaryTechnique or QuickItem) and dedicated PrimaryAttackSlot/SecondaryTechniqueSlot in player.tscn. Reuse preparation, cooldown and generation cancellation; do not add another attack timer. ActionManager.ActionSlotCount and its indexed access enumerate only the four QuickItem slots. Primary and secondary each have an explicit request and separate availability/cooldown view, not a fake slot index such as -1 sent into ActionContainer. PlayerAttackController remains the sole weapon/unarmed executor.

Weapon and a resolved secondary-technique adapter implement the proposed ICombatAttackAction preparation/start contract, and UnarmedAttackAction uses the already-supported null-weapon preparation. ActionSlot checks that adapter rather than assuming every prepared attack is a Weapon. Rebinding equipment respects the action gate, interrupts permitted obsolete startup and updates both dedicated slots; it never calls AssignFirstAvailableAction for a weapon. A slot binding that references an unequipped weapon is invalid, not a second attack source. Existing four ActionContainer widgets display quick items; add a separate compact primary/defense status view with explicit local subscriptions.

PlayerQuickItemController stores exactly four bindings and observes completed inventory acquisition and change notifications. Auto-assignment happens only after Acquire succeeds, source.CommitCollected and OnPickup complete, and Inventory.DeferChanges publishes the owned state. Resolve merges to the owned variety/entry; do not retain the world Item reference if it was merged into an existing stack. Fill the lowest truly Unassigned slot, skip already-bound varieties, and never overwrite reserved depleted slots or mutate pickup success because UI slots are full. Batch pickups process their committed acquisition order. Initial eligible items are explicitly quick-usable ConsumableItem definitions (potions, scrolls and opted-in food); do not use the broader IPlayerAction test. Add an authored AllowQuickUse property to ConsumableItem; equipment/tools/keys are ineligible. Fresh-run starter consumables can use the same assignment operation in inventory order; loading restores saved bindings without rerunning auto-fill.

Variety bindings use Item.DefinitionPath and the existing stack-compatibility rules, count compatible owned stacks, and resolve one owned entry deterministically by inventory order at use time. Instance bindings use existing InventoryItemSlot/ItemEntrySaveData.InstanceId plus Item.DefinitionPath for non-stackable mutable consumables. Keep the definition identity after depletion for safe icon/disguise display; never retain or reconstruct hidden per-instance food state merely to populate an empty quick slot. Food never silently advances to another instance or reveals contamination. Depleted bindings retain identity and show count zero. Manual clearing/reordering uses explicit commands; duplicate variety bindings are rejected. Variety cooldowns use definition identity, independent of slot indices or individual stack splits/merges. A removed binding cannot erase a running cooldown. Identification UI uses ItemIdentity presentation rather than raw names or hidden properties.

Move direct consumption behind a request/commit boundary: public TryBeginItemUse validates ownership, target and gate, snapshots the chosen source and timing, and reserves its source unit against conflicting drop/use operations. Existing PlayerInventoryController.TryUse callers and quick items both migrate to that request. The old atomic effect/removal logic becomes an internal TryCommitItemUse used only at the end of accepted preparation/use. Positive damage, stale source, cancellation, target invalidation or death invalidates its generation and releases the reservation, so no late callback can drink/read twice. Preserve existing deterministic source identity and runtime item state. Existing ConsumableItem.PerformAction can still supply the effect internally; do not call its immediate effect independently from the slot path.

The accepted item's Delay + PerformDuration is the initial stationary use channel, with effect and one-unit removal at successful completion. Cooldown starts on acceptance and survives interruption. Inventory/UI cannot also use/drop the reserved unit while preparation is pending; unrelated inventory operations retain their normal policy. At completion use one validated inventory transaction, then emit ItemConsumed and refresh quick counts. Slot binding is not cleared merely because count becomes zero. A targeted scroll selects an existing valid inventory target before accepting the use channel; cancelled targeting spends nothing. Target selection is UI state, not an invisible combat lease; revalidate source, target and cooldown when submitting the accepted action. This slice changes current instant-use behavior and must update its lifecycle tests explicitly.

## Elemental knowledge, equipped secondary and mana

Existing MagicStaff/RangedWeapon and PlayerAttackController already support a magical primary projectile through a CustomAttackDefinition. Keep that primary path. The current PerformSpecialAttack/CreateDefaultDefinition(isSpecial) hardcodes a sword spin; it is not a generic learned-spell system and must be replaced by explicit secondary resolution, including playground calls. Do not pick magic from a model name or reuse the player's four item slots as spell bindings.

Additional proposed data/components:

| Type / home | Contract |
| --- | --- |
| `common/actions/Element.cs` | Earth, Fire, Water, Wind; explicit None for non-elemental equipment, never a wildcard matching all spells |
| `scenes/actions/WeaponTechniqueDefinition.cs` | GlobalClass Resource; stable technique/cooldown ID, attack definition/style, execution duration and cooldown; martial techniques cost no mana initially |
| `scenes/magic/SpellDefinition.cs` | GlobalClass Resource; stable spell ID, element, mana cost, technique definition, targeting profile and effect definition; no learned/cooldown state |
| `scenes/magic/{ElementalSkillTree,SpellUnlockNode}.cs` | One GlobalClass Resource per file; five spell unlock entries per element, point costs and prerequisite IDs; validate unique IDs, element agreement and acyclic prerequisites |
| `scenes/magic/SpellProgressionComponent.cs` | Player-owned learned-ID set, unspent/granted point accounting and selection per element; no class permission check |
| `scenes/combat/ManaComponent.cs` | Actor-owned current/max mana and gameplay-time regeneration; atomic spend and explicit snapshot/restore; no automatic refill on equipment changes |
| `scenes/actions/EquippedActionResolver.cs` | Ordinary C# resolver; weapon primary and martial secondary or compatible selected learned spell; returns a reason when unavailable |
| `scenes/attacks/CastTargetingProfile.cs` | GlobalClass Resource; facing projectile, self, assisted ground point or perpendicular line; range/placement and preview tuning |
| `scenes/magic/SpellAreaEffect.cs` | Shared runtime Node for a released persistent damaging field; lifetime/pulse clock and captured context, not another cast wind-up timer |

Weapon exports an optional SecondaryTechnique reference. MagicStaff exports its required element; normal staff/loot variants share that element but may differ in stats. Add authored player/starter spell grants and skill-tree catalog references through CharacterClass/player composition. Starting mage gets a matching staff, a free primary and an initial learned/selected spell; other classes may spend points on the same trees. Existing classes keep their stat profiles rather than being checked by name at cast time. Add explicit max-mana/regen/spell-power derivation fields to StatProfile and resolved PlayerStats as needed; magical damage uses that declared scaling rather than accidentally taking a melee Strength bonus through the generic damage path. Initial element tags do not require a complete resistance/reaction system.

SpellProgressionComponent grants points from run levels once: store total granted points/last awarded level alongside unspent points and learned IDs. Class starter grants are separate authored grants and cannot be farmed by reloading. Prerequisites/affordability are checked before learning; reject unknown IDs, wrong-element nodes and invalid dependencies without mutation. Selection must be learned and match the chosen element. Store one selected ID per element, independent of staff InstanceId. New learning does not replace that selection. UI changes require a neutral actor and no living visible hostile within an authored loadout-safety radius; use a local group/range/line-of-sight query, not a new global combat registry. No respec implementation or per-spell rank system is required for the initial unlock-tree slice.

Proposed entry points include `TryLearn(spellId)`, `TrySelectSpell(element, spellId)`, `EquippedActionResolver.ResolvePrimary/ResolveSecondary`, and `ManaComponent.CanSpend/TrySpend`. Internal typed results distinguish Unlearned, NoSelection, WrongFocus, NoMana, Cooldown, InvalidTarget and Busy. Debug Node wrappers accept simple IDs/positions and expose primitive values; ordinary C# prepared snapshots need not cross Variant boundaries.

```text
Keyboard/gamepad primary / fresh secondary intent
  -> equipped-action resolver (current weapon + learned selection + focus)
  -> validate rig binding, target snapshot, cooldown, mana and gate
  -> accept one execution: lease + mana debit + character cooldown start atomically
     -> only after acceptance publish feedback / start prepared attack clock
     -> current AttackController samples pose and commits contact/release at its authored time
        -> captured target and power reach projectile/area/buff delivery
     -> interruption cancels this execution without refunding accepted mana/cooldown
```

Use the existing accepted prepared-attack clock; do not run a second spell timer beside it. Prepared data includes source weapon identity/element, technique/spell IDs, mana cost, cooldown key, damage/power snapshot and cast target. Primary and secondary requests obey the same startup/contact/recovery commitment rules. Secondary is new-press only; holding K/RB never repeats mana spending. Validation failures spend nothing and never reinterpret secondary as primary. Capture stats/target for the accepted execution so a mid-flight swap cannot raise released damage. Revalidate source compatibility before release; an externally lost focus cancels the outstanding cast without refund. Deny voluntary equipment swaps during active/recovery; a permitted startup rebind invalidates its pending callbacks and retains spent costs.

Keep primary, secondary and selected-spell cooldowns in a character-owned ledger keyed by stable technique ID. Dedicated ActionSlots reference that ledger rather than resetting cooldown when reassigned. Copies of a weapon/focus, other elements, spell selection, slot reassignment, save/load and stairs cannot erase an existing key's remaining time. Martial equivalents share an authored cooldown key. Mana regeneration continues in gameplay time (including during a free primary), pauses with gameplay, and never depends on frame count or a weapon instance. Capacity reductions clamp current mana; increases never refill it. Source/target validation, lease acquisition and resource debit have a single synchronous acceptance boundary with notifications deferred until all succeed; test rejection/reentrancy to prevent partial costs or double spends.

Keyboard aiming resolves from movement/facing and modest aim assistance. Existing valid soft targets provide position for a ground spell; otherwise use the authored forward placement distance. Self spells use the actor. The targeting validator requires discovered valid floor and unobstructed permitted placement, and rejects before resource debit if invalid. Wall orientation is perpendicular to the captured aim; ground previews persist through wind-up. Capture the actual location, not a live target reference that follows a moving enemy through a meteor delay. There is no mouse raycast dependency or new input for a targeting mode initially. UI input/selection cannot leak into gameplay after closing a menu.

Projectile Fireball can reuse the current projectile/area-impact mechanism. Wall of Fire adds a released, nonblocking timed field; each authored damage pulse has a distinct child execution identity and once-per-pulse owner de-duplication. A later pulse may hit after dodge immunity ends; this is explicit damage-over-time, not a retry of a single melee hit. Each pulse uses the shared DamageContext with authored defense traits (area damage normally dodgeable, not guardable/parryable). A released field snapshots source faction/power and finishes its lifetime after an equipment swap or source death, unless its definition explicitly requires a sustained focus. Self buffs use existing buff ownership/duration with explicit stacking policy; no permanent buff from replaying release. Stone/Ice walls with physical blocking require additional actor-overlap, doorway/stair exclusion and runtime-navigation verification before their content rollout; Fire Wall does not silently gain those mechanics.

Enemies can receive a selected authored spell and mana through the same components, without a player skill-point UI. A natural elemental caster may explicitly receive an innate focus element in ActorCapabilities; the default is None. Players always require the equipped matching staff; no body-focus override is granted by selecting the mage class. Legacy caster content remains functional while migrated explicitly; assigning a nonzero-cost learned spell without a mana source is an authoring error, not unlimited casting.

## Actor-neutral timed world interaction

Replace IInteractive's Player-only execution contract with IInteractionTarget. InteractiveComponent remains the reusable scene target, configured by InteractionDefinition and a local target-operation handler; remove direct mutation through Interacted(Player). InteractionArea stays a discovery adapter, not a timer or authority. PlayerInteractionController owns focus selection and player UI, while ActorInteractionComponent owns the accepted session. Enemy controllers provide an actor context and chosen offer directly; no player hotbar, input polling or floating key glyph is required.

Proposed key signatures, ordinary C# internally:

```csharp
InteractionOffer GetOffer(in ActorInteractionContext actor);
bool TryReserve(in InteractionOffer offer, out InteractionReservation reservation);
InteractionCommitResult TryCommit(in InteractionReservation reservation);
void Release(in InteractionReservation reservation);
InteractionRequestResult ActorInteractionComponent.TryBegin(in InteractionOffer offer);
void ActorInteractionComponent.Cancel(InteractionCancelReason reason);
ItemUseRequestResult PlayerQuickItemController.TryUseSlot(int index);
```

An offer includes target instance/revision, stable operation ID, required permission/tool method, range and one total duration. A reservation owns a single target operation until commit/cancel; it is runtime-only and not a saved lock state. Prepare validates without mutation, target reservation is tentative, then acquire the actor lease. Gate failure releases the tentative reservation and leaves the actor's current action untouched. Publish accepted progress only after both succeed. One actor owns one channel and one target accepts one reservation. Target state changes increment revision and invalidate obsolete offers/reservations.

```text
Player fresh interact hold / enemy's maintained interaction intent
  -> focus/goal adapter asks target for an actor-specific offer
  -> validate stationary actor, visibility, range, capability and prerequisites
  -> tentatively reserve target; acquire shared actor action lease
  -> ActorInteractionComponent snapshots target/verb/duration and emits progress
     -> input release, movement/combat intent, damage/stagger or lost validity cancels
        -> invalidate generation, release target + actor, clear progress; no world mutation
     -> elapsed reaches duration
        -> target revalidates reservation revision, actor, tools/range/access
        -> target commits operation and marks token consumed before callbacks
        -> Door/Chest/Lock source publishes committed state/deltas/rewards once
        -> release actor; cosmetic target animation may continue independently
```

Release/movement/combat cancellation and authoritative damage/target invalidation are processed before channel advancement/commit on the same tick. At most one action replacement is accepted afterward. Input-driven cancellation discards progress even if the replacement later fails; it never refunds already committed effects. ActionGate distinguishes Interaction from committed ItemUse, which denies voluntary cancellation in the initial contract. Save/pause/input suspension cancels world channels and releases reservations; resume requires a fresh press. No held-button auto-restart or automatic target retargeting.

Player focus selection filters discovered/visible/reachable nearby interactives, ranks facing/distance with stable target-path ties, and locks one target while channeling. It does not use last-entered overlap order. Only the focused prompt shows full actionable feedback. UI progress uses local accepted-session signals (target, label, elapsed, duration, cancel/outcome) and a circular progress widget; those signals do not commit gameplay. Expose Variant-compatible debug commands and primitive progress values instead of trying to marshal the C# records.

Door and Chest become operation handlers for Open/Close/OpenChest. They validate operation state again before calling existing motion/loot authority. Default InteractionDefinition duration is 1.0 seconds; authored zero-duration interactions may commit immediately through the same validation path, but ordinary doors/chests use the hold. Do not add a second hold threshold. Disable/refresh target availability while its cosmetic operation is busy; that animation is not another channel. Chest source loot still commits with opening and uses existing idempotent claims/persistence.

Mechanical locks remain owned by the dungeon access design. LockDefinition.PickDuration supplies the accepted PickAndOpen offer's total duration, overriding the ordinary one-second operation; LockComponent validates key/tool/gate and owns unlocked state, not a second picking timer. PickAndOpen commits only after gate/tool validation succeeds, and requests opening as one logical operation. A matching key is the preferred method when present; both key and pick remain reusable initially. No matching tools means an ineligible offer, not a progress bar that fails after five seconds. Encounter seals and concealed passages keep their existing authority and cannot be bypassed by generic interaction commands. Lock fixture acceptance can precede adding locks to generated floors, but actual lock functionality depends on the access implementation slice.

Humanoid AI may request a locally perceived ordinary-door operation when explicitly granted OpenDoors and able to reach its near-side interaction point. It must pause movement/attacks while the same session runs and can cancel to fight/flee. Rat/no-capability requests fail before reserving. Do not turn this into an omniscient closed-door path planner; preserve NavigationComponent/door link authority and test reachable local requests first. Door motion methods remain internal authority/persistence operations, never a duration/access bypass for AI intent.

## Acceptance, movement and animation flow

```text
PlayerInputController / EnemyDefenseController / combat playground
  -> DefenseComponent validates effective capability and snapshots settings
  -> shared ActorActionGateComponent accepts or rejects
     -> interruptible attack startup: invalidate old slot generation + cancel attack
        -> close contact/VFX and restore attack animation ownership
        -> preserve spent costs and remaining cooldown; do not refund or restart it
     -> DefenseComponent owns the accepted defense clock
        -> MovementComponent receives one exclusive locomotion override
        -> defense presentation owns its pose; no competing attack AnimationTree
        -> publish phase/feedback; release only the matching lease on completion
```

Modify `ActionSlot` to expose an explicit attack-interruption operation; merely calling `AttackController.CancelAttack` is insufficient while its async preparation awaits. Its invalidated timer cannot perform later. Reassignment, death and scene exit also invalidate pending work. The slot owns attack phase/cancellation notifications, while character-owned technique and item-variety ledgers own remaining offensive cooldowns; DefenseComponent owns defensive cooldowns; defense cancellation does not reset either. Enemy attack requests use the gate too, with `EnemyBehaviorComponent` retaining behavior and noncombat action ownership. While the gate owns defense, its recovery or stagger, the enemy host suspends behavior-state updates and ordinary attack/facing requests; do not duplicate defense timers in new EnemyAction values. Player busy/disabled checks likewise include the gate. Death and accepted ordinary hit stun cancel defense through the host; blocked chip damage does not enter that hit-stun path. Debug direct-attack entry points must pass the same gate; do not create a bypass through `StartAttack` overloads.

`MovementComponent` receives an explicit owner-token override for dash displacement or guard speed/facing. Input, AI navigation and attack-facing requests cannot overwrite it until release; death and accepted knockback can interrupt it through the coordinator. Continue collision-based MoveAndSlide, gravity, stair constraints and large-actor fit checks. A blocked dash ends its remaining displacement (without banking it), but its configured immunity/recovery clock continues. Set shared physics ordering explicitly in actor scenes: player input and enemy defense observations/request handling first, defense clock advancement second, ordinary steering and large-actor clearance constraints third, movement fourth, then attack/projectile and hazard damage resolution, then interaction/item-use channel advancement and final commit. This last step ensures a same-tick release, displacement, incoming damage or target invalidation wins over completion. Preserve that order across actors using process priorities rather than relying on incidental child order. Gate acquisition observes the previous completed boundary, so an accepted defensive request may cancel startup before this tick advances it into contact. A scripted/manual debug tick must apply the same ordering. No collision-layer removal or monitoring toggle. Clear overrides on cancellation and scene exit. Pause and slow motion use gameplay delta.

Defense/stagger presentation uses actor-specific bindings and one active pose owner. Extend the existing attack animation ownership protocol rather than running a competing mixer. Snapshot the validated binding; release returns to the current locomotion/death state. A missing optional clip must use an explicitly assigned fallback; invalid required bindings reject authoring/availability. Audit actual player, skeleton and large-rig clips in slice 1 before committing to names. Rat/golem game scenes are not assumed to exist because Studio exports exist.

## Damage and interruption flow

Add `AttackDefenseTraits` flags `Dodgeable`, `Guardable`, `Parryable`, separate from faction/damage source flags. `AttackDefinition` and default melee definition factories supply them; prepared attacks snapshot them. Author existing melee, ranged and area definitions explicitly to the concept's eligibility table. Do not infer defendability from the model, a weapon attachment, or Enemy/Boss flags. Hazard callers retaining the old signature receive no active-defense traits by default. Audit runtime-created definitions as well as `.tres` assets; do not leave an accidental default that lets poison be parried.

Use an execution identity `(source instance ID, local execution sequence)` at runtime, never a save key. Defense traits, identity and source direction travel through Projectile.Initialize and its pooled reset path. A melee incoming direction points horizontally from source root to target root at contact; at coincident centers fall back to committed strike direction. Projectile direction uses its travel direction. A guard compares captured facing to the opposite incoming direction; a zero/unknown direction cannot be directionally parried or guarded.

```text
AttackController timed contact / Projectile direct or splash contact
  -> existing world obstruction and target-owner de-duplication
  -> ICombatDamageable.ResolveDamage(DamageContext), else existing IDamageable adapter
     -> faction, alive state, debug immunity
     -> DefenseComponent.Evaluate
        -> Dodge: Dodged; no health, knockback, hit stun or parry
        -> Parry: consume window before notifying/cancelling attacker
        -> Guard: carry damage multiplier and suppress hit stun/knockback
     -> existing random evasion, reduced damage, armor and health pipeline as applicable
     -> typed DamageResolution + local feedback
  -> source consumes target contact even for Dodged / Parried / Blocked
  -> parry cancellation checks the captured execution before the next contact iteration
```

Extend existing `DamageOutcome` with Dodged, Parried, Blocked. Blocked reports final chip amount, including zero; preserve Evaded/Absorbed for non-block outcomes. Keep `DamageResolved(int,float)` compatible for Godot feedback; new internal evaluation/result records may carry parry response and suppression flags. Cosmetic hit particles are not unconditional on successful defenses: use defense-specific effects and sounds, and emit normal DamageTaken only for actual health loss. Chip damage updates health without cancelling guard.

DamageContext carries the source AttackController response reference as well as its identity. On parry, call its `TryInterruptForParry`: validate instance lifetime and matching current execution, then ask its composed DefenseComponent.TryStagger to acquire the high-priority stagger lease, cancelling the old attack through its registered interruption callback. DefenseComponent owns the stagger clock even on an actor whose optional dodge/guard capabilities are null. No attacker-name lookup or global event is used for this fallible operation. Projectile pooling must clear that response reference. Parry interrupts only a still-current matching attack execution and applies shared combat stagger to the source. Both player and enemy hosts stop offense/locomotion and cancel pending startup; stale projectile/contact references cannot cancel a newer action. No damage reflection or recursive damage resolution. When two parries are eligible in one melee sample, sort contacts by stable actor instance ID for that runtime; stop once the source execution is cancelled. Simultaneous incoming contacts are resolved in explicit simulation order (physics priority, then per-executor target order); consume the defender's parry before callbacks so it cannot succeed twice. Identical replay of physics is not promised.

Change `Projectile.TryGetDamageTarget` to canonicalize an actor body to its child HurtBoxComponent before accepting its root IDamageable facade. Otherwise the body collision would bypass the typed defense path. Body and hurtbox contacts retain the same damage owner.

Projectile direct contact and its splash share a root execution identity and damaged-owner set. An actively dodged, blocked or parried primary target is recorded before splash, so the explosion cannot bypass the defense. Other splash victims receive independently authored area eligibility; do not accidentally parry the explosion because its arrow was guardable. An ordinary direct projectile is consumed on those outcomes. Reflection and persistent beam/multi-hit exceptions are deferred. Legacy TakeDamage wraps a context with no defendability and retains existing behavior; every supported active-defense delivery must use the typed path.

## Player and enemy adapters

Player input reads just-pressed dodge/guard and guard release before secondary, primary, quick-item slots and fresh interaction hold handling. An accepted defense suppresses other requests that frame; a failed defense does not silently cancel another action. Expose availability/cooldown and feedback to UI with local subscriptions; no new SignalBus command API. Polling a held attack button cannot reacquire a slot while a defense or release recovery owns the gate. Focus loss, pause/input suspension and equipment removal release guard; resuming requires a fresh press.

EnemyDefenseController observes the current perceived opponent's public visible attack startup and snapshots an observation, not the player's key press or ActionSlot reservation. Add a read-only visible-threat view and local startup/end notification to AttackController. No broadcast threat registry is needed initially. Enemy policy draws probability/reaction delay once per observed execution, then revalidates sight, capability, cooldown, action phase and threatened range when the delay expires. A cancelled attack discards the reaction; it is not retried against that same execution. Candidate dodge positions must respect floor/prop/door and large-actor movement constraints; no valid direction means decline without cooldown. A chosen guard/dodge goes through the shared gate, with no animation-clock reads used to schedule a perfect parry.

Enemy instances own their runtime decision RNG, initialized explicitly by spawn/host setup using floor seed, persistent source ID and a dedicated defense-decision domain. Standalone playgrounds inject a fixed debug seed. Never draw from layout, encounter selection, source loot or item streams. A nest summon uses its existing stable summon ID. Probabilities/reaction draws depend on observed gameplay, not deterministic replay of player history; no cross-build seed stability or saved enemy decision history.

## Resources, lifecycle and persistence

Actor scenes export a ActorCapabilities; player effective guard is recomputed from current equipment on initialization and local equipment changes. Shared Resources never store cooldowns, guard-held flags, reaction decisions or runtime animation state. Accepted snapshots remain unchanged by Inspector edits; a supported explicit rebind cancels current defense and retains cooldowns before applying new valid definitions. Do not claim arbitrary live resource edits are safe.

Add `PlayerActionSaveData` under `PlayerSaveData`: remaining primary/secondary technique-ledger cooldowns, item-variety cooldown records, dodge cooldown, parry rearm cooldown and action recovery lockout in gameplay seconds. Persist four QuickItemBinding records with explicit Unassigned/Variety/Instance discriminants and stable definition/InstanceId references. A depleted bound instance remains a valid reserved binding; a malformed binding is a current-version validation error, not a reason to silently auto-fill. Never persist raw Resource/Node references. On load resolve inventory first, then bindings/counts/timers, with auto-assignment disabled for restoration. Partial world-interaction progress and target reservations are discarded; saving releases the live session before capture, with no target effect. Item use is either uncommitted (cancel without consuming, preserve its spent cooldown) or atomically committed; never save a half-consumed unit. A snapshot converts active dodge's remaining travel/recovery, attack recovery or parry stagger to a neutral reload lockout; it never grants the remainder of an invulnerability window on load. Loading clears held input, pending reactions, interaction/item-use sessions, action leases and movement overrides, restores the cooldown/lockout, and requires a fresh guard press plus its minimum-release interval. Equipment changes and floor travel carry those same player timers, so dropping/re-equipping a shield or climbing stairs cannot reset them. Stairs reject transfer while a committed attack/item use, interaction, defense or stagger owns the gate; movement cancellation must release an interaction before the existing transition can proceed. Gate completion must not auto-trigger a staircase without its normal overlap/exit rules; do not serialize half a dash onto another floor. Enemy revisits keep current transient-reset behavior.

Persist learned spell IDs, per-element selection, unspent/granted point accounting, current mana and the technique cooldown ledger under the player save data. Resolve/validate the authored catalog, inventory/focus and learned set before selections/timers; invalid same-version IDs/dependencies fail clearly rather than silently granting spells or resetting progress. Player mana and selected spell survive stairs with no refill. Snapshot/cancel uncommitted casts, retaining their already-paid costs. Live projectiles, delayed meteors, fields and temporary buffs follow the existing transient-runtime reset policy on load; do not serialize Node references or re-release effects from saved cast callbacks. Rebind source-equipped eligibility without changing learned knowledge. Enemy revisits retain their existing transient-reset policy.

Use SaveGame.CurrentVersion when schema changes land; reject incompatible saves without migration and test same-version round trips. Save codecs must preserve the new authored equipment guard reference by definition identity rather than serializing live Nodes; audit the existing custom item codec. Do not persist execution IDs, Node references, enemy cooldowns, guard-held input or observed threat records.

## Affected files

```text
+ docs/actor-actions-and-interactions.md                     intended gameplay
+ docs/specs/actor-actions-and-interactions-plan.md                 this proposed plan
~ docs/README.md, docs/game-design.md, docs/development-roadmap.md
+ common/combat/DamageContext.cs
+ common/actions/{ActorActionLease,QuickItemBinding}.cs
+ common/interfaces/{ICombatDamageable,ICombatAttackAction,IInteractionTarget}.cs
- common/interfaces/IInteractive.cs                 replace after all callers migrate
+ common/interactions/{ActorInteractionContext,InteractionOffer,InteractionReservation}.cs
+ scenes/interactions/{ActorInteractionComponent,InteractionDefinition}.cs
+ scenes/player/{PlayerQuickItemController,actions/UnarmedAttackAction}.cs
~ common/combat/DamageResolution.cs
+ scenes/actions/{ActorActionGateComponent,ActorCapabilities}.cs
+ scenes/combat/DefenseComponent.cs
+ scenes/combat/{DodgeDefinition,GuardDefinition,DefenseAnimationProfile}.cs
+ scenes/combat/definitions/*.tres                   player/body/equipment tuning
+ scenes/enemies/{EnemyDefenseController,EnemyDefensePolicy}.cs
~ scenes/components/{MovementComponent,HurtBoxComponent,InputComponent}.cs
~ scenes/attacks/{AttackController,AttackDefinition,Projectile,AttackAnimationComponent}.cs
~ scenes/player/{Player,PlayerInputController,PlayerInventoryController,ActionManager,ActionSlot}.cs
~ scenes/items/{EquipableItem,Weapon,MagicStaff,ConsumableItem}.cs
~ scenes/player/resources/{PlayerStats,StatProfile}.cs
~ scenes/player/classes/*                          starter knowledge and stat profile references
+ scenes/actions/WeaponTechniqueDefinition.cs, EquippedActionResolver.cs
+ common/actions/Element.cs
+ scenes/magic/{SpellDefinition,ElementalSkillTree,SpellUnlockNode,SpellProgressionComponent,SpellAreaEffect}.cs
+ scenes/combat/ManaComponent.cs
+ scenes/attacks/CastTargetingProfile.cs
+ scenes/magic/definitions/*                         four elements; five spell entries each after prototype
~ scenes/player/{PlayerInteractionController,InteractionArea,Inventory}.cs
~ scenes/components/InteractiveComponent.cs
~ scenes/props/{Door,Chest}.cs
~ scenes/ui/ActionContainer.cs + interaction progress UI
~ scenes/levels/LevelTransitionTrigger.cs             action gate integration
~ scenes/enemies/{EnemyBase,EnemyBehaviorComponent,EnemyBehaviorProfile}.cs
~ scenes/enemies/ai/{EnemyContext,ChasingState}.cs     defense request arbitration
~ scenes/levels/SpawnPoint.cs, scenes/levels/encounters/DestructibleNest.cs
~ scenes/player/player.tscn, scenes/enemies/enemy_base.tscn
~ selected sword-skeleton / Warden scenes and profiles
~ project.godot                                     distinct remappable input actions
~ scenes/debug/{AttackAnimationHarness.cs,attack_animation_harness.tscn}
~ scripts/saves/* and scripts/GameSession.cs         player bindings/timers and safe action capture/restore
+ tests/{ActorActionGateTest,QuickItemTest,TimedInteractionTest,CombatDefenseTest,EnemyDefenseTest,SpellProgressionTest,ManaCastingTest}.cs
~ tests/{MeleeContactTest,AttackAnimationTest,WardenTest}.cs
```

Bounded resource audit: current player melee weapons, selected shield/sword equipment, runtime default enemy melee definitions, current projectile/spell definitions and their area-impact settings. Grant no defense capability merely by inheriting enemy_base; empty capability fields leave all unselected enemies working as before.

## Delivery slices and checks

Hybrid delivery with one coherent action model; do not bolt defense onto the old attack/interact input conflict. Slices 2/3 and 4/5 can proceed after the shared contract, but each must leave existing gameplay loadable.

1. **Shared action ownership, primary input and item quick slots.** [Delivered] Added leased action arbitration (`ActorActionGateComponent`, `ActorActionLease`), prepared attack adapter (`ICombatAttackAction`, `UnarmedAttackAction`, `MartialTechniqueAction`), dedicated `PrimaryAttackSlot` and martial `SecondaryTechniqueSlot` on `ActionManager`. Migrated player input (`primary_action`, `secondary_action`, `interact`, `quick_item_1..4`), weapon equip routing, 4-slot HUD container, and timed consumable use channels with reservation and cancellation. Delivered `PlayerQuickItemController` with inventory-aware pickup auto-assignment to lowest unassigned slot, stackable merge without duplicate slots, non-stackable food instance tracking, depleted variety/instance retention, variety cooldown tracking, and `SaveGame.CurrentVersion` (bumped to 10) persistence for quick slots and cooldowns. Established damage contracts (`DamageContext`, `ICombatDamageable`, `AttackDefenseTraits`, and `DamageOutcome.Dodged/Parried/Blocked`). Verified with `ActorActionGateTest` (8/8 pass), `QuickItemTest` (10/10 pass), `ItemLifecycleIntegrationTest` (6/6 pass), and clean `ExportRelease` build.
2. **Timed ordinary interactions.** [Delivered] Migrated targets to actor-neutral `IInteractionTarget` with `ActorInteractionContext`, `InteractionOffer`, and `InteractionReservation` contracts. Implemented `ActorInteractionComponent` managing stationary channeling, target reservations, action gate acquisition (`ActorActionKind.WorldInteraction`), displacement tolerance, and cancellation. Updated `InteractiveComponent` to implement `IInteractionTarget`, manage revision-stamped reservations, and dispatch offers and commits. Wired `Door` (1.0s, `OpenDoors` permission) and `Chest` (1.0s, `OpenContainers` permission) as operation handlers. Enhanced `PlayerInteractionController` with facing-ranked focus selection, focus locking while channeling, and hold/release handling. Added interaction progress UI (`InteractionContainer` in `hud.tscn` and `Hud.cs`). Verified with `TimedInteractionTest` (8/8 pass: exact 1s/5s/10s durations, one completion per press, two actors contesting a target, movement/release/damage cancellation, humanoid vs incapable rat permissions, and Door/Chest authority execution).
3. **Mechanical lock integration.** Coordinate with the [dungeon progression plan](level-progression-prototype-plan.md) optional-lock slice: reuse its lock/gate definitions and completion authority. ActorInteractionComponent owns all picking time/progress. Author optional five/ten-second PickAndOpen fixtures and a matching-key one-second alternative. Verify tool loss, wrong key, encounter-seal refusal, deterministic reusable picking, interruption without partial progress, one unlock/open commit and persistent state without duplicate rewards. No required-route lock, charged picks, random failure or lockpicking minigame.
4. **Shared playable dodge.** Wire body definitions, displacement/immunity/recovery and distinct cooldown feedback through the same gate. Test blocked dash, solid actors/doors/walls, entering an existing swing during immunity, no same-swing retry, separate later attacks, hazards, time scaling, death and persistence. An accepted dodge cancels world interaction first; it cannot cancel committed item use or attack active/recovery. Exercise running into the Warden from all four sides with physics enabled and retain opposite-turn regressions.
5. **Guard, timed parry and shared stagger.** Add selected shield/sword/body guard profiles, inventory precedence, presentation ownership and typed damage/projectile outcomes. Test directional/height/body contacts, no knockback on chip, rearm against button spam, one parry per window, source identity cancellation in multi-target loops, primary projectile consumption and no splash retry on its defended target. Test two-handed suppression and an innate non-weapon guard, equipment changes and action leases against item use/interactions. Playground can request an enemy defense through the same component before autonomous AI exists.
6. **Fire-magic vertical slice.** Add learned spell progression, stable selection/point ledger, mana and secondary resolution, then prototype Firebolt primary and Fireball/Wall of Fire secondary with one fire-staff family. Use keyboard/gamepad-compatible targeting/previews, captured power/location, timed field pulses and current-version save/load. Verify a barbarian and mage can learn/cast under the same focus rule, unlearned/wrong-element/no-mana/invalid-target rejection with no cost, exactly-once spend, interrupted-cast costs, startup swap cancellation, active/recovery swap denial, selection/copy swap cooldown continuity, no capacity-refill exploit and no level-up point regrant. Ground spells cannot track a hidden or moved target after capture. Keep quick items and guard untouched by spell selection.
7. **Enemy intentions and content rollout.** Add restrained visible-threat defense decisions and local humanoid door intentions. Verify one delayed decision per observed attack, no hidden input reads, cancelled goals, direction commitment, invalid destinations, actor-specific permission failures, independent RNG streams, isolated shared Resources and transient reset on revisit. Roll out skeleton guard/parry first; rat scuttle, Warden shield and golem brace require actual gameplay scenes and valid presentation. Rat cannot open doors or pick locks. Do not grant every base enemy every action or build a new global navigation planner.

8. **Four-element content completion.** After the fire prototype is fun, author earth/fire/water/wind staff families and five learned spell nodes per element, with valid prerequisites and distinct battlefield roles. Complete authored delivery/buff/obstacle behaviors and effects rather than treating twenty names as twenty working skills. Test one selected compatible spell at a time, retained per-element selection across staff tiers/copies, all classes' eligibility, field lifetimes/stack limits and physical wall navigation/door/stair restrictions. No spell-cycling input, expanded active spell bar, cross-element automatic conversion or account progression is required.

Each code slice runs targeted GdUnit4 checks in the existing game project, Debug/ExportRelease builds, and Godot import/runtime checks for changed scenes/resources. Headless navmesh limitations require interactive doorway/dash/large-room checks. Editor acceptance: change a target duration from one to ten seconds, observe the same ring/channel semantics, cancel by moving or taking a hit, then finish and save/reload once; assign an innate guard to a non-weapon actor and invoke the same execution path as the player. Playtest mixed fights while opening/picking to evaluate pressure and escape responsiveness. Documentation-only delivery validates contracts and links; it does not claim these engine checks have run.

## Scope boundaries and remaining tuning

Semantic input separation, item-only quick slots, a shared action gate, one authoritative timed-interaction executor, completion-only effects and actor capability checks are settled. The keyboard-first J/K/L/Space/E and gamepad RT/RB/LT/B/A layouts are initial authored defaults to usability-test, not mouse dependencies. Input Map keys are primary_action, secondary_action, guard, dodge, interact and quick_item_1..4; migrate widgets/playground and audit old action_1..6 references without changing unrelated UI contexts. Unlock costs/prerequisites, mana/spell tuning, use/hold durations, defense balance and enemy probabilities need playtesting. Slice 1 must audit actual rig clips and default action bindings before authoring them; scene-visible fallback presentation is required. Collision-driven movement and same-tick cancellation order must be verified in engine. Enemy phasing, stamina, posture, reflection, forced ripostes, an expanded multi-spell ability bar, persistent picking progress and an omniscient door/key planner remain outside the initial implementation.
