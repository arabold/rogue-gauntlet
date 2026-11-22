using System;
using System.Collections.Generic;
using Godot;
using static WallFootprints;

/// <summary>Fits a room or floor wall graph into one non-overlapping candidate GridMap.</summary>
public static class BoundaryWallBuilder
{
	/// <summary>The six structural wall item names a wall family supplies for boundaries, corners, and junctions.</summary>
	public readonly record struct WallPieces(string Straight, string Half, string Short, string Corner, string TSplit, string Crossing)
	{
		public static readonly WallPieces Default = new("wall", "wall_half", "wall_junction_short", "wall_corner", "wall_Tsplit", "wall_crossing");
	}

	/// <summary>
	/// Resolves a room or floor boundary as a graph. Junction pieces own their vertex and the adjacent half of each
	/// incident edge; the remaining half or full edge is then tiled exactly once. The grid is replaced only after the
	/// complete candidate has been built, so a conflicting divider returns false without changing the grid.
	/// </summary>
	public static bool Build(GridMap grid, IEnumerable<(Vector2I Tile, Vector3I Center)> tiles,
		Func<Vector2I, int, int, bool> needsWall, Func<Vector3I, bool, int> pickStraight = null,
		IReadOnlySet<Vector3I> dividerCells = null, IReadOnlySet<(Vector3I Center, bool Horizontal)> framedEdges = null,
		WallPieces? structural = null)
	{
		const int west = 1, east = 2, north = 4, south = 8;
		var edges = new Dictionary<(Vector3I Cell, bool Horizontal), Vector2I>();
		var partitions = new HashSet<(Vector3I Cell, bool Horizontal)>();
		var vertices = new Dictionary<Vector2I, int>();
		void AddVertex(Vector2I vertex, int direction) => vertices[vertex] = vertices.GetValueOrDefault(vertex) | direction;
		void AddEdge(Vector3I center, bool horizontal, Vector2I inward)
		{
			var key = (center, horizontal);
			if (!edges.TryAdd(key, inward))
			{
				partitions.Add(key);
				return;
			}
			int half = Room.TileSize / 2;
			var start = new Vector2I(center.X - (horizontal ? half : 0), center.Z - (horizontal ? 0 : half));
			var end = new Vector2I(center.X + (horizontal ? half : 0), center.Z + (horizontal ? 0 : half));
			AddVertex(start, horizontal ? east : south);
			AddVertex(end, horizontal ? west : north);
		}
		int distance = Room.TileSize / 2;
		foreach (var (tile, center) in tiles)
		{
			if (needsWall(tile, 0, -1)) AddEdge(center + Vector3I.Forward * distance, true, Vector2I.Down);
			if (needsWall(tile, 0, 1)) AddEdge(center + Vector3I.Back * distance, true, Vector2I.Up);
			if (needsWall(tile, -1, 0)) AddEdge(center + Vector3I.Left * distance, false, Vector2I.Right);
			if (needsWall(tile, 1, 0)) AddEdge(center + Vector3I.Right * distance, false, Vector2I.Left);
		}

		// A frame occupies its entire edge. A perpendicular wall must stop at the frame face, half a unit
		// away from the endpoint. Collinear continuations still meet the frame at the endpoint itself.
		var frameAxes = new Dictionary<Vector2I, int>();
		if (framedEdges != null)
		foreach (var (center, horizontal) in framedEdges)
		{
			var step = horizontal ? new Vector2I(distance, 0) : new Vector2I(0, distance);
			var middle = new Vector2I(center.X, center.Z);
			foreach (var vertex in new[] { middle - step, middle + step })
				frameAxes[vertex] = frameAxes.GetValueOrDefault(vertex) | (horizontal ? 1 : 2);
		}
		foreach (var (vertex, axes) in frameAxes)
			if (axes == 3) throw new InvalidOperationException($"Perpendicular doorway frames overlap at {vertex}.");

		var library = grid.MeshLibrary;
		var set = structural ?? WallPieces.Default;
		var itemCache = new Dictionary<int, (Aabb Bounds, string Name)>();
		int straight = library.FindItemByName(set.Straight), halfWall = library.FindItemByName(set.Half);
		int shortWall = library.FindItemByName(set.Short);
		int corner = library.FindItemByName(set.Corner), tee = library.FindItemByName(set.TSplit);
		int crossing = library.FindItemByName(set.Crossing);
		if (straight < 0 || halfWall < 0 || shortWall < 0 || corner < 0 || tee < 0 || crossing < 0)
			throw new InvalidOperationException(
				$"Floor walls require {set.Straight}, {set.Half}, {set.Short}, {set.Corner}, {set.TSplit} and {set.Crossing}.");
		var pieces = new Dictionary<Vector3I, (int Item, int Orientation)>();
		var variants = new List<(Vector3I Cell, int Item, int Orientation)>();
		var junctions = new HashSet<Vector2I>();
		foreach (var (vertex, directions) in vertices)
		{
			if (frameAxes.ContainsKey(vertex)) continue;
			int item = corner;
			int orientation = directions switch
			{
				east | south => NorthWestCornerOrientation,
				west | south => NorthEastCornerOrientation,
				east | north => SouthWestCornerOrientation,
				west | north => SouthEastCornerOrientation,
				west | east | south => GridTurns.Turn0,
				west | east | north => GridTurns.Turn180,
				north | south | east => GridTurns.Turn90,
				north | south | west => GridTurns.Turn270,
				west | east | north | south => GridTurns.Turn0,
				_ => -1,
			};
			if (orientation < 0) continue;
			if (directions == (west | east | north | south)) item = crossing;
			else if (directions == (west | east | south) || directions == (west | east | north)
				|| directions == (north | south | east) || directions == (north | south | west)) item = tee;
			pieces.Add(new Vector3I(vertex.X, 0, vertex.Y), (item, orientation));
			junctions.Add(vertex);
		}
		foreach (var ((cell, horizontal), inward) in edges)
		{
			var start = new Vector2I(cell.X - (horizontal ? distance : 0), cell.Z - (horizontal ? 0 : distance));
			var end = new Vector2I(cell.X + (horizontal ? distance : 0), cell.Z + (horizontal ? 0 : distance));
			bool hasStart = junctions.Contains(start), hasEnd = junctions.Contains(end);
			bool trimStart = (frameAxes.GetValueOrDefault(start) & (horizontal ? 2 : 1)) != 0;
			bool trimEnd = (frameAxes.GetValueOrDefault(end) & (horizontal ? 2 : 1)) != 0;
			if (hasStart && hasEnd) continue;
			if (!hasStart && !hasEnd && !trimStart && !trimEnd)
			{
				int item = grid.GetCellItem(cell), orientation = item >= 0 ? grid.GetCellItemOrientation(cell) : FacingInto(inward);
				if (item < 0 && pickStraight != null) item = pickStraight(cell, partitions.Contains((cell, horizontal)));
				if (item >= 0 && library.GetItemMesh(item) is { })
				{
					var bounds = FootprintWorld(GetItemInfo(library, item, itemCache).Bounds, orientation, cell);
					var (min, max) = Along(bounds, horizontal);
					var (near, far) = Across(bounds, horizontal);
					float target = horizontal ? start.X : start.Y;
					float line = horizontal ? cell.Z : cell.X;
					// Decorative faces may extend past the standard wall thickness. Preserve the selected variant
					// only if its structural span fits; actual overlaps are checked after the whole boundary is known.
					if (Mathf.Abs(min - target) <= FitTolerance && Mathf.Abs(max - target - Room.TileSize) <= FitTolerance
						&& (dividerCells == null || !dividerCells.Contains(cell)))
					{
						if (Mathf.Abs(near - (line - .5f)) <= FitTolerance && Mathf.Abs(far - (line + .5f)) <= FitTolerance)
						{
							pieces.Add(cell, (item, orientation));
							continue;
						}
						if (near < line && far > line) variants.Add((cell, item, orientation));
					}
				}
				pieces.Add(cell, (straight, FacingInto(inward)));
				continue;
			}
			// Each half-edge is either owned by a junction, a standard two-unit half wall, or a
			// 1.5-unit wall stopping against a doorway frame. Its mesh origin stays on the integer lattice.
			float edgeStart = horizontal ? start.X : start.Y;
			void PlaceHalf(float targetMin, bool trimmed)
			{
				int wall = trimmed ? shortWall : halfWall;
				var wallBounds = GetItemInfo(library, wall, itemCache).Bounds;
				foreach (int facing in (ReadOnlySpan<int>)[FacingInto(inward), FacingInto(-inward)])
				{
					var bounds = FootprintWorld(wallBounds, facing, Vector3I.Zero);
					float origin = targetMin - Along(bounds, horizontal).Min;
					int along = Mathf.RoundToInt(origin);
					if (Mathf.Abs(along - origin) > FitTolerance) continue;
					var at = horizontal ? new Vector3I(along, 0, cell.Z) : new Vector3I(cell.X, 0, along);
					if (pieces.TryAdd(at, (wall, facing))) return;
				}
				throw new InvalidOperationException($"Cannot fit {(trimmed ? "short" : "half")} wall on edge {cell}.");
			}
			if (!hasStart) PlaceHalf(edgeStart + (trimStart ? .5f : 0), trimStart);
			if (!hasEnd) PlaceHalf(edgeStart + distance, trimEnd);
		}
		if ((dividerCells != null && dividerCells.Count > 0) || variants.Count > 0)
		{
			var boundsByCell = new Dictionary<Vector3I, Aabb>(pieces.Count);
			var buckets = new Dictionary<Vector2I, List<Vector3I>>();
			var seen = new HashSet<Vector3I>();
			foreach (var (cell, piece) in pieces)
			{
				var bounds = FootprintWorld(GetItemInfo(library, piece.Item, itemCache).Bounds, piece.Orientation, cell);
				boundsByCell[cell] = bounds;
				AddToBuckets(buckets, bounds, cell);
			}
			// Preserve room-owned dividers as explicit structural placements, not mesh-name conventions.
			if (dividerCells != null)
			foreach (var cell in dividerCells)
			{
				int item = grid.GetCellItem(cell), orientation = grid.GetCellItemOrientation(cell);
				if (item < 0) throw new InvalidOperationException($"Missing room divider at {cell}.");
				var (divLocal, divName) = GetItemInfo(library, item, itemCache);
				var divBounds = FootprintWorld(divLocal, orientation, cell);
				foreach (var otherCell in QueryBuckets(buckets, divBounds, seen))
				{
					if (otherCell == cell) continue;
					var other = pieces[otherCell];
					var (otherLocal, otherName) = GetItemInfo(library, other.Item, itemCache);
					if (PiecesOverlap(divLocal, divName, orientation, cell, otherLocal, otherName, other.Orientation, otherCell))
						return false;
				}
				pieces[cell] = (item, orientation);
				boundsByCell[cell] = divBounds;
				AddToBuckets(buckets, divBounds, cell);
			}
			if (variants.Count > 0)
			{
				// Test each selected style against the resolved boundary, dividers and full doorway footprints.
				var frameBounds = new List<(Vector3I Center, int Orientation, Aabb Bounds)>();
				var frameBuckets = new Dictionary<Vector2I, List<int>>();
				var seenFrames = new HashSet<int>();
				var (straightLocal, straightName) = GetItemInfo(library, straight, itemCache);
				if (framedEdges != null)
				foreach (var (center, horizontal) in framedEdges)
				{
					int facing = horizontal ? GridTurns.Turn0 : GridTurns.Turn90;
					var bounds = FootprintWorld(straightLocal, facing, center);
					AddToBuckets(frameBuckets, bounds, frameBounds.Count);
					frameBounds.Add((center, facing, bounds));
				}
				foreach (var (cell, item, orientation) in variants)
				{
					var (local, name) = GetItemInfo(library, item, itemCache);
					var bounds = FootprintWorld(local, orientation, cell);
					bool fits = true;
					foreach (var otherCell in QueryBuckets(buckets, bounds, seen))
					{
						if (otherCell == cell) continue;
						var other = pieces[otherCell];
						var (otherLocal, otherName) = GetItemInfo(library, other.Item, itemCache);
						if (Overlaps(bounds, boundsByCell[otherCell])
							&& PiecesOverlap(local, name, orientation, cell, otherLocal, otherName, other.Orientation, otherCell))
						{ fits = false; break; }
					}
					if (fits && frameBounds.Count > 0)
						foreach (int f in QueryBuckets(frameBuckets, bounds, seenFrames))
						{
							var (center, facing, frame) = frameBounds[f];
							if (Overlaps(bounds, frame) && PiecesOverlap(local, name, orientation, cell, straightLocal, straightName, facing, center))
							{ fits = false; break; }
						}
					if (fits)
					{
						pieces[cell] = (item, orientation);
						boundsByCell[cell] = bounds;
						AddToBuckets(buckets, bounds, cell);
					}
				}
			}
		}
		grid.Clear();
		foreach (var (cell, piece) in pieces) grid.SetCellItem(cell, piece.Item, piece.Orientation);
		return true;
	}
}