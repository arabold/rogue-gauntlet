# Weapon And Attack System

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
- `Weapon`: Base equipable action item. Adds damage, accuracy, crit, action timing, and optional `CustomAttackDefinition`.
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
