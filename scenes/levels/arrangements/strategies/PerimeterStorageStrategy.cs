using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Furniture may occupy the perimeter only; all ordinary collision and passage checks still apply.</summary>
[Tool, GlobalClass]
public partial class PerimeterStorageStrategy : ConnectedArrangementStrategy
{
	[Export(PropertyHint.Range, "0,1,0.05")] public float WallOffset { get; set; } = .5f;
	[Export(PropertyHint.Range, "1,8,0.5")] public float CornerWeight { get; set; } = 3;

	protected override Transform3D CandidateTransform(RoomRegion room, RoomArrangementDefinition definition,
		Vector2I anchor, int turn, Transform3D defaultTransform)
	{
		var outward = Rotate(Vector2I.Up, turn);
		defaultTransform.Origin -= new Vector3(outward.X, 0, outward.Y) * WallOffset;
		// Shift off the perpendicular wall as well when choosing an actual corner.
		if (outward.X == 0)
		{
			if (!room.Tiles.Contains(anchor + Vector2I.Left)) defaultTransform.Origin += Vector3.Right * WallOffset;
			else if (!room.Tiles.Contains(anchor + Vector2I.Right)) defaultTransform.Origin += Vector3.Left * WallOffset;
		}
		else
		{
			if (!room.Tiles.Contains(anchor + Vector2I.Up)) defaultTransform.Origin += Vector3.Back * WallOffset;
			else if (!room.Tiles.Contains(anchor + Vector2I.Down)) defaultTransform.Origin += Vector3.Forward * WallOffset;
		}
		return defaultTransform;
	}

	protected override double CandidateWeight(HashSet<Vector2I> floor, Vector2I[] occupied, bool atWall) => !atWall ? 0
		: occupied.Any(tile => MapData.CardinalOffsets.Count(direction => !floor.Contains(tile + direction)) >= 2) ? CornerWeight : 1;
}