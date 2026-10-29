# Elemental spells and effects

**In progress:** three-level visual prototypes for all four elemental spell sets exist in the workbench. All 20 have initial gameplay components in the combat harness. Learning, mana, equipped-focus lifetime, guard persistence and final visual acceptance remain proposed. Player and monster frozen status is described in [actor status integration](actor-status-integration-plan.md). This plan extends [actions and magic](../actor-actions-and-interactions.md#weapon-techniques-learned-magic-and-classes) and its [integration plan](actor-actions-and-interactions-plan.md#learned-spells-and-mana). Current staff attacks are described in [weapon attacks](../weapon-attack-system.md); project constraints are in [AGENTS.md](../../AGENTS.md), [README](../../README.md), [architecture](../../ARCHITECTURE.md), [GDD](../game-design.md) and [roadmap](../development-roadmap.md).

## Roster

Each element has five peer spells, available to learn in any order. Their first levels should offer comparable overall value, with different tactical roles and costs; free staff primary attacks remain separate. Spend run-level points either to learn a spell at level 1 or upgrade a learned spell to its next level. Starter grants are separate. Spell level is independent of equipment tier. Start with three levels per spell; additional levels, costs and numerical values remain playtest decisions.

| Element | Targeted | Self-centered area | Guard | Character |
| --- | --- | --- | --- | --- |
| Fire | 2 | 2 | 1 | Aggressive damage and reach |
| Earth | 1 | 2 | 2 | Area control and protection |
| Air / lightning | 2 | 2 | 1 | Balanced, emphasizing fast attacks and lightning |
| Water / ice | 1 | 2 | 2 | Defensive counterpart to fire |

Proposed names and roles; each is a separately learnable spell, not a level or prerequisite of another. Protection is an option alongside offense, not a class restriction.

| Element | Targeted spells | Area spells | Guards |
| --- | --- | --- | --- |
| Fire | **Fireball** — quick projectile; **Meteor Shower** — falling impacts | **Flame Burst** — expanding ring of flames; **Solar Flares** — circling flame serpent that ignites enemies | **Ember Guard** |
| Earth | **Rockfall** — falling stones | **Tremor** — quick ground shock; **Rock Spikes** — wider delayed eruption | **Stone Barrier** — rotating stone panels; **Spiked Armor** — close-fitting defensive spikes |
| Air | **Ball Lightning** — fast traveling lightning orb; **Chain Lightning** — arcs between distinct enemies | **Gust** — damaging outward push to escape a crowd; **Thunderstorm** — wider lightning field; the former electric-looking burst shader is reused in the visual preview | **Storm Guard** |
| Water | **Ice Lance** — crystalline strike | **Frost Nova** — expanding icy burst that briefly freezes enemies; **Hailstorm** — lingering storm of ice shards that slows enemies | **Frost Guard** — crystalline shield; **Ice Wall** — ring of ice that holds enemies out for several seconds |

Earth and water guards share the same mechanics but make different capacity, reflection and cost tradeoffs. Earth has more protective choices than air; exact numbers remain tuning. Gust, Frost Nova, Hailstorm, Ice Wall and Solar Flares explicitly author the effects below; other visuals do not add statuses or movement effects by themselves. The visual workbenches simulate guard interactions; the combat harness exercises actual guard capacity and reflection.

**Level growth.** Targeted spells independently increase damage and/or maximum distinct targets. Area spells increase damage and/or radius. Guards have their own hit points and independently increase capacity and/or reflected damage; they have no timer or target limit. Exact growth curves, point costs and mana tradeoffs are playtest decisions. Every spell has a distinct, polished base level and can become visually spectacular at higher levels.

## Targeting, area and guard rules

- **Targeted:** At acceptance, sample distinct living, visible hostile damage owners within the authored radius, in all directions. Deduplicate and order candidates before sampling with gameplay RNG. Capture each identity and position; later movement, death or disappearance does not replace a target. Flights and delayed strikes aim at captured positions and can miss. Each assigned owner resolves at most one contact per cast, including blocked contacts; non-selected owners take no damage. Unused slots may produce cosmetic-only impacts at valid, unobstructed floor points, with a separate cosmetic RNG. No targets is a valid paid cast.
- **Shower exception:** Rockfall and Meteor Shower use 3, 6 and 9 strikes at levels 1–3. Assign strikes across distinct eligible targets before repeating a target; individual strikes may each hit that target once. Additional smaller falling rocks or meteors are cosmetic and never deal damage. With no targets, all placements are cosmetic. Per-strike deduplication replaces cast-wide deduplication for these two spells; all other targeted spells retain the rule above. Counts and timing are preview values subject to tuning.
- **Meteor debris:** Explore incidental damage from smaller, untargeted debris only when a piece physically hits a monster, with less damage than a main meteor. The current extra debris is visual-only; this damage and its contact rules are not implemented.
- **Area:** On release, fix the area center at the caster’s current position. At the authored contact time, affect every eligible hostile owner then within its radius, with no numerical target cap. An actor can enter or leave during a delay; moving the caster does not move the area. Check blocking geometry using the existing combat visibility/obstruction rules; deduplicate damage owners and resolve at most one contact per enemy per cast, including defended contacts. A zero-enemy cast is valid and paid. Visual rings, overlapping layers and cosmetic sparks cannot add hits. An area effect cannot damage the caster or allies.
- **Solar Flares:** A circling flame serpent ignites eligible enemies it touches. The combat harness applies temporary burn damage; its values need tuning.
- **Area boundaries:** Area visuals must remain within the caster's visible area or room: waves, rings, floor cracks, spawned rocks and particles must not appear beyond blocking walls. Apply the same obstruction boundary to the effect's full footprint, not just enemy selection or effect origins. Account for particle travel and geometry that crosses the boundary; do not reduce the entire effect to the distance of the nearest wall. The fire and earth workbenches capture a sampled line-of-sight boundary at cast time and clip their area layers against it. Gameplay integration must account for moving blockers and height differences; gameplay contacts remain governed by the obstruction checks above. Validate with the caster next to a wall, across a doorway and near corners, from viewpoints on both sides.
- **Gust:** Each confirmed, unblocked area contact damages its enemy and applies one outward push from the captured release center, even if armor absorbs all damage. The push is independent of poise break; collision with walls or props can stop displacement. Dodge, parry, block, invulnerability and faction filtering prevent the push. An authored strength controls displacement; a target exactly at the center uses the caster’s captured facing as its direction. Do not apply multiple pushes from overlapping layers.
- **Frost Nova:** Each confirmed, unblocked damaging contact freezes a surviving enemy for an authored short gameplay-time duration. Freeze cancels ongoing voluntary movement, attacks and other actions, prevents new ones, pauses the actor’s pose and hit reactions, and displays an ice-covered version of the actual enemy. Damage can still be dealt and death still resolves normally. On expiry, death, removal or loss of the casting staff, restore the previous movement, action and surface state without restarting a canceled action. Reapplication by the same focus refreshes rather than stacking; separate sources retain independent ownership, so removing one source does not thaw an enemy still frozen by another. Duration and boss-specific resistance are tuning decisions; no immunity is assumed. Other water spells may opt into an authored freeze payload later.
- **Hailstorm:** A lingering storm slows every enemy inside it and drops evenly spread ice shards; each shard hits whoever stands near its landing spot, so enemies can take several hits and moving out avoids them. Its radius, duration, shard rate and damage, slow strength and resistance interactions remain playtest decisions; the visual workbench does not apply gameplay effects.
- **Ice Wall:** A ring of ice crystals erupts around the cast point and stays there for several seconds, even when the caster moves on. Enemies inside the ring are hit and shoved back out, and hit again while they keep pushing in, so it works as a temporary safe space. Its radius, duration, damage, push and repeat interval remain playtest decisions.
- **Guard:** A cast creates a personal pool of shield hit points, not a timed buff. After existing faction, dodge/parry/guard, invulnerability, vulnerability and armor resolution, absorb up to the remaining pool before health damage. Reflect an authored fraction of the actual absorbed amount to the valid living hostile monster attacker through normal damage resolution. An explicitly equipped monster guard uses the same rule against its hostile attacker. Any number of monsters, including the same attacker repeatedly, can trigger reflection while capacity remains. Environmental, missing, invalid or non-hostile sources receive no reflection; dodged, parried, invulnerable and zero-damage contacts consume and reflect nothing. The contact that depletes the shield still reflects its absorbed portion; overflow damages health. The guard ends at zero capacity or on focus loss.
- Mark reflected `DamageContext` so it cannot reflect again. It has no additional poise or knockback; normal target faction, defense and armor rules apply. Resolve the original contact once before the reflected contact. One guard per actor: a successful new guard replaces the old guard at its new full capacity; failed casts do not alter it. No automatic healing or refresh on an equipment/stat change.
- **Focus lifetime:** Every secondary spell effect is tied to the exact equipped staff instance that accepted it. Once that staff is unequipped, replaced, destroyed or otherwise removed, cancel its wind-up and immediately terminate its released projectiles, pending strikes, area effects, guard, reflection in flight, particles and other presentation. Stop further contacts and invalidate delayed callbacks before observers see equipment change; completed damage is not undone. Equipping even another staff of the same element does not restore old effects. A rejected removal changes nothing. Existing action-gate rules may deny a voluntary swap, but any removal that does occur, including forced removal, enforces this rule.
- **Save and travel:** An active guard’s spell ID, level, captured maximum and remaining capacity, captured reflection fraction and source staff instance ID survive current-version save/load and floor travel if that same staff remains equipped. Restore after inventory and progression, validate definition, capacity and matching equipped instance before applying state; reject an invalid save with a clear error. In-flight attacks and transient visuals never resume. Unequipping the staff before saving leaves no guard to restore. Follow the exact-version policy; no migration.

### Frost Nova combat slice (implemented in the combat harness)

`FrostNovaDefinition` is an immutable authored Resource containing per-level radius, damage and freeze duration plus a condition and defense traits. The `FrostNovaSpell` scene exports that definition, the existing nova visual and an explicitly assigned visual root. `Cast(caster, level, random)` validates all inputs before side effects, samples distinct hostile actor roots in the same world at release, checks radius and current `WorldSight` blockers, then resolves one `DamageContext` per target. Only a `Damaged` outcome against a surviving actor with a status controller applies the condition. A cast with no targets still plays the visual. The visual uses the same radius and a cast-local clipping mask; its shader never decides contacts.

The spell node is the source token for its statuses: repeated casts from the same node refresh each target, and `Cancel()` or tree exit removes its own statuses and in-flight visuals. The caller must own one spell node for the equipped focus and cancel it on focus loss; equipment and casting integration remain future work. The combat harness owns one instance and offers a level selector and a button for exercising the real contact path without learning or mana. Its cast button dispatches in the physics callback. Tests cover radius, distinct targets, walls, defense outcomes, lethal hits, source cleanup and normal visual lifetime. Values are temporary tuning.

### Gust combat slice (implemented in the combat harness)

`GustDefinition` is an immutable `[GlobalClass]` Resource with per-level radius, damage, forced-push strength and defense traits. `GustSpell` is a scene-composed Node with exported definition, existing Gust visual scene and visual root; `Cast(Node3D caster, int level, RandomNumberGenerator random)` validates the complete setup before mutation and `Cancel()` invalidates pending contacts and removes its own visuals. Shared `AreaSpellTargets` captures sorted hostile actor roots, range and world, while each release checks current obstruction and resolves at most one contact per owner. Frost Nova uses the same query without changing its freeze rules.

`DamageContext.ForcedPush` carries authored push strength to `HurtBoxComponent`; its unblocked `Damaged` or armor-`Absorbed` contact applies one horizontal push through the explicitly wired movement component. Dodged, blocked, guard-broken, parried, filtered, invulnerable and dead targets do not receive it. It does not depend on poise or ordinary hit-stun knockback; the cast captures its origin and facing for a zero-distance fallback. `MovementComponent` permits externally applied pushes while voluntary motion is status-locked, but death remains terminal. Collisions use the existing character-body movement. The visual timeline receives the release-time area mask and uses the same radius as contacts.

```text
harness button -> physics callback -> GustSpell.Cast -> validate -> capture origin/world/targets -> spawn clipped visual
  -> visible hostile contact -> HurtBoxComponent.ResolveDamage -> defenses/armor -> one forced push -> resolved outcome
Cancel/tree exit -> invalidate remaining synchronous contacts and free the cast's active visuals
```

The combat harness exposes a level selector, release button and a three-monster crowd. Focused Godot tests cover zero targets, radius, hostile filtering, walls, defenses including absorbed hits, exact-center fallback, frozen targets, collision, cancellation and visual masking. Learning, mana and focus-equipment integration remain separate.

### Shared area-cast lifecycle cleanup (implemented)

Each spell node owns an `AreaCastLifecycle` that captures its origin, radius and world, tracks visuals, and schedules contact on the physics clock. `ContactDelay` is authored on each instantaneous area definition; zero resolves immediately. At contact the lifecycle queries current occupants, checks visibility and resolves one contact per owner. Cancellation discards pending contacts and returns tracked visuals; Frost Nova also removes its source-owned statuses. Lingering fields instead query occupants throughout their active lifetime.

```text
spell Cast -> validate its definition -> AreaCastLifecycle.TryCapture -> validate/configure its visual
  -> visibility + spawn -> AreaCastLifecycle.Track -> Resolve -> optional frozen-status response
spell Cancel -> invalidate and snapshot visuals -> remove owned frozen statuses -> free snapshotted visuals
```

The common helper and the three existing spell scripts are the only runtime paths changed; existing Resources, scene exports, Inspector controls, damage, timing, RNG, transitions and teardown remain the same. Verify the existing parameterized area, Gust, Frost Nova and source-ownership tests, including cancellation reentrancy and invalid casts. Keep `ICombatSpell`, `IAreaSpellVisual`, `IStatusActor` and `IStatusRestoreSource`: each expresses a real caller or independent implementation. A larger migration of spell definitions/scene APIs is deferred because it changes authoring rather than just simplifying implementation.

### Shared harness controls and direct area spells (implemented in the combat harness)

`ICombatSpell` exposes a display name, `bool Cast(Node3D caster, int level, RandomNumberGenerator random)` and `Cancel()`. Frost Nova, Gust and a reusable `AreaDamageSpell` implement it. The harness exports an ordered array of explicitly assigned spell nodes, validates them, builds one spell selector and one level selector, and captures their selections when the cast button is pressed. The physics callback dispatches to that selected instance using its own seeded RNG. Exit cancels all configured spells. New spells need scene wiring but no harness-specific buttons or branches.

`AreaDamageSpell` uses an authored `AreaDamageDefinition` Resource for the display name, per-level radius/damage and defense traits. A scene-assigned visual implements `IAreaSpellVisual`, which validates and configures the level/radius before entry and receives the captured visibility mask after entry. Fire's burst and earth's tremor implement this on their existing visual roots. `AreaSpellTargets` shares target capture and contact resolution with Frost Nova and Gust; each cast owns its visuals and invalidates remaining contacts on cancellation. Tremor's workbench-specific wall dust and camera shake remain separate presentation work, not implied by area damage. Flame Burst and Tremor use independently authored definitions with temporary balance values.

```text
cast button -> capture selected spell and level -> physics callback -> ICombatSpell.Cast
  -> validate definition/visual -> capture area and visibility -> spawn visual
  -> AreaSpellTargets -> one damage resolution per unobstructed hostile actor
```

Update the two existing UI tests to use the common controls; one parameterized area test covers both new definitions and their existing visuals. Validate selector dispatch, interruption, walls, harmless no-target casts and scene imports. No learning, mana or focus-equipment wiring is added in this slice.

### Remaining harness gameplay slices (in progress)

Keep the shared selector and focus-owned `ICombatSpell` nodes. Direct area casts reuse `AreaDamageSpell`; a resource-authored contact delay aligns Rock Spikes with its eruption, and cancellation discards pending contacts. Thunderstorm composes its radius-scaled, masked field with lightning arcs and floor cracks at captured enemy and cosmetic positions. Area contacts query occupants and require a clear path at contact time; each receives at most one hit. Damage values are temporary.

The five guards use `MagicGuardSpell`, an authored `MagicGuardDefinition` and an `IGuardVisual` on each existing sustained effect. `IShieldOwner` exposes the actor's Inspector-wired hurtbox; the hurtbox accepts one `ICombatShield`, absorbs after armor and resolves the original contact before asking the shield to reflect. Replacing, depleting or canceling a guard removes its visual and registration. Reflection marks the contact to prevent loops and only targets a living hostile actor. Contacts have an optional `DamageElement`. The test harness owns the spell sources; staff selection, equipment lifetime and guard persistence remain outside this slice.

`TargetedSpell` samples distinct hostile owners and captures positions; showers assign each owner before repeating and use cosmetic-only debris. Fireball uses the existing collision projectile, while traveling effects and falling strikes resolve against the captured location after their flight time. Lightning arcs resolve once per selected target. `LingeringAreaSpell` owns each active field. Solar Flares makes one contact per actor and tests the moving ribbon heads and applies a short, source-owned burn to surviving damaged actors. Burn ticks bypass armor and do not stagger, and source cancellation clears them. Hailstorm applies source-owned slow conditions to every living actor inside the field. Its shards land on an R2 low-discrepancy pattern (`HailPattern`) seeded from the cast, so coverage is even rather than clumped; each lands after the visual's fall time and hits actors within its reach. Shards bypass armor, whose per-hit roll would swallow such small hits, and suppress hit reactions so the storm cannot stun-lock; slowed actors show the shared Chilled appearance. `SlowStatusComponent` reads those conditions and applies the strongest authored movement factor independently of action movement overrides. Leaving, cancellation, death or expiry removes that field's slow without disturbing other sources. Ice Wall's field stays at its cast origin; it hits every enemy inside its ring with an outward forced push and repeats per enemy after `RepelSeconds` while it stays inside. Player and monster Inspector `Slowed` and `Burning` toggles own separate persistent sources; the burning presenter uses the shared fading world-effect host. All numerical values need playtesting.

## Visual direction

Aim for striking Diablo-style spell effects at the gameplay camera, with KayKit's faceted models: low-poly meteors and rocks, layered luminous shaders, particles, lightning and timed local lighting. Pair flowing elemental effects with a small number of faceted, modeled accents: for example, a short broken shock ring beneath a fiery impact. Share the effect’s palette and timing so the shapes feel embedded in the magic. Every level-one spell must already look polished and visually distinct, with clear anticipation, release, impact and decay. As levels rise, expand the same recognizable signature with stronger scale, layered shaders, particles, lighting and impact; the highest level may fill the gameplay screen with an impressive spectacle. Guards need a strong continuous elemental shell, a visible hit flare and an unmistakable reflected trail toward the attacker. Even screen-filling effects must preserve the readability of the player, enemies, hostile telegraphs and critical floor hazards through timing, contrast and controlled opacity. Bound particle/light counts and lifetimes. Review in bright and dark dungeon lighting, in motion, with overlapping effects; measure performance on target hardware.

Reuse or refresh the existing `water_bolt_projectile.tscn`, `air_blade_projectile.tscn`, `earth_shard_projectile.tscn`, muzzle bursts, whirlwind and `MagicRibbonTrail` where appropriate. The current earth `AreaDrop` samples random positions, not enemies. Reuse actor/item effect hosts, anchors and preview camera/navigation where suitable; presentation never decides damage or targeting.

**Toon effect style.** All elemental spell visuals share a KayKit-matched toon language. Element-neutral building blocks live in `scenes/magic/shared/toon/`: noise, ramp and colour helpers (`toon.gdshaderinc`); alpha-eroded particle cards (`toon_card`); velocity-stretched diamond sparks (`toon_spark`); a faceted core that dissolves rather than fades (`toon_core`); a low-poly trailing crown (`toon_crown`); ramp-banded ribbons in a blended body (`toon_ribbon`) or additive glow (`toon_ribbon_glow`) variant; a clear-centred guard and orb shell (`toon_shell`); molten or electric floor cracks (`toon_cracks`); and shared flame, smoke and spark materials. Each element supplies its own hard-banded palettes: fire in `fireball/` (plus shared flame, smoke and spark life ramps), storm and wind in `air/`, ice and water in `water/`; earth keeps its solid rocks, spikes and stone with soft dust and seismic wisps, and only its fractures are toon-crisp. Bodies blend rather than add, so they keep their hue on bright floors; glow is limited to hot bands, lights and lightning, whose bolts, trails, bursts and storm shell (`air/storm_shell`) are additive white cores fading into a blue glow with no dark outline; timeline fades erode or dissolve shapes rather than turning them translucent; and guards, storms and fields keep thin, crisp lines with clear centres so actors stay readable. Fireball impacts flash, burst, cool to smoke and leave a fading scorch laid on the floor below the hit. Fireball, meteor and lightning impacts punch with a first-frame flash card (`toon_flash`) and a brief light spike authored as a `LightCurve` (on `VisualEffectTimeline` or `FireballImpact`); larger, higher-level impacts flash brighter, and `ImpactFlash` rate-limits spikes to about three a second so showers and storms never strobe. Spell lights flicker through `LightFlicker` (`SpellLightFlicker`): a soft wavering for flames and a hard strobe for electricity; `FlickeringSpellLight` does the same for a standalone light. A player-facing reduce-flashing option is still to come with an options menu. Wands and fire staffs fire the same toon Fireball projectile as the spell. Ice reads as opaque faceted crystals that grow in clusters of one tall and several smaller, leaning crystals, and as a cracked ice sheet on the floor (plates and white cracks from `toon_cell_border`); `CrystalField` draws each field as one multimesh, so Frost Guard's outward-bristling shell and Ice Wall's dense ring stay a single draw. Hailstorm keeps its floor still (frost patches that creep in once, then hold) and puts the motion in swirling snow and falling shards; each shard rings its landing spot while it falls, so the hit can be dodged, its shards and marks are particles emitted on request, and each shard bursts into splinters through a sub-emitter as it lands, a few draws however many shards fall.

## Air visual composition slice

The workbench-only air slice composes authored scenes: `VisualEffectTimeline` owns instance-local shader materials, a timed fade and optional sustained lifetime; `LightningArc` draws a seeded cosmetic path between captured endpoints; shared, stateless `DirectTargetStrategy` and `ChainTargetStrategy` Resources choose links from ordered preview positions; area surfaces use the existing `AreaVisibility` mask. Inspector-assigned visual nodes and PackedScenes are required; the workbench owns casts and target fixtures, while spawned scenes own their animation and cleanup. Shared shader Resources remain definitions, with material copies per cast. No component chooses gameplay targets or applies damage.

```text
Air workbench → order distinct visible fixtures → assigned strategy selects direct/chain links → instantiate authored orb or bolt scenes
              → configure endpoints and cosmetic seed → add to tree → timeline duplicates materials and runs
Air workbench → capture area visibility → instantiate Gust/Thunderstorm → bind mask to instance materials
Air workbench → instantiate sustained Storm Guard → preview reflection with a bolt → clear/deplete immediately
```

Implement and verify the shared timeline and arc in the air previews first; migrate existing fire/earth effects only where their lifecycle and rendering match, preserving their accepted visuals. Each timed instance must fade before freeing, sustained guards remain until cleared, and area surfaces and lightning fragments respect the captured mask. A missing required scene is reported in the workbench and produces no partial cast. Compare all five levels 1 and 3 in bright/dark light and next to walls; gameplay damage, movement and equipment integration remain future work. Falling debris and spikes remain in their existing scenes until another concrete effect needs their independent behavior.

## Water and ice visual slice

Reuse `VisualEffectTimeline`, captured `AreaVisibility` and the shared traveling visual scene contract. Extract the existing earth spike mesh construction into a shared faceted-mesh factory; the earth scenes retain their shape and the ice scenes supply their own materials. A composed crystal-field node owns its per-cast mesh instances, growth and clipping; Inspector-authored scenes choose the field shape, duration and water or ice shaders. Ice Lance targets distinct captured positions; Frost Nova grows an icy wave and crystalline eruption within the same sampled boundary. Its workbench-only frozen appearance uses the actor's `WorldEffectHost` and the shared Frozen definition, restores materials and animation on expiry, and does not implement gameplay status. Frost Guard retains crystalline armor; a Hailstorm area preview adds wall-clipped swirling snow, a frosted floor and falling shards, while Ice Wall raises its crystal ring at the gameplay radius at full crystal height. The workbench owns preview lifetimes; separate combat components supply the gameplay behavior described above.

```text
Water workbench → captured distinct positions → authored lance instance → flight → clipped impact
Water workbench → captured area mask → nova wave and crystal field → preview-only ice surface on visible fixtures → restore
Water workbench → hailstorm scene + level → timed, wall-clipped snow, frost and cosmetic shards
Water workbench → authored guard scene + level → sustained crystal guard → simulated reflection or immediate clear
```

Verify earth's extracted geometry retains its existing mesh, water scenes import and render at levels 1 and 3 near walls, target selection remains distinct, materials are per-instance, transient visuals decay and preview freeze always restores. Staff ownership and casting remain proposed gameplay work.

## Proposed contracts

Abbreviated declarations; proposed names, not existing APIs. Authored Resources are shared immutable definitions; runtime contexts and guard capacity belong to the actor/cast. Exported references are assigned in scenes; constructors and Inspector setters do not perform world queries or random rolls.

```csharp
// [GlobalClass] Resource, one script per authored Resource type
partial class SpellDefinition : Resource {
    string Id; MagicElement Element; SpellLevelDefinition[] Levels;
    PackedScene DeliveryScene; PackedScene CastEffectScene;
}
partial class SpellLevelDefinition : Resource {
    float DamageScale; int MaxTargets; float Radius; // target count only for targeted delivery
    AttackDefinition Attack; GuardDefinition Guard;
}
partial class GuardDefinition : Resource {
    float Capacity; float ReflectionFraction;
    PackedScene SustainedEffectScene; PackedScene ReflectionEffectScene;
}
// Runtime-only C# state
record SpellTarget(Node Owner, Vector3 CapturedPosition);
class SpellCastContext { /* captured focus instance, power, selected owners and resolved owners */ }
partial class SpellEffectOwner : Node { void EndForFocus(Item focus); }
// Saved only for an active guard, alongside character spell state.
class GuardSaveData { string SpellId; int Level; string StaffInstanceId; float RemainingCapacity; float MaxCapacity; float ReflectionFraction; }
// Actor-local Node; HurtBoxComponent and effect host are explicitly assigned
partial class SpellGuardComponent : Node {
    bool TryActivate(GuardDefinition definition, float capturedPower);
    GuardAbsorption Absorb(in DamageContext contact, float postArmorDamage);
    void Clear();
}
```

Each level references an attack or a `GuardDefinition` and self delivery; guard levels have no target cap. Learning stores level 1 and upgrading advances exactly one level after validating points and the authored maximum. Reject unknown, unlearned, maximum-level or unaffordable upgrades without mutation; spend points and update level atomically, then notify locally. Capture the learned level and resolved values on accepted casts; later upgrades affect subsequent casts only. Validate unique IDs, element, ordered nonempty levels, finite nonnegative damage, positive attack radius, targeted-only target cap or guard capacity, nonnegative reflection fraction, and required scenes before accepting; a failed request mutates nothing and emits no success notification. `SpellGuardComponent` retains per-activation values, never mutates the shared definition, and emits local presentation notifications only after state changes. Frost Nova already applies its area freeze through the combat-harness spell component and actor-local status adapter; retain that behavior when integrating equipped casting. Coordinate with the [attack-status plan](attack-status-effects-plan.md), whose proposed first slice excludes area statuses and currently targets player-only buffs. Do not route area freeze through its direct-hit-only payload. Frozen surface presentation must use [shared surface ownership](../world-effects.md#surface-ownership-and-visibility), preserving cloak, other skins, equipment and death cleanup. Reflected damage must enter `ICombatDamageable.ResolveDamage` with a new reflection marker; the existing `DamageResolved` signal lacks attacker/context and cannot implement this safely by itself. Inspect impact on every `DamageContext` producer and noncombat `IDamageable` before choosing the exact marker representation. A caster-local `SpellEffectOwner` tracks pending and released effects by exact focus instance. It invalidates them synchronously on a successful equipment removal, including canceling timers, hiding visuals and disabling contact before deferred node cleanup. No global registry.

`Inventory.Release` currently clears the slot and emits `ItemUnequipped`; `PlayerInventoryController.OnItemUnequipped` then emits `SignalBus.ItemUnequipped`. Do not rely on subscriber ordering. Add an actor-injected, ordinary C# pre-notification release callback on the runtime `Inventory`, invoked after successful preflight and slot removal but before `ItemUnequipped` and `Changed`. The controller registers/unregisters it with its inventory lifecycle and calls `SpellEffectOwner.EndForFocus` for the released weapon-hand item. This covers swaps, drops, destruction and forced releases through `Release`; it must not run on failed cursed removal or an in-place equipment change. Inventory restore suppresses creating new effects while old focus effects are ended, then restores the validated guard after the exact equipped instance is in place. Test every removal path and notification ordering.

```text
Secondary intent → action resolver validates learned spell, focus, mana, cooldown and authored dependencies
  → targeted delivery captures owners/positions; area and guard prepare self-centered delivery
  → action accepted: spend mana and start cooldown once; interruption prevents release
  → release: attack nodes share cast context OR guard component activates definition
  → targeted contact: assigned owner + cast-wide deduplication (per-strike for showers) → existing damage resolution
  → area release: query all eligible owners in caster-centered radius → deduplicate → damage once
      → Gust: apply outward push on eligible contact; Frost Nova: apply owned freeze on eligible damage
  → guard contact: HurtBoxComponent defense and armor → absorb → health/reaction → reflected contact (marked)
  → local effects observe committed changes
Successful focus removal → invalidate and stop every associated delivery, guard and visual before equipment notification
Save/load → restore equipped staff then active guard snapshot and capacity; discard in-flight casts
```

`HurtBoxComponent.ResolveDamageInternal` currently applies armor, emits `DamageTaken`, applies health, then reacts; insert absorption at that boundary and explicitly define outcome/poise for a fully absorbed hit during the guard slice. Its existing `DamageResolution` and traces must accurately report shield absorption and remaining health damage. Never infer reflectable ownership from the projectile node alone; use `DamageContext.OwnerNode` and faction.

## Affected paths

```text
+ scenes/magic/                         spell/target/guard definitions, deliveries and authored effects
~ scenes/attacks/                       AttackController, PreparedAttack, Projectile: captured casts and deduplication
~ scenes/components/HurtBoxComponent.cs damage ordering, area payload and guard integration
~ common/combat/                        DamageContext reflection marker and DamageResolution contract
~ scenes/enemies/ and scenes/buffs/     actor-local freeze, action interruption and expiry
~ scenes/effects/world/                ice surface presentation using shared appearance ownership
~ scenes/player/Inventory.cs           pre-notification release hook for the actor-owned focus
~ scenes/player/ and player.tscn        spell selection, mana, focus ownership and explicitly wired guard component
~ scenes/debug/effects/                 spell workbench and shared tab navigation
~ scripts/saves/                        spell IDs/levels, points, mana, cooldowns and active guard snapshot/source; version bump
+ tests/                                targeting, contact, guard, persistence and harness coverage
~ docs/weapon-attack-system.md          implemented behavior when delivered
```

The action plan owns exact learning/UI and save integration. Preserve free primary attacks and existing equipment throughout. Reject incompatible saves per [save policy](../randomness-and-seeds.md#save-compatibility-and-acceptance), without migration or overwrite.

## Delivery and checks

Hybrid rollout: prove the highest-risk contact and visual boundaries, then deliver playable elemental slices.

1. **Workbench and contact proof:** production targeting and delivery in a shared-navigation spell harness with 0–5 independently placed real targets, moving targets, obstacles, reset, seed/replay, slow motion, lighting and gameplay camera. Test unique selection, targeted caps, uncapped area contacts, Gust displacement and collision, Frost Nova freeze/restore, overlapping impacts, harmless fillers and focus removal before and after release. No progression UI yet.
2. **Playable fire:** action-plan mana, learning/selection and save state; Fireball and Meteor Shower, at independently learnable base levels, with distinct damage/target growth curves. Test independent learning, point spending, invalid/max-level upgrades, both growth curves, captured cast levels, interruption, staff swaps, forced removal and level/ledger save-load for both classes. Existing primaries remain usable; remaining fire spells are unavailable.
3. **Guard and remaining fire:** prove shield ordering and reflection against the real damage pipeline, including full/partial absorption, many and repeated attackers, blocked/parried hits, missing attackers, death, recursion, replacement, depletion, focus removal and save/load. Deliver Ember Guard and remaining fire spells; compare polished base and spectacular highest-level sustained and reflected visuals. Other elements retain their current attacks.
4. **Earth, water and air:** deliver their complete spell sets and reusable drops/arcs/guards. Verify level-specific target caps, damage growth and guard variants; compare every spell at its base and highest levels in the harness and gameplay, including screen-filling readability; tune balance and performance.

Each slice runs `dotnet build "Rogue Gauntlet.sln"`, focused GdUnit4 tests with `GODOT_BIN` and `.runsettings`, and the [Godot MCP workflow](../../.agents/skills/godot-mcp/SKILL.md) for scene import, resource validation and gameplay smoke checks. In the editor, create and assign a definition, inspect runtime isolation and exercise the production cast path. Visual quality and target-hardware performance require manual acceptance; passing tests do not establish either.

Open for playtesting: numeric balance, Gust push strength, freeze duration and boss resistance, whether further levels are needed, point costs, no-target feedback, full-absorption poise/reaction, and final effect budgets. Verify the engine's collision behavior and reflected-contact defense semantics before locking the delivery implementation.
