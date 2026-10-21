# Actor Actions and World Interactions

Status: intended gameplay, October 21, 2026; proposed, not implemented. This consolidates the earlier defense concept into one player/monster action model. The separate [implementation plan](specs/actor-actions-and-interactions-plan.md) defines code contracts and delivery. [Weapon attacks](weapon-attack-system.md) describes current combat; [dungeon access and discovery](dungeon-exploration-design.md) owns which locks and passages exist.

## Purpose and shared rules

Combat, movement, abilities and interactions must feel like one game. The actor chooses an action, commits to its authored time/cost, and receives readable success, cancellation or failure feedback. Player input and monster decisions are different request sources for the same execution rules. Not every actor has every capability: bodies, equipment, tools and authored abilities determine availability, rather than hardcoded player/monster or weapon/no-weapon distinctions.

Ordinary movement does not occupy an action slot. An actor can own only one committed action at a time: primary attack, secondary technique/spell, item use, dodge, guard, world interaction, or forced reaction. Action-specific policies determine movement and cancellation; being busy is not one universal uninterruptible state. A cancelled action cannot later complete through a stale timer.

Melee remains horizontal gameplay with reliable timed contact, including around the physical body. Height and visual blade position do not create safe pockets. Existing armor and random evasion remain distinct from deliberate dodge/guard/parry. Elaborate boss patterns, stamina/posture meters, cinematic ripostes, large general-purpose ability bars and complex lockpicking minigames are outside the initial scope.

## Player controls and four item quick-action slots

Primary and secondary are dedicated inputs driven by the equipped main-hand weapon. Primary is its reliable basic action, with an authored unarmed fallback. Secondary is its equipped technique: usually a heavy/special attack for a martial weapon, or the selected learned elemental spell for a staff. Neither consumes one of the four configurable slots. Unarmed secondary is unavailable unless explicitly authored. Dodge, guard and interact are dedicated inputs too. Guard contains the brief opening parry window; there is no additional parry button initially.

The four slots are reserved for **item quick actions** such as drinking a potion, reading a scroll, or eating an eligible food item. They are not attack or general combat-ability slots. Primary and secondary remain available independently of all four slot assignments. Equipment changes update primary/secondary actions and defensive capabilities, not quick-item assignments. There is no requirement to invent four abilities or populate every slot.

| Intent | Input behavior | Proposed keyboard default |
| --- | --- | --- |
| Move | Continuous directional input | WASD; arrow-key alternatives |
| Primary action | Press or hold to repeat complete basic attacks at their normal cadence | J |
| Secondary technique / spell | One request per new press; cooldown/mana as authored | K |
| Dodge | New press; direction captured from movement or facing | Space |
| Guard / parry | New press raises; hold guards; release lowers | L |
| Interact | Hold on one focused target until its authored duration completes | E |
| Item quick-action slots 1–4 | One use request per new press; never repeat consumption while held | 1–4 |

All bindings are remappable. Keyboard and eventual gamepad are the primary control targets; aiming and menus cannot require a mouse. A proposed controller layout is RT primary, RB secondary, LT guard, B dodge, A interact, and LB+ABXY for the four item slots. Modifier chords consume their face-button intent: LB+A never starts interaction and LB+B never dodges. UI input owns its context and must not leak clicks/held buttons into gameplay when menus close. Validate these defaults with the existing Input Map and usability testing; the semantic separation is the requirement.

J/K request the corresponding equipped attack even near a chest; an interaction press never attacks. There is no tap-to-attack / hold-to-interact multiplexing and no implicit fallback to another action when one fails. Pressing interact with no valid target only gives appropriate feedback. Repeated basic attack is continuous intent, not an extra queued attack. Cooldowns/recovery still govern when the next attack can start. A hold interaction completes at most once per press; it cannot reopen/close a door repeatedly while the button stays down.

## Weapon techniques, learned magic and classes

The character owns learned techniques/spells; equipment makes compatible actions available and improves their performance. The initial martial secondary is usually the weapon's authored heavy attack with a cooldown and no mana cost. A bow can instead provide a piercing or multishot technique. There is no mandatory universal secondary spell that works regardless of the wielded weapon.

| Main-hand family | Primary | Secondary |
| --- | --- | --- |
| Axe | Ordinary swing | Heavy cleave |
| Sword | Ordinary slash | Authored thrust or sweep |
| Bow | Ordinary shot | Authored piercing/multishot |
| Elemental staff | Free elemental projectile | One selected learned spell of its element, costing mana |

Class supplies starting stats, equipment, spell knowledge and possibly an initial ability; it is not an exclusive magic permission. A barbarian may learn fire magic and use it after equipping a fire staff; a mage may wield an axe and use its technique. Magical effectiveness is influenced by relevant attributes/equipment rather than a class-name check. Changing weapons does not erase learned spells. Classes remain distinct starting kits, not interchangeable appearances with identical starting stats.

### Four elemental specializations

Earth, fire, water and wind each have a staff family and an initial target of five learnable spells. Staff families can have many tiers, affixes and unique versions; this does not limit the loot catalog to four objects. Free primary projectiles sit outside the five learned spells. Water includes ice in the initial concept.

| Element | Free staff primary | Identity | Five learnable secondary spells |
| --- | --- | --- | --- |
| Fire | Firebolt | Burning, explosions, aggressive area damage | Fireball; Wall of Fire; Flame Burst; Flame Lance; Meteor Shower |
| Earth | Stone shard | Impact, displacement, protection and obstacles | Boulder; Stone Wall; Tremor; Rock Spikes; Stone Skin |
| Water | Water bolt | Slowing/freezing and sustained control | Water Jet; Ice Lance; Frost Nova; Ice Wall; Blizzard |
| Wind | Wind dart | Mobility, pushing/pulling and mobile damage | Gust; Air Blade; Cyclone; Tailwind; Vacuum Burst |

These are a content direction, not a claim that twenty implementations/assets already exist. New spells offer different tactical uses, rather than a chain where Meteor Shower makes Fireball obsolete. Cheap, fast spells remain useful against a few enemies; walls control routes, bursts create space, and delayed large areas reward setup.

Learn spells through a small per-element skill tree using points earned from levels during the current run. Start with unlock nodes and authored prerequisites/point costs; allow investing across elements. The first version does not require a large rank/augmentation system or permanent account unlocks. Class starter knowledge is authored separately from earned points. Level/skill-point bookkeeping must not grant points again on load. Respec policy remains a later decision; any respec must preserve resource/cooldown commitments.

### Staff requirement and selected spell

The player must both know the spell and wield a matching elemental staff. A learned Meteor Shower remains unavailable with an earth staff, axe or empty hands. There is no silent conversion to another element, staff-bashing fallback or automatic different spell on failure. A staff always retains its free primary projectile even if the character knows no compatible secondary spell or has no mana.

Remember one selected spell per element on the character, independent of a particular staff instance. Replacing a fire staff retains the selected fire spell; changing to earth exposes the separately selected earth spell. If no eligible spell is selected, show an unavailable secondary with the reason and an explicit selection affordance. Starting mage data should provide an initial learned/selected spell so the starting staff is usable immediately. A newly learned spell does not silently replace the current selection.

Initially change selected spells through the loadout UI while the actor is neutral and no living visible hostile is within an authored loadout-safety radius. No mid-cast selection, combat spell cycling, extra spell bar or automated spell queue is required. Consequently only one of the five spells for the equipped element is directly castable at a time. Test that loadout tradeoff before expanding controls.

### Mana, cooldowns and equipment commitments

Use one actor mana pool with gameplay-time regeneration, independent of stamina/posture. Staff primary costs zero; learned secondary spells consume mana. Illustrative starting values with a 100-mana pool: Fireball 12, Wall of Fire 25, Flame Burst 35, Meteor Shower 60; regeneration around eight per second. These are tuning examples, not spell ranks or universal values. Attribute/equipment changes may affect capacity, regeneration, spell power and casting speed. Upgrading a spell need not automatically increase its mana cost; more costly variants must explicitly trade efficiency for power. Ordinary spells rely primarily on mana/cast recovery; exceptional spells can also have cooldowns. Mana potions remain consumable item quick actions.

Validate spell knowledge, staff compatibility, target, current action, mana and cooldown before acceptance. On acceptance, spend mana and start the technique cooldown atomically. Cancelling an accepted wind-up by dodge/guard or equipment loss does not refund either; a rejected request spends nothing. Mana depletion never silently switches K to primary attack. Do not repeat a costly spell merely because K remains held.

Cooldowns are character-owned by stable technique/spell identity, not weapon instance or slot position. Martial weapons share the authored technique cooldown key for equivalent heavy attacks; switching copies cannot refresh it. Selecting another spell does not reset the old spell's cooldown. Weapon swapping is denied during active contact/release/recovery; a permitted startup swap cancels that startup while retaining its spent costs. An externally removed/incompatible focus also cancels before release. Resolve cast power and target at accepted execution; equipment/stat changes cannot boost a projectile or lingering field after it has already been released. Released effects finish their authored lifetime after a swap; sustained channels explicitly requiring a held focus stop if it is lost.

### Keyboard/gamepad targeting and shared execution

Aim primary projectiles and directional techniques using facing/movement intent plus the existing modest aim assistance; no mouse cursor is required. A fresh secondary press captures a valid visible assisted target or, without one, a ground point along facing within the spell's authored placement range. Ground/wall spells use valid discovered floor positions and cannot place through blocking geometry. Self-centered spells use the actor's position.

Show the selected ground area/line during wind-up and commit the captured position/orientation; a meteor must not secretly chase a moving enemy during its delay. Wall orientation is perpendicular to the captured aim direction. Fire walls damage without blocking movement. Physical stone/ice walls must be temporary, cannot spawn through actors or across protected doors/stairs, and require working runtime navigation before being added as content. An invalid ground target denies the cast with feedback before costs; no walk-to-cast or new target-mode button is necessary initially. Exact range/assistance and preview visibility need camera-distance playtests.

Player spells and monster magic reuse the same casting, targeting, mana, interruption and contact rules. Monsters receive authored known spells/selection rather than navigating a player skill tree. A monster's natural elemental casting may use an explicit innate elemental-focus capability; it is never inferred from species name or treated as permission for a player to bypass the staff requirement. Do not grant every enemy free casting by default.

## Quick-item assignment and use

A successful pickup automatically assigns an eligible consumable to the lowest-numbered **unassigned** quick slot if it is not already represented. Run this after inventory acquisition/stack merging succeeds, using the owned item/variety, never the disappearing world pickup. Failed pickups and an already-full quick bar do not change assignments or prevent inventory acquisition. Equipment, keys, tools, currency and non-consumable IPlayerAction implementations are not automatically eligible merely because they have a use method.

Manual assignment, clearing and rearrangement remain available through inventory UI. Automatic assignment never overwrites an assigned slot, including one whose item is currently depleted. For stackable interchangeable potions/scrolls, bind the item variety and show total owned quantity across compatible stacks. An existing variety binding replenishes its count on pickup rather than taking another slot. Count zero keeps a visible depleted/reserved binding until explicitly cleared; it is not an unassigned slot. Auto-assignment does not create duplicate variety bindings.

Non-stackable consumables with individual state (notably food) bind a specific owned instance. Do not group them by appearance, infer contamination, or silently consume a different instance after depletion. Show a depleted reserved binding if that instance is gone. Item names/icons use the existing per-run identification/disguise presentation; assignment, counts and validity feedback must not reveal hidden properties. A new individual item can fill another truly unassigned slot, but never silently replaces a manually selected one.

Each press consumes at most one unit, through the same ownership/target validation and inventory-use transaction as inventory UI. A targeted scroll opens the existing target-selection flow; cancellation spends nothing. Revalidate the owned source and selected target before committing. A missing source, invalid target, busy actor or active item cooldown reports a reason and never invokes another slot or falls back to attack. Quick-use cooldown belongs to the consumable variety, not its button position: moving/clearing/reassigning a binding cannot reset it. Manual duplicate variety bindings are rejected initially.

Item use is a committed action, with the item's existing authored preparation/use time (for example a short drinking/reading animation). The effect and one-unit consumption commit together at completion after final validation. Positive damage/hit stun or invalid prerequisites before that point can cancel without consuming the item; no effect can execute later from an obsolete callback. Actor movement and voluntary attacks/defenses are denied during accepted item use initially. This differs deliberately from a long world interaction that the player can abandon to flee. Cooldown starts when use is accepted and is not refunded on interruption; an invalid request that never starts spends nothing. The duration/commit change is proposed, not a description of current instant inventory use.

Monsters do not need a quick bar. Their innate attacks, named abilities and world-operation intentions request the shared actor action rules directly. Monster consumption of carried potions is not part of the initial rollout and is not implied by player quick-item support.

## Shared action families

| Family | Source | Actor commitment and outcome |
| --- | --- | --- |
| Primary attack | Equipped weapon or innate body | Preparation, active contact/release, recovery and cooldown; distinct from the four slots |
| Secondary technique / spell | Weapon technique, learned compatible spell, or explicit innate monster capability | Same action gate and attack phases; mana/cooldown paid on acceptance |
| Item use / named ability | Carried consumable; separately authored monster action | Committed preparation/execution with explicit cost/effect boundary; the player quick bar only contains items |
| Dodge | Body capability initially | Short committed displacement and shorter immunity interval, then recovery/cooldown |
| Guard / parry | Equipment or body capability | Directional held protection with a limited opening parry window |
| World interaction | Nearby target plus actor capability/tool | Stationary timed channel, final validation, then one committed world change |
| Forced reaction | Hit, parry stagger, spawning or death | Overrides allowed voluntary actions; cannot be escaped by input spam |

The same action family need not imply the same animation, duration, damage or capability on every actor. Do not force monsters through a player hotbar. AI requests a primary attack, a named ability or a chosen interaction directly.

## Timed world interactions

Use one target-authored duration and one execution/progress mechanism. Proposed ordinary door/chest operation is **one second total**. A simple mechanical lock might take **five seconds total**, a difficult one **ten seconds total**. Lock difficulty selects its authored picking duration; these are initial tuning values. Do not charge a separate one-second hold threshold before starting the five/ten-second action.

| Target / verb | Initial duration / policy |
| --- | --- |
| Open/close ordinary door; open chest | 1.0 second; movement and combat are unavailable while the hold continues |
| Open using a matching reusable key | 1.0 second total; key validated again at completion |
| Pick and open mechanical lock | Authored 5/10-second examples; requires a reusable lockpick; success is deterministic initially |
| Operate a lever or mechanism | Authored duration, default 1.0 second when introduced |
| Automatic pickup, inventory/UI actions, staircase overlap | Keep their explicit existing policies; do not blindly add a hold to everything |

Pick-and-open is one authored compound verb: after successful final validation, unlock and request opening. It does not demand another arbitrary one-second hold. If a matching key is carried, prefer the key/open verb over picking. A sealed or otherwise ineligible object explains the missing condition rather than starting a progress bar that cannot succeed. Required encounter seals remain unpickable. Tools/keys are reusable initially; no pick breakage, hidden failure roll or consumable costs are added by this action design.

### Focus, progress and commitment

Select one eligible nearby target using visibility, reachable interaction range, facing preference and distance, with a stable tie-break. It must not be selected through a wall or reveal a concealed passage. Show its verb, bound input, duration or unmet prerequisite. Other nearby objects do not compete with simultaneous full prompts. Selection must not depend on which object entered the overlap area most recently.

A fresh hold locks the target and verb for the entire attempt. Show a circular progress indicator filling from zero to completion, anchored consistently near the selected target/prompt, with the verb ("Opening", "Picking lock") and remaining time for long actions. The ring represents accepted gameplay progress, not a speculative input threshold. Invalid/busy requests show a reason and do not fill. Only the player receives the input glyph; a monster's ongoing interaction can have readable activity/progress without a fictitious key prompt.

Starting requires being stationary, in range, with a valid capability/tool and no conflicting action. There is no automatic walk-to-target initially. While channeling, neither attacking, moving, guarding nor dodging progresses simultaneously. A movement or fresh combat/quick-item request cancels the interaction first; the new action may then begin if independently valid. Releasing interact also cancels. Thus the player can abandon a dangerous lock immediately, but loses the time invested. This is commitment through opportunity cost, not forced paralysis.

Actual health damage, hit stun/stagger, death, external displacement beyond interaction tolerance, lost range/line of sight, target destruction/state change, or lost prerequisites cancel the attempt. Merely seeing a monster does not cancel it. Armor absorption with no damage or hit reaction does not interrupt by itself. If the player holds a movement direction already, a fresh interaction is rejected as Moving; it does not silently stop the player and start picking. A new interaction requires a fresh press after cancellation/completion, so a held E cannot automatically restart under repeated damage.

Progress resets on cancellation, target switch, save/load or floor change. Initial interactions have no partial progress persistence. Releasing the button is not successful completion. At 100%, validate actor, target state, range/line of sight, capability, inventory/tool and current reservation again, then commit once. Before that boundary: no open door, removed lock, loot, tool consumption or reward claim. After it: the world change remains committed if the player moves or takes damage during its cosmetic animation. An opening animation is not another hidden hold duration.

Only one actor may reserve a target operation at once. Another actor sees Busy; it cannot advance the same lock independently or double-open a chest. Cancelled reservations release promptly. External target state changes invalidate the old operation; they do not silently turn a picking attempt into a different action. Save committed changes through the existing source/delta persistence system; a chest opening must never duplicate or lose its seeded reward.

An accessibility toggle-to-channel may replace the physical hold later, with identical duration, immobility and interruption. It must not bypass the risk model.

## Action conflicts and interruption

| Current commitment | Accepted next intent |
| --- | --- |
| Idle / ordinary movement | Primary/secondary action, available quick item/defense; interaction additionally requires stationary valid focus |
| Attack wind-up before contact | Dodge/guard may cancel; interaction, another attack and item use are denied; spent costs and attack cooldown remain spent |
| Attack active contact or recovery | New voluntary actions wait; no queued input initially |
| Held guard | Dodge may replace it; attacking/interaction requires release and guard-release recovery |
| World interaction channel | Release, movement or fresh combat/quick-item intent cancels channel; execute the new request only after cancellation and validation |
| Committed consumable use | Defense/interaction denied initially; movement and voluntary cancellation are denied until completion or an external interruption |
| Dodge/recovery, guard-release recovery, hit stun/stagger, spawning/death | Actions denied until their state permits them; death clears pending work |

On one frame, forced reactions win. For voluntary input use dodge, guard, secondary, primary, quick-item slots in numeric order, then interact; accept at most one committed action. Movement is evaluated before starting interaction. A pending interaction cancellation is processed before the replacement request; releasing interact never falls through to attack. No rejected request silently invokes another intent. Different named-action cancel policies can be authored later; no generic "cancel anything" shortcut.

## Dodge

Capture a horizontal direction when the action begins: current movement input, otherwise current facing. Commit it for the dash. Movement is authoritative, not animation root motion. No target lock, automatic enemy crossing, teleport, or mid-dash steering is required.

Walls, closed doors, props, map boundaries and actor bodies remain solid. The dash stops its displacement at an obstacle; it does not move through enemies initially. A blocked dash still consumes its cooldown and cannot be retriggered to refresh immunity. Large-monster movement constraints remain effective. Immunity covers eligible attack contacts only; it does not disable the hurtbox, stop overlaps being queried, grant flight, or prevent damage from excluded hazards.

Initial tuning proposal: 3.6 units over 0.28 seconds, immunity during [0.02, 0.20), then 0.17 seconds of recovery; one second between accepted dodge starts. These are Inspector-authored starting values, not final balance or universal monster values. A rat can use a shorter scuttle, with its own timings and cooldown.

## Guard and parry

Capture horizontal facing when guard begins. It stays fixed while held, allowing the player to strafe or retreat without turning the shield away. Release and raise again to change facing; the same rule applies to an enemy's accepted guard. The initial frontal sector is 120 degrees total. Body-contact attacks use the actual direction from attacker to defender, so standing inside a large model cannot confuse the front/back test. A projectile uses its incoming travel direction.

Proposed starting values: defense begins after 0.03 seconds; parry lasts until 0.21 seconds after acceptance; ordinary guard reduces eligible damage by 75%, and movement is 35% of normal speed. Guard release has 0.12 seconds of recovery. Damage reduction applies before armor. Guard can deal chip damage, but blocked contacts do not produce ordinary hit stun or knockback, including when chip damage is nonzero.

A successful parry consumes the window after one contact, produces distinct audiovisual feedback, interrupts the attacker and causes 0.35 seconds of stagger. Guard remains held afterward. It creates an ordinary attack opportunity, not a special execution or guaranteed critical hit. Initial melee attackers can be staggered by parry; sustained boss stagger immunity and diminishing returns are deferred.

Releasing and immediately pressing again must not refresh parry. A new window requires both 0.65 seconds since the previous eligible guard raise and at least 0.15 seconds released. Raising during rearm can still guard, but supplies no parry window. Rearm is spent when a window opens, even if it catches nothing. Changing equipment cannot reset these timers. Simultaneous hits use stable resolution order: the first qualifying contact consumes the parry, later eligible contacts receive ordinary guard.

## Contact eligibility and outcomes

Melee remains planar: height and the visible blade trajectory do not decide whether a defense works. The existing timed forward and body contact volumes still detect a dodging/guarding actor. Defense changes the resolution of that contact, not the geometry or swing window.

| Incoming contact | Dodge | Guard | Parry |
| --- | --- | --- | --- |
| Ordinary melee, including animal bites and physical body strikes | Yes | Yes | Yes |
| Ordinary arrow/bolt projectile | Yes | Yes | No initially |
| Spell explosion / area impact | Yes when authored dodgeable | No initially | No |
| Persistent hazards, poison ticks, falling or environmental damage | No by default | No by default | No |

These are authored attack properties, independent of faction, model, weapon ownership and the defender's capability. An explicitly unavoidable or unblockable attack needs a clear presentation before entering normal content. Boss status alone never bypasses defense. No projectile reflection or catch-and-return in the first implementation.

Resolution checks faction/death/debug immunity first, then active dodge, then frontal parry/guard, then existing random evasion and armor as applicable. A valid parry does not depend on an evasion roll. Ordinary guard can still benefit from evasion; a blocked hit's reduced damage then meets armor. Report distinct Dodged, Parried, Blocked, Evaded, Absorbed and Damaged outcomes.

Each melee execution resolves once per defender, including a dodge or parry. Staying inside the hit area after immunity expires does not let that same swing retry. Separate attacks can still hit when immunity ends. An ordinary projectile that contacts a dodging or guarding target is consumed without reflection; an impact blocked/parried/actively dodged at that target must not apply splash damage to it again under another contact ID. Nearby third parties may still receive an authored explosion.

## Capability examples and readable enemies

| Actor | Starting authoring intent |
| --- | --- |
| Player | Body supplies dodge; suitable shield or weapon supplies guard with optional parry |
| Rat | Body supplies evasive scuttle; no guard or parry |
| Skeleton swordsman | Weapon guard and occasional parry; no dodge initially |
| Forsaken Warden | Strong shield guard and optional shield parry; no dodge |
| Golem | Stone-arm brace; optional physical parry; no weapon requirement |

This is a rollout matrix, not a claim these gameplay actors or animations all exist. Adopt capabilities incrementally. Prefer an equipped offhand guard source, then an eligible main-hand weapon, then an innate body guard. A two-handed weapon suppresses the offhand source. Sources replace rather than stack. Innate dodge is independent; equipment dodge overrides are deferred.

Enemies may observe visible attack wind-ups with line of sight, plausible range and a reaction delay. They cannot read input presses, hidden action reservations, random damage rolls, or future committed facing. Roll defensive intent once per newly observed attack, not each frame. Commit to a chosen direction or guard facing; no perfect last-instant retargeting. Use a cooldown, restrained probability and normal recovery so groups still offer openings. Suggested starting point for a rat: roughly 0.15–0.25 seconds of reaction and an occasional attempt, tuned in play rather than a universal success rate.

Show a clear dash/afterimage, a held guard pose, and a distinctive parry flash and sound. Show defensive cooldown availability without obscuring attack feedback. Missing specialized animations may use a clearly authored fallback lean/brace and VFX; gameplay never depends on finding a bone collider. A defense may not be advertised as available without valid readable presentation.

## Player and monster world capabilities

Humanoids may be authored to open ordinary doors or operate mechanisms. Rats do not acquire those capabilities because a door is nearby; they navigate around it or remain blocked. A golem's ability to brace does not imply it can pick a lock. Breaking doors or smashing containers is a separate future attack/destruction interaction, not a free substitute for an unavailable Open action.

AI interaction requests use the same stationary channel, reservation, range/tool validation, completion and interruption as player requests. AI maintains an interaction intent rather than holding a physical button; losing the goal or deciding to fight/flee cancels it. No omniscient room/key planning is implied. An enemy can only choose a locally perceived, reachable interaction consistent with its goal and capabilities. Ordinary closed doors and boss seals keep their existing access/navigation authority; AI cannot call door motion directly to bypass locks or duration.

## Persistence, feedback and acceptance

All action clocks advance in gameplay time and pause with gameplay. Primary, selected secondary, mana, four item quick actions and defense expose distinct availability/cooldown indicators; the interaction ring is a progress indicator, not an ability-slot cooldown. Show separate blocked, dodged, parried, interrupted and missing-tool feedback without presenting an unsuccessful interaction as success.

Learned spells, point allocations, elemental selections, mana, cooldowns and committed costs survive equipment changes, stairs and current-version save/load. Loading resumes neutral, releases held input and restores remaining cooldown/recovery lockout, not a half-finished attack, defense window or picking channel. Persist the four item bindings separately from primary/secondary selection and character-owned technique cooldowns. Mana loading or equipment/selection changes cannot refill the pool; lowering capacity clamps current mana, and raising it does not heal mana. An in-progress interaction contributes no world-state delta; its partial progress is discarded. Committed lock/door/chest changes retain their existing persistent source IDs. Enemy revisits retain the current transient-combat reset policy and cleared enemies stay cleared. No historical save migration is required.

Acceptance must exercise combat and interaction together: all four quick-item slots assigned while both weapon actions still work; axe primary/heavy versus staff free projectile/selected learned spell; matching-staff rejection, cross-class learning, keyboard ground targeting and mana/cooldown preservation; holding E beside a chest never attacks; one/five/ten-second channels show one matching ring; a nearby monster creates meaningful pressure; release, movement, damage and dodge abandon picking without opening or claiming loot; completion commits once; a humanoid can channel an allowed door action while a rat cannot. Retain the defense, height, body-contact, opposite-facing and physics-enabled regression cases in the implementation plan. Exact bindings, durations and balance remain playtest tuning within these rules.
