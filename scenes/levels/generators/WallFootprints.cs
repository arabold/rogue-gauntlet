using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Shared wall-geometry math: real, rotated mesh footprints as the single source of
/// truth for "is this tile edge covered by wall?". Used by the generator's corridor wall
/// pass and by <see cref="WallStyle"/>'s room walls to seal every required edge, and to
/// measure the walls rooms furnish against.
/// </summary>
public static class WallFootprints
{
	public const float Eps = 0.05f;

	public const int HorizontalWallOrientation = 0;
	public const int VerticalWallOrientation = 16;
	public const int NorthWestCornerOrientation = 16;
	public const int NorthEastCornerOrientation = 0;
	public const int SouthWestCornerOrientation = 10;
	public const int SouthEastCornerOrientation = 22;

	private readonly record struct WallStraightRequest(Vector3I Position, int Orientation);

	/// <summary>GridMap orientation that turns local +Z toward <paramref name="inward"/>.</summary>
	public static int FacingInto(Vector2I inward) =>
		inward == Vector2I.Down ? GridTurns.Turn0
		: inward == Vector2I.Up ? GridTurns.Turn180
		: inward == Vector2I.Right ? GridTurns.Turn90
		: GridTurns.Turn270;

	public struct WallCornerEdges
	{
		public bool HorizontalWest;
		public bool HorizontalEast;
		public bool VerticalNorth;
		public bool VerticalSouth;
	}

	/// <summary>
	/// Places corner pieces, straight walls and half walls onto <paramref name="wallGridMap"/> for every tile in
	/// <paramref name="sourceTiles"/> whose cardinal edge satisfies <paramref name="needsWallToward"/>.
	/// Shared between runtime level generation (<see cref="MapGenerator"/>) and standalone room generation
	/// (<see cref="WallStyle"/>). Pieces never overlap along a wall: corners go first, and since a corner's arms
	/// cover half of each edge they join, the rest of such an edge gets a half piece instead of a full one. Pieces
	/// face the tile that requested them; <paramref name="pickStraight"/> may vary the full piece per edge position.
	/// </summary>
	public static void PlaceWalls(
		GridMap wallGridMap,
		int tileSize,
		IEnumerable<(Vector2I Tile, Vector3I BasePosition)> sourceTiles,
		Func<Vector2I, int, int, bool> needsWallToward,
		Func<int> getWallItem,
		Func<int> getHalfWallItem,
		Func<int> getCornerWallItem,
		Func<Vector3I, int> pickStraight = null)
	{
		if (wallGridMap == null)
		{
			return;
		}

		var cornerEdges = new Dictionary<Vector2I, WallCornerEdges>();
		var straightWalls = new HashSet<WallStraightRequest>();
		var inwardDirections = new Dictionary<Vector3I, Vector2I>();
		var wallBoxes = Collect(wallGridMap);
		MeshLibrary library = wallGridMap.MeshLibrary;
		int tileCenter = tileSize / 2;

		foreach (var (tile, basePosition) in sourceTiles)
		{
			int westX = basePosition.X - tileCenter;
			int eastX = basePosition.X + tileCenter;
			int northZ = basePosition.Z - tileCenter;
			int southZ = basePosition.Z + tileCenter;

			bool north = needsWallToward(tile, 0, -1) && !EdgeFullyCovered(wallBoxes, horizontal: true, northZ, westX, eastX);
			bool south = needsWallToward(tile, 0, 1) && !EdgeFullyCovered(wallBoxes, horizontal: true, southZ, westX, eastX);
			bool west = needsWallToward(tile, -1, 0) && !EdgeFullyCovered(wallBoxes, horizontal: false, westX, northZ, southZ);
			bool east = needsWallToward(tile, 1, 0) && !EdgeFullyCovered(wallBoxes, horizontal: false, eastX, northZ, southZ);

			if (north)
			{
				AddHorizontalCornerEdge(cornerEdges, new Vector2I(westX, northZ), extendsEast: true);
				AddHorizontalCornerEdge(cornerEdges, new Vector2I(eastX, northZ), extendsEast: false);
				var position = basePosition + new Vector3I(0, 0, -tileCenter);
				straightWalls.Add(new(position, HorizontalWallOrientation));
				inwardDirections.TryAdd(position, Vector2I.Down);
			}

			if (south)
			{
				AddHorizontalCornerEdge(cornerEdges, new Vector2I(westX, southZ), extendsEast: true);
				AddHorizontalCornerEdge(cornerEdges, new Vector2I(eastX, southZ), extendsEast: false);
				var position = basePosition + new Vector3I(0, 0, tileCenter);
				straightWalls.Add(new(position, HorizontalWallOrientation));
				inwardDirections.TryAdd(position, Vector2I.Up);
			}

			if (west)
			{
				AddVerticalCornerEdge(cornerEdges, new Vector2I(westX, northZ), extendsSouth: true);
				AddVerticalCornerEdge(cornerEdges, new Vector2I(westX, southZ), extendsSouth: false);
				var position = basePosition + new Vector3I(-tileCenter, 0, 0);
				straightWalls.Add(new(position, VerticalWallOrientation));
				inwardDirections.TryAdd(position, Vector2I.Right);
			}

			if (east)
			{
				AddVerticalCornerEdge(cornerEdges, new Vector2I(eastX, northZ), extendsSouth: true);
				AddVerticalCornerEdge(cornerEdges, new Vector2I(eastX, southZ), extendsSouth: false);
				var position = basePosition + new Vector3I(tileCenter, 0, 0);
				straightWalls.Add(new(position, VerticalWallOrientation));
				inwardDirections.TryAdd(position, Vector2I.Left);
			}
		}

		PlaceWallCorners(wallGridMap, cornerEdges, getCornerWallItem, wallBoxes);

		foreach (var wall in straightWalls)
		{
			bool horizontal = wall.Orientation == HorizontalWallOrientation;
			var (line, min, max) = EdgeSpan(wall.Position, horizontal, tileSize);
			if (!TryGetUncoveredSpan(wallBoxes, horizontal, line, min, max, out var gap))
			{
				continue;
			}

			var inward = inwardDirections[wall.Position];
			if (TryPlaceHalf(wallGridMap, horizontal, line, min, tileSize, gap, inward, getHalfWallItem, wallBoxes)
				|| wallGridMap.GetCellItem(wall.Position) >= 0)
			{
				continue;
			}

			int wallItem = pickStraight?.Invoke(wall.Position) ?? getWallItem();
			if (wallItem >= 0)
			{
				Place(wallGridMap, wall.Position, wallItem, FacingInto(inward), wallBoxes);
			}
		}
	}

	/// <summary>
	/// Fills the half of an edge that the uncovered <paramref name="gap"/> lies in (a corner arm covers the other half)
	/// with the half piece, facing <paramref name="inward"/>. It covers the whole half even where a crossing wall's
	/// thickness shortens the gap, and turns around only when its facing cell, the edge's far end, holds another corner.
	/// </summary>
	private static bool TryPlaceHalf(
		GridMap wallGridMap, bool horizontal, float line, float min, int tileSize, (float A, float B) gap, Vector2I inward,
		Func<int> getHalfWallItem, List<Aabb> wallBoxes)
	{
		float mid = min + tileSize / 2f;
		if (gap.B - gap.A < tileSize / 4f || (gap.A < mid - Eps && gap.B > mid + Eps))
		{
			return false;
		}

		int item = getHalfWallItem();
		Mesh mesh = item >= 0 ? wallGridMap.MeshLibrary?.GetItemMesh(item) : null;
		if (mesh == null || Mathf.Abs(mesh.GetAabb().Size.X - tileSize / 2f) > Eps)
		{
			return false;
		}

		float target = gap.B <= mid + Eps ? min : mid;
		foreach (int facing in (ReadOnlySpan<int>)[FacingInto(inward), FacingInto(-inward)])
		{
			int along = Mathf.RoundToInt(target - Along(FootprintWorld(mesh.GetAabb(), facing, Vector3I.Zero), horizontal).Min);
			var cell = horizontal ? new Vector3I(along, 0, Mathf.RoundToInt(line)) : new Vector3I(Mathf.RoundToInt(line), 0, along);
			if (wallGridMap.GetCellItem(cell) < 0)
			{
				Place(wallGridMap, cell, item, facing, wallBoxes);
				return true;
			}
		}

		return false;
	}

	/// <summary>One tile edge as its wall pieces cover it. <see cref="Item"/> is the edge's own piece at
	/// <see cref="Cell"/> (a straight, or a half beside a corner), -1 where corner arms alone cover it.</summary>
	public readonly record struct WallEdge(bool Covered, float Depth, int Item, Vector3I Cell);

	/// <summary>
	/// Measures the tile edge centered on <paramref name="edgeCell"/> from the pieces running along it: whether they seal
	/// it, how far they reach from the wall line toward <paramref name="inward"/> (a corner by its arm, not its whole L),
	/// and the edge's own piece. Walls crossing the edge's ends share only their thickness with it and do not count.
	/// </summary>
	public static WallEdge MeasureEdge(GridMap wallGridMap, Vector3I edgeCell, Vector2I inward, int tileSize)
	{
		bool horizontal = inward.X == 0;
		var (line, min, max) = EdgeSpan(edgeCell, horizontal, tileSize);
		var step = horizontal ? Vector3I.Right : Vector3I.Back;
		var covering = new List<Aabb>();
		float depth = 0;
		var own = (Item: -1, Cell: Vector3I.Zero);
		// Pieces on the edge sit on its line within a tile of its center; the center goes first.
		for (int index = 0; index <= 2 * tileSize; index++)
		{
			var cell = edgeCell + step * ((index + 1) / 2 * (index % 2 == 0 ? -1 : 1));
			int item = wallGridMap.GetCellItem(cell);
			Mesh mesh = item >= 0 ? wallGridMap.MeshLibrary.GetItemMesh(item) : null;
			if (mesh == null)
			{
				continue;
			}

			foreach (var box in Footprints(mesh.GetAabb(), wallGridMap.GetCellItemOrientation(cell), cell, wallGridMap.MeshLibrary.GetItemName(item)))
			{
				var (a, b) = Along(box, horizontal);
				if (Mathf.Min(b, max) - Mathf.Max(a, min) <= tileSize / 4f)
				{
					continue;
				}

				covering.Add(box);
				var (near, far) = Across(box, horizontal);
				depth = Mathf.Max(depth, (horizontal ? inward.Y : inward.X) > 0 ? far - line : line - near);
				if (own.Item < 0 && a >= min - Eps && b <= max + Eps)
				{
					own = (item, cell);
				}
			}
		}

		if (covering.Count == 0)
		{
			return new(false, .5f, -1, default);
		}

		return new(!TryGetUncoveredSpan(covering, horizontal, line, min, max, out _), depth, own.Item, own.Cell);
	}

	public static bool EdgeFullyCovered(List<Aabb> wallBoxes, bool horizontal, float lineCoord, float spanMin, float spanMax)
	{
		return !TryGetUncoveredSpan(wallBoxes, horizontal, lineCoord, spanMin, spanMax, out _);
	}

	private static void AddHorizontalCornerEdge(Dictionary<Vector2I, WallCornerEdges> cornerEdges, Vector2I position, bool extendsEast)
	{
		cornerEdges.TryGetValue(position, out var edges);
		if (extendsEast)
		{
			edges.HorizontalEast = true;
		}
		else
		{
			edges.HorizontalWest = true;
		}

		cornerEdges[position] = edges;
	}

	private static void AddVerticalCornerEdge(Dictionary<Vector2I, WallCornerEdges> cornerEdges, Vector2I position, bool extendsSouth)
	{
		cornerEdges.TryGetValue(position, out var edges);
		if (extendsSouth)
		{
			edges.VerticalSouth = true;
		}
		else
		{
			edges.VerticalNorth = true;
		}

		cornerEdges[position] = edges;
	}

	private static void PlaceWallCorners(
		GridMap wallGridMap,
		Dictionary<Vector2I, WallCornerEdges> cornerEdges,
		Func<int> getCornerWallItem,
		List<Aabb> wallBoxes)
	{
		foreach (var (position, edges) in cornerEdges)
		{
			if (edges.HorizontalEast && edges.VerticalSouth)
			{
				PlaceWallCorner(wallGridMap, position, NorthWestCornerOrientation, getCornerWallItem, wallBoxes);
			}
			else if (edges.HorizontalWest && edges.VerticalSouth)
			{
				PlaceWallCorner(wallGridMap, position, NorthEastCornerOrientation, getCornerWallItem, wallBoxes);
			}
			else if (edges.HorizontalEast && edges.VerticalNorth)
			{
				PlaceWallCorner(wallGridMap, position, SouthWestCornerOrientation, getCornerWallItem, wallBoxes);
			}
			else if (edges.HorizontalWest && edges.VerticalNorth)
			{
				PlaceWallCorner(wallGridMap, position, SouthEastCornerOrientation, getCornerWallItem, wallBoxes);
			}
		}
	}

	private static void PlaceWallCorner(
		GridMap wallGridMap,
		Vector2I position,
		int orientation,
		Func<int> getCornerWallItem,
		List<Aabb> wallBoxes)
	{
		var cellPosition = new Vector3I(position.X, 0, position.Y);
		if (wallGridMap.GetCellItem(cellPosition) < 0)
		{
			int cornerItem = getCornerWallItem();
			if (cornerItem >= 0)
			{
				Place(wallGridMap, cellPosition, cornerItem, orientation, wallBoxes);
			}
		}
	}

	/// <summary>Sets a piece into the grid and records its footprints for the coverage checks that follow.</summary>
	private static void Place(GridMap wallGridMap, Vector3I cell, int item, int orientation, List<Aabb> wallBoxes)
	{
		wallGridMap.SetCellItem(cell, item, orientation);
		if (wallGridMap.MeshLibrary?.GetItemMesh(item) is { } mesh)
		{
			wallBoxes.AddRange(Footprints(mesh.GetAabb(), orientation, cell, wallGridMap.MeshLibrary.GetItemName(item)));
		}
	}

	/// <summary>
	/// World XZ boxes of a piece at <paramref name="cell"/>: its footprint, or for an L-shaped corner (at least half a
	/// tile deep along both axes) its two arms, each as thick as its wall, so the inside of the L stays open.
	/// </summary>
	private static IEnumerable<Aabb> Footprints(Aabb local, int orientation, Vector3I cell, string itemName)
	{
		var footprint = FootprintWorld(local, orientation, cell);
		if (footprint.Size.X < Room.TileSize / 2f || footprint.Size.Z < Room.TileSize / 2f)
		{
			yield return footprint;
			yield break;
		}

		float halfDepthZ = Mathf.Min(cell.Z - footprint.Position.Z, footprint.End.Z - cell.Z);
		float halfDepthX = Mathf.Min(cell.X - footprint.Position.X, footprint.End.X - cell.X);
		// A T junction is centered on its crossbar: its stem has the same thickness as that bar.
		if (itemName.StartsWith("wall_Tsplit"))
		{
			float thickness = Mathf.Min(halfDepthX, halfDepthZ);
			halfDepthX = thickness;
			halfDepthZ = thickness;
		}
		yield return new Aabb(new Vector3(footprint.Position.X, footprint.Position.Y, cell.Z - halfDepthZ), new Vector3(footprint.Size.X, footprint.Size.Y, 2 * halfDepthZ));
		yield return new Aabb(new Vector3(cell.X - halfDepthX, footprint.Position.Y, footprint.Position.Z), new Vector3(2 * halfDepthX, footprint.Size.Y, footprint.Size.Z));
	}

	/// <summary>The wall line of the tile edge centered on <paramref name="edgeCell"/> and the edge's extent along it.</summary>
	private static (float Line, float Min, float Max) EdgeSpan(Vector3I edgeCell, bool horizontal, int tileSize)
	{
		float center = horizontal ? edgeCell.X : edgeCell.Z;
		return (horizontal ? edgeCell.Z : edgeCell.X, center - tileSize / 2f, center + tileSize / 2f);
	}

	/// <summary>A box's extent along a horizontal (X) or vertical (Z) wall line.</summary>
	private static (float Min, float Max) Along(Aabb box, bool horizontal) =>
		horizontal ? (box.Position.X, box.End.X) : (box.Position.Z, box.End.Z);

	/// <summary>A box's extent across a wall line.</summary>
	private static (float Min, float Max) Across(Aabb box, bool horizontal) => Along(box, !horizontal);

	/// <summary>
	/// World XZ footprint of a wall cell. Wall pieces use only the upright Y-rotation
	/// orientations {0:0°, 16:90°, 10:180°, 22:270°}; the GridMaps use cell_center=false,
	/// so the mesh origin sits at the cell coordinate.
	/// </summary>
	public static Aabb FootprintWorld(Aabb local, int orientation, Vector3I origin)
	{
		float x0 = local.Position.X, x1 = local.End.X, z0 = local.Position.Z, z1 = local.End.Z;
		(float minX, float maxX, float minZ, float maxZ) = orientation switch
		{
			16 => (z0, z1, -x1, -x0),
			10 => (-x1, -x0, -z1, -z0),
			22 => (-z1, -z0, x0, x1),
			_ => (x0, x1, z0, z1),
		};

		return new Aabb(
			new Vector3(minX + origin.X, local.Position.Y + origin.Y, minZ + origin.Z),
			new Vector3(maxX - minX, local.Size.Y, maxZ - minZ));
	}

	/// <summary>
	/// World-space XZ footprints of every piece currently in a wall GridMap, from each
	/// piece's actual mesh AABB + orientation; corners count as their two arms (see <see cref="Footprints"/>).
	/// </summary>
	public static List<Aabb> Collect(GridMap wallGridMap)
	{
		var boxes = new List<Aabb>();
		MeshLibrary library = wallGridMap?.MeshLibrary;
		if (library == null)
		{
			return boxes;
		}

		foreach (Vector3I cell in wallGridMap.GetUsedCells())
		{
			int tileIndex = wallGridMap.GetCellItem(cell);
			if (tileIndex < 0)
			{
				continue;
			}

			Mesh mesh = library.GetItemMesh(tileIndex);
			if (mesh == null)
			{
				continue;
			}

			boxes.AddRange(Footprints(mesh.GetAabb(), wallGridMap.GetCellItemOrientation(cell), cell, library.GetItemName(tileIndex)));
		}

		return boxes;
	}

	/// <summary>
	/// Finds the first sub-span of an edge not covered by any wall footprint. The edge
	/// runs along X at z=<paramref name="lineCoord"/> when <paramref name="horizontal"/>,
	/// else along Z at x=lineCoord; only boxes actually crossing the edge line count.
	/// </summary>
	public static bool TryGetUncoveredSpan(
		List<Aabb> wallBoxes, bool horizontal, float lineCoord, float spanMin, float spanMax,
		out (float A, float B) uncovered)
	{
		var intervals = CollectEdgeIntervals(wallBoxes, horizontal, lineCoord, spanMin, spanMax);
		float reached = spanMin;
		foreach (var (a, b) in intervals)
		{
			if (a > reached + Eps)
			{
				uncovered = (reached, a);
				return true;
			}

			reached = Mathf.Max(reached, b);
		}

		if (reached < spanMax - Eps)
		{
			uncovered = (reached, spanMax);
			return true;
		}

		uncovered = default;
		return false;
	}

	/// <summary>Non-empty covered intervals along the edge axis, clamped to the span and sorted.</summary>
	private static List<(float A, float B)> CollectEdgeIntervals(
		List<Aabb> wallBoxes, bool horizontal, float lineCoord, float spanMin, float spanMax)
	{
		var intervals = new List<(float A, float B)>();
		foreach (Aabb box in wallBoxes)
		{
			var (perpMin, perpMax) = Across(box, horizontal);
			if (lineCoord < perpMin - Eps || lineCoord > perpMax + Eps)
			{
				continue;
			}

			var (a, b) = Along(box, horizontal);
			a = Mathf.Max(a, spanMin);
			b = Mathf.Min(b, spanMax);
			if (b > a)
			{
				intervals.Add((a, b));
			}
		}

		intervals.Sort((p, q) => p.A.CompareTo(q.A));
		return intervals;
	}
}