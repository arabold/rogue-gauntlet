# Actions and interactions: remaining work

Status: proposed extensions. Shared action ownership, dedicated weapon inputs, four item quick slots, ordinary timed interactions, dodge/guard/parry and monster tactics already exist. [Combat](../combat-system.md), [items](../stats-and-itemization.md) and [monster authoring](../monster-authoring-design.md) describe them. Do not recreate historical defense executors or timers.

[Action design](../actor-actions-and-interactions.md) owns player-facing rules and initial content direction. This plan records only the remaining integration boundaries; the [roadmap](../development-roadmap.md) owns priority.

## Mechanical locks

The implemented [door access system](../door-access.md) supplies optional locks, consumable keys/picks, final actor-context validation and saved state. The existing actor interaction component remains the sole channel/progress owner, with one-second key/open and authored pick/open durations. Further lock extensions follow the [remaining access plan](secret-doors-and-locks-plan.md). Required encounter seals cannot be picked.

Validate tools, target revision, range and reservation at completion. Cancellation commits nothing and retains no partial progress. Key/tool loss, wrong keys, interruption and repeated input cannot unlock or reward twice. Actor permissions remain explicit; a rat does not gain door or picking capability by proximity.

## Learned spells and mana

Retain the current staff/attack executor, replacing hardcoded secondary selection with explicit authored techniques and learned compatible spells. Proposed authoring lives under `scenes/magic/` and `scenes/actions/`; actor-local mana belongs under `scenes/combat/`.

| Proposed owner | Responsibility |
| --- | --- |
| Technique, spell and targeting Resources | Stable IDs, element, cost/cooldown, attack/effect and target rules; no live state |
| Elemental skill-tree Resources | Unlock costs and prerequisite IDs; unique IDs, matching elements and acyclic prerequisites |
| Spell progression component | Learned IDs, granted/unspent point ledger and one selected spell per element |
| Mana component | Current/capacity/regeneration, atomic spend and explicit restore |
| Equipped-action resolver | Current primary/secondary availability; no class-name permission check |
| Released area-effect scene | Captured power/target and its own lifetime/pulses, never a second cast clock |

Learning/selection requests reject invalid IDs, prerequisites, affordability or unsafe loadout changes without mutation. Class starter knowledge is separate from earned points; loading cannot grant either twice. Staff copies share elemental selection and character-owned cooldown identity. Expose editor-assigned catalogs and simple harness commands rather than a global registry.

```text
Fresh secondary intent
  → resolve equipment, learned selection and compatible focus
  → validate animation, target, action availability, mana and cooldown
  → accept once: existing action ownership + mana debit + cooldown
  → existing attack clock releases captured projectile/area/buff
  → finish or interrupt; accepted costs are retained
```

Invalid requests spend nothing and never fall back to primary. Primary staff attacks remain free. Holding secondary cannot repeat spending. Source incompatibility cancels startup; active/recovery denies voluntary swaps. Released effects retain captured power/position; focus-dependent channels stop on focus loss. Equipment or capacity changes cannot refill mana or reset cooldowns.

Extend current save capture with learned IDs, point ledger, elemental selections, mana and remaining cooldowns. Never save live execution objects or resume a half-finished cast. Follow the exact-version policy in [randomness and saves](../randomness-and-seeds.md#save-compatibility-and-acceptance).

## Playable slices and acceptance

1. **Fire vertical slice:** one staff family, free primary, learned Fireball and Wall of Fire, mana, selection and keyboard/gamepad-compatible preview/target capture. Both barbarian and mage may learn/cast with a matching staff. Verify rejection without cost, exactly-once spending, interrupted costs, no reload/weapon-copy exploits and no target tracking after capture.
2. **Optional locks:** coordinate the shared authority above with dungeon access. Verify one accepted commit, no partial progress and current-version round trips.
3. **Actor world intentions:** reuse existing interaction channels for locally perceived permitted doors/mechanisms, without omniscient key planning or bypassing access gates.
4. **Four-element content:** only after the fire slice is useful, complete the five-node trees and distinct effects from the concept. Physical stone/ice walls require actor-clear placement, working runtime navigation and protected door/stair routes before rollout.

Each implementation slice needs focused logic/runtime checks and an editor exercise through production entry points. Test combat while channeling, contrasting classes, save/resume and actual target placement. Exact mana costs, spell power, unlock cadence, controller bindings and durations remain playtest tuning. No expanded spell bar, weapon cycling, stamina/posture, persistent picking progress or additional monster roster is required.
