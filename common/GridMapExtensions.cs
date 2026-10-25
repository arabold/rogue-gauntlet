using Godot;

public static class GridMapExtensions
{
	/// <summary>A cell's grid-local transform (orientation, cell scale and centered position). Compose with the
	/// grid's global transform and the item's mesh transform to place the cell's mesh in the world.</summary>
	public static Transform3D GetCellTransform(this GridMap grid, Vector3I cell) =>
		new(grid.GetBasisWithOrthogonalIndex(grid.GetCellItemOrientation(cell)).Scaled(Vector3.One * grid.CellScale), grid.MapToLocal(cell));
}