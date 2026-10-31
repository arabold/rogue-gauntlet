using System.Collections.Generic;
using Godot;

/// <summary>Generation-time fountains: the rare health and mana fountains a run's schedule assigns to this depth.</summary>
public partial class MapGenerator
{
	/// <summary>Which fountains the run holds and on which depths. Unset places none.</summary>
	[ExportGroup("Fountains")]
	[Export] public FountainProfile Fountains { get; set; }

	/// <summary>The seed of the run this floor belongs to; fountains are scheduled across the run, not per floor.</summary>
	[Export] public ulong RunSeed { get; set; } = RunState.DefaultSeed;

	private readonly List<Vector3> _fountainPoints = [];

	/// <summary>
	/// Places each fountain scheduled for this depth as a freestanding centerpiece of a procedural room, preferring tiles
	/// walkable on every side. Fountains are solid, so like chests they sit under <see cref="NavigationRegion"/> before
	/// the bake and must keep the room's free space connected. Each kind has its own stream, so a fountain floor leaves
	/// the shared item stream's draws unchanged.
	/// </summary>
	private void PlaceFountains()
	{
		if (Fountains == null || NavigationRegion == null) return;
		foreach (var kind in Fountains.KindsAt(RunSeed, DungeonDepth))
		{
			if (kind.Variants.Count == 0) continue;
			using var rng = GenerationRandom.Create(Seed, GenerationRandom.Fountains, kind.Id);
			var scene = kind.Variants[rng.RandiRange(0, kind.Variants.Count - 1)];
			var remaining = GetFountainCandidates();
			bool placed = false;
			while (!placed && remaining.Count > 0)
			{
				int index = rng.RandiRange(0, remaining.Count - 1);
				var point = remaining[index];
				remaining.RemoveAt(index);
				if (IsBlockedLootPoint(point, [.. _accessPickupPoints, .. _fountainPoints], LootSpawnSpacing)) continue;

				var fountain = scene.Instantiate<Node3D>();
				float rotation = 90 * rng.RandiRange(0, 3);
				if (!PreservesRoomPassage(fountain, point, rotation))
				{
					fountain.Free();
					continue;
				}

				fountain.SetMeta("persistent_id", $"depth:{DungeonDepth}:fountain:{kind.Id}");
				NavigationRegion.AddChild(fountain);
				fountain.GlobalPosition = point + Vector3.Up * GetFloorSurfaceHeight(point);
				fountain.RotationDegrees = new Vector3(0, rotation, 0);
				_fountainPoints.Add(point);
				placed = true;
			}

			if (!placed) ReportNoLootCandidates($"the {kind.Id} fountain", "no procedural room tile fits it");
		}
	}

	/// <summary>Unoccupied procedural room tiles away from doorways; interior tiles with walkable floor on every side
	/// when any exist, so a fountain stands free rather than against a wall.</summary>
	private List<Vector3> GetFountainCandidates()
	{
		var interior = new List<Vector3>();
		var other = new List<Vector3>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (!Map.IsRoom(x, z) || !Floor.IsProceduralRoomTile(x, z) || Floor.IsOccupied(x, z) || IsAdjacentToConnector(x, z))
					continue;

				bool surrounded = true;
				foreach (var offset in MapData.CardinalOffsets)
				{
					int nx = x + offset.X, nz = z + offset.Y;
					surrounded &= Map.IsWithinBounds(nx, nz) && Map.IsRoom(nx, nz);
				}
				(surrounded ? interior : other).Add(Map.TileToWorld(x, z));
			}
		}
		return interior.Count > 0 ? interior : other;
	}
}