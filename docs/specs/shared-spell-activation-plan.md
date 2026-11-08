# Shared spells and activation mechanisms

Status: proposed; part of monster-plan slices 5–6. The [monster plan](monster-actions-and-boss-phases-plan.md) owns action choices/phases. [Weapon attacks](../weapon-attack-system.md), [item effects](../stats-and-itemization.md) and [item use](../actor-actions-and-interactions.md) describe the current implementation. The [status plan](attack-status-effects-plan.md) owns periodic poison, refresh and saves.

## One effect library, existing activation owners

Players and monsters invoke the same effect definitions, targeting rules, damage, buffs and lifetimes. Items/pools and monster action lists determine availability. A spell authored only for one side still works through another valid source in tests; effect code does not branch on whether a player or monster cast it.

| Shared effect | Activation sources | Existing owner of activation |
| --- | --- | --- |
| Fireball, Frost Nova and other combat magic | Wand/staff, innate monster action, authored scroll | `SpellCaster` plus the attack clock, or the existing item-use channel |
| Healing/regeneration | Potion, scroll, focus or innate action | Source selects self/ally intent, power and payment; shared effect applies it |
| Rage/protection/other buffs | Scroll/potion, focus or innate action | Same `BuffController` and modifier runtime |
| Identify, enchant, remove curse, teleport, summon | Current scrolls and any other explicitly authored source | Shared effect with explicit item/world context |

Given equal effect, power, targets, world context and RNG, gameplay results are equal. Timing, cost, access and origin cues may differ. Potions participate at the effect layer while keeping drinking, consumption and their original authored values. Regeneration and instant healing are distinct shared effects usable by any source.

## Keep canonical definitions and source data

Keep `SpellDefinition`, `MagicStaff.BoundSpell`, `SpellPool`, existing spell paths and item save identity. Retain `ManaCostByLevel`, `Cooldown` and `Cast` as the existing **combat defaults**. They need not move into another Resource to share effects. Existing item/AI Resources already state their source-specific timing, targeting and payment.

- A wand/staff uses its existing bound definition, tier, resolved weapon hit and mana defaults.
- `EnemySpellProfile.Spell` references that same definition, with authored level/hit power and a mana policy using the spell default or an explicit amount (including zero). Paid casts use the common mana path; absence of a required mana component rejects them.
- `ConsumableItem.Spell` plus `SpellLevel` references an effect while its existing delay, duration, cooldown, cue and animation remain authoritative. Ordinary item release costs one item and zero combat mana. It does not start another attack wind-up.
- Existing `SpellDefinition.LevelCount` remains derived; utility/buff definitions still author one `ManaCostByLevel` entry per supported level (for example `[0]` for a one-level item-only effect), rather than leaving that array empty. Item release ignores those combat-default costs. Do not introduce a second level-count field or rewrite pool/save entries. Validate effect support and require `Cast` only when requesting a timed combat activation.

Source adapters resolve these values before acceptance. Preserve pool order/weights, loot RNG draws, tooltips and canonical paths. No wrapper migration or extra binding `.tres` is required. External effect/buff definitions remain shared; actor state and accepted parameter snapshots are isolated.

## Reuse scene spells and utility strategies

Existing asynchronous combat effects remain `ICombatSpell` scenes. Generalize the current stateless `ScrollEffect` strategy into `SpellEffect`, using an actor-neutral context. Rename/migrate each strategy directly while retaining its algorithm: identify still reveals through run/item state, enchant still uses `Inventory.ChangeItem`/`LootRoller`, remove curse still uses the inventory transaction, teleport/summon still use the current floor services and feedback.

Add one reusable `EffectSpell : Node, ICombatSpell` adapter for these strategies. `SpellDefinition` references exactly one of its existing `SpellScene` or a `SpellEffect`; its existing `Instantiate` factory selects the implementation. This avoids a new Node/scene class per synchronous utility effect while keeping every source on one cast/release interface. Remove the old scroll-only strategy entry points after callers migrate; keep no duplicate algorithms or aliases.

Use `BuffSpellEffect` to apply a shared `Buff` definition through `BuffController`. Healing-over-time, poison, Rage and protection reuse `HealingBuff`, `PoisonBuff` and `StatModifierBuff`. `HealSpellEffect` supplies immediate flat/fractional healing. These are stateless effect strategies hosted by the same adapter; there is no separate `BuffSpell`/`HealSpell` actor component or empowerment multiplier path.

Preserve current content while extracting shared external definitions: the small healing potion grants eight health/second for three seconds and cures blindness; Scroll of Rage grants +25% min/max damage for 30 seconds. Mages may activate those exact effects. A different blessing/dose is another authored shared definition/level, not source-specific effect logic. Food/equipment can continue applying the same buff definitions through their own activation/lifetime rules.

## One context and existing preparation/lease boundaries

Extend existing `SpellCaster`, `SpellRelease` and `PreparedAttack`. Do not add another cast Node, prepared-cast protocol, target-request hierarchy or family of result enums.

`SpellCastContext` is the one immutable effect input: actor, `SpellPower`, actor-target intent/optional explicit actor, optional selected `Inventory`/`InventoryItemSlot`, and level/floor context when required. Pass RNG through the existing cast call. Include only capabilities used by current effects; no service dictionary, autoload lookup or source-type dispatch.

Extend `SpellRelease` to capture this context and its existing runtime spell/source identity. `PreparedAttack` continues to capture animation/clock data for combat. Player and AI adapters use shared preparation/paid-start methods on `SpellCaster`; it receives explicit attack/mana dependencies through scene exports. Preserve the existing player cast-start hook for focus-swap cancellation and combo reset.

```text
SpellCaster.TryPrepare(SpellDefinition definition, GodotObject source,
    in SpellCastContext context, out SpellRelease release) -> bool
SpellCaster.TryStart(PreparedAttack cast, float manaCost,
    ActorActionLease lease = default) -> ActorActionRequestResult
SpellCaster.TryRelease(SpellRelease release, RandomNumberGenerator random) -> bool
ICombatSpell.CanCast(in SpellCastContext context) -> bool
ICombatSpell.Cast(in SpellCastContext context, RandomNumberGenerator random) -> bool
ICombatSpell.Cancel() -> void
```

Preparation validates definition/level/context before payment and captures settings without world effects/RNG draws. Leaf validation is side-effect free. `Cast` returns false before mutation for an invalid release; true includes an intentional no-effect completion such as drinking a potion at full health. AI usefulness is a separate advisory question. Reuse `ActorActionRequestResult`, adding InsufficientMana for paid starts rather than new effect result taxonomies.

The existing attack `_releaseStarted` flag/lease and item-use generation/lease each guarantee exactly-once release at their own boundary. Do not duplicate these tokens in another preparation registry. Validate current source ownership there and reject stale callbacks. An accepted combat cast spends mana once and retains cost/cooldown on interruption or release-time fizzle. A rejected start spends nothing.

For items, keep the `ItemUse` lease valid through the guarded inventory transaction. Revalidate ownership/selected item, release the prepared effect and commit one-unit consumption/identification/notification once. Interruption keeps the item. No nested combat lease or second mana charge is needed. Preserve item targeting cancellation, variety cooldowns, four quick slots and unknown/full-health potion use. Reading/drinking origin cues belong to the source; effect/status cues belong to the shared effect, with no duplicate activation feedback.

## Target queries stay in their existing domains

Extend `AreaSpellTargets` with allied/self filtering; preserve its current hostile forward/area targeting, obstruction and deterministic tie-breaking. A selected inventory slot is passed directly to its effect and validated through the existing inventory/effect predicate. Shared execution does not require a universal target-acquisition service.

At existing AI decision cadence, compute bounded support readiness once and share compatible candidate facts with preparation. Stop after the required useful-recipient count; choose the best injured ally in one pass rather than sorting a full group. Reuse a decision-local buffer/facts, not a persistent target registry. Requery dynamic targets on release. Spatial queries remain on physics ticks.

Self/ally intent is source data, while the shared effect checks legal targets/radius/capabilities. Healing never resurrects, exceeds max health or reverses a latched boss phase. Amount, duration, stacking and cures belong to the effect/buff definition, so the same effect behaves identically from a potion and a mage. A plain heal cures nothing unless authored; preserve existing potion cures. Damage bonuses scale healing only if an effect explicitly declares that relationship.

## Shared modifiers: a real capability gap

`StatModifierBuff` currently calls `PlayerStats`. Add `IStatModifierTarget` for supported-stat checks and activation-owned registration/removal; `IBuffTarget.Modifiers` exposes it. `PlayerStats` can initially delegate through its current methods. Extract its arithmetic into one plain `StatModifierLedger` when introducing the monster adapter, preserving `(base + sum flats) * (1 + sum percents)` and hand-local weapon scope.

A small optional `ActorStatsComponent` on monsters uses that same ledger and explicit component bindings. Initialize its runtime bases after `EnemyBase._EnterTree` applies authored defaults and before child readiness; never capture a temporarily modified value as a new base. Resolve against existing `MonsterDefinition`, selected action `AttackStats` and health/movement/hurtbox/mana base values; do not add another monster stat-definition sheet or progression/inventory data. Reject an entire buff before mutation if a required stat is unsupported. Player derivation/equipment behavior stays in `PlayerStats`/`PlayerStatsController`.

Resolve an attack/spell hit once through the appropriate actor adapter at acceptance. Do not multiply again in `AttackController`. Both actors register/remove the same `StatModifierBuff` data under the activation, using the same math. Existing stack/refresh, cures, condition consumers, world/screen presentation and expiry remain common. AI can avoid redundant support choices; it cannot change a buff's stacking policy by caster type.

## Bounded ownership and persistence

Keep the equipped focus's existing source-bound instance and cancellation. Extend `SpellCaster` with explicitly assigned innate definitions and a bounded actor-local cache by **used definition**, not by item stack or every cast. Detached consumable/innate effects can reuse these instances; their own existing clocks stop while idle. Clear them on actor exit/death as appropriate. There is no per-cast lifetime registry or required completion-interface framework.

Consuming an item does not unbind/cancel the detached effect it just released. Existing `AreaCastLifecycle`/`SpellEffectGroup` continue owning pending contacts/transient visuals. Focus removal still cancels its source-bound pending effects/wards. Finite recipient buffs own their duration after release and survive source consumption/death; they save by canonical effect identity, not enemy/consumed-item Node. Maintain current ward restoration and validate release-time focus/source ownership.

Retaining spell paths avoids a save rewrite for source wrappers. Actual new poison refresh fields or changed serialized item payload data still require `SaveGame.CurrentVersion` bumps when they land, with no migrations. Preserve all supported player buff/mana/cooldown/ward state.

## Slices and acceptance

1. **Existing caster and offensive sources.** Extend one context/release contract, all current scene implementations/callers and `SpellCaster`; add innate sources while retaining BoundSpell/pool/save identity. Test the same offensive asset from player and monster, mana refusal/interruption and focus/ward behavior.
2. **Healing and one-use proof.** Add the shared strategy host, `BuffSpellEffect`/`HealSpellEffect`, and source references on consumables. Use the current healing-potion effect from a potion and mage, then exercise shared instant healing and an offensive scroll fixture. No double wind-up, mana or consumption; no loot rollout is implied by a fixture.
3. **Modifiers and utility strategy adaptation.** Extract the shared ledger and monster adapter; route the current Rage effect through both actors. Adapt the existing utility strategies rather than rebuild them, then migrate intended scroll/potion dispatch completely. This independently verifiable slice follows the casting proof; it introduces no temporary duplicated utility implementation.
4. **Compare mechanisms.** Equal effect asset/level/power/targets/RNG must yield equal health, damage, modifiers and status results through focus, monster and item sources. Separately assert source costs/timing, item targeting/cancellation, unknown/full-health consumption, no duplicate cue/consumption, detached-cache bounds, source death/swap, cures/stacking/expiry and save/stair restoration. Test content omitted from player pools or AI lists without runtime actor flags.

Affected paths include existing caster/release/interface/spell callers, utility strategies, consumable and inventory controllers, relevant Resources and tooltips; the new context/strategy host/leaf strategies; and the shared modifier interface/ledger/monster adapter. Keep current-system docs accurate as slices land. Do not rename `BoundSpell`, replace pools or migrate saves just to reshape authoring.

Use repository C# build/targeted GdUnit4/ExportRelease checks, Godot import and scene/resource validation, plus windowed combat/item harness acceptance. This review changes documentation only.
