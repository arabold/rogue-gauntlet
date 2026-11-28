# Weapon and attack system

Players and monsters share one attack executor for melee contact, projectiles and spell casts. [Combat actions and defense](combat-system.md) owns action conflicts and damage outcomes; [monster authoring](monster-authoring-design.md) owns AI requests; [stats and itemization](stats-and-itemization.md) owns weapon stats, tiers and mana. Authored Resources and scenes remain the source of tuning values.

## Equipment and hand capabilities

- **One-handed weapons:** swords, daggers, axes, maces and wands go in either the main hand (`EquipmentSlot.WeaponHand`) or the off hand (`EquipmentSlot.ShieldHand`).
- **Two-handed weapons:** greatswords, two-handed axes, polearms, warhammers, crossbows and staves take the main hand and occupy both.
- **Off-hand only:** shields and [portable lights](stats-and-itemization.md#portable-light-equipment) (torches, lanterns, lamps, candles, the candelabrum).
- **Equipping:** in the run menu, what either hand can hold offers **Main Hand** and **Off Hand**; everything else offers **Equip** and goes where it belongs. Auto-equipping uses the main hand, so the off hand is always an explicit choice.

| Main hand | Off hand | J: primary | K: secondary | L: off-hand action |
| --- | --- | --- | --- | --- |
| One-handed melee | Shield | Quick attack | Strong attack | Shield guard/parry |
| One-handed melee | One-handed melee | Paired quick attack | Paired strong attack | None |
| One-handed melee | Wand | Quick attack | Strong attack | Off-hand wand's spell |
| One-handed melee | Portable light | Quick attack | Strong attack | Makeshift guard/parry |
| Wand A | Wand B | A's free missiles | A's spell | B's spell |
| Wand | Shield or portable light | Free missiles | Bound spell | Shield or makeshift guard |
| Two-handed staff | Occupied | Free missiles | Bound spell | None |
| Two-handed melee | Occupied | Primary | Secondary | Weapon or innate guard |

The HUD's two hand slots ([user interface](user-interface.md#in-run-screens), `HandActionPanel`) mirror this table: J and K on the main hand, L on the off hand, each with its own cooldown bar and a spell's mana cost; a two-handed item fills both, and an unusable action dims only its own glyph.

## Paired melee attacks

Two one-handed melee weapons attack together on J and K as one combined hit. The main weapon supplies the attack definition, reach, contact shape, combo, timing, critical chance and cooldown; the off-hand weapon adds its `OffhandDamageScale` share of its damage. The defender resolves one hit, never a second roll or cooldown. Eligible pairs play paired styles (`dualwield.chop`, `dualwield.slice`, `dualwield.stab`) that animate both arms. Holding a melee weapon in the off hand removes guard: L does nothing and never falls back to an innate guard.

## Spell weapons (wands and staves)

Wands and staves are spell weapons. Each carries one bound spell (`MagicStaff.BoundSpell`, a `SpellDefinition`), rolled when the item is created; the spell sets the weapon's element and its item name ("of Fireball"), and the weapon's tier sets the spell's level. Spells are never learned or selected: trying another spell means equipping another weapon. Any class can cast; Intelligence sizes the [mana pool](stats-and-itemization.md#mana).

- **Missiles (J).** The main-hand spell weapon's free primary is the magic missile of its element, from a shared `MissileSet`: a staggered fan of small homing darts, the caster's dependable jab, always free and never the best answer to a crowd. Each dart deals a fraction of the weapon's hit; a two-handed staff fires one more, slightly stronger dart than a wand, making up for the lost off hand. At the first release the ranged aim query picks one target (nearest to facing inside the aiming cone, in range and sight), and every dart homes on it; without one, they fly straight along the fan. A dart gives the target up for good once it dies or falls more than 90° off its nose, never retargets, and otherwise behaves as an ordinary projectile: walls stop it, it hits the first eligible body it touches, it can be dodged and guarded but not parried, deals light poise damage and no hit-stop, and carries the element, so it passes armor. An off-hand wand never fires missiles. The [magic missile plan](specs/magic-missile-plan.md) holds the unfinished look.
- **Bound spell (K, or L for an off-hand wand).** `SpellCastAction` rejects an unequipped focus, a missing level or short mana without spending anything (a shortfall raises `ManaComponent.ManaShortfall`, which flashes the HUD's mana bar). Otherwise it captures the wand's own `ResolveAttack` hit as the spell's power (`SpellPower`), starts the cast and then spends the mana; an interruption before release keeps it spent. The cast is an ordinary attack whose prepared snapshot carries the spell, and `AttackController` casts it once at the contact time. Depleted mana never switches K to the missile, and a held K does not repeat a costly cast.
- **Spell power.** Spells author damage as scales of that hit, so weapon damage, quality, affixes and crit raise them as they raise the missiles, while the martial secondary's damage multiplier does not apply. Because tier already raises the hit, each level adds only a tenth of the level-1 scale and otherwise grows reach, targets or radius. Later stat changes never alter a released spell.
- **Cooldowns.** A spell may author a `Cooldown`; most rely on mana alone. Each wand keeps its own cooldown by item instance (`wand:<id>`), so two wands with the same spell cool down separately, and moving a wand between hands, saving and loading keep its timer.
- **Two wands.** As soon as one wand's cast releases, the other can take over its recovery (`AttackController.CanHandoffSpell`) without cancelling the first spell's projectiles or effects.
- **Tooltip.** Each spell describes its level for the item tooltip: name, level, mana per cast and damage per hit from the viewer's actual hit. For a weapon in the backpack that is the hit it would deal in place of the current one (`PlayerStats.PreviewAttack`), left out while its stats are unidentified.
- **Focus lifetime.** The actor's `SpellCaster` holds each equipped focus's own spell scene. Unequipping releases it, which ends its in-flight projectiles, pending strikes, fields and ward at once; completed damage is not undone, and equipping another weapon with the same spell restores nothing. An active ward is the one effect saves keep: its remaining capacity is stored with the player and raised again on the restored weapon after a load or the stairs.

## Off-hand defenses

Shields give dedicated directional blocking and a tight opening parry window on L. A portable light gives a weaker makeshift guard with a parry window, so a torch-bearer can defend without a shield. With an empty off hand, L falls back to the weapon's or the player's innate guard. [Guard and parry](combat-system.md#dodge-guard-and-parry) owns the rules.

## Authoring attacks

A weapon supplies stats, timing, poise damage and knockback (both derived from its kind), an attack style, a grip and an optional custom attack definition. `AttackDefinition` describes delivery, projectile pattern, targeting and effects without depending on quality, a particular rig or session state. Its impact fields cover poise damage, knockback, forced pushback, a hyper-armor interval, a commit share and hit-stop (see [poise](combat-system.md#poise-and-hit-reactions)); the `BreaksGuard` defense trait makes it [Heavy](combat-system.md#threat-tiers-and-telegraphs). Its tracking fields cover facing lock lead and wind-up turn rate. Preparation and monster validation share the same rules: they reject negative or non-finite values, a reversed armor interval, and a lead that is ranged or longer than the wind-up. Runtime attacks snapshot definitions rather than mutate shared Resources.

Attacks can carry authored on-hit status payloads, applied only after a direct contact deals damage and the target survives; see [status conditions](stats-and-itemization.md#buffs-and-status-conditions).

Reuse the generic projectile patterns, single and spread, before adding new mechanics. Projectile scenes carry movement, collision, effects and impact area; attack definitions set the contacts' damage element. Shots fly at chest height and only world geometry crossing that line stops them, so they clear low furniture, while an upright lane (`GroundReach`) still hits short monsters such as rats. Direct targets are excluded from the same projectile's splash, defended direct hits included.

Optional flight settings shape single and spread shots; all are off by default.

- **Stagger** releases a spread one projectile at a time on the attack clock, from the current muzzle. Cancelling the attack discards projectiles not yet released, and the last must leave within the prepared duration.
- **Homing** turns each projectile horizontally toward the target captured by the release-time aim query, at a limited rate after a short delay. It uses no randomness.
- **Launch ramp** starts a projectile at a fraction of its speed.
- **Lob.** An arc height (`ProjectileArcHeight`) turns a single unhoming shot into a parabola to the ground where its target stood at release, or a full range ahead without one, over anything between, so it can aim at a target behind cover. An optional `LandingMarker` on the projectile scene marks the spot for the whole flight, and the landing is the shot's one contact: everyone within its impact radius takes full damage with its defense traits and statuses. The gelatinous cube's goo and the spitting slime's globs are lobs.

Every hit rolls through `AttackStats`: an amount from the damage range, then a critical check that doubles it. Melee contacts, projectiles (once directly, again per splash owner) and spells share that rule, so a seeded run draws in the same order wherever a hit lands.

## Shared spell definitions

Players, items and monsters use the same `SpellDefinition`, `ICombatSpell` and `SpellEffect` behavior. `SpellCaster` prepares the shared effect; the activation source supplies its timing, target choice, animation and costs. Wands and staves spend mana at accepted cast start. Scrolls and potions commit through item use and then run the same spell effect immediately. `MonsterSpellAction` aims at a hostile, self or ally target through its target selector, plays the actor's authored cast through `AttackController`, and can charge the monster's `ManaComponent`; a missing target, unavailable spell or short mana rejects it before the cast begins, and at release `SpellRelease.Revalidate` lets the source re-check its target, so the shared executor never depends on monster types. Monster casters heal, regenerate and enrage through the same definitions the healing potion and Rage scroll use (plus an instant heal and haste of their own), which keeps monster support magic on the player's spell path.

## Elemental spells

Four elements each have five spells with distinct tactical roles; their first levels offer comparable value. Each spell scene lives under `scenes/magic/<element>/`, its definition under `scenes/magic/spells/`.

| Element | Targeted | Self-centered area | Protective |
| --- | --- | --- | --- |
| Fire | Fireball (quick projectile), Meteor (falling impacts) | Fire Nova (expanding ring of flame), Fire Serpent (circling serpent that sets enemies burning) | Ember Ward |
| Earth | Rockfall (falling stones) | Tremor (quick ground shock), Stone Spears (wider delayed eruption) | Stone Ward, Thorn Ward |
| Air | Lightning Orb (fast travelling orb), Chain Lightning (arcs between distinct enemies) | Gust (damaging outward push), Thunderstorm (wide lightning field) | Storm Ward |
| Water | Ice Lance (crystalline strike) | Frost Nova (burst that briefly freezes), Hailstorm (lingering storm that slows) | Frost Ward, Ice Wall (ring that holds enemies out) |

- **Forward-targeted** spells (Fireball, Ice Lance, Lightning Orb, Chain Lightning) share the missiles' range and aiming cone. At release they pick visible hostiles in the cone by angle to the caster's facing, then distance, each at most once per cast. Projectiles beyond the targets scatter harmlessly through the forward fan; with no target the first flies straight ahead, so casting stays responsive. A cast without targets is valid and paid.
- **Area-targeted** spells (Rockfall, Meteor) drop one strike on each distinct visible hostile within their radius, in every direction, and the rest at random clear spots. Every landing hurts everyone inside its fixed blast radius, so it catches moving targets and overlapping blasts hit again; smaller falling rocks and meteors are cosmetic.
- **Self-centered areas** fix their center at the caster at release and hit every eligible hostile then inside, with no target cap and at most one contact per enemy per cast. An expanding wave (`WaveFront`, shared by the visual's shader and `AreaCastLifecycle`) hits each occupant as its front arrives, and anyone behind cover is spared. Areas never hurt the caster or allies. Gust, the novas, Tremor and Ice Wall push outward ([spell pushes](combat-system.md#poise-and-hit-reactions)); Frost Nova freezes survivors, with each source owning and refreshing its own freeze, so removing one source never thaws an enemy another still holds. Lingering fields (Fire Serpent, Hailstorm, Ice Wall) query occupants throughout their life: Hailstorm slows everyone inside and drops evenly spread shards (`HailPattern`) that pass armor and cause no hit reactions, and Ice Wall stays where it was cast and shoves enemies out again while they push in.
- **Wards** raise a pool of shield hit points, not a timed buff. After defenses and armor, the ward absorbs up to its remaining capacity before health and reflects a fraction of what it absorbed to a living hostile attacker through normal damage resolution; reflected contacts cannot reflect again, and environmental or missing sources get nothing back. One ward per actor: a new cast replaces it at full capacity. It ends when depleted or when its weapon is unequipped.
- **Boundaries.** Area visuals capture a sampled line-of-sight boundary at cast time (`AreaVisibility`) and clip waves, cracks, rocks and particles to it, so nothing shows beyond a blocking wall; contacts follow the same obstruction checks.

**Look.** Spells share a KayKit-matched toon language built from element-neutral pieces in `scenes/magic/shared/toon/` (ramps, alpha-eroded cards, stretched sparks, dissolving faceted cores, ramp-banded ribbons, clear-centred shells and floor cracks), with each element supplying its own hard-banded palettes. Bodies blend rather than add, so they keep their hue on bright floors; glow is reserved for hot bands, lights and lightning; fades erode or dissolve rather than turn translucent; wards, storms and fields keep thin, crisp lines with clear centres so actors stay readable. Impacts punch with a first-frame flash and a brief light spike, and `ImpactFlash` rate-limits spikes so showers never strobe; spell lights use the shared [light flicker](level-design.md#lighting-and-atmosphere) presets. Every level-one spell is a polished base, and higher levels grow the same signature in scale and intensity without hiding the player, enemies, telegraphs or floor hazards. `VisualEffectTimeline` owns instance-local materials, timed fades and sustained lifetimes; presentation never decides damage or targeting. The [elemental spell plan](specs/elemental-spells-plan.md) lists the remaining visual and balance work.

## Attack styles, rig profiles, and timing

Attack style is independent of handedness and damage delivery. A weapon requests a semantic style; a nonempty definition override takes precedence. The actor's animation profile (`scenes/attacks/animation_profiles/`) maps that style to a native clip and a normalized contact or release window. Monsters bind only the styles their actions name.

Contact windows must match the visible strike: `ContactStart` opens as the weapon comes down, not while it is raised, and `ContactEnd` closes as the swing settles. Use `.agents/skills/godot-mcp/scripts/sample_attack_clip.gd` to print a bone's height, reach and speed through a clip and place the window on it.

Preparation validates the definition, animation and local dependencies before acceptance. Playback, contact or projectile release and recovery use one gameplay clock, and a cancelled wind-up cannot release a projectile later. Invalid mappings reject the action rather than silently choosing another clip. Profile edits affect the next accepted attack.

An animation binding can assign an optional `WindUpClip`, played during preparation up to the contact start before the main `Clip` plays through the rest; the skeleton crossbowman aims with `1H_Ranged_Aiming` and shoots with `1H_Ranged_Shoot`. Both clips are validated on acceptance, and the split changes neither timing nor aim locking.

Grip adapters and visual wrappers preserve attachment axes: melee bones expect the blade along local +Y, while a raw crossbow's barrel follows local +Z, so never reuse a blade's attachment for a crossbow. Monster crossbows mount on `handslot.r` with the same transform as the player's `kaykit_crossbow.tres` grip. Verify equipped items on the actual rig, not only in a floating catalog preview. Runtime grip edits require re-equipping. Bow animations exist, but there are no bows.

## Primary combos

A melee weapon may assign a `PrimaryCombo` (`AttackCombo`): ordered `AttackComboStep`s shared by its weapon family under `scenes/attacks/combos/`. Each step requests its own style (empty keeps the weapon's) and scales the weapon's own primary attack (duration, damage, poise damage and hit-stop), so every weapon keeps its stats, speed and contact shape. The families open with the weapon's style, follow with a second swing (`<family>.combo2`) and end with a slower, harder finisher (`<family>.combo3`); the rig profile binds those styles to clips.

The next step chains when the player attacks again within the combo's `ChainWindow` of the previous step's recovery starting. A fresh press, buffered or made during recovery, cancels that recovery into the next step; a held button lets recovery finish first. Interruption, any other action, switching weapons or letting the window lapse returns to the opener, and the finisher always plays its full recovery. `PlayerAttackController` owns the step index. Weapons without a combo, ranged weapons and unarmed attacks repeat their single primary.

## Reliable melee contact

`MeleeAttackProfile` defines actor-relative arc, box, forward-capsule or radial contact. A definition may override the weapon's profile; actors may provide a default. The actor scene assigns its detector explicitly. Gameplay contact is independent of exact blade motion.

Melee uses horizontal reach and one shared vertical contact envelope, so visual height, crouching or blade position never create safe pockets, including beside the attacker's own body. Stacked combat floors and height-based dodging are unsupported.

During the contact window, stationary and moving overlaps resolve to one damage owner and one result per swing, and crossing a whole window in one physics step still counts. Walls and blocking props obstruct damage. Facing commits at contact, or `FacingLockLead` seconds earlier when a melee attack authors a lead; an AI attacker tracks its target until then, no faster than `WindUpTurnRate`. Interruption stops further queries. Forward reach and engagement settings must agree with the visible attack.

Optional swooshes follow `TrailBase`/`TrailTip` markers on the equipped weapon or, for a monster whose weapon is baked into its model, on the node its `AttackController.BakedWeapon` names. A monster can also hold one of the game's weapon or shield scenes on a hand-slot `BoneAttachment3D` through the KayKit grip transform, and a `Muzzle` marker under its `BakedWeapon` is where its projectiles leave (the orc spearman's javelins). Natural attacks can use a generic arc; thrusts can omit effects. Fading or disabling the visual never extends or removes damage.

## Review

Run the [combat harness](combat-harness.md) to review equipment, contact volumes, moving targets, slow motion, defense and spells at gameplay scale: wind-up, contact or release, recovery, interruption and grip. Persistent spawns need clear supported space, while short-lived effect landings may overlap decorations as long as they keep wall margins.
