using System.Collections.Generic;
using Godot;

/// <summary>
/// Shared wall-geometry math: real, rotated mesh footprints as the single source of
/// truth for "is this tile edge covered by wall?". Used by the generator's wall pass
/// (to seal every required edge and verify it) and by <see cref="Room"/>'s doorway/
/// connector validation (to ask whether an authored edge is passable). Judging edges by
/// footprints instead of which cells hold anchors is what makes both rotation-invariant:
/// a rotated mesh's footprint rotates exactly with it, while cell-anchor sampling of a
/// half-open edge row flips corner anchors in and out of range depending on orientation.
/// </summary>
public static class WallFootprints
{
	public const float Eps = 0.05f;

	/// <summary>
	/// World XZ footprint of a wall cell. Wall pieces use only the upright Y-rotation
	/// orientations {0:0°, 16:90°, 10:180°, 22:270°}; the GridMaps use cell_center=false,
	/// so the mesh origin sits at the cell coordinate.
	/// </summary>
	public static Aabb FootprintWorld(Aabb local, int orientation, Vector3I origin)
	{
		int quarterTurns = orientation switch { 16 => 1, 10 => 2, 22 => 3, _ => 0 };
		float x0 = local.Position.X, x1 = local.End.X, z0 = local.Position.Z, z1 = local.End.Z;
		var corners = new (float X, float Z)[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) };
		float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
		foreach (var (px, pz) in corners)
		{
			(float rx, float rz) = quarterTurns switch
			{
				1 => (pz, -px),
				2 => (-px, -pz),
				3 => (-pz, px),
				_ => (px, pz),
			};
			minX = Mathf.Min(minX, rx);
			maxX = Mathf.Max(maxX, rx);
			minZ = Mathf.Min(minZ, rz);
			maxZ = Mathf.Max(maxZ, rz);
		}

		return new Aabb(
			new Vector3(minX + origin.X, local.Position.Y + origin.Y, minZ + origin.Z),
			new Vector3(maxX - minX, local.Size.Y, maxZ - minZ));
	}

	/// <summary>
	/// World-space XZ footprints of every piece currently in a wall GridMap, from each
	/// piece's actual mesh AABB + orientation (never guessed from its tile index, so
	/// decorative corner/half variants are handled correctly).
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
