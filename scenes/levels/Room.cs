using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Base class for a room in the level.
/// </summary>
[Tool]
public partial class Room : Node3D
{
	/// <summary>
	/// The tile map data for the room
	/// </summary>
	public MapData Map { get; private set; }

	/// <summary>
	/// The bounds of the room in grid coordinates
	/// </summary>
	public Rect2I Bounds { get; private set; }

	[Export] public GridMap FloorGridMap;
	[Export] public GridMap WallGridMap;
	[Export] public GridMap DecorationGridMap;
	private GridMap _debugGridMap;

	// Authored wall mesh footprints, rebuilt by every BakeTileMap (so a Rotate's
	// re-bake sees the rotated geometry). Backs HasWall's edge-coverage checks.
	private List<Aabb> _wallFootprints = new();

	[Export]
	public bool ShowDebugOverlay
	{
		get;
		set
		{
			if (value != field)
			{
				field = value;
				if (value)
				{
					// Property is set in the editor, so we need to check 
					// if the room is initialized already
					if (Engine.IsEditorHint() && IsNodeReady() && GetTree()?.EditedSceneRoot == this)
					{
						BakeTileMap();
						CreateDebugOverlay();
					}
				}
				else if (_debugGridMap != null)
				{
					RemoveDebugOverlay();
				}
			}
		}
	}

	/// <summary>
	/// The size of each tile in the grid map (must be 4x4)
	/// </summary>
	public const int TileSize = 4;

	public override void _Ready()
	{
		base._Ready();

		// Show the debug overlay if needed
		if (Engine.IsEditorHint() && GetTree().EditedSceneRoot == this && ShowDebugOverlay)
		{
			BakeTileMap();
			CreateDebugOverlay();
		}
	}

	public void InitGridMaps()
	{
		// Automatically find the grid maps if they are not set
		if (FloorGridMap == null)
		{
			FloorGridMap = GetNodeOrNull<GridMap>("FloorGridMap");
		}
		if (WallGridMap == null)
		{
			WallGridMap = GetNodeOrNull<GridMap>("WallGridMap");
		}
		if (DecorationGridMap == null)
		{
			DecorationGridMap = GetNodeOrNull<GridMap>("DecorationGridMap");
		}
	}

	/// <summary>
	/// Create a map of the room based on the floor and wall grid maps.
	/// <br/>
	/// This map will be used to determine which tiles are rooms and which
	/// are corridors that need to be connected with other rooms.
	/// </summary>
	public void BakeTileMap()
	{
		// Ensure the grid maps are set
		InitGridMaps();
		if (FloorGridMap == null || WallGridMap == null)
		{
			GD.PrintErr($"{Name}: FloorGridMap and WallGridMap must be set!");
			Map = null;
			return;
		}

		// Determine the bounds of the room
		var usedCells = FloorGridMap.GetUsedCells();
		if (usedCells.Count == 0)
		{
			GD.PrintErr($"{Name}: No floor tiles found in the room!");
			Map = null;
			return;
		}

		// Floor meshes anchor either exactly on a tile center (tile-sized meshes --
		// always even cell coordinates in every authoring path) or one cell off it
		// (half-tile meshes at center±1 -- odd). Snapping odd extremes inward by one
		// recovers the tile-center alignment, so Bounds.Position is the CENTER cell of
		// the room's first (north-west) tile for every floor-mesh size and rotation.
		// It anchors the whole tile coordinate system: tile (x,z)'s center cell is
		// Bounds.Position + (x,z) * TileSize (see TileCenterCell/LocalToTile). The
		// parity trick also avoids integer division entirely -- int '/' truncates
		// toward zero, which floors positive cells but CEILS negative ones, silently
		// shifting rooms that extend into negative coordinates.
		int minCellX = usedCells.MinBy(cell => cell.X).X;
		int maxCellX = usedCells.MaxBy(cell => cell.X).X;
		int minCellZ = usedCells.MinBy(cell => cell.Z).Z;
		int maxCellZ = usedCells.MaxBy(cell => cell.Z).Z;

		int roomXMin = minCellX + (minCellX & 1);
		int roomXMax = maxCellX - (maxCellX & 1);
		int roomZMin = minCellZ + (minCellZ & 1);
		int roomZMax = maxCellZ - (maxCellZ & 1);

		int roomWidth = roomXMax - roomXMin + TileSize;
		int roomDepth = roomZMax - roomZMin + TileSize;

		Bounds = new Rect2I(roomXMin, roomZMin, roomWidth, roomDepth);
		GD.Print($"Room bounds: {Bounds}");

		_wallFootprints = WallFootprints.Collect(WallGridMap);

		var mapSize = ToTilePosition(Bounds.Size.X, 0, Bounds.Size.Y);
		var map = new MapData(mapSize.X, mapSize.Y);

		// Each logical tile is a 4x4 block of GridMap cells centered on its
		// TileCenterCell; a tile is Room floor when any cell in that block holds a
		// floor mesh anchor.
		for (var x = 0; x < map.Width; x++)
		{
			for (var z = 0; z < map.Height; z++)
			{
				var center = TileCenterCell(x, z);
				var gridX = center.X - TileSize / 2;
				var gridZ = center.Z - TileSize / 2;

				// Check for floors
				for (var dx = 0; dx < TileSize; dx++)
				{
					for (var dz = 0; dz < TileSize; dz++)
					{
						var floorVec = new Vector3I(gridX + dx, 0, gridZ + dz);
						var floorCell = FloorGridMap.GetCellItem(floorVec);
						if (floorCell != -1)
						{
							map.SetTile(x, z, MapTile.Room);
							break;
						}
					}
				}
			}
		}

		// Find holes in the room layout and mark them as chasms
		var holes = FindChasms(map);
		foreach (var hole in holes)
		{
			foreach (var cell in hole)
			{
				map.SetTile(cell.X, cell.Y, MapTile.Chasm);
			}
		}

		// Once we have the final room layout, we can check for 
		// walls and corridors that need to be connected
		var doorwayMarkers = GetEnabledDoorwayMarkers().ToList();
		if (doorwayMarkers.Count > 0)
		{
			BakeDoorwayMarkers(map, doorwayMarkers);
		}
		else
		{
			BakeInferredConnectors(map);
		}

		Map = map;
		GD.Print($"Room map generated: {Map.Width}x{Map.Height}");
	}

	/// <summary>
	/// Rotates the room's authored content -- floor/wall/decoration GridMap cells and
	/// every other child node (props, doorway markers, doors) -- by
	/// <paramref name="steps"/> quarter turns (wrapped to 0-3) around the room's local
	/// origin, then re-bakes so Map/Bounds reflect the rotated geometry.
	/// <br/>
	/// This only ever needs to rotate raw geometry, not hand-derive a rotated Map: a
	/// 90-degree-multiple rotation around an integer pivot is a pure permutation of the
	/// integer lattice (a coordinate swap plus sign flips, no scaling or fractional
	/// offset), so it preserves every tile-grid alignment relationship BakeTileMap
	/// depends on -- an authored mesh anchored exactly on a tile's origin stays exactly
	/// on a (relabeled) tile's origin after rotation. Re-baking on the rotated geometry
	/// is therefore exactly as correct as if the room had been authored in that
	/// orientation to begin with, and reuses the same wall/doorway-marker scanning
	/// logic BakeTileMap already validates for every room.
	/// </summary>
	public void Rotate(int steps)
	{
		steps = ((steps % 4) + 4) % 4;
		if (steps == 0)
		{
			return;
		}

		InitGridMaps();
		RotateGridMapCells(FloorGridMap, steps);
		RotateGridMapCells(WallGridMap, steps);
		RotateGridMapCells(DecorationGridMap, steps);

		// Direct children only, not recursive: a node's LOCAL transform is relative to
		// its own parent, not the room, so rotating a direct child's own transform
		// already correctly reorients everything nested under it via normal Godot
		// transform inheritance -- a marker grouped under some organizational wrapper
		// node does not also need its own local transform touched.
		foreach (Node child in GetChildren())
		{
			if (child is Node3D node3D && node3D != FloorGridMap && node3D != WallGridMap && node3D != DecorationGridMap)
			{
				RotateChildTransform(node3D, steps);
			}
		}

		BakeTileMap();
	}

	private static void RotateGridMapCells(GridMap gridMap, int steps)
	{
		if (gridMap == null)
		{
			return;
		}

		// Snapshot before mutating: rewriting cells in place while iterating could have
		// a rotated cell land on a not-yet-processed original cell's position.
		var originalCells = new List<(Vector3I Cell, int Item, int Orientation)>();
		foreach (Vector3I cell in gridMap.GetUsedCells())
		{
			originalCells.Add((cell, gridMap.GetCellItem(cell), gridMap.GetCellItemOrientation(cell)));
		}

		gridMap.Clear();
		Basis rotationStep = new(Vector3.Up, Mathf.DegToRad(steps * 90f));
		foreach (var (cell, item, orientation) in originalCells)
		{
			// Compose via the engine's own basis math rather than a hardcoded lookup of
			// the 4 upright orientations this project's wall/floor content happens to
			// use: a hand-placed decoration can legitimately sit at any of Godot's 24
			// orthogonal orientations (e.g. tipped over for variety), and this handles
			// all of them correctly, not just the upright 4.
			Basis rotatedBasis = rotationStep * gridMap.GetBasisWithOrthogonalIndex(orientation);
			gridMap.SetCellItem(RotateCell(cell, steps), item, gridMap.GetOrthogonalIndexFromBasis(rotatedBasis));
		}
	}

	private static void RotateChildTransform(Node3D node, int steps)
	{
		if (node is DoorwayMarker marker)
		{
			// A marker's visual children (arrow, tile box, label) are entirely
			// regenerated from Directions every time it changes (RoomMarker.
			// UpdateEditorVisual/DoorwayMarker.UpdateArrowVisual) -- they are not fixed,
			// pre-authored geometry that needs the container's own Basis rotated to
			// reorient them. Rotating the Basis *and* updating Directions would rotate
			// the arrow twice (once via the marker's own now-rotated Basis, once via the
			// arrow being redrawn at the new direction's absolute angle) -- e.g. North
			// rotated by one step would end up facing 180 degrees away instead of the
			// correct 90. Only Position needs rotating; Directions' own setter already
			// triggers the single, correct redraw.
			marker.Position = RotatePosition(marker.Position, steps);
			marker.Directions = RotateDirections(marker.Directions, steps);
			return;
		}

		// Compose via the basis, not by adding to RotationDegrees.Y: Euler-angle
		// addition on just the Y component only reproduces "rotate by one more step"
		// correctly when the child's existing rotation is itself pure-Y (no tilt). A
		// hand-authored prop with any X/Z tilt (e.g. a knocked-over barrel) needs the
		// additional rotation applied on top of its existing orientation via matrix
		// composition, which is correct regardless of what that orientation already is.
		Basis rotationStep = new(Vector3.Up, Mathf.DegToRad(steps * 90f));
		node.Transform = new Transform3D(rotationStep * node.Transform.Basis, RotatePosition(node.Position, steps));
	}

	/// <summary>
	/// A quarter-turn rotation around Y, matching the convention already used for wall
	/// mesh footprints (MapGenerator.WallFootprintWorld): local +X rotates toward -Z
	/// per step, the standard right-handed rotation around +Y.
	/// </summary>
	private static Vector3 RotatePosition(Vector3 position, int steps) => steps switch
	{
		1 => new Vector3(position.Z, position.Y, -position.X),
		2 => new Vector3(-position.X, position.Y, -position.Z),
		3 => new Vector3(-position.Z, position.Y, position.X),
		_ => position,
	};

	private static Vector3I RotateCell(Vector3I cell, int steps) => steps switch
	{
		1 => new Vector3I(cell.Z, cell.Y, -cell.X),
		2 => new Vector3I(-cell.X, cell.Y, -cell.Z),
		3 => new Vector3I(-cell.Z, cell.Y, cell.X),
		_ => cell,
	};

	private static RoomMarkerDirection RotateDirections(RoomMarkerDirection directions, int steps)
	{
		RoomMarkerDirection rotated = 0;
		foreach (RoomMarkerDirection direction in new[]
			{ RoomMarkerDirection.North, RoomMarkerDirection.East, RoomMarkerDirection.South, RoomMarkerDirection.West })
		{
			if (!directions.HasFlag(direction))
			{
				continue;
			}

			Vector2I vector = DoorwayMarker.GetDirectionVector(direction);
			Vector3 rotatedVector = RotatePosition(new Vector3(vector.X, 0, vector.Y), steps);
			rotated |= DoorwayMarker.GetDirectionFlag(new Vector2I(
				Mathf.RoundToInt(rotatedVector.X), Mathf.RoundToInt(rotatedVector.Z)));
		}

		return rotated;
	}

	private void BakeDoorwayMarkers(MapData map, List<DoorwayMarker> doorwayMarkers)
	{
		foreach (var doorwayMarker in doorwayMarkers)
		{
			var localPosition = GetMarkerLocalPosition(doorwayMarker);
			var tile = LocalToTile(localPosition);

			// The authoring convention places markers exactly on their tile's center.
			// LocalToTile tolerates up to TileSize/2 of drift, but real drift means the
			// scene was authored against a different convention (or nudged by hand) --
			// surface it here at bake time, where the room and marker are known, instead
			// of letting it resurface as a misrouted corridor three systems later.
			var center = TileCenterCell(tile.X, tile.Y);
			float driftX = Mathf.Abs(localPosition.X - center.X);
			float driftZ = Mathf.Abs(localPosition.Z - center.Z);
			if (driftX > 1f || driftZ > 1f)
			{
				GD.PrintErr($"{Name}: Doorway marker {doorwayMarker.Name} sits ({driftX:F2},{driftZ:F2}) off "
					+ $"its tile's center {center}; markers should be authored exactly on a tile center.");
			}

			var directions = ValidateDoorwayMarker(map, doorwayMarker, tile);
			if (directions.Count == 0)
			{
				continue;
			}

			map.SetConnector(tile.X, tile.Y, directions, isDoorway: true);
		}
	}

	private void BakeInferredConnectors(MapData map)
	{
		for (var x = 0; x < map.Width; x++)
		{
			for (var z = 0; z < map.Height; z++)
			{
				if (map.IsRoom(x, z))
				{
					// A room tile becomes a connector tile if it has at least one
					// open side facing out of the room.
					var connectorDirections = GetConnectorDirections(map, x, z);
					if (connectorDirections.Count > 0)
					{
						map.SetConnector(x, z, connectorDirections);
					}
				}
			}
		}
	}

	private IEnumerable<DoorwayMarker> GetEnabledDoorwayMarkers()
	{
		return FindChildren("*", "", true, false)
			.OfType<DoorwayMarker>()
			.Where(marker => marker.Enabled);
	}

	private Vector3 GetMarkerLocalPosition(Node3D marker)
	{
		var markerTransform = marker.Transform;
		var current = marker.GetParent();

		while (current != null && current != this)
		{
			if (current is not Node3D parent3D)
			{
				GD.PrintErr($"{Name}: Doorway marker {marker.Name} must be parented under Node3D nodes.");
				return marker.Position;
			}

			markerTransform = parent3D.Transform * markerTransform;
			current = current.GetParent();
		}

		return markerTransform.Origin;
	}

	private List<Vector2I> ValidateDoorwayMarker(MapData map, DoorwayMarker marker, Vector2I tile)
	{
		if (!map.IsWithinBounds(tile.X, tile.Y))
		{
			GD.PrintErr($"{Name}: Doorway marker {marker.Name} is outside room bounds at tile {tile}.");
			return [];
		}

		if (!map.IsRoom(tile.X, tile.Y) && !map.IsConnector(tile.X, tile.Y))
		{
			GD.PrintErr($"{Name}: Doorway marker {marker.Name} must be placed on a floor tile. Current tile: {map.Tiles[tile.X, tile.Y]}.");
			return [];
		}

		var validDirections = new List<Vector2I>();
		foreach (var direction in marker.GetDirectionVectors())
		{
			if (direction == Vector2I.Zero)
			{
				continue;
			}

			if (HasWall(tile.X, tile.Y, direction.X, direction.Y))
			{
				GD.PrintErr($"{Name}: Doorway marker {marker.Name} points through a wall at direction {direction}. Remove the wall or change the marker direction.");
				continue;
			}

			var outsideTile = tile + direction;
			if (map.IsWithinBounds(outsideTile.X, outsideTile.Y)
				&& !map.IsEmpty(outsideTile.X, outsideTile.Y))
			{
				GD.PrintErr($"{Name}: Doorway marker {marker.Name} must point out of the room, not into {map.Tiles[outsideTile.X, outsideTile.Y]} at direction {direction}.");
				continue;
			}

			validDirections.Add(direction);
		}

		if (validDirections.Count == 0)
		{
			GD.PrintErr($"{Name}: Doorway marker {marker.Name} has no valid directions.");
		}

		return validDirections;
	}

	/// <summary>
	/// Check if the given tile is a corridor by checking if it is adjacent to an empty tile
	/// </summary>
	private List<Vector2I> GetConnectorDirections(MapData map, int x, int z)
	{
		var connectorDirections = new List<Vector2I>();

		// The 4 adjacent tiles
		var adjacentOffsets = new (int x, int z)[]
		{
			(1, 0), (-1, 0), (0, 1), (0, -1)
		};

		foreach (var (dx, dz) in adjacentOffsets)
		{
			int adjX = x + dx;
			int adjZ = z + dz;

			if (!map.IsWithinBounds(adjX, adjZ) || map.IsEmpty(adjX, adjZ))
			{
				// We found an empty tile adjacent to the room. Check if there is a wall
				// separating the two tiles
				bool hasWall = HasWall(x, z, dx, dz);

				if (!hasWall)
				{
					// We found one open side of the tile, so it can be connected
					// to corridors outside of the room.
					connectorDirections.Add(new Vector2I(dx, dz));
				}
			}
		}

		return connectorDirections;
	}

	/// <summary>
	/// Whether authored wall geometry blocks passage through the given edge of tile
	/// (x,z). Judged from real mesh footprints, not from which cells hold anchors: the
	/// edge is blocked unless its largest uncovered stretch is wide enough to walk
	/// through (half a tile). Footprint math is rotation-invariant -- a rotated wall's
	/// footprint rotates exactly with it -- where the previous cell sampling of a
	/// half-open edge row flipped corner anchors in and out of range per orientation,
	/// making the same room validate differently at different rotations. It also means
	/// a doorway's frame posts no longer read as "wall": only actual coverage counts.
	/// </summary>
	private bool HasWall(int x, int z, int dx, int dz)
	{
		var center = TileCenterCell(x, z);
		float half = TileSize / 2f;
		bool horizontal = dz != 0;
		float lineCoord = horizontal ? center.Z + dz * half : center.X + dx * half;
		float spanMin = (horizontal ? center.X : center.Z) - half;
		float spanMax = spanMin + TileSize;

		float passableGap = WallFootprints.MaxUncoveredGap(
			_wallFootprints, horizontal, lineCoord, spanMin, spanMax);
		return passableGap < TileSize / 2f - WallFootprints.Eps;
	}

	private void CreateDebugOverlay()
	{
		if (Map == null)
		{
			GD.PrintErr("No map data available to generate overlay!");
			return;
		}

		RemoveDebugOverlay();

		// We always create a new grid map to adapt to changes in the room layout
		_debugGridMap = new GridMap();
		_debugGridMap.Name = "DebugGridMap";
		_debugGridMap.MeshLibrary = GD.Load<MeshLibrary>("res://scenes/levels/BaseMapMeshLibrary.tres");
		_debugGridMap.CellSize = new Vector3(TileSize, TileSize, TileSize);
		_debugGridMap.CellCenterX = false;
		_debugGridMap.CellCenterY = false;
		_debugGridMap.CellCenterZ = false;
		_debugGridMap.Translate(new Vector3(Bounds.Position.X, 0.1f, Bounds.Position.Y));
		_debugGridMap.Visible = ShowDebugOverlay;
		AddChild(_debugGridMap);

		// TODO: Make this more efficient by only updating the overlay when needed
		// Add a refresh timer to update the overlay when the room layout changes
		// var timer = new Timer();
		// _debugGridMap.AddChild(timer);
		// timer.OneShot = true;
		// timer.WaitTime = 0.5f;
		// timer.Timeout += () =>
		// {
		// 	if (_showDebugOverlay)
		// 	{
		// 		RecreateMap();
		// 		CreateDebugOverlay();
		// 	}
		// };
		// timer.Start();

		GD.Print("Generating overlay...");
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				var position = new Vector3I(x, 0, z);
				if (Map.IsRoom(x, z))
				{
					_debugGridMap.SetCellItem(position, 0, 0);
				}
				else if (Map.IsConnector(x, z))
				{
					_debugGridMap.SetCellItem(position, 1, 0);
				}
				else
				{
					_debugGridMap.SetCellItem(position, 2, 0);
				}
			}
		}
	}

	private void RemoveDebugOverlay()
	{
		foreach (var child in GetChildren())
		{
			if (child is GridMap gridMap && gridMap.Name == "DebugGridMap")
			{
				RemoveChild(gridMap);
				gridMap.QueueFree();
			}
		}

		_debugGridMap = null;
	}

	/// <summary>
	/// Finds all holes in the TileMap.
	/// </summary>
	/// <returns>A list of lists, where each inner list contains cells forming a hole.</returns>
	public List<List<Vector2I>> FindChasms(MapData map)
	{
		var holes = new List<List<Vector2I>>();
		var visited = new bool[map.Width, map.Height];

		for (int i = 0; i < map.Width; i++)
		{
			for (int j = 0; j < map.Height; j++)
			{
				// If the cell is 0 and not visited, start BFS
				if (map.IsEmpty(i, j) && !visited[i, j])
				{
					var hole = BFS(map, i, j, visited);
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
	private List<Vector2I> BFS(MapData map, int startX, int startY, bool[,] visited)
	{
		var queue = new Queue<Vector2I>();
		var holeCells = new List<Vector2I>();
		bool isHole = true;

		queue.Enqueue(new Vector2I(startX, startY));
		visited[startX, startY] = true;

		// Directions: Up, Down, Left, Right
		int[] dX = { -1, 1, 0, 0 };
		int[] dY = { 0, 0, -1, 1 };

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			holeCells.Add(current);

			// If the current cell is on the boundary, it's not a hole
			if (map.IsOnBoundary(current.X, current.Y))
			{
				isHole = false;
			}

			// Explore all four directions
			for (int dir = 0; dir < 4; dir++)
			{
				int newX = current.X + dX[dir];
				int newY = current.Y + dY[dir];

				if (map.IsWithinBounds(newX, newY) && !visited[newX, newY] && map.IsEmpty(newX, newY))
				{
					queue.Enqueue(new Vector2I(newX, newY));
					visited[newX, newY] = true;
				}
			}
		}

		return isHole ? holeCells : null;
	}

	protected Vector2I ToTilePosition(Vector3I position)
	{
		return ToTilePosition(position.X, 0, position.Z);
	}

	protected Vector2I ToTilePosition(int x, int y, int z)
	{
		var tileX = Mathf.FloorToInt(x / TileSize);
		var tileZ = Mathf.FloorToInt(z / TileSize);
		return new Vector2I(tileX, tileZ);
	}

	protected Vector3I ToGridPosition(Vector2I position)
	{
		return ToGridPosition(position.X, 0, position.Y);
	}

	protected Vector3I ToGridPosition(int x, int y, int z)
	{
		var gridX = x * TileSize;
		var gridZ = z * TileSize;
		return new Vector3I(gridX, y, gridZ);
	}

	/// <summary>
	/// The room-local tile containing a local position: the tile whose CENTER is
	/// nearest. Tile (x,z)'s 4x4-cell footprint is centered on
	/// <see cref="TileCenterCell"/>, so a position keeps resolving to its tile with up
	/// to TileSize/2 of slack in every direction. (The previous floor-binning placed
	/// tile centers exactly on a bin boundary, where any negative drift -- e.g. float
	/// error from a rotated marker's composed transform -- flipped the result to the
	/// neighboring tile; that knife edge is what historically made rotated rooms fail
	/// doorway validation.)
	/// </summary>
	public Vector2I LocalToTile(Vector3 localPosition)
	{
		return new Vector2I(
			Mathf.FloorToInt((localPosition.X - Bounds.Position.X + TileSize / 2f) / TileSize),
			Mathf.FloorToInt((localPosition.Z - Bounds.Position.Y + TileSize / 2f) / TileSize));
	}

	/// <summary>
	/// The grid cell at the center of tile (x,z) -- the anchor every piece of
	/// tile-relative cell math hangs off. A tile's cells span this center
	/// ± TileSize/2 on both axes.
	/// </summary>
	public Vector3I TileCenterCell(int x, int z)
	{
		return new Vector3I(x * TileSize + Bounds.Position.X, 0, z * TileSize + Bounds.Position.Y);
	}
}
