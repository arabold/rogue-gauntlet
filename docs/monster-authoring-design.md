# Monster design and authoring

The active roster is Skeleton Minion, Skeleton Warrior, Skeleton Crossbowman, Skeleton Mage, Plague Rat and Forsaken Warden. See the [gameplay monster catalog](monster-catalog/README.md) for definitions, the [Asset Studio character catalog](../asset-studio/catalogs/characters/README.md) for published art and previews, and the [level system](level-design.md#first-realm-encounter-prototype) for encounter integration. The priority remains enjoyable depths 1–3; later realm order and additional monsters are provisional.

## Authoring ownership

| Surface | Owns |
| --- | --- |
| Asset Studio | Visuals, rig, clips, attachments and provenance |
| Godot monster scene | Editable component composition, model, shapes, animations and explicit dependencies |
| Monster definition and policy Resources | Starting stats, XP, loot, perception and combat decisions |
| Realm encounter Resources | Packs (member scenes), depth eligibility, weights, nest and boss assignments; budgets come from the level's threat profile |

Balance changes belong in Godot, not Studio JSON or documentation stat tables. A model's availability does not require adding it to encounters. Shared Resources are definitions; health, timers, targets and execution state belong to each actor.

## Create a monster in Godot

Duplicate the independent [melee or projectile assembly](../scenes/enemies/templates/README.md) into the monster's own folder. Keep reusable component scenes instanced; add/delete optional capabilities in the Scene tree. Assign model, shapes, animation bindings, definition and all exported dependencies explicitly. Use Make Unique or Save As for Resources that should vary locally.

Empty optional references mean unavailable, even when a matching child exists elsewhere. A shield mesh alone does not grant guard. Templates are starting points, not generators that overwrite customized actors; copied assemblies do not automatically acquire future template nodes.

Use **Project → Tools → Validate monster scene**, then exercise the actor in the [combat harness](combat-harness.md) and a generated floor. Save/reopen it and spawn two instances to check independent state. Arbitrary live Resource edits are not supported; reinstantiate to apply policy changes.

## Independent capabilities and policies

Components execute capabilities; strategies and actions request them; the [shared action gate](combat-system.md#action-ownership) decides whether they may start. Guard/dodge can accompany any action mix. The [monster action architecture plan](specs/monster-action-architecture-plan.md) lists what remains.

| Policy | Supported behavior |
| --- | --- |
| Movement | Close distance, engagement ring (wait for an attack slot around the player), maintain distance, attack then retreat |
| Actions | Any mix of `MonsterAttackAction`, `MonsterSpellAction` and `MonsterDashAction`, each with priority, weight, cooldown, range, target selector, conditions and weight modifiers |
| Defense | One categorical guard/dodge/no-response choice per observed threat |
| Low-health override | Optional retreat with duration bounds and health hysteresis |

### Actions

An `EnemyCombatProfile` lists its actions in order. Each action is a Resource that knows when it may start and how: a new kind of action is a new `MonsterAction` subclass, never an edit to the behavior host. Monster-only actions live as sub-resources inside the monster's `*_combat.tres`; shared strategies, spells, buffs and contact profiles stay separate files.

- **Selection.** Among actions that are off cooldown, have a free attack slot, are in position and meet their conditions, only the highest `Priority` competes; `Weight` × weight modifiers × the profile's repeat penalty choose within it, from the actor's seeded `combat.action` stream. Cooldown and history start only when the action actually starts.
- **Position.** `MinimumRange`/`MaximumRange` measure to the action's selected target (capped by the executor's own reach, such as melee contact or projectile range); `MaximumStartAngle` lets a heavy actor be flanked.
- **Targets.** `Target` empty means the current hostile (in sight). `SelfTargetSelector` and `AllyTargetSelector` (most injured visible ally first) make support actions; support needs no hostile target and is never blocked by tactics.
- **Conditions and weight modifiers.** `HealthBelowCondition` (actor or target), `TargetPressureCondition` (the hostile stayed close for a while) and `HealthPhaseCondition` gate an action or scale its weight. Conditions are stateless; their memory lives on the actor.
- **Primary action.** The first offensive action sets the engagement range and slot for movement strategies; other actions still fire whenever eligible, for example a rat's rush on its way in.
- **Tactics.** Offensive actions start only while the tactical decision allows attacking: retreating, waiting after an attack, fleeing at low health and encircling never attack.

`MonsterAttackAction` and `MonsterSpellAction` run on the actor's `AttackController` clock, with `Style`, `Duration` and damage on the action. Spells reuse the shared `SpellDefinition` and can pay the monster's mana; a spell action's optional `Cast` replaces the spell's own cast definition, so a monster can author its timing and hyper armor apart from how players cast it. Any attack or cast can be made uninterruptible (`AttackDefinition.Interruptible` off) for the committed moves of bosses and elites; see [poise](combat-system.md). At release the target must still be alive, in range, visible and worth the cast. `MonsterDashAction` drives the actor's `DashComponent`: wind-up, collision-resolved travel toward or away from the target and recovery through the shared action gate, after a clear-corridor check. A dash deals no damage.

### Health phases

An optional `HealthPhaseComponent` latches phases from descending health thresholds; healing never lowers it and it never interrupts a running action. Actions read it through `HealthPhaseCondition`, and the combat profile's `TurnRateByPhase` scales the monster's turning per phase. The Forsaken Warden uses 65% and 30%: the slash weighs less in phase 3, the overhead swing more in phases 2 and 3, and the Tremor stomp unlocks in phase 2. From then on it slams whenever its 8 s cooldown allows and the player stands inside its 7 m wave, so keeping out of sword reach or behind it does not stall the fight. The slam lands 0.6 s after it starts and is uninterruptible throughout; dodging cannot pass through its wave, so the player must leave its reach in time. Its sword swings are uninterruptible from their first frame through their contact, so a swing it has started goes through: a parry stops the hit but not the swing, and the opening comes in the recovery after it. It guards and counters only between attacks. Its turning speeds up per phase (`TurnRateByPhase` 1, 1.6, 2.2 on its 90°/s cap), so flanking pays less as it weakens. Its Tremor is the shared spell and shakes the camera through its `CameraShakeProfile`.

### Roster examples

The Plague Rat bites (35% `rat_venom.tres` on a surviving damaging hit, refreshing one activation) and rushes from 2–4.5 m. The Skeleton Mage keeps distance with a mana-free volley of three straight fire missiles (one on the aim line, one 10° to each side) as its primary, casts fireball, regenerates the most injured ally below 75%, heals itself below 35% and casts Rage on an ally; the shared spells reject casts that would change nothing. Venom and poison share one periodic buff; see [status conditions and poison](stats-and-itemization.md#buffs-and-status-conditions) and [contact resolution](combat-system.md#contact-resolution).

### Validation and tuning

Monster Resources are `[Tool]` and validate themselves, so **Project → Tools → Validate monster scene** and spawning report the same problems, labelled by Resource path (for example `Behavior.Combat.Actions[1] 'rush': …`). Scene-wiring problems name the node. In the [combat harness](combat-harness.md), the monster panel lists each action's priority, effective weight and whether it is ready, cooling down or out of position, plus the last choice; **Spawn / respawn** picks up Resource edits made through the Remote inspector.

The tactical policy bounds each retreat opportunity, delays retries and can require an intervening accepted shot attempt before retreating again. Point-blank pressure favors fighting; blocked escape yields to an eligible shot or approach. No rule guarantees safety or bypasses committed attacks, defenses, doorway crossings or collision. Low-health retreat cannot rearm repeatedly while the actor remains injured.

Guard/dodge are mutually exclusive probability outcomes. A raised guard slows a monster to its guard definition's `MovementSpeedMultiplier` (half speed for the skeleton warrior and the Warden) but never stops its pursuit, so blocking a ranged hero's volleys does not hold it in place. A monster never starts one during its own uninterruptible action, whose window the action gate keeps closed to every other action; ordinary hyper-armor lets a wind-up yield to guard or dodge as usual. Reaction delay and defense startup must fit incoming wind-ups. Guard pressure, maximum hold, cooldown and release recovery create openings; riposte requests a riposte-eligible attack action only after that sequence, with range/sight checked again. The Warden guards with a brief parry window and conditionally ripostes; the warrior also supports dodge. Both use ordinary combat rules. Large actors author movement clearance separately from their larger spawn staging clearance; the Warden stays in his home room while pursuing.

Assign `AttackController.ActionGate` to the actor's gate; validation reports a missing one, and without it attacks lose phases and hyper-armor. Assign the shared `AttackIndicatorComponent` to monsters that should telegraph weapon attacks; it presents the existing melee footprint or projectile lanes and has an editor preview. Leave the gate's `RecoveryCancels` off: a monster's attack recovery is the player's punish window. `enemy_base.tscn`, the crossbowman and the templates wire a `DamageNumbers` node (hurtbox, `damage_number.tscn`, the health bar as anchor, the invisibility component); keep it in new monster scenes. They also wire a `Nameplate` (`nameplate.tscn`, bound to the health and invisibility components) that carries the health bar and shows the definition's `DisplayName` in red while the player is within its `ShowDistance` (8 m) and the actor's tile is discovered. Its one `ConstantScreenSize3D` keeps the bar, the name and the gap between them the same size on screen at any zoom, and the bar's `ShowWithName` shows it with the name, even at full health. Place the plate where the bar should sit; multiline names grow upward from the label. The Warden scales its plate up. Large monsters set `MovementComponent.MaxTurnRate` and an action `MaximumStartAngle` so the player can [get behind them](combat-system.md#enemy-decisions-and-persistence). Monsters take turns through the player's [attack slots](combat-system.md#attack-slots); a boss's actions set `UsesAttackSlot = false`. An engagement ring must lie outside the primary action's range. Hurtboxes share `humanoid_vulnerability.tres` for [rear and sneak hits](combat-system.md#facing-and-sneak-attacks) through `enemy_base.tscn`, the crossbowman and the templates. Heavy or Unblockable attacks retain their tier-length wind-up; place melee contact windows on the visible strike with the clip sampler described in [weapon attacks](weapon-attack-system.md#attack-styles-rig-profiles-and-timing).

Hit reactions come from an optional `PoiseComponent` assigned to the hurtbox and bound to the actor's gate. Leave it empty for actors that every hit should interrupt (the rat). Validation reports a foreign or unbound component, an invalid definition, and a floating bar showing another actor's meter. Melee attacks author a facing lock lead within the wind-up and a wind-up turn rate, so slow turners such as the Warden can be sidestepped. See [poise](combat-system.md#poise-and-hit-reactions).

Group shelter, cover search, weapon switching and cinematic boss transitions remain deferred.

## Awareness and pursuit

Acquisition requires navigation reachability and unobstructed sight. Long-range detection is directional; close detection ignores facing but still requires sight, preventing hearing through walls or closed doors. Sight rays use the authored eye height.

An acquired target remains pursued while reachable, even around corners without sight. Breaking the path starts a search at the last reachable position; losing sight alone does not end a valid chase. Doorway crossings retain their commitment. Defense observes visible threats, never input presses or hidden future state.

The FSM owns awareness/pursuit, movement strategies express preferences and a per-actor tactical runtime bounds local choices. The clearance-grid navigation described in [level design](level-design.md#doors-and-enemy-navigation) supplies chase, flee and route movement sized by each monster's `BodyShape`; this is not a behavior-tree framework or a global planning service. Tactical decision traces are available through the [harness](combat-harness.md).

## Strength, placement and rewards

Strength is fixed authored data; depth does not silently scale health or damage. `MonsterDefinition` authors base health, speed, armor and mana; `ActorStatsComponent` is the only writer of those values on the actor, re-resolving them when buffs such as Rage add or remove modifiers. Stronger variants can reuse art but get separate definitions. A definition's `Archetype` and `BalanceDepth` let the [balance model](specs/balance-model.md#game-content) derive those stats and the monster's threat level; the threat level sets its XP and what it costs a floor's encounter budget. Encounter entries are packs: they list their member scenes and set inclusive depth bounds and frequency.

Invalid required gatekeeper configuration leaves descent locked with diagnostics. Cleared entities, finite nest slots and boss defeat use existing floor deltas; surviving actors reset on reconstruction. [Randomness and saves](randomness-and-seeds.md) owns those limits.

The [roadmap](development-roadmap.md#first-three-floor-acceptance) tracks generated-room movement, authoring acceptance and balance. In particular, review pressured crossbow shooting, Warden guard/recovery openings, skeleton animation transitions and the warrior's reward profile before expanding the roster.
