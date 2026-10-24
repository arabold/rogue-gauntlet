# Dungeon progression: remaining implementation

Status: proposed work. [Dungeon design](../dungeon-exploration-design.md) owns realm pools, route obligations, locks and secrets. [Level design](../level-design.md) owns existing generation, arrangements and first-realm encounters. Reuse those systems and their factories; do not rebuild encounter/reward mechanics.

## Definitions and ownership

Add realm/recipe Resources under `scenes/levels/progression/`. Realm definitions select depth ranges, room pools, recipes and the existing encounter factory. A pool entry selects exactly one authored scene or procedural builder, with stable identity, purpose/depth eligibility, weight and repeat cap. Recipes distinguish required existence, traversal and completion; none implies the others.

Plans and realized placements are ordinary per-generation C# data. Shared Resources remain immutable. Explicitly assign realm/recipe dependencies on Level; retain the current generator path for unconfigured development floors. Reuse room packing, rotation and doorway helpers instead of duplicating them. Editor changes apply on regeneration, not a live floor.

## Geometry and failure contracts

Reserve required locations first, then optional filler/branches. Route approved connections and seal unused openings. Actual corridor junctions and loops must preserve recipe obligations; validating room-center adjacency is insufficient. A must-traverse checkpoint must remain unavoidable in the realized walkable graph, and hidden destinations must not have visible alternate entrances.

Selection permissions/weights do not guarantee placement. Optional content may be omitted; required rooms, objectives and their access solutions may not. Attempts have deterministic identities and a bounded retry limit. Discard temporary geometry/spawns on failure. Exhaustion returns safely without publishing a loaded level or overwriting a save.

The existing Garrison preset already curates architectural purposes and optional non-repeating landmarks. Extend that selection with per-depth roles and existing scenes as role placeholders. Required route recipes follow without expanding realm order, map size or the monster roster.

## Local access composition

Proposed Nodes under `scenes/levels/access/` separate door motion, access conditions, locks, encounter seals and secret discovery. Door remains a thin scene facade. An access gate requires all explicitly assigned conditions; no conditions means ordinary unrestricted operation. Motion owns physical/navigation state; access checks do not manipulate its tween directly.

Level supplies encounter-completion dependencies to seals from existing cleared-source state. Lock picking uses the existing actor interaction executor; there is no second timer. Discovery controls concealed presentation and local detection, without granting permission to unlock/open. Use local signals and explicit references, not a gameplay registry.

```text
Seed + realm recipe → plan required/optional locations and approved connections
  → place/route → validate real topology and access solutions
  → compose doors/seals/discovery with stable endpoint identities
  → restore validated access differences and exploration history
  → finish navigation/encounters and permit gameplay
```

Keep ordinary door links, fog and AI crossings consistent. A visible boss seal must reject both physical passage and travel requests until the existing Warden objective is complete.

## Persistence and solvability

Stable recipe-slot/connection IDs survive translation and rotation within the same build/content. Separate layout, encounter and source-loot streams; runtime discovery/picking must not consume generation RNG.

Save access differences from the generated baseline: discovered, unlocked and currently open states. Exploration history and current physical openness must have separate meanings, so revisiting a revealed door does not reopen one the player closed. Replace the old opened-door authority coherently rather than maintain competing lists. Boss completion still derives from existing cleared encounter IDs.

Restore all access state before exposing interaction or traversal. Invalid IDs/states fail safely. Key-behind-own-lock cycles and bypassed required encounters are invalid; initial mandatory routes require no mechanical picking tool. Incompatible changes bump the code's save version, never add migration adapters.

## Delivery and acceptance

1. **Realm pools and recipes:** per-depth Garrison selection, required slots, a must-traverse checkpoint and intentional loops. Sweep realized geometry and bounded failures; no locks/secrets yet.
2. **Visible stair seal:** compose access/motion, persist current door state and bind the existing Warden completion. Check death, revisit, reload, physical blocking and both travel callers.
3. **Optional locks:** pickable prison cells and one matching-key alternative through the shared interaction executor. Reusable tools, deterministic success, no mandatory lock or breakage. Check missing tools, wrong keys, interruptions, alternate routes and duplicate rewards.
4. **Generated secrets:** vary corridor/procedural-wall candidates and destinations, with no fixed secret sockets in authored rooms. Prevent prompts, outlines or fog leaks before reachable-side discovery. Test shortcut versus secret-room topology and save restoration.

Use actual editor navigation and concealed-wall review alongside seeded topology/state tests. Update current-system docs as behavior lands; report remaining acceptance rather than append test logs here.
