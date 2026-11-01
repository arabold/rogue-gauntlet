# Room style samples

Open [room_style_samples.tscn](../room_style_samples.tscn) and run **F6**. Select one of the three preview entries, change the seed with **R**, rotate with **Q/E**, and inspect circulation with **W**. The depot and treasury are isolated content studies. The prison entry references the existing production prison recipe directly. The generated-floor view continues to use production content.

| Recipe | Study |
| --- | --- |
| [Partitioned depot](partitioned_depot.tres) | 4×5 tiles: a central T partition or doorless storage bays along one or both long walls, at least three supply clusters, two work/storage stations, shelves and lit fixtures |
| [Prison](../../../levels/rooms/room_types/prison.tres) | The production prison: seeded cell rows with brown or gray dirt floors, stone or brick aisles, guard furniture, shackles and lights |
| [Treasury](treasury.tres) | 3×3 tiles: a gold chest and banner, counting table, ledger bookcase, storage boxes, heraldry and three lit sconces |

All entries are ordinary `RoomType` resources; the prison has one shared definition for previews and production. They reuse existing shape, placement and surface strategies. [T partition](groups/t_partition.tres) uses `PropGroupMember.Required` for its structural pieces, so a placement cannot silently lose an arm; supply companions remain optional. The alternate `DividerWallStyle` replaces a straight perimeter module with a T junction and adds a straight or sloped end piece on one or both long walls before collision is recorded. The selected wall style enables the matching central partition where appropriate, leaving the central aisle and doorway approaches clear. Mutable placement state stays on `RoomBlueprint`.

Footprints are bounded independently for each composition. The larger depot requires more furnishing groups and content variety than the treasury. The depot and treasury require wall decoration and lit sconces, alongside optional finishing clutter. The prison's `CellsStep.CellFloors` palette chooses one material per room on a cosmetic RNG stream. Cell-floor patches follow fitted geometry without changing cell layout or walkability.

## Treasury rewards

The [treasury chest](treasury_chest.tscn) inherits the ordinary rare chest and overrides only its [gold reward profile](treasury_gold.tres). It guarantees **240 gold** with no depth scaling, configured by `Quantity` in [treasury_gold_table.tres](treasury_gold_table.tres). This is sample tuning, not a realm economy target. It uses the existing chest reward path; ordinary chest profiles are unchanged.

Loose piles and table coins are **decorative**. The real reward is in the chest. The static room harness previews the composition; player interaction, collection and level persistence require a gameplay context.

Adding samples to a production room pool, room-specific loot budgets, broader spatial zoning and vertical layouts are follow-up decisions after visual review.
