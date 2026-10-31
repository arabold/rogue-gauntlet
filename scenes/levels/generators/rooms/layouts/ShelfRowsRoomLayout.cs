using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Reusable wall shelves and optional freestanding shelf rows, suitable for libraries and mixed-use rooms.</summary>
[Tool, GlobalClass]
public partial class ShelfRowsRoomLayout : RoomContentLayout
{
	[Export] public RoomArrangementDefinition WallShelf { get; set; }
	[Export] public RoomArrangementDefinition StackArrangement { get; set; }
	[Export(PropertyHint.Flags, "North,East,South,West")] public int AllowedWalls { get; set; } = 15;
	[Export(PropertyHint.Range, "0,32")] public int MaximumWallShelves { get; set; } = 8;
	[Export(PropertyHint.Range, "0,8")] public int Rows { get; set; } = 3;
	[Export(PropertyHint.Range, "1,3")] public int ColumnsPerBank { get; set; } = 1;
	[Export(PropertyHint.Range, "0,4,0.1")] public float RowAisleWidth { get; set; } = 2.5f;
	[Export(PropertyHint.Range, "0,32")] public int MinimumShelves { get; set; } = 1;
	public override RoomArrangementDefinition[] Definitions => new[] { WallShelf, StackArrangement }.Where(entry => entry != null).SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).ToArray();
	public override int MaximumCount => MaximumWallShelves + Rows * ColumnsPerBank * 2;

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (WallShelf == null && StackArrangement == null) errors.Add("Shelves: assign a wall or freestanding composition.");
		foreach (var definition in Definitions) errors.AddRange(definition.Validate());
		if (WallShelf != null && (!WallShelf.RequiresWall || !WallShelf.AlignToWall)) errors.Add("Shelves: wall sections must align to their supporting wall.");
		if (StackArrangement?.RequiresWall == true) errors.Add("Shelves: interior sections must be freestanding.");
		if (AllowedWalls < 0 || (AllowedWalls & ~15) != 0 || MaximumWallShelves < 0 || MaximumWallShelves > 32
			|| Rows < 0 || Rows > 8 || ColumnsPerBank < 1 || ColumnsPerBank > 3 || MinimumShelves < 0 || MinimumShelves > MaximumCount
			|| !float.IsFinite(RowAisleWidth) || RowAisleWidth < (Rows > 1 ? 2.1f : 0) || RowAisleWidth > 4 || MaximumCount > 64)
			errors.Add("Shelves: assign bounded row/column counts, aisle width and minimum content.");
		return errors.ToArray();
	}

	public override bool Populate(RoomFurnishingContext context, string key)
	{
		int count = 0;
		if (StackArrangement != null && Rows > 0)
		{
			if (Rows > 1 && RowAisleWidth < context.PassageRadius * 2 + .1f) return false;
			float spacing = context.Bounds(StackArrangement).Size.Z + RowAisleWidth;
			int width = context.Room.Map.Width, height = context.Room.Map.Height;
			int rows = Mathf.Min(Rows, Mathf.FloorToInt((height - 3) * Room.TileSize / spacing) + 1);
			for (int bank = 0; bank < 2; bank++)
			for (int column = 0; column < ColumnsPerBank; column++)
			{
				int x = bank == 0 ? 1 + column : width - 2 - column;
				if (x <= 0 || x >= width - 1 || x == width / 2 || (bank == 0 ? x > width / 2 : x < width / 2)) continue;
				count += RoomFurnishingPatterns.PlaceBookshelfRows(context, StackArrangement, context.Center(new(x, 1)),
					1, Mathf.Max(0, rows), spacing, 0, $"{key}:stacks:{bank}:{column}");
			}
		}
		if (WallShelf != null)
		{
			int walls = 0;
			foreach (var wall in context.WallSpans().OrderByDescending(wall => wall.LengthTiles))
			{
				if ((AllowedWalls & (int)DoorwayMarker.GetDirectionFlag(wall.Outward)) == 0) continue;
				walls += RoomFurnishingPatterns.PlaceShelvesAlongWall(context, WallShelf, wall, 0, .05f,
					MaximumWallShelves - walls, $"{key}:wall:{wall.Start}:{wall.Outward}");
				if (walls >= MaximumWallShelves) break;
			}
			count += walls;
		}
		return count >= MinimumShelves;
	}
}