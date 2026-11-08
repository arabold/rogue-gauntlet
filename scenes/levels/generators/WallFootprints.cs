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
	/// <summary>Only export rounding is allowed between distinct pieces.</summary>
	public const float FitTolerance = 0.001f;

	public const int NorthWestCornerOrientation = 16;
	public const int NorthEastCornerOrientation = 0;
	public const int SouthWestCornerOrientation = 10;
	public const int SouthEastCornerOrientation = 22;

	/// <summary>Bounds in the GridMap item's local coordinates, including the library's mesh transform.</summary>
	public static Aabb MeshBounds(MeshLibrary library, int item) => library.GetItemMeshTransform(item) * library.GetItemMesh(item).GetAabb();

	/// <summary>GridMap orientation that turns local +Z toward <paramref name="inward"/>.</summary>
	public static int FacingInto(Vector2I inward) =>
		inward == Vector2I.Down ? GridTurns.Turn0
		: inward == Vector2I.Up ? GridTurns.Turn180
		: inward == Vector2I.Right ? GridTurns.Turn90
		: GridTurns.Turn270;

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

			foreach (var box in PieceFootprints(MeshBounds(wallGridMap.MeshLibrary, item), wallGridMap.GetCellItemOrientation(cell), cell, wallGridMap.MeshLibrary.GetItemName(item)))
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

	/// <summary>
	/// World XZ boxes of a piece at <paramref name="cell"/>: its footprint, or for an L-shaped corner (at least half a
	/// tile deep along both axes) its two arms, each as thick as its wall, so the inside of the L stays open.
	/// Junction assets must conform to these structural regions; their meshes are responsible for clean internal geometry.
	/// </summary>
	public static IEnumerable<Aabb> PieceFootprints(Aabb local, int orientation, Vector3I cell, string itemName)
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
		if (itemName.StartsWith("wall_Tsplit") || itemName == "wall_crossing")
		{
			float thickness = itemName == "wall_crossing" ? .5f : Mathf.Min(halfDepthX, halfDepthZ);
			halfDepthX = thickness;
			halfDepthZ = thickness;
		}
		yield return new Aabb(new Vector3(footprint.Position.X, footprint.Position.Y, cell.Z - halfDepthZ), new Vector3(footprint.Size.X, footprint.Size.Y, 2 * halfDepthZ));
		yield return new Aabb(new Vector3(cell.X - halfDepthX, footprint.Position.Y, footprint.Position.Z), new Vector3(2 * halfDepthX, footprint.Size.Y, footprint.Size.Z));
	}

	/// <summary>Whether two pieces penetrate in XZ. Regions of one compound piece may overlap each other;
	/// only intersections between different pieces are relevant. Export rounding within FitTolerance is accepted.</summary>
	public static bool PiecesOverlap(MeshLibrary library, int firstItem, int firstOrientation, Vector3I firstCell,
		int secondItem, int secondOrientation, Vector3I secondCell)
	{
		var firstMesh = MeshBounds(library, firstItem);
		var secondMesh = MeshBounds(library, secondItem);
		if (!Overlaps(FootprintWorld(firstMesh, firstOrientation, firstCell), FootprintWorld(secondMesh, secondOrientation, secondCell)))
			return false;
		var first = PieceFootprints(firstMesh, firstOrientation, firstCell, library.GetItemName(firstItem));
		var second = PieceFootprints(secondMesh, secondOrientation, secondCell, library.GetItemName(secondItem));
		foreach (var a in first)
			foreach (var b in second)
				if (Overlaps(a, b)) return true;
		return false;
	}

	private static bool Overlaps(Aabb a, Aabb b) =>
		Mathf.Min(a.End.X, b.End.X) - Mathf.Max(a.Position.X, b.Position.X) > FitTolerance
		&& Mathf.Min(a.End.Z, b.End.Z) - Mathf.Max(a.Position.Z, b.Position.Z) > FitTolerance;

	/// <summary>Checks distinct pieces of a candidate grid before the room is accepted.</summary>
	public static string FindOverlap(GridMap grid)
	{
		var pieces = new List<(Vector3I Cell, int Item, Aabb Bounds, List<Aabb> Regions)>();
		var library = grid.MeshLibrary;
		foreach (Vector3I cell in grid.GetUsedCells())
		{
			int item = grid.GetCellItem(cell), orientation = grid.GetCellItemOrientation(cell);
			var local = MeshBounds(library, item);
			pieces.Add((cell, item, FootprintWorld(local, orientation, cell),
				new List<Aabb>(PieceFootprints(local, orientation, cell, library.GetItemName(item)))));
		}
		for (int i = 0; i < pieces.Count; i++)
		for (int j = i + 1; j < pieces.Count; j++)
		{
			var a = pieces[i]; var b = pieces[j];
			if (!Overlaps(a.Bounds, b.Bounds)) continue;
			foreach (var first in a.Regions)
			foreach (var second in b.Regions)
				if (Overlaps(first, second))
					return $"{library.GetItemName(a.Item)} at {a.Cell} overlaps {library.GetItemName(b.Item)} at {b.Cell}";
		}
		return "";
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
	/// piece's actual mesh AABB + orientation; corners count as their two arms (see <see cref="PieceFootprints"/>).
	/// <paramref name="solidOnly"/> skips visual-only pieces without collision, such as doorway frames.
	/// </summary>
	public static List<Aabb> Collect(GridMap wallGridMap, bool solidOnly = false)
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
			if (mesh == null || (solidOnly && library.GetItemShapes(tileIndex).Count == 0))
			{
				continue;
			}

			boxes.AddRange(PieceFootprints(MeshBounds(library, tileIndex), wallGridMap.GetCellItemOrientation(cell), cell, library.GetItemName(tileIndex)));
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