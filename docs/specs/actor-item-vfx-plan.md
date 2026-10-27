# Actor and item VFX — remaining acceptance

Status: **implementation delivered; visual acceptance remains**. The reusable host/lifecycle, shared actor surface ownership, authored effect scenes, expanded workbench, healing/strength consumption wiring, all-scroll reading feedback, teleport departure/arrival and monster arrivals, and known-cursed item presentation are described in [world effects](../world-effects.md). Implementation details now live in source rather than the original plan.

## Catalog-wide activation feedback

The expanded requirement is one recognizable activation signature for every potion family and scroll type, optionally using world and screen channels. The [consumable feedback plan](consumable-feedback-plan.md) covers all 9 potion families and 11 scroll types, shared size variants, committed-use routing, independent status lifetimes and harness coverage. Shared reading confirmation now covers all scrolls; distinct result signatures for every consumable remain proposed.

The approved scroll ring is shared by production consumption and the [scroll preview](../world-effects.md#scroll-reading-preview). Its assigned glyph/color and smooth fade are the selected direction; older alternatives are removed.

## Remaining review

- Review iron and stone on the player, skeleton and Warden at the gameplay camera, in motion and in both lighting presets. Choose the preferred invulnerability symbol; retain the alternate as a selectable definition.
- Review the authored looks against the requested Diablo 3/4 quality reference. Tune layer silhouettes, timing, texture detail and brightness together. No passing test or particle-count increase alone establishes AAA quality.
- Complete GPU timing on the intended hardware/resolution before expanding crowd density. Godot's Metal GPU-time readout returns zero on the current machine, so it cannot supply the original GPU-budget acceptance. Retain bounded particles, draw calls, burst copies and tails meanwhile.
- Playtest effects alongside dungeon fog, hazards, loot, x-rays and full-screen overlays, including rapid gear changes, known-cursed items and deaths during transitions. The focused workbench and runtime tests support these exercises; they do not establish combat readability by themselves.

## Separate gameplay and spell work

Invulnerability rules, blessing bonuses/duration, monster buffs, magical-potion semantics and automatic leveling remain with their gameplay systems. Add a condition-to-sustained-effect adapter with the first actual mechanic; use `BuffController.HasCondition`/`BuffsChanged` and reconcile rather than replaying activation. No fake conditions or new buff mechanics are required for visual previews.

Future healing scrolls/fountains can use the recipient healing definition after a committed successful operation. Spell work can reuse scene layers, cosmetic seeds, hosts and anchors; add trajectory, aim, collision and hit-timing fixtures when actual spell content requires them. VFX must not own damage, mana or targeting.

Continue using the [program-design skill](../../.agents/skills/program-design/SKILL.md) when a future mechanic requires substantial contracts. No save migration or persisted particle state is planned.

## Future spell preview

Add a dedicated spell scene to the shared navigation's authored titles/paths, reusing the camera and navigation components. Keep effect selection and playback controls consistent with the current previews. The spell field should stage the player plus zero to four independently placed monster targets for casting/contact tests; those are gameplay targets, not comparison subjects. Spell trajectory, collision, damage, targeting and target reset behavior belong to that scene and the actual spell systems when implemented. Do not restore crowd/combination/item-fixture controls to the effects viewer or introduce a global harness registry.
