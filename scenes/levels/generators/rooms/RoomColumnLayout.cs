using Godot;
using System.Collections.Generic;
using System.Linq;

public enum RoomColumnPattern { AislePairs, PerimeterPairs }

/// <summary>Optional column groups planned after required furniture. A rejected group cannot displace content or split free space.</summary>
[Tool, GlobalClass]
public partial class RoomColumnLayout : Resource
{
	[Export] public RoomColumnPattern Pattern { get; set; }
	[Export] public string[] ColumnItems { get; set; } = ["pillar"];
	[Export(PropertyHint.Range, "1,4")] public int SpacingTiles { get; set; } = 2;
	[Export(PropertyHint.Range, "1,16")] public int MaximumGroups { get; set; } = 3;
	/// <summary>Local longitudinal adjustment in one-unit GridMap cells to fit beside furniture.</summary>
	[Export(PropertyHint.Range, "0,2")] public int AlongAdjustmentCells { get; set; } = 2;

	/// <summary>Adjust pairs across the room to fit beside furniture while retaining connected passage.</summary>
	[Export(PropertyHint.Range, "0,2")] public int AcrossAdjustmentCells { get; set; } = 2;

	public string[] Validate(MeshLibrary library)
	{
		var errors = new List<string>();
		if (!System.Enum.IsDefined(Pattern) || SpacingTiles < 1 || SpacingTiles > 4 || MaximumGroups < 1 || MaximumGroups > 16 || AlongAdjustmentCells < 0 || AlongAdjustmentCells > 2 || AcrossAdjustmentCells < 0 || AcrossAdjustmentCells > 2)
			errors.Add("Columns: choose a valid pattern, spacing and group limit.");
		if (library == null || ColumnItems == null || ColumnItems.Length == 0
			|| ColumnItems.Any(item => string.IsNullOrWhiteSpace(item) || library.FindItemByName(item) < 0))
			errors.Add("Columns: assign existing meshes in the shell decoration library.");
		return errors.ToArray();
	}

	public void Populate(Room room, IReadOnlyList<RoomArrangementPlacement> contents, RandomNumberGenerator rng, float passageRadius)
	{
		var region = RoomContentLayout.Describe(room);
		var walls = RoomArrangement.GetGridBounds(room.WallGridMap, room.WallGridMap.Transform);
		var furniture = contents.Select(placement => (Bounds: RoomDressingProfile.GetBounds([placement])[0],
			Clearance: placement.Definition.HorizontalClearance)).ToArray();
		var blocking = RoomDressingProfile.GetBounds(contents, true);
		var passage = new RoomPassageValidator(room.Map, region.Tiles.ToHashSet(), region.ConnectorTiles,
			tile => room.TileCenterCell(tile.X, tile.Y), walls.Concat(blocking).ToArray(), passageRadius);
		var accepted = new List<Aabb>();
		int width = room.Map.Width, height = room.Map.Height;
		var library = room.DecorationGridMap.MeshLibrary;
		int item = library.FindItemByName(ColumnItems[rng.RandiRange(0, ColumnItems.Length - 1)]);
		var used = new HashSet<Vector3I>();
		int groups = 0;
		using var probe = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		var adjustments = Enumerable.Range(-AlongAdjustmentCells, AlongAdjustmentCells * 2 + 1)
			.OrderBy(System.Math.Abs).ThenBy(offset => offset).ToArray();
		for (int along = 1; along < height && groups < MaximumGroups; along += SpacingTiles)
		{
			int left = Pattern == RoomColumnPattern.AislePairs ? width / 2 : 1;
			int right = Pattern == RoomColumnPattern.AislePairs ? width / 2 + 1 : width - 1;
			var corners = new[] { new Vector2I(left, along), new Vector2I(right, along) }.Distinct().ToArray();
			bool placed = false;
			foreach (int across in Enumerable.Range(-AcrossAdjustmentCells, AcrossAdjustmentCells * 2 + 1).OrderBy(System.Math.Abs).ThenBy(offset => offset))
			foreach (int adjustment in adjustments)
			{
				if (placed) break;
				var cells = corners.Select((corner, index) => room.TileCenterCell(corner.X, corner.Y) - new Vector3I(2 + (index == 0 ? across : -across), 0, 2 - adjustment)).ToArray();
				if (cells.Distinct().Count() != cells.Length || cells.Any(used.Contains)) continue;
				probe.Clear();
				foreach (var cell in cells) probe.SetCellItem(cell, item);
				var geometry = RoomArrangement.GetGridBounds(probe, Transform3D.Identity);
				var collision = RoomArrangement.GetGridBounds(probe, Transform3D.Identity, false);
				if (geometry.Any(box => !HasFloorSupport(room, region, box)
					|| furniture.Any(other => ConnectedArrangementStrategy.Overlaps(box, other.Bounds, other.Clearance))
					|| walls.Concat(accepted).Any(other => ConnectedArrangementStrategy.Overlaps(box, other)))
					|| !passage.IsConnected(accepted.Concat(collision).ToArray())) continue;
				foreach (var cell in cells) { room.DecorationGridMap.SetCellItem(cell, item); used.Add(cell); }
				accepted.AddRange(collision);
				groups++;
				placed = true;
				break;
			}
		}
	}

	/// <summary>Test the complete column envelope, including positions near a wall or a recessed L-shaped corner.</summary>
	private static bool HasFloorSupport(Room room, RoomRegion region, Aabb box)
	{
		var first = room.LocalToTile(box.Position + Vector3.One * .001f);
		var last = room.LocalToTile(box.End - Vector3.One * .001f);
		for (int x = first.X; x <= last.X; x++)
		for (int z = first.Y; z <= last.Y; z++)
			if (!region.Tiles.Contains(new(x, z))) return false;
		return true;
	}
}