using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Lays connected mining rail runs along 1-tile-wide corridors and into dead-end shafts using
/// straight, 90-degree curved, and buffer-stop end pieces. Pre-validates every tile before committing a run.
/// </summary>
[Tool, GlobalClass]
public partial class CorridorRailStep : CorridorStep
{
	[Export] public Prop Straight { get; set; }
	[Export] public Prop Curve { get; set; }
	[Export] public Prop End { get; set; }
	/// <summary>Optional mine cart parked on dead-end rail spurs in front of the buffer stop.</summary>
	[Export] public Prop Cart { get; set; }
	[Export(PropertyHint.Range, "0,1,0.05")] public float CartChance { get; set; } = 0.65f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CartElevation { get; set; } = 0.135f;
	[Export(PropertyHint.Range, "2,16")] public int MinimumRunTiles { get; set; } = 3;
	[Export(PropertyHint.Range, "2,24")] public int MaximumRunTiles { get; set; } = 10;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PerRunChance { get; set; } = 0.6f;

	private const float DeadEndCartOffset = 0.55f;

	private readonly record struct PlannedRail(Vector2I Tile, Prop Prop, Basis Facing, Vector2I? DeadEndOpenDir);

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add($"{GetType().Name}: assign a stable ID.");
		if (Straight == null || Curve == null || End == null)
			errors.Add($"Corridor rail step {Id}: assign Straight, Curve and End props.");
		if (MinimumRunTiles > MaximumRunTiles)
			errors.Add($"Corridor rail step {Id}: MinimumRunTiles exceeds MaximumRunTiles.");
		foreach (var prop in new[] { Straight, Curve, End, Cart })
		{
			if (prop != null) errors.AddRange(prop.Validate());
		}
		return errors.ToArray();
	}

	public override void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng)
	{
		if (Straight == null || Curve == null || End == null) return;

		var claimed = new HashSet<Vector2I>();
		var planned = new List<PlannedRail>();
		int placedRuns = 0;

		var deadEnds = corridors.DeadEnds.ToList();
		Shuffle(deadEnds, rng);
		foreach (var deadEnd in deadEnds)
		{
			if (placedRuns >= MaximumCount) break;
			if (claimed.Contains(deadEnd.Tile) || rng.Randf() >= PerRunChance) continue;
			if (!TryPlanRun(corridors, deadEnd.Tile, deadEnd.OpenDirection, startIsDeadEnd: true, claimed, planned)) continue;
			CommitRun(corridors, planned, claimed, rng);
			placedRuns++;
		}

		if (placedRuns >= MaximumCount) return;

		var spans = corridors.Spans.Where(span => span.Length >= MinimumRunTiles).ToList();
		Shuffle(spans, rng);
		foreach (var span in spans)
		{
			if (placedRuns >= MaximumCount) break;
			if (claimed.Contains(span.Start) || rng.Randf() >= PerRunChance) continue;
			if (!TryPlanRun(corridors, span.Start, span.Along, startIsDeadEnd: false, claimed, planned)) continue;
			CommitRun(corridors, planned, claimed, rng);
			placedRuns++;
		}
	}

	private bool TryPlanRun(
		CorridorBlueprint corridors,
		Vector2I startTile,
		Vector2I initialStep,
		bool startIsDeadEnd,
		HashSet<Vector2I> claimed,
		List<PlannedRail> planned)
	{
		planned.Clear();
		Span<Vector2I> open = stackalloc Vector2I[4];

		if (startIsDeadEnd)
		{
			planned.Add(new(startTile, End, StraightFacing(initialStep), initialStep));
		}
		else
		{
			if (corridors.Map.IsConnector(startTile.X, startTile.Y) || corridors.OpenDirections(startTile, open) != 2)
				return false;
			var (prop, facing) = PieceForTwoWayTile(open[0], open[1]);
			planned.Add(new(startTile, prop, facing, null));
		}

		var stepDir = initialStep;
		var cur = startTile + initialStep;
		while (planned.Count < MaximumRunTiles)
		{
			if (claimed.Contains(cur) || !corridors.Map.IsCorridor(cur.X, cur.Y) || corridors.Map.IsConnector(cur.X, cur.Y))
				break;
			int count = corridors.OpenDirections(cur, open);
			if (count == 1 && startIsDeadEnd && open[0] == -stepDir)
			{
				planned.Add(new(cur, End, StraightFacing(-stepDir), -stepDir));
				break;
			}
			if (count == 2)
			{
				var (prop, facing) = PieceForTwoWayTile(open[0], open[1]);
				planned.Add(new(cur, prop, facing, null));
				stepDir = open[0] == -stepDir ? open[1] : open[0];
				cur += stepDir;
			}
			else if (count == 3 && HasDirection(open, count, stepDir))
			{
				planned.Add(new(cur, Straight, StraightFacing(stepDir), null));
				cur += stepDir;
			}
			else
			{
				break;
			}
		}

		if (planned.Count < MinimumRunTiles) return false;
		foreach (var rail in planned)
		{
			if (!corridors.CanPlaceFloorTile(rail.Prop, rail.Tile, rail.Facing)) return false;
		}
		return true;
	}

	private void CommitRun(
		CorridorBlueprint corridors,
		List<PlannedRail> planned,
		HashSet<Vector2I> claimed,
		RandomNumberGenerator rng)
	{
		foreach (var rail in planned)
		{
			if (corridors.TryPlaceFloorTile(rail.Prop, rail.Tile, rail.Facing, rng)) claimed.Add(rail.Tile);
		}
		if (Cart == null) return;
		foreach (var rail in planned)
		{
			if (rail.DeadEndOpenDir is not Vector2I openDir) continue;
			if (rng.Randf() >= CartChance) continue;
			var offset = new Vector3(openDir.X * DeadEndCartOffset, CartElevation, openDir.Y * DeadEndCartOffset);
			corridors.TryPlaceOnFloorInlay(Cart, rail.Tile, offset, rail.Facing, rng, underlay: End);
		}
	}

	private (Prop Prop, Basis Facing) PieceForTwoWayTile(Vector2I a, Vector2I b) =>
		a + b == Vector2I.Zero
			? (Straight, StraightFacing(a))
			: (Curve, CurveFacing(a, b));

	private static bool HasDirection(ReadOnlySpan<Vector2I> dirs, int count, Vector2I target)
	{
		for (int i = 0; i < count; i++)
		{
			if (dirs[i] == target) return true;
		}
		return false;
	}

	private static Basis StraightFacing(Vector2I forward) =>
		new(Vector3.Up, Mathf.Atan2(forward.X, forward.Y));

	// Curve model joins +Z (Down) and +X (Right) at rotation 0.
	private static Basis CurveFacing(Vector2I a, Vector2I b) =>
		new(Vector3.Up, Mathf.Atan2(a.X + b.X, a.Y + b.Y) - Mathf.Pi * 0.25f);
}
