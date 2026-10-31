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

The current full-run concept is a 24-depth descent divided into 8 realms (the Sunken Epochs), each spanning 3 depths. Later realm order and rosters remain provisional; the immediate priority is making depths 1–3 playable and enjoyable. The final objective is reaching Depth 24 to confront the guardian of the World-Anchor and sever the gravitational tether dragging civilizations into the crust, achieving run victory. Death ends the active run. An optional return journey (an escape ascent against collapsing ruins) is reserved as an advanced post-victory challenge mode rather than a requirement for the initial game. Save/load preserves discoveries, carried inventory, spent resources, shop state, and persistent floor modifications.

Permanent power progression between runs is undecided. Do not make it a dependency of the first complete run.

## Characters and combat

[Monster design and authoring](monster-authoring-design.md) defines the first-realm roster and editable Godot composition workflow. Monsters use fixed authored strength, a single melee or projectile attack, independent defensive capabilities and basic movement strategies. More advanced tactics follow first-realm playtesting.

Character classes provide distinct starting attributes, equipment, and strengths. Existing classes are Barbarian, Knight, Rogue, and Mage. Items found during a run should allow adaptation beyond the starting loadout.

Combat uses real-time movement, attacks, and timed actions. Weapons should differ through reach, handling, damage, and situational usefulness where those differences are supported by gameplay. Armor offers protection with understandable tradeoffs; heavier armor may exchange mobility or ease of use for defense. Exact penalties and equipment requirements need playtesting.

Weapons must communicate their handling through appropriate attack animations: one- and two-handed swings, axe chops, spear thrusts, bow shots, crossbow shots, and casting are distinct authored styles. Handedness alone does not choose the motion. Each character or monster maps its attacks to animations appropriate to its own body and rig, with visible contact or projectile release synchronized to gameplay. Melee uses generous authored contact volumes for reliable hits. Optional slash/chop swooshes visually follow the animated blade while gameplay volumes remain independently tuned; thrusts and natural attacks can omit a trail. Cosmetic fading must not extend the damage window. See the [attack animation design](weapon-attack-system.md#attack-styles-rig-profiles-and-timing).

Player actions should form one coherent model: dedicated primary action, secondary technique/spell, dodge, guard/parry and interaction inputs, plus four item quick-action slots for potions, scrolls and other eligible consumables. The equipped weapon supplies primary and secondary independently of those slots: normal/heavy or special martial attacks, or a wand's or staff's free magic missile plus its bound spell. Controls are keyboard-first, with gamepad parity and no required mouse targeting. Successful pickups may auto-assign eligible items to unassigned slots without overwriting the player's choices or revealing unknown properties.

Magic belongs to the weapon. Every wand (one-handed) and staff (two-handed) fires a free homing magic missile and carries one of 20 elemental spells, five each for earth, fire, water and air, cast on the secondary input for mana. Nothing is learned and there are no skill points: deeper floors yield wands and staves of higher tiers, whose spells are stronger, and their damage bonuses raise the spell like any weapon's. A staff gives up the off hand for a stronger missile. Any class can use any wand or staff; Intelligence sizes the mana pool, so a Mage casts far more often than a Barbarian or Knight. Mana and spell cooldowns are character-owned so swapping cannot refresh them. Fire favors targeted attacks; earth favors area attacks and protection; air balances targeted and area attacks; water balances area control with protection. Gust damages and pushes nearby enemies away; Frost Nova damages and briefly freezes them in place; Hailstorm pelts an area with ice shards and slows enemies inside it, and Ice Wall raises a ring of ice where it is cast that hits and shoves back enemies trying to enter, a temporary safe space. Unequipping the casting weapon ends all of its active spell effects immediately; permanent account progression and a larger spell bar remain outside this proposal.

World interaction uses a stationary, interruptible hold with a filling circular progress indicator: an ordinary door/chest starts at one second total, while mechanical picking may take five or ten seconds according to its lock. Movement, combat intent, damage or lost prerequisites abandon progress before any world change; completion commits once. Player and capable monsters use the same execution rules. This creates pressure around enemies while allowing the player to abandon an unsafe task.

Dodge and the opening parry window of held guard share that action model. Bodies/equipment grant capabilities independently: a rat may dodge, a swordsman may parry, and a golem may brace without a weapon. Cooldowns and recovery provide the limits; there is no stamina. A per-actor poise meter decides whether hits merely flinch or break into a stagger. [Actor actions and world interactions](actor-actions-and-interactions.md) defines the intended rules; the separate [implementation plan](specs/actor-actions-and-interactions-plan.md) defines delivery. These changes are proposed, not implemented.

Enemy variety should encourage different responses through movement, perception, attack roles, defensive capabilities, and vulnerabilities. More health and damage alone should not be the only source of deeper-floor difficulty. Defensive enemies react with delay and restraint to readable threats; they do not require elaborate patterns, perfect input reads, or repeated flawless counters.

Encounter pacing should support both smaller tactical fights and moments of overwhelming crowd pressure inspired by Gauntlet and Vampire Survivors. Large groups should reward positioning, chokepoints, and appropriate builds while preserving readable threats and usable escape routes. Exact monster counts, encounter frequency, and performance budgets require prototypes and playtesting. Constant maximum density, automatic attacks, and arena-only progression are not implied by this influence.

Current combat and character foundations are described in [weapon attacks](weapon-attack-system.md) and [character classes](character-classes.md).

## Dungeon exploration

Floors combine realm-specific authored room pools with procedural rooms and corridors. A floor recipe defines required locations, optional branches, and progression conditions. A location can be required to exist, required to be traversed, or associated with an objective required before descent; these are separate guarantees. Room purpose should influence contents and rewards.

[Dungeon Layout, Access, and Discovery](dungeon-exploration-design.md) owns the detailed exploration concept. Doors compose opening/closing with optional locks and discovery. Predefined and generated rooms can have locked doors. Prison cells may require lockpicks, with matching keys as an authored alternative; color coding is an optional presentation feature, not a universal key rule. Initial mechanical locks protect optional content, with guaranteed matching keys reachable before their locks and pickability enabled. Successful unlocking consumes one matching key or lockpick; both stack to 20.

Secrets occur at variable generated corridor or procedural-room wall segments, not predictable sockets in predefined rooms. Nearby discovery rolls every 0.5 seconds use Intelligence initially; successful discovery from an accessible side reveals a closed door; unlocking, opening, and discovering the destination remain separate actions. Traps use similar proximity discovery and remain dangerous after being found. A short yellow glow blends the original surface into the discovered trap or door, then disappears. Traps are detected from farther away to provide advance warning. Locked rooms and secrets are occasional, capped features; secrets are more common. Secrets and tools offer optional exploration choices without becoming hidden prerequisites for the required route.

Health and mana fountains are rare landmarks rather than a supply source. A run holds two or three of each, spread across its depths, so food and potions keep their value. Drinking refills the matching pool completely, once; the fountain then runs dry for the rest of the run. Drinking at a full pool is refused instead of wasting the fountain.

Realm bosses initially gate descent through visible magical stair seals that lift automatically on defeat. Reaching or running around the boss does not satisfy completion. Arena lockdown and guaranteed boss-key alternatives remain separate encounter choices. Required access solutions must be reachable and cannot depend on random loot, shop stock, or an inventory-capacity failure.

Seeded generation recreates initial room choices, encounters, connections, access assignments, secrets, and source loot. Separate RNG streams isolate unrelated generation, gameplay and cosmetics. Save player-caused differences rather than full floor copies. Reproduction applies to the same build/content; historical seed compatibility is excluded during development. See [Randomness and Seeds](randomness-and-seeds.md) for current guarantees and limits, including supported actor reset behavior.

### Dark floors and portable lighting (planned)

Darkness is a property of the generated level, determined by its existing level seed alongside the layout; it needs no separate seed. Floors before depth 4 are never dark. From depth 4 onward, the initial tuning proposal is a 20% chance per floor. Across the intended 24-depth run this averages 4.2 dark floors, aiming for roughly 3–5 per playthrough without guaranteeing that count. Revisits and save/load preserve the floor's generated darkness.

Dark floors have no ambient light or environmental illumination: all torches and other light fixtures are extinguished. The player's small innate light provides baseline visibility on dark floors. Normal floors retain the original broad player illumination; carrying a light there does not add brightness. On dark floors, a lit torch or lamp carried in either hand increases illumination around the player; storing it in inventory provides no light. A weapon and lamp can be carried together, but the lamp occupies a hand that could otherwise hold a shield. Using a lamp therefore requires putting the shield in inventory, and a two-handed weapon cannot be used together with a lamp or torch.

Fog-of-war reveal works identically on normal and dark floors. Discovering space removes its exploration cover but does not illuminate it; on dark floors, revealed geometry remains dark outside the player's light. No additional darkness reveal system is needed. Light radius, fuel pacing, and whether enemies respond to carried light remain open tuning/design decisions.

[Level design](level-design.md) describes the current generator and its limitations; the [implementation plan](specs/level-progression-prototype-plan.md) describes remaining engineering work.

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
| Lamps and torches | Portable illumination | Hold lit in either hand to extend player light; competes with shields and two-handed weapons; activate or extinguish, consume fuel while active |
| Keys | Access to matching locks | Matching key identity/color; stack to 20 and consume one per successful unlock |
| Tools | Alternative ways to interact with the dungeon | Rare consumable lockpicks bypass pickable locks; Rogue starts with two, regular floors do not supply them, shops are a later source |
| Currency | Purchasing decisions | Collect, spend, and receive through trade |

### Instance state and persistence

Creation initializes an item's random state once. Pickup, inventory transfer, equipping, dropping, trading, floor transitions, and save/load preserve that state. Loading restores results rather than rolling them again. During development before public release, this guarantee applies to the current supported save version. Saves may break between versions; incompatible or unversioned saves require a new run and are not migrated.

Relevant state includes equipment quality, value scale, affixes and curses, food contamination, and remaining fuel or charges where implemented. Initial key matching uses the authored key definition; per-instance lock associations are unnecessary until a supported mechanic requires them. Store only state required by supported behavior. Authored definitions remain unchanged by play.

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

Names, models, descriptions, quality colors, stacking behavior, and prices must respect the intended information boundary. Players should understand observed effects without unintentionally seeing hidden properties.

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

Keep four concepts distinct: **tier** is base equipment strength; **quality** is an item's rolled, always-visible grade: Poor and Superior scale its base values, while Magic and Rare add affixes; **potency** describes an effect's magnitude; **availability** describes how often something can be found. A high-tier weapon can roll Poor. A basic utility scroll can remain valuable deep in the dungeon.

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
| Run objective, length, and victory | 24 depths across 8 realms (Sunken Epochs); World-Anchor victory at Depth 24 | Current direction; later realm order provisional (see [Realms and Monster Design](realms-and-monster-design.md)) |
| Death and between-run progression | Death ends the run; permanent power undecided | First complete run milestone |
| Inventory and shop pause behavior | Open | Balancing item-use timing and shop encounters |
| Food contamination and detection | Per-instance creation exists; probability and detection need tuning | Three-floor balance |
| Backpack capacity and upgrades | Implemented 20-entry backpack; food/equipment individual; larger packs later | Upgrade integration |
| Capacity accounting and stack limits | Equipped entries count; currency stays in stats; potion/scroll caps are 20; upgrade size remains open | Backpack upgrade design |
| Crowd encounter scale and pacing | Mix tactical fights with occasional overwhelming groups; counts and performance targets open | Encounter balancing |
| Lighting and fuel pacing | Seeded dark floors from depth 4, proposed 20% chance; either-hand lights compete with shields/two-handed weapons; light radius, fuel pacing and enemy response open | Lighting integration |
| Armor tradeoffs and visual coverage | Small set with readable differences | Armor integration |
| Keys and lockpicks | Implemented through [door access](door-access.md): optional mechanical locks; matching keys or deterministic timed picking; consume one key/pick on success, stack to 20, guarantee reachable matching keys. Lock counts, pick duration and rewards need tuning | Three-floor balance |
| Merchant knowledge, prices, and frequency | Persistent finite stock scaled by depth | Shop integration |

Update this document when intended behavior changes. Record implementation details in the linked feature documents and track delivery in the [roadmap](development-roadmap.md).

## Design-document references

- **Condor's Diablo pitch (1994):** reviewed from the supplied eight-page PDF; a [public copy](https://www.graybeardgames.com/download/diablo_pitch.pdf) is also linked by the discussion below. Its overview, gameplay walkthrough, procedural floors with authored set pieces, and attention to replayability are useful references for communicating the game. It is an early proposal, including turn-based combat, rather than a specification of the shipped game. Its historical scope, schedule, and business proposals are not requirements for Rogue Gauntlet.
- **[Finished Game Design Document Examples?](https://www.reddit.com/r/gamedesign/comments/7ze7xq/finished_game_design_document_examples/):** a discovery index for examples and a discussion of pitch, concept, and evolving design documents. Our working format keeps the vision and player experience here, delivery outcomes in the roadmap, and implementation detail in feature documents. Examples inform the format; they do not settle our design decisions.
