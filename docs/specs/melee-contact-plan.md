# Reliable melee contact and swing presentation

Status: implemented, October 19, 2026. Revises the remaining slice of the
[attack animation design](attack-animation-plan.md): gameplay collision is independent of
exact blade motion; contact timing remains shared. Blade-marker trails follow the weapon visually; gameplay
volumes retain their independently authored reach and geometry.
Uses the [program-design skill](../../.agents/skills/program-design/SKILL.md).

## Contracts and ownership

- `MeleeAttackProfile` is a shared `[GlobalClass] Resource`: arc/box/forward capsule/radial
  geometry, reach, width, height, center height, engagement margin, blocking mask, optional
  swoosh PackedScene. Shape construction has no scene/world side effects. Weapons assign a
  profile; AttackDefinition can override it. Enemy profiles assign one for the melee slot.
- `MeleeHitDetector` is an actor-local Node assigned to AttackController. It owns an attack's
  primitive query shape and optional debug mesh. It queries areas and bodies directly during
  contact; no transient Area3D entry signals. Query batches page using collider RID exclusions,
  so crowded attacks do not silently truncate targets. Body/hurtbox representations resolve
  to one damage owner and existing per-attack deduplication remains authoritative.
- Attack preparation duplicates the chosen contact Resource into the runtime definition.
  Invalid dimensions/missing dependencies reject acceptance before cooldown or damage.
  A physics interval intersecting contact evaluates at least once, including when it crosses
  both boundaries. Facing locks at first contact; actor position remains the volume origin.
  AI can turn in preparation and must approach inside the profile's conservative reach.
- Occlusion queries prevent damage through world/wall/prop blockers. Geometry tests actual
  target shapes, not only target centers. Vertical extent is authored, independent of rig bones.
- `MeleeSwoosh` is a cosmetic scene tracing authored blade markers on equipped weapons.
  It samples animation poses within the shared contact interval, independently of damage queries.
  Unmarked natural attacks and radial specials retain stylized arcs; stab/bite profiles usually
  omit effects. It cannot query targets or deal damage. A short cosmetic fade may outlive contact;
  disabling effects leaves the same collision behavior.
- HurtBoxComponent retains IDamageable.TakeDamage and adds a typed damage-resolution result
  plus a local signal for damaged/evaded/absorbed/invulnerable/filtered outcomes. AttackController
  reports contact outcomes, no contact, obstruction and interruption through local diagnostics.
  Gameplay RNG remains actor-owned; no generation RNG draws, new save fields or migrations.

```text
ActionSlot / EnemyBehaviorComponent
  → prepare definition + contact profile + animation binding (snapshot authored data)
  → AttackController physics interval
    → preparation: allow facing assistance
    → contact interval crossed: commit facing; query shape against current physics world
      → exclude self, normalize damage owners, block occluded contacts, deduplicate
      → HurtBox damage resolution → local outcome feedback
      → optional swoosh samples the same timeline (visuals only)
    → close contact / finish recovery / interrupt: stop queries; cosmetic fade is independent
```

## Files and slices

Hybrid delivery: establish query/geometry and observable outcomes first, then wire all hero and
monster Resources, then presentation/playground acceptance.

- `+ scenes/attacks/MeleeAttackProfile.cs`, `MeleeHitDetector.cs`, authored profiles under
  `scenes/attacks/melee_profiles/`; `~ AttackDefinition.cs`, `AttackController.cs`.
- `~ Weapon.cs`, all non-ranged weapon definitions; player defaults/special attack and scene.
- `~ EnemyBehaviorProfile.cs`, monster behavior Resources, melee request/facing and chase range;
  base enemy scene composes the same detector. Do not change reachability/chase-retention rules.
- `+ scenes/effects/MeleeSwoosh.cs`, slash/chop scenes; optional Resource references.
- `~ HurtBoxComponent.cs`, `+ common/combat/DamageResolution.cs`: preserve existing damage
  callers and return observable outcomes for melee. Armor/evasion rules remain intact.
- `~ attack_animation_harness.tscn` and harness script; add an authored practice target and
  controllable native monster. Include volume/effect toggles, defense modes and diagnostics.
- `+ tests/MeleeContactTest.cs`: actual physics overlap for pre-existing contacts, skipped
  windows, once-per-target, target edges, rotations, outside/behind targets, wall blockers,
  hero/monster masks, cancellation, and visuals disabled. Geometry/AI-range checks accompany
  runtime checks; headless navmesh limitations still apply.

Editor acceptance: use the playground to hit stationary/moving targets with slash/chop/thrust,
trigger a native monster attack against the hero, inspect volume overlays, and compare effects
on/off. Display actual evasion/absorption/interruption distinctly. Verify slow motion and low
frame-rate/large-step contact; tune feedback and generous contact volumes before tightening them.
Definition/profile edits apply to new attacks/items, not active execution snapshots. No claim of
pixel-perfect blade or particle collision is intended.

## Verification recorded

- Full GdUnit4 regression: **141 passed**, zero failed/skipped (3 minutes 18 seconds).
- Melee coverage includes all four geometry types, stationary overlaps, full skipped contact
  windows, body/hurtbox deduplication, crowded-query paging, edge contacts, facing commitment,
  blockers, faction masks, cancellation, defense outcomes and native monster damage.
- Debug and ExportRelease builds pass; existing Phantom Camera warnings remain. Godot import
  completes without resource/script errors. Tests continue to report orphan-node warnings.
- Rendered playground checks exercise slash, chop, thrust, yeti and skeleton attacks, each
  registering damage. Slash/chop ribbons, translucent query overlays and compact controls are
  visible; thrust has no ribbon. These are presentation checks, not final combat balance.
- Documentation links and diff whitespace checks pass. Navmesh pursuit remains subject to the
  existing headless-test limitation; this delivery tests the native monster attack path locally.

## Blade-aligned presentation refinement

The first fixed slash/chop arcs can drift from animated blades. Keep gameplay volumes as
implemented, and refine cosmetics with two editor-visible `Marker3D` children in each swing
weapon scene: `TrailBase` and `TrailTip`, positioned along its striking section in canonical
item-local coordinates. These authored points, not runtime mesh bounds, define the ribbon.
`MeleeSwoosh.Initialize` optionally accepts the equipped visual and resolves these markers and
its local BoneAttachment. `Sample` appends world-space marker pairs; pooled reuse clears history
and references. Missing markers retain the existing generic cosmetic arc (natural attacks);
radial specials deliberately use that arc rather than the equipped blade.

```text
AttackController contact interval (damage queries unchanged)
  → initialize optional swoosh with equipped visual
  → weapon markers available: sample bounded intermediate animation poses within contact
    → refresh the local BoneAttachment from the evaluated skeleton
    → append marker endpoints to the cosmetic ribbon
    → restore the current animation pose and attachment before rendering
  → no markers / radial special: sample the existing generic effect
  → close/interrupt: detach marker references; fade only the captured ribbon
```

Verification: engine tests compare ribbon endpoints against the actual equipped markers for
axe, broadsword and halberd, exercise a whole skipped contact window, and ensure pooled reuse
clears history. Render each weapon at several contact poses with volumes hidden; verify tip,
swing plane and direction, plus the existing effects-on/off contact tests.

Blade refinement verification: **27 attack/contact tests passed**, including three new blade
alignment/content/pooling cases. Rendered axe, broadsword and halberd contact poses align the
ribbon with the striking section. The earlier 141-test regression result above predates this
cosmetic refinement.
Debug and ExportRelease builds pass. Godot imports the marker scenes without errors; early,
middle and late contact renders preserve blade alignment and the soft outer-edge gradient.
