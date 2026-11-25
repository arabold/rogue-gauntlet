using System.Linq;
using Godot;

/// <summary>Which long wall a cross-fire row's spout hangs on.</summary>
public enum SpoutSides { Alternate, Both, Left, Right }

/// <summary>
/// Gauntlet rows of wall spouts on the side walls, firing straight across the room. A one-sided row whose wall has no
/// plain stretch there (an alcove, a pillar) hangs its spout on the opposite wall instead; a two-sided row stands with
/// whichever spouts fit.
/// </summary>
[Tool, GlobalClass]
public partial class CrossFireStep : HazardRowsStep
{
	/// <summary>The wall each row's spout hangs on.</summary>
	[Export] public SpoutSides Sides { get; set; } = SpoutSides.Alternate;

	protected override PropMount Mount => PropMount.Wall;

	protected override bool LayRow(RoomBlueprint room, RandomNumberGenerator rng, int row, int[] columns, int index, HazardBeat beat)
	{
		var left = (new Vector2I(columns[0], row), Vector2I.Left);
		var right = (new Vector2I(columns[^1], row), Vector2I.Right);
		if (Sides == SpoutSides.Both) return HangSpout(room, rng, left, beat) | HangSpout(room, rng, right, beat);
		bool leftFirst = Sides == SpoutSides.Left || Sides == SpoutSides.Alternate && index % 2 == 0;
		return HangSpout(room, rng, leftFirst ? left : right, beat) || HangSpout(room, rng, leftFirst ? right : left, beat);
	}

	/// <summary>Hangs one spout centred on <paramref name="wall"/>'s tile edge, on <paramref name="beat"/>.</summary>
	private bool HangSpout(RoomBlueprint room, RandomNumberGenerator rng, (Vector2I Tile, Vector2I Outward) wall, HazardBeat beat)
	{
		var run = room.WallRuns.FirstOrDefault(run => run.Outward == wall.Outward && run.IndexOf(wall.Tile) >= 0);
		if (run.Length == 0) return false;
		int index = run.IndexOf(wall.Tile);
		float middle = run.TileCenterAlong(index);
		foreach (float center in run.MountCenters(index, room.DecorMounts).OrderBy(center => Mathf.Abs(center - middle)))
		{
			var plan = Next(room, rng, out var choice);
			int piece = room.Pieces.Count;
			if (plan.IsEmpty || !TryPlaceAgainstWall(room, choice, plan, run, center - room.WidthAlong(plan, run) / 2)) continue;
			room.AssignBeat(piece, beat);
			return true;
		}
		return false;
	}
}
