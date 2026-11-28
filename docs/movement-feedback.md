# Movement presentation

Movement presentation gives the player and grounded monsters readable movement cues without changing movement or dodge mechanics. `LocomotionAnimationComponent` keeps locomotion animation in step with the actor's resolved ground speed. `MovementEffectsComponent` turns resolved movement into dust for running, stopping and dashing and a restrained trail along dash travel, sized and paced by a `MovementEffectProfile` (`scenes/effects/movement/movement_effect_profiles/`) so each cue stays in scale with its actor.

## Behavior

Effects respond to movement that actually occurs: blocked input does not produce running dust, and dash feedback begins only after the dodge travels. Dash dust direction follows the resolved displacement, including a permitted startup correction. Cues are suppressed while an actor is airborne, hidden, offscreen or otherwise visually suppressed, and cleared after a reset, relocation or pool reuse. They never affect movement speed, dash distance, charges, collision or immunity.

Movement mechanics stay with the movement and dodge systems; presentation consumes their resolved state independently, as described in the [architecture overview](../ARCHITECTURE.md). For visual review, move and dodge in the [combat harness](combat-harness.md) with **Combat → Movement effects** toggled to compare against bare movement; its `movement_feedback.json` scenario replays a run-stop-dash sequence.
