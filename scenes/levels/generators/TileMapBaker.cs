using System.Collections.Generic;
using Godot;

public class TileMapBaker
{
	private readonly MapData _map;
	private readonly int _rows;
	private readonly int _cols;
	private readonly bool[,] _visited;

	private GridMap FloorGridMap;
	private GridMap WallGridMap;
	private GridMap DecorationGridMap;

	public TileMapBaker(MapData map, GridMap floorGridMap, GridMap wallGridMap, GridMap decorationGridMap)
	{
		_map = map;
		_rows = map.Width;
		_cols = map.Height;
		_visited = new bool[_rows, _cols];
		FloorGridMap = floorGridMap;
		WallGridMap = wallGridMap;
		DecorationGridMap = decorationGridMap;
	}

	/// <summary>
	/// Finds all holes in the TileMap.
	/// </summary>
	/// <returns>A list of lists, where each inner list contains cells forming a hole.</returns>
	public List<List<Vector2I>> FindChasms()
	{
		var holes = new List<List<Vector2I>>();

		for (int i = 0; i < _rows; i++)
		{
			for (int j = 0; j < _cols; j++)
			{
				// If the cell is 0 and not visited, start BFS
				if (_map.IsEmpty(i, j) && !_visited[i, j])
				{
					var hole = BFS(i, j);
					if (hole != null && hole.Count > 0)
					{
						holes.Add(hole);
					}
				}
			}
		}

		return holes;
	}

	/// <summary>
	/// Performs BFS to find all contiguous zeros connected to the starting cell.
	/// </summary>
	/// <param name="startX">Starting cell's X-coordinate.</param>
	/// <param name="startY">Starting cell's Y-coordinate.</param>
	/// <returns>A list of cells forming a hole, or null if not a hole.</returns>
	private List<Vector2I> BFS(int startX, int startY)
	{
		var queue = new Queue<Vector2I>();
		var holeCells = new List<Vector2I>();
		bool isHole = true;

		queue.Enqueue(new Vector2I(startX, startY));
		_visited[startX, startY] = true;

		// Directions: Up, Down, Left, Right
		int[] dX = { -1, 1, 0, 0 };
		int[] dY = { 0, 0, -1, 1 };

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			holeCells.Add(current);

			// If the current cell is on the boundary, it's not a hole
			if (IsOnBoundary(current.X, current.Y))
			{
				isHole = false;
			}

			// Explore all four directions
			for (int dir = 0; dir < 4; dir++)
			{
				int newX = current.X + dX[dir];
				int newY = current.Y + dY[dir];

				if (IsValid(newX, newY) && !_visited[newX, newY] && _map.IsEmpty(newX, newY))
				{
					queue.Enqueue(new Vector2I(newX, newY));
					_visited[newX, newY] = true;
				}
			}
		}

		return isHole ? holeCells : null;
	}

	/// <summary>
	/// Checks if the given cell is on the boundary of the TileMap.
	/// </summary>
	private bool IsOnBoundary(int x, int y)
	{
		return x == 0 || y == 0 || x == _rows - 1 || y == _cols - 1;
	}

	/// <summary>
	/// Validates if the cell coordinates are within the TileMap bounds.
	/// </summary>
	private bool IsValid(int x, int y)
	{
		return x >= 0 && x < _rows && y >= 0 && y < _cols;
	}
}