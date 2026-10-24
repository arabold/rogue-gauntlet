# Dungeon Layout, Access, and Discovery

Status: intended gameplay design, October 20, 2026. This document defines the agreed exploration concept; it does not describe completed implementation. The [implementation plan](specs/level-progression-prototype-plan.md) defines proposed code changes and delivery slices. [Level design](level-design.md) describes the generator and encounter prototype that exist today. [Realms and monster design](realms-and-monster-design.md) owns world themes, realm cadence, and encounter concepts.

## Design goals

A floor should belong to a recognizable realm, provide a deliberate progression experience, and vary between runs. Generated ordinary rooms gain variety from small authored furnishing arrangements; complete authored rooms supply memorable landmarks and deliberate gameplay spaces. Corridors, branches and secrets vary the geography. The [current arrangement system](level-design.md#reusable-room-arrangements) reuses compositions while preserving access to every remaining free area. Players should learn the realm without learning a fixed checklist of secret-door locations.

Layout, access, and discovery are independent:

| Layer | Question | Examples |
| --- | --- | --- |
| Layout | Which locations exist, and how are they connected? | A required cell block, optional armory, corridor loop |
| Access | What permits passage or use? | Opening a door, picking a lock, presenting a key, defeating a boss |
| Discovery | Does the player know a connection exists? | An ordinary doorway or a concealed passage revealed nearby |

A concealed passage may also be locked. Discovering it does not unlock or open it. Unlocking an ordinary door does not open it. A visible staircase can remain inaccessible behind an encounter seal.

## Realm room pools and floor recipes

Each realm defines eligible authored rooms and procedural room styles. Room entries carry a purpose, floor/depth eligibility, selection weight, and repeat limit. A shared generic hall may belong to several pools; sewers do not enter the Garrison merely because they are in the repository. Selection permissions and weights do not guarantee placement.

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

A mechanical lock can accept a matching key, lockpicks, or either method. A cell may be pick-only; an authored alternative such as a guard's prison key can open a defined group of cells. Matching scope must be intentional. A reusable prison key and a one-door treasury key need not share a rule.

Color is presentation rather than the matching rule. A blue door/blue key puzzle is an explicit special feature, not the default for prison cells. Revealing or recoloring a door never satisfies its lock.

The initial working direction for lockpicking requires the appropriate tool and an interruptible timed action with deterministic success. The tool is reusable initially; duration is tuning. Pick breakage, charges, probabilistic failure, character skill, and key consumption are later decisions. This gives pickable cells an observable mechanic without introducing an unsupported resource economy.

Initial mechanical locks protect optional content. Any later required mechanical lock needs a guaranteed solution reachable before that lock, independent of random loot or shop stock. If a mandatory solution is an item, acquisition, inventory capacity, dropping, consumption, and persistence must all be unable to strand the run. Ordinary optional keys continue to obey normal inventory capacity.

## Gatekeepers and sealed stairs

The first Garrison boss condition is encounter completion, presented by a magical seal on the downward staircase. The stairs may remain visible and the player may find them before the boss. Approaching them explains the requirement; defeating the Warden removes the barrier and permits descent permanently for that run.

This rule allows flexible routes and prevents running around the boss from satisfying progression. A boss room and a sealed stairway do not require arena lockdown. Portcullises or temporary combat boundaries are separate encounter choices, not a universal realm rule.

A guaranteed boss key is a possible later alternative. It must use the same solvability and acquisition guarantees as other required keys. The initial stair seal unlocks automatically; boss loot remains reward content, not a chance to roll the only progression solution. Required encounter seals cannot be picked with ordinary lockpicks.

## Random secrets

Secret doors originate from generated geometry: a variable wall segment along a corridor, or a variable wall segment in a generated room. They are not fixed sockets in predefined room templates, and existing authored doors are not randomly relabeled as secrets. Predefined rooms can remain recognizable without teaching recurring secret-door positions.

The generator chooses a suitable host wall and reserves space for the concealed destination or connecting passage during layout. The room/passage exists from generation; approaching the secret does not reroll content or attach new geography. Placement checks include available space, wall style, structural corners, existing openings, and compatible concealed-wall visuals. Neither an obvious seam nor a distinctive prop should advertise every candidate.

Recipes request a bounded number of optional secrets, subject to valid placements. They need not occur on every floor. Candidate walls and destinations vary with the floor seed. The same run restores the same secrets on revisits; a new run chooses again. Exact counts, weights, and spacing require tuning.

A secret room has no ordinary visible entrance. A secret passage may deliberately reconnect known areas as a shortcut. These are separate placement roles: an alternate visible route is acceptable for a shortcut but invalidates a supposedly hidden room. Secrets must not bypass a must-traverse location or mandatory encounter seal. Required progression never depends on discovering a secret in the initial design.

### Discovery and opening

An undiscovered passage looks like continuous surrounding wall, remains closed to movement and navigation, and exposes no door prompt, x-ray indicator, map icon, or fog reveal beyond it. A tool or interaction aimed at ordinary walls must not identify secret candidates through differing feedback.

Initial discovery is automatic when the player approaches the concealed segment from a reachable side within a small authored radius. Proximity alone through an intervening wall, from another floor height, or from the inaccessible destination does not count. Once found, the wall reveals its door/passage treatment and ordinary interaction becomes available. It remains closed until opened and still obeys any lock.

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

Implement realm pools and recipe-driven placement, then composed access with the visible Warden seal and optional mechanical locks, then generated secret branches and discovery. Each step must retain current-version save/load and playable generation. [The implementation plan](specs/level-progression-prototype-plan.md) owns types, scene wiring, algorithms, failure handling, and verification; this document owns the gameplay rules.
