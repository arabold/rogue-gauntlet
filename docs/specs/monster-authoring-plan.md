# Monster authoring: remaining first-realm acceptance

Status: authoring foundation, basic tactics and roster replacement implemented, October 23, 2026. Remaining work is interactive authoring acceptance and first-three-floor tuning. [Monster authoring](../monster-authoring-design.md) describes implemented contracts and ownership; [Level design](../level-design.md), [Combat](../combat-system.md) and [Randomness](../randomness-and-seeds.md) describe the current execution, encounter and persistence systems.

## Delivered scope

- Five definition-driven actors: retained minion, warrior and crossbowman, approved Studio rat and relocated Forsaken Warden. The Warden has dedicated gameplay/export folders; its Studio category stays `bosses/forsaken_warden`.
- Editable independent melee/projectile example assemblies and an unfinished mobile assembly. Reusable component scenes remain instanced; authored dependencies are explicit Inspector assignments. Empty optional references stay unavailable. Project > Tools > Validate monster scene provides read-only diagnostics with inactive gameplay scripts in the editor.
- Immutable definition/policy Resources own starting health/speed/XP/loot and tactics; actor components/context own mutable state. Defaults initialize before child readiness. No generated missing components, legacy profile attack/defense fields or synthesized default monster attacks remain.
- Close, maintain-distance, attack-then-retreat and optional bounded low-health retreat. One attack policy per actor; shooters never silently bite. Matching normal executor completion starts post-attack retreat; rejected, stale or interrupted executions cannot. Low-health hysteresis prevents indefinite flight. Destination selection uses bounded local navigation/collision/support checks and preserves doorway crossings.
- Independent guard/dodge probabilities with one categorical draw per observed threat; conditional riposte requests the same configured attack after guard release/recovery. No weapon switching, flank planning, group shelter, cover search or boss phases.
- Realm entries have inclusive depth bounds, weights and threat. Pool authoring/identity/affordability checks precede random selection. Rats start at depth 2 and replace finite nest summons. Later depths use an explicit small development fallback. Invalid required Warden configuration reports an error and leaves descent locked.
- Retired legacy gameplay monsters, direct Quaternius game exports, unused profiles and catalog images removed. Approved Studio recipes, local source geometry, technical rigs and historical provenance retained. Rat/Warden exported through Studio; source audit has no missing inputs.
- Save version 13 requires a new run after roster changes. Cleared enemies, nest slots, Warden unlock and claimed source rewards continue using existing deterministic-baseline deltas. Survivor health, position and tactical state reset on reconstruction; no save migration or new survivor snapshots.

The [active catalog](../monster-catalog/README.md) links actual definitions and records starting values. This delivery preserves existing skeleton/Warden tuning where practical; automated success does not establish enjoyable pacing.

## Verification record

Verification exercises real scenes, Inspector-visible properties, initialization order, per-instance state, categorical defense, projectile-only sight/range checks, accepted completion identities, interrupted/stale completions, bounded retreat phases and low-health hysteresis. Encounter suites cover seeded selection, budgets/depth bounds, finite nests, source cleanup without rewards, clearance, gatekeeper unlock/restoration and main-scene generation. Editor-mode validation covers all five actors and both complete examples. Studio export verifies imported art; the source audit passes without the retired game pack.

Builds use the repository's Godot workflow: C# Debug compile, Godot import, targeted GdUnit4 runtime suites and ExportRelease compile. Known Phantom Camera warnings and test-runner shutdown/orphan diagnostics are separate from actual load/script/test failures. Record actual final test results in the delivery report rather than copying a stale count here.

## 1. Interactive authoring acceptance

Use the Godot editor to duplicate the projectile example into a temporary review scene. Assign a unique definition, change health/XP/range, and save referenced Resources with Make Unique/Save As when the change should be local. Add guard with its definition and valid rig animation, assign all component/gate/controller references, then validate. Remove guard, clear references/probabilities and validate again; repeat with dodge. Both attack delivery types must support either optional defense without a new actor class.

Save/reopen the scene to confirm all edits persist. Spawn two instances to check health, cooldown and retreat-state isolation, then remove the temporary fixture. No C# or Studio change should be necessary. Existing automated composition tests support this exercise but do not certify the entire interactive workflow.

## 2. Navigation acceptance

**Reported crossbow issue — open:** the crossbowman can appear stationary when the hero closes in during dungeon play. A GPU flat-arena reproduction does retreat from 3 units to about 4, then resumes shooting. The current policy stops retreat as soon as it crosses the minimum distance instead of continuing to the preferred 7-unit distance. Reproduce the reported generated-room case, inspect action commitment, doorway/clearance and blocked-destination handling, and add a regression for a hero approaching during repeated shots. Consider per-actor retreat hysteresis so a retreat continues toward the preferred distance. The flat probe also emitted an animation-state-machine loop warning; review the retained skeleton transitions during this follow-up. This issue is not resolved by the rat's navigation probe.

Exercise approach, maintain-distance and retreat through doors, walls, corners and Warden-clearance spaces in the real renderer/editor. Confirm:

- A clear shot starts one projectile attack; obstructed shots reposition and never fall back to melee.
- A successful bite finishes recovery, backs away, pauses and re-engages. Hit interruption or a rejected request does not start that cycle.
- Retreat increases separation on supported reachable floor. A blocked retreat holds/attacks when eligible and times out; it does not cross walls, fall into void, oscillate forever or interrupt a committed doorway crossing.
- Health retreat has bounded duration and cannot rearm while still injured. Ordinary retreat grants no dodge immunity.
- The Warden remains within its authored clearance footprint and keeps native scale/contact behavior.

A windowed GPU probe with an authored flat navmesh verified repeated rat bite/retreat/re-engagement cycles and wall-blocked destination rejection without changing the previous target. The lineup confirmed native scale and retained visuals after skeleton emergence completed. This checks local mechanics only: headless baking cannot certify navigation, and generated-room routes still need interactive review.

## Defense timing follow-up

The Warden's guard was wired, but its authored 0.15-second reaction delay used the entire wind-up of a normal 0.5-second hero swing (contact starts at 30%). Delayed threat revalidation then rejected the reaction after wind-up, before any guard pose could start. Its additional 0.15-second raise duration would also have made a block too late. Reaction is now 0.04 seconds and raise is 0.06; its non-parrying window matches that raise. Guard/riposte probabilities, damage reduction and recovery stay unchanged.

A real-scene regression fails with the original timing and passes with the corrected Resources: an ordinary hero swing is observed, the Warden acquires its guard pose, the actual contact blocks, and release/recovery leads into its configured sword riposte. The test selects a deterministic successful probability stream without replacing the authored timings or calling guard/riposte directly. Both idle defense and cancellation of the Warden's own wind-up into guard are covered. A GPU check also confirmed the visibly raised shield, blocked contact and subsequent riposte. This verifies eligible sequences, not sustained-pressure boss balance. Continue reviewing defense opportunities during committed attacks, maximum guard/recovery openings and response frequency in the three-floor playtest.

## 3. Three-floor tuning

Play depths 1–3 with two contrasting classes and three seeds, including mid-run save/resume and floor revisit. Tune Resources rather than Studio JSON or documentation stat tables.

| Floor | Intended experience | Main tuning surfaces |
| --- | --- | --- |
| 1 | Learn direct melee, frontal defense and ranged pressure | Role weights, group size, windups, damage and recovery |
| 2 | Mixed roles, rat bite/retreat and finite nests | Rat timing/speed, nest cadence/slots, room crowding and supplies |
| 3 | Apply learned positioning and defense against the Warden | Mixed encounter budget, boss openings, guard frequency and rewards |

Record unavoidable hits, doorway congestion, time spent chasing, useful escape/defense choices, supply pressure and boss recovery windows. Review the warrior's retained 30 XP/elite loot against the other ordinary enemies' 2–4 XP/common loot before calling progression balanced. Keep fixed authored strength; threat budgets guide selection and do not calculate XP or scale health at runtime.

Verify cleared IDs, killed nest slots, Warden defeat/descent and claimed rewards survive save/load and revisits. Earlier versions must be rejected without conversion or silent overwrite. Update current-system docs and the catalog only when authored behavior/tuning changes.

Wolf, spider, golem, later realms/reordering, shops, player XP progression and boss phases remain separate work. Do not expand the roster until this small authoring/play loop is satisfactory.
