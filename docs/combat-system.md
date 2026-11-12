# Combat actions and active defense

Implemented rules for players and monsters. See [weapon attacks](weapon-attack-system.md) for attack authoring, [monster authoring](monster-authoring-design.md) for AI policy, and the [combat harness](combat-harness.md) for diagnostics. The broader [action design](actor-actions-and-interactions.md) distinguishes future magic and interaction work.

## Action ownership

Each actor can own one committed action: attack, dodge, guard, item use, interaction or forced reaction. Executors receive the actor's gate through explicit `ActionGate` references, including `AttackController`. Rejected requests change nothing. Dodge or guard can replace attack startup, and dodge can replace attack recovery. A gate with `RecoveryCancels` (the player's) also lets guard replace attack recovery, the next [combo step](weapon-attack-system.md#primary-combos) replace primary recovery, and alternate spell casting between primary and off-hand wands hand off as soon as a spell releases without waiting for recovery. Monsters keep their recovery committed, so it stays the window in which they can be punished. Active contact remains committed. Committed item use prevents voluntary replacement. Stagger interrupts voluntary actions; death cancels and disables them. Ordinary movement remains separate but obeys the current action's restrictions. The current action may also report hyper-armor (see [poise](#poise-and-hit-reactions)); any change of ownership clears it.

Player presses of primary, secondary, dodge and guard wait 0.2 seconds (`PlayerInputController.InputBufferWindow`) for an action that can accept them. A press made during an attack's contact therefore runs as soon as its recovery begins instead of being dropped. An accepted dodge or guard drops older presses, and a buffered guard press still needs the button held. Holding primary repeats complete attacks without cancelling recovery.

The executor owns its timing and releases ownership on completion or interruption. Movement and animation follow that same commitment; stale callbacks cannot complete an old action or cancel a newer one. Shared authored Resources are definitions. Accepted actions snapshot their settings so live edits cannot change an action halfway through.

`AttackExecution` owns the attack phase and elapsed clock (`WindUp`, `Active`, `Recovery`), including the existing once-per-execution attacker hit stop. `AttackController` remains responsible for action-gate leases, contact sampling, projectile release and interruption. Effects and debug switches observe the execution; they do not drive phase transitions.

Dashes (a monster rush or hop-back) use a `DashComponent` beside `MovementComponent`, like `DodgeComponent`. It acquires the shared action gate, owns wind-up, travel and recovery through an `AttackExecution`, and drives the mover through a locomotion override; `MoveAndSlide` resolves travel against the world, a blocked dash stops instead of extending, navigation checks a clear corridor first, and pushes, status locks, stagger and death interrupt it. A dash grants no immunity and deals no damage.

## Contact feedback

After the hurtbox resolves a contact, one immutable result feeds target and camera feedback. `CombatFeedbackController` coordinates the existing target knockback with a short target flash, material-selected particles, damage numbers, cosmetic flinch recoil, and a physical attacker lunge on successful melee contact. The lunge moves the attacker's `CharacterBody3D` through `MovementComponent` for the profile's bounded distance and duration; `MoveAndSlide` resolves walls, while status locks, stagger and knockback take priority. Each attack execution can lunge once. The effect profile and impact-surface profile are assigned in the actor scene. `CombatCameraFeedbackController` scales its impulse from the resolved outcome, including a softer blocked response. Feedback never rolls damage, evaluates defense or applies another physical push. Sound remains deferred.

The existing hit stop remains on the attacking execution; other actors, defense timers, projectiles and movement continue. The existing authored push remains the only defender knockback. The attacker lunge is intentionally a gameplay movement change: it closes a small gap after a confirmed hit and may affect spacing for the next action. Target recoil remains a visual-only model offset.

## Dodge, guard and parry

Dodge combines physical displacement with an authored immunity interval, recovery, charges and a minimum interval between dodges. Collision may stop displacement without extending immunity. AI must also find a supported, clear destination before dodging.

A dodge can pass through other actors. `DodgeDefinition.PassThroughLayers` removes physics layers from the actor's collision mask while it travels. Temporary body collision exceptions also prevent those bodies' movement from pushing them out of the dodging actor; hurtboxes remain active and walls stay solid. The player's dodge passes through the enemies layer, so dodging through a monster puts the player behind it. A dodge that ends inside a pass-through body keeps moving at half speed, without immunity and for at most `PassThroughExitTime` (0.3 s), until it is clear; then the mask returns. Every exit path restores the mask and removes only the dodge's own collision exceptions: finishing, cancellation, interruption and leaving the tree. Monster dodges pass through nothing.

The player resolves a dodge heading from current processed movement input, then the most recent nonzero direction from the previous 0.08 seconds, then current facing. A neutral dodge follows facing instead of automatically backstepping; explicit backward input still dodges backward. External motion never supplies steering. An input-driven dodge starts immediately and accepts a new nonzero direction during its first 0.04 seconds. The correction window is fixed at acceptance and closes at expiry; direct requests such as monster, harness and preview dodges are locked by default. A corrected dodge keeps its captured facing for the hop and recovery, so changing the travel direction creates a sidestep. Directional animation can switch during this window while retaining its existing travel phase. The dodge does not restart or extend its movement, immunity, cooldown or action clock, and a collision-stopped dash cannot be redirected.

The player's dodge travels 6 units over 0.15 seconds, protects against eligible weapon/projectile contact throughout travel, and has 0.05 seconds of recovery. It has two charges (`MaxCharges`). Each spent charge refills after 1.2 seconds (`ChargeRechargeDuration`), one at a time, and dodges are at least 0.25 seconds apart (`CooldownDuration`). Evading a contact within 0.1 seconds of the dodge starting (`PerfectWindow`) refunds its charge once. Monster dodges keep one charge without a recharge, so only their cooldown applies. Hazards and environmental damage bypass dodge, including existing damage-over-time effects; elemental projectiles remain eligible attacks. The 0.04/0.08-second input windows and combat values are initial playtest tuning.

Guard commits to a frontal facing and progresses through raising, held protection and release recovery. Its optional opening parry window is single-use. Shields provide primary defensive guard; portable lights in the off hand provide a makeshift guard (35% mitigation). When the off hand is empty, guard falls back to the equipped one-handed weapon's guard or the actor's innate guard. Two-handed weapons and paired melee weapons suppress guard entirely, disabling the guard input on L. A shield visual alone grants no protection. Rebinding equipment cancels guard while retaining defensive cooldowns.

Guard pressure can prolong a response within its maximum hold. Normal release finishes the current reaction and recovery before an enemy may riposte. A riposte uses the actor's ordinary attack, rechecking range, sight and availability. It grants no extra action or immunity.

A parry staggers the attacker for the guard's `ParryStaggerDuration` (0.9 s for the shared weapon and shield guards), long enough to answer. Hits on the staggered attacker deal its poise definition's staggered bonus, so a riposte needs no separate system.

An unparried contact with the `BreaksGuard` defense trait breaks a raised guard: it returns `GuardBroken`, lands `GuardBreakDamageMultiplier` of its damage before armor, and staggers the defender for `GuardBreakStaggerDuration`. The parry window still beats a Heavy attack.

Presentation is explicitly assigned in the actor scene. Attacks, defenses and stagger share one pose owner and gameplay clock. Actor animation trees play locomotion, knockback and the actor's own states: item use for the player, spawn and sleep for skeletons. They have no attack states. One push plays one hit reaction, held at its last frame while the push continues. A knockback that overlaps a stagger shows only the stagger's pose, so an actor that can be staggered assigns a stagger clip to its action gate. Missing required clips make the capability unavailable rather than silently selecting unrelated animations.

Monsters, nests and the harness practice target show floating damage numbers through a `DamageNumbersComponent` wired to their hurtbox and anchored above their health bar. Each resolved contact shows its rounded damage: larger and gold for a critical hit, orange for a rear or sneak hit, grey for blocked chip damage, and a short word for a dodge, parry or block. Numbers pop in, rise, fade and return to the scene pool; an invisible monster shows none.

## Poise and hit reactions

An actor may assign a `PoiseComponent` to its hurtbox. Its shared `PoiseDefinition` sets max poise, regeneration delay and rate, break stagger, staggered damage multiplier, hyper-armor scale and whether the floating bar shows the meter. Each unprotected damaging contact removes the attack's `PoiseDamage`.

- **Flinch.** While poise remains, the hit does not interrupt and does not push. Hit particles still play.
- **Break.** The contact that empties poise staggers the actor through its action gate and pushes it by the attack's `Knockback`. The meter stays empty during the stagger, so later hits cannot chain breaks. It refills when the stagger ends, and otherwise after the regeneration delay.
- **Staggered target.** Contacts on a staggered actor, whether broken or parried, deal `StaggeredDamageMultiplier` damage before armor. A riposte is simply a hit in that window.
- **Without poise.** Actors without poise (rats, practice targets) keep the older rule: every damaging hit interrupts the current action and pushes. The exception is an action reporting hyper-armor, which only flinches.
- **Forced pushback.** An attack with `ForcePushback` also pushes a defender that would only flinch, by its `Knockback`, and interrupts its current action without a stagger or poise break. Hyper-armor still holds, a running stagger is pushed but not shortened, and poise with `ResistsForcedPushback` (the Warden) only flinches. The player's default melee secondary and the air staff's whirlwind (`Knockback` 4) force pushback.
- **Hyper-armor.** It is authored per attack as a normalized execution interval. It scales incoming poise damage by the definition's armored scale. A break still interrupts.

Attacks author `PoiseDamage`, `Knockback`, `ForcePushback` and `HitStopDuration`. Weapons without a custom definition use the player attack controller's default impact; the default secondary carries hyper-armor. Absorbed, blocked, dodged and parried contacts deal no poise damage.

Hit-stop pauses only the attacker's execution clock and pose, once per swing, after a damaging or blocked contact. Poise is runtime-only: loading, floor entry and harness resets start full. Sources without authored poise (traps, scripted hits) deal poise equal to their damage. Status ticks deal no poise damage or hit reaction; burning ticks pass through the hurtbox so elemental shields can absorb them.

## Threat tiers and telegraphs

An attack's `DefenseTraits` (Dodgeable, Guardable, Parryable, BreaksGuard) are the single source of what defenses do against it. Its `AttackThreatTier` is a label derived from them:

| Tier | Derived from | Guard | Parry | Dodge | Indicator style |
| --- | --- | --- | --- | --- | --- |
| Normal | Guardable or Parryable, no BreaksGuard | Blocks with chip damage | If Parryable | If Dodgeable | Shared warning, normal tint |
| Heavy | BreaksGuard | Breaks (see above) | If Parryable | If Dodgeable | Shared warning, orange tint |
| Unblockable | Neither Guardable nor Parryable | No | No | If Dodgeable | Shared warning, red tint |

Monsters use one reusable `AttackIndicatorComponent` (`attack_indicator.tscn`) for weapon warnings, including Normal melee attacks. It draws the authored melee contact footprint or projectile lanes on the ground, follows aim during wind-up, becomes clearer at commitment and fades after release. During projectile aiming, a translucent trapezoid starts at one-third range and grows brighter while its far edge narrows toward the projectile lane. At commitment it becomes a bar that reaches through and beyond the target to the visible attack range. Threat tier changes only the tint. The indicator does not choose targets, change timing or affect defense. Monster validation requires the capability for weapon attacks; the player omits it by default. Tier wind-up requirements remain 0.35 s Normal, 0.5 s Heavy and 0.6 s Unblockable.

The Warden's slash is Normal, so blocking it leaves time to counter-attack during its recovery: block, attack, block. The Warden uses the same indicator for its current slash and any future guard-breaking attack. The player's default melee secondary breaks guard, so it breaks a monster's guard.

## Projectile warnings

Crossbowmen use the shared warning during preparation. Aim locks 0.3 seconds before release; the shot uses that captured origin and direction even if the player moves afterward. Release, interruption and target death remove the warning; an interrupted preparation cannot fire later. Walls and invisible shooters suppress the guide.

`AttackDefinition.ProjectileAimLockDuration` opts single/spread shots into this behavior; zero retains release-time aiming. The attack executor owns tracking and commitment independently of effects/debug toggles. `AttackController.Indicator` references the actor's reusable `AttackIndicatorComponent`; the crossbowman and projectile template are prewired. The component only draws attack data and does not choose targets or apply damage. Radial and area-drop patterns require different warning shapes and do not accept this setting.

## Contact resolution

Confirmed contact resolves faction/death filtering, eligible active defense, debug immunity, the hit zone and sneak bonus, the staggered multiplier, armor, any active magic shield, health and then the hit reaction. Magic shields reflect a fraction of absorbed damage after the original contact resolves; reflected contacts cannot reflect again. There is **no passive evasion or accuracy contest**. Dexterity currently contributes critical chance, not an invisible defense roll. Physical movement can avoid contact; timed dodge/parry can reject eligible contact; guard reduces damage before armor. Armor absorbs a uniform roll between zero and its value from each physical hit; elemental damage (missiles, spells, burning) passes it, and elemental wards and resistances defend against it instead.

Player and enemy facades forward complete `ICombatDamageable` contacts to their authored hurtboxes. Body or child-collider contact must retain attack identity, delivery kind and defense eligibility, just like a direct hurtbox contact. Non-damageable detection areas (interaction, pickup or stairs) are ignored by projectile contact and splash queries; their parent actor does not make them combat surfaces.

Dodged/parried contacts suppress ordinary damage and hit reactions. Blocks report final chip damage without a hit reaction; guard breaks report their reduced damage and stagger instead. Positive unprotected damage then [flinches, interrupts or staggers](#poise-and-hit-reactions) the defender. Parry interrupts only the source execution it actually intercepted. Each swing resolves once per defender, including a defended contact; projectile splash cannot hit its directly resolved target a second time.

Defense eligibility is authored per attack: ordinary melee allows dodge, guard and parry; ordinary projectiles allow dodge/guard; area impacts have independent flags. Environmental damage does not automatically grant defense eligibility. Unavoidable content needs clear presentation.

Attack profiles may carry on-hit status payloads. The hurtbox rolls each payload only after direct contact resolves as damaging and leaves the target alive. Chance uses the attack or projectile RNG; a full chance consumes no draw. Venom and poison share the same periodic buff, with authored refresh identity for repeated applications. Defended, filtered, absorbed, splash and lethal contacts do not apply it. See [status conditions](stats-and-itemization.md#buffs-and-status-conditions).

## Facing and sneak attacks

A hurtbox may assign a `HitVulnerabilityProfile` (monsters use `humanoid_vulnerability.tres`; the player has none). Direct melee and projectile contacts are classified by where they come from relative to the defender's facing: front within `FrontHalfAngle` (60°), rear within `RearHalfAngle` (45°) of directly behind, side otherwise. Area, hazard and environmental damage have no zone.

- **Rear hits** deal `RearDamageMultiplier` (1.5×) damage and `RearPoiseMultiplier` (2×) poise damage.
- **Sneak attacks** land on a monster that is unaware: no target yet, and sleeping, idle, guarding or patrolling. They deal `UnawareDamageMultiplier` (2×) damage and, by default, empty its poise. Rear and sneak bonuses do not stack; the larger applies.
- **Noticing.** Awareness is read before the contact; afterwards the monster targets the player who struck it and starts chasing.
- **Feedback.** Rear and sneak hits scale the attacker's hit-stop by the profile's `EmphasisHitStopScale` (2×), returned in `DamageResolution`; the combat trace records zone and awareness.

Modifiers are deterministic multipliers, never rolls, and apply only to unprotected contacts.

## Attack slots

The player owns an `AttackSlotsComponent`: by default two melee and one ranged monster may start an attack against the player at once. A monster reserves a slot when its attack is accepted and returns it when the attack completes or is interrupted. A rejected start returns it immediately, and death, target loss or leaving the tree forgets it. After attacking, a monster yields for `ReacquireDelay` (0.75 s) only while another monster is waiting for the same kind of slot, so a lone monster keeps its normal cadence. Monster ripostes also need a slot. Attack policies can opt out with `UsesAttackSlot`; the Warden does, so minions never stall a boss.

Monsters using `EngagementRingStrategy` (skeleton minion and warrior) approach while a slot is free and otherwise wait on a ring around the player at `RingDistance` (3.5), facing it. Waiting monsters spread evenly by arrival order; with `PreferRear` the ring starts behind the player, so crowds surround and flank. Ring points snap to the navmesh. Rats and crossbowmen keep their own movement policies but still take turns through the slots.

## Enemy decisions and persistence

Enemies react to visible, plausible threats after an authored delay. They cannot read player inputs or reroll each frame: one threat produces one categorical dodge/guard/no-response choice. The defense policy's guard interval dodges instead when the observed attack is unsafe to guard (not Guardable, or BreaksGuard) and the monster can dodge, otherwise it does not respond; forced harness modes bypass the policy. Directional checks, cooldowns, pressure and recovery still govern execution. See [monster policies](monster-authoring-design.md#independent-capabilities-and-policies).

Monster melee wind-ups track their target no faster than the attack's `WindUpTurnRate`, then hold the facing committed `FacingLockLead` seconds before contact (see [reliable melee contact](weapon-attack-system.md#reliable-melee-contact)). Moving sideways during that window can make a committed swing miss.

Heavy actors can be outmaneuvered:

- **Turn cap.** `MovementComponent.MaxTurnRate` caps all smooth turning (degrees per second). Only explicit facing snaps ignore it, and monster defenses no longer use them.
- **Start angle.** An action's `MaximumStartAngle` only lets it start when the target is within that angle of the monster's facing.
- **Holding.** A monster in reach but not facing its target holds and turns toward it instead of swinging.
- **Defenses.** A monster raises its guard along its current facing and does not guard against an attacker outside the guard's frontal sector. Any turn-capped actor raises its guard where it already faces instead of snapping toward the requested direction. A riposte turns at the normal rate and is rejected if the target is not yet in front.
- **The Warden.** It turns at 90°/s, starts swings only within 45° and tracks a wind-up at 60°/s until 0.4 s before contact. A player who gets behind it and stays there draws no swings, but running full circles laps back through its front.

Saving retains defensive cooldowns, dodge charges with their recharge, and remaining combat commitment. Loading cancels transient actions and restores a neutral lockout without immunity; held guard requires a fresh press after load, pause or focus loss. Stair travel rejects committed actions. [Randomness and saves](randomness-and-seeds.md) owns reconstruction and compatibility rules.

Future Dexterity-based defense tuning must affect an explicitly activated action, never introduce passive hit rejection. Balance and generated-room movement remain playtest work in the [roadmap](development-roadmap.md).
