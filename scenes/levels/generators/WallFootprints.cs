using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Shared wall-geometry math: real, rotated mesh footprints as the single source of
/// truth for "is this tile edge covered by wall?". Used by the generator's wall pass
/// (to seal every required edge and verify it) and by <see cref="Room"/>'s doorway/
/// connector validation (to ask whether an authored edge is passable).
/// </summary>
public static class WallFootprints
{
	public const float Eps = 0.05f;

	public const int HorizontalWallOrientation = 0;
	public const int HorizontalWallWestHalfOrientation = 10;
	public const int VerticalWallOrientation = 16;
	public const int VerticalWallSouthHalfOrientation = 22;
	public const int NorthWestCornerOrientation = 16;
	public const int NorthEastCornerOrientation = 0;
	public const int SouthWestCornerOrientation = 10;
	public const int SouthEastCornerOrientation = 22;

	public readonly record struct WallStraightRequest(Vector3I Position, int Orientation);

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
	/// Places straight walls, gap-filling half-walls, and corner pieces onto <paramref name="wallGridMap"/>
	/// for every tile in <paramref name="sourceTiles"/> whose cardinal edge satisfies <paramref name="needsWallToward"/>.
	/// Shared between runtime level generation (<see cref="MapGenerator"/>) and standalone room generation
	/// (<see cref="WallStyle"/>). Requests keep their axis orientation (horizontal 0, vertical 16) for gap
	/// filling; <paramref name="pickStraight"/> may place a straight piece at either facing of that axis.
	/// </summary>
	public static void PlaceWalls(
		GridMap wallGridMap,
		int tileSize,
		IEnumerable<(Vector2I Tile, Vector3I BasePosition)> sourceTiles,
		Func<Vector2I, int, int, bool> needsWallToward,
		Func<int> getWallItem,
		Func<int> getHalfWallItem,
		Func<int> getCornerWallItem,
		Func<WallStraightRequest, (int Item, int Orientation)> pickStraight = null)
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

		foreach (var wall in straightWalls)
		{
			if (wallGridMap.GetCellItem(wall.Position) < 0)
			{
				// A positional picker may vary pieces along a run and turn asymmetric ones to face the room;
				// otherwise turn straight walls inward toward the tile that requested them.
				int defaultOrientation = inwardDirections.TryGetValue(wall.Position, out var inward)
					? FacingInto(inward)
					: wall.Orientation;
				var (wallItem, orientation) = pickStraight?.Invoke(wall) ?? (getWallItem(), defaultOrientation);
				if (wallItem >= 0)
				{
					wallGridMap.SetCellItem(wall.Position, wallItem, orientation);
					Mesh mesh = library?.GetItemMesh(wallItem);
					if (mesh != null)
					{
						wallBoxes.Add(FootprintWorld(mesh.GetAabb(), orientation, wall.Position));
					}
				}
			}
		}

		foreach (var wall in straightWalls)
		{
			EnsureEdgeCovered(wallGridMap, tileSize, wall, wallBoxes, getWallItem, getHalfWallItem);
		}

		PlaceWallCorners(wallGridMap, cornerEdges, getCornerWallItem);
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
		Func<int> getCornerWallItem)
	{
		foreach (var (position, edges) in cornerEdges)
		{
			if (edges.HorizontalEast && edges.VerticalSouth)
			{
				PlaceWallCorner(wallGridMap, position, NorthWestCornerOrientation, getCornerWallItem);
			}
			else if (edges.HorizontalWest && edges.VerticalSouth)
			{
				PlaceWallCorner(wallGridMap, position, NorthEastCornerOrientation, getCornerWallItem);
			}
			else if (edges.HorizontalEast && edges.VerticalNorth)
			{
				PlaceWallCorner(wallGridMap, position, SouthWestCornerOrientation, getCornerWallItem);
			}
			else if (edges.HorizontalWest && edges.VerticalNorth)
			{
				PlaceWallCorner(wallGridMap, position, SouthEastCornerOrientation, getCornerWallItem);
			}
		}
	}

	private static void PlaceWallCorner(
		GridMap wallGridMap,
		Vector2I position,
		int orientation,
		Func<int> getCornerWallItem)
	{
		var cellPosition = new Vector3I(position.X, 0, position.Y);
		if (wallGridMap.GetCellItem(cellPosition) < 0)
		{
			int cornerItem = getCornerWallItem();
			if (cornerItem >= 0)
			{
				wallGridMap.SetCellItem(cellPosition, cornerItem, orientation);
			}
		}
	}

	private static void EnsureEdgeCovered(
		GridMap wallGridMap,
		int tileSize,
		WallStraightRequest wall,
		List<Aabb> wallBoxes,
		Func<int> getWallItem,
		Func<int> getHalfWallItem)
	{
		bool horizontal = wall.Orientation == HorizontalWallOrientation;
		float lineCoord = horizontal ? wall.Position.Z : wall.Position.X;
		float spanMin = (horizontal ? wall.Position.X : wall.Position.Z) - tileSize / 2f;
		float spanMax = spanMin + tileSize;

		for (int attempt = 0; attempt < tileSize; attempt++)
		{
			if (!TryGetUncoveredSpan(wallBoxes, horizontal, lineCoord, spanMin, spanMax, out var gap))
			{
				return;
			}

			if (!TryFillEdgeGap(wallGridMap, tileSize, horizontal, lineCoord, gap, wallBoxes, getWallItem, getHalfWallItem))
			{
				break;
			}
		}

		if (TryGetUncoveredSpan(wallBoxes, horizontal, lineCoord, spanMin, spanMax, out var remaining))
		{
			GD.PrintErr($"Wall gap could not be sealed on the {(horizontal ? "horizontal" : "vertical")} edge at "
				+ $"{(horizontal ? "z" : "x")}={lineCoord}, span [{remaining.A:F1},{remaining.B:F1}] near {wall.Position}: "
				+ "no free cell on the edge row fits a covering wall piece.");
		}
	}

	private static bool TryFillEdgeGap(
		GridMap wallGridMap,
		int tileSize,
		bool horizontal,
		float lineCoord,
		(float A, float B) gap,
		List<Aabb> wallBoxes,
		Func<int> getWallItem,
		Func<int> getHalfWallItem)
	{
		MeshLibrary library = wallGridMap.MeshLibrary;
		if (library == null)
		{
			return false;
		}

		int fullItem = getWallItem();
		int halfItem = getHalfWallItem();
		Mesh fullMesh = fullItem >= 0 ? library.GetItemMesh(fullItem) : null;
		Mesh halfMesh = halfItem >= 0 ? library.GetItemMesh(halfItem) : null;
		Aabb fullAabb = fullMesh?.GetAabb() ?? default;
		Aabb halfAabb = halfMesh?.GetAabb() ?? default;

		ReadOnlySpan<(int Item, int Orientation, Aabb LocalAabb, bool Valid)> candidates = horizontal
			? [
				(fullItem, HorizontalWallOrientation, fullAabb, fullMesh != null),
				(halfItem, HorizontalWallOrientation, halfAabb, halfMesh != null),
				(halfItem, HorizontalWallWestHalfOrientation, halfAabb, halfMesh != null),
			]
			: [
				(fullItem, VerticalWallOrientation, fullAabb, fullMesh != null),
				(halfItem, VerticalWallOrientation, halfAabb, halfMesh != null),
				(halfItem, VerticalWallSouthHalfOrientation, halfAabb, halfMesh != null),
			];

		float bestOverlap = Eps;
		Vector3I bestCell = default;
		(int Item, int Orientation) bestCandidate = default;
		Aabb bestFootprint = default;

		int gapCenter = Mathf.RoundToInt((gap.A + gap.B) / 2f);
		for (int offset = -tileSize / 2; offset <= tileSize / 2; offset++)
		{
			var cell = horizontal
				? new Vector3I(gapCenter + offset, 0, Mathf.RoundToInt(lineCoord))
				: new Vector3I(Mathf.RoundToInt(lineCoord), 0, gapCenter + offset);
			if (wallGridMap.GetCellItem(cell) >= 0)
			{
				continue;
			}

			foreach (var candidate in candidates)
			{
				if (!candidate.Valid)
				{
					continue;
				}

				Aabb footprint = FootprintWorld(candidate.LocalAabb, candidate.Orientation, cell);
				float perpMin = horizontal ? footprint.Position.Z : footprint.Position.X;
				float perpMax = horizontal ? footprint.End.Z : footprint.End.X;
				if (lineCoord < perpMin - Eps || lineCoord > perpMax + Eps)
				{
					continue;
				}

				float a = horizontal ? footprint.Position.X : footprint.Position.Z;
				float b = horizontal ? footprint.End.X : footprint.End.Z;
				float overlap = Mathf.Min(b, gap.B) - Mathf.Max(a, gap.A);
				if (overlap > bestOverlap)
				{
					bestOverlap = overlap;
					bestCell = cell;
					bestCandidate = (candidate.Item, candidate.Orientation);
					bestFootprint = footprint;
				}
			}
		}

		if (bestOverlap <= Eps)
		{
			return false;
		}

		wallGridMap.SetCellItem(bestCell, bestCandidate.Item, bestCandidate.Orientation);
		wallBoxes.Add(bestFootprint);
		return true;
	}

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
	/// piece's actual mesh AABB + orientation.
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

			boxes.Add(FootprintWorld(mesh.GetAabb(), wallGridMap.GetCellItemOrientation(cell), cell));
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

	/// <summary>
	/// The largest contiguous uncovered stretch of an edge, in world units. 0 means the
	/// edge is fully walled; a value of at least half a tile means something can walk
	/// through the hole.
	/// </summary>
	public static float MaxUncoveredGap(
		List<Aabb> wallBoxes, bool horizontal, float lineCoord, float spanMin, float spanMax)
	{
		var intervals = CollectEdgeIntervals(wallBoxes, horizontal, lineCoord, spanMin, spanMax);
		float reached = spanMin;
		float maxGap = 0f;
		foreach (var (a, b) in intervals)
		{
			if (a > reached)
			{
				maxGap = Mathf.Max(maxGap, a - reached);
			}

			reached = Mathf.Max(reached, b);
		}

		return Mathf.Max(maxGap, spanMax - reached);
	}

	/// <summary>Non-empty covered intervals along the edge axis, clamped to the span and sorted.</summary>
	private static List<(float A, float B)> CollectEdgeIntervals(
		List<Aabb> wallBoxes, bool horizontal, float lineCoord, float spanMin, float spanMax)
	{
		var intervals = new List<(float A, float B)>();
		foreach (Aabb box in wallBoxes)
		{
			float perpMin = horizontal ? box.Position.Z : box.Position.X;
			float perpMax = horizontal ? box.End.Z : box.End.X;
			if (lineCoord < perpMin - Eps || lineCoord > perpMax + Eps)
			{
				continue;
			}

			float a = horizontal ? box.Position.X : box.Position.Z;
			float b = horizontal ? box.End.X : box.End.Z;
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