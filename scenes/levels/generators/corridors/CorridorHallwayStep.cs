using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Places wall decorations or periodic lights along straight corridor <see cref="WallRun"/>s of at least
/// <see cref="MinimumRunTiles"/> tiles, optionally pairing symmetric props across the hall.
/// </summary>
[Tool, GlobalClass]
public partial class CorridorHallwayStep : CorridorStep
{
	[Export(PropertyHint.Range, "1,12")] public int MinimumRunTiles { get; set; } = 3;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PairAcrossChance { get; set; } = .3f;

	public override void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng)
	{
		// One candidate per wall module, or per bay where the module's posts divide it.
		var candidates = new List<(WallRun Run, int TileIndex, float CenterAlong)>();
		foreach (var run in corridors.WallRuns.Where(run => run.Length >= MinimumRunTiles))
		{
			for (int index = 0; index < run.Length; index++)
			{
				foreach (float center in run.MountCenters(index, corridors.EdgeMounts))
					candidates.Add((run, index, center));
			}
		}

		Shuffle(candidates, rng);
		var placed = new List<Vector2>();
		float spacingSq = Spacing * Spacing;
		int count = 0;

		foreach (var (run, index, centerAlong) in candidates)
		{
			if (count >= MaximumCount) break;
			var tile = run.TileAt(index);
			if (!corridors.IsDecoratable(tile, run.Outward)) continue;

			var point = run.LineStart + run.Tangent * centerAlong;
			var pointXZ = RoomBlueprint.XZ(point);
			if (placed.Any(other => other.DistanceSquaredTo(pointXZ) < spacingSq)) continue;

			var prop = NextProp(corridors, rng);
			if (!corridors.TryPlaceAgainstWall(prop, run, centerAlong, rng, minLightSpacing: Spacing)) continue;

			placed.Add(pointXZ);
			count++;

			if (count < MaximumCount && rng.Randf() < PairAcrossChance
				&& corridors.TryPlaceOnTileWall(prop, tile, -run.Outward, rng, minLightSpacing: 0f))
			{
				count++;
			}
		}
	}
}