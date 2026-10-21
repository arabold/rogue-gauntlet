# Weapon And Attack System

Status: current-system documentation. Future ideas, where included, are proposals rather than implemented behavior. See the [documentation index](README.md) for related systems and implementation plans.

## Overview

Rogue Gauntlet uses one shared action and attack pipeline for melee weapons, ranged weapons, elemental staffs, and future wand or spell-like items. Staffs should not become a separate gameplay system unless they need fundamentally different input or ownership rules.

The intended flow is:

1. A usable item implements `IPlayerAction`.
2. `ActionManager` invokes the item from an action slot.
3. Weapons call into `PlayerAttackController` through `Player.MeleeAttack()` or `Player.RangedAttack()`.
4. `PlayerAttackController` resolves the equipped `Weapon`, selected `AttackDefinition`, player stats, and target mask.
5. `AttackController` executes the timing, hit windows, projectile spawning, cast effects, and impact effects.

This keeps regular weapons, bows, staffs, and later spells scaling through the same concepts: item stats, authored attack definitions, player stats, and reusable attack patterns.

## Core Types

- `IPlayerAction`: Common interface for anything the player can trigger from an action slot.
- `Weapon`: Base equipable action item. Adds damage, accuracy, crit, action timing, an explicit `AttackStyle`, a `Grip` Resource, and optional `CustomAttackDefinition`.
- `RangedWeapon`: Weapon subtype that triggers the ranged attack path and adds base projectile speed/range/aiming fields.
- `MagicStaff`: Ranged weapon marker with a `MagicElement`; staff behavior still comes from its authored `AttackDefinition`.
- `AttackDefinition`: Resource that defines attack timing, hit shape, projectile pattern, effect scenes, and placement/AoE parameters.
- `AttackController`: Runtime executor that turns an `AttackDefinition` plus actor stats into hitboxes, projectiles, and effects.
- `Projectile`: Runtime damage/effect carrier. It handles movement, collision, impact effects, pooling, and optional impact-radius damage.

`Projectile.ImpactRadius` is the projectile scene's inherent impact radius. `AttackDefinition.ImpactRadiusOverride` and `ImpactRadiusMinDamageScaleOverride` are attack-level overrides for a specific weapon or spell using that projectile. This lets two weapons reuse the same projectile scene while giving one of them a larger or smaller impact area or a sharper/softer damage falloff. Impact-radius damage is generic: it applies after direct or world impact, excludes the direct-hit target, and falls off from full damage at the center to the projectile's minimum impact damage scale at the edge.

## Authored Attack Data

`AttackDefinition` is the base template for a weapon's behavior. It should describe what the attack does without knowing item rarity, specific player stats, or session state.

Important shared fields:

- `ProjectilePattern`: Shape of ranged projectile spawning, e.g. `Single`, `Spread`, `Radial`, `AreaDrop`.
- `ProjectileCount`: Shared count for fireballs, ice bolts, radial air blades, falling rocks, and future multi-projectile weapons.
- `SpreadAngle`: Arc for spread projectiles.
- `ProjectileDamageScale`: Per-projectile damage multiplier, useful when one attack spawns many projectiles.
- `ProjectileSpeed`: Projectile movement speed.
- `Range`: Maximum projectile travel range or targeting range.
- `AimingAngle`: Auto-aim cone for directed ranged attacks.
- `TargetAreaRadius`: Radius around the actor used by placement-based attacks such as `AreaDrop` to choose target positions.
- `SpawnHeight`: Vertical offset for placement-based projectiles such as falling rocks, drop pods, lightning strikes, or trap volleys.
- `ImpactRadiusOverride`: Optional attack-level override for projectile world-impact AoE. Negative values keep the projectile scene's default `ImpactRadius`.
- `ImpactRadiusMinDamageScaleOverride`: Optional attack-level override for how much damage remains at the edge of impact radius.
- `ProjectileScene`, `MuzzleEffectScene`, `CastEffectScene`: Visual and hit carrier scenes.

Avoid adding element-specific fields like `MeteorRadius` to `AttackDefinition`. Prefer generic fields that can serve multiple future patterns.

## Current Staff Behaviors

- Fire staff: a single strong directed fireball.
- Frost staff: two ice/water projectiles with a small spread.
- Air staff: radial light projectile carriers plus a player-centered whirlwind cast effect.
- Earth staff: placement-based `AreaDrop`, spawning several falling rocks around the player on valid effect landing tiles.

These are all regular `MagicStaff` resources with different `AttackDefinition` data. The element is useful for item identity and future UI/resistance logic, but the current behavior is data-driven by attack definition and scenes.

## Placement And Collision

There are two related but intentionally different placement checks:

- Item/enemy spawn placement is strict and requires a clear collision column so pickups and spawned actors do not overlap props, stairs, walls, or blockers.
- Effect landing placement is looser than persistent spawn placement: it allows props and decorations, but keeps a tile margin from walls so vertical drops do not clip wall tops.

Use the strict item spawn helpers for persistent physical objects. Use effect landing helpers for short-lived VFX/damage events.

## Scaling Direction

The next scaling layer should compute an effective runtime attack from the authored `AttackDefinition` and the equipped weapon's stats.

Good scaling inputs:

- `Weapon.Tier`
- `Weapon.Level`
- `Item.Quality`
- `EquipableItem.Rarity`
- Buffs or future affixes

Good scaling outputs:

- `ProjectileCount`
- `ProjectileDamageScale`
- `ImpactRadiusOverride`
- `ImpactRadiusMinDamageScaleOverride`
- `TargetAreaRadius`
- `Range`
- `ProjectileSpeed`

Keep the authored `.tres` attack definition as the base value source. Apply scaling at cast time by creating or filling a runtime attack parameter object. Do not mutate shared resource definitions directly during gameplay.

## Extension Guidelines

- Add new weapons or staffs by authoring resources and scenes first; avoid hardcoded scene paths in gameplay code.
- Prefer a new `ProjectilePattern` only when existing patterns cannot express the behavior with generic fields.
- Prefer generic `AttackDefinition` fields over element-specific fields.
- Put reusable visual behavior in focused effect scripts or scenes.
- Keep `Projectile` as a damage/effect carrier, not as a place for element-specific spell logic.
- Add new broad notifications through `SignalBus` only when systems are intentionally decoupled.
- Keep pooling safe for high-churn effects and projectiles; reset all runtime state when implementing `IPooledNode`.

## Future Work

- Add a runtime attack parameter copy so rarity/tier/quality can scale attack fields safely.
- Add optional per-field scaling flags or curves for weapons that should not scale every value.
- Consider authored affixes that modify attack fields, e.g. `+1 projectile`, `+20% area radius`, or `+15% projectile speed`.
- Add resistances or elemental reactions only after the shared attack data model is stable.

## Attack styles, rig profiles, and timing

Every active weapon explicitly requests an `AttackStyle`, independent of handedness, stats,
and projectile behavior. `AttackDefinition.AttackStyle`, when set, overrides the weapon's
style. `AttackAnimationProfile` and its `AttackAnimationBinding` Resources map styles to
actor-native clips and normalized contact windows. The shared player profile covers slash,
chop, thrust, spin, crossbow, bow, casting, and unarmed styles across all four class rigs.
There is no active bow item yet; its two-handed ranged binding is a prepared authoring seam.

`ActionSlot` prepares before accepting a weapon action. `AttackController` snapshots authored
attack data, resolves contact times against the weapon's perform duration, and advances both
clip playback and hit/projectile execution from its physics clock through recovery.
`AttackAnimationComponent` temporarily owns the AnimationPlayer while disabling the actor's
AnimationTree, then restores the tree for movement and other actions. Animation method/audio
tracks do not schedule damage. Non-weapon actions retain their existing animation-ID path.
Equipment replacement, interruption, death, and scene exit cancel the active attack; a cancelled
wind-up cannot release a projectile later. Missing/invalid clip mappings reject acceptance.

Monsters retain two AI attack slots, `melee` and `ranged`. Each of the 19 scenes assigns its own
animation profile, so native bites, punches, and skeleton weapon swings need no hardcoded
monster-to-clip switch. The enemy behavior profile supplies damage and requested duration;
the executor owns attack completion. Single-attack models deliberately map both slots to the
same native clip. The wizard currently reuses its bite animation for ranged attacks; a more
suitable clip is still an art/content follow-up.

Weapons also assign a `WeaponGrip` Resource: a bone name and local adapter transform.
`BoneAttachmentManager` mounts the visual through this shared hand socket rather than a rig's
original prop-specific bone. The melee adapter reproduces the KayKit Barbarian axe socket;
the crossbow adapter reproduces the Rogue crossbow socket. These preserve the canonical item
wrapper axes and work across all four class rigs. To inspect grip changes live, edit the runtime weapon in the Remote Inspector and re-equip
it to rebuild the adapter; recreate items after changing authored definitions. Profile binding
and timing edits are captured by the next accepted attack. Editing imported clip tracks during
an active attack is not a supported live-edit operation.

Use [the attack playground](../scenes/debug/attack_animation_harness.tscn) to select equipment
and invoke the production action-slot path. Animation profiles live under
`scenes/attacks/animation_profiles/`; grip adapters live under `scenes/items/grips/`.
`AttackAnimationTest` verifies rig coverage, definition overrides, timing snapshots, native
grip transforms, exactly-once release, cancellation, and equipment switching. See the
[program design](specs/attack-animation-plan.md) for contracts and delivery boundaries.

## Reliable melee contact

Weapons assign a `MeleeAttackProfile` Resource; an `AttackDefinition` can override it, with
an actor default as fallback. Monster behavior profiles assign the same Resource for their
melee slot. Profiles under `scenes/attacks/melee_profiles/` define arc, box, forward capsule
or radial geometry, horizontal reach/width, engagement margin and blocker layers. Actor scenes
compose a local `MeleeHitDetector`; no weapon-bone collider or transient overlap-entry signal
is required. Accepted attacks snapshot the profile. Every forward contact footprint starts at its full
authored width through the attacker's center; sectors and thrusts do not taper to a point
beside the body. A separate contact column also covers the attacker's physical sphere,
capsule, cylinder or box footprint, with a small physics contact skin. This prevents a target
pressed into the body from finding a safe pocket even just behind the committed swing.
It uses the same timed window, faction mask, wall blocking and once-per-attack damage rules;
it is not a permanent contact-damage aura. Forward reach remains unchanged, and targets
farther behind the body are still outside ordinary forward attacks. Debug volumes show both
shapes. Opposite-facing turns use signed horizontal yaw so a target directly behind cannot
cancel the steering vector and leave an actor permanently facing away.

Melee is planar gameplay: weapon pose, creature size, crouching and flying height do not
change contact eligibility. All query shapes use one shared 128-unit vertical column centered
on the attacker (the game's visual elevation envelope), rather than per-weapon height.
`CenterHeight` only places cosmetic effects. Aim assist and melee engagement use XZ distance.
Blocker rays run horizontally at the shared gameplay plane, one unit above world Y=0, so
floor-level targets cannot send them into the ground and elevated targets cannot bypass walls.
Stacked combat floors and height-based dodging are not supported by this model.

During contact the executor queries current physics overlaps, including stationary targets.
A physics step crossing the whole contact window still queries once. Bodies and hurtboxes
resolve to one damage owner, hit once per attack; full query batches are paged to handle crowds.
World/wall/prop occlusion blocks contact. Facing commits when contact begins; monsters use a
conservative engagement distance derived from the same reach. Interruption ends queries.

Swing weapon scenes expose `TrailBase` and `TrailTip` markers along their striking section.
Swooshes sample those points from the evaluated animation, so their plane, length and direction
follow the equipped blade, including overhead chops and two-handed swings. Intermediate pose
samples preserve a continuous ribbon across large physics steps. The attachment is refreshed
before sampling and the current pose restored before rendering. Markers are editable in Godot;
geometry is authored rather than inferred from mesh bounds at runtime.

Natural attacks without markers and radial specials retain the generic cosmetic arc. Thrusts
usually omit effects. A cosmetic fade cannot extend damage, and disabling visuals preserves
hits. KayKit grip transforms and item axes are unchanged.

Run `scenes/debug/attack_animation_harness.tscn` with **F6** in Godot to exercise real equipment
and action slots. Toggle volumes, swooshes, moving targets, slow motion and defense modes;
spawn a yeti, skeleton or Forsaken Warden and trigger its native melee attack. Feedback distinguishes contact,
evasion, armor absorption, invulnerability, obstruction and interruption. `MeleeContactTest`
verifies actual physics overlap and hero/monster execution; see the
[implementation plan](specs/melee-contact-plan.md).


## Planned unified actions and interactions

[Actor actions and world interactions](actor-actions-and-interactions.md) consolidates intended primary attack, item quick actions, timed world interaction and active defense; its separate [implementation plan](specs/actor-actions-and-interactions-plan.md) owns proposed integration. Primary attack will leave the four item-only slots, and interaction will use a dedicated hold input rather than action_1. Successful eligible pickups can fill unassigned quick slots. One shared action boundary will arbitrate these operations with dodge, guard/parry and forced reactions. The current instant interaction/use paths and general Invulnerable flag are not implementations of that proposal.
