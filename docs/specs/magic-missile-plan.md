# Magic missile

**In progress.** Slices 1 and 2 are implemented: the flight contract is described in [weapon attacks](../weapon-attack-system.md), and every wand now fires a first-pass missile volley of its element. Slice 3, the finished look and acceptance, remains. Every wand's free primary attack becomes the same seeker volley of small homing missiles, tinted by the wand's element. The mana-costing spell on a wand's alternate attack and the wand/staff progression belong to the [magic rework plan](magic-rework-plan.md), which also gives staves a heavier volley; dual wielding is separate. This plan only replaces the wand primary. Current ranged attacks are described in [weapon attacks](../weapon-attack-system.md), poise and defenses in [combat](../combat-system.md), and the toon effect language in the [elemental spell plan](elemental-spells-plan.md#visual-direction).

## Gameplay rules

The missile is the caster's dependable jab: always free, always available, and never the best answer to a crowd. Its damage comes entirely from the existing damage stats (wand bonus, base damage and attributes), so a deeper wand improves it without extra authoring.

- **Volley.** One cast releases three missiles in a fan, 50 ms apart. Holding primary repeats casts at the normal cadence, like other primaries.
- **Target.** At the first release, the existing ranged aim query picks the target: nearest to facing inside the wand's aiming cone, within range and line of sight, with the player's breakable-object fallback. All three missiles home on that one target; with none, they fly straight out along the fan.
- **Homing.** After a short straight launch, each missile turns toward the target's current position at a limited horizontal turn rate. It gives up homing for good, flying straight on, when the target dies or is freed, or when the target falls more than 90° off its nose (it overshot, or the target dodged past it). No retargeting.
- **Contact.** Missiles are ordinary physical projectiles: walls stop them, they hit whichever eligible enemy they touch first, and each missile resolves its own contact. They can be dodged and guarded, not parried, deal light poise damage, no hit-stop and carry the wand's damage element.
- **Budget.** Each missile deals well under a crossbow bolt (about a fifth). A volley's sustained damage is about 90% of a same-tier crossbow's nominal damage, traded for near-certain hits, a one-handed weapon and a mana spell on the alternate attack.

### Current values

The four shared definitions under `scenes/magic/missile/` author these; they are tuning values, not balance commitments.

| Setting | Value | Notes |
| --- | --- | --- |
| Missiles per cast | 3 | `ProjectileCount`, `Spread` pattern |
| Fan | 60° total | `SpreadAngle`; narrower fans closed within a metre and read as one line from the gameplay camera |
| Stagger | 0.05 s | Release at 0.225 s, last missile at 0.325 s |
| Cast cycle | 0.6 s | Wand `Delay` 0, `PerformDuration` 0.5, `CooldownDuration` 0.1 (previously 0.85 s) |
| Damage per missile | 0.22 × stat damage | `ProjectileDamageScale`; a crossbow bolt is 1.0 |
| Speed | 16 m/s | Launches at 20%, full speed after 0.4 s: the darts drift out, then zip in together |
| Range | 16 m | Crossbows reach 20–22 m |
| Aiming cone | 55° | Current wand value; crossbows use 40–45° |
| Homing | 480°/s after 0.1 s | Outer darts open about 0.9 m at 8 m and pass within 0.75 m of the target from 1.5 to 16 m |
| Hit volume | 0.3 m radius, 1.8 m tall capsule | Homing is horizontal, so the tall volume still reaches rats below the 1.2 m muzzle |
| Poise damage / knockback | 2.5 / 1 | A volley removes 7.5; a crossbow primary removes 10 |
| Defense traits | Dodgeable, Guardable | Same as today's wands and the skeleton crossbow |

Budget check against stat damage *d*: a crossbow fires 1.0 *d* every 0.8 s (1.25 *d*/s); the volley deals 0.66 *d* every 0.6 s (1.1 *d*/s). A starting Mage (INT 16) with the tier-1 wand has *d* = 4.5–10.5, so a missile hits for about 1–2 and a skeleton minion (8 health) falls to about five missiles, two volleys. In the harness practice scenario the hero's tier-1 wand (*d* = 2–8) needs seven missiles over three volleys; a warrior guards the darts; the Celestial wand kills a rat with two.

## Look

All four elements share one silhouette, size and timing; the element changes only the color ramp and one small garnish. Build from the shared toon pieces in `scenes/magic/shared/toon/`.

- **Missile.** A small, elongated faceted crystal (about 0.35 m) with a hot white core and element-colored rim (`toon_core` with the element ramp), rolling about its flight axis. A short `MagicRibbonTrail` with `toon_ribbon_glow` follows it. No light in flight. The first pass uses a six-sided bipyramid without roll; an arrowhead shard from `FacetedSpikeMesh` and the roll remain.
- **Launch.** Each missile opens with a tiny sparkle at the wand tip; the cast keeps the element's existing staff muzzle burst until a small rotating rune flash replaces it.
- **Impact.** A first-frame `toon_flash` card, a thin ring and four to six `toon_spark` diamonds, gone within 0.3 s. No scorch, smoke column or decal, since this fires constantly. A dim `ImpactFlash` light spike is optional; its rate limit already suppresses strobing. Expiry at range plays a smaller fizzle.
- **Element garnish.** Fire sheds embers (`toon_fire_ramp`); water leaves frost motes (`toon_ice_ramp`); earth is an amber-obsidian shard trailing grit, opaque like the other earth stones, with a new earth ramp; air crackles with tiny additive sparks (`toon_storm_ramp`).
- **Readability.** Keep bodies blended and below the brightness of real spells. Only the core band glows, so a stream of missiles never hides monster telegraphs.

## Contracts

Proposed names; existing types keep their names. Every new export defaults to today's behavior, so existing attacks are unaffected.

```csharp
// AttackDefinition (existing [GlobalClass] Resource): new authored exports
[Export] public DamageElement Element { get; set; }   // None keeps today's contacts
[ExportGroup("Flight")]
[Export(PropertyHint.Range, "0,0.25,0.01")] public float ProjectileStagger { get; set; }    // seconds between projectiles of one release
[Export(PropertyHint.Range, "0,1440,1")] public float HomingTurnRate { get; set; }          // degrees per second; 0 flies straight
[Export(PropertyHint.Range, "0,0.5,0.01")] public float HomingDelay { get; set; }           // straight flight before steering
[Export(PropertyHint.Range, "0.1,1,0.05")] public float LaunchSpeedScale { get; set; } = 1; // fraction of ProjectileSpeed at release
[Export(PropertyHint.Range, "0,0.5,0.01")] public float LaunchRampDuration { get; set; }    // seconds to reach full speed
public ProjectileFlight Flight { get; }
/// Finite, nonnegative values; LaunchSpeedScale in (0, 1]; stagger/homing only on Single or Spread;
/// HitWindowStart + (ProjectileCount - 1) * ProjectileStagger fits in the prepared duration.
public bool HasValidFlight(double duration);

// Plain runtime value passed from the executor to each projectile
public readonly record struct ProjectileFlight(float TurnRate, float HomingDelay, float LaunchSpeedScale, float RampDuration);

// Projectile (existing pooled Node3D)
/// Call after Initialize. A null target flies straight; values were validated at preparation.
public void ConfigureFlight(in ProjectileFlight flight, Node3D homingTarget);
```

- `AttackController.TryPrepareAttack` rejects an invalid flight beside `HasValidImpact`, before any side effect. `MonsterSceneValidator` applies the same rule to monster attacks.
- `AttackController` replaces its single `_projectileSpawned` flag with a released count. The first release captures the target (aim-locked target, or a new `FindReleaseTarget` that `Aim` now wraps), the fan directions and the cast/muzzle effects. Each projectile then leaves from the current muzzle at its own stagger time and draws its own seed from the attack RNG. A tick that skips several release times releases all that are due. Cancellation, interruption, death or unequipping discards unreleased missiles; released ones fly on.
- `ApplyProjectileOverrides` also copies `Element` to the projectile, so contacts carry the wand's element.
- `Projectile` owns steering and the launch ramp on its own physics tick, before its world ray. It loses lock through a shared liveness check extracted from `AttackController.IsValidAimTarget` (freed, or a dead `EnemyBase`/`Player`). `ResetRuntimeState` clears the flight and target, so pooled reuse never inherits homing. A trace event records lost locks.
- Homing uses no RNG, so missile flight is deterministic for the same inputs. No save data or generation changes; `SaveGame.CurrentVersion` is unchanged.

```text
primary press/hold → ActionSlot.TryPerformAction → PlayerAttackController.TryPrepareWeaponAttack
  → AttackController.TryPrepareAttack: duplicate definition, apply playback contact time
      → HasValidImpact + HasValidFlight(duration)          [reject without side effects]
→ StartPreparedWeaponAttack → StartAttack(stat damage)
AttackController._PhysicsProcess (gameplay clock)
  → first release at HitWindowStart: capture target + fan, spawn cast/muzzle effects   [once per execution]
  → each due projectile i (HitWindowStart + i × stagger):
      ScenePool.Spawn → ApplyProjectileOverrides (impact, element) → Initialize(origin, direction i, seed)
      → ConfigureFlight(definition.Flight, target)
  → elapsed ≥ Duration → FinishAttack                       [unreleased missiles are discarded]
Projectile._PhysicsProcess
  → launch ramp → steer ≤ TurnRate × dt toward live target, or lose lock for good
  → world ray (wall impact) | hitbox contact → DamageContext (element, impact) → HurtBoxComponent → impact effect → pool
```

## Authored content

- One shared `AttackDefinition` per element holds the values above, its element and projectile scene. The prepared attack duplicates it, so wands can share it.
- One base projectile scene owns the hitbox, missile body, trail, garnish emitter and impact scene, in a neutral arcane palette. Four inherited scenes swap only the ramp, garnish particles and impact variant; `ScenePool` pools each separately. The impact reuses the `FireballImpact` driver for its flash, ring and spark burst, without scorch, ground layers or light.
- The three existing wands (Wand and Ruby Scepter Wand are fire; Celestial Golden Wand is air) reference their element's shared definition instead of an inline one, and take the new cast timing. Their stats, tiers and loot eligibility stay unchanged. Water and earth wands are content for the separate wand work; the missile supports them already.
- Staffs keep their current primaries until the wand/staff progression decides their role.

```text
~ scenes/attacks/AttackDefinition.cs          Element, flight exports, HasValidFlight
+ scenes/attacks/ProjectileFlight.cs          runtime flight value
~ scenes/attacks/AttackController.cs          staggered release, release target, element override
~ scenes/attacks/Projectile.cs                ConfigureFlight, steering, launch ramp, pooled reset
~ scenes/enemies/MonsterSceneValidator.cs     same flight rule for monster attacks
+ scenes/magic/missile/                       base + four element missile scenes, impacts, four attack definitions
+ scenes/magic/earth/toon_earth_ramp.tres     earth palette for the missile
~ scenes/items/weapons/wand.tres, wand/wand_ruby.tres, wand/wand_celestial.tres
~ scenes/debug/combat/combat_harness.tscn     wands in the hero weapon list
+ scenes/debug/combat/scenarios/magic_missile_practice.json
+ scenes/debug/effects/                        magic missile workbench page
+ tests/ProjectileFlightTest.cs               steering, lock loss, ramp, pooled reset
~ tests/ (attack controller release tests), tests/ItemCatalogTest.cs
~ docs/weapon-attack-system.md, docs/combat-harness.md, docs/item-catalog.md   when delivered
```

## Delivery

Hybrid: the flight contract is the risky part (convergence at short range, pooling, release timing), and all content depends on it, so it lands first on its own; content and visuals then arrive as playable slices.

1. **Flight contract (done).** Add the exports, validation, `ProjectileFlight`, staggered release, release target, element override and projectile steering. Existing attacks keep their defaults and behave as before. Runtime tests with a fixed physics delta: turning never exceeds the rate per tick; a volley from each fan angle hits a stationary target from 2 to 16 m; lock is lost on a freed or dead target and on overshoot; no steering before the delay; the launch ramp's distance; a pooled projectile reused without a target flies straight; stagger releases at the expected times, a skipped tick releases every due missile, and cancellation releases no more; invalid flights are rejected without side effects.
2. **Playable missile (done).** Author the four definitions, the base and element scenes with a first-pass look, the earth ramp and the rewired wands; add the fire and air wands to the combat harness and a practice scenario. Catalog test: every wand references a missile definition with homing and the element matching its `MagicElement`; a flight test checks that the authored fan edges reach a minion-sized target from 1.5 to 16 m. A hands-on Mage run in `main.tscn` and a wand-versus-crossbow comparison remain for the user; only the headless smoke test and harness scenario have exercised it.
3. **Look and acceptance.** Add a workbench page that fires each element's production projectile scene, with moving targets, wall impacts, slow motion and dark lighting. Finish garnish, launch sparkle, rune flash and impacts. The first pass showed that water and air read as two similar blues, and that the staff muzzle burst dwarfs the darts. Review all four elements in bright and dark rooms, next to walls and in long held streams. Check the particle and draw budget with several casters on target hardware, then fold the delivered behavior into [weapon attacks](../weapon-attack-system.md) and remove this plan.

Each slice runs `dotnet build "Rogue Gauntlet.sln"`, its focused GdUnit4 tests with `GODOT_BIN` and `.runsettings`, and the [Godot workflow](../../.agents/skills/godot-mcp/SKILL.md) import and resource validation for new scenes and definitions. Visual quality and feel need manual acceptance; passing tests do not establish them.

## Open decisions

- **Armor against small hits.** Armor subtracts a fresh roll from every hit, so it hurts missiles far more than bolts. Monsters have no armor today. Keeping it makes armored enemies a deliberate counter to missile spam (switch to the spell or a sword); bypassing it, like hailstorm shards, keeps wands universal. Decide when armored monsters arrive.
- **Poise-less actors.** Rats and practice targets are interrupted by every damaging hit, so a stream of missiles stun-locks them. Acceptable for weak fodder; revisit if a poise-less threat appears.
- **Damage numbers.** About five hits a second may crowd the floating numbers; consider merging a volley's hits on one target.
- **Cast motion.** The shared `staff.cast` clip (`Spellcast_Shoot`) compressed to 0.5 s may read poorly for a one-handed wand; a dedicated wand style is a candidate after slice 2.
- **Monster casters.** Monsters can use a missile definition through the shared executor; their definitions should author a gentler turn rate so the player can sidestep as well as dodge.
- **Modifiers.** Extra missiles, piercing, stronger homing and elemental riders are natural wand affixes; they belong to the [affix plan](item-quality-affixes-and-curses-plan.md), not this one.
