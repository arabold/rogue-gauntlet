# Combat actions and active defense

Implemented rules for players and monsters. See [weapon attacks](weapon-attack-system.md) for attack authoring, [monster authoring](monster-authoring-design.md) for AI policy, and the [combat harness](combat-harness.md) for diagnostics. The broader [action design](actor-actions-and-interactions.md) distinguishes future magic and interaction work.

## Action ownership

Each actor can own one committed action: attack, dodge, guard, item use, interaction or forced reaction. Rejected requests change nothing. Dodge or guard can replace attack startup, but cannot interrupt active contact or recovery. Committed item use prevents voluntary replacement. Stagger interrupts voluntary actions; death cancels and disables them. Ordinary movement remains separate but obeys the current action's restrictions.

The executor owns its timing and releases ownership on completion or interruption. Movement and animation follow that same commitment; stale callbacks cannot complete an old action or cancel a newer one. Shared authored Resources are definitions. Accepted actions snapshot their settings so live edits cannot change an action halfway through.

## Dodge, guard and parry

Dodge combines physical displacement with an authored immunity interval, recovery and cooldown. Collision may stop displacement without extending immunity. AI must also find a supported, clear destination before dodging.

Guard commits to a frontal facing and progresses through raising, held protection and release recovery. Its optional opening parry window is single-use. Shields take precedence over an eligible one-handed weapon, then innate guard; two-handed weapons suppress the offhand source. A shield visual alone grants no protection. Rebinding equipment cancels guard while retaining defensive cooldowns.

Guard pressure can prolong a response within its maximum hold. Normal release finishes the current reaction and recovery before an enemy may riposte. A riposte uses the actor's ordinary attack, rechecking range, sight and availability. It grants no extra action or immunity.

Presentation is explicitly assigned in the actor scene. Attacks, defenses and stagger share one pose owner and gameplay clock. Missing required clips make the capability unavailable rather than silently selecting unrelated animations.

## Contact resolution

Confirmed contact resolves faction/death filtering, eligible active defense, debug immunity, armor and health. There is **no passive evasion or accuracy contest**. Dexterity currently contributes critical chance, not an invisible defense roll. Physical movement can avoid contact; timed dodge/parry can reject eligible contact; guard reduces damage before armor.

Dodged/parried contacts suppress ordinary damage and hit stun. Blocks report final chip damage without ordinary knockback. Positive unprotected damage interrupts committed actions before knockback. Parry interrupts only the source execution it actually intercepted. Each swing resolves once per defender, including a defended contact; projectile splash cannot hit its directly resolved target a second time.

Defense eligibility is authored per attack: ordinary melee allows dodge, guard and parry; ordinary projectiles allow dodge/guard; area impacts have independent flags. Environmental damage does not automatically grant defense eligibility. Unavoidable content needs clear presentation.

## Enemy decisions and persistence

Enemies react to visible, plausible threats after an authored delay. They cannot read player inputs or reroll each frame: one threat produces one categorical dodge/guard/no-response choice. Directional checks, cooldowns, pressure and recovery still govern execution. See [monster policies](monster-authoring-design.md#independent-capabilities-and-policies).

Saving retains defensive cooldowns and remaining combat commitment. Loading cancels transient actions and restores a neutral lockout without immunity; held guard requires a fresh press after load, pause or focus loss. Stair travel rejects committed actions. [Randomness and saves](randomness-and-seeds.md) owns reconstruction and compatibility rules.

Future Dexterity-based defense tuning must affect an explicitly activated action, never introduce passive hit rejection. Balance and generated-room movement remain playtest work in the [roadmap](development-roadmap.md).
