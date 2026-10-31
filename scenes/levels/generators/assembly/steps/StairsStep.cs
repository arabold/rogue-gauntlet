using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The level's stairs. Up-stairs stand with their top against a wall, centered on a tile. Down-stairs sink into a
/// two-tile stairwell cut into open floor, with floor on every side so the level maps the hole as an enclosed pit.
/// Either way the landing in front of the walk-on end (the up-stairs' foot, the down-stairs' top) is claimed for the
/// rest of the assembly: no floor prop may cover it and every doorway keeps reaching it. Content follows the prop
/// convention, walk-on end toward +Z; sunken content is centered on its origin.
/// </summary>
[Tool, GlobalClass]
public partial class StairsStep : FurnishingStep
{
	private static readonly Vector2I[] Entries = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];
	/// <summary>Depth of the landing kept clear past the walk-on end, as wide as the stairs.</summary>
	private const float LandingDepth = 2.5f;

	/// <summary>Down-stairs: the content sinks into a stairwell carved from the floor under it.</summary>
	[Export] public bool Sunken { get; set; }

	public StairsStep()
	{
		MinimumCount = 1;
		MaximumCount = 1;
	}

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var plan = Next(room, rng, out var choice);
		if (plan.IsEmpty) return 0;
		var candidates = (Sunken ? Stairwells(room) : AgainstWalls(room, plan)).ToList();
		Shuffle(candidates, rng);
		var bounds = plan.Bounds(includeOptional: false);
		var landingBox = new Aabb(new Vector3(bounds.Position.X, 0, bounds.End.Z), new Vector3(bounds.Size.X, 1, LandingDepth));
		foreach (var (at, pit) in candidates)
		{
			if (!room.TryPlace(plan, at, pit, RoomBlueprint.XZ(at * landingBox))) continue;
			room.RecordUse(choice);
			return 1;
		}
		return 0;
	}

	/// <summary>Every wall run, at each tile the stairs can center on.</summary>
	private static IEnumerable<(Transform3D, Vector2I[])> AgainstWalls(RoomBlueprint room, PlacementPlan plan)
	{
		foreach (var run in room.WallRuns)
		{
			float width = room.WidthAlong(plan, run);
			for (int index = 0; index < run.Length; index++)
			{
				float along = (index + .5f) * Room.TileSize - width / 2;
				if (along >= 0 && along + width <= run.LengthUnits) yield return (room.AgainstWall(plan, run, along), null);
			}
		}
	}

	/// <summary>Every pair of adjacent tiles whose four neighbours are all floor, entered from either end.</summary>
	private static IEnumerable<(Transform3D, Vector2I[])> Stairwells(RoomBlueprint room)
	{
		foreach (var tile in room.Footprint.Tiles)
		foreach (var entry in Entries)
		{
			// The stairs descend away from the entry, so the second tile lies behind the first.
			Vector2I[] pit = [tile, tile - entry];
			if (!pit.All(cell => Enclosed(room, cell, pit))) continue;
			var center = (RoomFootprint.TileCenter(pit[0]) + RoomFootprint.TileCenter(pit[1])) / 2;
			yield return (new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(entry.X, entry.Y)), center), pit);
		}
	}

	private static bool Enclosed(RoomBlueprint room, Vector2I cell, Vector2I[] pit) =>
		room.IsFloor(cell) && MapData.CardinalOffsets.All(offset => pit.Contains(cell + offset) || room.IsFloor(cell + offset));
}
