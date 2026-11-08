using System.Linq;
using Godot;

/// <summary>
/// Monster navigation data: the floor's clearance grid, built from the same tiles and collision outlines as floor
/// reachability once every solid is placed, with each door's shut leaf recorded as a gate instead of a solid.
/// </summary>
public partial class MapGenerator
{
	/// <summary>Builds and freezes the clearance grid; runs after loot so chests and fountains count as solids.</summary>
	private void BuildNavigation()
	{
		var grid = new ClearanceGrid(FloorBounds());
		foreach (var (area, walkable) in TileAreas())
		{
			if (walkable) grid.AddFloor(area);
			else grid.AddObstacle(area);
		}
		foreach (var outline in SolidOutlines()) grid.AddObstacle(outline);
		// Gate i is door i: FloorNavigation reads the same list to open and close the gates.
		var toLocal = GlobalTransform.AffineInverse();
		var gates = LiveNodes<Door>().ToArray();
		foreach (var door in gates)
			grid.AddGate(door.Collision?.Shape != null ? door.LeafOutline(open: false, toLocal * door.GlobalTransform).Polygon : []);
		grid.Freeze();
		Floor.SetNavigation(grid, gates);
	}
}