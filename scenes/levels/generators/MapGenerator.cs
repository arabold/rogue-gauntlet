using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

[Tool]
public partial class MapGenerator : Node3D
{
	// Every partial opens its own export group, so the Inspector groups stay right whatever order the partials compile in.
	[ExportGroup("Generation")]
	[Export] public uint DungeonDepth { get; set => SetWithinBounds<uint>(ref field, value, 1, 100); } = 1;
	[Export] public uint MapWidth { get; set => SetWithinBounds<uint>(ref field, value, 20, 100); } = 20;
	[Export] public uint MapDepth { get; set => SetWithinBounds<uint>(ref field, value, 20, 100); } = 20;
	[Export] public uint MaxRooms { get; set => SetWithinBounds<uint>(ref field, value, 1, 100); } = 10;
	[Export] public ulong Seed { get; set; } = 42;
	/// <summary>Editor: rebuilds the floor preview on demand instead of after every property change.</summary>
	[ExportToolButton("Regenerate preview")] public Callable RegeneratePreviewButton => Callable.From(GenerateEditorPreview);

	[Export] public RoomLayoutStrategy RoomLayout { get; set; }
	[Export] public CorridorConnectorStrategy CorridorConnector { get; set; }
	[Export] public MobFactory MobFactory { get; set; }
	[Export] public WorldEffectDefinition SpawnArrivalEffect { get; set; }

	/// <summary>The shared room pool; each floor takes the rooms of its realm.</summary>
	[Export] public RoomFactory Rooms { get; set; }
	/// <summary>
	/// Realms by depth band: the one covering <see cref="DungeonDepth"/> picks the floor's rooms from <see cref="Rooms"/>
	/// and supplies its corridor tiles; the deepest realm carries on below the last band.
	/// </summary>
	[Export] public Realm[] Realms { get; set; } = [];

	/// <summary>The realm the current attempt builds from, resolved when the generator resets.</summary>
	public Realm CurrentRealm { get; private set; }
	private RoomFactory _realmRooms;
	private DungeonTileFactory Corridors => CurrentRealm?.Corridors;
	private readonly HashSet<Vector3I> _dividerCells = [];
	private readonly HashSet<(Vector3I Center, bool Horizontal)> _framedEdges = [];

	public MapData Map;
	/// <summary>The current attempt's floor: generation queries it while it fills, gameplay once it is frozen.</summary>
	public GeneratedFloor Floor { get; private set; }
	[Export] public GridMap FloorGridMap { get; set; }
	[Export] public GridMap WallGridMap { get; set; }
	[Export] public GridMap DecorationGridMap { get; set; }
	[Export] public GridMap OcclusionGridMap { get; set; }
	/// <summary>Parent of everything generation places on the floor: rooms, doors, props, traps and loose loot.</summary>
	[Export] public Node3D FloorContent { get; set; }

	// Shared unlit black cap mesh for permanent void coverage.
	private PlaneMesh _occlusionCapMesh;

	/// <summary>
	/// Y level the flat void occluder caps sit at (slightly above wall-top
	/// height at 4.0f to prevent z-fighting with the wall copings underneath).
	/// A horizontal cap has no tall front face, so it hides the void
	/// without clipping rooms behind it in the isometric view. Tune visually.
	/// </summary>
	public const float OcclusionCapHeight = 4.02f;

	private const int OcclusionItemId = 0;

	/// <summary>
	/// Extra rows/columns of occluder caps placed beyond the map bounds so the
	/// outer walls of edge rooms are hidden against the void.
	/// </summary>
	private const int OcclusionMargin = 2;

	public Array<SpawnPoint> EnemySpawnPoints;

	public override void _Ready()
	{
		GD.Print("Initializing map generator...");
		if (FloorGridMap == null || WallGridMap == null || DecorationGridMap == null || OcclusionGridMap == null || FloorContent == null)
		{
			GD.PushError($"{Name}: assign the floor, wall, decoration and occlusion GridMaps and the floor content node.");
			return;
		}
		OcclusionGridMap.MeshLibrary = BuildOcclusionMeshLibrary();

		if (Engine.IsEditorHint())
		{
			GenerateEditorPreview();
		}
	}

	/// <summary>Builds the editor preview, logging a failure instead of breaking the scene's editor instantiation.</summary>
	private void GenerateEditorPreview()
	{
		try
		{
			GenerateMap(false);
		}
		catch (Exception e)
		{
			GD.PushError($"{Name}: preview generation failed: {e.Message}");
		}
	}

	/// <summary>Ignores out-of-range Inspector values instead of storing them.</summary>
	private static void SetWithinBounds<T>(ref T field, T value, T min, T max) where T : IComparable<T>
	{
		if (Comparer<T>.Default.Compare(value, min) < 0 || Comparer<T>.Default.Compare(value, max) > 0) return;
		field = value;
	}

	protected void MergeRoomGridMaps(Room room, Vector3I placement)
	{
		// Generate the tile map from the room scene
		var roomFloorGridMap = room.FloorGridMap;
		var roomWallGridMap = room.WallGridMap;
		var roomDecorationGridMap = room.DecorationGridMap;

		var gridMapOffset = Map.TileToWorld(placement.X, placement.Z) - room.Origin;
		foreach (var cell in room.DividerCells) _dividerCells.Add(cell + gridMapOffset);
		foreach (var (center, horizontal) in room.FramedEdges) _framedEdges.Add((center + gridMapOffset, horizontal));
		MergeGridMaps(roomFloorGridMap, FloorGridMap, gridMapOffset);
		MergeGridMaps(roomWallGridMap, WallGridMap, gridMapOffset);
		MergeGridMaps(roomDecorationGridMap, DecorationGridMap, gridMapOffset);

		roomFloorGridMap.QueueFree();
		roomWallGridMap.QueueFree();
		roomDecorationGridMap.QueueFree();
		room.FloorGridMap = null;
		room.WallGridMap = null;
		room.DecorationGridMap = null;
	}

	private void MergeGridMaps(GridMap sourceGridMap, GridMap targetGridMap, Vector3I offset)
	{
		// Get all used cells in the source GridMap
		GD.Print($"Merging GridMaps from {sourceGridMap.Name} to {targetGridMap.Name}");
		foreach (Vector3I cell in sourceGridMap.GetUsedCells())
		{
			// Get the tile index at this cell
			int tileIndex = sourceGridMap.GetCellItem(cell);
			if (tileIndex != -1) // Skip empty cells
			{
				int transformIndex = sourceGridMap.GetCellItemOrientation(cell);

				// Apply the offset and copy the tile to the target GridMap
				Vector3I targetCell = cell + new Vector3I((int)offset.X, (int)offset.Y, (int)offset.Z);
				targetGridMap.SetCellItem(targetCell, tileIndex, transformIndex);
			}
		}
	}

	private LevelGenerationContext _random;
	public int GenerationAttempt { get; private set; }

	/// <summary>Recreates a reward stream without advancing generation or gameplay RNGs.</summary>
	public RandomNumberGenerator CreateSourceLootRng(string sourceId)
	{
		return GenerationRandom.Create(Seed, GenerationRandom.Loot, sourceId);
	}

	private void GenerateRooms()
	{
		GD.Print("Generating rooms...");

		var roomPlacements = RoomLayout.GenerateRooms(
			Map, _realmRooms, MaxRooms, _random.Layout);
		PlaceRooms(roomPlacements);
	}

	private void PlaceRooms(List<RoomPlacement> roomPlacements)
	{
		GD.Print("Placing rooms...");
		for (int roomIndex = 0; roomIndex < roomPlacements.Count; roomIndex++)
		{
			var placement = roomPlacements[roomIndex];
			var room = placement.Room;
			var traps = new List<(FloorTrap Trap, string Id)>();
			foreach (Node source in room.FindChildren("*", "", true, false))
			{
				if (source is FloorTrap trap) traps.Add((trap, $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}"));
				if (source is LootableItem)
				{ source.SetMeta("persistent_id", $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}"); }
				if (source is LootTableComponent loot)
				{
					loot.SourceId = $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}";
				}
				if (source is SpawnPoint spawn && string.IsNullOrEmpty(spawn.PersistentId))
				{
					spawn.PersistentId = $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}";
				}
			}
			var position = new Vector3I(placement.Position.X, 0, placement.Position.Y);
			MergeRoomGridMaps(room, position);

			// Rooms join the floor content, whose solids the reachability and navigation grids read.
			FloorContent.AddChild(room);

			room.Translate(Map.TileToWorld(placement.Position) - room.Origin);
			// Placed and merged: each authored trap can now find the floor cell it conceals.
			foreach (var (trap, id) in traps)
				trap.Initialize(id, Seed, FloorGridMap, FloorGridMap.LocalToMap(FloorGridMap.ToLocal(trap.GlobalPosition)));

			RegisterRoomRegion(placement);
		}
	}

	/// <summary>
	/// Records which master tiles belong to a placed room so the room can later be
	/// looked up by tile and revealed by its exact (possibly non-rectangular) shape.
	/// </summary>
	private void RegisterRoomRegion(RoomPlacement placement)
	{
		var roomMap = placement.Room.Map;
		var region = new RoomRegion(Floor.Rooms.Count) { LayoutAxis = placement.Room.LayoutAxis };
		for (int lx = 0; lx < roomMap.Width; lx++)
		{
			for (int lz = 0; lz < roomMap.Height; lz++)
			{
				bool isRoom = roomMap.IsRoom(lx, lz);
				bool isConnector = roomMap.IsConnector(lx, lz);
				// Chasms (interior pits) reveal with the room but are not walkable.
				bool isChasm = roomMap.IsChasm(lx, lz);
				if (!isRoom && !isConnector && !isChasm)
				{
					continue;
				}

				var tile = new Vector2I(placement.Position.X + lx, placement.Position.Y + lz);
				region.Tiles.Add(tile);

				// Only interior floor tiles count as "the player is in this room".
				// Connectors are doorway thresholds: counting them would announce room entry
				// when the player merely stands against its closed door, since the
				// position rounds onto the connector tile.
				if (isRoom)
				{
					Floor.AssignTile(tile, region.Id);
				}

				if (isConnector)
				{
					region.ConnectorTiles.Add(tile);
				}
			}
		}

		RegisterDoors(placement, region);
		Floor.AddRoom(region);
	}

	/// <summary>
	/// Takes over the doors the room set into its doorways, each guarding its doorway's connector (closed, it stops fog
	/// reveal there), and gives interior doors (cell gates) their room-scoped connection IDs.
	/// </summary>
	private void RegisterDoors(RoomPlacement placement, RoomRegion region)
	{
		foreach (var (tile, door) in placement.Room.Doors)
		{
			Floor.AddDoor(door, placement.Position + tile);
		}

		var interiorIds = new HashSet<string>();
		foreach (var door in placement.Room.FindChildren("*", "", true, false).OfType<Door>().Where(door => door.Interior != null))
		{
			if (string.IsNullOrWhiteSpace(door.Interior.Id) || !interiorIds.Add(door.Interior.Id)
				|| door.Lock?.Definition != null || door.Secret?.IsSecret == true)
				throw new InvalidOperationException("Interior doors require unique local IDs and unlocked, non-secret access.");
			door.ConnectionId = $"room:{region.Id}:interior:{door.Interior.Id}";
		}
	}

	/// <summary>
	/// A connector is connected when a corridor was routed out of one of its open
	/// sides (rooms never sit adjacent, so a real link always has a corridor tile).
	/// </summary>
	private bool IsConnectorConnected(Vector2I connector)
	{
		foreach (var direction in Map.GetConnectorDirections(connector.X, connector.Y))
		{
			var outside = connector + direction;
			if (Map.IsWithinBounds(outside.X, outside.Y) && Map.IsCorridor(outside.X, outside.Y))
			{
				return true;
			}
		}

		return false;
	}

	private bool ConnectRooms()
	{
		GD.Print("Connecting rooms...");

		CorridorConnector.ConnectRooms(Map, Floor.Rooms);

		// Doorways are structural commitments, including both ends of a hall's aisle, and their doors and frames are
		// already set. Never turn an unrouteable doorway into a wall and deliver a different room.
		foreach (var room in Floor.Rooms)
		foreach (var tile in room.ConnectorTiles)
			if (!IsConnectorConnected(tile))
				throw new InvalidOperationException($"Required doorway at {tile} could not be connected; the floor cannot be generated.");

		PlaceCorridors();
		return PlaceWalls();
	}

	private void PlaceCorridors()
	{
		GD.Print("Placing corridors...");
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				var position = Map.TileToWorld(x, z);
				if ((Map.IsCorridor(x, z) || Map.IsConnector(x, z)) && FloorGridMap.GetCellItem(position) < 0)
				{
					int tileIndex = Corridors.GetCorridorTileIndex(_random.Decoration);
					FloorGridMap.SetCellItem(position, tileIndex, 0);
				}
			}
		}
	}

	private bool PlaceWalls()
	{
		var tiles = new List<(Vector2I Tile, Vector3I Center)>();
		for (int x = 0; x < Map.Width; x++)
		for (int z = 0; z < Map.Height; z++)
			if (Map.IsExplorable(x, z)) tiles.Add((new Vector2I(x, z), Map.TileToWorld(x, z)));

		using var rng = GenerationRandom.Create(Seed, GenerationRandom.Decoration, $"corridor-walls:{GenerationAttempt}");
		return BoundaryWallBuilder.Build(WallGridMap, tiles, (tile, dx, dz) => NeedsWallToward(tile.X, tile.Y, dx, dz),
			_ => WallGridMap.MeshLibrary.FindItemByName(Corridors.Walls.DrawEdge(rng)),
			dividerCells: _dividerCells, framedEdges: _framedEdges);
	}

	private bool NeedsGeneratedWallAgainst(int x, int z)
	{
		return !Map.IsWithinBounds(x, z) || Map.IsWallOrEmpty(x, z);
	}

	private bool NeedsWallToward(int x, int z, int dx, int dz)
	{
		return NeedsGeneratedWallAgainst(x + dx, z + dz)
			|| Map.RequiresInteriorWall(x, z, new Vector2I(dx, dz));
	}

	/// <summary>
	/// Builds the single-item mesh library used by the occlusion grid: an unlit
	/// black horizontal cap that covers one logical tile footprint at wall-top
	/// height. Contiguous void tiles tile into a continuous flat black roof.
	/// </summary>
	private MeshLibrary BuildOcclusionMeshLibrary()
	{
		var library = new MeshLibrary();
		library.CreateItem(OcclusionItemId);
		library.SetItemName(OcclusionItemId, "void_occluder");
		library.SetItemMesh(OcclusionItemId, OcclusionCapMesh);
		// Floor tiles are centered on their cell, so the cap centers in X/Z; lift it
		// to wall-top height so it forms a flat roof over the void.
		library.SetItemMeshTransform(
			OcclusionItemId,
			new Transform3D(Basis.Identity, new Vector3(0, OcclusionCapHeight, 0)));

		return library;
	}

	/// <summary>
	/// The shared unlit black cap covering one void tile footprint.
	/// Visible from both sides so the cap reads as black at any camera yaw.
	/// </summary>
	private PlaneMesh OcclusionCapMesh => _occlusionCapMesh ??= new PlaneMesh
	{
		Size = new Vector2(Room.TileSize, Room.TileSize),
		Material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.Black,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		},
	};

	/// <summary>Decorates corridor walls, corners, thresholds, and straight spans using the realm's corridor steps.</summary>
	private void DecorateCorridors()
	{
		var dungeonTiles = Corridors;
		if (dungeonTiles == null || dungeonTiles.Steps.Length == 0) return;
		ulong corridorSeed = GenerationRandom.DeriveSeed(Seed, GenerationRandom.Decoration, $"corridors:{GenerationAttempt}");
		var blueprint = new CorridorBlueprint(this, dungeonTiles.Walls, corridorSeed);
		for (int index = 0; index < dungeonTiles.Steps.Length; index++)
		{
			var step = dungeonTiles.Steps[index];
			if (step == null) continue;
			using var rng = blueprint.Random($"{index}:{step.Id}");
			if (rng.Randf() <= step.Chance)
			{
				step.Decorate(blueprint, rng);
			}
		}

		blueprint.EmitProps(this, dungeonTiles.FixtureLitChance);
	}

	/// <summary>Caps permanent void and the outer margin. Gameplay exploration is rendered separately.</summary>
	private void PlaceOcclusion()
	{
		GD.Print("Placing occlusion...");
		if (OcclusionGridMap == null)
		{
			return;
		}

		for (int x = -OcclusionMargin; x < Map.Width + OcclusionMargin; x++)
		{
			for (int z = -OcclusionMargin; z < Map.Height + OcclusionMargin; z++)
			{
				if (!Map.IsExplorable(x, z))
				{
					OcclusionGridMap.SetCellItem(Map.TileToWorld(x, z), OcclusionItemId, 0);
				}
			}
		}
	}

	/// <summary>Resolve a placed node through its owning room, even when a staircase trigger extends beyond its floor tiles.</summary>
	internal int RoomRegionFor(Node node)
	{
		while (node != null && node is not Room) node = node.GetParent();
		if (node is not Room room || room.Map == null) return -1;
		for (int x = 0; x < room.Map.Width; x++)
		for (int z = 0; z < room.Map.Height; z++)
		{
			if (!room.Map.IsRoom(x, z)) continue;
			var local = (Vector3)room.Origin + new Vector3(x * Room.TileSize, 0, z * Room.TileSize);
			int region = Floor.RoomIdAt(Map.WorldToTile(room.ToGlobal(local)));
			if (region >= 0) return region;
		}
		return -1;
	}

	private void FindProgressionAnchors()
	{
		// Rooms are generated content, so this is a typed query over them rather than an authored reference. The exit
		// room carries a spawn point too, for arriving from below; the descent arrival is the one the floor is built around.
		var nodes = FloorContent.FindChildren("*", "", true, false).OfType<Node3D>().ToList();
		Floor.Arrival = nodes.OfType<PlayerSpawnPoint>()
			.FirstOrDefault(point => point.SpawnAfterTravelDirection == GameSession.LevelTravelDirection.Down);
		Floor.Descent = nodes.OfType<LevelTransitionTrigger>()
			.FirstOrDefault(trigger => trigger.Direction == GameSession.LevelTravelDirection.Down);
		Floor.StairLandings = nodes.OfType<PlayerSpawnPoint>().Select(point => (point.Landing ?? point).GlobalPosition).ToList();
		Floor.SpawnAnchors = nodes.Where(node => node is IProgressionAnchor || node.IsInGroup("stairs"))
			.Select(node => node.GlobalPosition).Concat(Floor.StairLandings).ToList();
		if (Floor.Arrival == null)
		{
			GD.PrintErr("PlayerSpawnPoint not found in the scene.");
		}
	}

	/// <summary>
	/// Decides the floor's special encounters once, before access and loot placement, so locks protect the gatekeeper's
	/// room and loot keeps clear of it. An authored marker anchored anywhere in the level wins; otherwise the roomiest
	/// fit nearest the descent. False when a gatekeeper needs a fit and no room has one, so the attempt is retried.
	/// </summary>
	private bool ResolveEncounters()
	{
		var plan = Floor.Encounter = MobFactory.PlanEncounters(DungeonDepth);
		if (plan?.Gatekeeper == null) return true;
		var marker = GatekeeperSpawnMarker.Find(this.GetAncestorOrNull<Level>());
		if (marker != null)
		{
			// SpawnPoint turns what it spawns around; the marker's own facing is the boss's.
			Floor.GatekeeperSite = new Transform3D(marker.GlobalBasis * new Basis(Vector3.Up, Mathf.Pi), marker.GlobalPosition);
			return true;
		}
		if (plan.GatekeeperFit == null) return true;
		if (!Floor.TryFindLargeEncounterPosition(Floor.Descent?.GlobalPosition ?? Vector3.Zero,
			plan.GatekeeperFit.MinimumClearance, plan.GatekeeperFit.PreferredClearance, out var position)) return false;
		Floor.GatekeeperSite = new Transform3D(Basis.Identity, position);
		return true;
	}

	/// <summary>Pack members stand within this distance of their pack's first member.</summary>
	internal const float PackRadius = 3.5f;
	/// <summary>Nests a floor with a nest plan places, each in place of one pack.</summary>
	internal const int NestsPerFloor = 2;
	/// <summary>Distance between members of one pack.</summary>
	private const float PackSpacing = 1.3f;

	/// <summary>
	/// Places the floor's packs and applies its plan. Each pack's first member takes a spawn point spaced from every
	/// other pack; the rest stand around it in the same room. The gatekeeper takes the first pack's place at its resolved
	/// site (or at the first picked point when it has neither a marker nor a fit), and the first two packs of a nest
	/// floor become nests.
	/// </summary>
	private void GenerateEnemySpawnPoints()
	{
		var packs = MobFactory.CreatePacks(DungeonDepth, this.GetAncestorOrNull<Level>()?.ThreatProfile, _random.Encounters);
		var plan = Floor.Encounter;
		GD.Print($"Generating {packs.Count} packs of {packs.Sum(pack => pack.Length)} monsters...");

		var spawnPoints = new List<Vector3>();
		int first = 0;
		if (plan?.Gatekeeper != null && Floor.GatekeeperSite is Transform3D site)
		{
			Floor.GatekeeperSpawn = AddEnemySpawnPoint(plan.Gatekeeper, plan.GatekeeperId, site);
			spawnPoints.Add(site.Origin);
			first = 1;
		}

		// Candidate selection uses map tiles and collision clearance, not navigation routes.
		// Keep the initial encounter baseline reproducible even in headless validation.
		var candidates = Floor.OpenTileCandidates();
		if (candidates.Count == 0)
		{
			GD.PrintErr("Cannot generate enemy spawn points: no valid map tiles found.");
			return;
		}

		int nests = plan?.Nest == null ? 0 : NestsPerFloor;
		for (int p = first; p < packs.Count; p++)
		{
			if (!TryPickEnemySpawnPoint(candidates, spawnPoints, out var anchor))
			{
				GD.PrintErr("Could not find a valid enemy spawn point.");
				continue;
			}
			spawnPoints.Add(anchor);

			string id = $"depth:{DungeonDepth}:pack:{p}";
			// The pack faces one way, so it reads as a group.
			var heading = new Basis(Vector3.Up, (float)(_random.Encounters.Randf() * 2 * Math.PI));
			if (plan?.Gatekeeper != null && Floor.GatekeeperSpawn == null)
			{
				Floor.GatekeeperSpawn = AddEnemySpawnPoint(plan.Gatekeeper, plan.GatekeeperId, new Transform3D(heading, anchor));
				continue;
			}
			if (nests > 0)
			{
				nests--;
				AddEnemySpawnPoint(plan.Nest, id + ":nest", new Transform3D(heading, anchor));
				continue;
			}

			var members = new List<Vector3> { anchor };
			for (int m = 0; m < packs[p].Length; m++)
			{
				var point = anchor;
				if (m > 0)
				{
					if (!Floor.TryPlacePackMember(anchor, members, PackRadius, PackSpacing, _random.Encounters, out point))
					{
						GD.PushWarning($"{id}: no room for member {m}; the pack spawns smaller.");
						continue;
					}
					members.Add(point);
					spawnPoints.Add(point);
				}
				// Enemy scenes are loaded lazily by the factory to keep unused variants out of memory.
				if (packs[p][m] != null) AddEnemySpawnPoint(packs[p][m], $"{id}:{m}", new Transform3D(heading, point));
			}
		}
	}

	private SpawnPoint AddEnemySpawnPoint(PackedScene scene, string persistentId, Transform3D transform)
	{
		var spawnPoint = new SpawnPoint { SpawnOnStart = true, Scenes = [scene], PersistentId = persistentId, ArrivalEffect = SpawnArrivalEffect };
		AddChild(spawnPoint);
		spawnPoint.GlobalTransform = transform;
		EnemySpawnPoints.Add(spawnPoint);
		return spawnPoint;
	}

	private bool TryPickEnemySpawnPoint(IReadOnlyList<Vector3> candidates, List<Vector3> spawnPoints, out Vector3 point)
	{
		point = Vector3.Zero;
		var remainingCandidates = new List<Vector3>(candidates);
		while (remainingCandidates.Count > 0)
		{
			int index = _random.Encounters.RandiRange(0, remainingCandidates.Count - 1);
			var candidate = remainingCandidates[index];
			remainingCandidates.RemoveAt(index);

			if (Floor.IsBlockedSpawnPoint(candidate, spawnPoints))
			{
				continue;
			}

			point = candidate;
			return true;
		}

		return false;
	}

	public void GenerateMap(bool includeGameplay = true)
	{
		for (GenerationAttempt = 0; GenerationAttempt < 8; GenerationAttempt++)
		{
			if (GenerateMapAttempt(includeGameplay)) return;
			GD.Print($"Required structure, progression or encounter placement does not fit; retrying layout (attempt {GenerationAttempt + 1}/8).");
		}
		throw new InvalidOperationException("No generated layout provides required stairs, structural fit and encounter space after eight attempts.");
	}

	/// <summary>Retries layouts whose required stairs, walls or gatekeeper placement cannot fit.</summary>
	private bool GenerateMapAttempt(bool includeGameplay)
	{
		Reset();

		GD.Print("Generating map...");
		if (RoomLayout == null || CorridorConnector == null
			|| _realmRooms == null || MobFactory == null || Corridors == null
			|| FloorGridMap == null || WallGridMap == null || DecorationGridMap == null)
		{
			// This is especially important to check in the editor
			return true;
		}

		// Step 1: Generate random rooms
		GenerateRooms();

		// Gameplay needs both stairs. A crowded layout without an exit must be retried, never delivered as a dead end.
		if (includeGameplay)
		{
			FindProgressionAnchors();
			if (Floor.Arrival == null || Floor.Descent == null) return false;
		}

		// Step 2: Connect the rooms
		if (!ConnectRooms()) return false;
		AccessProfile?.Validate();
		DecorateCorridors();

		// Step 2c: Cap permanent void. Exploration is the level's FloorDiscovery, started once the floor is handed off.
		PlaceOcclusion();
		Floor.SolidWalls = WallFootprints.Collect(WallGridMap, solidOnly: true)
			.Select(box => CollisionOutlines.FromBox(WallGridMap.Transform * box).Polygon).ToList();

		if (!includeGameplay)
		{
			Floor.Freeze();
			GD.Print("Map preview generated.");
			return true;
		}

		// Step 3.1: Reachability over the finished geometry; everything placed from here on lands on reachable cells.
		BuildWalkability();

		// Step 3a: Decide special encounters before anything else claims the floor.
		if (!ResolveEncounters()) return false;

		// Step 3b: Place access, scheduled fountains, then chests/traps/loose items. Must run before BuildNavigation so
		// fountains and chests are included as navigation solids, and before GenerateEnemySpawnPoints so
		// enemy-spawn clearance (which already checks the "prop" group) naturally avoids them.
		PlaceAccess();
		PlaceLoot();

		// Step 4: The clearance grid monsters navigate on
		BuildNavigation();

		// Step 5: Create enemy spawn points
		GenerateEnemySpawnPoints();
		Floor.Freeze();

		GD.Print("Map generated.");
		return true;
	}

	private void Reset()
	{
		GD.Print("Resetting map generator...");
		_random = new LevelGenerationContext(Seed, GenerationAttempt);
		CurrentRealm = Realms.FirstOrDefault(realm => realm?.Covers(DungeonDepth) == true) ?? Realms.Where(realm => realm != null).MaxBy(realm => realm.LastDepth);
		_realmRooms = Rooms?.ForRealm(CurrentRealm?.Id);

		ResetAccess();
		_dividerCells.Clear();
		_framedEdges.Clear();

		// Initialize the map with empty tiles and a walled border
		Map = new MapData((int)MapWidth, (int)MapDepth);
		Map.ResetToBorderedEmpty();
		Floor = new GeneratedFloor(Map, this, DecorationGridMap);

		FloorGridMap?.Clear();
		WallGridMap?.Clear();
		DecorationGridMap?.Clear();
		OcclusionGridMap?.Clear();

		if (EnemySpawnPoints != null)
		{
			foreach (var spawnPoint in EnemySpawnPoints)
			{
				RemoveChild(spawnPoint);
				spawnPoint.QueueFree();
			}
		}
		EnemySpawnPoints = new Array<SpawnPoint>();

		if (FloorContent != null)
		{
			foreach (var node in FloorContent.GetChildren())
			{
				FloorContent.RemoveChild(node);
				node.QueueFree();
			}
		}

		if (GetNodeOrNull<Node3D>("CorridorProps") is { } corridorProps)
		{
			RemoveChild(corridorProps);
			corridorProps.QueueFree();
		}

		Corridors?.ValidateOrThrow();
	}

	/// <summary>
	/// Debug/tooling query: every connector tile's world position and each open direction
	/// annotated with whether it actually leads to an open passage or is sealed by a
	/// generated wall. All-Godot-native return types so this is callable
	/// from a GDScript preview/verification script even though MapData itself (a plain
	/// C# class) can't marshal across that boundary -- see
	/// .agents/skills/godot-mcp/scripts/render_level_topdown.gd, which renders this
	/// over a top-down screenshot so doorway alignment can be checked visually instead
	/// of by reasoning about coordinates.
	/// </summary>
	public Godot.Collections.Array<Godot.Collections.Dictionary> GetConnectorDebugInfo()
	{
		var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (!Map.IsConnector(x, z))
				{
					continue;
				}

				var directionsInfo = new Godot.Collections.Array<Godot.Collections.Dictionary>();
				foreach (var direction in Map.GetConnectorDirections(x, z))
				{
					var outside = new Vector2I(x + direction.X, z + direction.Y);
					bool open = Map.IsWithinBounds(outside.X, outside.Y) && !Map.IsWallOrEmpty(outside.X, outside.Y);
					directionsInfo.Add(new Godot.Collections.Dictionary
					{
						{ "direction", new Vector3(direction.X, 0, direction.Y) },
						{ "open", open },
					});
				}

				result.Add(new Godot.Collections.Dictionary
				{
					{ "worldPosition", (Vector3)Map.TileToWorld(x, z) },
					{ "directions", directionsInfo },
				});
			}
		}

		return result;
	}
}