# Development roadmap

The priority is a varied, readable and enjoyable first three depths. [Game design](game-design.md) owns the intended experience; [the documentation index](README.md) links current systems. Status here distinguishes implemented foundations from acceptance and proposed work.

## Existing foundations

The game has seeded room generation, fog/doors, five playable monsters, threat budgets, finite nests and a Warden descent check. Combat includes shared action ownership, timed contact, active dodge/guard/parry and bounded tactical retreat. Items have runtime identity, stacks/capacity, food, Diablo II-style [item quality](stats-and-itemization.md#equipment-quality-affixes-and-enchantment) with depth-tiered affixes, per-item identification and hidden sticky curses ([remaining work](specs/item-quality-affixes-and-curses-plan.md)), identification, grouped loot and seeded floor deltas. These systems exist; they do not establish final pacing, balance or a complete run.

Studio exports supply art independently of Godot gameplay definitions. Available models do not require adding content before its role and behavior are useful.

## Room variety and furnishing

[Room assembly](level-design.md#room-assembly) builds every Garrison room, entrance and exit included, from a room type: shape, doorways, wall and floor styles, columns and ordered furnishing steps over shared props and groups, with [reachability](level-design.md#reachability) keeping content out of inaccessible space. Fourteen room types cover halls, storage, mess halls, barracks, libraries, alchemists, passages, fenced walkways, partitioned stores, prisons, chapels, kitchens, guardrooms and ruins, dressed with set tables, altars, trunks, sacks and pottery, banner colours per room, and windowed, scaffolded, pillared, arched and ruined walls over bordered, split and drained floors. Entrances and exits place the level's stairs in any of their shapes; there are no hand-authored rooms.

Remaining:
- **More variety:** further room types, props, groups and wall/floor styles; this is authoring work on the existing strategies (see the [authoring guide](../scenes/levels/generators/assembly/README.md)).
- **Corridor dressing:** weighted scatter or opposing pairs along corridor walls, reusing props and wall-decor placement.
- **Opaque partitions and internal locks:** a room-local sight boundary projected through rotation and placement; internal mechanical locks need guaranteed reachable sources and saved state. Bars and fences stay non-occluding.
- **Sewer and cistern content:** crossings (bridges or stepping stones over pools) and shallow water as gameplay.
- **Per-depth room roles** and required routes.

## First-three-floor acceptance

Play two contrasting classes across three seeds, including save/resume and revisits. Tune authored Resources together rather than expanding the roster first.

| Depth | Intended experience | Review |
| --- | --- | --- |
| 1 | Learn melee approach, frontal defense and ranged pressure | Wind-ups, damage, group size, readable responses and supplies |
| 2 | Combine roles with rat retreat and finite nests | Escape space, nest pacing, doorway congestion and recovery |
| 3 | Apply positioning and defense against the Warden | Reachability, boss guard/recovery openings and rewards |

Check pressured/cornered crossbowmen still shoot, retreat never traps or indefinitely disables them, rat retreat follows completed bites, and the Warden stays within its clearance. Review skeleton animation transitions and reward pacing; the regular warrior now uses common loot and a provisional XP reward closer to the other skeletons. Cleared enemies, nest slots, boss defeat and claimed rewards must survive reconstruction.

The [combat redesign](specs/combat-redesign-plan.md) changes this combat review. All five slices are implemented: poise, committed wind-ups, rear and sneak hits, dodging through monsters, attack slots, threat tiers, strike-aligned contact timing, and the player kit (primary combos, input buffer, recovery cancels and dodge charges). They need playtesting against these depths; the Warden guard-breaking attack and guard walking remain.

Authoring acceptance also requires duplicating an actor in Godot, adding/removing guard or dodge with explicit references, saving/reopening, and checking two independent instances. The [template guide](../scenes/enemies/templates/README.md) owns that workflow. Automated checks support these reviews; real navigation and fun require playtesting.

## Dungeon progression

[Dungeon design](dungeon-exploration-design.md) and its [remaining plan](specs/level-progression-prototype-plan.md) cover realm pools, required locations/routes, composed access and secrets. Implemented [door access](door-access.md) includes consumable keys/picks, matching colored markers, guaranteed accessible key sources, occasional capped secret pockets and locks, random Intelligence-based door/trap discovery, shared discovery feedback and separate saved exploration/current door state. Regular floors supply no lockpicks; the Rogue starts with two. [Remaining access work](specs/secret-doors-and-locks-plan.md) is tuning and later mechanisms/chains/shortcuts. Per-depth recipes and the visible Warden stair seal remain separately planned. Existing encounters/rewards are reused.

Acceptance must validate actual geometry and access solutions, including loops, locked/closed states, discovery and save/load. Required rooms and routes cannot disappear when optional content does not fit. Full later realm order remains provisional.

## Actions, magic and useful objects

Dedicated weapon inputs, four item quick slots, ordinary timed interactions and active defenses are implemented. [Action design](actor-actions-and-interactions.md) owns remaining gameplay decisions; its [plan](specs/actor-actions-and-interactions-plan.md) owns further actor intentions, and the [magic rework](specs/magic-rework-plan.md) owns spell wands and mana. [Off-hand weapons](specs/off-hand-weapons-plan.md) proposes a passive off-hand damage bonus or, once a left-hand attack clip exists, animated dual-wield. Statuses applied by attacks (venomous monster attacks, poisoned weapons) are proposed in the [attack status effects plan](specs/attack-status-effects-plan.md).

[Actor statuses](specs/actor-status-integration-plan.md) provide source-owned timed conditions and a shared frozen response for players and monsters. Frost Nova, Gust, Flame Burst and Tremor use shared area contacts in the combat harness; casting from wands, other attack payloads, slow, petrification and general monster buff behavior remain planned.

[Actor and item world effects](world-effects.md) provide authored looks, a shared cloak/skin material owner, burst/sustained lifetimes, committed healing/strength potion feedback and known-cursed item auras. Scroll reading uses the appearance-matched rune ring; teleport scrolls have paired departure/arrival cues and monster spawns reuse arrival. The shared tabbed previews provide one effect and subject, mouse orbit, gameplay camera bindings, replay, pause/slow motion and skin finish experiments. Lifecycle combinations stay in automated tests. [Remaining acceptance](specs/actor-item-vfx-plan.md) covers final visual quality, preferred skin, GPU timing and in-game readability; preview support does not implement the corresponding gameplay mechanics.

After the first-three-floor loop is satisfactory, rework magic around spell weapons per the [magic rework plan](specs/magic-rework-plan.md): the mana pool, all 20 spells on wands and staves (whose tier sets the spell level), spell tooltips, the HUD's spell cost and out-of-mana cue, and wards that survive saves exist; balance and the level 4–5 visuals await playtesting; spell learning and skill points are dropped. The [spell plan](specs/elemental-spells-plan.md) defines the roster, surrounding-enemy targeting and visual acceptance. The [magic missile plan](specs/magic-missile-plan.md) gives every wand and staff its free homing seeker volley. Portable lighting/fuel, wearable armor integration, backpack upgrades and tool interactions should each add an observable choice. Model exports alone do not implement these capabilities. Reward usefulness, supply rates and visual presentation still need tuning.

[Dark floors and portable lighting](game-design.md#dark-floors-and-portable-lighting-planned) make visibility an equipment choice. The debug darkness override and six either-hand portable lights are implemented, including shield/two-handed conflicts and equipment restoration through current-version save/load. Seeded dark-floor selection, normal loot supply, fuel and manual ignition remain planned. Acceptance for automatic darkness must cover reproducible floors on revisits/save-load, extinguished environmental lighting and zero ambient light, unchanged fog-of-war reveal and baseline player light. Frequency, light radius and fuel pacing need playtesting.

The next consumable-feedback goal is [catalog-wide activation coverage](specs/consumable-feedback-plan.md): distinct world/screen signatures for all 9 potion families and 11 scroll types, independent of longer-lived status presentation. Sizes share their type's signature. This is proposed work, beyond the existing healing/strength cues, shared scroll-reading ring and teleport/spawn presentation.

## Later delivery

- **Architecture cleanup:** floor services, the run state and player persistence are separated. Remaining: item use through one context (consumable and scroll-effect signatures, `ActionManager` slot creation), and a scene autoload for the session-flow scene paths shared with `SceneDebugBootstrap`.
- **Message feed:** the [logging proposal](specs/logging-plan.md) remains separate from existing combat traces.
- **Shop:** finite depth-appropriate stock, atomic trade, persistent balances and no identification leaks or trivial resale loops.
- **Complete run:** connect class selection, descent, recovery/preparation and a valid win/loss outcome. The intended 24-depth/eight-realm journey ends at the World-Anchor; permanent progression remains undecided.
- **Scale:** [attack slots](combat-system.md#attack-slots) bound simultaneous attackers; still establish a readable crowd size and measured performance budget before increasing density or adding further coordinated AI.

Shops, later monsters/realms, boss phases, group shelter, crafting, hunger and permanent progression are not prerequisites for the current slice. Promote them when a playtested need supports the design pillars. Mark work complete only when its player-facing acceptance is met; keep implementation and test history in code and delivery reports.
