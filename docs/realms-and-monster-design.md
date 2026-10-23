# Realms and Monster Design

Status: world concept, revised October 23, 2026. The eight-realm, 24-depth descent is the current direction; later realm order, placement and rosters remain provisional. Delivery focuses on playable depths 1–3. [Monster design and authoring](monster-authoring-design.md) owns roster, capabilities and balancing rules; its [implementation plan](specs/monster-authoring-plan.md) owns the rollout. Room/access/discovery rules live in [Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md). See the [Game Design Document](game-design.md), current [Level Design](level-design.md), and remaining [implementation plan](specs/level-progression-prototype-plan.md) for their separate responsibilities.

---

## 1. Narrative Premise and Run Objective

### The Sinking World
In Rogue Gauntlet, civilizational decline is a physical, geological reality. When an empire on the surface collapses or succumbs to corruption, its monuments, fortresses, and cities physically sink into the planetary crust under their own weight, stacking layer upon layer beneath the era that replaces them.

The surface kingdom is currently fracturing: ground tremors, vanishing outposts, and deepening fissures signal that the current world is being drawn downward into the abyss.

### The World-Anchor (Depth 24)
At the bottom of all history—at Depth 24—lies the **World-Anchor** (also known as the *Sinking Maw*). This colossal primordial tether acts as a cosmic siphon, slowly drinking the spiritual and material essence of civilizations across eons.

- **The Hero's Mission:** The hero descends through all eight collapsed epochs carrying the **Gauntlet**, an ancient catalyst able to attune to and channel the runic energies of the fallen realms.
- **The Climax:** At Depth 24, the hero enters the Anchor Sanctum, overcomes the guardian wound around the core mechanism, and strikes the anchor chain with the attuned Gauntlet, permanently breaking the downward tether and securing victory for the active run.
- **Run Length and Victory:** The full journey spans 24 depths. Defeating the Depth 24 encounter ends the run in victory. Death anywhere during the descent ends the active run.
- **Post-Victory Considerations:** An optional high-difficulty return trial (an escape ascent back to Depth 1 through collapsing ruins against surging hordes) is reserved as an advanced post-game challenge mode rather than a requirement for the initial game loop.

---

## 2. The Eight Sunken Epochs (Depths 1 to 24)

The current world concept spans 24 depths partitioned into eight 3-depth realms. Each realm represents a fallen civilization with its own architecture, hazards and ecology. The order and depth assignments below are a provisional world-building sketch, not fixed content IDs or a requirement for the first-three-floor implementation.

```
[Surface Kingdom]
       │
       ▼
┌────────────────────────────────────────────────────────┐
│ Depth 1–3:   Realm 1 — The Sunken Garrison             │
│ Depth 4–6:   Realm 2 — The Drowned Forum / Cisterns    │
│ Depth 7–9:   Realm 3 — The Jade Undergrowth            │
│ Depth 10–12: Realm 4 — The Ashen Scriptorium           │
│ Depth 13–15: Realm 5 — The Iron Foundry                │
│ Depth 16–18: Realm 6 — The Glacial Necropolis          │
│ Depth 19–21: Realm 7 — The Gilded Pantheon             │
│ Depth 22–24: Realm 8 — The Primordial Rift             │
└────────────────────────────────────────────────────────┘
       │
       ▼
[The World-Anchor: Victory]
```

### High-Level Epoch Profiles

1. **The Sunken Garrison (Depths 1–3):** Feudal military fortress; damp stone cellars, barracks, armories, and iron portcullises. Scavenging vermin and brittle skeleton sentries.
2. **The Drowned Forum / Cisterns (Depths 4–6):** Classical antiquity; flooded colonnades, aqueducts, and sunken arches. Aquatic lurkers, spear guards, and slowing water hazards.
3. **The Jade Undergrowth (Depths 7–9):** Primeval jungle terraces; roots tearing through stone, toxic spore pods, and fast predatory reptiles.
4. **The Ashen Scriptorium (Depths 10–12):** Ancient occult scholarship; multi-tiered library shelves, wax homunculi, living grimoires, and teleporting casters.
5. **The Iron Foundry (Depths 13–15):** Brutalist precursor industry; iron catwalks over open slag channels, steam pistons, and armored automata.
6. **The Glacial Necropolis (Depths 16–18):** Boreal permafrost tombs; translucent ice sheets, frost wraiths, yeti brutes, and frozen footing.
7. **The Gilded Pantheon (Depths 19–21):** Divine monuments; colossal golden obelisks, skybridges over chasms, leaping vanguards, and sweeping solar beams.
8. **The Primordial Rift (Depths 22–24):** Bedrock of reality; floating void monoliths, gravitational distortions, phase stalkers, and the World-Anchor.

---

## 3. The Three-Depth Realm Cadence

Every realm spans three floors with a recognizable escalation. These are intended roles, not a claim that every encounter, room or merchant is implemented.

| Floor | Progression role | Content direction |
| --- | --- | --- |
| 1 | Ingress and reconnaissance | Introduce realm architecture, baseline monsters and hazards; restrained patrols and optional supplies |
| 2 | Escalation and swarm pressure | Nest chambers, denser encounters, specialists and optional guarded rewards |
| 3 | Sanctum and Gatekeeper trial | Gatekeeper location and visibly sealed descent; a peaceful merchant outpost is later content |

Realm room pools and floor recipes select architecture appropriate to each depth. Required presence, required traversal and required encounter completion are distinct constraints, as defined in [Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md). The Garrison's first floor emphasizes barracks and supplies, its second a prison wing and nest chamber, and its third the Warden's domain. Existing rooms and skeletons can stand in for these roles while content grows.

### Ingress and reconnaissance

Introduce baseline monster roles through small, readable encounters. Room purposes guide supplies and optional risks. Mechanical locks initially protect optional rooms or rewards; matching keys and lockpicks follow the shared access design rather than a universal colored-key scheme.

### Escalation and swarm pressure

Nests create reinforcement pressure in selected rooms; optional elite or locked reward branches invite resource decisions. The current finite-summon implementation is documented in [Level design](level-design.md#first-realm-encounter-prototype). Encounter counts, caps and pacing remain tuning choices rather than an obligation to spawn indefinitely.

### Sanctum and Gatekeeper trial

The staircase is visible but sealed by the realm's Gatekeeper condition. Finding the stairs or bypassing the boss spatially does not permit descent. Defeating the Gatekeeper automatically removes the visible seal and permanently enables descent for that run. A specific traversal constraint or arena lockdown can be authored for an encounter, but neither is required for this progression rule.

A future sanctum can place a peaceful merchant near the entrance. High-tier boss rewards and Realm Sigils remain intended reward/attunement content; they are not random prerequisites for opening the initial seal. A guaranteed boss-key variant is possible later under the shared required-access guarantees.

---

## 4. Prototype Environmental Styling (Using Existing Dungeon Assets)

To prototype without immediately modeling new 3D tile kits, later realm experiments can explore the Garrison, Cisterns and Scriptorium by re-dressing the existing modular dungeon assets with distinct lighting, fog, props, and room selections.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Realm 1: The Sunken Garrison (Depths 1–3)                                  │
│ • Architecture: Barracks, armories, mess halls, prison cells, Warden's hall.       │
│ • Key Props: Bunk beds, weapon racks, tables, wooden crates, iron gates.   │
│ • Atmosphere: Warm amber torchlight (2600K), dark gray stone, light fog.   │
│ • Gameplay Accent: Chokepoints, heavy doors, corridor duels.               │
├────────────────────────────────────────────────────────────────────────────┤
│ Realm 2: The Drowned Forum / Cisterns (Depths 4–6)                         │
│ • Architecture: Sunken courtyards, sewer crossings, arched water channels. │
│ • Key Props: Sewer grates, drainage sluices, rubble mounds, broken stone.  │
│ • Atmosphere: Cold teal/aquamarine light, damp reflections, dense mist.    │
│ • Gameplay Accent: Shallow water slows movement by 20% off dry slabs.      │
├────────────────────────────────────────────────────────────────────────────┤
│ Realm 4: The Ashen Scriptorium (Depths 10–12)                              │
│ • Architecture: Tall library halls, secluded reading alcoves, rotundas.    │
│ • Key Props: Large bookshelves, candle clusters, study desks, alchemy gear.│
│ • Atmosphere: Deep violet/indigo shadows, golden candlelight, purple haze. │
│ • Gameplay Accent: Bookcases break line of sight against ranged snipers.   │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. First-realm roster and encounters

The initial playable roster is the skeleton minion, shield-bearing skeleton warrior, skeleton crossbowman, Plague Rat and Forsaken Warden. These replace the earlier speculative Garrison roster; deserters, sergeants and extra bosses are not required. Keep the three existing skeletons and approved Warden visual. Add the rat through the Studio export and Godot composition workflow.

The first floor introduces direct melee, frontal defense and readable ranged pressure. The second combines these roles with rats and finite nest pressure. The third leads to the Warden and rewards learned positioning/defense. Monster identity, fixed strength, movement/attack/defense policies, threat and XP are defined in [Monster design and authoring](monster-authoring-design.md). Concrete numbers live in authored Godot Resources; [Level design](level-design.md#first-realm-encounter-prototype) records current implemented budgets, nests and gatekeeper behavior.

The Warden remains a single-phase encounter for the initial rollout. A larger silhouette and committed swings can make it distinct without phase scripting. Summoning, leaps, shockwaves, weapon changes and enrage phases are deferred rather than prerequisites for a playable third floor.

Later realm monsters and bosses remain open design work. Choose them to support a realm's tactical focus after the first realm is enjoyable. Earlier detailed HP/damage tables, threat-class ranges and room-budget formulas have been retired: they were speculative and conflicted with the implemented roster. A later realm need not reuse those exact creatures or numbers.

## 6. Encounter direction

Mix complementary roles within finite threat budgets and respect available fighting space. Selection frequency, threat cost and actual combat strength are separate tuning decisions. Realm pools and depth eligibility control where monsters appear; monsters do not automatically scale with dungeon depth.

Nests provide bounded reinforcement pressure, with authored interval, batch size, simultaneous cap and total summon slots. Destroying a source stops further summons; the current implementation also collapses surviving bound summons without rewards. See [Level design](level-design.md#first-realm-encounter-prototype) for current persistence and values. The monster rollout may change the summoned roster without changing those ownership rules.
