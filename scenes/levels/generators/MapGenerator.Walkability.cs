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
		var grid = new WalkabilityGrid(FloorBounds(), ReachabilityRadius);
		foreach (var (area, walkable) in TileAreas())
		{
			if (walkable) grid.AddFloor(area);
			else grid.AddObstacle(area);
		}
		foreach (var outline in SolidOutlines()) grid.AddObstacle(outline);
		return grid;
	}

	/// <summary>The whole map in generator space, tile edges included.</summary>
	private Rect2 FloorBounds()
	{
		var min = Map.TileToWorld(0, 0);
		var max = Map.TileToWorld(Map.Width - 1, Map.Height - 1);
		float half = Room.TileSize / 2f;
		return new Rect2(min.X - half, min.Z - half, max.X - min.X + Room.TileSize, max.Z - min.Z + Room.TileSize);
	}

	/// <summary>Every map tile's area and whether actors may stand on it.</summary>
	private IEnumerable<(Rect2 Area, bool Walkable)> TileAreas()
	{
		float half = Room.TileSize / 2f;
		for (int x = 0; x < Map.Width; x++)
		for (int z = 0; z < Map.Height; z++)
		{
			var center = Map.TileToWorld(x, z);
			yield return (new Rect2(center.X - half, center.Z - half, Room.TileSize, Room.TileSize), Map.IsWalkable(x, z));
		}
	}

	/// <summary>
	/// Real collision, not tile classes: doorway frames stay passable and wall depth varies per piece. Doors are left
	/// out; they open and close.
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