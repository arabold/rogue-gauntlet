# Development roadmap

The priority is a varied, readable and enjoyable first three depths. [Game design](game-design.md) owns the intended experience; [the documentation index](README.md) links current systems. Status here distinguishes implemented foundations from acceptance and proposed work.

## Existing foundations

The game has seeded mixed room generation, fog/doors, five playable monsters, threat budgets, finite nests and a Warden descent check. Combat includes shared action ownership, timed contact, active dodge/guard/parry and bounded tactical retreat. Items have runtime identity, stacks/capacity, food, Diablo II-style [item quality](stats-and-itemization.md#equipment-quality-affixes-and-enchantment) with depth-tiered affixes, per-item identification and hidden sticky curses ([remaining work](specs/item-quality-affixes-and-curses-plan.md)), identification, grouped loot and seeded floor deltas. These systems exist; they do not establish final pacing, balance or a complete run.

Studio exports supply art independently of Godot gameplay definitions. Available models do not require adding content before its role and behavior are useful.

## Room variety and furnishing

[Arrangements](level-design.md#reusable-room-arrangements) support editable scene compositions, quarter-turn rotation, purpose eligibility, connected free space and editor generation. Floor furniture and elevated wall mounts share the same placement policy. Existing torch and banner fixtures demonstrate live light/particles without reserving floor space; solid collision still preserves access.

Future corridor dressing can reuse the same scenes with weighted scatter, opposing-pair or alternating-side patterns and spacing/jitter settings. Accept opposing pairs together against compatible wall spans; symmetry is optional. Fixed compositions such as torches framing a banner remain authored scenes. Corridor placement is not part of the current slice.

The Garrison preset now restricts generated architecture and uses barracks/storage as non-repeating landmarks. Gameplay and the playground share the preset; per-depth roles and route guarantees remain future work. Next, review density and repetition using the [authoring guide/playground](../scenes/levels/arrangements/README.md); map-size expansion is a separate comparison. Interactions and loot-bearing arrangements remain deferred.

## First-three-floor acceptance

Play two contrasting classes across three seeds, including save/resume and revisits. Tune authored Resources together rather than expanding the roster first.

| Depth | Intended experience | Review |
| --- | --- | --- |
| 1 | Learn melee approach, frontal defense and ranged pressure | Wind-ups, damage, group size, readable responses and supplies |
| 2 | Combine roles with rat retreat and finite nests | Escape space, nest pacing, doorway congestion and recovery |
| 3 | Apply positioning and defense against the Warden | Reachability, boss guard/recovery openings and rewards |

Check pressured/cornered crossbowmen still shoot, retreat never traps or indefinitely disables them, rat retreat follows completed bites, and the Warden stays within its clearance. Review skeleton animation transitions and reward pacing; the regular warrior now uses common loot and a provisional XP reward closer to the other skeletons. Cleared enemies, nest slots, boss defeat and claimed rewards must survive reconstruction.

Authoring acceptance also requires duplicating an actor in Godot, adding/removing guard or dodge with explicit references, saving/reopening, and checking two independent instances. The [template guide](../scenes/enemies/templates/README.md) owns that workflow. Automated checks support these reviews; real navigation and fun require playtesting.

## Dungeon progression

[Dungeon design](dungeon-exploration-design.md) and its [remaining plan](specs/level-progression-prototype-plan.md) cover realm pools, required locations/routes, composed access and secrets. Implemented [door access](door-access.md) includes consumable keys/picks, matching colored markers, guaranteed accessible key sources, occasional capped secret pockets and locks, random Intelligence-based door/trap discovery, shared discovery feedback and separate saved exploration/current door state. Regular floors supply no lockpicks; the Rogue starts with two. [Remaining access work](specs/secret-doors-and-locks-plan.md) is tuning and later mechanisms/chains/shortcuts. Per-depth recipes and the visible Warden stair seal remain separately planned. Existing encounters/rewards are reused.

Acceptance must validate actual geometry and access solutions, including loops, locked/closed states, discovery and save/load. Required rooms and routes cannot disappear when optional content does not fit. Full later realm order remains provisional.

## Actions, magic and useful objects

Dedicated weapon inputs, four item quick slots, ordinary timed interactions and active defenses are implemented. [Action design](actor-actions-and-interactions.md) owns remaining gameplay decisions; its [plan](specs/actor-actions-and-interactions-plan.md) owns mechanical-lock integration, learned spells and mana. Statuses applied by attacks (venomous monster attacks, poisoned weapons) are proposed in the [attack status effects plan](specs/attack-status-effects-plan.md).

After the first-three-floor loop is satisfactory, build one fire-magic slice with shared targeting, learning, selection and mana before completing four elemental trees. Portable lighting/fuel, wearable armor integration, backpack upgrades and tool interactions should each add an observable choice. Model exports alone do not implement these capabilities. Reward usefulness, supply rates and visual presentation still need tuning.

[Dark floors and portable lighting](game-design.md#dark-floors-and-portable-lighting-planned) make visibility an equipment choice. The debug darkness override and six either-hand portable lights are implemented, including shield/two-handed conflicts and equipment restoration through current-version save/load. Seeded dark-floor selection, normal loot supply, fuel and manual ignition remain planned. Acceptance for automatic darkness must cover reproducible floors on revisits/save-load, extinguished environmental lighting and zero ambient light, unchanged fog-of-war reveal and baseline player light. Frequency, light radius and fuel pacing need playtesting.

## Later delivery

- **Message feed:** the [logging proposal](specs/logging-plan.md) remains separate from existing combat traces.
- **Shop:** finite depth-appropriate stock, atomic trade, persistent balances and no identification leaks or trivial resale loops.
- **Complete run:** connect class selection, descent, recovery/preparation and a valid win/loss outcome. The intended 24-depth/eight-realm journey ends at the World-Anchor; permanent progression remains undecided.
- **Scale:** establish a readable crowd size and measured performance budget before increasing density or adding coordinated AI.

Shops, later monsters/realms, boss phases, flanking/group shelter, crafting, hunger and permanent progression are not prerequisites for the current slice. Promote them when a playtested need supports the design pillars. Mark work complete only when its player-facing acceptance is met; keep implementation and test history in code and delivery reports.
