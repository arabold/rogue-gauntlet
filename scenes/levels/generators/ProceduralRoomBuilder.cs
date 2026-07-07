using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Builds <see cref="Room"/> instances procedurally instead of loading an authored
/// scene, for instant layout variety between the handcrafted templates.
/// <br/>
/// A footprint of logical tiles (rectangle, L-shape, or a union of two rectangles) is
/// filled with floor meshes (optionally mixing in a second accent material over a
/// centered or bordering sub-region), an optional column arrangement chosen for the
/// whole room rather than scattered per-corner, and an optional interior pit that bakes
/// into a chasm. By default the room stays fully open
/// (no <see cref="DoorwayMarker"/>s), exposing every open edge as an inferred connector
/// exactly like the authored cave rooms. With <see cref="DoorwayChance"/> it instead
/// gets a handful of explicit, guaranteed-connected doorways, each optionally fitted
/// with a real interactive door (<see cref="DoorScene"/>) rather than staying an open
/// archway. Explicit doorways change how the room seals itself: once any
/// DoorwayMarker exists, every OTHER open edge stops being an inferred connector and
/// falls back to a plain Room tile, which the map generator's existing wall pass then
/// seals like any void-facing edge (see MapGenerator.PlaceWalls) — so marking doorways
/// never has to be paired with hand-authoring walls to avoid unintended openings.
/// <br/>
/// Grid conventions (matching the authored rooms): GridMaps use 1-unit cells with
/// cell_center disabled, and the mesh geometry is centered on its anchor cell — a
/// 4x4 floor mesh anchored at a logical tile's origin (multiples of 4) covers that
/// tile exactly.
/// </summary>
[Tool]
[GlobalClass]
public partial class ProceduralRoomBuilder : Resource
{
	/// <summary>One logical map tile is 4 GridMap cells (see docs/level-design.md).</summary>
	private const int TileSize = Room.TileSize;

	private static readonly Vector2I[] CardinalDirections =
	{
		Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left,
	};

	[Export] public MeshLibrary FloorLibrary { get; set; }
	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public MeshLibrary DecorationLibrary { get; set; }

	/// <summary>Base floor item names to choose from; one is picked per room for coherence.</summary>
	[Export] public string[] FloorItems { get; set; } = ["floor_dirt_large", "floor_dirt_large_rocky"];

	/// <summary>
	/// Accent floor item names, used for a deliberate sub-region of the room (a rug-like
	/// center patch or a border trim) instead of the base item. See
	/// <see cref="FloorAccentChance"/>.
	/// </summary>
	[Export] public string[] AccentFloorItems { get; set; } = ["floor_wood_large", "floor_wood_large_dark"];

	/// <summary>Chance a room's floor gets a second accent material instead of staying uniform.</summary>
	[Export(PropertyHint.Range, "0,1")] public float FloorAccentChance { get; set; } = 0.3f;

	/// <summary>Decoration items usable as free-standing column features.</summary>
	[Export] public string[] ColumnItems { get; set; } = ["pillar", "column"];

	/// <summary>
	/// Room extent per axis, in logical tiles. Floored at 3: a 2-tile-wide room has no
	/// tile away from an edge, so every floor tile bakes as a Connector rather than a
	/// Room tile (Room.BakeInferredConnectors), leaving the room with no true interior.
	/// </summary>
	[Export(PropertyHint.Range, "3,10")] public int MinTiles { get; set; } = 3;
	[Export(PropertyHint.Range, "3,10")] public int MaxTiles { get; set; } = 6;

	/// <summary>Chance a room gets columns at all; if it does, one pattern is chosen for the whole room.</summary>
	[Export(PropertyHint.Range, "0,1")] public float ColumnChance { get; set; } = 0.25f;

	/// <summary>Tile spacing between columns along a wall for the colonnade pattern.</summary>
	[Export(PropertyHint.Range, "1,4")] public int ColumnSpacing { get; set; } = 2;

	/// <summary>Chance for a rectangular room to contain an interior chasm pit.</summary>
	[Export(PropertyHint.Range, "0,1")] public float ChasmChance { get; set; } = 0.2f;

	/// <summary>
	/// Chance a room gets explicit, guaranteed-connected doorways instead of staying
	/// fully open (every edge an optional inferred connector, cave-style).
	/// </summary>
	[Export(PropertyHint.Range, "0,1")] public float DoorwayChance { get; set; } = 0.5f;

	/// <summary>
	/// Per explicit doorway, the chance it gets a real interactive door instead of
	/// staying an open archway. 0 makes every doorway an archway; 1 makes every doorway
	/// doored; anywhere between mixes the two per room.
	/// </summary>
	[Export(PropertyHint.Range, "0,1")] public float DoorChance { get; set; } = 0.5f;

	/// <summary>Door scene instanced for doored doorways. Left unset, doorways are always open archways.</summary>
	[Export] public PackedScene DoorScene { get; set; }

	[Export(PropertyHint.Range, "1,4")] public int MinDoorways { get; set; } = 1;
	[Export(PropertyHint.Range, "1,4")] public int MaxDoorways { get; set; } = 2;

	/// <summary>
	/// Builds a new room. Returns null when the configuration is unusable (missing
	/// libraries or none of the configured items exist).
	/// </summary>
	public Room BuildRoom()
	{
		if (FloorLibrary == null || WallLibrary == null || DecorationLibrary == null)
		{
			GD.PrintErr("ProceduralRoomBuilder: floor, wall, and decoration mesh libraries must be set.");
			return null;
		}

		int floorItem = PickItem(FloorLibrary, FloorItems);
		if (floorItem < 0)
		{
			GD.PrintErr("ProceduralRoomBuilder: no configured floor item exists in the floor library.");
			return null;
		}

		var footprint = GenerateFootprint();

		var room = new Room { Name = "ProceduralRoom" };
		room.FloorGridMap = AddGridMap(room, "FloorGridMap", FloorLibrary);
		room.WallGridMap = AddGridMap(room, "WallGridMap", WallLibrary);
		room.DecorationGridMap = AddGridMap(room, "DecorationGridMap", DecorationLibrary);

		PlaceFloors(room.FloorGridMap, floorItem, footprint);
		PlaceColumns(room.DecorationGridMap, footprint);
		PlaceDoorways(room, footprint);
		return room;
	}

	/// <summary>
	/// The room shape as a tile mask. Pit tiles are carved to false just like any other
	/// excluded tile: Room.BakeTileMap independently determines which excluded region is
	/// an enclosed chasm (fully surrounded by floor) versus exterior void, so this mask
	/// doesn't need to track pits separately from the rest of the footprint.
	/// <br/>
	/// Internal (not private): the column-pattern generators below take this by value,
	/// and tests construct one directly to assert each pattern's geometry in isolation.
	/// </summary>
	internal readonly struct Footprint
	{
		public Footprint(bool[,] tiles)
		{
			Tiles = tiles;
		}

		public bool[,] Tiles { get; }

		public int Width => Tiles.GetLength(0);
		public int Height => Tiles.GetLength(1);

		public bool IsFloor(int x, int z)
		{
			return x >= 0 && x < Width && z >= 0 && z < Height && Tiles[x, z];
		}
	}

	private Footprint GenerateFootprint()
	{
		int minTiles = Mathf.Max(3, MinTiles);
		int maxTiles = Mathf.Max(minTiles, MaxTiles);
		int width = GD.RandRange(minTiles, maxTiles);
		int height = GD.RandRange(minTiles, maxTiles);
		var tiles = new bool[width, height];
		for (int x = 0; x < width; x++)
		{
			for (int z = 0; z < height; z++)
			{
				tiles[x, z] = true;
			}
		}

		switch (GD.RandRange(0, 2))
		{
			case 0:
				// Plain rectangle; large ones may hold an interior chasm pit. Only
				// rectangles get pits: an L/union notch could merge with a pit into a
				// region touching the room bounds, which would not bake into a chasm.
				if (width >= 4 && height >= 4 && GD.Randf() < ChasmChance)
				{
					CarvePit(tiles, width, height);
				}
				break;

			case 1:
				CarveCorner(tiles, width, height);
				break;

			case 2:
				CarveUnionNotches(tiles, width, height);
				break;
		}

		return new Footprint(tiles);
	}

	/// <summary>Removes a random corner rectangle, leaving an L-shaped room.</summary>
	private static void CarveCorner(bool[,] tiles, int width, int height)
	{
		int carveWidth = GD.RandRange(1, width - 2);
		int carveHeight = GD.RandRange(1, height - 2);
		bool east = GD.Randf() < 0.5f;
		bool south = GD.Randf() < 0.5f;
		for (int x = 0; x < carveWidth; x++)
		{
			for (int z = 0; z < carveHeight; z++)
			{
				tiles[east ? width - 1 - x : x, south ? height - 1 - z : z] = false;
			}
		}
	}

	/// <summary>
	/// Removes two opposing corner rectangles, producing the union of two overlapping
	/// rectangles (an S/T-like shape).
	/// </summary>
	private static void CarveUnionNotches(bool[,] tiles, int width, int height)
	{
		if (width < 4 || height < 4)
		{
			CarveCorner(tiles, width, height);
			return;
		}

		int carveWidth = GD.RandRange(1, width / 2 - 1);
		int carveHeight = GD.RandRange(1, height / 2 - 1);
		bool mirrored = GD.Randf() < 0.5f;
		for (int x = 0; x < carveWidth; x++)
		{
			for (int z = 0; z < carveHeight; z++)
			{
				tiles[x, mirrored ? height - 1 - z : z] = false;
				tiles[width - 1 - x, mirrored ? z : height - 1 - z] = false;
			}
		}
	}

	/// <summary>Carves a small interior pit, at least one tile away from every room edge.</summary>
	private static void CarvePit(bool[,] tiles, int width, int height)
	{
		int pitWidth = Mathf.Min(GD.RandRange(1, 2), width - 3);
		int pitHeight = Mathf.Min(GD.RandRange(1, 2), height - 3);
		int pitX = GD.RandRange(1, width - 1 - pitWidth);
		int pitZ = GD.RandRange(1, height - 1 - pitHeight);
		for (int x = 0; x < pitWidth; x++)
		{
			for (int z = 0; z < pitHeight; z++)
			{
				tiles[pitX + x, pitZ + z] = false;
			}
		}
	}

	/// <summary>
	/// Fills the footprint with <paramref name="floorItem"/>, or -- with
	/// <see cref="FloorAccentChance"/> probability -- a mix of it and one accent item
	/// from <see cref="AccentFloorItems"/> over a deliberate sub-region (a centered rug
	/// or a border trim), rather than per-tile random noise. Base and accent items may
	/// have different footprint sizes: <see cref="PlaceFloorTile"/> resolves size
	/// per-tile.
	/// </summary>
	private void PlaceFloors(GridMap floorGridMap, int floorItem, Footprint footprint)
	{
		int accentItem = -1;
		Func<int, int, bool> isAccentTile = null;
		if (AccentFloorItems.Length > 0 && GD.Randf() < FloorAccentChance)
		{
			accentItem = PickItem(FloorLibrary, AccentFloorItems);
			isAccentTile = GD.Randf() < 0.5f ? GetCenterAccentRegion(footprint) : GetBorderAccentRegion(footprint);
		}

		for (int x = 0; x < footprint.Width; x++)
		{
			for (int z = 0; z < footprint.Height; z++)
			{
				if (!footprint.IsFloor(x, z))
				{
					continue;
				}

				bool useAccent = accentItem >= 0 && isAccentTile(x, z);
				PlaceFloorTile(floorGridMap, useAccent ? accentItem : floorItem, x, z);
			}
		}
	}

	/// <summary>
	/// Places one floor item across logical tile (x,z), anchoring a grid of sub-cells
	/// sized to the item's own footprint (verified against the existing tile-quadrant
	/// contract in ProceduralRoomBuilderTest: tile (x,z) is centered at world
	/// x*TileSize/z*TileSize, spanning +/-TileSize/2). Generalizes what used to be two
	/// hardcoded cases (exactly tile-sized, or exactly half-tile) to any mesh footprint
	/// size that evenly divides TileSize with matching parity -- the only sizes for
	/// which every sub-cell anchor lands on an integer GridMap coordinate.
	/// </summary>
	private void PlaceFloorTile(GridMap floorGridMap, int item, int x, int z)
	{
		int meshSize = ItemFootprintSize(FloorLibrary, item);
		if (meshSize <= 0 || TileSize % meshSize != 0 || (TileSize - meshSize) % 2 != 0)
		{
			GD.PrintErr($"ProceduralRoomBuilder: floor item '{FloorLibrary.GetItemName(item)}' has an "
				+ $"untileable footprint size {meshSize} for a TileSize of {TileSize}; treating it as a full tile.");
			meshSize = TileSize;
		}

		int cellsPerSide = TileSize / meshSize;
		int baseOffset = -(TileSize - meshSize) / 2;
		for (int cx = 0; cx < cellsPerSide; cx++)
		{
			for (int cz = 0; cz < cellsPerSide; cz++)
			{
				floorGridMap.SetCellItem(new Vector3I(
					x * TileSize + baseOffset + cx * meshSize,
					0,
					z * TileSize + baseOffset + cz * meshSize), item);
			}
		}
	}

	/// <summary>A centered sub-rectangle roughly half the footprint's extent, like a rug on a base floor.</summary>
	private static Func<int, int, bool> GetCenterAccentRegion(Footprint footprint)
	{
		int accentWidth = Mathf.Max(1, footprint.Width / 2);
		int accentHeight = Mathf.Max(1, footprint.Height / 2);
		int x0 = (footprint.Width - accentWidth) / 2;
		int z0 = (footprint.Height - accentHeight) / 2;
		return (x, z) => x >= x0 && x < x0 + accentWidth && z >= z0 && z < z0 + accentHeight;
	}

	/// <summary>The ring of floor tiles touching the footprint's bounding-box edge, like a trim border.</summary>
	private static Func<int, int, bool> GetBorderAccentRegion(Footprint footprint)
	{
		return (x, z) => x == 0 || x == footprint.Width - 1 || z == 0 || z == footprint.Height - 1;
	}

	/// <summary>
	/// Places columns at interior tile corners (points where four floor tiles meet)
	/// using one deliberately chosen arrangement per room -- centered, clustered, or
	/// rhythmically spaced along the walls -- mirroring how real rooms place columns,
	/// rather than scattering them independently at random per corner.
	/// </summary>
	private void PlaceColumns(GridMap decorationGridMap, Footprint footprint)
	{
		int columnItem = PickItem(DecorationLibrary, ColumnItems);
		if (columnItem < 0 || GD.Randf() >= ColumnChance)
		{
			return;
		}

		Func<Footprint, List<Vector2I>>[] patterns =
		{
			GetCenterSingleColumns,
			GetCenterClusterColumns,
			candidate => GetWallColonnadeColumns(candidate, ColumnSpacing),
			GetSymmetricScatterColumns,
		};
		List<Vector2I> corners = patterns[GD.RandRange(0, patterns.Length - 1)](footprint);

		// Patterns may propose corners outside the footprint or in an L/union notch
		// (invalid) and, for the colonnade, the same corner from two sides (harmless
		// duplicate) -- both are filtered/deduped here rather than in every pattern.
		var stamped = new HashSet<Vector2I>();
		foreach (Vector2I corner in corners)
		{
			if (!stamped.Add(corner) || !IsInteriorCorner(footprint, corner.X, corner.Y))
			{
				continue;
			}

			decorationGridMap.SetCellItem(CornerAnchor(corner.X, corner.Y), columnItem);
		}
	}

	internal static bool IsInteriorCorner(Footprint footprint, int x, int z)
	{
		return footprint.IsFloor(x, z) && footprint.IsFloor(x - 1, z)
			&& footprint.IsFloor(x, z - 1) && footprint.IsFloor(x - 1, z - 1);
	}

	private static Vector3I CornerAnchor(int x, int z)
	{
		return new Vector3I(x * TileSize - TileSize / 2, 0, z * TileSize - TileSize / 2);
	}

	/// <summary>The single interior corner nearest the footprint's centroid.</summary>
	internal static List<Vector2I> GetCenterSingleColumns(Footprint footprint)
	{
		float centerX = footprint.Width / 2f;
		float centerZ = footprint.Height / 2f;
		bool found = false;
		float bestDistance = 0f;
		Vector2I best = default;
		for (int x = 1; x < footprint.Width; x++)
		{
			for (int z = 1; z < footprint.Height; z++)
			{
				if (!IsInteriorCorner(footprint, x, z))
				{
					continue;
				}

				// Distance only ever needs comparing, so squared distance avoids a sqrt.
				float distance = new Vector2(x - centerX, z - centerZ).LengthSquared();
				if (!found || distance < bestDistance)
				{
					found = true;
					bestDistance = distance;
					best = new Vector2I(x, z);
				}
			}
		}

		return found ? new List<Vector2I> { best } : new List<Vector2I>();
	}

	/// <summary>The 2x2 block of corners immediately surrounding the footprint's centroid.</summary>
	internal static List<Vector2I> GetCenterClusterColumns(Footprint footprint)
	{
		int cx = footprint.Width / 2;
		int cz = footprint.Height / 2;
		return new List<Vector2I> { new(cx, cz), new(cx + 1, cz), new(cx, cz + 1), new(cx + 1, cz + 1) };
	}

	/// <summary>Corners on the footprint's boundary ring, spaced every <paramref name="spacing"/> tiles.</summary>
	internal static List<Vector2I> GetWallColonnadeColumns(Footprint footprint, int spacing)
	{
		spacing = Mathf.Max(1, spacing);
		var corners = new List<Vector2I>();
		for (int x = 1; x < footprint.Width; x += spacing)
		{
			corners.Add(new Vector2I(x, 1));
			corners.Add(new Vector2I(x, footprint.Height - 1));
		}

		for (int z = 1; z < footprint.Height; z += spacing)
		{
			corners.Add(new Vector2I(1, z));
			corners.Add(new Vector2I(footprint.Width - 1, z));
		}

		return corners;
	}

	/// <summary>
	/// Rolls <see cref="ColumnChance"/> once per corner in one quadrant, then mirrors
	/// whatever is chosen across both the width and height mid-axis -- keeps some
	/// organic variety while guaranteeing the result always reads as intentional
	/// (mirror-symmetric) instead of lopsided.
	/// </summary>
	internal List<Vector2I> GetSymmetricScatterColumns(Footprint footprint)
	{
		var corners = new List<Vector2I>();
		int maxX = footprint.Width / 2;
		int maxZ = footprint.Height / 2;
		for (int x = 1; x <= maxX; x++)
		{
			for (int z = 1; z <= maxZ; z++)
			{
				if (GD.Randf() >= ColumnChance)
				{
					continue;
				}

				int mirroredX = footprint.Width - x;
				int mirroredZ = footprint.Height - z;
				corners.Add(new Vector2I(x, z));
				corners.Add(new Vector2I(mirroredX, z));
				corners.Add(new Vector2I(x, mirroredZ));
				corners.Add(new Vector2I(mirroredX, mirroredZ));
			}
		}

		return corners;
	}

	/// <summary>
	/// With <see cref="DoorwayChance"/> probability, replaces the room's default
	/// inferred-connector edges with a handful of explicit doorways (see class remarks
	/// for why this is safe without separately authoring walls), each independently
	/// getting a real door with <see cref="DoorChance"/> probability.
	/// </summary>
	private void PlaceDoorways(Room room, Footprint footprint)
	{
		if (GD.Randf() >= DoorwayChance)
		{
			return; // Stay fully open: BakeInferredConnectors covers every edge.
		}

		var candidates = CollectDoorwayCandidates(footprint);
		if (candidates.Count == 0)
		{
			return;
		}

		int minDoorways = Mathf.Max(1, MinDoorways);
		int maxDoorways = Mathf.Max(minDoorways, MaxDoorways);
		int count = Mathf.Min(GD.RandRange(minDoorways, maxDoorways), candidates.Count);
		Shuffle(candidates);

		for (int i = 0; i < count; i++)
		{
			(Vector2I tile, Vector2I direction) = candidates[i];
			room.AddChild(new DoorwayMarker
			{
				Position = new Vector3(tile.X * TileSize, 0, tile.Y * TileSize),
				Directions = DoorwayMarker.GetDirectionFlag(direction),
			});

			if (DoorScene != null && GD.Randf() < DoorChance)
			{
				PlaceDoor(room, tile, direction);
			}
		}
	}

	/// <summary>
	/// Every floor tile's edges that face strictly outside the footprint's bounding
	/// box, one candidate per tile. Deliberately narrower than "faces a non-floor
	/// tile": an interior chasm pit is also non-floor but fully enclosed by floor
	/// (CarvePit keeps a 1-tile margin from every edge), so a tile facing a pit must not
	/// become a doorway candidate -- Room.BakeDoorwayMarkers requires a doorway to point
	/// into void, and pointing into a chasm is rejected.
	/// <br/>
	/// A corner tile can face two directions at once, but only one is kept: corridor
	/// routing happens later (after the whole map is placed) and can only ever connect
	/// through whichever single direction this doorway is marked with, so committing to
	/// one here keeps the marker's sanctioned direction and a placed door's facing in
	/// permanent agreement -- combining both into one flag set would let the corridor
	/// connect through the direction the door does NOT face, leaving that door pointless
	/// against a generated wall while the real passage sits open on the other side.
	/// </summary>
	private static List<(Vector2I Tile, Vector2I Direction)> CollectDoorwayCandidates(Footprint footprint)
	{
		var directionsByTile = new Dictionary<Vector2I, List<Vector2I>>();
		for (int x = 0; x < footprint.Width; x++)
		{
			for (int z = 0; z < footprint.Height; z++)
			{
				if (!footprint.IsFloor(x, z))
				{
					continue;
				}

				foreach (var direction in CardinalDirections)
				{
					int nx = x + direction.X, nz = z + direction.Y;
					bool facesOutsideBoundingBox = nx < 0 || nx >= footprint.Width || nz < 0 || nz >= footprint.Height;
					if (!facesOutsideBoundingBox)
					{
						continue;
					}

					var tile = new Vector2I(x, z);
					if (!directionsByTile.TryGetValue(tile, out var directions))
					{
						directions = new List<Vector2I>();
						directionsByTile[tile] = directions;
					}

					directions.Add(direction);
				}
			}
		}

		var candidates = new List<(Vector2I, Vector2I)>();
		foreach (var (tile, directions) in directionsByTile)
		{
			candidates.Add((tile, directions[GD.RandRange(0, directions.Count - 1)]));
		}

		return candidates;
	}

	private void PlaceDoor(Room room, Vector2I tile, Vector2I direction)
	{
		var door = DoorScene.Instantiate<Node3D>();
		door.Position = new Vector3(
			tile.X * TileSize + direction.X * (TileSize / 2),
			0,
			tile.Y * TileSize + direction.Y * (TileSize / 2));
		door.RotationDegrees = new Vector3(0, DoorwayMarker.GetYRotationDegrees(DoorwayMarker.GetDirectionFlag(direction)), 0);
		room.AddChild(door);
	}

	private static void Shuffle<T>(List<T> items)
	{
		for (int i = items.Count - 1; i > 0; i--)
		{
			int j = GD.RandRange(0, i);
			(items[i], items[j]) = (items[j], items[i]);
		}
	}

	private static GridMap AddGridMap(Room room, string name, MeshLibrary library)
	{
		var gridMap = new GridMap
		{
			Name = name,
			MeshLibrary = library,
			CellSize = Vector3.One,
			CellCenterX = false,
			CellCenterY = false,
			CellCenterZ = false,
		};
		room.AddChild(gridMap);
		return gridMap;
	}

	/// <summary>Picks a random configured item that exists in the library, or -1.</summary>
	private static int PickItem(MeshLibrary library, string[] itemNames)
	{
		var available = new List<int>();
		foreach (string name in itemNames ?? [])
		{
			int item = library.FindItemByName(name);
			if (item >= 0)
			{
				available.Add(item);
			}
			else
			{
				GD.PrintErr($"ProceduralRoomBuilder: item '{name}' not found in mesh library.");
			}
		}

		return available.Count > 0 ? available[GD.RandRange(0, available.Count - 1)] : -1;
	}

	/// <summary>Mesh footprint size in cells, measured from its AABB (e.g. 2x2 or 4x4 floors).</summary>
	private static int ItemFootprintSize(MeshLibrary library, int item)
	{
		Mesh mesh = library.GetItemMesh(item);
		if (mesh == null)
		{
			return TileSize;
		}

		var size = mesh.GetAabb().Size;
		return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(size.X, size.Z)));
	}
}
