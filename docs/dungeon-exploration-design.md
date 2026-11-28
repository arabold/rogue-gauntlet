# Dungeon Layout, Access, and Discovery

Intended gameplay rules for a floor's structure, access and secrets. [Level design](level-design.md) and [door access](door-access.md) describe what is implemented, [realm content](realm-content.md) how each realm stages its floors, and the [progression plan](specs/level-progression-prototype-plan.md) the remaining work: guaranteed locations per floor and a visible stair seal. [Realms and monster design](realms-and-monster-design.md) owns world themes and encounter concepts.

## Design goals

A floor should belong to a recognizable realm, provide a deliberate progression experience, and vary between runs. Rooms are generated compositions built from small authored arrangements: room identity describes function, so a barracks can vary its footprint, sleeping layout, columns, materials and dressing. Corridors, branches and secrets vary the geography. Every doorway stays reachable and [floor reachability](level-design.md#reachability) keeps spawns and loot out of inaccessible space. Players should learn the realm without learning a fixed checklist of secret-door locations.

Layout, access, and discovery are independent:

| Layer | Question | Examples |
| --- | --- | --- |
| Layout | Which locations exist, and how are they connected? | A required cell block, optional armory, corridor loop |
| Access | What permits passage or use? | Opening a door, picking a lock, presenting a key, defeating a boss |
| Discovery | Does the player know a connection exists? | An ordinary doorway or a concealed passage revealed nearby |

A concealed passage may also be locked. Discovering it does not unlock or open it. Unlocking an ordinary door does not open it. A visible staircase can remain inaccessible behind an encounter seal.

## Realm room pools and floor recipes

Each realm offers its own rooms over windows of its floors, picks each floor's plan, and gives each floor one headline room ([realm content](realm-content.md)). A shared room may belong to several realms and change with their themes; sewers do not enter the Garrison merely because they are in the repository. Selection permissions and weights do not guarantee placement.

A floor recipe defines required locations, optional branches, encounter roles, access features, and secret opportunities. Required locations reserve space before optional filler. Recipes distinguish three independent obligations:

| Obligation | Guarantee | Example |
| --- | --- | --- |
| Must exist | The location appears on the floor | A prison wing |
| Must traverse | Every legal route to a specified destination passes through the location | A checkpoint before the exit section |
| Must complete | A named objective is satisfied before an action or route is enabled | Defeat the Warden before descending |

A location may carry more than one obligation. Passing through a boss room does not prove the boss was defeated. Conversely, requiring a boss defeat does not require every approach to the staircase to pass through the boss room.

Today the boss room of a gatekeeper floor and the trap gauntlet before a descent are must-traverse, checked on the finished floor, and the gatekeeper's defeat is a must-complete objective for descending. Nothing yet guarantees a location: a floor's headline is placed first but dropped when it does not fit.

The intended process is to plan locations and connections, place their geometry, then validate the finished walkable floor. Required traversal must survive real corridor junctions and loops, not merely look correct in a room diagram. Optional loops are welcome when they preserve the recipe's obligations. If optional content does not fit, omit it; never silently omit a required location or its access solution. Existing rooms can stand in for a recipe role (a cell block, the Warden's hall); new monsters, tile kits or a dedicated shop room are not prerequisites.

## Connections and doors

Access belongs to a connection between spaces, or to an action such as descending. A door is one physical presentation of that connection. A room with two entrances is not secured by locking only one of them: either protect every relevant entrance or deliberately allow a bypass.

Doors use composition. Opening and closing, an optional lock and optional secret discovery are separate capabilities. Stairs can use an encounter access condition without pretending to be a door. A visible barrier presents a restriction; an authoritative access check enforces it even if the player can approach or move around the visual effect.

Room types decide which doorways get doors. Lock eligibility is permission, not an instruction to lock every door: main routes stay protected from arbitrary lock assignment, so rooms holding stairs or the gatekeeper, must-traverse rooms and rooms that reserve their access never take a generated lock.

Small flags such as pickability are appropriate within a capability. Avoid a single door type with combinations such as secret-blue-boss-prison: discovery, unlocking methods, and appearance vary independently.

## Locks and unlocking methods

[Actor actions and world interactions](actor-actions-and-interactions.md) owns interaction timing, interruption and progress feedback. This document owns access eligibility and solvability. Opening and unlocking use a short hold, picking a longer one authored on the lock's method, through the same actor-neutral executor. Keys and tools are validated at completion, and abandoned picking keeps no partial progress.

| Access mechanism | Suitable use | Appearance |
| --- | --- | --- |
| Pickable mechanical lock | Prison cells, storage, optional treasure | Conventional lock; communicates that tools apply |
| Matching key | A guard's cells, quarters, treasury | Key/lock identity; color coding is optional |
| Encounter seal | Boss-gated descent | Readable magical barrier tied to the encounter |

A mechanical lock declares its matching key and the methods that may open it (matching key, lockpick). One lockpick bypasses any pickable lock regardless of its key; an unpickable lock requires its key; generated locks accept both. Encounter seals are separate conditions that no key or pick bypasses.

Keys of the same variety share one canonical identity and stack; different varieties never merge. A small colored marker above a discovered locked door shows the required variety and disappears on unlock, and the interaction text names the key. Concealed doors suppress the marker entirely. Matching uses identity, never a sampled color or display name. Prefer a matching key over picking when both are carried. Keys and picks are used contextually at doors rather than from quick slots.

Successful unlocking consumes exactly one matching key or one lockpick, committed with the unlock exactly once; cancellation or failed validation spends nothing. Unlocking persists, and closing and reopening a door never charges another item. Random pick failure, breakage and partial progress are out of scope.

Mechanical locks protect optional content: a floor may have none, and every locked room holds a reward. Lockpicks are exceptional supplies, never floor loot. Every generated keyed lock reserves a guaranteed matching key that is reachable before the lock, independent of random loot, shops, secrets and lockpicks; several locks of one variety need enough keys for all of them. Keys normally lie in openly reachable space. A key chain may put a later key behind an earlier lock, as long as each key is reachable once the locks before it are open, so the chain solves in order. Validate actual walkable geometry and every relevant entrance, not room-center adjacency. An optional lock that cannot fit with its key is omitted.

Any later required mechanical lock needs a guaranteed solution reachable before that lock. If a mandatory solution is an item, acquisition, inventory capacity, dropping, consumption and persistence must all be unable to strand the run. Ordinary optional keys obey normal inventory capacity.

## Gatekeepers and sealed stairs

A gatekeeper's condition is encounter completion, presented on the downward staircase. The stairs may stay visible and the player may find them before the boss. Approaching them explains the requirement; defeating the gatekeeper permits descent permanently for that run. Today the requirement reads as a label over the stairs and is enforced by every travel path; the readable magical barrier is still to come.

This rule allows flexible routes and prevents running around the boss from satisfying progression. A boss room and a sealed stairway do not require arena lockdown; portcullises or temporary combat boundaries are separate encounter choices. The current boss floors also route the descent through the boss room.

A guaranteed boss key is a possible later alternative, with the same solvability and acquisition guarantees as other required keys. The seal lifts automatically; boss loot stays reward content, never the only progression solution.

## Random secrets

Room types choose the chance that each doorway is concealed, as part of assembling the room: a single concealed entrance to a treasure room, or a secret doorway beside visible entrances. The destination exists from generation; approaching the secret does not reroll content or attach new geography. The disguise occupies the doorway's wall span and becomes a closed door when discovered. Neither an obvious seam nor a distinctive prop should advertise every candidate. Secrets need not occur on every floor; the same run restores the same secrets on revisits, and a new run chooses again.

A secret room has no ordinary visible entrance. A secret passage may deliberately reconnect known areas as a shortcut. These are separate roles: an alternate visible route is acceptable for a shortcut but invalidates a supposedly hidden room. Secrets must not bypass a must-traverse location or a mandatory encounter seal. Required progression may depend on discovering a secret, but generated keys stay reachable from the entrance without discovering secrets or unlocking doors.

### Discovery and opening

Secrets and hidden traps are found the same way: a periodic proximity roll from an accessible side, using a stat through an authored detection profile (Intelligence until a Perception stat exists). Each eligible secret gets at most one roll per interval, and pause, menus and frame stalls never accumulate a burst of checks; standing nearby keeps rolling. Eligibility needs a small radius, compatible floor height and an unobstructed approach; proximity through another wall or from an inaccessible side does not count. Traps have a larger radius for earlier warning. Discovery exposes a trap without disarming it. Detection randomness is separate from generation randomness.

An undiscovered passage looks like continuous surrounding wall, made of an ordinary wall piece with matching material and collision, never a recognizable secret variant. It stays closed to movement and navigation and exposes no prompt, x-ray indicator, map icon or fog reveal beyond it. No tool or interaction aimed at ordinary walls may identify secret candidates through differing feedback. Discovery replaces the disguise with the closed door, with a brief shared reveal effect, and opens nothing.

Reveal, unlock and open are separate operations. Future hidden buttons can use the same discovery behavior and target a stable connection identity: a secret may allow proximity discovery, a mechanism, or both, and a button may reveal, reveal and open, or also unlock. Opening still checks any remaining lock or encounter condition.

Fog discovery and secret discovery are different. Revealing a corridor must not reveal the hidden branch; discovering the opening must not reveal the unopened room. Opening the passage reveals through it under normal exploration rules. Enemies cannot use an undiscovered or closed secret connection as an invisible route.

## Persistent state and solvability

Generate the untouched floor from its seed, including room choices, connections, locks, access solutions, and secret locations. Save player-caused differences: discoveries, unlocked locks, door open/closed state, explored space, spent resources, rewards, and completed encounters. Shared authored definitions are not mutated by play. A discovered secret stays discovered; a closed but previously explored branch stays explored. A defeated boss's seal cannot return on a revisit or reload.

Before exposing the generated floor to play, validate:

- Required locations exist, and specified traversal constraints hold in the actual walkable layout.
- Mandatory access conditions have reachable, non-circular solutions and no unintended bypass.
- Locked rewards are secured at every relevant entrance; intentional alternate entrances are explicit.
- Secrets lead somewhere valid, remain optional, and do not leak through ordinary discovery/UI systems.
- Entry and exit remain usable after supported access conditions change.

Generation uses bounded retries. If required content cannot be placed, report a generation failure rather than delivering an impossible floor. Optional secret or reward branches may be dropped. Retry limits and discovery distances belong to implementation and tuning, not to the realm fiction.
