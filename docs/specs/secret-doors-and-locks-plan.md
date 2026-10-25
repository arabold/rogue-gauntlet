# Secret doors and locks: remaining work

Status: core implementation delivered October 24, 2026. [Current door access](../door-access.md) owns the implemented components, authoring, generation and save behavior. [Dungeon design](../dungeon-exploration-design.md) owns intended gameplay. The [progression plan](level-progression-prototype-plan.md) retains realm recipes and encounter seals.

## Follow-up scope

- Tune discovery chance/radius against ordinary walking speed, optional lock/secret counts, pick duration and rewards using complete-floor playtests. Keep all required progression independent of secrets and mechanical tools.
- Add authored hidden buttons through the existing idempotent reveal operation. A button-only secret disables proximity discovery. Mechanism references use stable connection identities; explicit unlock/open authority must respect any remaining encounter condition. Do not introduce a second interaction timer.
- Optional later key chains need a state search that tracks remaining key quantities and collected sources, not just a dependency graph. Initial key sources stay in openly reachable regions; maintain guaranteed matching quantities and avoid reliance on chance loot or picks.
- Later secret shortcuts or larger pocket recipes must validate realized geometry against required traversal/seals. The current single-entrance pockets cannot bypass existing locations.
- Introduce a Perception stat/talent only with a broader character-progression decision; current detection uses the selected primary attribute and has no hidden talent dependency.

Each extension needs focused GdUnit4 checks, current-version access/item save round trips and actual editor navigation/visibility exercises. Keep shared definitions immutable, preserve actor-context final validation and exactly-once spending, and bump the save version when generation/schema changes invalidate existing deltas. Historical save migration remains out of scope.
