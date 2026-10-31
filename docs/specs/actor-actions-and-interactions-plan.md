# Actions and interactions: remaining work

Status: proposed extensions. Shared action ownership, dedicated weapon inputs, four item quick slots, ordinary timed interactions, dodge/guard/parry, monster tactics and mechanical locks already exist. [Combat](../combat-system.md), [items](../stats-and-itemization.md), [monster authoring](../monster-authoring-design.md) and [door access](../door-access.md) describe them; further lock extensions follow the [remaining access plan](secret-doors-and-locks-plan.md). Do not recreate historical defense executors or timers.

[Action design](../actor-actions-and-interactions.md) owns player-facing rules and initial content direction. This plan records only the remaining integration boundaries; the [roadmap](../development-roadmap.md) owns priority.

## Spell weapons and mana

Spell learning, skill points and per-element spell selection are dropped. Wands and staves carry their spell and the actor owns a mana pool; the [magic rework plan](magic-rework-plan.md) owns those contracts, persistence and slices. Invalid casts spend nothing and never fall back to primary; holding secondary cannot repeat spending; all spell effects end when their casting weapon is removed, through the pre-notification inventory release hook in the [spell plan](elemental-spells-plan.md#proposed-contracts). Never save live execution objects or resume a half-finished cast.

## Playable slices and acceptance

1. **Actor world intentions:** reuse existing interaction channels for locally perceived permitted doors/mechanisms, without omniscient key planning or bypassing access gates.

Each implementation slice needs focused logic/runtime checks and an editor exercise through production entry points. Test combat while channeling, contrasting classes, save/resume and actual target placement. Exact controller bindings and durations remain playtest tuning. No expanded spell bar, weapon cycling, stamina/posture, persistent picking progress or additional monster roster is required.
