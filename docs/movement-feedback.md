# Movement presentation

Movement presentation gives the player and grounded monsters readable movement cues without changing movement or dodge mechanics. Locomotion animation follows resolved movement speed; dust marks running, stopping and dashing, while a restrained trail emphasizes dash travel. Actor-size tuning keeps each cue in scale with its subject.

## Behavior

Effects respond to movement that actually occurs: blocked input does not produce running dust, and dash feedback begins only after the dodge travels. Dash dust direction follows the resolved displacement, including a permitted startup correction. Cues are suppressed when an actor is airborne, hidden, offscreen or otherwise visually suppressed. They do not affect movement speed, dash distance, charges, collision or immunity.

Movement mechanics remain owned by the movement and dodge systems. Presentation consumes their resolved state independently, as described in the [architecture overview](../ARCHITECTURE.md). For visual review, use the movement feedback harness listed in [debug harnesses](../scenes/debug/README.md#world-effects).
