using System.Linq;
using Godot;

[System.Flags]
public enum WallSides { Long = 1, Short = 2, Any = Long | Short }

/// <summary>
/// Lines wall runs with content, packed from one end of each run: beds, shelves, supply stations, workbenches.
/// Long sides run along the room's axis.
/// </summary>
[Tool, GlobalClass]
public partial class AlongWallsStep : FurnishingStep
{
	[Export] public WallSides Sides { get; set; } = WallSides.Any;
	/// <summary>Skips wall sides that hold a doorway.</summary>
	[Export] public bool AvoidDoorwayWalls { get; set; }
	[Export(PropertyHint.Range, "0,4,0.05")] public float Spacing { get; set; } = .3f;
	/// <summary>Distance kept from the run's ends (the corner wall line, or a doorway's edge).</summary>
	[Export(PropertyHint.Range, "0,4,0.05")] public float EndClearance { get; set; } = .6f;
	/// <summary>One piece centered on each run instead of packing from an end: altars, focal furniture.</summary>
	[Export] public bool Centered { get; set; }

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var runs = room.WallRuns.Where(run => Sides.HasFlag(run.Outward.X != 0 ? WallSides.Long : WallSides.Short)
			&& !(AvoidDoorwayWalls && room.Doorways.Any(doorway => doorway.Direction == run.Outward))).ToList();
		Shuffle(runs, rng);
		int count = 0;
		var plan = Next(room, rng, out var choice);
		foreach (var run in runs)
		{
			if (Centered)
			{
				if (count >= MaximumCount || plan.IsEmpty) break;
				if (!TryPlaceAgainstWall(room, choice, plan, run, (run.LengthUnits - room.WidthAlong(plan, run)) / 2)) continue;
				count++;
				plan = Next(room, rng, out choice);
				continue;
			}
			bool fromEnd = rng.Randf() < .5f;
			float offset = EndClearance;
			while (count < MaximumCount && !plan.IsEmpty)
			{
				float width = room.WidthAlong(plan, run);
				if (offset + width > run.LengthUnits - EndClearance) break;
				float along = fromEnd ? run.LengthUnits - offset - width : offset;
				if (TryPlaceAgainstWall(room, choice, plan, run, along))
				{
					count++;
					offset += width + Spacing;
					plan = Next(room, rng, out choice);
				}
				else
				{
					// Trim, pillars and doorway approaches interrupt runs; slide on rather than giving up the wall.
					offset += .5f;
				}
			}
			if (count >= MaximumCount) break;
		}
		return count;
	}
}
