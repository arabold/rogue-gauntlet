using System.Linq;
using Godot;

/// <summary>Wall-backed shelves and close, double-sided stack banks surround a continuous central aisle.</summary>
[Tool, GlobalClass]
public partial class LibraryRoomGenerator : RoomGenerator
{
	[Export] public RoomArrangementDefinition WallBookcase { get; set; }
	[Export] public RoomArrangementDefinition StackArrangement { get; set; }
	[Export] public RoomArrangementDefinition CornerBookcase { get; set; }
	[Export(PropertyHint.Range, "2.1,4,0.1")] public float MinimumAisleWidth { get; set; } = 2.4f;
	/// <summary>Adjoining shelf sections on each side of the central aisle; determines the room width.</summary>
	[Export(PropertyHint.Range, "1,3")] public int ColumnsPerBank { get; set; } = 1;
	/// <summary>Eligible stack-row counts; each chosen count determines the room length.</summary>
	[Export] public int[] StackRows { get; set; } = [3, 5];
	/// <summary>World-unit row spacing; every pair of book-facing rows retains an accessible aisle.</summary>
	[Export(PropertyHint.Range, "3.4,8,0.1")] public float StackRowSpacing { get; set; } = 3.8f;

	public override RoomArrangementPool CreateInteriorPool() => CornerBookcase == null ? CreateSlotPool(WallBookcase, StackArrangement) : CreateSlotPool(WallBookcase, StackArrangement, CornerBookcase);

	public override string[] Validate()
	{
		var errors = base.Validate().Concat(ValidateSlotShell()).ToList();
		if (ColumnsPerBank < 1 || ColumnsPerBank > 3) errors.Add("Library: choose one to three shelf columns per bank.");
		if (StackRows == null || StackRows.Length == 0 || StackRows.Any(rows => rows < 1 || rows > 8)) errors.Add("Library: choose one to eight stack rows.");
		else if (StackRows.Any(rows => 2 * ColumnsPerBank * rows + 4 * ColumnsPerBank + 2 * (SizeForRows(rows).Y - 2) + (CornerBookcase != null ? 8 : 0) > 64))
			errors.Add("Library: shelf counts exceed the 64-piece furnishing budget.");
		if (!float.IsFinite(StackRowSpacing) || StackRowSpacing < 1.3f || StackRowSpacing > 8 || !float.IsFinite(MinimumAisleWidth) || MinimumAisleWidth < 2.1f || MinimumAisleWidth > 4)
			errors.Add("Library: assign row spacing and a player-width shelf aisle.");
		if (StackRows != null && StackRows.Any(rows => SizeForRows(rows).X > 12 || SizeForRows(rows).Y > 12)) errors.Add("Library: shelf counts exceed the shell's size limit.");
		if (errors.Count == 0 && StackRows.Any(rows => rows > 1)
			&& StackRowSpacing < StackArrangement.Variants.Max(entry => entry.GetPlacementBounds().Size.Z) + MinimumAisleWidth - .001f)
			errors.Add("Library: leave a player-width aisle between the faces of adjacent shelf rows.");
		foreach (var definition in new[] { WallBookcase, StackArrangement, CornerBookcase })
			if (definition != null && (definition.Placement != ArrangementPlacement.Floor || definition.OccupiedTiles == null
				|| !definition.OccupiedTiles.SequenceEqual(new[] { Vector2I.Zero }))) errors.Add("Library: assign single-tile shelf compositions.");
		if (WallBookcase != null && (!WallBookcase.AlignToWall || !WallBookcase.RequiresWall)) errors.Add("Library: wall bookcases must align to their supporting wall.");
		if (StackArrangement != null && (StackArrangement.RequiresWall || StackArrangement.AlignToWall)) errors.Add("Library: interior stacks must be freestanding.");
		if (CornerBookcase != null && (!CornerBookcase.RequiresWall || !CornerBookcase.AlignToWall)) errors.Add("Library: corner sections must align to their supporting wall.");
		return errors.ToArray();
	}

	/// <summary>Round the selected shelf rows outward to tiles, retaining the central and perimeter aisles.</summary>
	public Vector2I SizeForRows(int rows) => new(ColumnsPerBank * 2 + 3, Mathf.CeilToInt((rows - 1) * StackRowSpacing / Room.TileSize) + 3);

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		int rows = StackRows[rng.RandiRange(0, StackRows.Length - 1)];
		var size = SizeForRows(rows);
		int width = size.X, length = size.Y;
		if (!PopulateSlotShell(room, rng, true, new(width, length))) return false;
		var context = new RoomFurnishingContext(room, rng.Seed, 1);
		bool WallShelf(Vector2I tile, int turn)
		{
			string id = $"wall:{tile}:{turn}";
			foreach (var definition in context.Variants(WallBookcase, id))
				if (context.TryPlace(definition, new(new Basis(Vector3.Up, turn * Mathf.Pi / 2), context.Center(tile)), id, out _, checkPassage: false)) return true;
			return false;
		}
		// The end walls keep their central entrance tile clear; the side walls are shelved end to end.
		for (int x = 1; x < width - 1; x++)
		{
			if (x == width / 2) continue;
			if (!WallShelf(new(x, 0), 0)) return false;
			if (!WallShelf(new(x, length - 1), 2)) return false;
		}
		for (int z = 1; z < length - 1; z++)
		{
			if (!WallShelf(new(0, z), 1)) return false;
			if (!WallShelf(new(width - 1, z), 3)) return false;
		}
		// Half-width sections join the existing rows to the corner trim without leaving an empty corner tile.
		if (CornerBookcase != null)
			foreach (int x in new[] { 0, width - 1 })
			foreach (int z in new[] { 0, length - 1 })
			{
				var corner = context.Center(new(x, z));
				var northSouth = corner + Vector3.Right * (x == 0 ? 1 : -1);
				var westEast = corner + Vector3.Back * (z == 0 ? 1 : -1);
				context.TryPlace(CornerBookcase, new(new Basis(Vector3.Up, z == 0 ? 0 : Mathf.Pi), northSouth), $"corner:{x}:{z}:end", out _, checkPassage: false);
				context.TryPlace(CornerBookcase, new(new Basis(Vector3.Up, x == 0 ? Mathf.Pi / 2 : 3 * Mathf.Pi / 2), westEast), $"corner:{x}:{z}:side", out _, checkPassage: false);
			}
		// Tile-sized sections abut horizontally; row spacing is independent of the coarse map grid.
		foreach (int x in new[] { 1, width / 2 + 1 })
			if (RoomFurnishingPatterns.PlaceBookshelfRows(context, StackArrangement, context.Center(new(x, 1)),
				ColumnsPerBank, rows, StackRowSpacing, 0, $"stacks:{x}") != rows * ColumnsPerBank) return false;
		if (!context.IsConnected) return false;
		AddPlannedSlots(room, context.Placements, "library");
		return true;
	}
}