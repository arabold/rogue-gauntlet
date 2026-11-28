# Actor actions and world interactions

Status: mostly implemented. Dedicated weapon and off-hand inputs, spell weapons, four item quick slots, timed interactions with reservation and a progress ring, and mechanical locks ([door access](door-access.md)) exist. Monster world interactions, levers and an accessibility toggle-to-channel are intended but not built; they are marked below. [Combat](combat-system.md), [weapon attacks](weapon-attack-system.md) and [items](stats-and-itemization.md) own the delivered rules; [dungeon design](dungeon-exploration-design.md) owns which locks and passages exist.

## Purpose and shared rules

Combat, movement, abilities and interactions must feel like one game. An actor chooses an action, commits to its authored time and cost, and gets readable success, cancellation or failure feedback. Player input and monster decisions are different request sources for the same execution rules. Bodies, equipment, tools and authored abilities decide what an actor can do, never hardcoded player/monster or weapon/no-weapon distinctions.

Ordinary movement does not occupy an action slot. An actor owns at most one committed action at a time: primary attack, secondary technique or spell, item use, dodge, guard, world interaction or forced reaction. Each action's policy decides movement and cancellation; being busy is not one universal uninterruptible state, and a cancelled action can never complete later through a stale timer ([action ownership](combat-system.md#action-ownership)).

Melee is horizontal gameplay with reliable timed contact. Armor mitigates contact damage; avoiding confirmed contact takes a deliberate dodge, guard or parry, never a passive roll. Stamina meters, cinematic ripostes, large ability bars and lockpicking minigames are out of scope.

## Player controls and four item quick-action slots

Primary and secondary are dedicated inputs driven by the main-hand weapon: primary is its basic attack, with an unarmed fallback; secondary is its technique, a heavy attack for a martial weapon or the bound spell of a wand or staff. L performs the off hand's action: a shield or makeshift guard with its opening parry window, or the off-hand wand's spell; paired melee weapons and two-handed weapons leave it empty ([hand capabilities](weapon-attack-system.md#equipment-and-hand-capabilities)). None of these uses a quick slot.

| Intent | Input behavior | Keyboard |
| --- | --- | --- |
| Move | Continuous directional input | WASD or arrow keys |
| Primary | Press or hold to repeat complete attacks at their normal cadence | J |
| Secondary technique or spell | One request per new press; cooldown and mana as authored | K |
| Off-hand action | Guard while held, or the off-hand wand's spell | L |
| Dodge | New press; direction from movement or facing | Space |
| Interact | Hold on one focused target until its duration completes | E |
| Item quick slots | One use per new press; never repeats while held | 1–4 |

Bindings live in the project's Input Map; player remapping waits for a settings menu. Keyboard and gamepad are the primary targets: aiming and menus never require a mouse. The gamepad puts the weapon actions on the face buttons (A primary, X secondary, Y off-hand, B dodge), interaction on RB, the quick slots on the D-pad, the camera on the right stick, the floor map on LB, and the run menu on Start (Gear) and View (System); each gameplay button has one action, and menu bindings reuse buttons only while the game is paused. The [README](../README.md#controls) lists every binding. UI input owns its context and must not leak clicks or held buttons into gameplay when a menu closes.

J and K attack even beside a chest, and an interaction press never attacks: there is no tap-to-attack, hold-to-interact multiplexing and no fallback to another action when one fails. Interact with no valid target only gives feedback. A hold interaction completes at most once per press, so it never reopens a door while the button stays down.

## Weapon techniques, spell weapons and classes

Equipment supplies both weapon actions; the character learns nothing. A martial weapon's secondary is its heavy attack with a cooldown and no mana cost. Magic lives on wands and staves, each carrying one elemental spell chosen when the item is rolled and cast at the level its tier sets; there is no universal secondary spell. Class supplies starting stats and equipment, not a magic permission: any class can cast from a found wand, Intelligence sizes the mana pool, and a mage may swing an axe. [Spell weapons](weapon-attack-system.md#spell-weapons-wands-and-staves) owns missiles, casting, mana, cooldowns and focus lifetime, and [elemental spells](weapon-attack-system.md#elemental-spells) owns the roster and targeting.

Player spells and monster magic share `SpellDefinition`, spell effects and their targeting, interruption and contact rules. Wands, staves, scrolls, potions and innate monster actions are activation sources that each supply access, power, targets, timing and costs. A monster casts only an explicitly authored spell action, never one inferred from its species, and not every enemy casts.

## Quick-item assignment and use

`PlayerQuickItemController` owns the four item-only slots. A successful pickup or purchase assigns a quick-usable consumable (`ConsumableItem.AllowQuickUse`) to the lowest-numbered unassigned slot unless it is already represented; failed pickups and a full bar change nothing. Equipment, keys, picks, lights and gold are not consumables and never take a slot. Assignment, clearing and rearranging also happen in the run menu's Pack tab, and auto-assignment never overwrites an assigned slot, even a depleted one.

Interchangeable stacks (potions, scrolls) bind their variety and show the total owned; another pickup of that variety replenishes the count instead of taking a slot, and a variety appears in at most one slot. Count zero keeps a visible depleted binding until cleared. Items with individual state, such as food, bind one instance and are never replaced by another. Names and icons follow the run's [identification](item-identification-system.md) disguises, so a binding never reveals hidden properties.

Each press uses at most one unit through the same validation and inventory transaction as the menu. A targeted scroll opens target selection, and cancelling spends nothing. A missing source, invalid target, busy actor or active cooldown reports a reason and never falls through to another slot or an attack. Quick-use cooldowns belong to the consumable variety, not the slot, so rearranging bindings never resets them. Item use is a committed action with the item's authored use time; the effect and the consumption commit together at completion, and damage or invalid prerequisites before then cancel without consuming the item. A voluntary attack or defense cannot replace an item use in progress.

Monsters have no quick bar: their attacks, abilities and spells request the shared action rules directly, and they do not drink carried potions.

## Timed world interactions

`ActorInteractionComponent` runs one channel per actor: a target-authored duration, a reservation, cancellation and an action-gate lease. There is no separate hold threshold before an action starts.

| Target or verb | Duration and policy |
| --- | --- |
| Open an unlocked door | 0.5 seconds |
| Close a door, open a chest | 1 second |
| Unlock and open with a matching key | 1 second; one key is consumed at completion |
| Pick and open a mechanical lock | 5 seconds; one lockpick is consumed at completion |
| Operate a lever or mechanism | Not built; default 1 second when introduced |
| Pickup, inventory actions, stairs | Their own existing policies, without a hold |

Picking and opening is one compound verb: a successful channel unlocks and opens. With a matching key, the key verb is preferred over picking. A sealed or otherwise ineligible target explains what is missing instead of starting a channel that cannot succeed, and required encounter seals cannot be picked. Cancellation and failed validation consume nothing; there is no random pick breakage. Keys, picks and lock eligibility follow [door access](door-access.md).

### Focus, progress and commitment

One eligible nearby target is focused by visibility, reachable range, facing and distance, with a stable tie-break; never through a wall and never revealing a concealed passage. Its prompt shows the verb and bound input, or the unmet prerequisite, such as a missing key. A fresh hold locks target and verb for the attempt, and a progress ring near the hero fills with accepted progress only.

Starting requires standing still, in range, with the needed capability and tool and no conflicting action; there is no automatic walk to the target. Releasing interact, moving, or a fresh combat or quick-item request cancels the channel first, so the player can abandon a dangerous lock at once but loses the time spent. Health damage, hit stun, death, displacement, lost range or sight, or a target state change also cancel; armor absorbing a hit without a reaction does not. A new attempt needs a fresh press.

Progress resets on cancellation, target change, save/load or floor change. At completion the actor, target, range, sight, capability and tools are validated again and the change commits once: before that boundary nothing opens, unlocks, drops or is consumed; after it, the change stays even if the hero moves during its animation. Only one actor may reserve a target at a time; others see it as busy. Committed changes persist through the existing source and delta saves, so a chest never duplicates or loses its seeded reward.

An accessibility toggle-to-channel may later replace the physical hold, with identical duration, immobility and interruption.

## Monster world capabilities

Interaction permissions (`InteractionPermission`: open doors, open containers, use mechanisms, pick locks) live on an actor's `ActorCapabilities`; only the player carries them today. The intent is per-actor authoring: humanoids may be allowed to open doors, while rats navigate around a door or stay blocked, and a golem that can brace still cannot pick a lock. Breaking doors or smashing containers would be a separate destruction attack, not a substitute for a missing Open action.

Not built yet (issue #44): monsters do not channel interactions. When they do, AI requests use the same channel, reservation, range, tool, completion and interruption rules as the player, holding an interaction intent instead of a button; losing the goal or choosing to fight or flee cancels it. A monster only chooses a locally perceived, reachable interaction consistent with its goal, and doors and seals keep their access and navigation authority: AI never moves a door directly to bypass a lock or duration.

## Persistence and feedback

All action clocks advance in gameplay time and pause with gameplay. Primary, secondary, mana, the four quick slots and defenses show distinct availability and cooldowns; the interaction ring is progress, not a cooldown. Blocked, dodged, parried, interrupted and missing-tool outcomes give distinct feedback, and a failed interaction never looks like success.

Mana, cooldowns and committed costs survive equipment changes, stairs and current-version save/load; loading never refills mana, and lowering capacity clamps it. Loading resumes neutral and releases held input: no half-finished attack, defense window or channel resumes. Quick-slot bindings and variety cooldowns are saved separately from the weapon actions. An in-progress interaction leaves no world-state delta. Enemy revisits follow the current transient-combat reset, and cleared enemies stay cleared.
