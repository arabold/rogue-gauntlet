# Arrangement authoring

Arrangements are editable furnishing compositions placed within generated rooms. See [level design](../../../docs/level-design.md#reusable-room-arrangements) for selection and accessibility rules.

## Create or customize

1. Duplicate an arrangement scene and its matching definition `.tres`. Keep the root transform at identity and explicitly assign its direct **Content** Node3D. Put all meshes, GridMaps, instanced scenes, static collision, lights, particles and local components beneath Content. Moving physics bodies are rejected.
2. Assign a unique definition ID and your scene. Set **PlacementBounds** to contain the composition’s static visual and collision geometry, excluding light and particle extents. Keep any scripted motion within that envelope. Set **OccupiedTiles** to cover that envelope. Offsets may span multiple rows; each tile is 4×4 world units, centered on the root anchor plus its offset.
3. Configure the definition in the Inspector:

   | Setting | Purpose |
   | --- | --- |
   | Weight | Relative chance of selecting this composition |
   | MaximumPerRoom | Repeat limit |
   | AllowedPurposes | Eligible architectural room types |
   | WallPositionWeight / InteriorPositionWeight | Relative preference among safe positions, not guaranteed percentages |
   | Placement | Floor furnishing or elevated Wall mount |
   | RequiresWall | Require a backing wall on local North (-Z); always required for Wall placement |
   | AlignToWall | For floor furniture, move its backing edge against the actual supporting wall; requires RequiresWall |
   | WallPlaneDistance | Optional fixed distance from anchor to backing surface; zero follows wall geometry. Bookshelves use 1.5 to overlap wall trim and remain straight |
   | HorizontalClearance | Horizontal gap around floor furniture; zero permits adjoining shelf sections |
   | MountHeight | Wall scene root height above the floor |
   | WallInset | Recess from projecting wall trim to the mounting surface; calibrated for the wall kit |

4. Add the definition to a pool's **Entries**. The pool has separate floor and wall counts, a floor density limit and **PassageRadius**; keep this at least as large as the navigation bake radius and the actors it must accommodate. Make shared Resources unique before editing only one variant.
5. Regenerate to apply changes. Invalid authoring reports errors; a valid arrangement without a safe position is omitted. Whole compositions rotate automatically through all four quarter-turns.

Definitions can assign **Variations**: alternative leaf definitions with their own scene, ID and accurate geometry, retaining the same placement/wall requirements. Alternatives cannot nest. Repeated furnishing functions prefer unused variants before repeats and balance reuse once the set is exhausted, with seeded weighted tie breaking. Generic optional pools likewise prefer their least-used safe entries. A single available arrangement remains repeatable; uniqueness depends on available art and physical fit.

Floor arrangements reserve their footprint. Wall mounts align the envelope’s backing edge to an actual supporting wall span, rotate with it, and are omitted at unsupported or obstructed positions. Elevated decoration can share a floor tile with furniture when their physical envelopes do not overlap.

Only enabled solid static collision affects passage checks; lights, particles and Area3D triggers do not. Keep blocking geometry represented when editing or adding pieces. The dining mesh library includes chair collision. Wall torch, banner and heraldic shield examples reuse existing art; the torch retains its own light and particles.

## Coded room recipes

In **Room → Config**, assign **Coded Generator Preset** to a generator Resource under [Garrison room presets](../garrison/rooms/). Each room type has its own folder containing the recipe and its shell/decorations; reusable shells live in `shared/`. Re-Roll and **Generate Room Scene…** use that recipe's Inspector settings, with the dialog's seed controls. The generic shape/material/furnishing controls are ignored while a coded preset is assigned; clear its path to use them again. Coded standalone previews always generate perimeter walls.

Open the recipe Resource in the Inspector to edit its shell and assigned assets. **ShellBuilder → SurfaceStyles** assigns weighted floor/wall families. Ordinary Garrison presets use stone or single-tone light/dark wood; cellars can use earth. Formal halls/libraries additionally offer patterned wood at a lower weight. Duplicate a surface style to edit its floor and matching straight/half/corner wall items. An empty palette uses the generic Material fallback. Surface styles are selected once per candidate, with a separate deterministic stream. Make shared subresources unique when creating a variant. Barracks and storage use `ModularRoomGenerator`: its weighted **Layouts** pair reusable base layouts (shape, minimum/maximum dimensions and entrances) with content layouts (wall beds, perimeter bays, shelves or ordered composites). Shared policies live under `garrison/layouts/`. Recipes set a minimum usable floor-cell count. Narrow 1×N/2×N rooms are valid; furnishings decide whether they fit. Wall runs and perimeter bays also support irregular footprints. The same content policy can furnish different room identities by changing its assigned composition assets.

Beds, bedside tables, small boxes, torches, banners and shields are separate arrangements. A **BedClusterDefinition** assigns optional Box/Sideboard references and offsets plus a wall-decoration palette/chance. **BoxChance**, **SideboardChance**, and **MirrorChance** vary companions and their side independently. Each part can use its own Variations; unsafe optional companions are omitted. **WallBedsRoomLayout** measures the combined cluster width and fills eligible walls up to its bed limit. **CompositeRoomLayout.Steps** shares reservations between functions; the alchemist preset combines one sleeping quarter with **ShelfRowsRoomLayout**. An empty Steps array explicitly represents an unfurnished passage. Saved room scenes keep every placed piece separately editable.

**RoomAssemblyDefinition** composes a primary arrangement with **RoomAssemblyPart** companions: each has an ID, assigned arrangement/variations, offset, quarter-turn facing and chance. Shared supply-station and workspace presets use this operation. Assemblies try wall ends and corners first. Perimeter supplies similarly favor safe corners; optional loose-supply layouts can set MinimumCount to zero.

**RoomBoundaryPalette** assigns full/half segments and corners. **RoomBoundaryLayout** selects a wall/corner bay, sided walkway or divider. Bay sizes and entrances use the palette’s module length; single-wall bays also align both front stubs. Walkways expose left/right flags, width and end inset. Leave enough end space past projecting wall corners. A sealed actor-sized pocket rejects the complete composition. Half-wall assets here are half-length, full-height decorative partitions; these static furnishings follow the existing non-occluding exploration rule. Barred prisons use `RoomChamberDefinition`/`RoomChamberPatterns` for shared cell dividers and operable interior gates, with bed and assembly functions furnishing each room. See [cell gate access](../../../docs/door-access.md#interior-cell-gates).

Assign optional **Columns**, **ColumnChance**, and the column policy's mesh palette/pair pattern/spacing. Columns are planned around required furnishings and passages; groups that cannot fit are omitted. Clearing Columns disables the pass. Its random stream is independent of required content, so changing the chance does not reroll beds or floor geometry. The shell's generic column setting is suppressed for modular recipes. A failed required composition leaves the previous editor room intact.

Hall presets expose odd width/length choices, paired columns, and optional midpoint side entrances through `SideOpeningChance`; these entrances are independent of generic shell doorway counts. Dining and library recipes retain their own required-slot sizing controls. All required slots have stable IDs, rotate with the structure and pass the shared physical checks; their transforms may include sub-tile offsets. See [coded room generators](../../../docs/level-design.md#coded-room-generators) for runtime construction.

A recipe's **Dressing** Resource assigns optional **Lighting** and **Clutter** policies. Lighting exposes door placement/pairing chances, a lit/unlit ratio, mounting height, a door-side offset to avoid corner trim, and maximum count. Enable **LineHallSides** for regularly spaced hall-side fixtures; assign **BedArrangement** to enable occasional bed-head lights. Both torch definitions must be assigned. Clutter uses an ordinary pool with a small count and a room-appropriate palette. Empty optional references disable that pass. Lights are planned before clutter so loose supplies cannot displace doorway fixtures.

Libraries use separate wall and interior compositions. **ColumnsPerBank** determines width and **StackRows** determines length, with a central aisle and perimeter circulation. The shared **ShelfRowsRoomLayout** exposes row/column counts for shelving inside mixed-use rooms. Wall shelves require **AlignToWall**, while the stack scene joins two shelf banks back to back. **StackRowSpacing** controls row spacing independently of the four-unit tile grid. Shelf sections may abut across columns, while book-facing rows must retain a walkable aisle. **MinimumAisleWidth** defaults to 2.4 units; the 3.8-unit row spacing leaves 2.5 units between the current stacks. Optional **CornerBookcase** short sections extend wall rows toward the corner trim. The complete plan also preserves connected circulation around all banks. Adjoining sections use zero **HorizontalClearance**; keep the scene's bounds and solid collision accurate when editing its geometry.

## Generate and review

Open a Room scene and use **Room → Config** in the 3D toolbar. Enable **Furnishings**, assign an **Arrangement Pool**, and choose a random or manual seed. **Re-Roll** replaces existing arrangement roots and marks the scene unsaved. **Project → Tools → Generate Room Scene…** uses the same settings. The builder retains the resolved **Purpose** in the Room Inspector.

Saved room scenes retain editable arrangement instances and become authored rooms, so gameplay does not furnish them again. Manually placed content remains the room author's responsibility; it is not automatically certified.

Run [room_generation_harness.tscn](../../debug/rooms/room_generation_harness.tscn) to inspect individual recipes or a complete generated floor. Its panel selects the view, recipe, seed and furnishings; modular recipes additionally expose **Layout** and **Columns** selectors. Choose seeded selection or an exact variant, then compare columns off/on at the same seed. Overrides belong to the preview and do not modify production Resources. Single rooms rotate before furnishing. **R** advances the seed, **A** toggles furnishings, **F** switches views, **1–9** selects a recipe and **Q/E** rotates a room. Check actual actor passage as well as appearance. See the [harness index](../../debug/README.md).
