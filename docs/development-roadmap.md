# Development roadmap

The priority is a varied, readable and enjoyable first realm (depths 1–5). [Game design](game-design.md) owns the intended experience; [the documentation index](README.md) links current systems and plans. This page separates implemented foundations from acceptance and proposed work, and links each open item to its GitHub issue.

## Existing foundations

- **Dungeon:** five realms of five floors (Garrison, Prison, Sewer, Mines, Cathedral) with room schedules, headlines and motifs, weighted floor plans and boss plans, room groups with must-traverse members, theme-tagged room types with room-owned doorways and secret doors, locks and key chains, fountains, fog of war and a floor map ([level design](level-design.md), [door access](door-access.md)).
- **Monsters:** twenty monsters across four realms ([catalog](monster-catalog/README.md)) with data-driven actions, health phases and signature capabilities (charge, lob, ambush, theft, blink, flight, revive); three gatekeepers (Warden, Minotaur, Gelatinous Cube) seal their realm's descent; threat budgets with per-monster floor caps and finite nests ([monster authoring](monster-authoring-design.md)).
- **Combat:** shared action ownership, timed contact, dodge, guard/parry/guard break, poise, threat tiers and telegraphs, attack slots and flanking, knockback in metres ([combat](combat-system.md), [weapon attacks](weapon-attack-system.md)).
- **Traps:** hidden pressure plates fire spikes, darts or fireballs; visible spouts and pendulum blades form gauntlet rooms placed before a few descents per run ([trap gauntlets](level-design.md#trap-gauntlets), [triggers](level-design.md#triggers)).
- **Magic:** twenty elemental spells on wands and staves across five levels, free homing missiles and a character-owned mana pool ([elemental spells](weapon-attack-system.md#elemental-spells)).
- **Items:** Diablo II-style [quality and affixes](stats-and-itemization.md#equipment-quality-affixes-and-enchantment), per-run identification and hidden curses, grouped loot, quick slots, body armor and helmets worn on the rig, and a [Quartermaster](shops.md) on the first floor of each realm after the Garrison.
- **Interface:** front end, depth card, HUD with a message feed, floor map, run menu (Gear, Pack, Notes, System, and Buy/Sell when trading), death screen and confirmations ([user interface](user-interface.md)).
- **Balance:** the [balance model](balance-model.md) and its simulator derive monster stats, XP, floor budgets and trap damage.
- **Runs:** class selection, saving on floor arrival and exit, and [permadeath](run-lifecycle.md) with a death screen.

These systems exist; they do not establish final pacing, balance or a complete run. Studio exports supply art independently of gameplay definitions: an available model is not a reason to add content before its role and behavior are useful.

## Room variety and furnishing

[Room assembly](level-design.md#room-assembly) builds every room from a room type over shared props, groups and styles, with [reachability](level-design.md#reachability) keeping content out of inaccessible space. Remaining:

- **More variety:** further room types, props, groups and wall/floor styles; authoring work on the existing strategies (see the [authoring guide](../scenes/levels/generators/assembly/README.md)), including the [missing floor-plan props](specs/floor-plan-props.md).
- **Secret entrances:** playtest [room-owned secret doors](door-access.md#generation-and-guaranteed-sources); hidden buttons that reveal them are [#42](https://github.com/arabold/rogue-gauntlet/issues/42).
- **Opaque partitions and internal locks:** a room-local sight boundary projected through rotation and placement; internal mechanical locks need guaranteed reachable sources and saved state. Bars and fences stay non-occluding.
- **Sewer crossings:** bridges or stepping stones over impassable pools; flyers already cross water on their own air grid.
- **Trap pacing:** corridor gauntlets, realm hazard pacing and the remaining hazards ([trap plan](specs/trap-hazards-plan.md), [#159](https://github.com/arabold/rogue-gauntlet/issues/159)); switches, puzzles and switch-opened doors ([triggers plan](specs/triggers-plan.md), [#160](https://github.com/arabold/rogue-gauntlet/issues/160)).
- **Encounters follow motifs:** packs weighted by a floor's motifs and rooms that prefer certain packs ([pacing plan](specs/realm-content-pacing-plan.md), [#161](https://github.com/arabold/rogue-gauntlet/issues/161)).

<a id="first-three-floor-acceptance"></a>
## First-realm acceptance

Play two contrasting classes across three seeds through the Garrison (depths 1–5) into the first Prison floor's shop, with save/resume and revisits ([#30](https://github.com/arabold/rogue-gauntlet/issues/30)). Tune authored Resources; balance changes go through the [balance model](balance-model.md), and `simulate.py playtest` measures a run against the role targets and floor attrition.

| Depth | Content | Review |
| --- | --- | --- |
| 1 | Rat packs, skeleton pairs and warriors | Wind-ups, group size, supplies |
| 2 | Rat hordes, patrols, sentries and two bone nests | Escape space, nest pacing, doorway congestion |
| 3–4 | Warbands with the mage's healing; plate traps and gauntlets | Focus fire, trap reading, attrition |
| 5 | The Forsaken Warden's three phases | Reachability, guard/recovery openings, Tremor timing, rewards |

Check that pressured or cornered crossbowmen still shoot, retreat never traps or indefinitely disables them, flanking rats stay fair against melee and ranged heroes, and the Warden stays within its clearance. Cleared enemies, nest slots, gatekeeper defeat and claimed rewards must survive reconstruction. Prison, Sewer and Mines monsters and gatekeepers have harness tests but no playtest acceptance yet.

Combat needs the same review: the [combat redesign](specs/combat-redesign-plan.md) is implemented except a guarded walk ([#41](https://github.com/arabold/rogue-gauntlet/issues/41)); the Warden's overhead already breaks guard. Human review at normal speed remains for dash responsiveness and the bent dodge path. The [combat harness](combat-harness.md) hosts the game's debug menu, whose Monsters and Combat tools control AI, defense modes, invulnerability and debug drawing; whole-world hit stop remains a separate pacing decision.

Authoring acceptance also requires duplicating an actor in Godot, adding/removing guard or dodge with explicit references, saving/reopening, and checking two independent instances; the [template guide](../scenes/enemies/templates/README.md) owns that workflow. Automated checks support these reviews; real navigation and fun require playtesting.

## Monster roster

The [realm monster roster](specs/realm-monster-roster-plan.md) ([#158](https://github.com/arabold/rogue-gauntlet/issues/158)) remains for the Mines cast and gatekeeper, the Cathedral cast and gatekeeper (until then the Cathedral falls back to single Garrison skeletons and rats), and the Sewer's aquator and tentacles, with the capabilities they need (grasp, petrify, burrow, gaze, invisibility, door phasing, drain, summon). Monsters do not open doors yet ([#44](https://github.com/arabold/rogue-gauntlet/issues/44)).

## Dungeon progression

[Dungeon design](dungeon-exploration-design.md) owns realm pools, required routes, composed access and secrets. Realm schedules, floor plans, must-traverse room groups, gatekeeper-sealed descents (a label over the stairs, enforced on every travel path), theme-tagged rooms and room-owned doorways are delivered. Guaranteed locations per floor and a visible gatekeeper seal remain in the [progression plan](specs/level-progression-prototype-plan.md) ([#32](https://github.com/arabold/rogue-gauntlet/issues/32)).

Acceptance must validate actual geometry and access solutions, including loops, locked/closed states, discovery and save/load. Required rooms and routes cannot disappear when optional content does not fit.

## Actions, magic and useful objects

Dedicated weapon inputs, four item quick slots, timed interactions, active defenses and paired hand capabilities are implemented ([actor actions](actor-actions-and-interactions.md)). Wands, staves, scrolls, potions and innate monster abilities invoke the same spell definitions through their own activation owners; area spells use shared area contacts. [Weapon attacks](weapon-attack-system.md#elemental-spells) describes the roster, targeting, wards and missiles; the [spell plan](specs/elemental-spells-plan.md) keeps level 4–5 visuals, acceptance and balance, and the [magic missile plan](specs/magic-missile-plan.md) the darts' finished look ([#162](https://github.com/arabold/rogue-gauntlet/issues/162)).

[Actor and item world effects](world-effects.md) cover authored looks, status looks (frozen, chilled, burning, goo-bound, invisibility), potion, scroll and level-up cues, item auras and glowing weapons. Visual review of final quality, GPU timing and readability at the gameplay camera remains ([#37](https://github.com/arabold/rogue-gauntlet/issues/37)), as does a distinct activation signature for every potion and scroll ([consumable feedback plan](specs/consumable-feedback-plan.md), [#36](https://github.com/arabold/rogue-gauntlet/issues/36)).

[Dark floors and portable lighting](game-design.md#dark-floors-and-portable-lighting-planned) make visibility an equipment choice ([#38](https://github.com/arabold/rogue-gauntlet/issues/38)). The debug darkness switch and six off-hand portable lights exist: without a held light, an unlit dark floor closes sight to a small circle, and a held torch or lamp lights the way on any floor. Seeded dark floors, light supply in loot and shops, fuel and manual ignition remain. Larger backpacks and tools ([#48](https://github.com/arabold/rogue-gauntlet/issues/48)), deferred potions ([#47](https://github.com/arabold/rogue-gauntlet/issues/47)) and later itemization ([#46](https://github.com/arabold/rogue-gauntlet/issues/46), [#33](https://github.com/arabold/rogue-gauntlet/issues/33)) should each add an observable choice; model exports alone do not implement them.

## Later delivery

- **Architecture cleanup** ([#39](https://github.com/arabold/rogue-gauntlet/issues/39)): item use through one context (consumable and scroll-effect signatures, `ActionManager` slot creation), and a scene autoload for the session-flow scene paths shared with `SceneDebugBootstrap`.
- **Logging** ([#49](https://github.com/arabold/rogue-gauntlet/issues/49)): the player channel, narrator, HUD feed and saved Notes exist ([user interface](user-interface.md#message-feed-and-notes)); Internal/Debug channels replacing the Combat/AI log toggles, settings, command-line switches and JSONL output remain ([logging plan](specs/logging-plan.md)).
- **Shops** ([#51](https://github.com/arabold/rogue-gauntlet/issues/51)): equipment stock beyond tier 1 (deeper shelves sell supplies only), playtesting gold and purchase choices after the Warden, then the [shop room and realm merchants](specs/shop-design.md).
- **Complete run** ([#40](https://github.com/arabold/rogue-gauntlet/issues/40)): a provisional victory at the final depth of the 25-depth, five-realm journey (the descent currently continues past depth 25), and a full playthrough. Restarting a fallen run from depth 1 with its seed is a small addition. Permanent progression remains undecided.
- **UI** ([#163](https://github.com/arabold/rogue-gauntlet/issues/163)): gamepad glyphs, readable item lines, settings and credits, later the touch HUD ([UI plan](specs/ui-system-plan.md)).
- **Audio:** the game has no sound yet; a gameplay [audio system](https://github.com/arabold/rogue-gauntlet/pull/96) is an open pull request.
- **Scale** ([#45](https://github.com/arabold/rogue-gauntlet/issues/45)): [attack slots](combat-system.md#attack-slots) bound simultaneous attackers; establish a readable crowd size and a measured performance budget before increasing density or adding coordinated AI.

Further merchants, group shelter, crafting, hunger and permanent progression are not prerequisites for the current slice. Promote them when a playtested need supports the design pillars. Mark work complete only when its player-facing acceptance is met; keep implementation and test history in code and delivery reports.
