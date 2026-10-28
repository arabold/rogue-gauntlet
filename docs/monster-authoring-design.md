# Monster design and authoring

The active roster is Skeleton Minion, Skeleton Warrior, Skeleton Crossbowman, Plague Rat and Forsaken Warden. See the [gameplay monster catalog](monster-catalog/README.md) for definitions, the [Asset Studio character catalog](../asset-studio/catalogs/characters/README.md) for published art and previews, and the [level system](level-design.md#first-realm-encounter-prototype) for encounter integration. The priority remains enjoyable depths 1–3; later realm order and additional monsters are provisional.

## Authoring ownership

| Surface | Owns |
| --- | --- |
| Asset Studio | Visuals, rig, clips, attachments and provenance |
| Godot monster scene | Editable component composition, model, shapes, animations and explicit dependencies |
| Monster definition and policy Resources | Starting stats, XP, loot, perception and combat decisions |
| Realm encounter Resources | Depth eligibility, weights, threat budgets, nest and boss assignments |

Balance changes belong in Godot, not Studio JSON or documentation stat tables. A model's availability does not require adding it to encounters. Shared Resources are definitions; health, timers, targets and execution state belong to each actor.

## Create a monster in Godot

Duplicate the independent [melee or projectile assembly](../scenes/enemies/templates/README.md) into the monster's own folder. Keep reusable component scenes instanced; add/delete optional capabilities in the Scene tree. Assign model, shapes, animation bindings, definition and all exported dependencies explicitly. Use Make Unique or Save As for Resources that should vary locally.

Empty optional references mean unavailable, even when a matching child exists elsewhere. A shield mesh alone does not grant guard. Templates are starting points, not generators that overwrite customized actors; copied assemblies do not automatically acquire future template nodes.

Use **Project → Tools → Validate monster scene**, then exercise the actor in the [combat harness](combat-harness.md) and a generated floor. Save/reopen it and spawn two instances to check independent state. Arbitrary live Resource edits are not supported; reinstantiate to apply policy changes.

## Independent capabilities and policies

Components execute capabilities; strategies request them; the [shared action gate](combat-system.md#action-ownership) decides whether they may start. Guard/dodge can accompany either attack type.

| Policy | Supported behavior |
| --- | --- |
| Movement | Close distance, engagement ring (wait for an attack slot around the player), maintain distance, attack then retreat |
| Attack | One melee or clear-sight projectile attack using one visible weapon/natural attack |
| Defense | One categorical guard/dodge/no-response choice per observed threat |
| Low-health override | Optional retreat with duration bounds and health hysteresis |

Crossbowmen never silently switch to melee. A blocked or unavailable shot repositions; it cannot fire through walls. Rat attack-then-retreat starts only after normal completion of its accepted bite, not a rejected or interrupted attack. Ordinary retreat has no dodge immunity.

The tactical policy bounds each retreat opportunity, delays retries and can require an intervening accepted shot attempt before retreating again. Point-blank pressure favors fighting; blocked escape yields to an eligible shot or approach. No rule guarantees safety or bypasses committed attacks, defenses, doorway crossings or collision. Low-health retreat cannot rearm repeatedly while the actor remains injured.

Guard/dodge are mutually exclusive probability outcomes. Reaction delay and defense startup must fit incoming wind-ups. Guard pressure, maximum hold, cooldown and release recovery create openings; riposte requests the same attack only after that sequence, with range/sight checked again. The Warden guards with a brief parry window and conditionally ripostes; the warrior also supports dodge. Both use ordinary combat rules. Large actors author movement clearance separately from their larger spawn staging clearance; the Warden stays in his home room while pursuing.

Assign `AttackController.ActionGate` to the actor's gate; validation reports a missing one, and without it attacks lose phases and hyper-armor. Leave the gate's `RecoveryCancels` off: a monster's attack recovery is the player's punish window. `enemy_base.tscn`, the crossbowman and the templates wire a `DamageNumbers` node (hurtbox, `damage_number.tscn`, the health bar as anchor, the invisibility component); keep it in new monster scenes. Large monsters set `MovementComponent.MaxTurnRate` and a melee `MaxStartAngle` so the player can [get behind them](combat-system.md#enemy-decisions-and-persistence). Monsters take turns through the player's [attack slots](combat-system.md#attack-slots); a boss policy sets `UsesAttackSlot = false`. An engagement ring must lie outside the attack's engagement distance. Hurtboxes share `humanoid_vulnerability.tres` for [rear and sneak hits](combat-system.md#facing-and-sneak-attacks) through `enemy_base.tscn`, the crossbowman and the templates. Heavy or Unblockable melee needs an assigned `AttackTelegraph` and a tier-length wind-up; place contact windows on the visible strike with the clip sampler described in [weapon attacks](weapon-attack-system.md#attack-styles-rig-profiles-and-timing).

Hit reactions come from an optional `PoiseComponent` assigned to the hurtbox and bound to the actor's gate. Leave it empty for actors that every hit should interrupt (the rat). Validation reports a foreign or unbound component, an invalid definition, and a floating bar showing another actor's meter. Melee attacks author a facing lock lead within the wind-up and a wind-up turn rate, so slow turners such as the Warden can be sidestepped. See [poise](combat-system.md#poise-and-hit-reactions).

Group shelter, cover search, weapon switching, multiple attacks per monster and boss phases remain deferred.

## Awareness and pursuit

Acquisition requires navigation reachability and unobstructed sight. Long-range detection is directional; close detection ignores facing but still requires sight, preventing hearing through walls or closed doors. Sight rays use the authored eye height.

An acquired target remains pursued while reachable, even around corners without sight. Breaking the path starts a search at the last reachable position; losing sight alone does not end a valid chase. Doorway crossings retain their commitment. Defense observes visible threats, never input presses or hidden future state.

The FSM owns awareness/pursuit, movement strategies express preferences and a per-actor tactical runtime bounds local choices. Existing navigation supplies reachable destinations; this is not a behavior-tree framework or a global planning service. Tactical decision traces are available through the [harness](combat-harness.md).

## Strength, placement and rewards

Strength is fixed authored data; depth does not silently scale health or damage. Stronger variants can reuse art but get separate definitions. Encounter entries set inclusive depth bounds, frequency and threat cost. XP is an explicit reward, not calculated from threat, and player leveling remains separate work.

Invalid required gatekeeper configuration leaves descent locked with diagnostics. Cleared entities, finite nest slots and boss defeat use existing floor deltas; surviving actors reset on reconstruction. [Randomness and saves](randomness-and-seeds.md) owns those limits.

The [roadmap](development-roadmap.md#first-three-floor-acceptance) tracks generated-room movement, authoring acceptance and balance. In particular, review pressured crossbow shooting, Warden guard/recovery openings, skeleton animation transitions and the warrior's reward profile before expanding the roster.
