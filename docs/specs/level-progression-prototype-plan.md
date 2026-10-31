# Dungeon progression: remaining implementation

Status: proposed work. [Dungeon design](../dungeon-exploration-design.md) owns realm pools, route obligations, locks and secrets. [Level design](../level-design.md) owns existing generation, arrangements and first-realm encounters. Reuse those systems and their factories; do not rebuild encounter/reward mechanics.

## Definitions and ownership

Add realm/recipe Resources under `scenes/levels/progression/`. Realm definitions select depth ranges, room pools, recipes and the existing encounter factory. An ordinary/themed pool entry selects a generated composition from the [implemented room system](../level-design.md#coded-room-generators), with stable identity, purpose/depth eligibility, weight and repeat cap. Entrance/exit anchors can retain their existing authored scenes. Recipes distinguish required existence, traversal and completion; none implies the others.

Plans and realized placements are ordinary per-generation C# data. Shared Resources remain immutable. Explicitly assign realm/recipe dependencies on Level; retain the current generator path for unconfigured development floors. Reuse room packing, rotation and doorway helpers instead of duplicating them. Editor changes apply on regeneration, not a live floor.

## Geometry and failure contracts

Reserve required locations first, then optional filler/branches. Route approved connections and seal unused openings. Actual corridor junctions and loops must preserve recipe obligations; validating room-center adjacency is insufficient. A must-traverse checkpoint must remain unavoidable in the realized walkable graph, and hidden destinations must not have visible alternate entrances.

Selection permissions/weights do not guarantee placement. Optional content may be omitted; required rooms, objectives and their access solutions may not. Attempts have deterministic identities and a bounded retry limit. Discard temporary geometry/spawns on failure. Exhaustion returns safely without publishing a loaded level or overwriting a save.

The Garrison preset curates generated recipes, including modular barracks/storage that replace their predefined scenes. Extend these recipes with per-depth roles; room function or rarity must not depend on retaining a frozen room scene. Required route recipes follow without expanding realm order, map size or the monster roster.

## Extend existing access for encounter seals

[Door access](../door-access.md) already implements local lock/discovery composition, actor-context interactions, stable connection identity, exact explored tiles and saved current door state. Reuse that state/interaction boundary instead of adding competing door lists or timers. The separate [access follow-up plan](secret-doors-and-locks-plan.md) owns tuning, buttons, chains and shortcuts.

The visible Warden stair seal remains work: bind an explicitly assigned encounter condition to existing cleared-source state, show a readable barrier and enforce it for physical passage and both travel callers. Existing Warden completion remains the authority; ordinary picks cannot bypass it. Opening a route and discovering space remain independent. No new global gameplay registry.

```text
Seed + realm recipe -> required/optional locations and approved connections
  -> realize geometry -> validate required topology and access solutions
  -> existing door access + explicit encounter condition
  -> restore access/item/exploration differences before gameplay
```

The access generator reserves required large-encounter space and excludes gated regions from final boss placement. New recipe constraints must preserve those guarantees in actual geometry. Same-version saves retain completion and supported access state; incompatible generation/schema changes bump the save version without migrations.

## Remaining delivery and acceptance

1. **Realm pools and recipes:** per-depth Garrison selection, required locations, must-traverse checkpoint and intentional loops. Required ordinary/themed roles use generated compositions. Sweep realized geometry, route obligations and bounded failures; optional access cannot invalidate required content.
2. **Visible stair seal:** compose the encounter condition with current access state and Warden completion. Check death, revisit, reload, physical blocking and both travel callers. Ordinary locks/secrets are already delivered and need not wait for this work.

Use actual editor navigation alongside seeded topology/state tests. Consolidate completed slices into current-system docs; keep this plan focused on remaining work.
