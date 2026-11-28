# Monster design and authoring

A monster is a Godot scene of single-capability components plus Resources: a definition, a behavior and combat profile, and the actions it chooses from. The [monster catalog](monster-catalog/README.md) lists every monster the dungeon spawns, realm by realm; the [Asset Studio character catalog](../asset-studio/catalogs/characters/README.md) holds published art and previews; [level design](level-design.md#encounters) places packs on floors; the [realm monster roster](specs/realm-monster-roster-plan.md) proposes the casts still missing.

## Authoring ownership

| Surface | Owns |
| --- | --- |
| [Asset Studio](../asset-studio/character-workflow.md) | Visuals, rig, clips, baked gear and provenance |
| Godot monster scene | Editable component composition, model, shapes, animations and explicit dependencies |
| Monster definition and policy Resources | Starting stats, XP, loot, perception and combat decisions |
| Realm encounter Resources | Packs (member scenes), depth eligibility, weights, nest and boss assignments; budgets come from the level's threat profile |

Balance changes belong in Godot Resources and the [balance model](balance-model.md), not Studio JSON or documentation stat tables. A model's availability does not require adding it to encounters. Shared Resources are definitions; health, timers, targets and execution state belong to each actor.

## Create a monster in Godot

Duplicate the independent [melee or projectile assembly](../scenes/enemies/templates/README.md) into the monster's own folder (`scenes/enemies/<family>/`). Keep reusable component scenes instanced; add or delete optional capabilities in the Scene tree. Assign model, shapes, animation bindings, definition and all exported dependencies explicitly. Use Make Unique or Save As for Resources that should vary locally. A variant with the same body and new stats can inherit the base scene and swap its definition, as the sewer rat does the plague rat's.

Empty optional references mean unavailable, even when a matching child exists elsewhere. A shield mesh alone does not grant guard. Templates are starting points, not generators that overwrite customized actors; copied assemblies do not automatically acquire future template nodes.

Choose the definition's `Archetype` and `BalanceDepth` and let `simulate.py derive` write its stats (see [strength, placement and rewards](#strength-placement-and-rewards)). Use **Project → Tools → Validate monster scene**, then exercise the actor in the [combat harness](combat-harness.md) and on a generated floor. Save, reopen and spawn two instances to check independent state. Arbitrary live Resource edits are not supported; reinstantiate to apply policy changes.

## Independent capabilities and policies

Components execute capabilities; strategies and actions request them; the [shared action gate](combat-system.md#action-ownership) decides whether they may start. Guard and dodge can accompany any action mix. Shared AI, death, UI and navigation code never names a monster: a new behavior is a new strategy, action, condition or component authored on the monsters that use it.

| Policy | Supported behavior |
| --- | --- |
| Movement | Close distance, engagement ring (wait for an attack slot around the player), maintain distance, attack then retreat (`bite_and_dart`); an optional flank on any of them (`flank_rear`, `surround`); wrappers that change another pattern: erratic flight and retreat-while |
| Actions | Any mix of `MonsterAttackAction`, `MonsterSpellAction`, `MonsterDashAction` and `MonsterBlinkAction`, each with priority, weight, cooldown, range, target selector, conditions and weight modifiers |
| Defense | One categorical guard/dodge/no-response choice per observed threat |
| Low-health override | Optional retreat with duration bounds and health hysteresis |

### Actions

An `EnemyCombatProfile` lists its actions in order. Each action is a Resource that knows when it may start and how: a new kind of action is a new `MonsterAction` subclass, never an edit to the behavior host. Monster-only actions live as sub-resources inside the monster's `*_combat.tres`; shared strategies, spells, buffs and contact profiles stay separate files.

- **Selection.** Among actions that are off cooldown, have a free attack slot, are in position and meet their conditions, only the highest `Priority` competes; `Weight` × weight modifiers × the profile's repeat penalty choose within it, from the actor's seeded `combat.action` stream. Cooldown and history start only when the action actually starts.
- **Position.** `MinimumRange`/`MaximumRange` measure to the action's selected target (capped by the executor's own reach, such as melee contact or projectile range); `MaximumStartAngle` lets a heavy actor be flanked.
- **Targets.** `Target` empty means the current hostile (in sight). `SelfTargetSelector` and `AllyTargetSelector` (most injured visible ally first) make support actions; support needs no hostile target and is never blocked by tactics.
- **Conditions and weight modifiers.** `HealthBelowCondition` (actor or target), `TargetPressureCondition` (the hostile stayed close for a while), `HealthPhaseCondition` and `CarryingLootCondition` gate an action or scale its weight. Conditions are stateless; their memory lives on the actor.
- **Primary action.** The first offensive action sets the engagement range and slot for movement strategies; other actions still fire whenever eligible, such as a rat's rush on its way in or a shooter's shot while it closes in.
- **Tactics.** Offensive actions start only while the tactical decision allows attacking: retreating, waiting after an attack, fleeing at low health and encircling never attack.

`MonsterAttackAction` and `MonsterSpellAction` run on the actor's `AttackController` clock, with `Style`, `Duration` and damage on the action. Spells reuse the shared `SpellDefinition` and can pay the monster's mana; a spell action's optional `Cast` replaces the spell's own cast definition, so a monster authors its timing and hyper armor apart from how players cast it. Shared spells reject casts that would change nothing, and at release the target must still be alive, in range, visible and worth the cast. Any attack or cast can be made uninterruptible (`AttackDefinition.Interruptible` off) for the committed moves of bosses and elites; see [poise](combat-system.md#poise-and-hit-reactions). `MonsterDashAction` drives the actor's `DashComponent`: wind-up, collision-resolved travel toward or away from the target and recovery through the shared action gate, after a clear-corridor check; a dash deals no damage. `MonsterBlinkAction` teleports (see [theft and blink](#signature-capabilities)).

### Health phases

An optional `HealthPhaseComponent` latches phases from descending health thresholds; healing never lowers it and it never interrupts a running action. Actions read it through `HealthPhaseCondition`, and the combat profile's `TurnRateByPhase` scales the monster's turning per phase. Gatekeepers use phases to change the fight as they weaken: the Forsaken Warden unlocks its Tremor stomp and turns faster, so flanking pays less; the Minotaur charges faster and more often below half health, and the Gelatinous Cube lobs more often. A signature area attack such as the Warden's Tremor should keep it from being stalled by distance or position, and its uninterruptible swings move the hero's opening to the recovery after them.

### Signature capabilities

Each capability is a reusable component, action, strategy or attack field, authored on the monsters that use it.

| Capability | Building block | Used by |
| --- | --- | --- |
| Charge | Melee `AttackDefinition.TravelDistance`, `WallImpactStagger` | Minotaur |
| Lob and ground patches | `AttackDefinition.ProjectileArcHeight`, `Projectile.LandingMarker`, `GooPatch`, `SlimeTrailComponent` | Spitting slime, Gelatinous Cube |
| Ambush | `AmbushComponent` with a `Disguise` scene or a `DormantClip` | Chest mimic, snake |
| Contact retaliation | `ContactRetaliationComponent` on the hurtbox | Acid blob |
| Theft and escape | `TheftComponent`, `RetreatWhileStrategy`, `CarryingLootCondition` | Nymph |
| Blink | `MonsterBlinkAction` | Nymph |
| Flight | `NavigationComponent.Layer` `Air`, `ErraticFlightStrategy` | Giant bat |
| Revive | `ReviveComponent` (an `IDeathGuard`) | Stone golem |
| Leaving and death effects | `DeathComponent.Leave`, `DeathEffect` | Nymph, chest mimic |

- **Charge.** A melee attack with a `TravelDistance` runs the attacker that far along its locked facing through the contact window, on the execution's own clock (it holds still through hit-stop), so its ordinary contact travels with it. Its warning shows the contact swept along the run, cut short at the first wall, and the action starts only along a clear line to its target. A run that meets a wall or solid prop head-on ends there and staggers the attacker (`WallImpactStagger`, optional `WallImpactShake`): the hero's opening. Bodies never stop it. `MonsterDashAction` stays the damage-free hop.
- **Lob.** An attack's `ProjectileArcHeight` turns a single unhoming shot into a lob over cover and allies to where the target stood at release, marked by the projectile's `LandingMarker` for the whole flight: stepping aside escapes it, ducking behind a crate does not. Its landing is the shot's one contact, and its impact effect can be a `GooPatch` that holds a buff on the hero only while they stand in it (the slime's poison pool, the cube's slow); monsters wade through. A `SlimeTrailComponent` drops such patches behind a moving actor. Saves leave out every hold whose source is not part of the hero. The cube's goo also binds the hero in place through the player's goo-bound status (see [status conditions](stats-and-itemization.md#buffs-and-status-conditions)).
- **Ambush.** While dormant an `AmbushComponent` keeps its actor concealed (`EnemyBase.IsConcealed`) and its AI off. Aim-assist and seeking spells pass it over (`AttackController.IsTargetable`), and it is hidden from the hero (`EnemyBase.IsHiddenFromHero`), so its nameplate and monster x-ray stay off. It stays an enemy, so blasts and blows still hit it, which wakes it. It shows a `Disguise` scene in place of its `Model` (the mimic shows a real chest, with the chest's gold x-ray) or rests in its `DormantClip` (the snake's coil). Opening the disguise, staying within `WakeRadius` in its sight for `WakeDelay` (0 wakes it at once), or hitting it wakes it: it plays its `WakeClip`, then hunts.
- **Contact retaliation.** A `ContactRetaliationComponent` on a hurtbox applies its `Effect` to whoever lands a direct melee hit; ranged, spell and area hits are safe.
- **Theft and escape.** A thief's `TheftComponent` takes one random item stack the hero is not wearing (never quest items) on its first damaging melee hit, and the message feed reports it. Its movement is a `RetreatWhileStrategy` on a `CarryingLootCondition`: while the condition holds the thief backs away to `Distance` and waits there without attacking. The retreat preempts (`EnemyMovementStrategy.Preempt`), so it outranks the low-health retreat and flank; wrappers pass their pattern's preemption through. Once the thief has stayed out of the hero's sight, or farther than `EscapeDistance` (keep the retreat's `Distance` at least that far), for `EscapeTime`, it leaves with the loot for good. Killing it first drops the loot at its feet as an ordinary saved pickup.
- **Blink.** A `MonsterBlinkAction` fades its actor out in its `Departure` effect while holding the action gate, then lands at a spot it could have walked to, `MinimumDistance` to `MaximumDistance` from the target on its own side (within `Spread` degrees), and plays its `Arrival`. A stagger during the fade cancels it. It deals no damage, so it may start while the actor retreats.
- **Revive.** A `ReviveComponent` registers as its `HealthComponent`'s death guard. A blow that would kill instead leaves a sliver of health and lands like a kill (no hit reaction, push or on-hit statuses); the monster cancels its action, takes an action-gate status lock, turns its hurtbox invulnerable and plays its `CollapseClip`, so the revive cannot be skipped. After `DownedDuration` it plays the clip backwards, heals to `ReviveHealthShare` and fights on, `Revives` times; the next lethal blow is the ordinary death, which alone pays experience and loot and records it cleared.
- **Leaving and death effects.** A `DeathComponent` can play a one-shot `DeathEffect` where the actor dies; `Delay` and `FadeDuration` set how long the corpse lies. `Leave` takes a monster out without a death: no experience or loot, and it stops acting, moving and taking hits at once. Dying or leaving records the spawn's `persistent_id` as cleared, so a reloaded floor does not bring it back.

### Bodies, movement and flight

- **Turning.** Large monsters set `MovementComponent.MaxTurnRate` and an action `MaximumStartAngle` so the player can [get behind them](combat-system.md#enemy-decisions-and-persistence). Melee attacks author a facing lock lead within the wind-up and a wind-up turn rate, so slow turners can be sidestepped.
- **Facing travel.** Every movement goal except an approach keeps a monster facing its target. Bodies that cannot move backward or sideways (a snake, a hopping chest) set the behavior's `FacesTravel`, so they turn toward their travel while they retreat or circle; attacks and dashes still aim through their own facing.
- **Bodies.** A walker's `BodyShape` starts at the actor's origin, where its model's feet stand; validation reports a body that starts higher. Large actors author movement clearance apart from their larger spawn staging clearance. `StayInHomeRoom` keeps a monster in its spawn room while it pursues (gatekeepers, the stone golem, the mimic).
- **Flight.** A flyer sets its `EnemyBehaviorComponent/Navigation` node's `Layer` to `Air`, its `MovementComponent.Gravity` to 0, its body shape above `ClearanceGrid.AirClearanceHeight` and a collision mask without walking terrain (layer 10), so it passes physically where the [air grid](level-design.md#doors-and-enemy-navigation) says it can; validation reports a flyer missing any of these. Its hurtbox still reaches down into the hero's melee arcs and projectile height. An `ErraticFlightStrategy` wraps another strategy and replaces its holds and close approaches with darts around the target, so the flyer never holds still.
- **Turn-taking.** Monsters take turns through the player's [attack slots](combat-system.md#attack-slots); a boss's actions set `UsesAttackSlot = false`. An engagement ring must lie outside the primary action's range.

### Defense

Guard and dodge are mutually exclusive probability outcomes. A raised guard slows a monster to its guard definition's `MovementSpeedMultiplier` but never stops its pursuit, so blocking a ranged hero's volleys does not hold it in place. A monster never starts a defense during its own uninterruptible action, whose window the action gate keeps closed to every other action; ordinary hyper armor lets a wind-up yield to guard or dodge as usual. Reaction delay and defense startup must fit incoming wind-ups. Guard pressure, maximum hold, cooldown and release recovery create openings, but a blocked or parried hit may be answered at once (`RiposteChance` per defended hit): the guard drops and a riposte-eligible attack starts regardless of its cooldown, armored from its first frame, with range and sight checked first.

The tactical policy bounds each retreat opportunity, delays retries and can require an intervening accepted shot attempt before retreating again. Point-blank pressure favors fighting; blocked escape yields to an eligible shot or approach. No rule guarantees safety or bypasses committed attacks, defenses, doorway crossings or collision. Low-health retreat cannot rearm repeatedly while the actor remains injured.

### Scene wiring

- Assign `AttackController.ActionGate` to the actor's gate; without it attacks lose phases and hyper armor. Leave the gate's `RecoveryCancels` off: a monster's attack recovery is the player's punish window.
- Assign the shared `AttackIndicatorComponent` to monsters that telegraph weapon attacks; it presents the melee footprint or projectile lanes and has an editor preview. Heavy or Unblockable attacks keep their tier-length wind-up; place melee contact windows on the visible strike with the clip sampler in [weapon attacks](weapon-attack-system.md#attack-styles-rig-profiles-and-timing).
- Hit reactions come from an optional `PoiseComponent` assigned to the hurtbox and bound to the actor's gate; leave it empty for actors that follow hit recovery (the rats). Each monster scene points at its own poise definition, whose meter, break growth and memory `derive` writes from the archetype, along with every attack's `Commit`. See [poise](combat-system.md#poise-and-hit-reactions).
- Hurtboxes share `humanoid_vulnerability.tres` for [rear and sneak hits](combat-system.md#facing-and-sneak-attacks).
- Keep the `DamageNumbers` node and the `Nameplate` (`nameplate.tscn`, bound to the health component) that `enemy_base.tscn` and the templates wire. The plate carries the health bar and shows the definition's `DisplayName` while the player is within its `ShowDistance` and the actor's tile is discovered, at a constant screen size; place it where the bar should sit. The actor assigns itself as the plate's and its monster x-ray's `Subject` (`IHideable`), so both hide while the actor is hidden from the hero.

### Validation and tuning

Monster Resources are `[Tool]` and validate themselves, so **Project → Tools → Validate monster scene** and spawning report the same problems, labelled by Resource path (for example `Behavior.Combat.Actions[1] 'rush': …`). Scene-wiring problems name the node. In the [combat harness](combat-harness.md), the monster panel lists each action's priority, effective weight and whether it is ready, cooling down or out of position, plus the last choice. A combat profile's action list is read when the monster spawns; **Spawn / respawn** picks up Resource edits made through the Remote inspector.

### Deferred

- Group shelter, cover search, weapon switching and cinematic boss transitions.
- **Phase content:** phase entry actions (a roar, brief immunity) and per-phase speed or defense overrides wait for a boss that needs them; phases gate and weight actions and scale turning today.
- **Spell cast validation:** a spell action's cast definition does not get the wind-up and threat-tier checks attack actions get.
- **Dodge on the dash component:** `DodgeComponent` keeps its own displacement (immunity, charges, steering, pass-through) apart from `DashComponent`.
- **Editor badges:** scene-tree configuration warnings would need `EnemyBase` to become `[Tool]`, which would run gameplay setters against editor placeholders, so the validate menu stays the editor surface.

## Awareness and pursuit

Acquisition requires navigation reachability and unobstructed sight. Long-range detection is directional; close detection ignores facing but still requires sight, preventing hearing through walls or closed doors. Sight rays use the authored eye height.

An acquired target remains pursued while reachable, even around corners without sight. Breaking the path starts a search at the last reachable position; losing sight alone does not end a valid chase. Doorway crossings retain their commitment. Defense observes visible threats, never input presses or hidden future state.

The FSM owns awareness and pursuit, movement strategies express preferences and a per-actor tactical runtime bounds local choices. The clearance-grid navigation described in [level design](level-design.md#doors-and-enemy-navigation) supplies chase, flee and route movement sized by each monster's `BodyShape`; this is not a behavior-tree framework or a global planning service. Tactical decision traces are available through the [harness](combat-harness.md).

## Strength, placement and rewards

Strength is fixed authored data; depth does not silently scale health or damage. `MonsterDefinition` authors base health, speed, armor and mana; `ActorStatsComponent` is the only writer of those values on the actor, re-resolving them when buffs such as Rage or Haste add or remove modifiers. Stronger variants can reuse art but get separate definitions. A definition's `Archetype` and `BalanceDepth` let the [balance model](balance-model.md#game-content) derive those stats and the monster's threat level, and its `SpeedScale` sets its share of the archetype's pace (a slow blob, a snake slower than a rat); the threat level sets its XP and what it costs a floor's encounter budget. Encounter entries are packs: they list their member scenes and set inclusive depth bounds and frequency. A definition's `MaximumPerFloor` keeps a special monster from filling a floor: a floor's packs never hold more of it, and a pack that alone holds more fails validation. Loot comes from the definition's `LootDropProfile`; gatekeepers drop on the gatekeeper creation policy, a richer quality ladder.

Invalid required gatekeeper configuration leaves descent locked with diagnostics. Cleared entities, finite nest slots and boss defeat use existing floor deltas; surviving actors reset on reconstruction, so phases, cooldowns and dash state are never saved. [Randomness and saves](randomness-and-seeds.md) owns those limits. Playtest acceptance lives in the [roadmap](development-roadmap.md).
