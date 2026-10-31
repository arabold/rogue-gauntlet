# Dungeon Layout, Access, and Discovery

Status: intended gameplay design, updated October 30, 2026. [Door access](door-access.md) describes the implemented first version; its [remaining plan](specs/secret-doors-and-locks-plan.md) covers later extensions and tuning. This document defines the agreed exploration concept; it does not describe completed implementation. The [implementation plan](specs/level-progression-prototype-plan.md) defines proposed code changes and delivery slices. [Level design](level-design.md) describes the generator and encounter prototype that exist today. [Realms and monster design](realms-and-monster-design.md) owns world themes, realm cadence, and encounter concepts.

## Design goals

A floor should belong to a recognizable realm, provide a deliberate progression experience, and vary between runs. Ordinary rooms and themed landmarks use generated modular compositions built from small authored arrangements. Room identity describes function: a barracks can vary its footprint, sleeping layout, optional columns, materials and dressing. Reusable sections allow a library to occupy a whole room or attach to an office. [Room types](level-design.md#room-assembly) generate every Garrison room, the entrance and exit with their stairs included. Corridors, branches and secrets vary the geography. Generated rooms keep every doorway reachable, and [floor reachability](level-design.md#reachability) keeps spawns and loot out of inaccessible space. Players should learn the realm without learning a fixed checklist of secret-door locations.

Layout, access, and discovery are independent:

| Layer | Question | Examples |
| --- | --- | --- |
| Layout | Which locations exist, and how are they connected? | A required cell block, optional armory, corridor loop |
| Access | What permits passage or use? | Opening a door, picking a lock, presenting a key, defeating a boss |
| Discovery | Does the player know a connection exists? | An ordinary doorway or a concealed passage revealed nearby |

A concealed passage may also be locked. Discovering it does not unlock or open it. Unlocking an ordinary door does not open it. A visible staircase can remain inaccessible behind an encounter seal.

## Realm room pools and floor recipes

Each realm defines eligible generated room recipes and progression anchors. Room entries carry a purpose, floor/depth eligibility, selection weight, and repeat limit. Recipes compose compatible footprint and content layouts with independently optional columns and dressing. A shared generic hall may belong to several pools; sewers do not enter the Garrison merely because they are in the repository. Selection permissions and weights do not guarantee placement.

A floor recipe defines required locations, optional branches, encounter roles, access features, and secret opportunities. Required locations reserve space before optional filler. Recipes distinguish three independent obligations:

| Obligation | Guarantee | Example |
| --- | --- | --- |
| Must exist | The location appears on the floor | A prison wing |
| Must traverse | Every legal route to a specified destination passes through the location | A checkpoint before the exit section |
| Must complete | A named objective is satisfied before an action or route is enabled | Defeat the Warden before descending |

A location may carry more than one obligation. Passing through a boss room does not prove the boss was defeated. Conversely, requiring a boss defeat does not require every approach to the staircase to pass through the boss room.

The intended process is to plan locations and connections, place their geometry, then validate the finished walkable floor. Required traversal must survive real corridor junctions and loops, not merely look correct in a room diagram. Optional loops are welcome when they preserve the recipe's obligations. If optional content does not fit, omit it; never silently omit a required location or its access solution.

### Garrison example

These are initial recipe targets, not claims that new room scenes already exist:

| Floor | Required content | Optional content | Progression condition |
| --- | --- | --- | --- |
| 1: Garrison approach | Entrance, barracks, descent | Mess hall, storage, armory | Find the descent; a barracks traversal constraint is available when deliberately authored |
| 2: Prison wing | Entrance, cell block, nest chamber, descent | Pickable cells, guard quarters, reward branches | Reach descent; nests create pressure without automatically becoming mandatory kills |
| 3: Warden's domain | Entrance, Warden's hall, descent | Supplies, elite/reward branch | Defeat the Warden to remove the stair seal |

First delivery reuses existing skeleton encounters and dungeon pieces. A recipe role such as Warden's hall can use an existing room as a placeholder. New monsters, new tile kits, merchants, and bespoke boss phases are not prerequisites.

## Connections and doors

Access belongs to a connection between spaces, or to an action such as descending. A door is one physical presentation of that connection. A room with two entrances is not secured by locking only one of them: either protect every relevant entrance or deliberately allow a bypass.

Doors use composition. Opening/closing, optional lock behavior, and optional secret discovery are separate capabilities. Stairs can use an encounter access condition without pretending to be a door. A visible barrier presents a restriction; an authoritative access check enforces it even if the player can approach or move around the visual effect.

Predefined and generated rooms can both contain ordinary or locked doors. Authors mark suitable predefined entrances and cell doors as lock eligible, or assign an explicit lock. Main routes stay protected from arbitrary lock assignment. Generated door locations receive equivalent permissions when the floor recipe assigns their role. Eligibility is permission, not an instruction to lock every door.

Small flags such as pickability are appropriate within a capability. Avoid a single door type with combinations such as secret-blue-boss-prison: discovery, unlocking methods, and appearance vary independently.

## Locks and unlocking methods

[Actor actions and world interactions](actor-actions-and-interactions.md) owns interaction timing, interruption and progress feedback. This document owns access eligibility and solvability. Ordinary operations use an initial one-second total hold; picking uses the lock's authored total duration (for example five or ten seconds), through the same actor-neutral executor. Keys/tools are validated at completion, and abandoned picking retains no partial progress.


| Access mechanism | Suitable use | Appearance |
| --- | --- | --- |
| Pickable mechanical lock | Prison cells, storage, optional treasure | Conventional lock; communicates that tools apply |
| Matching key | A guard's cells, quarters, treasury | Key/lock identity; color coding is optional |
| Encounter seal | Boss-gated descent | Readable magical barrier tied to the encounter |

A mechanical lock declares its matching key and the methods that may open it (matching key, lockpick). Generated locks initially permit picking; one lockpick bypasses any pickable mechanical lock regardless of its key requirement. An unpickable mechanical lock requires its matching key. Encounter seals remain separate conditions and cannot be bypassed by a key or pick. Initial generated locks accept both their matching key and a lockpick; pick-only authored locks may omit a key deliberately.

Keys of the same color/variety share one canonical identity and item definition, and stack to 20. Different colors/identities never merge. A small colored diamond above a discovered locked door shows the required key variety and disappears on unlock; the interaction text also names the key. Concealed doors suppress this marker entirely. Matching uses identity, not a sampled visual color or display name; names/icons should also communicate the variety. Lockpicks stack to 20. Prefer a matching key over picking when both are carried. Key and pick items are used contextually at doors rather than assigned automatically to quick slots.

Successful unlocking consumes exactly one matching key or one lockpick. Picking is an interruptible timed action with deterministic success; its duration is authored tuning. Validate the item again at completion and commit consumption with the unlock exactly once. Cancellation or failed validation spends nothing. Unlocking persists; closing and reopening a door never charges another item. Random pick failure, breakage and partial progress are outside the initial scope.

Initial mechanical locks protect optional content. Floors may contain no locked room: the current Garrison tuning makes a 20% placement attempt with a hard maximum of one generated locked door. Secret-room attempts are more frequent at 45%, capped at one pocket, with no guarantee of either feature on a floor. Lockpicks are exceptional supplies rather than floor loot; the Rogue starts with two and shops may sell them later. Every generated keyed lock reserves a guaranteed matching-key pickup reachable before unlocking its protected region; random loot, shops, secrets and lockpicks do not satisfy this guarantee. Initially all these keys are placed in the openly reachable region, avoiding lock chains. Multiple locks sharing an identity require sufficient key quantities for all of them. Validate actual walkable geometry and every relevant entrance, not room-center adjacency alone. If an optional lock/key pair cannot fit, omit that pair or retry within a bound.

Any later required mechanical lock needs a guaranteed solution reachable before that lock, independent of random loot or shop stock. If a mandatory solution is an item, acquisition, inventory capacity, dropping, consumption, and persistence must all be unable to strand the run. Ordinary optional keys continue to obey normal inventory capacity.

## Gatekeepers and sealed stairs

The first Garrison boss condition is encounter completion, presented by a magical seal on the downward staircase. The stairs may remain visible and the player may find them before the boss. Approaching them explains the requirement; defeating the Warden removes the barrier and permits descent permanently for that run.

This rule allows flexible routes and prevents running around the boss from satisfying progression. A boss room and a sealed stairway do not require arena lockdown. Portcullises or temporary combat boundaries are separate encounter choices, not a universal realm rule.

A guaranteed boss key is a possible later alternative. It must use the same solvability and acquisition guarantees as other required keys. The initial stair seal unlocks automatically; boss loot remains reward content, not a chance to roll the only progression solution. Required encounter seals cannot be picked with ordinary lockpicks.

## Random secrets

Secret doors originate from generated geometry: a variable wall segment along a corridor, or a variable wall segment in a generated room. Complete authored/scripted room scenes never receive generated secret entrances, either as the host or hidden destination. Procedural recipe rooms and corridor walls are eligible; being generated by code does not make a room an excluded scripted room. Secrets are not fixed sockets in predefined room templates, and existing authored doors are not randomly relabeled as secrets. Predefined rooms can remain recognizable without teaching recurring secret-door positions.

The generator chooses a suitable host wall and reserves space for the concealed destination or connecting passage during layout. The room/passage exists from generation; approaching the secret does not reroll content or attach new geography. Placement checks include available space, wall style, structural corners, existing openings, and compatible concealed-wall visuals. Neither an obvious seam nor a distinctive prop should advertise every candidate.

Recipes request a bounded number of optional secrets, subject to valid placements. They need not occur on every floor. Candidate walls and destinations vary with the floor seed. The same run restores the same secrets on revisits; a new run chooses again. Exact counts, weights, and spacing require tuning.

A secret room has no ordinary visible entrance. A secret passage may deliberately reconnect known areas as a shortcut. These are separate placement roles: an alternate visible route is acceptable for a shortcut but invalidates a supposedly hidden room. Secrets must not bypass a must-traverse location or mandatory encounter seal. Required progression never depends on discovering a secret in the initial design.

### Discovery and opening

Traps follow the same proximity-roll discovery rule from any accessible side. Discovery exposes the trap plate without disarming it. Traps have a larger discovery radius for advance warning. Secret doors and traps share a brief yellow surface glow and blend on live discovery, which clears completely; loaded discoveries restore silently. Keys remain guaranteed when a generated lock is placed, while lockpicks are not generated as regular supplies.

An undiscovered passage looks like continuous surrounding wall, remains closed to movement and navigation, and exposes no door prompt, x-ray indicator, map icon, or fog reveal beyond it. A tool or interaction aimed at ordinary walls must not identify secret candidates through differing feedback.

Retain the actual ordinary wall tile selected for the concealed segment, including mesh-library identity, orientation, material and collision; do not use a recognizable secret-wall variant. On discovery, replace its presentation with the doorway/door assembly while keeping passage blocked until opening.

Initial discovery rolls every 0.5 seconds of active gameplay for eligible nearby secrets. Use current resolved Intelligence through an authored detection profile, since no Perception stat/talent exists yet; the profile can select a different stat later without changing door behavior. Chance depends on the selected stat and the secret's difficulty, with tunable bounded probabilities. Each eligible secret gets at most one roll per interval; pause/menu time and frame stalls must not accumulate a burst of checks. Repeated checks while standing nearby are intentional.

Eligibility requires a small authored radius, compatible floor height and an unobstructed approach from an accessible side. Proximity through another wall or from an inaccessible destination does not count. Success permanently reveals a closed door and enables ordinary interaction; it neither unlocks nor opens it and does not reveal the unopened destination. Gameplay detection randomness is separate from generation randomness.

Reveal, unlock and open are separate operations. Future hidden buttons can use the same discovery behavior and target a stable connection identity. Secrets may allow proximity discovery, a mechanism trigger, or both; button-only secrets disable proximity checks. A button may reveal, reveal and open, or explicitly unlock as well. Opening still checks any remaining lock or encounter condition. Only these extension seams are in scope now; button content and a puzzle framework are deferred.

Fog discovery and secret discovery are different. Revealing a corridor must not reveal the hidden branch; discovering the opening must not reveal the unopened room. Opening the passage can reveal through it under normal exploration rules. Enemies cannot use an undiscovered or closed secret connection as an invisible route.

## Persistent state and solvability

Generate the untouched floor from its seed, including room choices, connections, locks, access solutions, and secret locations. Save player-caused differences: discoveries, unlocked locks, door open/closed state, explored space, spent resources, rewards, and completed encounters. Shared authored definitions are not mutated by play. A discovered secret stays discovered; a closed but previously explored branch stays explored. A defeated boss's seal cannot return on a revisit or reload.

Before exposing the generated floor to play, validate:

- Required locations exist, and specified traversal constraints hold in the actual walkable layout.
- Mandatory access conditions have reachable, non-circular solutions and no unintended bypass.
- Locked rewards are secured at every relevant entrance; intentional alternate entrances are explicit.
- Secrets lead somewhere valid, remain optional, and do not leak through ordinary discovery/UI systems.
- Entry and exit remain usable after supported access conditions change.

Generation uses bounded retries. If required content cannot be placed, report a generation failure rather than delivering an impossible floor. Optional secret or reward branches may be dropped within the recipe's declared limits. Exact retry limits and discovery distances belong to implementation/tuning, not to the realm fiction.

## Delivery boundary

The first [access implementation](door-access.md) delivers consumable locks and saved access state, guaranteed generated lock/key pairs, secret pockets and random discovery. Its [remaining plan](specs/secret-doors-and-locks-plan.md) covers later extensions and tuning. Remaining per-depth realm recipes and the visible Warden seal are separate progression work, not prerequisites for this delivery. Each step must retain current-version save/load and playable generation. [The implementation plan](specs/level-progression-prototype-plan.md) owns types, scene wiring, algorithms, failure handling, and verification; this document owns the gameplay rules.
