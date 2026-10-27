# Combat actions and active defense

Implemented rules for players and monsters. See [weapon attacks](weapon-attack-system.md) for attack authoring, [monster authoring](monster-authoring-design.md) for AI policy, and the [combat harness](combat-harness.md) for diagnostics. The broader [action design](actor-actions-and-interactions.md) distinguishes future magic and interaction work.

## Action ownership

Each actor can own one committed action: attack, dodge, guard, item use, interaction or forced reaction. Rejected requests change nothing. Dodge or guard can replace attack startup; dodge can also replace attack recovery. Active contact remains committed. Committed item use prevents voluntary replacement. Stagger interrupts voluntary actions; death cancels and disables them. Ordinary movement remains separate but obeys the current action's restrictions. The current action may also report hyper-armor (see [poise](#poise-and-hit-reactions)); any change of ownership clears it.

The executor owns its timing and releases ownership on completion or interruption. Movement and animation follow that same commitment; stale callbacks cannot complete an old action or cancel a newer one. Shared authored Resources are definitions. Accepted actions snapshot their settings so live edits cannot change an action halfway through.

## Dodge, guard and parry

Dodge combines physical displacement with an authored immunity interval, recovery and cooldown. Collision may stop displacement without extending immunity. AI must also find a supported, clear destination before dodging.

A dodge can pass through other actors. `DodgeDefinition.PassThroughLayers` removes physics layers from the actor's collision mask while it travels; walls stay solid. The player's dodge passes through the enemies layer, so dodging through a monster puts the player behind it. A dodge that ends inside a pass-through body keeps moving at half speed, without immunity and for at most `PassThroughExitTime` (0.3 s), until it is clear; then the mask returns. Every exit path restores the mask: finishing, cancellation, interruption and leaving the tree. Monster dodges pass through nothing.

The player snapshots current steering for dodge direction; with no steering, they backstep relative to facing. External motion never supplies steering. Dodge interrupts turning and holds the facing angle reached at acceptance, choosing its forward/backward/sidestep animation relative to that angle. Movement starts immediately in the requested direction; held steering resumes turning from the preserved angle after recovery. The player dodge travels 2.4 units over 0.15 seconds, protects against eligible weapon/projectile contact throughout travel, and has 0.02 seconds of recovery with a 0.6-second cooldown from acceptance. The directional dodge animation follows the same shorter travel clock for a quick hop. Hazards and environmental damage bypass dodge, including existing damage-over-time effects; elemental projectiles remain eligible attacks. These values are initial playtest tuning.

Guard commits to a frontal facing and progresses through raising, held protection and release recovery. Its optional opening parry window is single-use. Shields take precedence over an eligible one-handed weapon, then innate guard; two-handed weapons suppress the offhand source. A shield visual alone grants no protection. Rebinding equipment cancels guard while retaining defensive cooldowns.

Guard pressure can prolong a response within its maximum hold. Normal release finishes the current reaction and recovery before an enemy may riposte. A riposte uses the actor's ordinary attack, rechecking range, sight and availability. It grants no extra action or immunity.

A parry staggers the attacker for the guard's `ParryStaggerDuration` (0.9 s for the shared weapon and shield guards), long enough to answer. Hits on the staggered attacker deal its poise definition's staggered bonus, so a riposte needs no separate system.

An unparried contact with the `BreaksGuard` defense trait breaks a raised guard: it returns `GuardBroken`, lands `GuardBreakDamageMultiplier` of its damage before armor, and staggers the defender for `GuardBreakStaggerDuration`. The parry window still beats a Heavy attack.

Presentation is explicitly assigned in the actor scene. Attacks, defenses and stagger share one pose owner and gameplay clock. Missing required clips make the capability unavailable rather than silently selecting unrelated animations.

## Poise and hit reactions

An actor may assign a `PoiseComponent` to its hurtbox. Its shared `PoiseDefinition` sets max poise, regeneration delay and rate, break stagger, staggered damage multiplier, hyper-armor scale and whether the floating bar shows the meter. Each unprotected damaging contact removes the attack's `PoiseDamage`.

- **Flinch.** While poise remains, the hit does not interrupt and does not push. Hit particles still play.
- **Break.** The contact that empties poise staggers the actor through its action gate and pushes it by the attack's `Knockback`. The meter stays empty during the stagger, so later hits cannot chain breaks. It refills when the stagger ends, and otherwise after the regeneration delay.
- **Staggered target.** Contacts on a staggered actor, whether broken or parried, deal `StaggeredDamageMultiplier` damage before armor. A riposte is simply a hit in that window.
- **Without poise.** Actors without poise (rats, practice targets) keep the older rule: every damaging hit interrupts the current action and pushes. The exception is an action reporting hyper-armor, which only flinches.
- **Hyper-armor.** It is authored per attack as a normalized execution interval. It scales incoming poise damage by the definition's armored scale. A break still interrupts.

Attacks author `PoiseDamage`, `Knockback` and `HitStopDuration`. Weapons without a custom definition use the player attack controller's default impact; the default secondary carries hyper-armor. Absorbed, blocked, dodged and parried contacts deal no poise damage.

Hit-stop pauses only the attacker's execution clock and pose, once per swing, after a damaging or blocked contact. The main scene adds a short camera impulse when the player is interrupted or staggered, or causes hit-stop. Poise is runtime-only: loading, floor entry and harness resets start full. Sources without authored poise (traps, scripted hits) deal poise equal to their damage. Status ticks bypass the hurtbox and deal none.

## Threat tiers and telegraphs

An attack's `DefenseTraits` (Dodgeable, Guardable, Parryable, BreaksGuard) are the single source of what defenses do against it. Its `AttackThreatTier` is a label derived from them:

| Tier | Derived from | Guard | Parry | Dodge | Telegraph |
| --- | --- | --- | --- | --- | --- |
| Normal | Guardable or Parryable, no BreaksGuard | Blocks with chip damage | If Parryable | If Dodgeable | The wind-up animation |
| Heavy | BreaksGuard | Breaks (see above) | If Parryable | If Dodgeable | Orange ground footprint |
| Unblockable | Neither Guardable nor Parryable | No | No | If Dodgeable | Red ground footprint |

A monster's Heavy or Unblockable melee draws its contact footprint through an assigned `AttackTelegraph` (`attack_telegraph.tscn`). The footprint is the same outline the hit detector extrudes (`MeleeAttackProfile.FootprintOutline`), so it shows the real hit area. It follows tracking, brightens with a short cue when facing commits, and disappears when the contact window closes or the attack is interrupted. It is presentation only. Validation requires the telegraph for those tiers and a minimum wind-up before contact: 0.35 s Normal, 0.5 s Heavy, 0.6 s Unblockable.

The Warden's slash is Normal, so blocking it leaves time to counter-attack during its recovery: block, attack, block. The Warden keeps its `AttackTelegraph` wired for a future guard-breaking attack. The player's default melee secondary breaks guard, so it breaks a monster's guard.

## Projectile warnings

Crossbowmen show a subdued dashed aiming guide during preparation. It becomes wider, solid and arrow-tipped with a short sound when aim locks, 0.3 seconds before release. The shot uses that captured origin and direction even if the player moves afterward. Release, interruption and target death remove the warning; an interrupted preparation cannot fire later. Walls and invisible shooters suppress the guide.

`AttackDefinition.ProjectileAimLockDuration` opts single/spread shots into this behavior; zero retains release-time aiming. The attack executor owns tracking and commitment independently of effects/debug toggles. Assign its optional `AimIndicator` explicitly to the reusable `projectile_aim_indicator.tscn`; the crossbowman and projectile template are prewired, and monster validation flags missing warning assignments for locked shots. The indicator draws the trajectory and plays its assigned sound without choosing targets or applying damage. Radial and area-drop patterns require different warning shapes and do not accept this setting.

## Contact resolution

Confirmed contact resolves faction/death filtering, eligible active defense, debug immunity, the hit zone and sneak bonus, the staggered multiplier, armor, health and then the hit reaction. There is **no passive evasion or accuracy contest**. Dexterity currently contributes critical chance, not an invisible defense roll. Physical movement can avoid contact; timed dodge/parry can reject eligible contact; guard reduces damage before armor.

Player and enemy facades forward complete `ICombatDamageable` contacts to their authored hurtboxes. Body or child-collider contact must retain attack identity, delivery kind and defense eligibility, just like a direct hurtbox contact. Non-damageable detection areas (interaction, pickup or stairs) are ignored by projectile contact and splash queries; their parent actor does not make them combat surfaces.

Dodged/parried contacts suppress ordinary damage and hit reactions. Blocks report final chip damage without a hit reaction; guard breaks report their reduced damage and stagger instead. Positive unprotected damage then [flinches, interrupts or staggers](#poise-and-hit-reactions) the defender. Parry interrupts only the source execution it actually intercepted. Each swing resolves once per defender, including a defended contact; projectile splash cannot hit its directly resolved target a second time.

Defense eligibility is authored per attack: ordinary melee allows dodge, guard and parry; ordinary projectiles allow dodge/guard; area impacts have independent flags. Environmental damage does not automatically grant defense eligibility. Unavoidable content needs clear presentation.

## Facing and sneak attacks

A hurtbox may assign a `HitVulnerabilityProfile` (monsters use `humanoid_vulnerability.tres`; the player has none). Direct melee and projectile contacts are classified by where they come from relative to the defender's facing: front within `FrontHalfAngle` (60°), rear within `RearHalfAngle` (45°) of directly behind, side otherwise. Area, hazard and environmental damage have no zone.

- **Rear hits** deal `RearDamageMultiplier` (1.5×) damage and `RearPoiseMultiplier` (2×) poise damage.
- **Sneak attacks** land on a monster that is unaware: no target yet, and sleeping, idle, guarding or patrolling. They deal `UnawareDamageMultiplier` (2×) damage and, by default, empty its poise. Rear and sneak bonuses do not stack; the larger applies.
- **Noticing.** Awareness is read before the contact; afterwards the monster targets the player who struck it and starts chasing.
- **Feedback.** Rear and sneak hits double the attacker's hit-stop and report their zone and awareness through `DamageResolution` and `HurtBoxComponent.HitReacted`.

Modifiers are deterministic multipliers, never rolls, and apply only to unprotected contacts.

## Attack slots

The player owns an `AttackSlotsComponent`: by default two melee and one ranged monster may start an attack against the player at once. A monster reserves a slot when its attack is accepted and returns it when the attack completes or is interrupted. A rejected start returns it immediately, and death, target loss or leaving the tree forgets it. After attacking, a monster yields for `ReacquireDelay` (0.75 s) only while another monster is waiting for the same kind of slot, so a lone monster keeps its normal cadence. Monster ripostes also need a slot. Attack policies can opt out with `UsesAttackSlot`; the Warden does, so minions never stall a boss.

Monsters using `EngagementRingStrategy` (skeleton minion and warrior) approach while a slot is free and otherwise wait on a ring around the player at `RingDistance` (3.5), facing it. Waiting monsters spread evenly by arrival order; with `PreferRear` the ring starts behind the player, so crowds surround and flank. Ring points snap to the navmesh. Rats and crossbowmen keep their own movement policies but still take turns through the slots.

## Enemy decisions and persistence

Enemies react to visible, plausible threats after an authored delay. They cannot read player inputs or reroll each frame: one threat produces one categorical dodge/guard/no-response choice. The defense policy's guard interval dodges instead when the observed attack is unsafe to guard (not Guardable, or BreaksGuard) and the monster can dodge, otherwise it does not respond; forced harness modes bypass the policy. Directional checks, cooldowns, pressure and recovery still govern execution. See [monster policies](monster-authoring-design.md#independent-capabilities-and-policies).

Monster melee wind-ups track their target no faster than the attack's `WindUpTurnRate`, then hold the facing committed `FacingLockLead` seconds before contact (see [reliable melee contact](weapon-attack-system.md#reliable-melee-contact)). Moving sideways during that window can make a committed swing miss.

Heavy actors can be outmaneuvered:

- **Turn cap.** `MovementComponent.MaxTurnRate` caps all smooth turning (degrees per second). Only explicit facing snaps ignore it, and monster defenses no longer use them.
- **Start angle.** A melee policy's `MaxStartAngle` only lets a swing start when the target is within that angle of the monster's facing.
- **Holding.** A monster in reach but not facing its target holds and turns toward it instead of swinging.
- **Defenses.** A monster raises its guard along its current facing and does not guard against an attacker outside the guard's frontal sector. A riposte turns at the normal rate and is rejected if the target is not yet in front.
- **The Warden.** It turns at 90°/s, starts swings only within 45° and tracks a wind-up at 60°/s until 0.4 s before contact. A player who gets behind it and stays there draws no swings, but running full circles laps back through its front.

Saving retains defensive cooldowns and remaining combat commitment. Loading cancels transient actions and restores a neutral lockout without immunity; held guard requires a fresh press after load, pause or focus loss. Stair travel rejects committed actions. [Randomness and saves](randomness-and-seeds.md) owns reconstruction and compatibility rules.

Future Dexterity-based defense tuning must affect an explicitly activated action, never introduce passive hit rejection. Balance and generated-room movement remain playtest work in the [roadmap](development-roadmap.md).
