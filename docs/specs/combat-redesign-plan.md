# Combat redesign: guard walking

**Status:** implemented except for a guarded walk. [Combat actions and defense](../combat-system.md) and [weapon attacks](../weapon-attack-system.md) describe the delivered telegraph → respond → punish combat; issue #41 tracks the guarded walk and #30 the playtest risks below.

## Guard walking

`GuardDefinition.MovementSpeedMultiplier` already lets a guarding actor move: shield-bearing monsters keep closing in at half speed behind a raised guard, while the player's `weapon_guard.tres`, `shield_guard.tres` and `makeshift_guard.tres` hold still. The guard pose holds the `Blocking` clip, so a moving guard slides, and the KayKit rigs have no guarded walk. Once a clip exists, for example authored from the upper body of `Blocking` and the lower body of a walk cycle:

- Add an optional guard-walk clip to `DefenseAnimationProfile` and switch the guard pose to it while the actor moves, sampled from the movement clock.
- Give the player's guards a speed (start at 0.4) only on rigs that have the clip.
- Verify in the editor that the walk reads as guarded and that facing still covers the guarded sector.

## Open decisions and risks

- **Player kit tuning.** Fresh presses cancel recovery for the first two combo steps, so timed presses raise damage per second well above a held button, and player contact windows cover only the visible strike. Playtest both against monster health and reach before retuning.
- **Playtest review.** Engagement-ring spacing in generated rooms, dodging through monsters next to walls, sneak-attack frequency, riposte pacing and whether flinches read clearly.
- **Deferred.** A charged secondary, depth-scaled attack slots and a rear vulnerability profile for the player wait until playtests show a need.
