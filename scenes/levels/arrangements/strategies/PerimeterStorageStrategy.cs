using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Storage may occupy the rectangular perimeter only; all ordinary collision and passage checks still apply.</summary>
[Tool, GlobalClass]
public partial class PerimeterStorageStrategy : ConnectedArrangementStrategy
{
	protected override double CandidateWeight(HashSet<Vector2I> floor, Vector2I[] occupied, bool atWall)
	{
		if (!atWall) return 0;
		int minX = floor.Min(tile => tile.X), maxX = floor.Max(tile => tile.X);
		int minZ = floor.Min(tile => tile.Y), maxZ = floor.Max(tile => tile.Y);
		return occupied.All(tile => tile.X == minX || tile.X == maxX || tile.Y == minZ || tile.Y == maxZ) ? 1 : 0;
	}
}