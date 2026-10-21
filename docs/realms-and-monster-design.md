# Realms and Monster Design

Status: concept and system design document, October 2026. Defines the 24-depth world concept, the eight Sunken Epochs, the 3-depth realm cadence, and the complete monster and boss bestiary for the initial prototype realms (Realms 1, 2, and 4). World themes and encounter concepts live here; room/access/discovery rules live in [Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md). See the [Game Design Document](game-design.md), current [Level Design](level-design.md), and remaining [implementation plan](specs/level-progression-prototype-plan.md) for their separate responsibilities.

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

The descent spans 24 depths partitioned into eight distinct, 3-depth realms. Each realm represents an entirely different historical civilization with its own architectural kits, hazards, room topologies, and monster ecologies.

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

To prototype without immediately modeling new 3D tile kits, the initial implementation targets **Realms 1, 2, and 4** by re-dressing the existing modular dungeon assets with distinct lighting, fog, props, and room selections.

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

## 5. The Prototype Bestiary (Realms 1, 2, and 4)

### Reference Player Baselines
- Player HP: 90–120 (Knight: 120, Barbarian: 115, Rogue: 95, Mage: 90).
- Player Walk Speed: ~4.0 units/second.
- Player Basic Weapon Damage: ~6–15 per hit (depending on weapon and attributes).

---

### Realm 1: The Sunken Garrison (Depths 1–3)
*Tactical focus: Teaching weapon reach, recovery timing, stamina control, and doorway chokepoints.*

| Monster | Silhouette & Size | Threat | HP | Damage | Speed | Tactical Behavior & Attack Profile |
|---|---|---|---|---|---|---|
| **Plague Rat** *(Swarmer)* | Tiny ($0.4\text{m}$) quadruped | 1 | 5 | 2–4 | 4.6 | Fast scuttler. Lunges forward in packs of 3–5. Easily cleaved in chokepoints. Emitted by *Bone/Carcass Nests* on Depth 2. |
| **Deserter Scout** *(Skirmisher)* | Medium ($1.0\text{m}$) humanoid | 2 | 10 | 4–7 | 3.4 | Light shortsword slash ($0.5\text{s}$ windup). Steps backward after striking to evade counter-swings. |
| **Brittle Skeleton** *(Bruiser)* | Medium ($1.1\text{m}$) undead | 4 | 18 | 5–9 | 2.5 | Wields a rusted buckler (blocks 50% frontal damage until poise broken) and strikes with heavy axe chops. |
| **Renegade Crossbowman** *(Specialist)* | Medium ($1.0\text{m}$) humanoid | 6 | 10 | 6–10 | 2.8 | Maintains distance at corridor ends; fires a high-damage bolt preceded by a 0.8s red line telegraph. |
| **Ironclad Sergeant** *(Minor Boss / Elite)* | Large ($1.5\text{m}$) armored brute | 12 | 45 | 9–15 | 2.7 | Wields a greatsword with a wide $180^\circ$ sweep that knocks players back. Guards optional Depth 2 vaults. |

#### Realm 1 Gatekeeper (Depth 3 Boss): The Forsaken Warden
- **Silhouette & Size:** Huge ($2.0\text{m}$ tall armored revenant in battered full plate).
- **Attributes:** 110 HP, 10–18 damage per hit, 2.8 move speed.
- **Location and access:** Warden's hall or barracks courtyard, with a visibly sealed descent that unlocks automatically on defeat. Iron-portcullis arena lockdown is an optional later encounter feature.
- **Three-Phase Encounter:**
  1. *Phase 1 ($100\%\rightarrow 65\%$ HP):* Heavy two-handed executioner swings and short-range shield bashes. Teaches sidestepping and attack windows.
  2. *Phase 2 ($65\%\rightarrow 30\%$ HP):* War horn roar (summons 3 Plague Rats) followed by a leaping ground slam with an expanding ring shockwave.
  3. *Phase 3 ($<30\%$ HP):* Enraged frenzy. Drops the shield to two-hand the blade; attack recovery time decreases by 30%, leaving ignited friction trails on the stone.

---

### Realm 2: The Drowned Cisterns / Forum (Depths 4–6)
*Tactical focus: Positioning on dry stone versus wading in water; dodging thrust attacks; managing aquatic ambushes.*

| Monster | Silhouette & Size | Threat | HP | Damage | Speed | Tactical Behavior & Attack Profile |
|---|---|---|---|---|---|---|
| **Mire Leech** *(Swarmer)* | Small ($0.5\text{m}$) slithering worm | 1 | 7 | 3–5 | 4.4 water / 2.2 land | Hides submerged in shallow water; lunges onto dry platforms in pairs. Emitted by *Algae Basins* on Depth 5. |
| **Drowned Spearman** *(Skirmisher)* | Medium ($1.1\text{m}$) water-logged zombie | 2 | 14 | 5–8 | 2.9 | Thrusts with a long polearm ($3.2\text{m}$ reach). Pushes players backward off dry stones into deep water. |
| **Slime Hulk** *(Bruiser)* | Large ($1.8\text{m}$ wide) amorphous blob | 5 | 28 | 8–13 | 2.4 | Immune to water slow penalties. Unleashes a heavy body slam that creates a caustic splash zone. |
| **Siren Harpy** *(Specialist)* | Medium ($1.2\text{m}$) flying raptor-human | 7 | 12 | 5–8 | 3.8 | Flies over water and pits; unleashes a concussive shriek that interrupts player weapon actions. |
| **Abyssal Eel-Drake** *(Minor Boss / Elite)* | Large ($2.4\text{m}$ long) aquatic serpent | 14 | 55 | 10–16 | 4.2 water | Dives beneath water surfaces, resurfaces behind the player, and emits a short-range electrical shockwave. |

#### Realm 2 Gatekeeper (Depth 6 Boss): The Gorgon Praetor
- **Silhouette & Size:** Large ($1.8\text{m}$ tall, half-humanoid, half-serpentine stone guardian).
- **Attributes:** 160 HP, 12–20 damage, 3.2 move speed.
- **Arena:** Flooded circular forum with four raised dry marble platforms and crumbling pillars.
- **Three-Phase Encounter:**
  1. *Phase 1 ($100\%\rightarrow 65\%$ HP):* Sweeping bronze halberd combos and tail sweeps that knock the player into water channels.
  2. *Phase 2 ($65\%\rightarrow 35\%$ HP):* **Petrifying Gaze Telegraph**: Eyes flare emerald for $1.2\text{s}$ with a sharp audio cue. If the player is facing the boss when it fires, they are turned to stone (frozen in place) for $1.5\text{s}$. The player must turn away or duck behind a pillar.
  3. *Phase 3 ($<35\%$ HP):* Floods the outer perimeter completely, breaking two pillars and summoning Mire Leech swarms while moving faster across water.

---

### Realm 4: The Ashen Scriptorium (Depths 10–12)
*Tactical focus: Countering teleporting ranged casters; damage-type choices (blunt vs. slashing); breaking line-of-sight with tall bookcases.*

| Monster | Silhouette & Size | Threat | HP | Damage | Speed | Tactical Behavior & Attack Profile |
|---|---|---|---|---|---|---|
| **Flying Grimoire** *(Swarmer)* | Small ($0.4\text{m}$) animated book | 1 | 9 | 3–6 | 4.0 | Erratic fluttering flyer. Casts sharp paper volleys from mid-range. Emitted by *Cursed Lecterns* on Depth 11. |
| **Wax Homunculus** *(Skirmisher)* | Medium ($1.0\text{m}$) malleable clay/wax form | 3 | 18 | 6–10 | 3.5 | Fast blunt fists. **Special mechanic:** If slain by a slashing weapon, splits into 2 mini-homunculi ($6\text{HP}$ each). Blunt/magic damage kills cleanly. |
| **Granite Scribe** *(Bruiser)* | Large ($1.6\text{m}$) stone gargoyle | 6 | 36 | 10–16 | 2.2 | Sits motionless on plinths until player approaches within $6\text{m}$; awakens with a diving ground pound. High armor defense. |
| **Curator Magus** *(Specialist)* | Medium ($1.2\text{m}$) floating hooded caster | 8 | 16 | 7–13 | 3.0 | Teleports when melee attackers get close. Casts a gravitational vortex that drags actors toward its center. |
| **Animated Book Golem** *(Minor Boss / Elite)* | Large ($2.2\text{m}$) swirling cyclone of folios | 16 | 75 | 12–18 | 3.2 | Spins in a whirlwind state deflecting arrows/bolts and launching page shrapnel radially. |

#### Realm 4 Gatekeeper (Depth 12 Boss): The High Archivist (Lamassu Construct)
- **Silhouette & Size:** Huge ($2.4\text{m}$ winged stone lion construct with a bearded sage head).
- **Attributes:** 240 HP, 15–26 damage, 3.0 move speed.
- **Arena:** Grand reading rotunda with four massive stone book archives.
- **Three-Phase Encounter:**
  1. *Phase 1 ($100\%\rightarrow 65\%$ HP):* Dual burning staves firing rotating radial arcane runes. Player must use the library shelves to break line of sight.
  2. *Phase 2 ($65\%\rightarrow 30\%$ HP):* Drops **Arcane Silence Domes** onto the arena floor (standing in them disables scrolls and magic weapons). Awakens two Granite Scribes from wall alcoves.
  3. *Phase 3 ($<30\%$ HP):* Collapses two of the book shelves into rubble, cutting down safe cover, and unleashes rapid charging rushes across the room.

---

## 6. Spawning Rules and Threat Budgets

### Threat Cost Classes
- **Swarmer (1 pt):** Fragile crowd pressure, easily cleaved in groups.
- **Skirmisher (2–3 pts):** Standard direct combatant.
- **Bruiser (4–6 pts):** Tanky frontline, forces repositioning and stamina care.
- **Specialist (6–8 pts):** Ranged sniper, buffer, or disruptor.
- **Elite / Minor Boss (12–16 pts):** High health, guards major rewards.
- **Gatekeeper (25+ pts):** Realm climax encounter.

### Room Threat Budgets
Every placed room receives a threat budget calculated as:
$$\text{Budget} = \text{BaseBudget}(\text{Depth}) \times \text{RoomRoleMultiplier} \times \text{SizeFactor}$$

- **Corridors:** 0–2 points (occasional solo wanderer or pair of swarmers).
- **Standard Rooms (Floor 1):** 4–8 points (e.g., 2 Skirmishers + 2 Swarmers).
- **Standard Rooms (Floor 2):** 10–16 points (e.g., 1 Bruiser + 2 Skirmishers + 4 Swarmers).
- **Guarded Vaults:** 15–25 points (e.g., 1 Specialist + 1 Bruiser + 4 Skirmishers).
- **Nest Chambers (Floor 2):** 1 Destructible Nest + 4–8 initial guard points.

### Destructible Nests (Swarm Mechanics)

Damageable realm props (Bone Piles, Algae Basins, Cursed Lecterns) supply bounded reinforcement pressure. Their authored encounter tuning controls interval, batch size, simultaneous cap and total summon budget. Destroying the source stops further summons. The first Garrison prototype has finite skeleton slots and collapses surviving bound summons on destruction; see [Level design](level-design.md#first-realm-encounter-prototype) for the implemented values. Expanding pressure or changing summon ownership requires explicit balance and persistence work.
