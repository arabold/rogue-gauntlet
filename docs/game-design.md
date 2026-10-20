# Rogue Gauntlet — Game Design Document

Status: living design draft, October 19, 2026. This document describes the intended game; it is not a claim that every feature is implemented. The [development roadmap](development-roadmap.md) separates existing foundations from upcoming work.

Use the [documentation index](README.md) for current implemented systems and proposed technical plans. Seeded generation already exists; [Randomness and Seeds](randomness-and-seeds.md) records its current behavior and limitations. This GDD owns the intended experience, not the implementation inventory.

## Game vision

Rogue Gauntlet is a real-time dungeon crawler about descending into an unpredictable dungeon, surviving dangerous encounters, and making the most of the equipment and supplies found along the way. Each run should produce a different combination of discoveries, risks, and character-building decisions.

The player should regularly face choices such as: spend a healing potion now or save it; wear an unidentified amulet or wait; explore a locked side room or conserve supplies; buy reliable armor or an unusual tool; descend with current equipment or search further.

### Creative references

These references guide our own design choices. Each contributes a different part of the intended experience; adopting a reference does not commit us to its entire ruleset.

| Reference | What we want to draw from it |
| --- | --- |
| Rogue and NetHack | Discovery, uncertainty, resource management, and objects with useful interactions |
| [Shattered Pixel Dungeon](https://shatteredpixel.com/) | A modern reference for making traditional dungeon exploration, item discovery, and run decisions approachable and readable |
| Diablo | The pull of descending, fighting, finding better equipment, and developing a character; procedural replayability combined with authored encounters |
| Gauntlet | Immediate action, recognizable fantasy roles, and pressure from groups of enemies |
| [Vampire Survivors](https://poncle.games/vampire-survivors) | The excitement of overwhelming monster numbers, crowd pressure, and builds that can handle escalating threats |
| Dungeons & Dragons games, including the Baldur's Gate series | Class identity, distinctive magical objects, tactical preparation, and multiple ways to approach encounters |
| The Witcher series | Equipment identity, preparation for danger, and objects that belong to the world |

Shattered Pixel Dungeon's [official repository overview](https://github.com/00-Evan/shattered-pixel-dungeon) provides a primary reference alongside its website. The choices above describe our intended inspiration rather than a feature checklist for those games.

## Design pillars

1. **Discovery changes decisions.** Learning what an item does, revealing a room, or recognizing an enemy should create an opportunity to act differently.
2. **Objects have a purpose.** Equipment, food, tools, and treasure should contribute to survival, exploration, access, or trade. Asset availability alone does not justify adding an item.
3. **Preparation matters alongside execution.** Positioning and action timing matter in combat; inventory choices and supplies matter before and between encounters.
4. **Depth increases opportunity and danger.** Deeper floors offer stronger rewards and harder choices while keeping basic supplies and utility items relevant.
5. **Randomness creates situations the player can respond to.** Essential routes remain solvable, important feedback is readable, and player knowledge improves future decisions.

## Core gameplay loop

Choose a character → enter the dungeon → explore and fight → discover and evaluate loot → recover, equip, or trade → descend into greater danger.

Within a floor, players alternate between combat, exploration, and preparation. Optional rooms, chests, and shops offer reasons to take detours. Descending should be a meaningful commitment to increased danger, rather than an automatic action after clearing every room.

### Example intended play session

This walkthrough illustrates the proposed experience; it does not describe a currently complete build.

The player chooses a Knight and enters with basic equipment and limited backpack space. A side room contains an apple, plain bolts, and a locked chest. Each apple occupies its own slot, whether wholesome or secretly poisoned; identical plain bolts share a stack. The player leaves room for later discoveries rather than collecting everything automatically.

A group of enemies pressures the Knight into a corridor. Positioning and equipment help control the fight. One defeated enemy drops armor, while further exploration reveals a matching key. Opening the optional chest yields an unidentified amulet: wearing it could help now, but waiting for identification avoids an uncertain effect.

With the backpack nearly full, the player chooses between carrying spare armor, food, and another utility item. A shop offers a chance to sell surplus equipment and buy recovery supplies appropriate to this depth. A larger backpack could become a later reward or purchase; its exact availability remains a tuning decision.

Before descending, the player uses a lamp to explore a dark branch, spending fuel in exchange for better visibility. A later encounter brings a much larger crowd, changing the immediate problem from dueling an enemy to finding space and controlling the approach. The player survives, decides whether current supplies are enough, and descends. Saving and returning preserves discoveries, carried objects, spent fuel, and purchased stock.

The intended first complete run needs a defined endpoint and a clear loss condition. Its exact objective, length, death rules, and any return journey remain open decisions. The proposed starting point is a finite descent with a final encounter or objective, and death ending the active run. Save/load should support resuming play without changing previously created items or rewards.

Permanent power progression between runs is undecided. Do not make it a dependency of the first complete run.

## Characters and combat

Character classes provide distinct starting attributes, equipment, and strengths. Existing classes are Barbarian, Knight, Rogue, and Mage. Items found during a run should allow adaptation beyond the starting loadout.

Combat uses real-time movement, attacks, and timed actions. Weapons should differ through reach, handling, damage, and situational usefulness where those differences are supported by gameplay. Armor offers protection with understandable tradeoffs; heavier armor may exchange mobility or ease of use for defense. Exact penalties and equipment requirements need playtesting.

Weapons must communicate their handling through appropriate attack animations: one- and two-handed swings, axe chops, spear thrusts, bow shots, crossbow shots, and casting are distinct authored styles. Handedness alone does not choose the motion. Each character or monster maps its attacks to animations appropriate to its own body and rig, with visible contact or projectile release synchronized to gameplay. Melee uses generous authored contact volumes for reliable hits. Optional slash/chop swooshes visually follow the animated blade while gameplay volumes remain independently tuned; thrusts and natural attacks can omit a trail. Cosmetic fading must not extend the damage window. See the [attack animation design](specs/attack-animation-plan.md).

Enemy variety should encourage different responses through movement, perception, attack patterns, and vulnerabilities. More health and damage alone should not be the only source of deeper-floor difficulty.

Encounter pacing should support both smaller tactical fights and moments of overwhelming crowd pressure inspired by Gauntlet and Vampire Survivors. Large groups should reward positioning, chokepoints, and appropriate builds while preserving readable threats and usable escape routes. Exact monster counts, encounter frequency, and performance budgets require prototypes and playtesting. Constant maximum density, automatic attacks, and arena-only progression are not implied by this influence.

Current combat and character foundations are described in [weapon attacks](weapon-attack-system.md) and [character classes](character-classes.md).

## Dungeon exploration

Floors combine procedural layout with authored rooms. Players discover space through exploration, encounter enemies and hazards, find loose items, open chests, and reach the next descent.

Seeded generation is a core principle: the run seed and level-specific generators recreate the initial scenario, including encounter choices and positions. Separate random streams keep unrelated gameplay and cosmetic activity from altering that baseline. Save only differences caused by play, such as defeated monsters, collected loot, opened chests, changed items, and player drops. Regeneration followed by those changes restores the supported world state; untouched future floors need no stored state. Whether moved or injured living enemies reset on revisit or retain actor-specific changes remains an explicit design decision.

Repeatability applies to the same build, content, and relevant inputs. Changing code, content ordering, or RNG consumption between builds may change the whole scenario, which is acceptable during development. Separately seeded level, monster, and item generators limit accidental coupling; they do not promise that monsters remain in the same places if the level layout changes. Historical seed outputs do not need compatibility support.

Room purpose should influence its contents: a storeroom can contain supplies, a treasury can reward overcoming a lock, and a dangerous guarded room can offer better equipment. Start with a small set of readable room roles and expand them as their gameplay becomes useful.

Locks initially protect optional rewards. Any future required lock must have a guaranteed solution reachable before the lock, without depending on a random shop purchase or monster drop. Tools may provide alternative access where explicitly supported.

Proposed darkness and portable lighting should affect exploration or perception enough to justify carrying fuel. Fog of war represents discovered space; illumination represents current visibility. Their exact relationship, and whether enemies respond to carried light, remain to be designed.

See [level design](level-design.md) for the current generation and visibility implementation.

## Items and their lifecycle

Every item has an authored definition, a particular instance when state is needed, and a presentation that reflects what the player knows. The asset studio supplies visuals; gameplay authoring supplies rules, effects, availability, and value. One model can serve multiple definitions or appearances, and multiple models need not imply different mechanics.

| Item family | Gameplay purpose | Initial intended behavior |
| --- | --- | --- |
| Weapons and shields | Combat choices and build progression | Equip; apply authored properties and eligible rolled affixes |
| Armor | Protection and build tradeoffs | Equip into supported slots; show accurate effects and compatible visuals |
| Food and drink | Recovery and exploration supplies | Consume for authored effects; eligible food can be poisoned when created |
| Potions | Immediate tactical effects | Consume; discover hidden identities through supported identification rules |
| Scrolls | Situational solutions | Read for an effect, sometimes selecting a target |
| Rings and amulets | Passive specialization and discovery | Equip for benefits or drawbacks; use the identification system |
| Lamps and torches | Portable illumination | Activate or extinguish; consume fuel while active |
| Keys | Access to matching locks | Unlock by an explicit matching rule; consumption depends on the key definition |
| Tools | Alternative ways to interact with the dungeon | Start with lockpicks; add other tools alongside the world interactions they require |
| Currency | Purchasing decisions | Collect, spend, and receive through trade |

### Instance state and persistence

Creation initializes an item's random state once. Pickup, inventory transfer, equipping, dropping, trading, floor transitions, and save/load preserve that state. Loading restores results rather than rolling them again. During development before public release, this guarantee applies to the current supported save version. Saves may break between versions; incompatible or unversioned saves require a new run and are not migrated.

Relevant state includes equipment rarity and affixes, food contamination, remaining fuel or charges, and a key's lock association. Store only state required by supported behavior. Authored definitions remain unchanged by play.

Stackability is explicitly authored and defaults to disabled. An approved stackable variety has interchangeable units with no individual variation, including hidden properties. Any item with per-unit rolls or mutable state remains individual. Splitting and merging preserve variety and total quantity.

Plain arrows or bolts are examples of potential stackable varieties. Blunt, metal, splitting, and explosive bolts are separate varieties; only units of the same variety stack. These are candidate ammunition types, not a commitment that all four are implemented or required for the first playable.

Food does not stack, including food that happens to be wholesome. Each piece retains its own contamination state and is selected individually when consumed. Stack behavior therefore cannot distinguish poisoned food from unpoisoned food.

Persistence must cover carried items, player-dropped items, remaining floor loot, opened or emptied containers, and shop stock. Recreate unchanged content from seeds and store removals, overrides, and additions rather than complete lists of untouched floor objects. Regenerating a layout must not replenish rewards already collected or discard player changes.

### Backpack capacity and carrying choices

Backpack capacity is a gameplay resource. Use **20 occupied slots as the initial tuning proposal**: one individual item takes one slot, and one allowed stack takes one slot. A full backpack can accept more units into an existing compatible stack up to its authored limit; creating a new stack or receiving an individual item requires a free slot. Stack limits remain a tuning decision.

Larger backpacks become available later, increasing carrying capacity without changing which items may stack. Capacity upgrades and contents persist through save/load. The size increments, acquisition method, and timing remain open. Nested bags and separate inventories inside backpacks are outside the initial scope.

Decide whether equipped items, currency, and progression keys count toward the limit before implementing capacity rules. In particular, required progression objects need an access policy that cannot strand the player because the backpack is full. Pickups and purchases must explain capacity failures and preserve the world item or shop stock when acquisition fails.

### Use and player knowledge

Offer verbs appropriate to the object: eat, drink, read, equip, light, extinguish, unlock, or apply. Validate targets and prerequisites before committing the item or its charges. Failed or cancelled use should have clear, deliberately authored costs; existing targeted-scroll cancellation preserves the scroll.

Keep type knowledge separate from instance knowledge. Discovering a potion's effect can identify that type across the run. Discovering that one apple is poisoned must not mark every apple as poisoned. Current jewelry identification on wearing remains the baseline; restrictive curses and curse removal are future proposals.

Names, models, descriptions, rarity colors, stacking behavior, and prices must respect the intended information boundary. Players should understand observed effects without unintentionally seeing hidden properties.

Food initially provides recovery and contamination risk. Hunger, spoilage, cooking, and nutrition are deferred until exploration pacing demonstrates a need for them.

See [stats and itemization](stats-and-itemization.md) and [item identification](item-identification-system.md) for existing rules and implementation.

## Loot generation and depth progression

Monsters, chests, floor finds, and shops use consistent eligibility and item-creation rules. Their reward profiles differ according to their role.

Generation considers dungeon depth, source, and any supported region or reward modifier. It first determines reward categories and quantities, then selects eligible definitions and initializes instances. Adding many new weapons should not inadvertently reduce the supply of food or potions.

| Source | Intended reward character |
| --- | --- |
| Humanoid enemies | Appropriate equipment, currency, and carried supplies |
| Beasts and other creatures | Thematically appropriate rewards; avoid arbitrary full equipment pools |
| Ordinary chests | Several opportunities for supplies, currency, or equipment |
| Guarded or locked chests | Improved reward opportunity for the additional risk or access cost |
| Floor finds | Useful supplies, occasional equipment, and exploration surprises |
| Shops | Curated, purchasable choices appropriate to local depth |

Keep four concepts distinct: **tier** is base equipment strength; **rarity** describes its rolled magical properties; **potency** describes an effect's magnitude; **availability** describes how often something can be found. A high-tier weapon can have common rarity. A basic utility scroll can remain valuable deep in the dungeon.

Deeper floors shift rewards toward stronger bases, eligible affixes, and suitable consumable potency. Use overlapping bands to retain variety and occasional exceptional finds. Set explicit bounds on above-band rewards. Keep essential supplies available instead of replacing every object with a stronger version.

Depth drives world progression; player level should not silently rescale existing items or shop stock. Numerical curves, drop rates, category budgets, and reward bonuses remain tuning decisions.

## Shops and economy

Occasional shops provide a chance to recover, address a weakness, or buy an interesting alternative. Their frequency and guaranteed placement rules remain open.

Proposed stock consists of a dependable core of basic supplies, several equipment options from the local depth band, and a small utility selection. An occasional expensive standout can appear within the allowed reward bounds. Stock should offer useful choices across builds without guaranteeing the ideal upgrade.

Generate stock once for the shop and persist remaining quantities, item state, and transactions. Reopening the interface, revisiting a floor, or loading a save must preserve purchases. Restocking is deferred until a deliberate rule exists.

Buying and selling must transfer items atomically: insufficient funds or inventory space cannot lose currency or items. Set buy/sell spreads to prevent immediate resale profit. Decide how merchants handle unidentified or contaminated goods before implementing prices that could reveal hidden properties.

## Player experience and presentation

The world model, inventory preview, equipped appearance, and item details should describe the same object. Equipment presentation must work with supported character rigs; gameplay stats alone do not complete wearable armor integration.

Inventory interactions should make available actions, equipment comparisons, quantities, fuel or charges when known, and failure reasons understandable. Important effects need visible feedback. Dangerous encounters and item use must remain readable at the actual gameplay camera distance.

Whether inventory and shop interfaces pause real-time play remains an explicit design decision. Establish it before balancing consumable timing and trading safety.

## First playable scope

Build a short connected descent demonstrating combat, exploration, item discovery, equipment progression, one meaningful tool interaction, trade, and persistent state. Initial content should include food, a small armor selection, existing potion/scroll/jewelry behavior, one fueled light source, matching keys and optional locked chests, and one shop type.

Crafting, complex survival simulation, broad tool combinations, elaborate merchant simulation, permanent power progression, and extensive biome content are outside this initial scope. A tool enters the playable catalog when its corresponding interaction is ready.

## Decisions to resolve through design and playtesting

| Decision | Working direction | Resolve before |
| --- | --- | --- |
| Run objective, length, and victory | Finite descent with a final objective | First complete run milestone |
| Death and between-run progression | Death ends the run; permanent power undecided | First complete run milestone |
| Inventory and shop pause behavior | Open | Balancing item-use timing and shop encounters |
| Food contamination and detection | Per-instance creation roll; probability and detection open | Food integration |
| Backpack capacity and upgrades | Explicit stackability; food individual; initial 20-slot proposal; larger packs later | Inventory integration |
| Capacity accounting and stack limits | Equipped items, currency, key handling, stack caps, and upgrade sizes open | Inventory integration |
| Crowd encounter scale and pacing | Mix tactical fights with occasional overwhelming groups; counts and performance targets open | Encounter balancing |
| Lighting, fuel pacing, and equipment occupancy | One useful portable light; exact visibility and hand/slot rules open | Lighting integration |
| Armor tradeoffs and visual coverage | Small set with readable differences | Armor integration |
| Keys and lockpicks | Matching keys; optional locked rewards first | Lock integration |
| Merchant knowledge, prices, and frequency | Persistent finite stock scaled by depth | Shop integration |

Update this document when intended behavior changes. Record implementation details in the linked feature documents and track delivery in the [roadmap](development-roadmap.md).

## Design-document references

- **Condor's Diablo pitch (1994):** reviewed from the supplied eight-page PDF; a [public copy](https://www.graybeardgames.com/download/diablo_pitch.pdf) is also linked by the discussion below. Its overview, gameplay walkthrough, procedural floors with authored set pieces, and attention to replayability are useful references for communicating the game. It is an early proposal, including turn-based combat, rather than a specification of the shipped game. Its historical scope, schedule, and business proposals are not requirements for Rogue Gauntlet.
- **[Finished Game Design Document Examples?](https://www.reddit.com/r/gamedesign/comments/7ze7xq/finished_game_design_document_examples/):** a discovery index for examples and a discussion of pitch, concept, and evolving design documents. Our working format keeps the vision and player experience here, delivery outcomes in the roadmap, and implementation detail in feature documents. Examples inform the format; they do not settle our design decisions.
