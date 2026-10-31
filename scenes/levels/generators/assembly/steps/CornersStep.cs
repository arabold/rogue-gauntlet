using System.Collections.Generic;
using Godot;

/// <summary>Tucks content into the room's corners: crates, corner shelves, barrels.</summary>
[Tool, GlobalClass]
public partial class CornersStep : FurnishingStep
{
	/// <summary>Distance kept from the perpendicular wall line.</summary>
	[Export(PropertyHint.Range, "0,2,0.05")] public float Clearance { get; set; } = .55f;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var corners = new List<(WallRun Run, bool AtStart)>();
		foreach (var run in room.WallRuns)
		{
			if (room.StartsAtCorner(run)) corners.Add((run, true));
			if (room.EndsAtCorner(run)) corners.Add((run, false));
		}
		Shuffle(corners, rng);
		int count = 0;
		var plan = Next(room, rng, out var choice);
		foreach (var (run, atStart) in corners)
		{
			if (count >= MaximumCount || plan.IsEmpty) break;
			float width = room.WidthAlong(plan, run);
			if (width + Clearance > run.LengthUnits) continue;
			float along = atStart ? Clearance : run.LengthUnits - Clearance - width;
			if (!TryPlaceAgainstWall(room, choice, plan, run, along)) continue;
			count++;
			plan = Next(room, rng, out choice);
		}
		return count;
	}
}
