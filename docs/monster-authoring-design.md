# Monster design and authoring

Status: implemented authoring system and five-monster roster, October 23, 2026; interactive authoring acceptance and first-three-floor balance remain open. Current encounter and combat behavior is documented in [Level design](level-design.md) and [Combat actions and defense](combat-system.md). The [implementation plan](specs/monster-authoring-plan.md) owns proposed contracts and delivery slices.

## Immediate goal

Make depths 1–3 enjoyable with a small, readable roster: skeleton minion, skeleton warrior, skeleton crossbowman, Plague Rat and Forsaken Warden. The three skeletons are separate gameplay monsters even though they share art conventions and components. Retain their existing models. Re-export approved Studio art without recreating its visuals. Wolf, spider, golem and the rest of the Studio catalog can follow when this workflow works well.

Realm order beyond the first realm remains provisional. [Realms and monster design](realms-and-monster-design.md) owns world themes and floor cadence. A model's availability does not require adding it to the playable roster.

## Authoring ownership

| Surface | Authoritative content |
| --- | --- |
| Asset Studio recipes and exports | Geometry, materials, rig, clips, scale and visual attachments; art metadata and provenance |
| Godot monster scene | Editable component composition, model instance, physical shapes, animation bindings and exported dependencies |
| Godot monster definition and referenced Resources | Stable identity, starting health, speed, XP, loot, perception/roaming profile, attack and tactical policies |
| Realm encounter Resources | Eligible monster scenes, depth bounds, selection weights, threat costs, nest and boss assignments |

Godot Resources and scenes are the gameplay source of truth. Studio JSON contains no health, damage, XP, realm or combat-policy settings. Documentation explains design intent; it is not a second executable stat database. Balance changes must not require exporting art.

## Create a monster in Godot

Duplicate a small starting actor assembly into the monster's own folder. Keep reusable components instanced as scenes, then edit their exported dependencies and Resources in the Inspector. Add or delete optional capabilities in the Scene tree. Avoid deep actor-scene inheritance that makes inherited capabilities difficult to remove. Melee and ranged example assemblies are conveniences, not exclusive actor classes; guarding is independent of attack delivery.

Assign an exported model, collision/hurtbox dimensions, animation profile and visual weapon/socket bindings. Create a monster definition and customize referenced Resources with Make Unique or Save As when the change should affect only this monster. Put the completed scene into an encounter entry and exercise it in the combat harness and a generated floor. No C# or Studio recipe edit is required to compose a monster from supported capabilities.

Templates establish useful defaults; they are not regenerated over customized scenes. A future scaffolding tool may copy these templates, but it must preserve manual edits. Reusable component behavior continues to update through its script/scene. A copied assembly does not automatically acquire future template nodes.

## Independent capabilities and policies

Components execute capabilities; strategy Resources decide when to request them. The shared actor action gate arbitrates all attacks, defenses, stagger and movement restrictions. Strategies cannot bypass it.

| Policy | Initial choices | Essential tuning |
| --- | --- | --- |
| Combat movement | Close distance; maintain distance; attack then retreat | Stop/range bands, retreat distance, bounded retreat duration, re-engagement delay |
| Attack | Melee in reach; projectile with clear sight | One authored attack, damage range, accuracy, critical chance, duration and engagement range |
| Defensive reaction | One probability policy choosing guard, dodge or nothing | Reaction delay, probabilities, projectile response, guard pressure/max hold and conditional riposte |
| Low-health retreat | Optional override of ordinary combat movement | Enter/exit health fractions, desired distance and bounded retreat duration |

Each monster uses one visible weapon or natural attack and one attack policy. Shooters do not silently switch to melee when approached. A shooter without a clear shot repositions using its movement policy; it does not fire through walls. Guard and dodge can accompany either attack type. A shield visual alone does not grant protection; guard also needs the composed capability, definition and valid animation binding.

Guard/dodge probabilities are mutually exclusive outcomes, with the remaining probability meaning no response. Tune reaction delay plus defense startup against actual incoming attack wind-ups: structurally valid components/clips can still respond too late to block. Use a real attacker in the combat harness, including while the defender is winding up its own attack, rather than verifying only direct guard requests. Riposte is a conditional follow-up to successful guard/parry after guard release/recovery, using the monster's same attack policy. It is not another attack type or a weapon swap. A ranged riposte needs clear sight and valid ranged engagement just like an ordinary shot.

Attack-then-retreat begins after an accepted attack finishes recovery normally, whether or not it hit. Rejected or interrupted attacks do not earn a retreat cycle. Ordinary retreat is movement, without dodge immunity. Low-health retreat takes precedence over normal tactics once the current commitment permits movement. Hysteresis and a maximum duration prevent oscillation and indefinite flight. Re-entry requires health recovery above the exit threshold; an injured monster cannot reset the retreat timer every frame.

Retreat means increasing separation where local navigation and geometry permit, not guaranteeing safety. Use existing navigation, collision and doorway rules with bounded destination checks. If no usable retreat destination exists, hold and attempt the configured attack/defense when eligible; never walk through walls or introduce an unbounded search.

Defer flanking, cover selection, group shelter, coordinated packs, weapon switching and boss phases. Establish the strategy pattern with these basic choices first.

## Strength, placement and rewards

Monsters have fixed authored strength. Depth does not silently scale health or damage. Stronger variants get separate definitions/scenes and may reuse models and Resources. Do not add a monster-level field until it has a purpose beyond duplicating depth or threat.

Encounter pools establish realm membership; their entries set minimum/maximum absolute depth, weight and threat cost. A monster may appear in several pools without changing its definition. Reordering realms changes encounter configuration rather than monster identity. Weight determines selection frequency; threat determines budget consumption. Neither substitutes for combat stats.

XP is an explicit reward per definition, tuned against encounter difficulty. Threat can guide tuning but does not calculate XP at runtime. Player level thresholds and stat growth are separate work. Preserve the existing nest rule that source-destruction cleanup awards no XP or loot; ordinary killed finite summons use their authored rewards and existing cleared-slot persistence.

## First-three-floor playtest

| Floor | Encounter purpose |
| --- | --- |
| 1 | Introduce minion approach, warrior guard/recovery and readable crossbow shots in restrained groups |
| 2 | Combine roles, introduce rat pressure and finite nest encounters, retain space to retreat |
| 3 | Test learned responses in mixed encounters and the Warden fight; defeat enables descent |

Tune health, damage, timing, crowd size, rewards and recovery supplies together. Do not copy old speculative bestiary numbers over working content. Start from existing skeleton/Warden tuning and evaluate rats at gameplay scale. The Warden can remain a single-phase encounter for this delivery.

Acceptance requires contrasting player classes and multiple seeds: visible attack windups/releases, useful guard/dodge responses, reachable enemies and boss, no unavoidable doorway pileups, adequate recovery, and a successful save/resume/revisit. Record balance findings and revise the authored Resources. A combat harness pass alone does not establish fun.

## Implemented authoring foundation

All five active monsters use `MonsterDefinition` Resources for initial health, speed, XP, loot and behavior. Minion/warrior/Warden close for melee, the crossbowman maintains distance and only shoots, and the rat bites before retreating. The rat also has a bounded low-health override. Warrior and Warden guard/riposte policies are independent of attack delivery; the warrior also has a dodge capability. Components and each actor's `EnemyTacticsState` retain live state separately from shared Resources. Retired gameplay scenes, legacy profile tuning and runtime attack synthesis have been removed.

Duplicate the independent [melee](../scenes/enemies/templates/melee_enemy.tscn) or [projectile](../scenes/enemies/templates/projectile_enemy.tscn) example in Godot to start an editable actor. Its [instructions](../scenes/enemies/templates/README.md) explain how to customize Resources and use Project > Tools > Validate monster scene. The empty mobile assembly needs a definition, model, shapes and animation setup before it can run. Validation is read-only and works with inactive gameplay scripts in editor mode; it does not generate missing components. Arbitrary live Resource edits remain unsupported.

Authored component dependencies are explicit Inspector assignments. The warrior root references its guard, dodge, defense controller and attack controller; its defense controller references the assigned guard/dodge. Empty optional references stay unavailable to that consumer, even if another component exists in the scene. The controller uses the AI target dynamically; it does not discover defense capabilities at startup or during processing. Validation reports missing required connections.

## Runtime and encounter integration

`EnemyBase._EnterTree` validates authored references and initializes definition defaults before child readiness. Invalid actors are disabled with diagnostics. `EnemyBehaviorComponent` applies strategy intent through perception/navigation and requests the one attack through `AttackController`; every action still uses the shared action gate. The executor reports accepted execution identity and interruption after releasing ownership. Only a normally completed matching attack starts attack-then-retreat. Target loss/death clears transient tactics; low-health retreat cannot rearm without health recovery.

Retreat tries three local away destinations with navigation reachability, increased separation, body sweep, floor support and optional large-actor clearance checks. It preserves doorway crossing commitment. Failed destinations leave navigation unchanged; callers hold and may try the configured attack. Bounded timers still expire. This establishes local retreat, without cover search or group coordination.

The realm factory validates entry bounds, affordability, scenes, authored dependencies and unique monster IDs before consuming selection RNG. The required gatekeeper is also validated before placement; invalid configuration is reported and locks descent. Definitions carry fixed starting strength and explicit XP; inclusive entry depth bounds control availability. Floors 1–3 use the small roster; later depths use the same explicit development fallback until further realms are authored. Save version 13 requires a new run after this roster change; existing cleared-ID, nest-slot and Warden-unlock persistence remain in use.

The active [catalog](monster-catalog/README.md) summarizes shipped definitions. Approved rat/Warden art was re-exported through Studio; the Warden now owns separate gameplay and game-export folders. The old direct Quaternius game pack is retired, while adopted Studio sources and historical provenance remain intact. Studio audit reports no missing source inputs.
