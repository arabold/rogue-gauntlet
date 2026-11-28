# Realms and Monster Design

Status: world concept and design direction. The run is five realms of five floors: Garrison (depths 1–5), Prison (6–10), Sewer (11–15), Mines (16–20) and Cathedral (21–25); [level design](level-design.md#current-generation) describes how they generate and [realm content](realm-content.md) how each stages its rooms across its floors. The [monster catalog](monster-catalog/README.md) lists each realm's current cast, [monster design and authoring](monster-authoring-design.md) owns capabilities and composition, and the [realm monster roster](specs/realm-monster-roster-plan.md) proposes the casts still missing. Room, access and discovery rules live in [Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md); the [Game Design Document](game-design.md) owns the intended experience.

---

## 1. Narrative Premise and Run Objective

### The Sinking World
In Rogue Gauntlet, civilizational decline is a physical, geological reality. When an empire on the surface collapses or succumbs to corruption, its monuments, fortresses, and cities physically sink into the planetary crust under their own weight, stacking layer upon layer beneath the era that replaces them.

The surface kingdom is currently fracturing: ground tremors, vanishing outposts, and deepening fissures signal that the current world is being drawn downward into the abyss.

### The World-Anchor
At the bottom of all history lies the **World-Anchor** (also known as the *Sinking Maw*). This colossal primordial tether acts as a cosmic siphon, slowly drinking the spiritual and material essence of civilizations across eons.

- **The Hero's Mission:** The hero descends through the collapsed epochs carrying the **Gauntlet**, an ancient catalyst able to attune to and channel the runic energies of the fallen realms.
- **The Climax:** At Depth 25, the hero enters the Anchor Sanctum, overcomes the guardian wound around the core mechanism, and strikes the anchor chain with the attuned Gauntlet, permanently breaking the downward tether and securing victory for the active run.
- **Run Length and Victory:** The full journey spans 25 depths. Defeating the Depth 25 encounter ends the run in victory. Death anywhere during the descent ends the active run.
- **Post-Victory Considerations:** An optional high-difficulty return trial (an escape ascent back to Depth 1 through collapsing ruins against surging hordes) is reserved as an advanced post-game challenge mode rather than a requirement for the initial game loop.

---

## 2. The Sunken Epochs

The original concept partitioned the descent into eight sunken epochs. They are now a pool of themes for realms, not fixed depths or content IDs: the Garrison draws on the first, the Sewer on the second's cisterns, and later realms may draw on the rest. Each represents a fallen civilization with its own architecture, hazards and ecology.

1. **The Sunken Garrison:** Feudal military fortress; damp stone cellars, barracks, armories, and iron portcullises. Scavenging vermin and brittle skeleton sentries.
2. **The Drowned Forum / Cisterns:** Classical antiquity; flooded colonnades, aqueducts, and sunken arches. Aquatic lurkers, spear guards, and slowing water hazards.
3. **The Jade Undergrowth:** Primeval jungle terraces; roots tearing through stone, toxic spore pods, and fast predatory reptiles.
4. **The Ashen Scriptorium:** Ancient occult scholarship; multi-tiered library shelves, wax homunculi, living grimoires, and teleporting casters.
5. **The Iron Foundry:** Brutalist precursor industry; iron catwalks over open slag channels, steam pistons, and armored automata.
6. **The Glacial Necropolis:** Boreal permafrost tombs; translucent ice sheets, frost wraiths, yeti brutes, and frozen footing.
7. **The Gilded Pantheon:** Divine monuments; colossal golden obelisks, skybridges over chasms, leaping vanguards, and sweeping solar beams.
8. **The Primordial Rift:** Bedrock of reality; floating void monoliths, gravitational distortions, phase stalkers, and the World-Anchor.

---

## 3. The Realm Cadence

Every realm spans five floors with a recognizable escalation; its middle floors share the escalation role. These are intended roles, not a claim that every encounter, room or merchant is implemented.

| Floor | Progression role | Content direction |
| --- | --- | --- |
| 1 | Ingress and reconnaissance | Introduce realm architecture, baseline monsters and hazards; restrained patrols and optional supplies; from the second realm on, a merchant near the arrival |
| 2–4 | Escalation and swarm pressure | Nest chambers, denser encounters, specialists and optional guarded rewards |
| 5 | Sanctum and Gatekeeper trial | Gatekeeper location and visibly sealed descent |

Realm room pools and floor recipes select architecture appropriate to each depth. Required presence, required traversal and required encounter completion are distinct constraints, as defined in [Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md).

### Ingress and reconnaissance

Introduce the realm's monster roles through small, readable encounters. Room purposes guide supplies and optional risks. Mechanical locks protect optional rooms or rewards; matching keys and lockpicks follow the shared access design rather than a universal colored-key scheme. After a gatekeeper, the first floor greets the hero with a [merchant](shops.md), the chance to recover and prepare.

### Escalation and swarm pressure

Nests create reinforcement pressure in selected rooms; optional elite or locked reward branches invite resource decisions. Nest counts, caps and pacing remain tuning choices rather than an obligation to spawn indefinitely.

### Sanctum and Gatekeeper trial

The staircase is visible but sealed by the realm's Gatekeeper condition. Finding the stairs or bypassing the boss spatially does not permit descent. Defeating the Gatekeeper automatically removes the seal and permanently enables descent for that run. A specific traversal constraint or arena lockdown can be authored for an encounter, but neither is required for this progression rule. Each gatekeeper has a signature mechanic the realm's monsters have prepared the hero for.

High-tier boss rewards and Realm Sigils remain intended reward and attunement content; they are not random prerequisites for opening the seal. A guaranteed boss-key variant is possible later under the shared required-access guarantees.

---

## 4. Realm casts

Each realm fields a cast that supports its tactical focus, read at a glance in the KayKit style, and teaches at least one new way to move or attack: the Garrison's skeletons and rats teach melee approach, frontal defense and ranged pressure; the Prison's orc warband, flying bats and a thieving nymph teach crowd control and guarding the pack; the Sewer's oozes and ambushing snakes teach that not everything should be fought in melee. Some realms field one tiered humanoid family (skeletons, orcs) whose tiers differ in gear, size and one capability. Monster identity, fixed strength, movement, attack and defense policies, threat and XP are defined in [monster design and authoring](monster-authoring-design.md); concrete numbers live in the authored Godot Resources and the [balance model](balance-model.md).

## 5. Encounter direction

Mix complementary roles within finite threat budgets and respect available fighting space. Selection frequency is a tuning decision of its own; threat cost follows from combat strength through the balance model. Realm encounter resources and depth eligibility control where monsters appear; monsters do not automatically scale with dungeon depth. A special monster (a thief) stays rare through a per-floor cap rather than a low weight.

Nests provide bounded reinforcement pressure, with authored interval, batch size, simultaneous cap and total summon slots. Destroying a source stops further summons, while the minions it already released fight on. [Level design](level-design.md#encounters) records the current nests, persistence and gatekeeper behavior.
