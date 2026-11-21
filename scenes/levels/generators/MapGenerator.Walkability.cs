using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Floor-level reachability: one actor-sized walkability grid over the finished geometry, flood-filled from the
/// arrival with every door open. Inaccessible pockets (behind furniture, inside enclosures) are allowed to exist;
/// this is what keeps spawns, loot and drops out of them, whatever placed the geometry.
/// </summary>
public partial class MapGenerator
{
	/// <summary>Builds the grid once rooms, corridors, walls and doors exist, before encounters and loot.</summary>
	private void BuildWalkability()
	{
		if (Floor.Arrival == null) return;
		var grid = CreateWalkabilityGrid();
		Floor.SetWalkability(grid, grid.Reachable([ArrivalPoint()], Room.TileSize));
	}

	private Vector2 ArrivalPoint()
	{
		// The arrival spawns on the stairs, which block walking; the landing in front is where play starts.
		var at = Floor.Arrival.LandingPosition;
		return new(at.X, at.Z);
	}

	private WalkabilityGrid CreateWalkabilityGrid()
	{
		var grid = new WalkabilityGrid(Map.WorldBounds, RoomAssembler.ActorRadius);
		foreach (var (area, walkable) in TileAreas())
		{
			if (walkable) grid.AddFloor(area);
			else grid.AddObstacle(area);
		}
		foreach (var outline in SolidOutlines()) grid.AddObstacle(outline);
		return grid;
	}

	/// <summary>
	/// Every walkable tile's area, and every unwalkable one beside it. Unwalkable tiles farther out lie a whole tile from
	/// any floor, beyond the room either grid measures (<see cref="ClearanceGrid.DefaultMaxClearance"/> is under a tile).
	/// </summary>
	private IEnumerable<(Rect2 Area, bool Walkable)> TileAreas()
	{
		for (int x = 0; x < Map.Width; x++)
		for (int z = 0; z < Map.Height; z++)
		{
			bool walkable = Map.IsWalkable(x, z);
			if (!walkable && !BordersWalkable(x, z)) continue;
			yield return (RoomFootprint.TileArea(Map.TileToWorld(x, z)), walkable);
		}
	}

	private bool BordersWalkable(int x, int z)
	{
		for (int dx = -1; dx <= 1; dx++)
		for (int dz = -1; dz <= 1; dz++)
			if (Map.IsWithinBounds(x + dx, z + dz) && Map.IsWalkable(x + dx, z + dz)) return true;
		return false;
	}

	/// <summary>
	/// Real collision, not tile classes: doorway openings stay passable and wall depth varies per piece. Doors count with
	/// their frame and their leaf standing open.
	/// </summary>
	private SolidOutline[] _cachedStaticOutlines;

	private IReadOnlyList<SolidOutline> StaticOutlines() => _cachedStaticOutlines ??=
	[
		.. CollisionOutlines.FromGridMap(WallGridMap, Transform3D.Identity),
		.. CollisionOutlines.FromGridMap(DecorationGridMap, Transform3D.Identity),
	];

	private IEnumerable<SolidOutline> SolidOutlines()
	{
		var toLocal = GlobalTransform.AffineInverse();
		foreach (var outline in StaticOutlines()) yield return outline;
		foreach (var outline in CollisionOutlines.FromStaticBodies(FloorContent, toLocal * FloorContent.GlobalTransform)) yield return outline;
	}

	/// <summary>Physical reachability with inaccessible doorway tiles blocked, without changing the floor grid.</summary>
	private bool[] ReachableWithoutConnectors(IEnumerable<Vector2I> blocked)
	{
		var grid = Floor.Walkability;
		if (grid == null) return null;
		var snapshots = new List<(Rect2 Area, ClearanceGrid.RegionSnapshot Snapshot)>();
		foreach (var tile in blocked)
		{
			var area = RoomFootprint.TileArea(Map.TileToWorld(tile));
			snapshots.Add((area, grid.SaveRegion(area)));
		}
		try
		{
			foreach (var (area, _) in snapshots) grid.AddObstacle(area);
			return grid.Reachable([ArrivalPoint()], Room.TileSize);
		}
		finally
		{
			for (int i = snapshots.Count - 1; i >= 0; i--) grid.RestoreRegion(snapshots[i].Snapshot);
		}
	}

	/// <summary>
	/// Adds solid geometry to the floor's walkability only when it creates no new inaccessible pocket: every cell
	/// reachable before is still reachable or lies under the new geometry itself. Leaves the floor unchanged on false.
	/// </summary>
	private bool TryBlockReachably(IReadOnlyList<SolidOutline> solids)
	{
		var grid = Floor.Walkability;
		if (grid == null || solids.Count == 0) return true;
		var union = solids[0].Bounds;
		for (int i = 1; i < solids.Count; i++) union = union.Merge(solids[i].Bounds);
		var area = new Rect2(union.Position.X, union.Position.Z, union.Size.X, union.Size.Z);

		var before = Floor.Reachable;
		var saved = grid.SaveRegion(area);
		foreach (var solid in solids) grid.AddObstacle(solid);
		var after = grid.Reachable([ArrivalPoint()], Room.TileSize);
		for (int index = 0; index < before.Length; index++)
		{
			if (!before[index] || after[index]) continue;
			if (grid.IsStandable(new Vector2I(index % grid.Size.X, index / grid.Size.X)))
			{
				grid.RestoreRegion(saved);
				return false;
			}
		}
		if (!after.Any(reached => reached))
		{
			grid.RestoreRegion(saved);
			return false;
		}
		Floor.SetWalkability(grid, after);
		return true;
	}
}