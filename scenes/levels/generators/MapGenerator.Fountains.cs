using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Generation-time fountains: the rare health and mana fountains a run's schedule assigns to this depth.</summary>
public partial class MapGenerator
{
	/// <summary>Which fountains the run holds and on which depths. Unset places none.</summary>
	[ExportGroup("Fountains")]
	[Export] public FountainProfile Fountains { get; set; }

	/// <summary>The seed of the run this floor belongs to; fountains are scheduled across the run, not per floor.</summary>
	[Export] public ulong RunSeed { get; set; } = RunState.DefaultSeed;

	/// <summary>
	/// Places each fountain scheduled for this depth as a free-standing centerpiece of a room, preferring
	/// tiles with no wall beside them. Fountains are solid props like chests and claim their spots before any loot.
	/// Each kind has its own stream, so a fountain floor leaves the shared item stream's draws unchanged.
	/// </summary>
	private void PlaceFountains(List<Vector3> placedPoints)
	{
		if (Fountains == null || NavigationRegion == null) return;
		foreach (var kind in Fountains.KindsAt(RunSeed, DungeonDepth))
		{
			if (kind.Variants.Count == 0) continue;
			using var rng = GenerationRandom.Create(Seed, GenerationRandom.Fountains, kind.Id);
			var scene = kind.Variants[rng.RandiRange(0, kind.Variants.Count - 1)];
			var tiles = GetSolidPropTiles();
			var remaining = tiles.Where(tile => tile.Walls.Count == 0).Select(tile => tile.Point).ToList();
			if (remaining.Count == 0) remaining = tiles.Select(tile => tile.Point).ToList();
			if (remaining.Count == 0)
			{
				GD.PrintErr($"Cannot place the {kind.Id} fountain: no free room tiles found.");
				continue;
			}

			bool placed = false;
			while (!placed && remaining.Count > 0)
			{
				int index = rng.RandiRange(0, remaining.Count - 1);
				var point = remaining[index];
				remaining.RemoveAt(index);
				placed = !IsBlockedLootPoint(point, placedPoints, LootSpawnSpacing)
					&& TryPlaceSolidProp(scene, point, 90 * rng.RandiRange(0, 3), $"depth:{DungeonDepth}:fountain:{kind.Id}");
				if (placed) placedPoints.Add(point);
			}

			// A cramped floor is a normal outcome; the run simply holds one fewer fountain.
			if (!placed) GD.Print($"Skipping the {kind.Id} fountain: no room tile fits it.");
		}
	}
}