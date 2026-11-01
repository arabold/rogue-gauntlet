using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Replaces centered long-wall modules with junctions and finished inward dividers before collision is built.</summary>
[Tool, GlobalClass]
public partial class DividerWallStyle : WallStyle
{
	[Export] public string Junction { get; set; } = "wall_Tsplit";
	[Export] public string[] Ends { get; set; } = [];
	[Export(PropertyHint.Range, "1,2")] public int Sides { get; set; } = 1;

	public override string[] Validate()
	{
		var errors = new List<string>(base.Validate());
		if (Library == null) return errors.ToArray();
		if (Library.FindItemByName(Junction) < 0 || Ends.Length == 0 || Ends.Any(end => Library.FindItemByName(end) < 0))
			errors.Add($"Wall style {Id}: assign a junction and existing end pieces.");
		return errors.ToArray();
	}

	public override void Build(RoomBlueprint room, RandomNumberGenerator rng)
	{
		base.Build(room, rng);
		var grid = room.Room.WallGridMap;
		var runs = room.WallRuns.Where(run => run.Outward.X != 0 && run.Length >= 3 && run.Length % 2 == 1).ToList();
		GenerationRandom.Shuffle(runs, rng);
		var placedSides = new HashSet<Vector2I>();
		foreach (var run in runs)
		{
			if (placedSides.Contains(run.Outward)) continue;
			var tile = run.TileAt(run.Length / 2);
			var edge = EdgeCell(tile, run.Outward);
			var end = edge + new Vector3I(-run.Outward.X, 0, -run.Outward.Y) * Room.TileSize;
			int original = grid.GetCellItem(edge);
			if (original < 0 || !Straight.ContainsKey(Library.GetItemName(original)) || grid.GetCellItem(end) >= 0
				|| !room.Footprint.IsFloor(tile - run.Outward)) continue;
			int facing = WallFootprints.FacingInto(-run.Outward);
			int turn = Array.IndexOf(GridTurns.Upright, facing);
			int junction = Library.FindItemByName(Junction);
			int tip = Library.FindItemByName(Ends[rng.RandiRange(0, Ends.Length - 1)]);
			int tipFacing = GridTurns.Upright[(turn + 3) % 4];
			var junctionBox = WallFootprints.FootprintWorld(Library.GetItemMesh(junction).GetAabb(), facing, edge);
			var tipBox = WallFootprints.FootprintWorld(Library.GetItemMesh(tip).GetAabb(), tipFacing, end);
			if (Overlaps(junctionBox, tipBox) || grid.GetUsedCells().Cast<Vector3I>().Where(cell => cell != edge).Any(cell =>
			{
				int item = grid.GetCellItem(cell);
				var box = WallFootprints.FootprintWorld(Library.GetItemMesh(item).GetAabb(), grid.GetCellItemOrientation(cell), cell);
				return Overlaps(junctionBox, box) || Overlaps(tipBox, box);
			})) continue;
			grid.SetCellItem(edge, junction, facing);
			grid.SetCellItem(end, tip, tipFacing);
			room.SetWall(tile, run.Outward, .5f, false);
			placedSides.Add(run.Outward);
			if (placedSides.Count == Sides) return;
		}
		throw new InvalidOperationException($"Wall style {Id}: cannot fit {Sides} wall dividers in room {room.Type.Id}.");
	}

	private static bool Overlaps(Aabb a, Aabb b) =>
		Mathf.Min(a.End.X, b.End.X) - Mathf.Max(a.Position.X, b.Position.X) > WallFootprints.Eps
		&& Mathf.Min(a.End.Z, b.End.Z) - Mathf.Max(a.Position.Z, b.Position.Z) > WallFootprints.Eps;
}