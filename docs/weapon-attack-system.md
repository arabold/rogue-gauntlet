# Weapon and attack system

Players and monsters share an attack executor for melee, projectiles and current staff effects. [Combat actions and defense](combat-system.md) owns action conflicts and damage outcomes; [monster authoring](monster-authoring-design.md) owns AI requests. Authored Resources and scenes remain the source of tuning values.

## Authoring attacks

A weapon supplies stats, timing, an explicit attack style, grip and optional custom attack definition. `AttackDefinition` describes delivery, projectile pattern, targeting and effects without depending on quality, a particular actor rig or session state. Its impact fields cover poise damage, knockback, forced pushback, a hyper-armor interval and hit-stop (see [poise](combat-system.md#poise-and-hit-reactions)); the `BreaksGuard` defense trait makes it [Heavy](combat-system.md#threat-tiers-and-telegraphs). Its tracking fields cover facing lock lead and wind-up turn rate. Preparation and monster validation share the same rules: they reject negative or non-finite values, a reversed armor interval, and a lead that is ranged or longer than the wind-up. Runtime attacks snapshot definitions rather than mutate shared Resources.

Reuse generic projectile patterns—single, spread, radial and area drop—and their count, spread, speed, range, damage and impact-area settings before adding new mechanics. Projectile scenes carry movement, collision and effects; attack definitions may override their impact area/falloff. Shots fly at chest height and only world geometry crossing that flight line stops them, so they clear low furniture. Their damage still reaches the full height below it: an upright lane (`GroundReach`) hits short monsters such as rats. Projectiles that fall vertically, such as area-drop shards, set it to zero. Direct targets are excluded from the same projectile's splash, including defended direct hits.

Current staffs use authored fireball, frost-spread, radial air/whirlwind and falling-rock behaviors. These are existing attacks, **not** the proposed learned-spell/mana system. Its separate [action design](actor-actions-and-interactions.md#weapon-techniques-learned-magic-and-classes) describes the intended progression.

## Attack styles, rig profiles, and timing

Attack style is independent of handedness and damage delivery. A weapon requests a semantic style; a nonempty definition override takes precedence. The actor's animation profile maps that style to a native clip and normalized contact/release window. Monsters use their own bindings and one configured attack policy; a model need not support every player style.

Contact windows must match the visible strike: `ContactStart` opens as the weapon comes down, not while it is raised, and `ContactEnd` closes as the swing settles. Use `.agents/skills/godot-mcp/scripts/sample_attack_clip.gd` to print a bone's height, reach and speed through a clip. Monster profiles follow it: the skeleton chop uses 0.52–0.66, the Warden slash 0.32–0.50 and the rat bite 0.48–0.62. So does the player profile, measured on the KayKit adventurer clips: the one-handed diagonal slice uses 0.35–0.50, the one-handed chop 0.50–0.64, the two-handed chop 0.40–0.58 and thrusts 0.20–0.34. One-handed axes and hammers perform in 0.42 seconds (heavy maces 0.55), so their later strike still lands about 0.2 seconds after the press.

Preparation validates the definition, animation and local dependencies before acceptance. Playback, contact/projectile release and recovery use one gameplay clock. A cancelled wind-up cannot release a projectile later. Invalid mappings reject the action rather than silently choosing another clip. Profile edits affect the next accepted attack.

An animation binding can assign an optional `WindUpClip`. It plays during preparation up to the normalized contact start, then the main `Clip` plays from its beginning through the remaining action. The crossbow skeleton uses `1H_Ranged_Aiming` through tracking and aim commitment, switching to `1H_Ranged_Shoot` when the projectile releases. Both clips are validated and captured on acceptance; this presentation does not change shot timing or aim locking.

Grip adapters and visual wrappers preserve attachment axes. Verify equipped items on the actual rig, not only in a floating catalog preview. Runtime grip edits require re-equipping; imported clip-track edits during playback are unsupported. Bow animation support exists, but active bow gameplay remains unfinished.

The skeleton crossbowman and projectile-monster template mount their crossbow on `handslot.r`, with the same authored transform as `kaykit_crossbow.tres`. The raw crossbow's barrel follows local +Z; the aiming pose must align that axis with the target once the weapon is raised and aim locks. Do not reuse a blade's wrist attachment/offset for a crossbow. Tests check these fixed scene mounts against the player grip and sample the actual aiming pose during the locked interval.

## Primary combos

A melee weapon may assign a `PrimaryCombo` (`AttackCombo`): ordered `AttackComboStep`s shared by its weapon family under `scenes/attacks/combos/`. Each step requests its own style (empty keeps the weapon's) and scales the weapon's own primary attack: duration, damage, poise damage and hit-stop. Every weapon therefore keeps its stats, speed and contact shape. The current families open with the weapon's style, follow with a second swing (`<family>.combo2`) and end with a finisher (`<family>.combo3`) at 1.3× duration, 1.5× damage, 1.8× poise damage and 1.6× hit-stop. The rig profile binds those styles to clips.

The next step chains when the player attacks again within `ChainWindow` (0.35 s) of the previous step's recovery starting. A fresh press, buffered or made during recovery, cancels that recovery into the next step; a held button lets recovery finish first. Interruption, any other action (dodge, guard, technique, item use, stagger), switching weapons or letting the window lapse returns to the opener. The finisher always plays its full recovery. `PlayerAttackController` owns the step index. Weapons without a combo, ranged weapons and unarmed attacks repeat their single primary attack.

## Reliable melee contact

`MeleeAttackProfile` defines actor-relative arc, box, forward-capsule or radial contact. A definition may override the weapon's profile; actors may provide a default. The actor scene explicitly assigns its detector. Reliable gameplay contact is independent of exact blade motion.

Melee uses horizontal reach and one shared vertical contact envelope. Visual height, crouching or blade position do not create safe pockets, including beside the attacker's physical body. Stacked combat floors and height-based dodging are unsupported.

During the timed contact window, stationary and moving overlaps resolve to one damage owner and one result per swing. Crossing an entire window in a physics step still checks contact. Walls and blocking props obstruct damage. Facing commits at contact, or `FacingLockLead` seconds earlier when an attack authors a lead (melee only, at most the wind-up). An AI attacker tracks its target until then, turning no faster than `WindUpTurnRate`. Interruption stops further queries. Forward reach and engagement settings must agree with the visible attack.

Optional swooshes follow authored `TrailBase`/`TrailTip` markers on the equipped weapon. Natural attacks can use a generic arc; thrusts can omit effects. Fading or disabling the visual cannot extend or remove damage.

## Placement and review

Persistent item/actor spawns require clear supported space. Short-lived effect landing may allow decorations while retaining wall margins; use the appropriate placement rule rather than treating an effect as persistent furniture.

Run the [combat harness](combat-harness.md) to review equipment, contact volumes, moving targets, slow motion and defense. Check wind-up, contact/release, recovery, interruption and grip at gameplay scale. Additional spell behavior, attack-field affixes, resistances and richer monster clips belong to future slices in the [roadmap](development-roadmap.md).
