# Monster action architecture

Status: landed on PR #95; only the work below remains. [Monster authoring](../monster-authoring-design.md#actions) describes the implemented system: actions are polymorphic Resources with target selectors, conditions and weight modifiers, chosen by a per-actor runtime; `DashComponent` and `HealthPhaseComponent` are optional capabilities; monster Resources validate themselves. [Combat](../combat-system.md) and [weapon attacks](../weapon-attack-system.md) own the shared executors. The [roadmap](../development-roadmap.md) owns delivery.

Goal for every remaining step: a new monster is a scene plus Resources, and a new kind of behavior is one new Resource class, never an edit to the behavior host, the attack executor or the validator.

## Remaining work

- **Dodge on the dash component.** `DodgeComponent` keeps its own displacement (immunity, charges, steering, pass-through). Moving its travel onto `DashComponent` waits until both have settled in play.
- **Editor badges.** Scene-tree configuration warnings for monsters need `EnemyBase` to become `[Tool]`, which would run gameplay setters against editor placeholders. **Project → Tools → Validate monster scene** stays the editor surface.
- **Phase content.** Phase entry actions (a roar, brief immunity) and per-phase movement or defense overrides wait for a boss that needs them; today phases only gate and weight actions.
- **Spell cast validation.** A spell action's cast definition does not get the attack timing checks (wind-up, threat tier) that attack actions get; add them once the shared cast definitions are tuned against those rules.
- **Consumables.** Plain buff potions and scrolls keep `BuffedItem.ApplyBuff`; an item moves to a spell only when a monster needs the same effect.

## Playtest review

These tuning changes came with the migration; check them in play:

- The Warden reuses the player's Tremor spell, so both shake with `tremor_shake.tres` (0.32 s decay instead of the player's former 0.18 s).
- The Warden uses one repeat penalty (0.35) in every phase instead of 0.25 in phase 3, and its Tremor takes no attack slot, like its swings.
- Close pressure counts from the start of the fight, so a player already hugging the Warden can draw the stomp as soon as phase 2 begins.
- Shooters may fire while closing in when they have sight and their action's range allows it.

## Persistence

Surviving monsters reset on floor reconstruction, so phases, cooldowns and dash state are not saved. Player buff saves add one field, `BuffPath`, naming a refreshing buff's definition (such as venom). `SaveGame.CurrentVersion` is 76 because the sewer storeroom fix changes generated floors.

A combat profile's action list is read when the monster spawns: respawn to apply edits (spawning again from **Monsters → Spawn** picks up Remote-inspector changes).
