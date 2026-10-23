# Combat actions and active defense

This describes the implemented system. [Actor actions and world interactions](actor-actions-and-interactions.md) contains the broader intended gameplay; [weapon attacks](weapon-attack-system.md) covers contact volumes and weapon styles. Use the [combat harness](combat-harness.md) to exercise these paths.

## Ownership and clocks

Each actor has an `ActorActionGateComponent`. Attacks, dodge, guard, item use, interactions and forced reactions compete for one generation-stamped lease. A rejected request cannot change its owner. Attack startup can be replaced by dodge/guard; active contact and recovery cannot. Committed item use blocks voluntary replacement. Forced stagger interrupts voluntary actions. Death disables the gate and cancels its current owner.

`ActionSlot` owns player preparation. It hands the accepted lease to `AttackController`, which reports its actual wind-up, active and recovery phases and releases the lease on completion or interruption. Enemy and direct debug attacks acquire through this same executor. Enemy AI suspends ordinary steering and attack requests while defense or stagger owns the actor. Movement reads the gate directly: stagger duration is independent of movement speed, and recovery cannot resume retained locomotion input.

Physics priorities in `CombatPhysics` order input sampling, intentions, defense clocks, AI steering, large-actor clearance, movement, attack/projectile damage, interaction completion and trace snapshots. Rendering frames are not physics ticks; engine tests wait for physics boundaries when asserting durations or displacement.

## Definitions and presentation

`DodgeComponent` owns displacement, immunity, recovery and cooldown. Physical collisions halt displacement without extending immunity. AI also checks the proposed path and endpoint for collisions, floor support and large-actor clearance before accepting a dodge.

`GuardComponent` owns fixed frontal facing, raising, a single-use opening parry window, held blocking, release recovery and rearm. Shields take precedence over one-handed weapons, then an authored innate guard. Two-handed weapons suppress the offhand shield; ranged weapons without an effective shield have no guard. The authored movement multiplier controls strafing while guard is active (initial definitions use zero); release recovery remains stationary. Normal release finishes any in-progress block/parry reaction before the release pose and recovery timer complete; forced cancellation can interrupt both. Equipment rebinding cancels an active guard and preserves rearm. Repeated release does not extend recovery; pressing guard during release recovery is rejected.

Definitions are shared authored Resources. An accepted defense takes its own snapshot; edits to its source do not change that action. `DefenseAnimationProfile` supplies humanoid clips through dodge/guard definitions and the gate's stagger binding. `CombatAnimationComponent` owns the rig using tokens, so attacks, defenses and stagger cannot run competing mixers. Gameplay clocks sample clips; shared Animation resources are never modified to force looping. Actors without an explicit stagger clip retain their authored AnimationTree hit reaction through `IsHit`. Missing assigned presentation clips reject defense availability instead of searching unrelated descendants or guessing rig paths.

## Contact resolution

`DamageContext` carries execution identity, source and owner, faction, delivery, defensive eligibility, damage, accuracy, direction and an explicit RNG. Actor roots register their composed `IDefenseEvaluator` implementations with the hurtbox; the hurtbox does not discover concrete guard/dodge types.

Resolution applies faction/death filtering, eligible active defense, debug invulnerability, passive evasion, armor and health. Dodge/parry suppress ordinary hit stun and damage. Blocks report final chip damage and suppress normal knockback; armor and passive evasion still apply. Positive unprotected damage cancels committed actions before applying knockback. A parry consumes its eligibility before callbacks and interrupts only the captured source execution; a stale source cannot stagger a newer owner.

Ordinary melee is dodgeable, guardable and parryable. Ordinary projectile defaults are dodgeable/guardable; authored flags are passed through when spawning. Area impacts have a separate authored flag set, initially dodgeable. Legacy environmental damage grants no active-defense eligibility. A direct projectile's resolved target is excluded from its splash, including when the direct contact was defended.

Attack and projectile randomness stays local. Enemy hosts derive separate attack and defense streams from floor seed plus persistent source identity; standalone scenes use a fixed default seed. These reproduce scenarios within the same build and relevant inputs, not arbitrary gameplay histories or physics replay.

## Enemy policy and persistence

Enemy defense observes its current perceived opponent's visible startup, not player inputs. An explicitly injected harness target is supported. Sight and range are revalidated when the reaction delay expires. One probability decision is made per observed attack/projectile execution; projectile exposure over multiple ticks cannot repeatedly reroll the chance. Forced harness modes change probability, retaining production delay and cooldown rules.

Blocked and parried contacts both refresh guard pressure. Enemies finish their guard hold and release recovery before attempting a riposte; a parry never bypasses the defensive sequence. Melee reach and visibility are checked again when the riposte is attempted. Riposte success is announced only after an attack is accepted, with a visible target in range and no conflicting action.

Save version 12 stores dodge cooldown, parry rearm and the actual remaining combat commitment as a neutral reload lockout. Loading cancels transient action/animation/movement ownership, restores timers, and recreates the lockout without immunity. Guard requires a fresh press after load, pause or focus loss. Stair travel rejects committed actions. Older development saves require a new run; no migration is performed.
