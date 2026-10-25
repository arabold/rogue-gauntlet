# Item quality, affixes and curses: remaining work

Status: slices 1–3 landed on October 25, 2026:
- qualities (Poor, Normal, Superior, Magic, Rare) introduced by depth;
- per-type roll tables and depth-tiered affix families;
- per-item identification;
- hidden, sticky curses with the Scroll of Remove Curse.

The [stats and itemization](../stats-and-itemization.md#equipment-quality-affixes-and-enchantment) and [item identification](../item-identification-system.md#per-item-knowledge) documents describe them. This plan keeps only what remains.

## Source ladders (slice 4, optional)

Elite enemies and rare chests should roll better qualities without changing ordinary drops. The Forsaken Warden already does this with `quality_ladder_gatekeeper.tres` and `gatekeeper_item_policy.tres`.

- **Resources:** author a richer `QualityLadder` (for example `quality_ladder_rich.tres`) with earlier Magic and Rare introductions and higher growth. Add an `ItemCreationPolicy` that references it, with the same curse settings.
- **Wiring:** assign that policy on the elite enemies' and rare chests' `LootTableComponent.CreationPolicy` in their scenes. No code changes, because creation already takes the source's policy.
- **Verify:** a sweep over both policies shows the richer ladder's Magic and Rare shares above the ordinary ladder's at every depth, while ordinary sources are unchanged.

## Later options

- **Rare names:** generated two-word names, as in Diablo II, instead of the first prefix and suffix.
- **Unique items:** hand-authored items with fixed affixes and their own names, in the reserved orange.
- **Affix families:** families that need new systems: elemental damage, life steal, regeneration, light radius.

## Open decisions

- **Tuning:** quality odds, affix ranges, adjectives, the curse chance and penalty strengths are draft data, tuned in the Inspector.
