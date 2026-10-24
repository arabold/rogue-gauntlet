# Weapon and attack system

Players and monsters share an attack executor for melee, projectiles and current staff effects. [Combat actions and defense](combat-system.md) owns action conflicts and damage outcomes; [monster authoring](monster-authoring-design.md) owns AI requests. Authored Resources and scenes remain the source of tuning values.

## Authoring attacks

A weapon supplies stats, timing, an explicit attack style, grip and optional custom attack definition. `AttackDefinition` describes delivery, projectile pattern, targeting and effects without depending on rarity, a particular actor rig or session state. Runtime attacks snapshot definitions rather than mutate shared Resources.

Reuse generic projectile patterns—single, spread, radial and area drop—and their count, spread, speed, range, damage and impact-area settings before adding new mechanics. Projectile scenes carry movement, collision and effects; attack definitions may override their impact area/falloff. Direct targets are excluded from the same projectile's splash, including defended direct hits.

Current staffs use authored fireball, frost-spread, radial air/whirlwind and falling-rock behaviors. These are existing attacks, **not** the proposed learned-spell/mana system. Its separate [action design](actor-actions-and-interactions.md#weapon-techniques-learned-magic-and-classes) describes the intended progression.

## Attack styles, rig profiles, and timing

Attack style is independent of handedness and damage delivery. A weapon requests a semantic style; a nonempty definition override takes precedence. The actor's animation profile maps that style to a native clip and normalized contact/release window. Monsters use their own bindings and one configured attack policy; a model need not support every player style.

Preparation validates the definition, animation and local dependencies before acceptance. Playback, contact/projectile release and recovery use one gameplay clock. A cancelled wind-up cannot release a projectile later. Invalid mappings reject the action rather than silently choosing another clip. Profile edits affect the next accepted attack.

Grip adapters and visual wrappers preserve attachment axes. Verify equipped items on the actual rig, not only in a floating catalog preview. Runtime grip edits require re-equipping; imported clip-track edits during playback are unsupported. Bow animation support exists, but active bow gameplay remains unfinished.

## Reliable melee contact

`MeleeAttackProfile` defines actor-relative arc, box, forward-capsule or radial contact. A definition may override the weapon's profile; actors may provide a default. The actor scene explicitly assigns its detector. Reliable gameplay contact is independent of exact blade motion.

Melee uses horizontal reach and one shared vertical contact envelope. Visual height, crouching or blade position do not create safe pockets, including beside the attacker's physical body. Stacked combat floors and height-based dodging are unsupported.

During the timed contact window, stationary and moving overlaps resolve to one damage owner and one result per swing. Crossing an entire window in a physics step still checks contact. Walls and blocking props obstruct damage. Facing commits at contact; interruption stops further queries. Forward reach and engagement settings must agree with the visible attack.

Optional swooshes follow authored `TrailBase`/`TrailTip` markers on the equipped weapon. Natural attacks can use a generic arc; thrusts can omit effects. Fading or disabling the visual cannot extend or remove damage.

## Placement and review

Persistent item/actor spawns require clear supported space. Short-lived effect landing may allow decorations while retaining wall margins; use the appropriate placement rule rather than treating an effect as persistent furniture.

Run the [combat harness](combat-harness.md) to review equipment, contact volumes, moving targets, slow motion and defense. Check wind-up, contact/release, recovery, interruption and grip at gameplay scale. Additional spell behavior, attack-field affixes, resistances and richer monster clips belong to future slices in the [roadmap](development-roadmap.md).
