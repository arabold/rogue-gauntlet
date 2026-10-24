using System.Linq;
using Godot;

/// <summary>Wall-backed shelves and close, double-sided stack banks surround a continuous central aisle.</summary>
[Tool, GlobalClass]
public partial class LibraryRoomGenerator : RoomGenerator
{
	[Export] public RoomArrangementDefinition WallBookcase { get; set; }
	[Export] public RoomArrangementDefinition StackArrangement { get; set; }
	[Export] public int[] Sizes { get; set; } = [5, 7];
	/// <summary>World-unit spacing: a 1.3-deep double-sided stack leaves at least 2.4 units for walking.</summary>
	[Export(PropertyHint.Range, "3.7,4,0.1")] public float StackRowSpacing { get; set; } = 3.8f;

	public override RoomArrangementPool CreateInteriorPool() => CreateSlotPool(WallBookcase, StackArrangement);

	public override string[] Validate()
	{
		var errors = base.Validate().Concat(ValidateSlotShell()).ToList();
		if (Sizes == null || Sizes.Length == 0 || Sizes.Any(size => size != 5 && size != 7)) errors.Add("Library: choose sizes of five or seven tiles.");
		if (!float.IsFinite(StackRowSpacing) || StackRowSpacing < 3.7f || StackRowSpacing > 4) errors.Add("Library: stack row spacing must preserve a 2.4-unit aisle (3.7–4 units).");
		foreach (var definition in new[] { WallBookcase, StackArrangement })
			if (definition != null && (definition.Placement != ArrangementPlacement.Floor || definition.OccupiedTiles == null
				|| !definition.OccupiedTiles.SequenceEqual(new[] { Vector2I.Zero }))) errors.Add("Library: assign single-tile shelf compositions.");
		if (WallBookcase != null && (!WallBookcase.AlignToWall || !WallBookcase.RequiresWall)) errors.Add("Library: wall bookcases must align to their supporting wall.");
		if (StackArrangement != null && (StackArrangement.RequiresWall || StackArrangement.AlignToWall)) errors.Add("Library: interior stacks must be freestanding.");
		return errors.ToArray();
	}

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		int size = Sizes[rng.RandiRange(0, Sizes.Length - 1)];
		if (!PopulateSlotShell(room, rng, standalone, new(size, size))) return false;
		for (int along = 1; along < size - 1; along++)
		{
			if (along != size / 2)
			{
				AddSlot(room, WallBookcase, new(along, 0));
				AddSlot(room, WallBookcase, new(along, size - 1), 2);
			}
			AddSlot(room, WallBookcase, new(0, along), 1);
			AddSlot(room, WallBookcase, new(size - 1, along), 3);
		}
		// Tile-sized sections abut horizontally; row spacing is independent of the coarse map grid.
		for (int x = 1; x < size - 1; x++)
		{
			if (x == size / 2) continue;
			for (float z = Room.TileSize; z <= (size - 2) * Room.TileSize; z += StackRowSpacing)
			{
				int tileZ = Mathf.RoundToInt(z / Room.TileSize);
				AddSlot(room, StackArrangement, new(x, tileZ), 0, new Vector3(0, 0, z - tileZ * Room.TileSize));
			}
		}
		return true;
	}
}