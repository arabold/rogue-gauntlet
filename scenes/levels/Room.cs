using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// An assembled room: its floor, wall and decoration GridMaps, its props and doors, and the tile map the level
/// generator places and connects. The room's assembly hands over the map as data (<see cref="SetLayout"/>), so nothing
/// is read back from geometry; <see cref="Rotate"/> turns the geometry and the map together.
/// </summary>
[Tool]
[GlobalClass]
public partial class Room : Node3D
{
	/// <summary>Floor, pit (chasm) and doorway (connector) tiles in room-local tile coordinates.</summary>
	public MapData Map { get; private set; }

	/// <summary>The room-local grid cell at the center of map tile (0, 0); tile (x, z) is centered TileSize·(x, z) from it.</summary>
	public Vector3I Origin { get; private set; }

	/// <summary>The door set into each doorway that has one, by its connector tile in <see cref="Map"/>.</summary>
	public IReadOnlyDictionary<Vector2I, Door> Doors => _doors;
	private Dictionary<Vector2I, Door> _doors = [];

	/// <summary>The room type that assembled this room, content included.</summary>
	[Export] public RoomType Type { get; set; }
	/// <summary>Circulation axis; zero means the room has no primary aisle.</summary>
	[Export] public Vector2I LayoutAxis { get; set; }

	[Export] public GridMap FloorGridMap { get; set; }
	[Export] public GridMap WallGridMap { get; set; }
	[Export] public GridMap DecorationGridMap { get; set; }

	/// <summary>
	/// The size of each tile in the grid map (must be 4x4)
	/// </summary>
	public const int TileSize = 4;

	/// <summary>
	/// Sets the room's tile map from its assembly: every footprint tile is floor, except pits cut out of it (chasms that
	/// reveal with the room but are not walkable) and doorways (connectors toward their outward direction). Footprint
	/// tile (0, 0) is centered on the local origin.
	/// </summary>
	public void SetLayout(RoomFootprint footprint, IEnumerable<Vector2I> pits, IReadOnlyCollection<(RoomDoorway Doorway, Door Door)> doorways)
	{
		// Corridors only ever reach a room through a doorway.
		if (doorways.Count == 0) throw new InvalidOperationException($"{Name}: a room needs at least one doorway.");
		var map = new MapData(footprint.Width, footprint.Length);
		foreach (var tile in footprint.Tiles) map.SetTile(tile.X, tile.Y, MapTile.Room);
		foreach (var pit in pits) map.SetTile(pit.X, pit.Y, MapTile.Chasm);
		var doors = new Dictionary<Vector2I, Door>();
		foreach (var (doorway, door) in doorways)
		{
			map.SetConnector(doorway.Tile.X, doorway.Tile.Y, [doorway.Direction]);
			if (door != null) doors[doorway.Tile] = door;
		}
		Map = map;
		Origin = Vector3I.Zero;
		_doors = doors;
	}

	/// <summary>
	/// Rotates the room by <paramref name="steps"/> quarter turns (wrapped to 0-3) around its local origin: the
	/// floor/wall/decoration GridMap cells, every other child node (props, doors, frames) and, as data, its tile map.
	/// A quarter turn of an integer lattice is a pure permutation (a coordinate swap plus a sign flip), so every
	/// tile-grid alignment survives exactly.
	/// </summary>
	public void Rotate(int steps)
	{
		steps = ((steps % 4) + 4) % 4;
		if (steps == 0)
		{
			return;
		}

		var axis = RotatePosition(new Vector3(LayoutAxis.X, 0, LayoutAxis.Y), steps);
		LayoutAxis = new((int)axis.X, (int)axis.Z);
		RotateGridMapCells(FloorGridMap, steps);
		RotateGridMapCells(WallGridMap, steps);
		RotateGridMapCells(DecorationGridMap, steps);

		// Direct children only, not recursive: a node's LOCAL transform is relative to
		// its own parent, not the room, so rotating a direct child's own transform
		// already correctly reorients everything nested under it via normal Godot
		// transform inheritance.
		foreach (Node child in GetChildren())
		{
			if (child is Node3D node3D && node3D != FloorGridMap && node3D != WallGridMap && node3D != DecorationGridMap)
			{
				RotateChildTransform(node3D, steps);
			}
		}

		for (int step = 0; step < steps; step++)
		{
			// Tile (0, 0) of the turned map is the old map's last column of its first row.
			Origin = RotateCell(Origin, 1) + new Vector3I(0, 0, -(Map.Width - 1) * TileSize);
			_doors = _doors.ToDictionary(pair => MapData.TurnTile(pair.Key, Map.Width), pair => pair.Value);
			Map = Map.Turned();
		}
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
			// use: a decoration can legitimately sit at any of Godot's 24 orthogonal
			// orientations (e.g. tipped over for variety), and this handles all of them.
			Basis rotatedBasis = rotationStep * gridMap.GetBasisWithOrthogonalIndex(orientation);
			gridMap.SetCellItem(RotateCell(cell, steps), item, gridMap.GetOrthogonalIndexFromBasis(rotatedBasis));
		}
	}

	private static void RotateChildTransform(Node3D node, int steps)
	{
		// Compose via the basis, not by adding to RotationDegrees.Y: Euler-angle
		// addition on just the Y component only reproduces "rotate by one more step"
		// correctly when the child's existing rotation is itself pure-Y (no tilt). A
		// prop with any X/Z tilt (e.g. a knocked-over barrel) needs the additional
		// rotation applied on top of its existing orientation via matrix composition.
		Basis rotationStep = new(Vector3.Up, Mathf.DegToRad(steps * 90f));
		node.Transform = new Transform3D(rotationStep * node.Transform.Basis, RotatePosition(node.Position, steps));
	}

	/// <summary>
	/// A quarter-turn rotation around Y, matching the convention already used for wall
	/// mesh footprints (WallFootprints.FootprintWorld): local +X rotates toward -Z
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
}