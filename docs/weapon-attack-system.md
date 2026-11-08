# Weapon and attack system

Players and monsters share an attack executor for melee contact, projectiles and spell casts. [Combat actions and defense](combat-system.md) owns action conflicts and damage outcomes; [monster authoring](monster-authoring-design.md) owns AI requests. Authored Resources and scenes remain the source of tuning values.

## Equipment and hand capabilities

The equipment model supports one-handed weapons in either hand, dedicated off-hand items, two-handed weapons, and dual spell casting.

- **One-handed weapons:** Swords, daggers, axes, maces, and wands can be equipped in either the main hand (`EquipmentSlot.WeaponHand`) or off hand (`EquipmentSlot.ShieldHand`).
- **Two-handed weapons:** Greatswords, polearms, crossbows, and staves require the main hand and occupy both hands.
- **Dedicated off-hand items:** Shields and portable lights (lanterns, torches, candelabra) are off-hand only.
- **Context menu & Equipping:** The inventory context menu presents "Equip in main hand" and "Equip in off hand", disabling invalid slots. Double-clicking or auto-equipping an item defaults to the main hand, reserving the off hand for explicit context-menu selection.

| Main hand | Off hand | J: primary | K: secondary | L: off-hand action |
| --- | --- | --- | --- | --- |
| One-handed melee | Shield | Quick attack | Strong/weapon technique | Shield guard/parry |
| One-handed melee | One-handed melee | Quick paired attack | Strong paired attack | Unavailable; no guard/parry |
| One-handed melee | Wand | Quick attack | Strong/weapon technique | Off-hand wand's bound spell |
| One-handed melee | Portable light | Quick attack | Strong/weapon technique | Makeshift guard/parry |
| Wand A | Wand B | A's free missiles only | A's bound spell | B's bound spell |
| Wand | Shield | Free missiles | Bound spell | Shield guard/parry |
| Wand | Portable light | Free missiles | Bound spell | Makeshift guard/parry |
| Two-handed staff | Occupied by staff | Staff's free missiles | Staff's bound spell | Unavailable |
| Two-handed melee | Occupied by weapon | Existing primary | Existing secondary | Existing weapon/innate guard |

## Paired melee attacks

Equipping two one-handed melee weapons activates paired attacks on J and K:

- **Single combined hit:** The main weapon supplies the attack definition, reach, contact envelope, combo structure, timing, critical chance and cooldown. The off-hand weapon contributes a configured fraction (`OffhandDamageScale`) of its damage to each strike. The defender resolves one combined hit; the off-hand weapon does not trigger separate hit rolls or attack cooldowns.
- **Dual-wield animations:** An eligible melee pair selects paired semantic styles (`dualwield.chop`, `dualwield.slice`, `dualwield.stab`), animating both arms together.
- **Removal of guard:** Equipping an off-hand melee weapon removes defensive guard; pressing L performs no action and cannot silently fall back to an innate guard.

## Spell weapons (wands and staves)

Wands and staves are spell weapons. Each carries one bound spell (`MagicStaff.BoundSpell`, a `SpellDefinition`), which also sets its element.

- **Primary missiles:** Only the main-hand wand or staff fires free homing magic missiles on J (three darts at 0.22× hit for a wand, four at 0.25× for a two-handed staff). An off-hand wand never fires missiles.
- **Bound spells:** Secondary K casts the main wand's spell; off-hand L casts the off-hand wand's spell. Both consume mana from the shared actor pool.
- **Independent cooldowns:** Each wand tracks its cooldown independently by item instance ID (`wand:<id>`). Equipping two wands with the same spell gives each its own cooldown timer. Moving a wand between hands, reassigning slots, or saving and reloading preserves active cooldown timers.
- **Released-cast handoff:** As soon as an active spell cast reaches its release point and enters `Recovery`, casting with the opposite wand can immediately take over the recovery pose. The handoff never cancels the first spell's active projectiles or world effects, and both wands retain their own cooldowns.
- **Wards:** The actor's `SpellCaster` manages spell instances per equipped focus. An active defensive ward is owned by its specific source wand instance; remaining capacity is preserved and restored on that wand across save/load and floor transitions.

## Off-hand defenses

- **Shields:** Provide dedicated directional blocking and a tight opening parry window on L.
- **Portable lights:** Equip in the off hand and provide a lightweight makeshift guard on L (e.g. 35% damage mitigation) with parry capability, allowing torch-bearing adventurers to defend themselves without a shield.
- **Fallback guards:** When the off hand is empty, L falls back to the equipped weapon's guard or the player's innate guard.

## HUD: hand-based action panels

The combat HUD provides two held-item panels in place of separate action previews:

- **Main hand panel:** Displays the item held in the main hand. Input glyphs J and K are aligned on the left side of the panel, with small independent cooldown bars beneath each glyph. K's spell mana cost appears at the lower right when applicable.
- **Off hand panel:** Displays the off-hand item (or empty slot with fallback guard). Input glyph L is positioned on the left side with its own cooldown bar and spell mana cost.
- **Two-handed items:** A two-handed weapon or staff visually projects into both panels, showing J/K availability on the main panel and weapon guard availability (or unavailable for staves) on the off-hand panel.
- **State feedback:** Inactive actions or cooling/unaffordable techniques dim their individual glyphs and indicators without disabling usable sibling actions.

## Authoring attacks

A weapon supplies stats, timing, an explicit attack style, grip and optional custom attack definition. `AttackDefinition` describes delivery, projectile pattern, targeting and effects without depending on quality, a particular actor rig or session state. Its impact fields cover poise damage, knockback, forced pushback, a hyper-armor interval and hit-stop (see [poise](combat-system.md#poise-and-hit-reactions)); the `BreaksGuard` defense trait makes it [Heavy](combat-system.md#threat-tiers-and-telegraphs). Its tracking fields cover facing lock lead and wind-up turn rate. Preparation and monster validation share the same rules: they reject negative or non-finite values, a reversed armor interval, and a lead that is ranged or longer than the wind-up. Runtime attacks snapshot definitions rather than mutate shared Resources.

Reuse the generic projectile patterns, single and spread, and their count, spread, speed, range and damage settings before adding new mechanics. Projectile scenes carry movement, collision, effects and impact area; attack definitions set the contacts' damage element. Shots fly at chest height and only world geometry crossing that flight line stops them, so they clear low furniture. Their damage still reaches the full height below it: an upright lane (`GroundReach`) hits short monsters such as rats. Direct targets are excluded from the same projectile's splash, including defended direct hits.

Optional flight settings shape single and spread shots. A stagger releases a spread one projectile at a time on the attack clock, from the current muzzle; cancelling the attack discards projectiles not yet released, and the last one must leave within the prepared duration. Homing turns each projectile horizontally toward the target captured by the release-time aim query, at a limited rate after a short delay. A projectile gives the target up for good once it dies or is freed, or falls more than 90° off its nose, so a miss flies on instead of circling. A launch ramp starts any projectile at a fraction of its speed. All three are off by default.

Every hit rolls through `AttackStats`: an amount from the damage range, then a critical check that doubles it. Melee contacts, projectiles (once directly, again per splash owner) and spells share that one rule, so a seeded run draws in the same order wherever a hit lands.

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
