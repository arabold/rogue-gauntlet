using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The level's stairs. Up-stairs stand with their top against a wall, centered on a tile. Sunken down-stairs
/// (<see cref="Prop.Sunken"/>) cover two adjacent floor tiles, entered from either end; the blueprint cuts their pit
/// and refuses spots without floor on every side. Stairs props are interactive, so the landing at the walk-on end
/// stays clear and reachable. Content follows the prop convention, walk-on end toward +Z; sunken content is centered
/// on its origin.
/// </summary>
[Tool, GlobalClass]
public partial class StairsStep : FurnishingStep
{
	public StairsStep()
	{
		MinimumCount = 1;
		MaximumCount = 1;
	}

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var plan = Next(room, rng, out var choice);
		if (plan.IsEmpty) return 0;
		var candidates = (plan.Pieces[0].Prop.Sunken ? Stairwells(room) : AgainstWalls(room, plan)).ToList();
		Shuffle(candidates, rng);
		foreach (var at in candidates)
			if (TryPlace(room, choice, plan, at)) return 1;
		return 0;
	}

	/// <summary>Every wall run, centered on each tile the stairs fit along.</summary>
	private static IEnumerable<Transform3D> AgainstWalls(RoomBlueprint room, PlacementPlan plan)
	{
		foreach (var (run, center) in ModuleCenters(room))
		{
			float width = room.WidthAlong(plan, run), along = center - width / 2;
			if (along >= 0 && along + width <= run.LengthUnits) yield return room.AgainstWall(plan, run, along);
		}
	}

	/// <summary>Every pair of adjacent floor tiles, entered from either end.</summary>
	private static IEnumerable<Transform3D> Stairwells(RoomBlueprint room)
	{
		foreach (var tile in room.Footprint.Tiles)
		foreach (var entry in MapData.CardinalOffsets)
		{
			// The stairs descend away from the entry, over this tile and the one behind it.
			if (!room.Footprint.IsFloor(tile - entry)) continue;
			var center = (RoomFootprint.TileCenter(tile) + RoomFootprint.TileCenter(tile - entry)) / 2;
			yield return new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(entry.X, entry.Y)), center);
		}
	}
}
