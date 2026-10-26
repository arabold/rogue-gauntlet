using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Footprint shape archetypes available for procedurally generated rooms.
/// </summary>
public enum RoomShapeArchetype
{
	Random = 0,
	Rectangle = 1,
	LShape = 2,
	SteppedZ = 3,
	Cross = 4,
	TJunction = 5,
	UShape = 6,
}

/// <summary>
/// Primary surface material palette for a generated room.
/// </summary>
public enum RoomMaterial
{
	Random = 0,
	StoneSlab = 1,
	CobbleGrid = 2,
	WoodPlanks = 3,
	StoneAndWood = 4,
	Earthen = 5,
	RuinedMix = 6,
}

/// <summary>
/// Architectural purpose and coordinated interior layout (floor zoning, drainage grates, and column arrangement).
/// </summary>
public enum RoomPurpose
{
	Random = 0,
	Chamber = 1,
	GreatHall = 2,
	Temple = 3,
	Colonnade = 4,
	SewerCistern = 5,
	SewerAqueduct = 6,
	RuinedCollapse = 7,
}

/// <summary>
/// Builds <see cref="Room"/> instances procedurally instead of loading an authored
/// scene, for both runtime layout variety and in-editor room creation.
/// </summary>
[Tool]
[GlobalClass]
public partial class ProceduralRoomBuilder : Resource
{
	/// <summary>One logical map tile is 4 GridMap cells (see docs/level-design.md).</summary>
	private const int TileSize = Room.TileSize;

	private static readonly Vector2I[] CardinalDirections =
	[
		Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left,
	];

	/// <summary>The 4 upright orthogonal GridMap indices (0°, 90°, 180°, 270° around +Y).</summary>
	private static readonly int[] UprightOrientations = [0, 16, 10, 22];

	private static readonly string[] DirtSmallItems = ["floor_dirt_small_A", "floor_dirt_small_B", "floor_dirt_small_C", "floor_dirt_small_D"];
	private static readonly string[] BrokenStoneSmallItems = ["floor_tile_small_broken_A", "floor_tile_small_broken_B"];
	private static readonly string[] StoneWeedSmallItems = ["floor_tile_small_weeds_A", "floor_tile_small_weeds_B"];


	private enum TileMaterial
	{
		Stone,
		WoodLight,
		WoodDark,
		Dirt,
	}

	[Export] public MeshLibrary FloorLibrary { get; set; }
	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public MeshLibrary DecorationLibrary { get; set; }

	/// <summary>Optional standalone editor furnishings. Gameplay supplies its pool after corridor resolution instead.</summary>
	[Export] public RoomArrangementPool ArrangementPool { get; set; }

	/// <summary>Decoration items usable as free-standing column features.</summary>
	[Export] public string[] ColumnItems { get; set; } = ["pillar", "column"];

	/// <summary>
	/// Room extent per axis, in logical tiles. Floored at 3: a 2-tile-wide room has no
	/// tile away from an edge, so every floor tile bakes as a Connector rather than a
	/// Room tile (Room.BakeInferredConnectors), leaving the room with no true interior.
	/// </summary>
	[Export(PropertyHint.Range, "3,12")] public int MinTiles { get; set; } = 3;
	[Export(PropertyHint.Range, "3,12")] public int MaxTiles { get; set; } = 6;

	/// <summary>The footprint shape archetype to generate.</summary>
	[Export] public RoomShapeArchetype ShapeArchetype { get; set; } = RoomShapeArchetype.Random;

	/// <summary>Primary floor material palette.</summary>
	[Export] public RoomMaterial Material { get; set; } = RoomMaterial.Random;

	/// <summary>Architectural room purpose coordinating floor zoning, grates, and columns.</summary>
	[Export] public RoomPurpose Purpose { get; set; } = RoomPurpose.Random;

	/// <summary>Eligible purposes when Purpose is Random; a fixed Purpose overrides this selection mask.</summary>
	[Export(PropertyHint.Flags, "Chamber,Great Hall,Temple,Colonnade,Sewer Cistern,Sewer Aqueduct,Ruined Collapse")]
	public int RandomPurposeMask { get; set; } = 127;

	/// <summary>Chance that a placed sewer grate uses its open variant.</summary>
	[Export(PropertyHint.Range, "0,1")] public float OpenGrateChance { get; set; } = 0.2f;

	/// <summary>Density of broken tiles, rocky slabs, and cracked walls.</summary>
	[Export(PropertyHint.Range, "0,1")] public float WearAndRubbleChance { get; set; } = 0.15f;

	/// <summary>Density of green weeds and moss, biased toward walls, corners, dirt, and grates.</summary>
	[Export(PropertyHint.Range, "0,1")] public float WeedsDensity { get; set; } = 0.15f;

	/// <summary>Whether corner overlay tiles (floor_tile_small_corner / floor_dirt_small_corner) are placed over 4x4 slabs at transitions and corners.</summary>
	[Export] public bool EnableCornerOverlays { get; set; } = true;

	/// <summary>Chance a room gets optional columns (and master enable when > 0 for architectural column rooms).</summary>
	[Export(PropertyHint.Range, "0,1")] public float ColumnChance { get; set; } = 0.25f;

	/// <summary>Per-corner probability within the symmetric-scatter column pattern.</summary>
	[Export(PropertyHint.Range, "0,1")] public float ColumnScatterChance { get; set; } = 0.25f;

	/// <summary>Tile spacing between columns along a wall or hall aisle.</summary>
	[Export(PropertyHint.Range, "1,4")] public int ColumnSpacing { get; set; } = 2;

	/// <summary>Whether interior chasm pits are allowed to generate. Disabled by default as gameplay does not yet support chasms.</summary>
	[Export] public bool EnableChasms { get; set; } = false;

	/// <summary>Chance for a rectangular room to contain an interior chasm pit when <see cref="EnableChasms"/> is enabled.</summary>
	[Export(PropertyHint.Range, "0,1")] public float ChasmChance { get; set; } = 0.2f;

	/// <summary>
	/// Whether to render perimeter walls and corners directly into the room's WallGridMap,
	/// leaving open gaps at the chosen doorway openings. False by default for runtime procedural
	/// rooms (where MapGenerator places walls globally); enabled by default in the Editor tool.
	/// </summary>
	[Export] public bool GenerateWalls { get; set; } = false;

	/// <summary>
	/// Chance a room gets explicit, guaranteed-connected doorways instead of staying
	/// fully open when <see cref="GenerateWalls"/> is false. When <see cref="GenerateWalls"/>
	/// is true, explicit openings are always generated so the walled room has entrances.
	/// </summary>
	[Export(PropertyHint.Range, "0,1")] public float DoorwayChance { get; set; } = 0.5f;

	/// <summary>Door scene instanced for doored openings. Left unset, openings stay open archways.</summary>
	[Export] public PackedScene DoorScene { get; set; }

	/// <summary>Minimum number of doorway openings to carve along the room perimeter.</summary>
	[Export(PropertyHint.Range, "1,8")] public int MinDoorways { get; set; } = 1;

	/// <summary>Maximum number of doorway openings to carve along the room perimeter.</summary>
	[Export(PropertyHint.Range, "1,8")] public int MaxDoorways { get; set; } = 3;

	/// <summary>Minimum number of openings that receive a Door prop (clamped to the chosen opening count).</summary>
	[Export(PropertyHint.Range, "0,8")] public int MinDoors { get; set; } = 0;

	/// <summary>Maximum number of openings that receive a Door prop (clamped to the chosen opening count).</summary>
	[Export(PropertyHint.Range, "0,8")] public int MaxDoors { get; set; } = 2;

	/// <summary>
	/// Builds a new room. Returns null when the configuration is unusable (missing
	/// libraries or empty floor library).
	/// </summary>
	public Room BuildRoom(RandomNumberGenerator rng)
	{
		var room = new Room { Name = "ProceduralRoom" };
		if (!PopulateRoom(room, rng))
		{
			room.QueueFree();
			return null;
		}

		return room;
	}

	/// <summary>
	/// Populates an existing Room node with newly generated layout geometry,
	/// clearing previous floor/wall/decoration cells and doorway markers.
	/// </summary>
	public bool PopulateRoom(Room room, RandomNumberGenerator rng) => PopulateRoomCore(room, rng);

	/// <summary>Lets a recipe choose dimensions and midpoint openings before geometry construction.
	/// Null opening directions retain the generic doorway policy; explicit directions bypass it.</summary>
	public bool PopulateRoomAtSize(Room room, RandomNumberGenerator rng, int widthTiles, int depthTiles, Vector2I[] openingDirections = null)
	{
		if (widthTiles < 3 || widthTiles > 12 || depthTiles < 3 || depthTiles > 12)
		{
			GD.PrintErr("ProceduralRoomBuilder: explicit dimensions must be between 3 and 12 tiles.");
			return false;
		}
		if (openingDirections != null && (ShapeArchetype != RoomShapeArchetype.Rectangle
			|| openingDirections.Length == 0 || openingDirections.Distinct().Count() != openingDirections.Length
			|| openingDirections.Any(direction => !CardinalDirections.Contains(direction))))
		{
			GD.PrintErr("ProceduralRoomBuilder: explicit midpoint openings require a rectangle and unique cardinal directions.");
			return false;
		}
		return PopulateRoomCore(room, rng, new Vector2I(widthTiles, depthTiles), openingDirections);
	}

	private bool PopulateRoomCore(Room room, RandomNumberGenerator rng, Vector2I? dimensions = null, Vector2I[] openingDirections = null)
	{
		if (room == null || FloorLibrary == null || WallLibrary == null || DecorationLibrary == null)
		{
			GD.PrintErr("ProceduralRoomBuilder: room, floor, wall, and decoration mesh libraries must be set.");
			return false;
		}

		if (FloorLibrary.GetItemList().Length == 0)
		{
			GD.PrintErr("ProceduralRoomBuilder: floor library contains no items.");
			return false;
		}

		if (Purpose == RoomPurpose.Random && (RandomPurposeMask <= 0 || (RandomPurposeMask & ~127) != 0))
		{
			GD.PrintErr("ProceduralRoomBuilder: select at least one supported random room purpose.");
			return false;
		}

		var arrangementErrors = ArrangementPool?.Validate() ?? [];
		if (arrangementErrors.Length > 0)
		{
			GD.PrintErr(string.Join("\n", arrangementErrors));
			return false;
		}
		ulong furnishingSeed = rng.Seed;
		room.Generator = null;
		room.LayoutAxis = Vector2I.Zero;
		room.FloorGridMap ??= AddGridMap(room, "FloorGridMap", FloorLibrary);
		room.WallGridMap ??= AddGridMap(room, "WallGridMap", WallLibrary);
		room.DecorationGridMap ??= AddGridMap(room, "DecorationGridMap", DecorationLibrary);

		room.FloorGridMap.Clear();
		room.WallGridMap.Clear();
		room.DecorationGridMap.Clear();

		var toRemove = new List<Node>();
		foreach (Node child in room.GetChildren())
		{
			if (child is DoorwayMarker || child is Door || child is RoomArrangement || child is RoomFurnishingSlot)
			{
				toRemove.Add(child);
			}
		}
		foreach (Node node in toRemove)
		{
			room.RemoveChild(node);
			node.QueueFree();
		}

		var footprint = GenerateFootprint(rng, dimensions);
		RoomPurpose purpose = ResolvePurpose(rng);
		room.Purpose = purpose;
		RoomMaterial material = ResolveMaterial(purpose, rng);

		var openings = PlaceDoorways(room, footprint, rng, openingDirections);
		bool[,] quadCovered = PlaceStyledFloors(room.FloorGridMap, footprint, openings, material, purpose, rng);
		PlaceColumns(room.DecorationGridMap, footprint, purpose, quadCovered, rng);

		if (GenerateWalls)
		{
			PlaceRoomWalls(room.WallGridMap, footprint, openings, purpose, rng);
		}

		room.BakeTileMap();
		if (ArrangementPool != null) FurnishStandaloneRoom(room, furnishingSeed);

		if (room.IsInsideTree() && room.GetTree()?.EditedSceneRoot == room)
		{
			room.ShowDebugOverlay = true;
			SetOwnersForPacking(room, room);
		}

		return true;
	}

	/// <summary>Use the gameplay planner for standalone rooms, protecting every authored opening.</summary>
	public void FurnishStandaloneRoom(Room room, ulong seed, RoomArrangementPool pool = null, RoomDressingProfile dressing = null)
	{
		var region = new RoomRegion(0) { IsProcedural = true, Purpose = room.Purpose, LayoutAxis = room.LayoutAxis };
		for (int x = 0; x < room.Map.Width; x++)
			for (int z = 0; z < room.Map.Height; z++)
				if (room.Map.IsWalkable(x, z))
				{
					region.Tiles.Add(new(x, z));
					if (room.Map.GetConnectorDirections(x, z).Count > 0) region.ConnectorTiles.Add(new(x, z));
				}
		foreach (var slot in room.GetChildren().OfType<RoomFurnishingSlot>())
			region.FurnishingSlots.Add(slot.Resolve(region.Id, room.LocalToTile(slot.Position), slot.Transform));
		Vector3 Center(Vector2I tile) => (Vector3)room.TileCenterCell(tile.X, tile.Y);
		pool ??= ArrangementPool;
		var obstacles = RoomArrangement.GetGridBounds(room.DecorationGridMap, room.DecorationGridMap.Transform)
			.Concat(RoomArrangement.GetStaticPropBounds(room, Transform3D.Identity)).ToArray();
		var walls = RoomArrangement.GetGridBounds(room.WallGridMap, room.WallGridMap.Transform);
		var plan = pool.PlacementStrategy.Plan(room.Map, [region], pool, seed, 0, Center, obstacles, walls);
		if (dressing != null) plan.AddRange(dressing.Plan(room.Map, region, seed, 0, Center,
			obstacles.Concat(RoomDressingProfile.GetBounds(plan)).ToArray(), walls,
			obstacles.Concat(RoomDressingProfile.GetBounds(plan, true)).ToArray(), pool.PassageRadius));
		foreach (var placement in plan)
		{
			var instance = placement.Definition.Instantiate();
			instance.Name = $"Arrangement_{room.GetChildCount()}";
			instance.SetMeta("arrangement_id", placement.Definition.Id);
			instance.SetMeta("anchor", placement.Anchor);
			instance.SetMeta("is_room_detail", placement.IsDetail);
			room.AddChild(instance);
			instance.Transform = placement.Transform;
			instance.Owner = room;
		}
	}

	/// <summary>
	/// Generates a new room scene with this builder and saves it directly to a .tscn file at <paramref name="filePath"/>.
	/// </summary>
	public bool SaveRoomScene(string filePath, ulong seed = 0, bool overwrite = true)
	{
		if (string.IsNullOrWhiteSpace(filePath) || (!overwrite && FileAccess.FileExists(filePath)))
		{
			return false;
		}

		using var rng = new RandomNumberGenerator { Seed = seed };
		Room room = BuildRoom(rng);
		if (room == null)
		{
			return false;
		}

		try
		{
			string dir = filePath.GetBaseDir();
			if (!DirAccess.DirExistsAbsolute(dir))
			{
				DirAccess.MakeDirRecursiveAbsolute(dir);
			}

			room.Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
			room.ShowDebugOverlay = true;
			SetOwnersForPacking(room, room);

			var packedScene = new PackedScene();
			return packedScene.Pack(room) == Error.Ok && ResourceSaver.Save(packedScene, filePath) == Error.Ok;
		}
		finally
		{
			room.QueueFree();
		}
	}

	internal static void SetOwnersForPacking(Node node, Node root)
	{
		foreach (Node child in node.GetChildren())
		{
			child.Owner = root;
			if (string.IsNullOrEmpty(child.SceneFilePath))
			{
				SetOwnersForPacking(child, root);
			}
		}
	}

	private RoomPurpose ResolvePurpose(RandomNumberGenerator rng)
	{
		if (Purpose != RoomPurpose.Random) return Purpose;
		var eligible = Enumerable.Range(1, 7).Where(value => (RandomPurposeMask & (1 << (value - 1))) != 0).ToArray();
		return (RoomPurpose)eligible[rng.RandiRange(0, eligible.Length - 1)];
	}

	private RoomMaterial ResolveMaterial(RoomPurpose purpose, RandomNumberGenerator rng)
	{
		if (Material != RoomMaterial.Random)
		{
			return Material;
		}

		if (purpose == RoomPurpose.RuinedCollapse)
		{
			return RoomMaterial.RuinedMix;
		}

		if (purpose is RoomPurpose.SewerCistern or RoomPurpose.SewerAqueduct)
		{
			RoomMaterial[] sewerMaterials = [RoomMaterial.StoneSlab, RoomMaterial.CobbleGrid, RoomMaterial.RuinedMix];
			return sewerMaterials[rng.RandiRange(0, sewerMaterials.Length - 1)];
		}

		return (RoomMaterial)rng.RandiRange(1, 6);
	}

	/// <summary>
	/// The room shape as a tile mask, plus an optional mask of interior chasm pit tiles.
	/// </summary>
	internal readonly struct Footprint
	{
		public Footprint(bool[,] tiles, bool[,] pits = null)
		{
			Tiles = tiles;
			Pits = pits;
		}

		public bool[,] Tiles { get; }
		public bool[,] Pits { get; }

		public int Width => Tiles.GetLength(0);
		public int Height => Tiles.GetLength(1);

		public bool IsFloor(int x, int z)
		{
			return x >= 0 && x < Width && z >= 0 && z < Height && Tiles[x, z];
		}

		public bool IsPit(int x, int z)
		{
			return Pits != null && x >= 0 && x < Width && z >= 0 && z < Height && Pits[x, z];
		}

		public bool IsBoundaryFloor(int x, int z)
		{
			if (!IsFloor(x, z))
			{
				return false;
			}

			return !IsFloor(x - 1, z) || !IsFloor(x + 1, z) || !IsFloor(x, z - 1) || !IsFloor(x, z + 1);
		}
	}

	private Footprint GenerateFootprint(RandomNumberGenerator rng, Vector2I? dimensions = null)
	{
		int min = Mathf.Max(3, MinTiles);
		int max = Mathf.Max(min, MaxTiles);
		int width = dimensions?.X ?? rng.RandiRange(min, max);
		int height = dimensions?.Y ?? rng.RandiRange(min, max);

		for (int attempt = 0; attempt < 8; attempt++)
		{
			var candidate = GenerateConnectedFootprint(width, height, rng);
			if (HasInteriorFloorTile(candidate))
			{
				return candidate;
			}
		}

		return MakeFullRectangle(width, height);
	}

	private Footprint GenerateConnectedFootprint(int width, int height, RandomNumberGenerator rng)
	{
		var tiles = new bool[width, height];
		for (int x = 0; x < width; x++)
		{
			for (int z = 0; z < height; z++)
			{
				tiles[x, z] = true;
			}
		}

		RoomShapeArchetype shape = ShapeArchetype;
		if (shape == RoomShapeArchetype.Random)
		{
			int roll = rng.RandiRange(0, 11);
			shape = roll switch
			{
				<= 3 => RoomShapeArchetype.Rectangle,
				<= 5 => RoomShapeArchetype.LShape,
				<= 7 => RoomShapeArchetype.SteppedZ,
				8 => RoomShapeArchetype.Cross,
				9 => RoomShapeArchetype.TJunction,
				_ => RoomShapeArchetype.UShape,
			};
		}

		switch (shape)
		{
			case RoomShapeArchetype.Rectangle:
				if (EnableChasms && width >= 4 && height >= 4 && rng.Randf() < ChasmChance)
				{
					var pits = new bool[width, height];
					CarvePit(tiles, pits, width, height, rng);
					return new Footprint(tiles, pits);
				}
				break;

			case RoomShapeArchetype.LShape:
				CarveCorner(tiles, width, height, rng);
				break;

			case RoomShapeArchetype.SteppedZ:
				CarveSteppedZ(tiles, width, height, rng);
				break;

			case RoomShapeArchetype.Cross:
				CarveCross(tiles, width, height, rng);
				break;

			case RoomShapeArchetype.TJunction:
				CarveTJunction(tiles, width, height, rng);
				break;

			case RoomShapeArchetype.UShape:
				CarveUShape(tiles, width, height, rng);
				break;
		}

		return new Footprint(tiles);
	}

	private static Footprint MakeFullRectangle(int width, int height)
	{
		var tiles = new bool[width, height];
		for (int x = 0; x < width; x++)
		{
			for (int z = 0; z < height; z++)
			{
				tiles[x, z] = true;
			}
		}

		return new Footprint(tiles);
	}

	private static bool HasInteriorFloorTile(Footprint footprint)
	{
		for (int x = 1; x < footprint.Width - 1; x++)
		{
			for (int z = 1; z < footprint.Height - 1; z++)
			{
				if (footprint.IsFloor(x, z) && !footprint.IsBoundaryFloor(x, z))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static void CarveCorner(bool[,] tiles, int width, int height, RandomNumberGenerator rng, int? forcedCorner = null)
	{
		int carveWidth = rng.RandiRange(1, Mathf.Max(1, width / 2 - 1));
		int carveHeight = rng.RandiRange(1, Mathf.Max(1, height / 2 - 1));
		int corner = forcedCorner ?? rng.RandiRange(0, 3);
		int startX = (corner & 1) == 0 ? 0 : width - carveWidth;
		int startZ = (corner & 2) == 0 ? 0 : height - carveHeight;

		for (int x = startX; x < startX + carveWidth; x++)
		{
			for (int z = startZ; z < startZ + carveHeight; z++)
			{
				tiles[x, z] = false;
			}
		}
	}

	private static void CarveSteppedZ(bool[,] tiles, int width, int height, RandomNumberGenerator rng)
	{
		int firstCorner = rng.RandiRange(0, 3);
		int oppositeCorner = firstCorner ^ 3;
		CarveCorner(tiles, width, height, rng, firstCorner);
		CarveCorner(tiles, width, height, rng, oppositeCorner);
	}

	private static void CarveCross(bool[,] tiles, int width, int height, RandomNumberGenerator rng)
	{
		if (width < 5 || height < 5)
		{
			CarveCorner(tiles, width, height, rng);
			return;
		}

		int carveWidth = rng.RandiRange(1, (width - 3) / 2);
		int carveHeight = rng.RandiRange(1, (height - 3) / 2);
		for (int x = 0; x < carveWidth; x++)
		{
			for (int z = 0; z < carveHeight; z++)
			{
				tiles[x, z] = false;
				tiles[width - 1 - x, z] = false;
				tiles[x, height - 1 - z] = false;
				tiles[width - 1 - x, height - 1 - z] = false;
			}
		}
	}

	private static void CarveTJunction(bool[,] tiles, int width, int height, RandomNumberGenerator rng)
	{
		if (width < 5 || height < 4)
		{
			CarveCorner(tiles, width, height, rng);
			return;
		}

		int carveWidth = rng.RandiRange(1, width / 2 - 1);
		int carveHeight = rng.RandiRange(1, height / 2 - 1);
		int side = rng.RandiRange(0, 3);
		for (int x = 0; x < carveWidth; x++)
		{
			for (int z = 0; z < carveHeight; z++)
			{
				switch (side)
				{
					case 0:
						tiles[x, z] = false;
						tiles[width - 1 - x, z] = false;
						break;
					case 1:
						tiles[width - 1 - x, z] = false;
						tiles[width - 1 - x, height - 1 - z] = false;
						break;
					case 2:
						tiles[x, height - 1 - z] = false;
						tiles[width - 1 - x, height - 1 - z] = false;
						break;
					case 3:
						tiles[x, z] = false;
						tiles[x, height - 1 - z] = false;
						break;
				}
			}
		}
	}

	private static void CarveUShape(bool[,] tiles, int width, int height, RandomNumberGenerator rng)
	{
		if (width < 5 || height < 5)
		{
			CarveCorner(tiles, width, height, rng);
			return;
		}

		int side = rng.RandiRange(0, 3);
		if (side == 0 || side == 2)
		{
			int recessWidth = rng.RandiRange(1, Mathf.Max(1, width - 4));
			int recessDepth = rng.RandiRange(1, Mathf.Max(1, height - 3));
			int startX = (width - recessWidth) / 2;
			for (int x = startX; x < startX + recessWidth; x++)
			{
				for (int z = 0; z < recessDepth; z++)
				{
					tiles[x, side == 0 ? z : height - 1 - z] = false;
				}
			}
		}
		else
		{
			int recessHeight = rng.RandiRange(1, Mathf.Max(1, height - 4));
			int recessDepth = rng.RandiRange(1, Mathf.Max(1, width - 3));
			int startZ = (height - recessHeight) / 2;
			for (int z = startZ; z < startZ + recessHeight; z++)
			{
				for (int x = 0; x < recessDepth; x++)
				{
					tiles[side == 3 ? x : width - 1 - x, z] = false;
				}
			}
		}
	}

	private static void CarvePit(bool[,] tiles, bool[,] pits, int width, int height, RandomNumberGenerator rng)
	{
		int pitWidth = Mathf.Min(rng.RandiRange(1, 2), width - 3);
		int pitHeight = Mathf.Min(rng.RandiRange(1, 2), height - 3);
		int pitX = rng.RandiRange(1, width - 1 - pitWidth);
		int pitZ = rng.RandiRange(1, height - 1 - pitHeight);
		for (int x = 0; x < pitWidth; x++)
		{
			for (int z = 0; z < pitHeight; z++)
			{
				tiles[pitX + x, pitZ + z] = false;
				pits[pitX + x, pitZ + z] = true;
			}
		}
	}

	/// <summary>
	/// Multi-scale non-overlapping floor planner coordinated by <see cref="RoomMaterial"/> and <see cref="RoomPurpose"/>.
	/// Returns the 2x2 sub-quadrant grate coverage mask so column placement can avoid grates.
	/// </summary>
	private bool[,] PlaceStyledFloors(
		GridMap floorGridMap,
		Footprint footprint,
		HashSet<(Vector2I Tile, Vector2I Direction)> openings,
		RoomMaterial material,
		RoomPurpose purpose,
		RandomNumberGenerator rng)
	{
		// All wood tiles in the room share a single plank orientation so boards always run together.
		int woodOrientation = rng.Randf() < 0.5f ? 0 : 16;
		int stylePattern = rng.RandiRange(0, 2);

		int w = footprint.Width;
		int h = footprint.Height;

		// 1. Place architectural drainage grates dictated by the room's purpose.
		bool[,] quadCovered = PlanPurposeGutters(floorGridMap, footprint, openings, material, purpose, rng);

		// 2. Plan per-tile material and whether the material/purpose combination wants a 2x2 subdivision.
		var tileMaterial = new TileMaterial[w, h];
		var preferSmallTiles = new bool[w, h];
		for (int x = 0; x < w; x++)
		{
			for (int z = 0; z < h; z++)
			{
				if (footprint.IsFloor(x, z))
				{
					(tileMaterial[x, z], preferSmallTiles[x, z]) = EvaluateTileStyle(footprint, material, purpose, stylePattern, x, z);
				}
			}
		}

		float effectiveWear = purpose == RoomPurpose.RuinedCollapse
			? Mathf.Max(WearAndRubbleChance, 0.45f)
			: WearAndRubbleChance;
		float effectiveWeeds = purpose == RoomPurpose.RuinedCollapse
			? Mathf.Max(WeedsDensity, 0.3f)
			: WeedsDensity;

		int stoneCornerItem = FloorLibrary.FindItemByName("floor_tile_small_corner");
		int dirtCornerItem = FloorLibrary.FindItemByName("floor_dirt_small_corner");
		var detailSmallItem = new int[2, 2];
		var detailSmallRot = new int[2, 2];

		// 3. Place floor tiles for each logical tile without overlapping sizes.
		for (int x = 0; x < w; x++)
		{
			for (int z = 0; z < h; z++)
			{
				if (!footprint.IsFloor(x, z))
				{
					continue;
				}

				bool hasPartialOccupation = false;
				bool allCovered = quadCovered != null;
				if (quadCovered != null)
				{
					for (int qx = 0; qx < 2; qx++)
					{
						for (int qz = 0; qz < 2; qz++)
						{
							bool covered = quadCovered[2 * x + qx, 2 * z + qz];
							hasPartialOccupation |= covered;
							allCovered &= covered;
						}
					}
				}

				if (allCovered)
				{
					continue;
				}

				TileMaterial mat = tileMaterial[x, z];
				bool isBoundary = footprint.IsBoundaryFloor(x, z);
				float localWeedChance = effectiveWeeds * (isBoundary ? 1.35f : 0.55f);

				bool subdivideForDetail = false;
				for (int qx = 0; qx < 2; qx++)
				{
					for (int qz = 0; qz < 2; qz++)
					{
						detailSmallItem[qx, qz] = -1;
						int gx = 2 * x + qx;
						int gz = 2 * z + qz;
						if (quadCovered != null && quadCovered[gx, gz])
						{
							continue;
						}

						bool nearGrate = HasAdjacentGrate(quadCovered, gx, gz);
						float qWeedChance = nearGrate ? Mathf.Min(1f, localWeedChance * 1.8f) : localWeedChance;

						if (mat == TileMaterial.Stone)
						{
							if (rng.Randf() < qWeedChance * 0.6f)
							{
								detailSmallItem[qx, qz] = FindFloorItem(StoneWeedSmallItems[rng.RandiRange(0, StoneWeedSmallItems.Length - 1)], "floor_tile_small");
								detailSmallRot[qx, qz] = PickUprightOrientation(rng);
								subdivideForDetail = true;
							}
							else if (rng.Randf() < effectiveWear * 0.55f)
							{
								detailSmallItem[qx, qz] = FindFloorItem(BrokenStoneSmallItems[rng.RandiRange(0, BrokenStoneSmallItems.Length - 1)], "floor_tile_small");
								detailSmallRot[qx, qz] = PickUprightOrientation(rng);
								subdivideForDetail = true;
							}
						}
						else if (mat == TileMaterial.Dirt && rng.Randf() < qWeedChance * 0.75f)
						{
							detailSmallItem[qx, qz] = FindFloorItem("floor_dirt_small_weeds", "floor_dirt_small_A");
							detailSmallRot[qx, qz] = PickUprightOrientation(rng);
							subdivideForDetail = true;
						}
					}
				}

				bool subdivide = hasPartialOccupation || preferSmallTiles[x, z] || subdivideForDetail;
				if (!subdivide)
				{
					(int largeItem, int largeRot) = PickLargeTileForMaterial(mat, woodOrientation, effectiveWear, rng);
					floorGridMap.SetCellItem(new Vector3I(x * TileSize, 0, z * TileSize), largeItem, largeRot);

					if (EnableCornerOverlays)
					{
						TryPlaceCornerOverlaysOnLargeTile(floorGridMap, footprint, tileMaterial, x, z, mat, stoneCornerItem, dirtCornerItem, rng);
					}
				}
				else
				{
					bool decoratedZone = material == RoomMaterial.CobbleGrid
						|| (purpose == RoomPurpose.Temple && IsTempleSanctuaryTile(footprint, x, z));
					for (int qx = 0; qx < 2; qx++)
					{
						for (int qz = 0; qz < 2; qz++)
						{
							if (quadCovered != null && quadCovered[2 * x + qx, 2 * z + qz])
							{
								continue;
							}

							var cell = new Vector3I(x * TileSize + (qx == 0 ? -1 : 1), 0, z * TileSize + (qz == 0 ? -1 : 1));
							if (detailSmallItem[qx, qz] >= 0)
							{
								floorGridMap.SetCellItem(cell, detailSmallItem[qx, qz], detailSmallRot[qx, qz]);
							}
							else
							{
								TileMaterial qMat = ResolveSubQuadrantMaterial(footprint, material, purpose, stylePattern, x, z, qx, qz, mat);
								(int smallItem, int smallRot) = PickSmallTileForMaterial(qMat, decoratedZone, woodOrientation, rng);
								floorGridMap.SetCellItem(cell, smallItem, smallRot);
							}
						}
					}
				}
			}
		}

		return quadCovered;
	}

	private bool[,] PlanPurposeGutters(
		GridMap floorGridMap,
		Footprint footprint,
		HashSet<(Vector2I Tile, Vector2I Direction)> openings,
		RoomMaterial material,
		RoomPurpose purpose,
		RandomNumberGenerator rng)
	{
		if (purpose is not (RoomPurpose.SewerCistern or RoomPurpose.SewerAqueduct or RoomPurpose.Colonnade))
		{
			return null;
		}

		int w = footprint.Width;
		int h = footprint.Height;
		int cx = w / 2;
		int cz = h / 2;

		if (purpose == RoomPurpose.Colonnade)
		{
			if (w >= 5 && h >= 5 && material != RoomMaterial.WoodPlanks
				&& footprint.IsFloor(cx, cz) && !footprint.IsBoundaryFloor(cx, cz)
				&& rng.Randf() < 0.5f)
			{
				var courtDrain = new bool[2 * w, 2 * h];
				PlaceBigGrate(floorGridMap, courtDrain, cx, cz, rng);
				return courtDrain;
			}
			return null;
		}

		var quadCovered = new bool[2 * w, 2 * h];

		if (purpose == RoomPurpose.SewerCistern)
		{
			if (w >= 5 && h >= 5 && TryFindNearestTile(footprint, new Vector2((w - 1) / 2f, (h - 1) / 2f), (x, z) => IsSolid3x3Center(footprint, x, z), out int center3X, out int center3Z))
			{
				int xlGrate = PickGrate("floor_tile_extralarge_grates", rng);
				if (xlGrate >= 0)
				{
					floorGridMap.SetCellItem(new Vector3I(center3X * TileSize, 0, center3Z * TileSize), xlGrate, PickUprightOrientation(rng));
					for (int dx = 0; dx < 4; dx++)
					{
						for (int dz = 0; dz < 4; dz++)
						{
							quadCovered[2 * center3X - 1 + dx, 2 * center3Z - 1 + dz] = true;
						}
					}
				}
			}
			else if (TryFindNearestTile(footprint, new Vector2(cx, cz), (x, z) => footprint.IsFloor(x, z) && !footprint.IsBoundaryFloor(x, z), out int bestX, out int bestZ))
			{
				PlaceBigGrate(floorGridMap, quadCovered, bestX, bestZ, rng);
			}

			PlacePerimeterTrench(floorGridMap, footprint, openings, quadCovered, rng);
			return quadCovered;
		}

		if (purpose == RoomPurpose.SewerAqueduct)
		{
			if (TryFindNearestTile(footprint, new Vector2(cx, cz), footprint.IsFloor, out int hubX, out int hubZ))
			{
				cx = hubX;
				cz = hubZ;
			}

			bool horizontal = w >= h;
			bool bothAxes = ShapeArchetype == RoomShapeArchetype.Cross || w == h;

			if (footprint.IsFloor(cx, cz))
			{
				PlaceBigGrate(floorGridMap, quadCovered, cx, cz, rng);
			}

			if (horizontal || bothAxes)
			{
				for (int x = 0; x < w; x++)
				{
					if (x != cx && footprint.IsFloor(x, cz))
					{
						PlaceChannelGrate(floorGridMap, quadCovered, x, cz, horizontal: true, stripIndex: 0, rng);
					}
				}
			}

			if (!horizontal || bothAxes)
			{
				for (int z = 0; z < h; z++)
				{
					if (z != cz && footprint.IsFloor(cx, z))
					{
						PlaceChannelGrate(floorGridMap, quadCovered, cx, z, horizontal: false, stripIndex: 0, rng);
					}
				}
			}
		}

		return quadCovered;
	}

	private void PlacePerimeterTrench(
		GridMap floorGridMap,
		Footprint footprint,
		HashSet<(Vector2I Tile, Vector2I Direction)> openings,
		bool[,] quadCovered,
		RandomNumberGenerator rng)
	{
		var openingTiles = new HashSet<Vector2I>();
		foreach (var (tile, _) in openings)
		{
			openingTiles.Add(tile);
		}

		for (int x = 0; x < footprint.Width; x++)
		{
			for (int z = 0; z < footprint.Height; z++)
			{
				if (!footprint.IsBoundaryFloor(x, z) || openingTiles.Contains(new Vector2I(x, z)))
				{
					continue;
				}

				if (quadCovered[2 * x, 2 * z] || quadCovered[2 * x + 1, 2 * z]
					|| quadCovered[2 * x, 2 * z + 1] || quadCovered[2 * x + 1, 2 * z + 1])
				{
					continue;
				}

				bool north = !footprint.IsFloor(x, z - 1);
				bool south = !footprint.IsFloor(x, z + 1);
				bool west = !footprint.IsFloor(x - 1, z);
				bool east = !footprint.IsFloor(x + 1, z);

				if (((north || south) && (west || east)) || (north && south) || (west && east))
				{
					PlaceBigGrate(floorGridMap, quadCovered, x, z, rng);
				}
				else if (north)
				{
					PlaceChannelGrate(floorGridMap, quadCovered, x, z, horizontal: true, stripIndex: 0, rng);
				}
				else if (south)
				{
					PlaceChannelGrate(floorGridMap, quadCovered, x, z, horizontal: true, stripIndex: 1, rng);
				}
				else if (west)
				{
					PlaceChannelGrate(floorGridMap, quadCovered, x, z, horizontal: false, stripIndex: 0, rng);
				}
				else if (east)
				{
					PlaceChannelGrate(floorGridMap, quadCovered, x, z, horizontal: false, stripIndex: 1, rng);
				}
			}
		}
	}

	private void PlaceBigGrate(GridMap floorGridMap, bool[,] quadCovered, int x, int z, RandomNumberGenerator rng)
	{
		int item = PickGrate("floor_tile_big_grate", rng);
		if (item < 0)
		{
			return;
		}

		floorGridMap.SetCellItem(new Vector3I(x * TileSize, 0, z * TileSize), item, PickUprightOrientation(rng));
		quadCovered[2 * x, 2 * z] = true;
		quadCovered[2 * x + 1, 2 * z] = true;
		quadCovered[2 * x, 2 * z + 1] = true;
		quadCovered[2 * x + 1, 2 * z + 1] = true;
	}

	private void PlaceChannelGrate(
		GridMap floorGridMap,
		bool[,] quadCovered,
		int x,
		int z,
		bool horizontal,
		int stripIndex,
		RandomNumberGenerator rng)
	{
		int item = PickGrate("floor_tile_grate", rng);
		if (item < 0)
		{
			return;
		}

		int offset = stripIndex == 0 ? -1 : 1;
		if (horizontal)
		{
			floorGridMap.SetCellItem(new Vector3I(x * TileSize, 0, z * TileSize + offset), item, 0);
			quadCovered[2 * x, 2 * z + stripIndex] = true;
			quadCovered[2 * x + 1, 2 * z + stripIndex] = true;
		}
		else
		{
			floorGridMap.SetCellItem(new Vector3I(x * TileSize + offset, 0, z * TileSize), item, 16);
			quadCovered[2 * x + stripIndex, 2 * z] = true;
			quadCovered[2 * x + stripIndex, 2 * z + 1] = true;
		}
	}

	private static bool IsSolid3x3Center(Footprint footprint, int x, int z)
	{
		if (x < 1 || x >= footprint.Width - 1 || z < 1 || z >= footprint.Height - 1)
		{
			return false;
		}

		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				if (!footprint.IsFloor(x + dx, z + dz))
				{
					return false;
				}
			}
		}

		return true;
	}

	private static bool TryFindNearestTile(
		Footprint footprint,
		Vector2 target,
		Func<int, int, bool> predicate,
		out int bestX,
		out int bestZ)
	{
		bestX = Mathf.RoundToInt(target.X);
		bestZ = Mathf.RoundToInt(target.Y);
		float bestDist = float.MaxValue;
		bool found = false;

		for (int x = 0; x < footprint.Width; x++)
		{
			for (int z = 0; z < footprint.Height; z++)
			{
				if (!predicate(x, z))
				{
					continue;
				}

				float dist = new Vector2(x - target.X, z - target.Y).LengthSquared();
				if (dist < bestDist)
				{
					bestDist = dist;
					bestX = x;
					bestZ = z;
					found = true;
				}
			}
		}

		return found;
	}

	private static bool IsHallWalkwayTile(Footprint footprint, int x, int z)
	{
		return footprint.Width >= footprint.Height
			? z == footprint.Height / 2
			: x == footprint.Width / 2;
	}

	private static bool IsTempleSanctuaryTile(Footprint footprint, int x, int z)
	{
		if (footprint.IsBoundaryFloor(x, z))
		{
			return false;
		}

		float midX = (footprint.Width - 1) / 2f;
		float midZ = (footprint.Height - 1) / 2f;
		return Mathf.Abs(x - midX) <= 1.1f && Mathf.Abs(z - midZ) <= 1.1f;
	}

	private static (TileMaterial Material, bool PreferSmall) EvaluateTileStyle(
		Footprint footprint,
		RoomMaterial material,
		RoomPurpose purpose,
		int pattern,
		int x,
		int z)
	{
		bool isBoundary = footprint.IsBoundaryFloor(x, z);

		if (purpose == RoomPurpose.GreatHall)
		{
			bool onWalkway = IsHallWalkwayTile(footprint, x, z);
			return material switch
			{
				RoomMaterial.WoodPlanks => (onWalkway ? TileMaterial.WoodLight : TileMaterial.WoodDark, !onWalkway && isBoundary),
				RoomMaterial.StoneAndWood => (onWalkway ? TileMaterial.WoodLight : TileMaterial.Stone, !onWalkway),
				RoomMaterial.Earthen or RoomMaterial.RuinedMix => (onWalkway ? TileMaterial.Stone : TileMaterial.Dirt, false),
				_ => (TileMaterial.Stone, !onWalkway),
			};
		}

		if (purpose == RoomPurpose.Temple)
		{
			bool inSanctuary = IsTempleSanctuaryTile(footprint, x, z);
			return material switch
			{
				RoomMaterial.WoodPlanks => (inSanctuary ? TileMaterial.WoodLight : TileMaterial.WoodDark, isBoundary),
				RoomMaterial.StoneAndWood => (inSanctuary ? TileMaterial.WoodLight : TileMaterial.Stone, inSanctuary),
				RoomMaterial.Earthen or RoomMaterial.RuinedMix => (inSanctuary ? TileMaterial.Stone : TileMaterial.Dirt, inSanctuary),
				_ => (TileMaterial.Stone, inSanctuary || material == RoomMaterial.CobbleGrid),
			};
		}

		if (purpose == RoomPurpose.Colonnade)
		{
			return material switch
			{
				RoomMaterial.WoodPlanks => (isBoundary ? TileMaterial.WoodDark : TileMaterial.WoodLight, isBoundary),
				RoomMaterial.StoneAndWood => (isBoundary ? TileMaterial.Stone : TileMaterial.WoodLight, isBoundary),
				RoomMaterial.Earthen => (TileMaterial.Dirt, isBoundary),
				RoomMaterial.RuinedMix => (isBoundary ? TileMaterial.Dirt : TileMaterial.Stone, isBoundary),
				_ => (TileMaterial.Stone, isBoundary || material == RoomMaterial.CobbleGrid),
			};
		}

		if (purpose == RoomPurpose.RuinedCollapse)
		{
			bool dirtPatch = pattern switch
			{
				0 => x + z < (footprint.Width + footprint.Height) / 2,
				1 => isBoundary || ((x + z) % 2 == 0),
				_ => x < footprint.Width / 2,
			};
			if (dirtPatch)
			{
				return (TileMaterial.Dirt, (x + z) % 2 == 1);
			}
		}

		switch (material)
		{
			case RoomMaterial.StoneSlab:
				return pattern switch
				{
					0 => (TileMaterial.Stone, false),
					1 => (TileMaterial.Stone, isBoundary),
					_ => (TileMaterial.Stone, (x + z) % 2 == 1),
				};

			case RoomMaterial.CobbleGrid:
				return (TileMaterial.Stone, true);

			case RoomMaterial.WoodPlanks:
				return pattern switch
				{
					0 => (isBoundary ? TileMaterial.WoodDark : TileMaterial.WoodLight, isBoundary),
					1 => ((z % 2 == 0) ? TileMaterial.WoodLight : TileMaterial.WoodDark, false),
					_ => (((x + z) % 2 == 0) ? TileMaterial.WoodLight : TileMaterial.WoodDark, (x + z) % 2 == 1),
				};

			case RoomMaterial.StoneAndWood:
				if (isBoundary)
				{
					return (TileMaterial.Stone, pattern == 1);
				}
				return (pattern == 2 && (x + z) % 2 == 1 ? TileMaterial.WoodDark : TileMaterial.WoodLight, false);

			case RoomMaterial.Earthen:
				return (TileMaterial.Dirt, pattern == 1 ? isBoundary : (pattern == 2 && (x + z) % 2 == 1));

			case RoomMaterial.RuinedMix:
			default:
				bool dirtZone = pattern switch
				{
					0 => x + z < (footprint.Width + footprint.Height) / 2,
					1 => isBoundary && ((x + z) % 2 == 0),
					_ => x < footprint.Width / 2,
				};
				return (dirtZone ? TileMaterial.Dirt : TileMaterial.Stone, false);
		}
	}

	private static TileMaterial ResolveSubQuadrantMaterial(
		Footprint footprint,
		RoomMaterial material,
		RoomPurpose purpose,
		int pattern,
		int x,
		int z,
		int qx,
		int qz,
		TileMaterial baseMaterial)
	{
		if (material == RoomMaterial.WoodPlanks && (pattern == 0 || purpose == RoomPurpose.Colonnade) && footprint.IsBoundaryFloor(x, z))
		{
			bool touchesOuter = (qx == 0 && !footprint.IsFloor(x - 1, z))
				|| (qx == 1 && !footprint.IsFloor(x + 1, z))
				|| (qz == 0 && !footprint.IsFloor(x, z - 1))
				|| (qz == 1 && !footprint.IsFloor(x, z + 1));
			return touchesOuter ? TileMaterial.WoodDark : TileMaterial.WoodLight;
		}

		return baseMaterial;
	}

	private (int Item, int Orientation) PickLargeTileForMaterial(
		TileMaterial material,
		int woodOrientation,
		float wearChance,
		RandomNumberGenerator rng)
	{
		switch (material)
		{
			case TileMaterial.WoodLight:
				return (FindFloorItem("floor_wood_large", "floor_tile_large"), woodOrientation);

			case TileMaterial.WoodDark:
				return (FindFloorItem("floor_wood_large_dark", "floor_wood_large"), woodOrientation);

			case TileMaterial.Dirt:
				string dirtName = rng.Randf() < wearChance ? "floor_dirt_large_rocky" : "floor_dirt_large";
				return (FindFloorItem(dirtName, "floor_dirt_large"), PickUprightOrientation(rng));

			case TileMaterial.Stone:
			default:
				string stoneName = rng.Randf() < wearChance * 0.45f ? "floor_tile_large_rocks" : "floor_tile_large";
				return (FindFloorItem(stoneName, "floor_tile_large"), PickUprightOrientation(rng));
		}
	}

	private (int Item, int Orientation) PickSmallTileForMaterial(
		TileMaterial material,
		bool decoratedZone,
		int woodOrientation,
		RandomNumberGenerator rng)
	{
		switch (material)
		{
			case TileMaterial.WoodLight:
				return (FindFloorItem("floor_wood_small", "floor_tile_small"), woodOrientation);

			case TileMaterial.WoodDark:
				return (FindFloorItem("floor_wood_small_dark", "floor_wood_small"), woodOrientation);

			case TileMaterial.Dirt:
				string dirtSmall = DirtSmallItems[rng.RandiRange(0, DirtSmallItems.Length - 1)];
				return (FindFloorItem(dirtSmall, "floor_dirt_small_A"), PickUprightOrientation(rng));

			case TileMaterial.Stone:
			default:
				string stoneSmall = decoratedZone && rng.Randf() < 0.25f
					? "floor_tile_small_decorated"
					: "floor_tile_small";
				return (FindFloorItem(stoneSmall, "floor_tile_small"), PickUprightOrientation(rng));
		}
	}

	/// <summary>
	/// Places floor_tile_small_corner or floor_dirt_small_corner on the odd quadrant cells
	/// (4x ± 1, 0, 4z ± 1) over a 4x4 base slab where materials transition or at outer room corners.
	/// Orientation mapping verified against room_dining.tscn: NW=22, NE=10, SE=16, SW=0.
	/// </summary>
	private void TryPlaceCornerOverlaysOnLargeTile(
		GridMap floorGridMap,
		Footprint footprint,
		TileMaterial[,] tileMaterial,
		int x,
		int z,
		TileMaterial mat,
		int stoneCornerItem,
		int dirtCornerItem,
		RandomNumberGenerator rng)
	{
		if (stoneCornerItem < 0 && dirtCornerItem < 0)
		{
			return;
		}

		ReadOnlySpan<(int Qx, int Qz, int Dx, int Dz, int Rot)> corners =
		[
			(0, 0, -1, -1, 22),
			(1, 0, 1, -1, 10),
			(1, 1, 1, 1, 16),
			(0, 1, -1, 1, 0),
		];

		foreach (var (qx, qz, dx, dz, rot) in corners)
		{
			var cell = new Vector3I(x * TileSize + (qx == 0 ? -1 : 1), 0, z * TileSize + (qz == 0 ? -1 : 1));
			if (floorGridMap.GetCellItem(cell) >= 0)
			{
				continue;
			}

			bool adjXFloor = footprint.IsFloor(x + dx, z);
			bool adjZFloor = footprint.IsFloor(x, z + dz);

			if (mat == TileMaterial.Dirt && stoneCornerItem >= 0)
			{
				bool bordersStone = (adjXFloor && tileMaterial[x + dx, z] == TileMaterial.Stone)
					|| (adjZFloor && tileMaterial[x, z + dz] == TileMaterial.Stone);
				bool isOuterRoomCorner = !adjXFloor && !adjZFloor;

				if (bordersStone || (isOuterRoomCorner && rng.Randf() < 0.45f))
				{
					floorGridMap.SetCellItem(cell, stoneCornerItem, rot);
				}
			}
			else if (mat == TileMaterial.Stone && dirtCornerItem >= 0)
			{
				bool bordersDirt = (adjXFloor && tileMaterial[x + dx, z] == TileMaterial.Dirt)
					&& (adjZFloor && tileMaterial[x, z + dz] == TileMaterial.Dirt);
				bool isOuterRoomCorner = !adjXFloor && !adjZFloor;

				if (bordersDirt || (isOuterRoomCorner && WeedsDensity > 0.05f && rng.Randf() < 0.25f))
				{
					floorGridMap.SetCellItem(cell, dirtCornerItem, rot);
				}
			}
		}
	}

	private static bool HasAdjacentGrate(bool[,] quadCovered, int gx, int gz)
	{
		if (quadCovered == null)
		{
			return false;
		}

		int qw = quadCovered.GetLength(0);
		int qh = quadCovered.GetLength(1);
		return (gx > 0 && quadCovered[gx - 1, gz])
			|| (gx + 1 < qw && quadCovered[gx + 1, gz])
			|| (gz > 0 && quadCovered[gx, gz - 1])
			|| (gz + 1 < qh && quadCovered[gx, gz + 1]);
	}

	private static bool CornerTouchesGrate(bool[,] quadCovered, int cornerX, int cornerZ)
	{
		if (quadCovered == null)
		{
			return false;
		}

		int qw = quadCovered.GetLength(0);
		int qh = quadCovered.GetLength(1);
		for (int dx = -1; dx <= 0; dx++)
		{
			for (int dz = -1; dz <= 0; dz++)
			{
				int gx = 2 * cornerX + dx;
				int gz = 2 * cornerZ + dz;
				if (gx >= 0 && gx < qw && gz >= 0 && gz < qh && quadCovered[gx, gz])
				{
					return true;
				}
			}
		}

		return false;
	}

	private int PickGrate(string baseName, RandomNumberGenerator rng)
	{
		bool open = rng.Randf() < OpenGrateChance;
		return FindFloorItem(open ? $"{baseName}_open" : baseName, baseName);
	}

	private int FindFloorItem(string preferred, string fallback)
	{
		int id = FloorLibrary.FindItemByName(preferred);
		if (id >= 0)
		{
			return id;
		}

		id = FloorLibrary.FindItemByName(fallback);
		return id >= 0 ? id : 0;
	}

	private static int PickUprightOrientation(RandomNumberGenerator rng)
	{
		return UprightOrientations[rng.RandiRange(0, UprightOrientations.Length - 1)];
	}

	/// <summary>
	/// Places columns at interior tile corners (points where four floor tiles meet)
	/// using the column layout appropriate for <paramref name="purpose"/>.
	/// </summary>
	private void PlaceColumns(
		GridMap decorationGridMap,
		Footprint footprint,
		RoomPurpose purpose,
		bool[,] quadCovered,
		RandomNumberGenerator rng)
	{
		if (ColumnChance <= 0f)
		{
			return;
		}

		bool signatureColumns = purpose is RoomPurpose.GreatHall
			or RoomPurpose.Temple
			or RoomPurpose.Colonnade
			or RoomPurpose.RuinedCollapse;

		if (!signatureColumns && rng.Randf() >= ColumnChance)
		{
			return;
		}

		int defaultColumnItem = PickItem(DecorationLibrary, ColumnItems, rng);
		if (defaultColumnItem < 0)
		{
			return;
		}

		List<Vector2I> corners = purpose switch
		{
			RoomPurpose.GreatHall => GetHallAisleColumns(footprint, ColumnSpacing),
			RoomPurpose.Temple => GetCenterClusterColumns(footprint),
			RoomPurpose.Colonnade or RoomPurpose.RuinedCollapse => GetWallColonnadeColumns(footprint, ColumnSpacing),
			RoomPurpose.SewerCistern or RoomPurpose.SewerAqueduct => rng.Randf() < 0.5f
				? GetWallColonnadeColumns(footprint, ColumnSpacing)
				: GetCenterClusterColumns(footprint),
			_ => rng.Randf() < 0.5f
				? GetCenterSingleColumns(footprint)
				: GetSymmetricScatterColumns(footprint, rng),
		};

		var stamped = new HashSet<Vector2I>();
		foreach (Vector2I corner in corners)
		{
			if (!stamped.Add(corner)
				|| !IsInteriorCorner(footprint, corner.X, corner.Y)
				|| CornerTouchesGrate(quadCovered, corner.X, corner.Y))
			{
				continue;
			}

			if (purpose == RoomPurpose.RuinedCollapse && stamped.Count > 1 && rng.Randf() < 0.35f)
			{
				continue;
			}

			int itemToPlace = purpose == RoomPurpose.RuinedCollapse
				? PickItem(DecorationLibrary, ColumnItems, rng)
				: defaultColumnItem;
			decorationGridMap.SetCellItem(CornerAnchor(corner.X, corner.Y), itemToPlace >= 0 ? itemToPlace : defaultColumnItem);
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
		return TryFindNearestTile(footprint, new Vector2(footprint.Width / 2f, footprint.Height / 2f), (x, z) => IsInteriorCorner(footprint, x, z), out int bestX, out int bestZ)
			? [new Vector2I(bestX, bestZ)]
			: [];
	}

	/// <summary>The 2x2 block of corners immediately surrounding the footprint's centroid.</summary>
	internal static List<Vector2I> GetCenterClusterColumns(Footprint footprint)
	{
		int cx = footprint.Width / 2;
		int cz = footprint.Height / 2;
		return [new(cx, cz), new(cx + 1, cz), new(cx, cz + 1), new(cx + 1, cz + 1)];
	}

	/// <summary>
	/// Two parallel rows of corners flanking the central walkway tile row/column,
	/// spaced every <paramref name="spacing"/> tiles along the hall's primary axis.
	/// </summary>
	internal static List<Vector2I> GetHallAisleColumns(Footprint footprint, int spacing)
	{
		spacing = Mathf.Max(1, spacing);
		var corners = new List<Vector2I>();
		if (footprint.Width >= footprint.Height)
		{
			int cz = footprint.Height / 2;
			for (int x = 1; x < footprint.Width; x += spacing)
			{
				corners.Add(new Vector2I(x, cz));
				corners.Add(new Vector2I(x, cz + 1));
			}
		}
		else
		{
			int cx = footprint.Width / 2;
			for (int z = 1; z < footprint.Height; z += spacing)
			{
				corners.Add(new Vector2I(cx, z));
				corners.Add(new Vector2I(cx + 1, z));
			}
		}

		return corners;
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
	/// Rolls <see cref="ColumnScatterChance"/> once per corner in one quadrant, then
	/// mirrors whatever is chosen across both the width and height mid-axis.
	/// </summary>
	internal List<Vector2I> GetSymmetricScatterColumns(Footprint footprint, RandomNumberGenerator rng)
	{
		var corners = new List<Vector2I>();
		int maxX = footprint.Width / 2;
		int maxZ = footprint.Height / 2;
		for (int x = 1; x <= maxX; x++)
		{
			for (int z = 1; z <= maxZ; z++)
			{
				if (rng.Randf() >= ColumnScatterChance)
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
	/// Selects and places doorway openings and optional interactive Door props.
	/// When <see cref="GenerateWalls"/> is enabled, openings are always generated so the
	/// walled room has valid entrances.
	/// </summary>
	private HashSet<(Vector2I Tile, Vector2I Direction)> PlaceDoorways(
		Room room,
		Footprint footprint,
		RandomNumberGenerator rng,
		Vector2I[] openingDirections = null)
	{
		var chosenOpenings = new HashSet<(Vector2I Tile, Vector2I Direction)>();
		List<(Vector2I Tile, Vector2I Direction)> selected;
		if (openingDirections != null)
		{
			// The recipe owns these entrances. Use them before floors, walls and Door props are built.
			selected = openingDirections.Select(direction => (new Vector2I(
				direction.X < 0 ? 0 : direction.X > 0 ? footprint.Width - 1 : footprint.Width / 2,
				direction.Y < 0 ? 0 : direction.Y > 0 ? footprint.Height - 1 : footprint.Height / 2), direction)).ToList();
		}
		else
		{
			if (!GenerateWalls && rng.Randf() >= DoorwayChance) return chosenOpenings;
			var candidates = CollectDoorwayCandidates(footprint, rng);
			if (candidates.Count == 0) return chosenOpenings;
			int minOpenings = Mathf.Max(1, MinDoorways);
			int maxOpenings = Mathf.Max(minOpenings, MaxDoorways);
			int count = Mathf.Min(rng.RandiRange(minOpenings, maxOpenings), candidates.Count);
			selected = SelectArchitecturalOpenings(candidates, count, footprint, rng);
		}

		var doorIndices = new HashSet<int>();
		if (DoorScene != null)
		{
			int minD = Mathf.Clamp(MinDoors, 0, selected.Count);
			int maxD = Mathf.Clamp(Mathf.Max(MinDoors, MaxDoors), minD, selected.Count);
			int doorCount = rng.RandiRange(minD, maxD);
			var order = new List<int>(selected.Count);
			for (int i = 0; i < selected.Count; i++)
			{
				order.Add(i);
			}
			Shuffle(order, rng);
			for (int i = 0; i < doorCount; i++)
			{
				doorIndices.Add(order[i]);
			}
		}

		for (int i = 0; i < selected.Count; i++)
		{
			(Vector2I tile, Vector2I direction) = selected[i];
			chosenOpenings.Add((tile, direction));

			room.AddChild(new DoorwayMarker
			{
				Name = $"DoorwayMarker{i + 1}",
				Position = new Vector3(tile.X * TileSize, 0, tile.Y * TileSize),
				Directions = DoorwayMarker.GetDirectionFlag(direction),
			});

			if (doorIndices.Contains(i))
			{
				PlaceDoor(room, tile, direction, i + 1);
			}
		}

		return chosenOpenings;
	}

	/// <summary>
	/// Spreads chosen openings across different cardinal directions first (preferring non-corner tiles
	/// near wall midpoints), then fills remaining slots without crowding adjacent tiles when possible.
	/// </summary>
	private static List<(Vector2I Tile, Vector2I Direction)> SelectArchitecturalOpenings(
		List<(Vector2I Tile, Vector2I Direction)> candidates,
		int count,
		Footprint footprint,
		RandomNumberGenerator rng)
	{
		Shuffle(candidates, rng);

		var byDirection = new Dictionary<Vector2I, List<(Vector2I Tile, Vector2I Direction)>>();
		foreach (var candidate in candidates)
		{
			if (!byDirection.TryGetValue(candidate.Direction, out var list))
			{
				list = new List<(Vector2I Tile, Vector2I Direction)>();
				byDirection[candidate.Direction] = list;
			}
			list.Add(candidate);
		}

		float midX = (footprint.Width - 1) / 2f;
		float midZ = (footprint.Height - 1) / 2f;
		foreach (var (dir, list) in byDirection)
		{
			list.Sort((a, b) =>
			{
				float distA = dir.X != 0 ? Mathf.Abs(a.Tile.Y - midZ) : Mathf.Abs(a.Tile.X - midX);
				float distB = dir.X != 0 ? Mathf.Abs(b.Tile.Y - midZ) : Mathf.Abs(b.Tile.X - midX);
				return distA.CompareTo(distB);
			});
		}

		var directions = new List<Vector2I>(byDirection.Keys);
		Shuffle(directions, rng);

		var selected = new List<(Vector2I Tile, Vector2I Direction)>();
		var usedTiles = new HashSet<Vector2I>();

		foreach (var dir in directions)
		{
			if (selected.Count >= count)
			{
				break;
			}

			foreach (var candidate in byDirection[dir])
			{
				if (usedTiles.Add(candidate.Tile))
				{
					selected.Add(candidate);
					break;
				}
			}
		}

		foreach (var candidate in candidates)
		{
			if (selected.Count >= count)
			{
				break;
			}

			if (usedTiles.Contains(candidate.Tile))
			{
				continue;
			}

			bool adjacentToExisting = false;
			foreach (var existing in selected)
			{
				if (Mathf.Abs(existing.Tile.X - candidate.Tile.X) + Mathf.Abs(existing.Tile.Y - candidate.Tile.Y) <= 1)
				{
					adjacentToExisting = true;
					break;
				}
			}

			if (!adjacentToExisting)
			{
				selected.Add(candidate);
				usedTiles.Add(candidate.Tile);
			}
		}

		foreach (var candidate in candidates)
		{
			if (selected.Count >= count)
			{
				break;
			}

			if (usedTiles.Add(candidate.Tile))
			{
				selected.Add(candidate);
			}
		}

		return selected;
	}

	/// <summary>
	/// Places perimeter walls and corners onto <paramref name="wallGridMap"/> using the shared
	/// <see cref="WallFootprints.PlaceWalls"/> algorithm, leaving open gaps at every chosen opening.
	/// </summary>
	private void PlaceRoomWalls(
		GridMap wallGridMap,
		Footprint footprint,
		HashSet<(Vector2I Tile, Vector2I Direction)> openings,
		RoomPurpose purpose,
		RandomNumberGenerator rng)
	{
		int baseWall = WallLibrary.FindItemByName("wall");
		if (baseWall < 0)
		{
			baseWall = 0;
		}
		int crackedWall = WallLibrary.FindItemByName("wall_cracked");
		int halfWall = WallLibrary.FindItemByName("wall_half");
		if (halfWall < 0)
		{
			halfWall = baseWall;
		}
		int cornerWall = WallLibrary.FindItemByName("wall_corner");
		if (cornerWall < 0)
		{
			cornerWall = baseWall;
		}

		float effectiveWear = purpose == RoomPurpose.RuinedCollapse
			? Mathf.Max(WearAndRubbleChance, 0.45f)
			: WearAndRubbleChance;

		var sourceTiles = new List<(Vector2I Tile, Vector3I BasePosition)>();
		for (int x = 0; x < footprint.Width; x++)
		{
			for (int z = 0; z < footprint.Height; z++)
			{
				if (footprint.IsFloor(x, z))
				{
					sourceTiles.Add((new Vector2I(x, z), new Vector3I(x * TileSize, 0, z * TileSize)));
				}
			}
		}

		WallFootprints.PlaceWalls(
			wallGridMap,
			TileSize,
			sourceTiles,
			(tile, dx, dz) =>
			{
				int nx = tile.X + dx;
				int nz = tile.Y + dz;
				if (footprint.IsFloor(nx, nz) || footprint.IsPit(nx, nz))
				{
					return false;
				}
				return !openings.Contains((tile, new Vector2I(dx, dz)));
			},
			() => (crackedWall >= 0 && rng.Randf() < effectiveWear) ? crackedWall : baseWall,
			() => halfWall,
			() => cornerWall);
	}

	private static List<(Vector2I Tile, Vector2I Direction)> CollectDoorwayCandidates(Footprint footprint, RandomNumberGenerator rng)
	{
		var candidates = new List<(Vector2I, Vector2I)>();
		Span<Vector2I> directions = stackalloc Vector2I[4];

		for (int x = 0; x < footprint.Width; x++)
		{
			for (int z = 0; z < footprint.Height; z++)
			{
				if (!footprint.IsFloor(x, z))
				{
					continue;
				}

				int count = 0;
				foreach (var direction in CardinalDirections)
				{
					int nx = x + direction.X, nz = z + direction.Y;
					if (nx < 0 || nx >= footprint.Width || nz < 0 || nz >= footprint.Height)
					{
						directions[count++] = direction;
					}
				}

				if (count > 0)
				{
					candidates.Add((new Vector2I(x, z), directions[rng.RandiRange(0, count - 1)]));
				}
			}
		}

		return candidates;
	}

	private void PlaceDoor(Room room, Vector2I tile, Vector2I direction, int index)
	{
		var door = DoorScene.Instantiate<Node3D>();
		door.Name = $"Door{index}";
		door.Position = new Vector3(
			tile.X * TileSize + direction.X * (TileSize / 2),
			0,
			tile.Y * TileSize + direction.Y * (TileSize / 2));
		door.RotationDegrees = new Vector3(0, DoorwayMarker.GetYRotationDegrees(DoorwayMarker.GetDirectionFlag(direction)), 0);
		room.AddChild(door);
	}

	private static void Shuffle<T>(List<T> items, RandomNumberGenerator rng)
	{
		for (int i = items.Count - 1; i > 0; i--)
		{
			int j = rng.RandiRange(0, i);
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
	private static int PickItem(MeshLibrary library, string[] names, RandomNumberGenerator rng)
	{
		if (names == null || names.Length == 0)
		{
			return -1;
		}

		Span<int> resolved = stackalloc int[names.Length];
		int count = 0;
		foreach (string name in names)
		{
			int id = library.FindItemByName(name);
			if (id >= 0)
			{
				resolved[count++] = id;
			}
		}

		return count > 0 ? resolved[rng.RandiRange(0, count - 1)] : -1;
	}
}