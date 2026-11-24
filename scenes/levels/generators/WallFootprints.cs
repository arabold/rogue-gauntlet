using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

	/// <summary>Bottom of the band wall decor hangs in: base rubble, roots and plinths below it never push decor out.</summary>
	private const float MountBandBottom = .7f;

	private static readonly ConditionalWeakTable<MeshLibrary, ConcurrentDictionary<int, Aabb>> MountBandBounds = new();
	private static readonly ConditionalWeakTable<MeshLibrary, ConcurrentDictionary<int, (Vector2 From, Vector2 To)[]>> DeclaredMounts = new();
	private static readonly StringName Extras = "extras";
	private const string MountSpansKey = "mount_spans";

	/// <summary>
	/// Where wall decor may hang on the item, as segments of its wall line in local XZ: the model's own
	/// <c>mount_spans</c> extras (the Asset Studio exports them for walls whose face carries posts or frames). Null when the
	/// model declares none, so decor may hang anywhere along it. Read once per library item.
	/// </summary>
	private static (Vector2 From, Vector2 To)[] MountSpans(MeshLibrary library, int item) =>
		DeclaredMounts.GetOrCreateValue(library).GetOrAdd(item, static (id, lib) => ReadMountSpans(lib, id), library);

	private static (Vector2 From, Vector2 To)[] ReadMountSpans(MeshLibrary library, int item)
	{
		Mesh mesh = library.GetItemMesh(item);
		if (mesh == null || !mesh.HasMeta(Extras) || !mesh.GetMeta(Extras).AsGodotDictionary().TryGetValue(MountSpansKey, out var value))
			return null;
		var spans = value.AsGodotArray();
		var result = new (Vector2 From, Vector2 To)[spans.Count];
		for (int i = 0; i < spans.Count; i++)
		{
			var span = spans[i].AsGodotArray();
			result[i] = (new Vector2(span[0].AsSingle(), span[1].AsSingle()), new Vector2(span[2].AsSingle(), span[3].AsSingle()));
		}
		return result;
	}

	/// <summary>
	/// Bounds of the item's mesh vertices at or above <see cref="MountBandBottom"/>, where wall decor hangs. Scanning the
	/// vertices is costly, so each library item is read once and kept for as long as its library lives; wall libraries
	/// do not change at runtime.
	/// </summary>
	public static Aabb UpperMeshBounds(MeshLibrary library, int item) =>
		MountBandBounds.GetOrCreateValue(library).GetOrAdd(item, static (id, lib) => ReadUpperMeshBounds(lib, id), library);

	private static Aabb ReadUpperMeshBounds(MeshLibrary library, int item)
	{
		Mesh mesh = library.GetItemMesh(item);
		if (mesh == null) return default;
		Transform3D meshTransform = library.GetItemMeshTransform(item);
		Aabb? bounds = null;
		for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			var arrays = mesh.SurfaceGetArrays(surface);
			if (arrays.Count <= (int)Mesh.ArrayType.Vertex) continue;
			var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			for (int i = 0; i < verts.Length; i++)
			{
				Vector3 p = meshTransform * verts[i];
				if (p.Y < MountBandBottom) continue;
				bounds = bounds?.Expand(p) ?? new Aabb(p, Vector3.Zero);
			}
		}
		return bounds ?? (meshTransform * mesh.GetAabb());
	}

	private static (Aabb Bounds, Aabb UpperBounds, string Name) ReadWallItemInfo(MeshLibrary library, int item)
	{
		Mesh mesh = library.GetItemMesh(item);
		if (mesh == null) return (default, default, null);
		return (library.GetItemMeshTransform(item) * mesh.GetAabb(), UpperMeshBounds(library, item), library.GetItemName(item));
	}

	/// <summary>Cached item mesh bounds and name within one validation or build pass.</summary>
	internal static (Aabb Bounds, string Name) GetItemInfo(MeshLibrary library, int item, Dictionary<int, (Aabb Bounds, string Name)> cache)
	{
		if (!cache.TryGetValue(item, out var info))
			cache[item] = info = (MeshBounds(library, item), library.GetItemName(item));
		return info;
	}

	internal static (Aabb Bounds, Aabb UpperBounds, string Name) GetWallItemInfo(
		MeshLibrary library, int item, Dictionary<int, (Aabb Bounds, Aabb UpperBounds, string Name)> cache)
	{
		if (cache == null) return ReadWallItemInfo(library, item);
		if (!cache.TryGetValue(item, out var info))
			cache[item] = info = ReadWallItemInfo(library, item);
		return info;
	}

	internal static void AddToBuckets<T>(Dictionary<Vector2I, List<T>> buckets, Aabb bounds, T entry)
	{
		int minX = Mathf.FloorToInt((bounds.Position.X - FitTolerance) / Room.TileSize);
		int maxX = Mathf.FloorToInt((bounds.End.X + FitTolerance) / Room.TileSize);
		int minZ = Mathf.FloorToInt((bounds.Position.Z - FitTolerance) / Room.TileSize);
		int maxZ = Mathf.FloorToInt((bounds.End.Z + FitTolerance) / Room.TileSize);
		for (int bx = minX; bx <= maxX; bx++)
		for (int bz = minZ; bz <= maxZ; bz++)
		{
			var key = new Vector2I(bx, bz);
			if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = [];
			list.Add(entry);
		}
	}

	internal static IEnumerable<T> QueryBuckets<T>(Dictionary<Vector2I, List<T>> buckets, Aabb bounds, HashSet<T> seen)
	{
		seen.Clear();
		int minX = Mathf.FloorToInt((bounds.Position.X - FitTolerance) / Room.TileSize);
		int maxX = Mathf.FloorToInt((bounds.End.X + FitTolerance) / Room.TileSize);
		int minZ = Mathf.FloorToInt((bounds.Position.Z - FitTolerance) / Room.TileSize);
		int maxZ = Mathf.FloorToInt((bounds.End.Z + FitTolerance) / Room.TileSize);
		for (int bx = minX; bx <= maxX; bx++)
		for (int bz = minZ; bz <= maxZ; bz++)
		{
			if (!buckets.TryGetValue(new Vector2I(bx, bz), out var list)) continue;
			foreach (var item in list)
				if (seen.Add(item)) yield return item;
		}
	}

	/// <summary>GridMap orientation that turns local +Z toward <paramref name="inward"/>.</summary>
	public static int FacingInto(Vector2I inward) =>
		inward == Vector2I.Down ? GridTurns.Turn0
		: inward == Vector2I.Up ? GridTurns.Turn180
		: inward == Vector2I.Right ? GridTurns.Turn90
		: GridTurns.Turn270;

	/// <summary>One or two stack-allocated XZ boxes for a wall piece without heap allocation on hot paths.</summary>
	internal readonly record struct FootprintPair(Aabb First, Aabb Second, bool HasSecond);

	/// <summary>One tile edge as its wall pieces cover it. <see cref="Item"/> is the edge's own piece at
	/// <see cref="Cell"/> (a straight, or a half beside a corner), -1 where corner arms alone cover it. <see cref="Mounts"/>
	/// are the stretches of the edge, from its -X/-Z end, where wall decor may hang.</summary>
	public readonly record struct WallEdge(bool Covered, float Depth, float MountDepth, int Item, Vector3I Cell, MountSpan[] Mounts)
	{
		/// <summary>The edge as props meet it: decor hangs within its mounts only where <paramref name="decoratable"/>.</summary>
		public WallFace Face(bool decoratable) => new(Depth, MountDepth, decoratable ? Mounts : []);
	}

	/// <summary>
	/// Measures the tile edge centered on <paramref name="edgeCell"/> from the pieces running along it: whether they seal
	/// it, how far they reach from the wall line toward <paramref name="inward"/> (a corner by its arm, not its whole L),
	/// how far their upper mounting band reaches, the edge's own piece, and where along it decor may hang: each piece's
	/// declared <see cref="MountSpans"/> on this line, and the stretch it covers when it declares none. An edge whose
	/// pieces declare nothing is open end to end, like the plain walls it is built from. Walls crossing the edge's ends
	/// share only their thickness with it and do not count.
	/// </summary>
	public static WallEdge MeasureEdge(GridMap wallGridMap, Vector3I edgeCell, Vector2I inward, int tileSize) =>
		MeasureEdge(wallGridMap, edgeCell, inward, tileSize, null);

	internal static WallEdge MeasureEdge(
		GridMap wallGridMap,
		Vector3I edgeCell,
		Vector2I inward,
		int tileSize,
		Dictionary<int, (Aabb Bounds, Aabb UpperBounds, string Name)> itemCache)
	{
		bool horizontal = inward.X == 0;
		var (line, min, max) = EdgeSpan(edgeCell, horizontal, tileSize);
		var step = horizontal ? Vector3I.Right : Vector3I.Back;
		var covering = new List<Aabb>();
		var mounts = new List<MountSpan>();
		bool declaring = false;
		float depth = 0;
		float mountDepth = 0;
		var own = (Item: -1, Cell: Vector3I.Zero);
		var library = wallGridMap.MeshLibrary;
		// Pieces on the edge sit on its line within a tile of its center; the center goes first.
		for (int index = 0; index <= 2 * tileSize; index++)
		{
			var cell = edgeCell + step * ((index + 1) / 2 * (index % 2 == 0 ? -1 : 1));
			int item = wallGridMap.GetCellItem(cell);
			if (item < 0) continue;
			var (meshBounds, upperBounds, name) = GetWallItemInfo(library, item, itemCache);
			if (name == null) continue;

			int orientation = wallGridMap.GetCellItemOrientation(cell);
			var declared = MountSpans(library, item);
			var pair = PieceFootprintPair(meshBounds, orientation, cell, name);
			var upperPair = PieceFootprintPair(upperBounds, orientation, cell, name);
			bool counts = CheckBox(pair.First, upperPair.First);
			if (pair.HasSecond) counts |= CheckBox(pair.Second, upperPair.HasSecond ? upperPair.Second : upperPair.First);
			if (counts && declared != null)
			{
				declaring = true;
				// A junction's arms across this edge are off its line and drop out.
				foreach (var (from, to) in declared)
				{
					var span = FootprintWorld(new Aabb(new Vector3(from.X, 0, from.Y), Vector3.Zero).Expand(new Vector3(to.X, 0, to.Y)), orientation, cell);
					var (near, far) = Across(span, horizontal);
					var (start, end) = Along(span, horizontal);
					if (Mathf.Abs(near - line) <= Eps && Mathf.Abs(far - line) <= Eps) AddMount(start, end);
				}
			}

			bool CheckBox(Aabb box, Aabb upperBox)
			{
				var (a, b) = Along(box, horizontal);
				if (Mathf.Min(b, max) - Mathf.Max(a, min) <= tileSize / 4f)
				{
					return false;
				}

				covering.Add(box);
				bool positiveInward = (horizontal ? inward.Y : inward.X) > 0;
				var (near, far) = Across(box, horizontal);
				depth = Mathf.Max(depth, positiveInward ? far - line : line - near);
				var (uNear, uFar) = Across(upperBox, horizontal);
				mountDepth = Mathf.Max(mountDepth, positiveInward ? uFar - line : line - uNear);
				if (own.Item < 0 && a >= min - Eps && b <= max + Eps)
				{
					own = (item, cell);
				}
				if (declared == null) AddMount(a, b);
				return true;
			}
		}

		void AddMount(float a, float b)
		{
			float from = Mathf.Max(Mathf.Min(a, b), min), to = Mathf.Min(Mathf.Max(a, b), max);
			if (to - from > Eps) mounts.Add(new(from - min, to - min));
		}

		if (covering.Count == 0)
		{
			return new(false, .5f, .5f, -1, default, []);
		}

		if (mountDepth <= 0f) mountDepth = depth;
		return new(!TryGetUncoveredSpan(covering, horizontal, line, min, max, out _), depth, mountDepth, own.Item, own.Cell,
			declaring ? MountSpan.Join(mounts) : [new(0, tileSize)]);
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
	internal static FootprintPair PieceFootprintPair(Aabb local, int orientation, Vector3I cell, string itemName) =>
		PieceFootprintPair(FootprintWorld(local, orientation, cell), cell, itemName);

	/// <summary>Splits the world bounds of a junction around its placement origin, including off-grid wall pieces.</summary>
	internal static FootprintPair PieceFootprintPair(Aabb footprint, Vector3 origin, string itemName)
	{
		if (footprint.Size.X < Room.TileSize / 2f || footprint.Size.Z < Room.TileSize / 2f)
		{
			return new FootprintPair(footprint, default, false);
		}

		float halfDepthZ = Mathf.Min(origin.Z - footprint.Position.Z, footprint.End.Z - origin.Z);
		float halfDepthX = Mathf.Min(origin.X - footprint.Position.X, footprint.End.X - origin.X);
		// A T junction is centered on its crossbar: its stem has the same thickness as that bar.
		if (itemName.Contains("_Tsplit") || itemName.Contains("_crossing"))
		{
			float thickness = itemName.Contains("_crossing") ? .5f : Mathf.Min(halfDepthX, halfDepthZ);
			halfDepthX = thickness;
			halfDepthZ = thickness;
		}
		var h = new Aabb(new Vector3(footprint.Position.X, footprint.Position.Y, origin.Z - halfDepthZ), new Vector3(footprint.Size.X, footprint.Size.Y, 2 * halfDepthZ));
		var v = new Aabb(new Vector3(origin.X - halfDepthX, footprint.Position.Y, footprint.Position.Z), new Vector3(2 * halfDepthX, footprint.Size.Y, footprint.Size.Z));
		return new FootprintPair(h, v, true);
	}

	public static IEnumerable<Aabb> PieceFootprints(Aabb local, int orientation, Vector3I cell, string itemName)
	{
		var pair = PieceFootprintPair(local, orientation, cell, itemName);
		yield return pair.First;
		if (pair.HasSecond) yield return pair.Second;
	}

	/// <summary>Whether two pieces penetrate in XZ. Regions of one compound piece may overlap each other;
	/// only intersections between different pieces are relevant. Export rounding within FitTolerance is accepted.</summary>
	public static bool PiecesOverlap(MeshLibrary library, int firstItem, int firstOrientation, Vector3I firstCell,
		int secondItem, int secondOrientation, Vector3I secondCell) =>
		PiecesOverlap(MeshBounds(library, firstItem), library.GetItemName(firstItem), firstOrientation, firstCell,
			MeshBounds(library, secondItem), library.GetItemName(secondItem), secondOrientation, secondCell);

	internal static bool PiecesOverlap(Aabb firstMesh, string firstName, int firstOrientation, Vector3I firstCell,
		Aabb secondMesh, string secondName, int secondOrientation, Vector3I secondCell)
	{
		if (!Overlaps(FootprintWorld(firstMesh, firstOrientation, firstCell), FootprintWorld(secondMesh, secondOrientation, secondCell)))
			return false;
		return FootprintPairsOverlap(
			PieceFootprintPair(firstMesh, firstOrientation, firstCell, firstName),
			PieceFootprintPair(secondMesh, secondOrientation, secondCell, secondName));
	}

	private static bool FootprintPairsOverlap(in FootprintPair a, in FootprintPair b)
	{
		if (Overlaps(a.First, b.First)) return true;
		if (b.HasSecond && Overlaps(a.First, b.Second)) return true;
		if (a.HasSecond)
		{
			if (Overlaps(a.Second, b.First)) return true;
			if (b.HasSecond && Overlaps(a.Second, b.Second)) return true;
		}
		return false;
	}

	internal static bool Overlaps(Aabb a, Aabb b) =>
		Mathf.Min(a.End.X, b.End.X) - Mathf.Max(a.Position.X, b.Position.X) > FitTolerance
		&& Mathf.Min(a.End.Z, b.End.Z) - Mathf.Max(a.Position.Z, b.Position.Z) > FitTolerance;

	/// <summary>Checks distinct pieces of a candidate grid before the room is accepted.</summary>
	public static string FindOverlap(GridMap grid)
	{
		var pieces = new List<(Vector3I Cell, int Item, Aabb Bounds, FootprintPair Regions)>();
		var buckets = new Dictionary<Vector2I, List<int>>();
		var itemCache = new Dictionary<int, (Aabb Bounds, string Name)>();
		var seen = new HashSet<int>();
		var library = grid.MeshLibrary;
		foreach (Vector3I cell in grid.GetUsedCells())
		{
			int item = grid.GetCellItem(cell), orientation = grid.GetCellItemOrientation(cell);
			var (local, name) = GetItemInfo(library, item, itemCache);
			var bounds = FootprintWorld(local, orientation, cell);
			int index = pieces.Count;
			pieces.Add((cell, item, bounds, PieceFootprintPair(local, orientation, cell, name)));
			AddToBuckets(buckets, bounds, index);
		}
		for (int i = 0; i < pieces.Count; i++)
		{
			var a = pieces[i];
			foreach (int j in QueryBuckets(buckets, a.Bounds, seen))
			{
				if (j <= i) continue;
				var b = pieces[j];
				if (Overlaps(a.Bounds, b.Bounds) && FootprintPairsOverlap(a.Regions, b.Regions))
					return $"{itemCache[a.Item].Name} at {a.Cell} overlaps {itemCache[b.Item].Name} at {b.Cell}";
			}
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
	internal static (float Min, float Max) Along(Aabb box, bool horizontal) =>
		horizontal ? (box.Position.X, box.End.X) : (box.Position.Z, box.End.Z);

	/// <summary>A box's extent across a wall line.</summary>
	internal static (float Min, float Max) Across(Aabb box, bool horizontal) => Along(box, !horizontal);

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
	/// <paramref name="cells"/> restricts the collection to specific wall cells when supplied.
	/// </summary>
	public static List<Aabb> Collect(GridMap wallGridMap, bool solidOnly = false, IEnumerable<Vector3I> cells = null)
	{
		var boxes = new List<Aabb>();
		MeshLibrary library = wallGridMap?.MeshLibrary;
		if (library == null)
		{
			return boxes;
		}

		var cache = new Dictionary<int, (bool Valid, Aabb Bounds, string Name)>();
		foreach (Vector3I cell in cells ?? wallGridMap.GetUsedCells())
		{
			int tileIndex = wallGridMap.GetCellItem(cell);
			if (tileIndex < 0)
			{
				continue;
			}

			if (!cache.TryGetValue(tileIndex, out var info))
			{
				Mesh mesh = library.GetItemMesh(tileIndex);
				bool valid = mesh != null && (!solidOnly || library.GetItemShapes(tileIndex).Count > 0);
				info = valid
					? (true, library.GetItemMeshTransform(tileIndex) * mesh.GetAabb(), library.GetItemName(tileIndex))
					: default;
				cache[tileIndex] = info;
			}
			if (!info.Valid) continue;

			var pair = PieceFootprintPair(info.Bounds, wallGridMap.GetCellItemOrientation(cell), cell, info.Name);
			boxes.Add(pair.First);
			if (pair.HasSecond) boxes.Add(pair.Second);
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

/// <summary>A stretch along a wall, in units from a reference end, where wall decor may hang.</summary>
public readonly record struct MountSpan(float From, float To)
{
	public float Center => (From + To) / 2;

	/// <summary>The spans sorted, with any that meet or overlap joined.</summary>
	public static MountSpan[] Join(IEnumerable<MountSpan> spans)
	{
		var joined = new List<MountSpan>();
		foreach (var span in spans.OrderBy(span => span.From))
		{
			if (joined.Count > 0 && span.From <= joined[^1].To + WallFootprints.Eps)
				joined[^1] = joined[^1] with { To = Mathf.Max(joined[^1].To, span.To) };
			else
				joined.Add(span);
		}
		return joined.ToArray();
	}
}