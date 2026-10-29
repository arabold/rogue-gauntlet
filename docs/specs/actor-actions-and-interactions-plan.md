# Actions and interactions: remaining work

Status: proposed extensions. Shared action ownership, dedicated weapon inputs, four item quick slots, ordinary timed interactions, dodge/guard/parry, monster tactics and mechanical locks already exist. [Combat](../combat-system.md), [items](../stats-and-itemization.md), [monster authoring](../monster-authoring-design.md) and [door access](../door-access.md) describe them; further lock extensions follow the [remaining access plan](secret-doors-and-locks-plan.md). Do not recreate historical defense executors or timers.

[Action design](../actor-actions-and-interactions.md) owns player-facing rules and initial content direction. This plan records only the remaining integration boundaries; the [roadmap](../development-roadmap.md) owns priority.

## Learned spells and mana

Retain the current staff/attack executor, replacing hardcoded secondary selection with explicit authored techniques and learned compatible spells. Proposed authoring lives under `scenes/magic/` and `scenes/actions/`; actor-local mana belongs under `scenes/combat/`.

| Proposed owner | Responsibility |
| --- | --- |
| Technique, spell and targeting Resources | Stable IDs, element, cost/cooldown, attack/effect and target rules; no live state |
| Elemental spell catalogs and level Resources | Five independent spells per element; ordered authored levels and point costs, unique IDs and matching elements |
| Spell progression component | Learned IDs and levels, granted/unspent point ledger and one selected spell per element |
| Mana component | Current/capacity/regeneration, atomic spend and explicit restore |
| Equipped-action resolver | Current primary/secondary availability; no class-name permission check |
| Caster-local spell effect owner | Tracks active deliveries and guards by exact focus instance; synchronously invalidates them on removal |

Learning/selection requests reject invalid IDs, unlearned or maximum-level upgrades, affordability or unsafe loadout changes without mutation. Class starter knowledge is separate from earned points; loading cannot grant either twice. Staff copies share elemental selection and character-owned cooldown identity. Expose editor-assigned catalogs and simple harness commands rather than a global registry.

```text
Fresh secondary intent
  → resolve equipment, learned selection and compatible focus
  → validate animation, target, action availability, mana and cooldown
  → accept once: existing action ownership + mana debit + cooldown
  → existing attack clock releases captured projectile/area/buff
  → focus removal: invalidate all associated casts, effects and guard before equipment notifications
  → finish or interrupt; accepted costs are retained
```

Invalid requests spend nothing and never fall back to primary. Primary staff attacks remain free. Holding secondary cannot repeat spending. Source incompatibility cancels startup; active/recovery denies voluntary swaps. All spell effects, including released projectiles, areas and guards, terminate immediately when their original staff is unequipped or removed; a same-element replacement cannot retain them. Stop contacts and invalidate callbacks synchronously through the pre-notification inventory release hook in the [spell plan](elemental-spells-plan.md#proposed-contracts). Equipment or capacity changes cannot refill mana or reset cooldowns.

Extend current save capture with learned IDs and levels, point ledger, elemental selections, mana, remaining cooldowns and active guard capacity plus its source staff instance. Restore a guard only after validating and restoring that exact equipped staff; no guard persists after removal. Never save live execution objects or resume a half-finished cast. Follow the exact-version policy in [randomness and saves](../randomness-and-seeds.md#save-compatibility-and-acceptance).

## Playable slices and acceptance

1. **Fire vertical slice:** one staff family, free primary, independently learnable and upgradeable Fireball and Meteor Shower, mana, selection and surrounding-enemy target capture. Both barbarian and mage may learn/cast with a matching staff. Verify rejection without cost, exactly-once spending, interrupted costs, no reload/weapon-copy exploits and no target tracking after capture.
2. **Actor world intentions:** reuse existing interaction channels for locally perceived permitted doors/mechanisms, without omniscient key planning or bypassing access gates.
3. **Four-element content:** only after the fire slice is useful, complete the five-spell catalogs and distinct effects from the concept. The [spell plan](elemental-spells-plan.md) owns the revised roster, targeting, Gust push, Frost Nova freeze, visuals and staged delivery; area freeze requires its own status payload beyond the direct-hit-only [attack-status plan](attack-status-effects-plan.md).

Each implementation slice needs focused logic/runtime checks and an editor exercise through production entry points. Test combat while channeling, contrasting classes, save/resume and actual target placement. Exact mana costs, spell power, unlock cadence, controller bindings and durations remain playtest tuning. No expanded spell bar, weapon cycling, stamina/posture, persistent picking progress or additional monster roster is required.
