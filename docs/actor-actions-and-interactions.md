# Actor Actions and World Interactions

Status: mixed current and intended gameplay. Dedicated weapon inputs, four item quick slots, ordinary timed interactions, active defenses and mechanical locks ([door access](door-access.md)) are implemented; spell weapons and mana remain proposed. [Combat](combat-system.md) and [items](stats-and-itemization.md) own current rules; the [remaining plan](specs/actor-actions-and-interactions-plan.md) covers extensions. [Dungeon design](dungeon-exploration-design.md) owns which locks and passages exist.

## Purpose and shared rules

Combat, movement, abilities and interactions must feel like one game. The actor chooses an action, commits to its authored time/cost, and receives readable success, cancellation or failure feedback. Player input and monster decisions are different request sources for the same execution rules. Not every actor has every capability: bodies, equipment, tools and authored abilities determine availability, rather than hardcoded player/monster or weapon/no-weapon distinctions.

Ordinary movement does not occupy an action slot. An actor can own only one committed action at a time: primary attack, secondary technique/spell, item use, dodge, guard, world interaction, or forced reaction. Action-specific policies determine movement and cancellation; being busy is not one universal uninterruptible state. A cancelled action cannot later complete through a stale timer.

Melee remains horizontal gameplay with reliable timed contact, including around the physical body. Height and visual blade position do not create safe pockets. Armor mitigates contact damage; avoiding confirmed contact requires deliberate dodge/guard/parry. Elaborate boss patterns, stamina meters, cinematic ripostes, large general-purpose ability bars and complex lockpicking minigames are outside the initial scope.

## Player controls and four item quick-action slots

Primary and secondary are dedicated inputs driven by the equipped main-hand weapon. Primary is its reliable basic action, with an authored unarmed fallback. Secondary is its equipped technique: usually a heavy/special attack for a martial weapon, or the bound elemental spell of a wand or staff. Neither consumes one of the four configurable slots. Unarmed secondary is unavailable unless explicitly authored. Dodge, guard and interact are dedicated inputs too. Guard contains the brief opening parry window; there is no additional parry button initially. The [off-hand weapons plan](specs/off-hand-weapons-plan.md) keeps these inputs for a passive off-hand weapon; its dual-wield alternative replaces the guard input with an off-hand action (shield guard or off-hand weapon strike) and a new gamepad layout.

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

## Weapon techniques, spell weapons and classes

Equipment supplies both weapon actions; the character learns nothing. The initial martial secondary is usually the weapon's authored heavy attack with a cooldown and no mana cost. A bow can instead provide a piercing or multishot technique. Magic lives on wands and staves, each carrying one spell. There is no universal secondary spell that works regardless of the wielded weapon.

| Main-hand family | Primary | Secondary |
| --- | --- | --- |
| Axe | Ordinary swing | Heavy cleave |
| Sword | Ordinary slash | Authored thrust or sweep |
| Bow | Ordinary shot | Authored piercing/multishot |
| Wand (one-handed) or staff (two-handed) | Free magic missile volley of the weapon's element | The weapon's bound elemental spell, costing mana |

Class supplies starting stats and equipment; it is not a magic permission. Any class can equip any wand or staff and cast its spell; Intelligence sizes the mana pool, so a Mage casts more often than a Barbarian, and a mage may wield an axe and use its technique. Classes remain distinct starting kits, not interchangeable appearances with identical starting stats.

### Spell wands and staves

Earth, fire, water and air each have five spells: targeted attacks, caster-centered area attacks and protective guards. Water includes ice. Each wand or staff carries exactly one spell, rolled when the item is created, and casts it at the level its tier sets, so deeper weapons carry stronger spells. Spells are never learned, upgraded or selected; trying another spell means equipping another weapon. The free missile primary stays available without mana or a usable spell. A staff gives up the off hand for a stronger missile volley. The [magic rework plan](specs/magic-rework-plan.md) owns spell names, levels, damage and mana numbers; the [spell plan](specs/elemental-spells-plan.md) owns targeting, area, guard and visual rules. Permanent account unlocks remain outside the first version.

### Mana, cooldowns and equipment commitments

Use one actor mana pool with gameplay-time regeneration, independent of poise; there is no stamina. Missiles cost nothing; a bound spell consumes its authored cost. Intelligence and equipment change capacity; equipment and stat changes never refill mana, and lowering capacity clamps it. Ordinary spells rely primarily on mana and cast recovery; exceptional spells can also have cooldowns. A mana potion, if added, remains a consumable item quick action.

Validate the equipped spell weapon, target, current action, mana and cooldown before acceptance. On acceptance, spend mana and start the cooldown atomically. Cancelling an accepted wind-up by dodge/guard or equipment loss does not refund either; a rejected request spends nothing. Mana depletion never silently switches K to the missile. Do not repeat a costly spell merely because K remains held.

Cooldowns are character-owned by stable technique/spell identity, not weapon instance or slot position. Martial weapons share the authored technique cooldown key for equivalent heavy attacks, and two wands with the same spell share its key, so switching copies cannot refresh it. Weapon swapping is denied during active contact/release/recovery; a permitted startup swap cancels that startup while retaining its spent costs. A committed or forced removal of the casting weapon immediately ends all effects it created, including projectiles, area effects and guards; equipping another weapon with the same spell does not retain them. Cancel pending contacts and callbacks before equipment-change observers run. Already resolved damage is not reversed. Capture cast power at acceptance; later stat changes cannot boost released effects.

### Keyboard/gamepad targeting and shared execution

Aim primary projectiles and directional techniques using facing/movement intent plus existing aim assistance; no mouse is required. Targeted secondary spells sample distinct visible enemies around the caster and capture their positions on acceptance, with at most one resolved contact per selected enemy per cast. Empty strike slots may show harmless effects. Area spells center on the caster at release and resolve one contact on every eligible enemy in radius, without a target cap. Gust also pushes unblocked enemies outward; Frost Nova briefly freezes surviving enemies after damaging contact. Guards absorb into their own untimed hit-point pool and reflect to eligible attacking monsters until depleted or the casting weapon is unequipped. See the [spell plan](specs/elemental-spells-plan.md#targeting-area-and-guard-rules) for obstruction, reflection and persistence rules.

Player spells and monster magic reuse the same casting, targeting, mana, interruption and contact rules. A monster casts from an equipped spell weapon or an explicitly authored innate spell; casting is never inferred from species name. Do not grant every enemy free casting by default.

## Quick-item assignment and use

A successful pickup automatically assigns an eligible consumable to the lowest-numbered **unassigned** quick slot if it is not already represented. Run this after inventory acquisition/stack merging succeeds, using the owned item/variety, never the disappearing world pickup. Failed pickups and an already-full quick bar do not change assignments or prevent inventory acquisition. Equipment, keys, tools, currency and non-consumable IPlayerAction implementations are not automatically eligible merely because they have a use method.

Manual assignment, clearing and rearrangement remain available through inventory UI. Automatic assignment never overwrites an assigned slot, including one whose item is currently depleted. For stackable interchangeable potions/scrolls, bind the item variety and show total owned quantity across compatible stacks. An existing variety binding replenishes its count on pickup rather than taking another slot. Count zero keeps a visible depleted/reserved binding until explicitly cleared; it is not an unassigned slot. Auto-assignment does not create duplicate variety bindings.

Non-stackable consumables with individual state (notably food) bind a specific owned instance. Do not group them by appearance, infer contamination, or silently consume a different instance after depletion. Show a depleted reserved binding if that instance is gone. Item names/icons use the existing per-run identification/disguise presentation; assignment, counts and validity feedback must not reveal hidden properties. A new individual item can fill another truly unassigned slot, but never silently replaces a manually selected one.

Each press consumes at most one unit, through the same ownership/target validation and inventory-use transaction as inventory UI. A targeted scroll opens the existing target-selection flow; cancellation spends nothing. Revalidate the owned source and selected target before committing. A missing source, invalid target, busy actor or active item cooldown reports a reason and never invokes another slot or falls back to attack. Quick-use cooldown belongs to the consumable variety, not its button position: moving/clearing/reassigning a binding cannot reset it. Manual duplicate variety bindings are rejected initially.

Item use is a committed action, with the item's existing authored preparation/use time (for example a short drinking/reading animation). The effect and one-unit consumption commit together at completion after final validation. Positive damage/hit stun or invalid prerequisites before that point can cancel without consuming the item; no effect can execute later from an obsolete callback. Actor movement and voluntary attacks/defenses are denied during accepted item use initially. This differs deliberately from a long world interaction that the player can abandon to flee. Cooldown starts when use is accepted and is not refunded on interruption; an invalid request that never starts spends nothing. The [current combat and item docs](combat-system.md) describe delivered execution; any extension must preserve these ownership rules.

Monsters do not need a quick bar. Their innate attacks, named abilities and world-operation intentions request the shared actor action rules directly. Monster consumption of carried potions is not part of the initial rollout and is not implied by player quick-item support.

## Shared action families

| Family | Source | Actor commitment and outcome |
| --- | --- | --- |
| Primary attack | Equipped weapon or innate body | Preparation, active contact/release, recovery and cooldown; distinct from the four slots |
| Secondary technique / spell | Weapon technique, a spell weapon's bound spell, or explicit innate monster capability | Same action gate and attack phases; mana/cooldown paid on acceptance |
| Item use / named ability | Carried consumable; separately authored monster action | Committed preparation/execution with explicit cost/effect boundary; the player quick bar only contains items |
| Dodge | Body capability initially | Short committed displacement and shorter immunity interval, then recovery/cooldown |
| Guard / parry | Equipment or body capability | Directional held protection with a limited opening parry window |
| World interaction | Nearby target plus actor capability/tool | Stationary timed channel, final validation, then one committed world change |
| Forced reaction | Hit, parry stagger, spawning or death | Overrides allowed voluntary actions; cannot be escaped by input spam |

The same action family need not imply the same animation, duration, damage or capability on every actor. Do not force monsters through a player hotbar. AI requests a primary attack, a named ability or a chosen interaction directly.

## Timed world interactions

Use one target-authored duration and one execution/progress mechanism. Opening an unlocked door takes **half a second**; closing a door or opening a chest takes **one second total**. A simple mechanical lock might take **five seconds total**, a difficult one **ten seconds total**. Lock difficulty selects its authored picking duration; these are initial tuning values. Do not charge a separate one-second hold threshold before starting the five/ten-second action.

| Target / verb | Initial duration / policy |
| --- | --- |
| Open unlocked door | 0.5 seconds; movement and combat are unavailable while the hold continues |
| Close door; open chest | 1.0 second; movement and combat are unavailable while the hold continues |
| Unlock and open using a matching consumable key | 1.0 second total; consume one matching key only at successful completion |
| Pick and open mechanical lock | Authored 5/10-second examples; requires one consumable lockpick and a pickable lock; success is deterministic initially |
| Operate a lever or mechanism | Authored duration, default 1.0 second when introduced |
| Automatic pickup, inventory/UI actions, staircase overlap | Keep their explicit existing policies; do not blindly add a hold to everything |

Pick-and-open is one authored compound verb: after successful final validation, unlock and request opening. It does not demand another arbitrary one-second hold. If a matching key is carried, prefer the key/open verb over picking. A sealed or otherwise ineligible object explains the missing condition rather than starting a progress bar that cannot succeed. Required encounter seals remain unpickable. Successful unlocking consumes exactly one matching key or one lockpick, committed with the unlock after final validation. Cancellation and failed validation consume nothing. Both item varieties stack to 20; key matching, pickability and guaranteed acquisition follow [dungeon access design](dungeon-exploration-design.md#locks-and-unlocking-methods). Random pick breakage and hidden failure rolls remain outside scope.

### Focus, progress and commitment

Select one eligible nearby target using visibility, reachable interaction range, facing preference and distance, with a stable tie-break. It must not be selected through a wall or reveal a concealed passage. Show its verb, bound input, duration or unmet prerequisite. Other nearby objects do not compete with simultaneous full prompts. Selection must not depend on which object entered the overlap area most recently.

A fresh hold locks the target and verb for the entire attempt. Show a circular progress indicator filling from zero to completion, anchored consistently near the selected target/prompt, without an accompanying message or countdown. The ring represents accepted gameplay progress, not a speculative input threshold. Invalid/busy requests show a reason and do not fill. Only the player receives the input glyph; a monster's ongoing interaction can have readable activity/progress without a fictitious key prompt.

Starting requires being stationary, in range, with a valid capability/tool and no conflicting action. There is no automatic walk-to-target initially. While channeling, neither attacking, moving, guarding nor dodging progresses simultaneously. A movement or fresh combat/quick-item request cancels the interaction first; the new action may then begin if independently valid. Releasing interact also cancels. Thus the player can abandon a dangerous lock immediately, but loses the time invested. This is commitment through opportunity cost, not forced paralysis.

Actual health damage, hit stun/stagger, death, external displacement beyond interaction tolerance, lost range/line of sight, target destruction/state change, or lost prerequisites cancel the attempt. Merely seeing a monster does not cancel it. Armor absorption with no damage or hit reaction does not interrupt by itself. If the player holds a movement direction already, a fresh interaction is rejected as Moving; it does not silently stop the player and start picking. A new interaction requires a fresh press after cancellation/completion, so a held E cannot automatically restart under repeated damage.

Progress resets on cancellation, target switch, save/load or floor change. Initial interactions have no partial progress persistence. Releasing the button is not successful completion. At 100%, validate actor, target state, range/line of sight, capability, inventory/tool and current reservation again, then commit once. Before that boundary: no open door, removed lock, loot, tool consumption or reward claim. After it: the world change remains committed if the player moves or takes damage during its cosmetic animation. An opening animation is not another hidden hold duration.

Only one actor may reserve a target operation at once. Another actor sees Busy; it cannot advance the same lock independently or double-open a chest. Cancelled reservations release promptly. External target state changes invalidate the old operation; they do not silently turn a picking attempt into a different action. Save committed changes through the existing source/delta persistence system; a chest opening must never duplicate or lose its seeded reward.

An accessibility toggle-to-channel may replace the physical hold later, with identical duration, immobility and interruption. It must not bypass the risk model.

## Action conflicts and active defense

The implemented [combat rules](combat-system.md) own interruption, dodge, frontal guard/parry and contact resolution. There is no passive hit rejection. All actors use the same execution rules with explicitly composed capabilities; monster species or a shield visual alone grants none.

Readable animation, timing, direction and recovery must match a defense's effect. Later Dexterity tuning may improve an activated action's timing/mitigation, but never grants an automatic evasion roll. Stamina and cinematic riposte systems remain outside the first scope; elemental shield reflection is proposed separately. A per-actor [poise meter](combat-system.md#poise-and-hit-reactions) governs hit reactions only.

## Player and monster world capabilities

Humanoids may be authored to open ordinary doors or operate mechanisms. Rats do not acquire those capabilities because a door is nearby; they navigate around it or remain blocked. A golem's ability to brace does not imply it can pick a lock. Breaking doors or smashing containers is a separate future attack/destruction interaction, not a free substitute for an unavailable Open action.

AI interaction requests use the same stationary channel, reservation, range/tool validation, completion and interruption as player requests. AI maintains an interaction intent rather than holding a physical button; losing the goal or deciding to fight/flee cancels it. No omniscient room/key planning is implied. An enemy can only choose a locally perceived, reachable interaction consistent with its goal and capabilities. Ordinary closed doors and boss seals keep their existing access/navigation authority; AI cannot call door motion directly to bypass locks or duration.

## Persistence, feedback and acceptance

All action clocks advance in gameplay time and pause with gameplay. Primary, secondary, mana, four item quick actions and defense expose distinct availability/cooldown indicators; the interaction ring is a progress indicator, not an ability-slot cooldown. Show separate blocked, dodged, parried, interrupted and missing-tool feedback without presenting an unsuccessful interaction as success.

Mana, cooldowns and committed costs survive equipment changes, stairs and current-version save/load. An active guard and its remaining capacity survive save/load and floor travel only while its original casting weapon remains equipped; unequipping it ends the guard immediately. Loading resumes neutral, releases held input and restores remaining cooldown/recovery lockout, not a half-finished attack, defense window or picking channel. Persist the four item bindings separately from primary/secondary selection and character-owned technique cooldowns. Mana loading or equipment/selection changes cannot refill the pool; lowering capacity clamps current mana, and raising it does not heal mana. An in-progress interaction contributes no world-state delta; its partial progress is discarded. Committed lock/door/chest changes retain their existing persistent source IDs. Enemy revisits retain the current transient-combat reset policy and cleared enemies stay cleared. No historical save migration is required.

Acceptance must exercise combat and interaction together: all four quick-item slots assigned while both weapon actions still work; axe primary/heavy versus wand free missile/bound spell; out-of-mana rejection, any class casting a found wand, surrounding-enemy targeting and mana/cooldown preservation; holding E beside a chest never attacks; one/five/ten-second channels show one matching ring; a nearby monster creates meaningful pressure; release, movement, damage and dodge abandon picking without opening or claiming loot; completion commits once; a humanoid can channel an allowed door action while a rat cannot. Retain the defense, height, body-contact, opposite-facing and physics-enabled regression cases in the implementation plan. Exact bindings, durations and balance remain playtest tuning within these rules.

## Monster rollout boundary

[Monster design and authoring](monster-authoring-design.md) owns the agreed first-realm roster and basic movement/attack/defense policies; the [roadmap](development-roadmap.md#first-three-floor-acceptance) owns remaining monster acceptance. Generic examples above do not grant a rat a dodge/guard animation or require every monster to have active defense. Compose only capabilities with authored gameplay and valid presentation. The first rollout excludes weapon switching, group shelter and boss phases. Flanking exists through [rear hits and attack slots](combat-system.md#facing-and-sneak-attacks).
