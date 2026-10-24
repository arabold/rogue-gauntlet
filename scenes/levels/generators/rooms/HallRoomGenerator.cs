using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>A rectangular columned hall with wall fixtures and an unfurnished central aisle.</summary>
[Tool, GlobalClass]
public partial class HallRoomGenerator : RoomGenerator
{
	[Export] public RoomArrangementPool WallDecorations { get; set; }
	/// <summary>Odd dimensions give every wall a unique central doorway tile.</summary>
	[Export(PropertyHint.Range, "3,11,2")] public int WidthTiles { get; set; } = 5;
	[Export] public int[] LengthTiles { get; set; } = [5, 7];
	/// <summary>Each side wall may add a central opening; both aisle ends are always open.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float SideOpeningChance { get; set; } = .35f;
	[Export] public string[] ColumnItems { get; set; } = ["pillar", "column"];
	[Export(PropertyHint.Range, "1,4")] public int ColumnSpacing { get; set; } = 2;

	public override RoomArrangementPool CreateInteriorPool() => WallDecorations;

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		int length = LengthTiles[rng.RandiRange(0, LengthTiles.Length - 1)];
		using var builder = (ProceduralRoomBuilder)ShellBuilder.Duplicate();
		builder.GenerateWalls = standalone;
		// Match the floor planner's primary axis, including its X-axis tie break for square halls.
		bool aisleAlongX = WidthTiles == length;
		var end = aisleAlongX ? Vector2I.Right : Vector2I.Down;
		var side = aisleAlongX ? Vector2I.Down : Vector2I.Right;
		var openings = new List<Vector2I> { -end, end };
		if (rng.Randf() < SideOpeningChance) openings.Add(-side);
		if (rng.Randf() < SideOpeningChance) openings.Add(side);
		if (!builder.PopulateRoomAtSize(room, rng, WidthTiles, length, openings.ToArray())) return false;
		room.LayoutAxis = end;
		using var columns = GenerationRandom.Create(rng.Seed, GenerationRandom.Decoration, "hall-columns");
		int column = builder.DecorationLibrary.FindItemByName(ColumnItems[columns.RandiRange(0, ColumnItems.Length - 1)]);
		// Corner coordinates reflect across the room center. Stop before opposing rows crowd each other.
		for (int near = 1; near * 2 <= length; near += ColumnSpacing)
		{
			int far = length - near;
			if (far != near && far - near < ColumnSpacing) break;
			foreach (int along in new[] { near, far }.Distinct())
			foreach (int across in new[] { WidthTiles / 2, WidthTiles / 2 + 1 })
			{
				// The shared GreatHall floor planner runs its aisle along the longest axis (X on a square).
				var corner = WidthTiles == length ? new Vector2I(along, across) : new Vector2I(across, along);
				room.DecorationGridMap.SetCellItem(new Vector3I(corner.X * Room.TileSize - Room.TileSize / 2, 0,
					corner.Y * Room.TileSize - Room.TileSize / 2), column);
			}
		}
		return true;
	}

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		if (ShellBuilder != null && (ShellBuilder.ShapeArchetype != RoomShapeArchetype.Rectangle
			|| ShellBuilder.Purpose != RoomPurpose.GreatHall || ShellBuilder.ColumnChance != 0 || ShellBuilder.EnableChasms))
			errors.Add("Hall: assign a rectangular GreatHall shell with generic columns and chasms disabled.");
		if (WidthTiles < 3 || WidthTiles > 11 || WidthTiles % 2 == 0 || LengthTiles == null || LengthTiles.Length == 0
			|| LengthTiles.Any(length => length < WidthTiles || length > 11 || length % 2 == 0 || length - 2 < ColumnSpacing)
			|| LengthTiles.Distinct().Count() != LengthTiles.Length)
			errors.Add("Hall: choose an odd width (3–11) and unique odd lengths at least as large as the width (up to 11).");
		if (ColumnSpacing < 1 || ColumnSpacing > 4 || ColumnItems == null || ColumnItems.Length == 0
			|| ShellBuilder?.DecorationLibrary == null || ColumnItems.Any(item => string.IsNullOrWhiteSpace(item) || ShellBuilder.DecorationLibrary.FindItemByName(item) < 0))
			errors.Add("Hall: assign an existing column mesh and spacing from 1 to 4 tiles.");
		if (!float.IsFinite(SideOpeningChance) || SideOpeningChance < 0 || SideOpeningChance > 1)
			errors.Add("Hall: side opening chance must be between zero and one.");
		if (WallDecorations == null) errors.Add("Hall: assign wall decorations.");
		else if (WallDecorations.Entries == null || WallDecorations.Entries.Any(entry => entry == null || entry.Placement != ArrangementPlacement.Wall))
			errors.Add("Hall: the primary decoration pool accepts only wall-mounted arrangements.");
		return errors.ToArray();
	}
}