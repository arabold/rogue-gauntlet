# Room boundaries: remaining work

[Level design](../level-design.md#coded-room-generators) and the [authoring guide](../../scenes/levels/arrangements/README.md) describe the implemented composition functions: individual variations, primary/companion assemblies, count-driven shelf banks with joined columns and accessible row aisles, coherent surfaces, open wall/corner bays, sided fenced walkways and half-wall dividers. These emit individual editable pieces and validate every actor-sized free region.

## Opaque chambers and internal locks

Unlocked barred prison cells are implemented through reusable chamber rows and [interior gate capabilities](../door-access.md#interior-cell-gates). Individual collision bounds and furniture swing reservations survive rotation/placement; gates retain independent identities, normal interaction/navigation and saved open state. Bars and low rails follow the existing non-occluding exploration rule.

Opaque partitions still need a room-local sight boundary contract projected through rotation and floor placement. Physical blocking and sight blocking must remain distinct. Internal mechanical locks additionally need guaranteed reachable sources, sub-room access validation and supported saved state; do not add decorative lock state or inaccessible rewards. Exterior connector rules cannot substitute for internal boundaries.

Reuse `RoomChamberDefinition`/`RoomChamberPatterns` for row chambers, `RoomBoundaryPalette`/`RoomBoundaryPatterns` for physical runs and the existing furnishing functions for content. Shared Resources remain immutable; per-generation contexts own plans and reservations. Do not reserve a whole chamber's envelope, which would consume its circulation.

## Sewage content

Add drainage/crossing compositions around the implemented low rails and walkway functions. Keep end/side access to every player-sized free area, select appropriate coherent surface styles, and validate gameplay packing before production selection. Specialized internal gates remain separate from these static decorations.

Required composition failure must preserve the edited room and fail explicitly after bounded attempts. Keep optional content omittable, derive function-specific streams from stable keys, and bump `SaveGame.CurrentVersion` when changing the generation baseline. No historical save conversion is required.

Use the [Godot workflow](../../.agents/skills/godot-mcp/SKILL.md): compile, import/UID generation, validate resources, run affected GdUnit4 suites with an isolated short TMPDIR, and compile ExportRelease. Review actual windowed room/floor captures and actor movement; headless navigation cannot certify baked paths. Move completed behavior into implemented docs and remove its plan text.
