using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Monster navigation data: the floor's clearance grids, built from the same tiles and collision outlines as floor
/// reachability once every solid is placed, with each door's shut leaf recorded as a gate instead of a solid. Walkers
/// get the ground grid; flyers get an air grid where pits and water are open and solids no taller than
/// <see cref="ClearanceGrid.AirClearanceHeight"/> do not block.
/// </summary>
public partial class MapGenerator
{
	/// <summary>
	/// Builds and freezes both grids; runs after loot so chests and fountains count as solids. Void, walls, doors and tall
	/// solids block both, so they go into one grid that is then copied: flyers add the pits as floor, walkers add pits,
	/// low solids and water's walking barriers as obstacles.
	/// </summary>
	private void BuildNavigation()
	{
		var shared = new ClearanceGrid(Map.WorldBounds);
		var pits = new List<Rect2>();
		var walkersOnly = new List<Rect2>();
		for (int x = 0; x < Map.Width; x++)
		for (int z = 0; z < Map.Height; z++)
		{
			var area = RoomFootprint.TileArea(Map.TileToWorld(x, z));
			if (Map.IsWalkable(x, z)) shared.AddFloor(area);
			else if (Map.IsChasm(x, z))
			{
				pits.Add(area);
				if (BordersOpen(x, z, flying: false)) walkersOnly.Add(area);
			}
			// Void beside a pit alone lies a whole tile from any walkable cell, beyond the clearance walkers measure.
			else if (BordersOpen(x, z, flying: true)) shared.AddObstacle(area);
		}
		var low = new List<SolidOutline>();
		foreach (var outline in SolidOutlines(CollisionOutlines.BlockingLayers & ~CollisionOutlines.WalkingTerrainLayer))
		{
			if (outline.Top > ClearanceGrid.AirClearanceHeight) shared.AddObstacle(outline);
			else low.Add(outline);
		}
		// Gate i is door i in both grids: FloorNavigation reads the same list to open and close the gates.
		var toLocal = GlobalTransform.AffineInverse();
		var gates = LiveNodes<Door>().ToArray();
		foreach (var door in gates)
			shared.AddGate(door.Collision?.Shape != null ? door.LeafOutline(open: false, toLocal * door.GlobalTransform).Polygon : []);

		var air = shared.Copy();
		foreach (var pit in pits) air.AddFloor(pit);
		air.Freeze();

		var ground = shared;
		foreach (var pit in walkersOnly) ground.AddObstacle(pit);
		foreach (var outline in low) ground.AddObstacle(outline);
		foreach (var barrier in LiveNodes<StaticBody3D>().Where(body => body.CollisionLayer == CollisionOutlines.WalkingTerrainLayer))
		foreach (var outline in CollisionOutlines.FromStaticBodies(barrier, toLocal * barrier.GlobalTransform, CollisionOutlines.WalkingTerrainLayer))
			ground.AddObstacle(outline);
		ground.Freeze();
		Floor.SetNavigation(ground, air, gates);
	}
}
