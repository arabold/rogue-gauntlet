# Combat redesign: remaining work

Status: all five slices are implemented: poise and hit-stop, committed monster wind-ups, rear and sneak hits, dodging through monsters, attack slots with an engagement ring, threat tiers with guard break and telegraphs, strike-aligned contact windows, and the player kit (primary combos, the input buffer, recovery cancels and dodge charges). [Combat actions and defense](../combat-system.md) and [weapon attacks](../weapon-attack-system.md) describe them; [monster authoring](../monster-authoring-design.md) covers their authoring. This plan records only what remains: a Warden guard-breaking attack and guard walking. The [roadmap](../development-roadmap.md) owns priority. When both land, move their rules into those documents and remove this plan.

## Goal

Every exchange follows **telegraph → respond → punish**, with the existing dedicated inputs and no jump, separate parry button or lock-on.

## Warden guard-breaking attack

The Warden's Normal slash supports block/counter play. Its proposed Heavy overhead, multiple action choices and validation now belong to the [monster actions and boss phases plan](monster-actions-and-boss-phases-plan.md), which also covers stomp, health phases, rush and casting. Keep one source of implementation decisions there. Guard walking remains independent below.

## Guard walking

`GuardDefinition.MovementSpeedMultiplier` already lets a guarding actor move, but the guard pose holds the `Blocking` clip, so a moving guard would slide. The KayKit rigs have no guarded walk. Keep the multiplier at 0 until a matching clip exists, for example one authored from the upper body of `Blocking` and the lower body of a walk cycle. Then:

- Add an optional guard-walk clip to `DefenseAnimationProfile` and switch the guard pose to it while the actor moves, sampled from the movement clock.
- Raise `MovementSpeedMultiplier` on `weapon_guard.tres` and `shield_guard.tres` (start at 0.4) only for rigs that have the clip.
- Verify in the editor that the walk reads as guarded and that facing still covers the guarded sector.

## Delivery and verification

Run `dotnet build "Rogue Gauntlet.sln"`, the targeted GdUnit4 tests, a harness scenario under `scenes/debug/combat/scenarios/`, a headless `main.tscn` smoke and an editor playtest for feel.

## Open decisions and risks

- **Player kit tuning.** Fresh presses cancel recovery for the first two combo steps, so timed presses raise damage per second well above the old single attack, while holding the button stays close to it. Player contact windows now cover only the visible strike, which is shorter than the old 0.3–0.65 default. Playtest both against current monster health and reach before retuning.
- **Charged secondary and depth-scaled attack slots.** Deferred until playtests show a need.
- **Playtest review of the implemented slices.** Engagement-ring navigation and ring spacing in generated rooms, dodging through monsters next to walls, sneak-attack frequency, Warden guard-break and riposte pacing, and whether flinches read clearly enough. Delivered attack execution, feedback and harness behavior are documented in [combat actions and defense](../combat-system.md), [weapon attacks](../weapon-attack-system.md) and the [combat harness](../combat-harness.md). Headless runs cannot bake a navmesh, so ring movement needs the editor.
- **Further multi-attack content.** After the Warden slam, the same attack selection serves Unblockable area monsters and other tier mixes.
- **Player rear vulnerability.** The player has no vulnerability profile; revisit if being surrounded is not dangerous enough.
