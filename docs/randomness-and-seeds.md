# Randomness and seeds

Implemented reproducibility and persistence rules. Seeds reproduce initial scenarios within the same build/content and inputs; they do not replay arbitrary combat, physics or player history. Algorithms and seed derivation live in [GenerationRandom](../scenes/levels/generators/GenerationRandom.cs) and [GameSession](../scripts/GameSession.cs).

## Run and level seeds

A new run chooses one seed; saves retain it and the current depth. Each depth derives its own level seed. Regeneration rebuilds the initial scenario rather than advancing a global RNG. Code, content ordering and draw-order changes between builds may change scenarios; historical seed outputs are not preserved before release.

## Generation streams and source rewards

| Domain | Purpose |
| --- | --- |
| Layout | Room selection, procedural construction and placement |
| Encounters | Initial monsters, facing and encounter selection |
| Item placement | Chests, traps and loose finds |
| Decoration | Corridor/wall mesh variants |
| Arrangements | Independent per-room furnishing choices |
| Access | Independently derived lock/key placement, secret-pocket layout and local discovery streams |
| Source loot | Rewards derived from stable source identity |
| Identification | Per-run, per-category appearance assignment |

Strategies receive explicit RNG inputs and retain no shared mutable sequence. Isolation prevents accidental draw-order coupling, but intentional dependencies remain: different geometry changes valid encounter or loot positions.

Generated enemies, authored spawns and reward sources receive stable identities within the supported build/content. Opening chest A before B does not change their initial rewards. Loose-item initialization follows the same source-derived rule. Rebuilding a floor does not consume gameplay-time loot events.

## Gameplay-time randomness

Dynamic operations such as enchantment and rewards without a stable source use a separate saved continuation counter. It advances per RNG request, not per draw. Actor combat streams are local to their explicit source/identity; navigation, physics and AI decisions are not a deterministic replay system.

Secret and trap detection use local gameplay streams separate from generation and source loot. Saved discoveries persist, but future failed/successful rolls are not a replay guarantee after reload. Identification uses separate category streams and saves both appearance assignments and discoveries. Item copies and restoration preserve actual rolls without requesting randomness.

## Saved world differences

Untouched floor content regenerates from its seed. Saves retain deviations rather than full floor snapshots:

- Cleared enemies, destroyed nests, killed finite summon slots and gatekeeper defeat.
- Exact explored tiles, discovered trap identities and discovered/locked/current-open door deviations per depth; closing an explored door does not reopen it on restore.
- Removed or changed baseline items, added/player-dropped items and activated reward sources.
- Carried item identities, quantities, rolls, food state and equipment references.

Activated-source records retain enough authored inputs and activation location to reconstruct rewards after their owner is gone. Restoration rebuilds the baseline and rewards, applies validated changes, and restores claimed/open state without firing new pickup, reward or identification effects.

Living enemies reset health, position and tactical state on revisit; active timed buffs are not currently restored. Large-actor placement uses bounded deterministic layout retries unless an authored boss anchor supplies placement. Encounter determinism does not certify navigation or balance. See [level design](level-design.md), [items](stats-and-itemization.md#item-ownership-food-and-persistence) and [combat persistence](combat-system.md#enemy-decisions-and-persistence).

## Save compatibility and acceptance

`SaveGame.CurrentVersion` in [SaveGame.cs](../scripts/saves/SaveGame.cs) is the sole supported-version value. Missing or mismatched versions require a new run. There are no migrations, legacy DTO adapters or fallback conversions. Rejecting an incompatible or invalid save must not overwrite it. Current-version inventory and floor restoration must still preserve supported state correctly.

Verify actual regenerated layouts, encounter definitions/positions and rewards—not merely equal seeds—and save/resume/revisit after changing supported world state. Headless tests cannot certify baked navigation; actual passage remains an editor playtest. Test and command guidance belongs in [AGENTS.md](../AGENTS.md), with remaining acceptance in the [roadmap](development-roadmap.md).
