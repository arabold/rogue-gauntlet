using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Tucks content into the room's corners: crates, corner shelves, barrels, or diagonal corner webs.</summary>
[Tool, GlobalClass]
public partial class CornersStep : FurnishingStep
{
	/// <summary>Distance kept from the perpendicular wall's face, however far that wall (or its corner piece) reaches.</summary>
	[Export(PropertyHint.Range, "0,2,0.05")] public float Clearance { get; set; } = .05f;

	/// <summary>When true, places diagonal corner-spanning content (such as 3D corner webs) right at the corner vertex, facing into the room along the corner bisector.</summary>
	[Export] public bool Diagonal { get; set; }

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
			if (Diagonal)
			{
				if (!TryPlaceDiagonal(room, choice, plan, run, atStart)) continue;
			}
			else
			{
				float width = room.WidthAlong(plan, run);
				float clearance = Clearance + room.EndDepth(run, atStart);
				if (width + clearance > run.LengthUnits) continue;
				float along = atStart ? clearance : run.LengthUnits - clearance - width;
				if (!TryPlaceAgainstWall(room, choice, plan, run, along)) continue;
			}
			count++;
			plan = Next(room, rng, out choice);
		}
		return count;
	}

	private static bool TryPlaceDiagonal(RoomBlueprint room, Placeable choice, PlacementPlan plan, WallRun run, bool atStart)
	{
		Vector2I cornerTile = atStart ? run.Start : run.Last;
		Vector2I sideOutward = atStart ? -run.Along : run.Along;
		if (room.IsDoorway(cornerTile, sideOutward) || room.IsDoorway(cornerTile, run.Outward)) return false;

		Vector3 inwardPerp = atStart ? run.Tangent : -run.Tangent;
		Vector3 bisector = (run.Inward + inwardPerp).Normalized();
		Basis facing = new(Vector3.Up, Mathf.Atan2(bisector.X, bisector.Z));

		var anchor = plan.Pieces[0].Prop;
		float inset = anchor.Mount == PropMount.Wall ? anchor.WallInset : 0f;
		float face = room.RunDepth(run) - inset;
		float side = room.EndDepth(run, atStart) - inset;
		Vector3 corner = run.LineStart + run.Inward * face + (atStart ? run.Tangent * side : run.Tangent * (run.LengthUnits - side));
		if (anchor.Mount == PropMount.Wall) corner.Y = anchor.MountHeight;

		Aabb bounds = plan.Bounds();
		Vector3 pos = corner;

		// Apex corner props (e.g. web_corner_*) have their apex at (0, 0) and extend in +Z.
		// Flat wall props (e.g. web_wall_*) have their origin centered at (0, 0) and thin depth in Z.
		// Bridging a 90-degree corner with a flat piece of width W requires placing its center at
		// distance W / 2 along the corner bisector, so its ends touch the two walls at W / sqrt(2).
		if (bounds.Size.Z < bounds.Size.X * 0.25f)
		{
			float reach = bounds.Size.X / Mathf.Sqrt(2f);
			if (run.LengthUnits < reach) return false;

			float bridgeOffset = bounds.Size.X * 0.5f;
			pos += bisector * bridgeOffset;
		}

		// Keep diagonal corner webs clear of doorway openings and pathways
		Vector2 posXZ = RoomBlueprint.XZ(pos);
		foreach (var doorway in room.Doorways)
		{
			if ((posXZ - RoomBlueprint.XZ(doorway.Edge)).Length() < 2.5f) return false;
		}

		var at = new Transform3D(facing, pos);
		var area = RoomBlueprint.XZ(at * bounds);
		if (room.Reserved.Any(reserved => reserved.Intersects(area))) return false;

		return TryPlace(room, choice, plan, at);
	}
}
