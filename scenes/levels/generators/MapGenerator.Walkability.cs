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
		var at = (Floor.Arrival.Landing ?? Floor.Arrival).GlobalPosition;
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
		float half = Room.TileSize / 2f;
		for (int x = 0; x < Map.Width; x++)
		for (int z = 0; z < Map.Height; z++)
		{
			bool walkable = Map.IsWalkable(x, z);
			if (!walkable && !BordersWalkable(x, z)) continue;
			var center = Map.TileToWorld(x, z);
			yield return (new Rect2(center.X - half, center.Z - half, Room.TileSize, Room.TileSize), walkable);
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
	private IEnumerable<SolidOutline> SolidOutlines()
	{
		var toLocal = GlobalTransform.AffineInverse();
		foreach (var outline in CollisionOutlines.FromGridMap(WallGridMap, Transform3D.Identity)) yield return outline;
		foreach (var outline in CollisionOutlines.FromGridMap(DecorationGridMap, Transform3D.Identity)) yield return outline;
		foreach (var outline in CollisionOutlines.FromStaticBodies(FloorContent, toLocal * FloorContent.GlobalTransform)) yield return outline;
	}

	/// <summary>Physical reachability with inaccessible doorway tiles blocked, without changing the floor grid.</summary>
	private bool[] ReachableWithoutConnectors(IEnumerable<Vector2I> blocked)
	{
		var grid = Floor.Walkability;
		if (grid == null) return null;
		var saved = grid.Save();
		try
		{
			foreach (var tile in blocked)
			{
				var center = Map.TileToWorld(tile);
				float half = Room.TileSize / 2f;
				grid.AddObstacle(new Rect2(center.X - half, center.Z - half, Room.TileSize, Room.TileSize));
			}
			return grid.Reachable([ArrivalPoint()], Room.TileSize);
		}
		finally { grid.Restore(saved); }
	}

	/// <summary>
	/// Adds solid geometry to the floor's walkability only when it creates no new inaccessible pocket: every cell
	/// reachable before is still reachable or lies under the new geometry itself. Leaves the floor unchanged on false.
	/// </summary>
	private bool TryBlockReachably(IReadOnlyList<SolidOutline> solids)
	{
		var grid = Floor.Walkability;
		if (grid == null) return true;
		var before = Floor.Reachable;
		var saved = grid.Save();
		foreach (var solid in solids) grid.AddObstacle(solid);
		var after = grid.Reachable([ArrivalPoint()], Room.TileSize);
		for (int index = 0; index < before.Length; index++)
		{
			if (!before[index] || after[index]) continue;
			if (grid.IsStandable(new Vector2I(index % grid.Size.X, index / grid.Size.X)))
			{
				grid.Restore(saved);
				return false;
			}
		}
		if (!after.Any(reached => reached))
		{
			grid.Restore(saved);
			return false;
		}
		Floor.SetWalkability(grid, after);
		return true;
	}
}