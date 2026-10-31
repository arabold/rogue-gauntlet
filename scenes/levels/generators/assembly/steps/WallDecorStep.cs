using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Wall-mounted decoration: torches flanking doorways, or banners, shields and sconces spread along plain walls.
/// Only hangs on decoratable wall pieces and never overlaps tall furniture.
/// </summary>
[Tool, GlobalClass]
public partial class WallDecorStep : FurnishingStep
{
	[Export] public bool BesideDoorways { get; set; }
	/// <summary>Distance from a doorway's center line to the fixture's center.</summary>
	[Export(PropertyHint.Range, "1,6,0.1")] public float DoorwayOffset { get; set; } = 3.4f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PairChance { get; set; } = .6f;
	/// <summary>Minimum distance between fixtures placed by this step.</summary>
	[Export(PropertyHint.Range, "0,12,0.1")] public float Spacing { get; set; } = 3.5f;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var candidates = BesideDoorways ? DoorwayCandidates(room, rng) : WallCandidates(room, rng);
		var placed = new List<Vector3>();
		int count = 0;
		var plan = Next(room, rng, out var choice);
		foreach (var (run, center) in candidates)
		{
			if (count >= MaximumCount || plan.IsEmpty) break;
			var point = run.LineStart + run.Tangent * center;
			if (placed.Any(other => other.DistanceTo(point) < Spacing)) continue;
			float width = room.WidthAlong(plan, run);
			if (!TryPlaceAgainstWall(room, choice, plan, run, center - width / 2)) continue;
			placed.Add(point);
			count++;
			plan = Next(room, rng, out choice);
		}
		return count;
	}

	/// <summary>Points on the walls either side of each doorway; the second side of a doorway only by chance.</summary>
	private List<(WallRun Run, float Center)> DoorwayCandidates(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var result = new List<(WallRun, float)>();
		var doorways = room.Doorways.ToList();
		Shuffle(doorways, rng);
		foreach (var doorway in doorways)
		{
			bool pair = rng.Randf() < PairChance;
			float first = rng.Randf() < .5f ? -1 : 1;
			var door = RoomFootprint.TileCenter(doorway.Tile);
			var edge = door + new Vector3(doorway.Direction.X, 0, doorway.Direction.Y) * (Room.TileSize / 2f);
			foreach (float side in pair ? new[] { first, -first } : new[] { first })
			foreach (var run in room.WallRuns.Where(run => run.Outward == doorway.Direction && Mathf.Abs((run.LineStart - edge).Dot(run.Inward)) < .01f))
			{
				float center = (door - run.LineStart).Dot(run.Tangent) + side * DoorwayOffset;
				if (center > 0 && center < run.LengthUnits) result.Add((run, center));
			}
		}
		return result;
	}

	/// <summary>The middle of every wall module, shuffled.</summary>
	private static List<(WallRun Run, float Center)> WallCandidates(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var result = room.WallRuns.SelectMany(run => Enumerable.Range(0, run.Length).Select(index => (run, (index + .5f) * Room.TileSize))).ToList();
		Shuffle(result, rng);
		return result;
	}
}
