using System.Collections.Generic;
using System.Linq;
using Godot;

public enum FencedAreaMode { WallBay, CornerBay, Walkway, Divider }

/// <summary>
/// Low fences or partitions from modular pieces: a bay against a wall or in a corner with one entrance (filled with
/// <see cref="FurnishingStep.Content"/> along its back wall), a walkway fenced along one or both sides of the room's
/// axis, or a short divider off a wall. Counts are content pieces placed inside bays. When <see cref="RequireFence"/>
/// is set and no fence fits, the attempt fails and the room retries with a new shape.
/// </summary>
[Tool, GlobalClass]
public partial class FencedAreaStep : FurnishingStep
{
	[Export] public FencedAreaMode Mode { get; set; }
	/// <summary>The fence defines the room: failing to place it fails the attempt.</summary>
	[Export] public bool RequireFence { get; set; } = true;
	/// <summary>Full-length pieces, weighted; they run along local X.</summary>
	[Export] public Prop[] Segments { get; set; } = [];
	/// <summary>Half-length piece that closes runs which are not a multiple of the segment length.</summary>
	[Export] public Prop Half { get; set; }
	/// <summary>L piece with legs along -X and +Z from its origin; bays need it.</summary>
	[Export] public Prop Corner { get; set; }
	/// <summary>Bay width along the wall (X) and depth into the room (Y).</summary>
	[Export] public Vector2 BaySize { get; set; } = new(8, 6);
	[Export(PropertyHint.Range, "2.5,8,0.1")] public float EntranceWidth { get; set; } = 4;
	[Export(PropertyHint.Range, "2.5,8,0.1")] public float WalkwayWidth { get; set; } = 4;
	/// <summary>Free floor kept at both ends of a walkway's fences.</summary>
	[Export(PropertyHint.Range, "2,8,0.1")] public float WalkwayEndInset { get; set; } = 3;
	[Export(PropertyHint.Range, "2,12,0.5")] public float DividerLength { get; set; } = 4;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		int placed = Mode switch
		{
			FencedAreaMode.Walkway => PlaceWalkway(room, rng) ? 0 : -1,
			FencedAreaMode.Divider => PlaceDivider(room, rng) ? 0 : -1,
			_ => PlaceBay(room, rng),
		};
		// A negative count fails even an optional minimum; only a required fence may do that.
		return placed < 0 && !RequireFence ? 0 : placed;
	}

	private int PlaceBay(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var candidates = new List<(WallRun Run, float X)>();
		foreach (var run in room.WallRuns.Where(run => run.LengthUnits >= BaySize.X + (Mode == FencedAreaMode.WallBay ? 4 : 0)))
		{
			float half = run.LengthUnits / 2;
			if (Mode == FencedAreaMode.WallBay) candidates.Add((run, -BaySize.X / 2));
			else
			{
				// Corner bays use the room's perpendicular wall (from its face) as their second side; local X may run either way.
				var frame = room.RunFrame(run);
				float startX = (run.LineStart - frame.Origin).Dot(frame.Basis.X);
				float near = -half + .5f, far = half - .5f - BaySize.X;
				if (room.StartsAtCorner(run)) candidates.Add((run, startX < 0 ? near : far));
				if (room.EndsAtCorner(run)) candidates.Add((run, startX < 0 ? far : near));
			}
		}
		Shuffle(candidates, rng);
		foreach (var (run, x) in candidates)
		{
			var frame = room.RunFrame(run);
			bool leftWall = Mode == FencedAreaMode.CornerBay && x <= -run.LengthUnits / 2 + .51f;
			bool rightWall = Mode == FencedAreaMode.CornerBay && !leftWall;
			var plan = new PlacementPlan();
			if (!BayPlan(plan, x, leftWall, rightWall, rng) || !room.TryPlace(plan, frame)) continue;
			return FillBay(room, run, frame, x, rng);
		}
		return -1;
	}

	/// <summary>Fence outline of a bay in the run frame: free sides, front corners and a centered entrance.</summary>
	private bool BayPlan(PlacementPlan plan, float x0, bool leftWall, bool rightWall, RandomNumberGenerator rng)
	{
		float x1 = x0 + BaySize.X, depth = BaySize.Y, leg = 2;
		float entranceLeft = x0 + (BaySize.X - EntranceWidth) / 2, entranceRight = entranceLeft + EntranceWidth;
		if (!leftWall)
		{
			if (!AddRun(plan, new(x0, 0, 0), new(x0, 0, depth - leg), rng)) return false;
			plan.Add(Corner, 0, new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(x0, 0, depth)), optional: false);
		}
		if (!rightWall)
		{
			if (!AddRun(plan, new(x1, 0, 0), new(x1, 0, depth - leg), rng)) return false;
			plan.Add(Corner, 0, new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), new Vector3(x1, 0, depth)), optional: false);
		}
		return AddRun(plan, new(leftWall ? x0 : x0 + leg, 0, depth), new(entranceLeft, 0, depth), rng)
			&& AddRun(plan, new(entranceRight, 0, depth), new(rightWall ? x1 : x1 - leg, 0, depth), rng);
	}

	private int FillBay(RoomBlueprint room, WallRun run, Transform3D frame, float x0, RandomNumberGenerator rng)
	{
		float a = RoomBlueprint.AlongRun(run, frame * new Vector3(x0, 0, 0));
		float b = RoomBlueprint.AlongRun(run, frame * new Vector3(x0 + BaySize.X, 0, 0));
		float start = Mathf.Min(a, b) + .6f, end = Mathf.Max(a, b) - .6f;
		int count = 0;
		var plan = Next(room, rng, out var choice);
		for (float along = start; count < MaximumCount && !plan.IsEmpty;)
		{
			float width = room.WidthAlong(plan, run);
			if (along + width > end) break;
			if (TryPlaceAgainstWall(room, choice, plan, run, along))
			{
				count++;
				along += width + .2f;
				plan = Next(room, rng, out choice);
			}
			else along += .5f;
		}
		return count;
	}

	private bool PlaceWalkway(RoomBlueprint room, RandomNumberGenerator rng)
	{
		int sides = rng.RandiRange(1, 3);
		var area = room.Interior;
		float center = area.GetCenter().X, z0 = area.Position.Y + .5f + WalkwayEndInset, z1 = area.End.Y - .5f - WalkwayEndInset;
		var plan = new PlacementPlan();
		foreach (int side in new[] { 1, 2 })
		{
			if ((sides & side) == 0) continue;
			float x = center + (side == 1 ? -1 : 1) * WalkwayWidth / 2;
			if (!AddRun(plan, new(x, 0, z0), new(x, 0, z1), rng, allowShort: true)) return false;
		}
		return room.TryPlace(plan, Transform3D.Identity);
	}

	private bool PlaceDivider(RoomBlueprint room, RandomNumberGenerator rng)
	{
		foreach (var run in room.WallRuns.OrderByDescending(run => run.Length).ThenBy(_ => rng.Randi()))
		{
			var plan = new PlacementPlan();
			if (AddRun(plan, Vector3.Zero, new Vector3(0, 0, DividerLength), rng, allowShort: true) && room.TryPlace(plan, room.RunFrame(run))) return true;
		}
		return false;
	}

	/// <summary>Axis-aligned run of segments, closed with a half piece. A run that does not fit the modules fails, or with
	/// <paramref name="allowShort"/> (open-ended walkways and dividers) simply stops short.</summary>
	private bool AddRun(PlacementPlan plan, Vector3 start, Vector3 end, RandomNumberGenerator rng, bool allowShort = false)
	{
		var delta = end - start;
		float length = delta.Length();
		if (length < .01f) return true;
		var direction = delta / length;
		var basis = new Basis(Vector3.Up, Mathf.Atan2(-direction.Z, direction.X));
		for (float offset = 0; offset < length - .01f;)
		{
			var segment = Weighted.Pick(Segments, entry => entry.Weight, rng);
			if (segment.Geometry(0).Envelope.Size.X > length - offset + .01f) segment = Half;
			if (segment == null || segment.Geometry(0).Envelope.Size.X > length - offset + .01f) return allowShort && offset > 0;
			// Pieces need not be centered on their origin: start each where its measured extent begins.
			var extent = segment.Geometry(0).Envelope;
			plan.Add(segment, 0, new Transform3D(basis, start + direction * (offset - extent.Position.X)), optional: false);
			offset += extent.Size.X;
		}
		return true;
	}

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("FencedAreaStep: assign a stable ID.");
		if (Segments.Length == 0 || Segments.Any(segment => segment == null)) errors.Add($"Step {Id}: assign fence segments.");
		if (Mode is FencedAreaMode.WallBay or FencedAreaMode.CornerBay && Corner == null) errors.Add($"Step {Id}: bays need a corner piece.");
		if (Mode is FencedAreaMode.WallBay or FencedAreaMode.CornerBay && Content.Any(entry => entry == null)) errors.Add($"Step {Id}: remove empty content entries.");
		errors.AddRange(Segments.Append(Half).Append(Corner).Where(prop => prop != null).SelectMany(prop => prop.Validate()));
		errors.AddRange(Content.Where(entry => entry != null).SelectMany(entry => entry.Validate()));
		return errors.ToArray();
	}
}
