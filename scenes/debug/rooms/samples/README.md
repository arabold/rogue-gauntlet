# Room style samples

Open [room_style_samples.tscn](../room_style_samples.tscn) and run **F6**. Select one of three recipes, change the seed with **R**, rotate with **Q/E**, and inspect circulation with **W**. These are isolated content studies; the production room pool does not include them. The generated-floor view continues to use production content.

| Recipe | Study |
| --- | --- |
| [Partitioned depot](partitioned_depot.tres) | 4×5 tiles: a required T partition, at least three supply clusters, two work/storage stations, shelves and lit fixtures |
| [Compact lockup](compact_lockup.tres) | 4×4 tiles: two cells with brown-earth floors, a stone guard aisle, guard furniture, a weapon rack, shackles and lights |
| [Treasury](treasury.tres) | 3×3 tiles: a gold chest and banner, counting table, ledger bookcase, storage boxes, heraldry and three lit sconces |

The recipes are ordinary `RoomType` resources. They reuse existing shape, placement and surface strategies. [T partition](groups/t_partition.tres) uses `PropGroupMember.Required` for its structural pieces, so a placement cannot silently lose an arm; supply companions remain optional. Mutable placement state stays on `RoomBlueprint`.

Footprints are bounded independently for each composition. The larger depot requires more furnishing groups and content variety than the treasury. Required wall decoration and lit sconces are part of each recipe, alongside optional finishing clutter. Cell-floor patches come from fitted `CellsStep` geometry; the material remains editable independently of cell layout.

## Treasury rewards

The [treasury chest](treasury_chest.tscn) inherits the ordinary rare chest and overrides only its [gold reward profile](treasury_gold.tres). It guarantees **240 gold** with no depth scaling, configured by `Quantity` in [treasury_gold_table.tres](treasury_gold_table.tres). This is sample tuning, not a realm economy target. It uses the existing chest reward path; ordinary chest profiles are unchanged.

Loose piles and table coins are **decorative**. The real reward is in the chest. The static room harness previews the composition; player interaction, collection and level persistence require a gameplay context.

Adding samples to a production room pool, room-specific loot budgets, broader spatial zoning and vertical layouts are follow-up decisions after visual review.
