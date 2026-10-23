# Attack styles and actor animation profiles

Status: animation selection and shared timing implemented, October 19, 2026. Player weapon
styles and all 19 monster scene profiles are authored. Reliable independent melee volumes and optional swooshes are also implemented; see the
[melee contact plan](melee-contact-plan.md). Uses the [program-design skill](../../.agents/skills/program-design/SKILL.md).
See the current [attack system](../weapon-attack-system.md) and [item catalog](../item-catalog.md).

## Authored responsibilities

A weapon's handedness determines equipment occupancy and attachment; it does not determine
its attack animation. Two-handed swords, axes, spears, bows, and crossbows can all occupy two
hands while requiring different motions. Animation selection must be explicit authored data,
independent of rarity, damage, model filename, and whether the attack launches a projectile.

- A weapon supplies a default **attack style**. An authored `AttackDefinition` can override it
  for a particular attack, including special attacks. Examples: `sword.1h.slash`,
  `sword.2h.slash`, `axe.1h.chop`, `axe.2h.chop`, `spear.2h.thrust`, `bow.shoot`,
  `crossbow.shoot`, and `staff.cast`. These semantic keys are independent of imported clip names.
- An actor owns an **animation profile** mapping supported styles to its animation clips
  and normalized contact/release timing. The same style can use different clips on
  different rigs. Weapons never refer directly to a monster's animation library.
- An enemy's existing melee/ranged `AttackDefinition` supplies its style even when it has no
  inventory weapon. Monster AI currently requests just two stable slots, `melee` and `ranged`;
  each monster profile binds these to its own native clips. A bite and axe chop therefore use
  the same AI slot without sharing a hardcoded clip. AttackDefinition style overrides remain
  available for future richer attack choices. No new monster-specific attack enum is needed.
- `AttackDefinition` continues to own hit shape, projectile pattern, targeting, and effects.
  The selected actor binding owns animation-specific contact timing. Neither the clip name
  nor the weapon category decides damage behavior implicitly.

Use shared immutable Resources for profiles/bindings and a focused actor child Node for
playback. Preserve existing KayKit attachment axes; animation integration must not compensate
for unsuitable clips by changing the item orientation contract.

## Delivered execution seams

`ActionSlot` prepares a weapon attack before accepting its delay/cooldown, then starts the
captured attack and waits for the executor's completion instead of running an independent
weapon perform timer. Slot reassignment invalidates asynchronous work. `ActionManager` gates
concurrent actions and retains the existing animation-ID path for non-weapon actions.

`PlayerAttackController` selects the weapon's default or custom definition and style, then
`AttackController` duplicates the definition and resolves normalized contact timing against the
requested perform duration. `CombatAnimationComponent` temporarily disables the actor's
AnimationTree and evaluates the selected AnimationPlayer clip from that same physics clock.
On completion/interruption it restores the tree and its Idle entry; the tree continues to own
locomotion, hit/death transitions, and other actions. No animation library/state-machine assets
are modified at runtime. Clip method/audio tracks are not gameplay event sources.

`EnemyBehaviorComponent` resolves one of its two slots before accepting an attack and gates AI
on executor completion. Its existing profile still supplies attack durations and damage rules.
All monster scenes supply local animation profiles/player/tree paths. Models with a single
native attack deliberately reuse it for both slots; the wizard's ranged slot currently uses
its existing bite motion, since that model has no casting clip.

`BoneAttachmentManager` mounts authored `WeaponGrip` Resources on the shared `handslot.r`
bone, rather than assuming the character's original prop bone suits every weapon. The melee
adapter matches the original Barbarian axe socket; the crossbow adapter matches the original
Rogue crossbow socket, including rotation and offset. Item wrapper orientation remains intact.
All 69 weapon definitions explicitly assign a style and grip. The bow style is prepared using
the available two-handed ranged clip; there is no active bow item to certify yet.

## Implemented contracts (abbreviated)

One `[GlobalClass]` Resource per script, authored in the Inspector:

```csharp
// Weapon: default for attacks without an explicit style override.
[Export] public StringName AttackStyle { get; set; }
// AttackDefinition: nonempty takes precedence over Weapon.AttackStyle.
[Export] public StringName AttackStyle { get; set; }

// AttackAnimationBinding : Resource
[Export] public StringName Style { get; set; }
[Export] public StringName Clip { get; set; }
[Export] public float ContactStart { get; set; } // normalized [0, 1]
[Export] public float ContactEnd { get; set; }   // normalized [start, 1]

// AttackAnimationProfile : Resource
[Export] public AttackAnimationBinding[] Bindings { get; set; }

// CombatAnimationComponent : Node; injected from the actor scene.
[Export] public NodePath AnimationTreePath { get; set; }
[Export] public NodePath AnimationPlayerPath { get; set; }
[Export] public AttackAnimationProfile Profile { get; set; }
public bool TryResolve(StringName style, out AttackPlayback playback);

// WeaponGrip : Resource; Weapon.Grip is assigned on every weapon definition.
[Export] public StringName Bone { get; set; }
[Export] public Transform3D Transform { get; set; }
```

The runtime `PreparedAttack` record contains an actor-owned definition copy, immutable
`AttackPlayback` values (style, clip, clip length, contact fractions), and perform duration.
It is discarded on completion/cancellation. Profiles retain no
elapsed time, current actor, or target. No new autoload or global animation registry.

Bindings use normalized contact positions so attack-speed scaling can derive both clip
playback speed and gameplay timing from one effective duration. Melee opens/closes a hit
window; ranged attacks release once at contact start. Recovery occupies the rest of the
perform duration; cooldown remains a separate action rule. Preparation/delay occurs before
this shared timeline. Existing hit-window fields remain available to explicitly unbound utility/test controllers; converted attacks use the resolved binding's window exclusively.

## Melee contact and presentation

The original proposal for exact attachment-path sweeps was revised after gameplay testing:
reliable contact uses authored actor-relative volumes, independently of detailed weapon motion.
Contact timing, committed facing and reach align animation, gameplay and optional slash/chop
swooshes. Thrusts and natural attacks can omit effects. See the implemented
[melee contact plan](melee-contact-plan.md) for ownership, query semantics and verification.

## Ordering and failure contracts

```text
ActionSlot.TryPerformAction / EnemyBehaviorComponent attack decision
  → resolve definition + explicit style + actor binding before accepting the attack
    → validate clip, duration, normalized window, and required local dependencies
    → snapshot effective timing (including supported speed modifiers)
  → accept action; begin cooldown/accounting only according to successful action rules
  → after preparation: start playback and AttackController on one attack timeline
    → open melee contact window OR release projectile once
    → close contact window; retain recovery until resolved perform duration ends
  → finish, or cancel on interruption/death/scene exit
    → stop playback and hit execution together; invalidate pending release callbacks
```

Duplicate style keys, empty keys, unavailable clips, nonpositive duration, and invalid
windows are authoring errors. Report the actor/profile/style and reject that attack before
side effects; never silently choose a one-handed swing for a missing spear/bow animation.
A deliberately authored temporary clip binding is identified as provisional content. Actor profiles need only cover that actor's
reachable attacks; validate the player's supported equipment against each selectable rig.

Profile edits apply on the next attack. Active attacks use their captured values. Supported
live-edit/debug controls must validate before applying changes; exporting a Resource is not
a promise that replacing clips mid-swing is safe. Selection consumes no RNG. Runtime grip edits require re-equipping; recreate items to test authored definition edits.
Imported clip-track edits during playback are not a supported live-edit operation. No new save
fields are needed for animation profiles or transient attack playback; do not persist an
in-progress swing or add save migrations.

## Delivery and remaining slice

The first three slices are implemented: shared Resource resolution and executor timing,
explicit player weapon authoring, and actor-specific profiles for all 19 monster scenes.
Runtime tests cover all player rigs and monster slots, style precedence, invalid authoring,
resource immutability, moving poses, recovery, interruption, and exactly-once ranged release.
The native melee/crossbow grip transforms are checked against the original KayKit bones.

`scenes/debug/combat_harness.tscn` provides a floor, player, camera, eleven selectable
weapons, and an Attack button using real inventory and action slots. Inspect the component,
profiles, and grips through Godot's Inspector/Remote tree. Visual checks have exercised slash,
chop, thrust, crossbow, and casting poses; clip contact fractions remain initial tuning values.

Remaining work is content tuning, bow gameplay and improved presentation for monsters with a single native clip.

Verification as implementation lands: build/import per the Godot skill; targeted GdUnit4
coverage for precedence, missing mappings, shared-resource immutability, scaled contact timing,
exactly-once release, and cancellation. Use engine tests for actual clip lookup. Editor
acceptance must equip contrasting one-/two-handed weapons, an axe and spear, bow and crossbow,
and preview two monster attacks. Check grip, motion, contact/release, recovery, and interruption
in the actual animated scene. Include equipment switching during an attack: the accepted
attack keeps its snapshot or is explicitly cancelled, never changes style halfway through.
Documentation-only verification checks links and consistency; it does not certify clip coverage.

## Verification recorded

- Final full GdUnit4 run: **128 passed**, no failed/skipped tests (3 minutes 14 seconds).
- Focused attack suite: **11 passed**, including preparation interruption and the real
  playground selection/button path. A standalone gameplay probe completed all eleven weapon
  selections, including delayed crossbows and caster projectiles.
- Godot import and ExportRelease build passed; the export build retains the existing Phantom
  Camera warnings. Test runs report orphan-node warnings. Standalone pose/playground CLI
  probes completed their checks but reported Godot resource/shader cleanup errors at shutdown;
  those probe exits are not claimed as clean engine shutdowns.
- Resource references, load-step headers, documentation links, and diff whitespace checks pass.
  The animation-only verification above predates the melee contact delivery; see its plan for subsequent checks. Final contact-fraction tuning remains iterative.
