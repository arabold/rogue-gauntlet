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

Floor arrangements reserve their footprint. Wall mounts align the envelope’s backing edge to an actual supporting wall span, rotate with it, and are omitted at unsupported or obstructed positions. Elevated decoration can share a floor tile with furniture when their physical envelopes do not overlap.

Only enabled solid static collision affects passage checks; lights, particles and Area3D triggers do not. Keep blocking geometry represented when editing or adding pieces. The dining mesh library includes chair collision. Wall torch, banner and heraldic shield examples reuse existing art; the torch retains its own light and particles.

## Coded room recipes

In **Room → Config**, assign **Coded Generator Preset** to a generator Resource under [Garrison room presets](../garrison/rooms/). Each room type has its own folder containing the recipe and its shell/decorations; reusable shells live in `shared/`. Re-Roll and **Generate Room Scene…** use that recipe's Inspector settings, with the dialog's seed controls. The generic shape/material/furnishing controls are ignored while a coded preset is assigned; clear its path to use them again. Coded standalone previews always generate perimeter walls.

Open the recipe Resource in the Inspector to edit its shell and assigned assets. Make shared subresources unique when creating a variant. Hall presets expose an odd width, a list of compatible odd lengths, a column mesh palette and spacing; they produce centered openings at both aisle ends and columns mirrored on both axes. `SideOpeningChance` adds optional side entrances. Hall openings are chosen by its C# algorithm, independently of the shell’s generic doorway counts. Their primary pool accepts wall decorations; cellar presets select a storage palette and bound cluster count/density. Mess hall, barracks and library recipes own required furnishing slots. Change their assigned composition assets and allowed row/pair/size choices in the Inspector; slots and the room grow together. Slots rotate with the structure and must all pass placement checks. Invalid presets fail before replacing existing editor geometry. See [coded room generators](../../../docs/level-design.md#coded-room-generators) for the two-stage runtime path and extension points.

A recipe's **Dressing** Resource assigns optional **Lighting** and **Clutter** policies. Lighting exposes door placement/pairing chances, a lit/unlit ratio, mounting height, a door-side offset to avoid corner trim, and maximum count. Enable **LineHallSides** for regularly spaced hall-side fixtures; assign **BedArrangement** to enable occasional bed-head lights. Both torch definitions must be assigned. Clutter uses an ordinary pool with a small count and a room-appropriate palette. Empty optional references disable that pass. Lights are planned before clutter so loose supplies cannot displace doorway fixtures.

Libraries use separate wall and interior compositions. Wall shelves require **AlignToWall**, while the stack scene joins two shelf banks back to back. **StackRowSpacing** controls aisle spacing independently of the four-unit tile grid. Adjoining sections use zero **HorizontalClearance**; keep the scene's bounds and solid collision accurate when editing its geometry.

## Generate and review

Open a Room scene and use **Room → Config** in the 3D toolbar. Enable **Furnishings**, assign an **Arrangement Pool**, and choose a random or manual seed. **Re-Roll** replaces existing arrangement roots and marks the scene unsaved. **Project → Tools → Generate Room Scene…** uses the same settings. The builder retains the resolved **Purpose** in the Room Inspector.

Saved room scenes retain editable arrangement instances and become authored rooms, so gameplay does not furnish them again. Manually placed content remains the room author's responsibility; it is not automatically certified.

Run [room_generation_harness.tscn](../../debug/rooms/room_generation_harness.tscn) to inspect individual recipes or a complete generated floor. Its panel selects the view, recipe, seed and furnishings; single-room previews also rotate before furnishing. **R** advances the seed, **A** toggles furnishings, **F** switches views, **1–5** selects a recipe and **Q/E** rotates a room. Check actual actor passage as well as appearance. See the [harness index](../../debug/README.md).
