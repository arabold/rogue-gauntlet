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

	public override bool Build(RoomBlueprint room, RandomNumberGenerator rng)
	{
		if (!base.Build(room, rng)) return false;
		var grid = room.Room.WallGridMap;
		var runs = room.WallRuns.Where(run => run.Outward.X != 0 && run.Length >= 3 && run.Length % 2 == 1).ToList();
		GenerationRandom.Shuffle(runs, rng);
		var placedSides = new HashSet<Vector2I>();
		foreach (var run in runs)
		{
			if (placedSides.Contains(run.Outward)) continue;
			var tile = run.TileAt(run.Length / 2);
			var edge = EdgeCell(tile, run.Outward);
			int original = grid.GetCellItem(edge);
			if (original < 0 || !Straight.ContainsKey(Library.GetItemName(original))
				|| !room.Footprint.IsFloor(tile - run.Outward)) continue;
			int facing = WallFootprints.FacingInto(-run.Outward);
			int turn = Array.IndexOf(GridTurns.Upright, facing);
			int junction = Library.FindItemByName(Junction);
			int tip = Library.FindItemByName(Ends[rng.RandiRange(0, Ends.Length - 1)]);
			int tipFacing = GridTurns.Upright[(turn + 3) % 4];
			// Match the measured ends, not a presumed tile length. GridMap origins are integer coordinates;
			// a piece requiring a fractional origin cannot be fitted here.
			var junctionBounds = WallFootprints.FootprintWorld(WallFootprints.MeshBounds(Library, junction), facing, edge);
			var tipBounds = WallFootprints.FootprintWorld(WallFootprints.MeshBounds(Library, tip), tipFacing, edge);
			var inward = new Vector3I(-run.Outward.X, 0, -run.Outward.Y);
			float junctionEnd = inward.X != 0 ? (inward.X > 0 ? junctionBounds.End.X : -junctionBounds.Position.X)
				: (inward.Z > 0 ? junctionBounds.End.Z : -junctionBounds.Position.Z);
			float tipStart = inward.X != 0 ? (inward.X > 0 ? tipBounds.Position.X : -tipBounds.End.X)
				: (inward.Z > 0 ? tipBounds.Position.Z : -tipBounds.End.Z);
			float distance = junctionEnd - tipStart;
			int steps = Mathf.RoundToInt(distance);
			if (steps <= 0 || Mathf.Abs(distance - steps) > .001f) continue;
			var end = edge + inward * steps;
			if (grid.GetCellItem(end) >= 0) continue;
			bool Overlaps(int item, int orientation, Vector3I cell, int other, int otherOrientation, Vector3I otherCell) =>
				WallFootprints.PiecesOverlap(Library, item, orientation, cell, other, otherOrientation, otherCell);
			if (Overlaps(junction, facing, edge, tip, tipFacing, end) || grid.GetUsedCells().Cast<Vector3I>().Where(cell => cell != edge).Any(cell =>
			{
				int item = grid.GetCellItem(cell);
				int orientation = grid.GetCellItemOrientation(cell);
				return Overlaps(junction, facing, edge, item, orientation, cell) || Overlaps(tip, tipFacing, end, item, orientation, cell);
			})) continue;
			grid.SetCellItem(edge, junction, facing);
			grid.SetCellItem(end, tip, tipFacing);
			room.Room.DividerCells.Add(edge);
			room.Room.DividerCells.Add(end);
			room.SetWall(tile, run.Outward, .5f, false);
			placedSides.Add(run.Outward);
			if (placedSides.Count == Sides) return true;
		}
		return false;
	}
}