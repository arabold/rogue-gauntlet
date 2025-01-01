using Godot;

public partial class MapManager : Node
{
	private GridMap _floorGrid;
	private GridMap _wallGrid;
	private GridMap _decorationGrid;

	public override void _Ready()
	{
		// Get references to the GridMaps
		_floorGrid = GetNode<GridMap>("FloorGrid");
		_wallGrid = GetNode<GridMap>("WallGrid");
		_decorationGrid = GetNode<GridMap>("DecorationGrid");

		GenerateMap();
	}

	public void GenerateMap()
	{
		// Example dimensions
		int width = 10;
		int depth = 10;

		// Place floor tiles with random variations
		for (int x = 0; x < width; x++)
		{
			for (int z = 0; z < depth; z++)
			{
				var cell = new Vector3I(x, 0, z);
				int randomTile = GD.RandRange(0, 2);  // Random tile
				_floorGrid.SetCellItem(cell, randomTile);
			}
		}

		// Create basis for each wall direction
		var northBasis = new Basis(Vector3.Up, 0);                   // Facing north
		var southBasis = new Basis(Vector3.Up, Mathf.Pi);            // Facing south
		var eastBasis = new Basis(Vector3.Up, Mathf.Pi / 2);         // Facing east
		var westBasis = new Basis(Vector3.Up, -Mathf.Pi / 2);        // Facing west

		// Place walls around the edges with proper rotation
		for (int i = 0; i < width; i++)
		{
			_wallGrid.SetCellItem(new Vector3I(i, 0, 0), 0, _wallGrid.GetOrthogonalIndexFromBasis(northBasis));
			_wallGrid.SetCellItem(new Vector3I(i, 0, depth - 1), 0, _wallGrid.GetOrthogonalIndexFromBasis(southBasis));
		}
		for (int i = 0; i < depth; i++)
		{
			_wallGrid.SetCellItem(new Vector3I(0, 0, i), 0, _wallGrid.GetOrthogonalIndexFromBasis(eastBasis));
			_wallGrid.SetCellItem(new Vector3I(width - 1, 0, i), 0, _wallGrid.GetOrthogonalIndexFromBasis(westBasis));
		}

		// Place a decorative torch
		//_decorationGrid.SetCellItem(new Vector3I(5, 1, 5), 2);
	}
}
