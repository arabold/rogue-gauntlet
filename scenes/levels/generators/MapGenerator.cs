using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

[Tool]
public partial class MapGenerator : Node3D
{
	[Export] public uint DungeonDepth { get; set => SetPropertyWithBounds<uint>(ref field, value, 1, 100); } = 1;
	[Export] public uint MapWidth { get; set => SetPropertyWithBounds<uint>(ref field, value, 20, 100); } = 20;
	[Export] public uint MapDepth { get; set => SetPropertyWithBounds<uint>(ref field, value, 20, 100); } = 20;
	[Export] public uint MaxRooms { get; set => SetPropertyWithBounds<uint>(ref field, value, 1, 100); } = 10;
	[Export] public ulong Seed { get; set => SetProperty(ref field, value); } = 42;

	[Export] public RoomLayoutStrategy RoomLayout { get; set => SetProperty(ref field, value); }
	[Export] public CorridorConnectorStrategy CorridorConnector { get; set => SetProperty(ref field, value); }
	[Export] public RoomFactory RoomFactory { get; set => SetProperty(ref field, value); }
	[Export] public MobFactory MobFactory { get; set => SetProperty(ref field, value); }
	[Export] public TileFactory TileFactory { get; set => SetProperty(ref field, value); }

	/// <summary>Chest prop placed against a room wall, facing into the room. Unset skips chest placement.</summary>
	[Export] public PackedScene ChestScene { get; set => SetProperty(ref field, value); }
	[Export(PropertyHint.Range, "0,10")] public int MinChests { get; set => SetProperty(ref field, value); } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxChests { get; set => SetProperty(ref field, value); } = 2;

	/// <summary>Floor trap prop, placeable on any open room/corridor tile. Unset skips trap placement.</summary>
	[Export] public PackedScene TrapScene { get; set => SetProperty(ref field, value); }
	[Export(PropertyHint.Range, "0,10")] public int MinTraps { get; set => SetProperty(ref field, value); } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxTraps { get; set => SetProperty(ref field, value); } = 3;

	/// <summary>Loose ground-item prop. Unset skips loose-item placement.</summary>
	[Export] public PackedScene LooseItemScene { get; set => SetProperty(ref field, value); }
	/// <summary>Weighted pool loose items are rolled from. Unset skips loose-item placement.</summary>
	[Export] public LootTable LooseItemLootTable { get; set => SetProperty(ref field, value); }
	[Export(PropertyHint.Range, "0,10")] public int MinLooseItems { get; set => SetProperty(ref field, value); } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxLooseItems { get; set => SetProperty(ref field, value); } = 4;

	public MapData Map;
	public GridMap FloorGridMap { get; private set; }
	public GridMap WallGridMap { get; private set; }
	public GridMap DecorationGridMap { get; private set; }
	public GridMap OcclusionGridMap { get; private set; }
	public NavigationRegion3D NavigationRegion { get; private set; }

	// Lazily-built translucent overlay of the baked navigation mesh, toggled by the debug menu.
	private MeshInstance3D _navigationDebugMesh;

	private readonly List<RoomRegion> _roomRegions = new();
	// Interior floor tile -> room id, for "which room is the player in" detection.
	private readonly System.Collections.Generic.Dictionary<Vector2I, int> _tileToRoom = new();
	// Connector tile -> room id (every connector). Drives the reveal cascade.
	private readonly System.Collections.Generic.Dictionary<Vector2I, int> _connectorToRoom = new();
	// Connectors guarded by a door; the reveal cascade stops here until it opens.
	private readonly HashSet<Vector2I> _dooredConnectors = new();
	private readonly HashSet<Vector2I> _revealedTiles = new();
	// Tiles uncovered by the in-progress RevealRoom call, batched so they fade out
	// together as one overlay. Null when a reveal should apply instantly (e.g. on load).
	private List<Vector2I> _revealBatch;
	// Shared unlit black cap mesh, reused by the static occlusion grid and the fade overlay.
	private PlaneMesh _occlusionCapMesh;
	// Doors paired with the connector they guard, for toggling their x-ray indicator.
	private readonly List<(Door Door, Vector2I Connector)> _doorIndicators = new();
	// Doorway markers paired with the connector they mark, so a marker whose doorway
	// never got a corridor routed to it (see FinalizeDoors) can be hidden instead of
	// misleadingly pointing at what is now a solid generated wall.
	private readonly List<(DoorwayMarker Marker, Vector2I Connector)> _markerIndicators = new();

	private static readonly Vector2I[] CardinalOffsets =
	{
		new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
	};

	// FIXME: Centralize the tile size to avoid hardcoding it in multiple places
	/// <summary>
	/// The size of each tile in the base map when translating to the GridMaps.
	/// </summary>
	public readonly uint TileSize = 4;

	/// <summary>
	/// Y level the flat void occluder caps sit at (one tile up, i.e. wall-top
	/// height). A horizontal cap has no tall front face, so it hides the void
	/// without clipping rooms behind it in the isometric view. Tune visually.
	/// </summary>
	public readonly float OcclusionCapHeight = 4f;

	/// <summary>
	/// Seconds the black caps over a newly revealed area take to fade out, so exploring
	/// eases an area into view instead of popping it.
	/// </summary>
	private const float FogRevealFadeSeconds = 0.6f;

	private const int OcclusionItemId = 0;
	private const int HorizontalWallOrientation = 0;
	private const int HorizontalWallWestHalfOrientation = 10;
	private const int VerticalWallOrientation = 16;
	private const int VerticalWallSouthHalfOrientation = 22;
	private const int NorthWestCornerOrientation = 16;
	private const int NorthEastCornerOrientation = 0;
	private const int SouthWestCornerOrientation = 10;
	private const int SouthEastCornerOrientation = 22;
	private const float EnemySpawnPlayerClearance = 20f;
	private const float EnemySpawnPointSpacing = 5f;
	private const float EnemySpawnBlockedAreaClearance = 7f;
	private const float EnemySpawnPropClearance = 2f;

	// Smaller than the enemy-spawn equivalents: loot close to the entrance is fine, and a
	// tight level shouldn't run out of valid spots just to keep loot far from the player.
	// Internal (not private) so LootPlacementTest can assert against the real thresholds.
	internal const float LootSpawnPlayerClearance = 10f;
	internal const float LootSpawnSpacing = 6f;

	// Physics layers a runtime spawn must stay clear of: world (1) | walls (2) | props (5).
	// Keeps items out of stairs, transition blockers, walls, and props via a single overlap test.
	private const uint SpawnObstructionMask = 1u | 2u | 16u;
	private const uint EffectLandingSurfaceMask = 1u;
	private const float EffectLandingRayHeight = 10.0f;
	private const float EffectLandingRayDepth = 14.0f;

	/// <summary>
	/// Extra rows/columns of occluder caps placed beyond the map bounds so the
	/// outer walls of edge rooms are hidden against the void.
	/// </summary>
	private const int OcclusionMargin = 2;

	public PlayerSpawnPoint PlayerSpawnPoint;
	public Array<SpawnPoint> EnemySpawnPoints;

	public override void _Ready()
	{
		GD.Print("Initializing map generator...");
		FloorGridMap = GetNode<GridMap>("FloorGridMap");
		WallGridMap = GetNode<GridMap>("WallGridMap");
		DecorationGridMap = GetNode<GridMap>("DecorationGridMap");
		OcclusionGridMap = GetNode<GridMap>("OcclusionGridMap");
		OcclusionGridMap.MeshLibrary = BuildOcclusionMeshLibrary();
		NavigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");

		if (Engine.IsEditorHint())
		{
			GenerateMap(false);
		}
	}

	protected void SetProperty<T>(ref T field, T value)
	{
		field = value;

		// Generate the map when the properties are set in the editor
		if (Engine.IsEditorHint() && IsNodeReady())
		{
			GenerateMap(false);
		}
	}

	protected void SetPropertyWithBounds<T>(ref T field, T value, T min, T max) where T : IComparable<T>
	{
		if (Comparer<T>.Default.Compare(value, min) < 0 || Comparer<T>.Default.Compare(value, max) > 0) return;
		SetProperty(ref field, value);
	}

	protected void MergeRoomGridMaps(Room room, Vector3I placement)
	{
		// Generate the tile map from the room scene
		var roomFloorGridMap = room.FloorGridMap;
		var roomWallGridMap = room.WallGridMap;
		var roomDecorationGridMap = room.DecorationGridMap;

		var roomOffset = new Vector3I(room.Bounds.Position.X, 0, room.Bounds.Position.Y);
		var gridMapOffset = TileToWorld(placement) - roomOffset;
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

	private void GenerateRooms()
	{
		GD.Print("Generating rooms...");

		var roomPlacements = RoomLayout.GenerateRooms(
			Map, RoomFactory, MaxRooms);
		PlaceRooms(roomPlacements);
	}

	private void PlaceRooms(List<RoomPlacement> roomPlacements)
	{
		GD.Print("Placing rooms...");
		foreach (var placement in roomPlacements)
		{
			var room = placement.Room;
			var position = new Vector3I(placement.Position.X, 0, placement.Position.Y);
			MergeRoomGridMaps(room, position);

			// Make the room a child of the navigation region, so
			// it is included in the navigation mesh and enemies
			// avoid obstactles in it.
			NavigationRegion.AddChild(room);

			var roomOffset = new Vector3I(room.Bounds.Position.X, 0, room.Bounds.Position.Y);
			room.Translate(TileToWorld(position) - roomOffset);

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
		if (roomMap == null)
		{
			return;
		}

		// A procedurally built room (ProceduralRoomBuilder.BuildRoom) is a fresh `new
		// Room` with no backing scene file; an authored room is instantiated from a
		// PackedScene, which sets SceneFilePath on the instantiated root.
		var region = new RoomRegion(_roomRegions.Count) { IsProcedural = string.IsNullOrEmpty(placement.Room.SceneFilePath) };
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
				// Connectors are doorway thresholds: counting them would reveal a room
				// when the player merely stands against its closed door, since the
				// position rounds onto the connector tile.
				if (isRoom)
				{
					_tileToRoom[tile] = region.Id;
				}

				if (isConnector)
				{
					region.ConnectorTiles.Add(tile);
					_connectorToRoom[tile] = region.Id;
				}
			}
		}

		RegisterDoors(placement.Room, region);
		_roomRegions.Add(region);
	}

	/// <summary>
	/// Marks the connectors guarded by hand-placed Door props so fog reveal stops
	/// at them, and separately tracks every DoorwayMarker so one whose doorway never
	/// gets a corridor routed to it (see FinalizeDoors) can be hidden rather than left
	/// pointing at a solid wall. Doors sit in the wall opening rather than exactly on a
	/// connector tile, so they're matched to the nearest connector of the room. Markers
	/// are authored exactly on their own tile's center (see the coordinate-convention
	/// docs in Room.cs), so they're matched to that exact tile directly -- proximity
	/// matching would let a marker whose OWN doorway failed validation (its tile never
	/// became a connector at all) borrow a nearby, unrelated doorway's connected status
	/// instead of correctly always reading as unconnected.
	/// </summary>
	private void RegisterDoors(Room room, RoomRegion region)
	{
		if (region.ConnectorTiles.Count == 0)
		{
			return;
		}

		foreach (Node node in room.FindChildren("*", "", true, false))
		{
			if (node is Door door)
			{
				// Candidates only: which doorways are real passages isn't known until
				// corridors have been routed (see FinalizeDoors).
				if (TryFindNearestConnector(region, door.GlobalPosition, out var connector))
				{
					_doorIndicators.Add((door, connector));
				}
			}
			else if (node is DoorwayMarker marker)
			{
				var tile = WorldToTile(marker.GlobalPosition);
				if (region.ConnectorTiles.Contains(tile))
				{
					_markerIndicators.Add((marker, tile));
				}
				else
				{
					// The marker's own doorway validation failed outright (its tile
					// never became a connector at all) -- known now, without waiting on
					// corridor routing, so disable it immediately rather than leaving it
					// enabled forever (FinalizeMarkers only ever visits tracked markers).
					marker.Enabled = false;
				}
			}
		}
	}

	private bool TryFindNearestConnector(RoomRegion region, Vector3 worldPosition, out Vector2I connector)
	{
		connector = default;
		float bestDistance = float.MaxValue;
		foreach (var candidate in region.ConnectorTiles)
		{
			var center = TileToWorld(candidate.X, 0, candidate.Y);
			float dx = worldPosition.X - center.X;
			float dz = worldPosition.Z - center.Z;
			float distance = dx * dx + dz * dz;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				connector = candidate;
			}
		}

		// Doors sit in the wall opening, up to ~1.5 tiles from the connector floor
		// tile they guard (plus tile-center rounding), so accept the nearest match
		// within a 2-tile radius and reject doors with no connector near them.
		float maxDistance = TileSize * 2f;
		return bestDistance <= maxDistance * maxDistance;
	}

	/// <summary>
	/// Resolves hand-placed doors against the routed corridors. A door whose doorway
	/// was connected becomes a real gating door (its connector seals the fog); a door
	/// at a doorway the connector step walled shut is removed so it does not linger as
	/// an interactable door embedded in a solid wall.
	/// </summary>
	private void FinalizeDoors()
	{
		var connectedDoors = new List<(Door Door, Vector2I Connector)>();
		foreach (var (door, connector) in _doorIndicators)
		{
			if (IsConnectorConnected(connector))
			{
				_dooredConnectors.Add(connector);
				connectedDoors.Add((door, connector));
			}
			else
			{
				door.QueueFree();
			}
		}

		_doorIndicators.Clear();
		_doorIndicators.AddRange(connectedDoors);
	}

	/// <summary>
	/// Hides the editor-only gizmo of every DoorwayMarker whose doorway never got a
	/// corridor routed to it. A doorway can validate fine (it becomes a real
	/// MapData connector) yet still end up walled shut -- e.g. a tightly packed room
	/// with more doorways than the layout had room to route corridors to, or one side
	/// of a corner tile after Room.Rotate -- and MapData keeps classifying that tile
	/// as a Connector either way (wall placement is pure geometry, it never touches
	/// tile classification). Without this, the marker's arrow keeps pointing at what
	/// is now a solid generated wall, which is confusing when inspecting a level in
	/// the editor even though it has no effect on actual gameplay (the arrow doesn't
	/// render outside the editor at all).
	/// </summary>
	private void FinalizeMarkers()
	{
		foreach (var (marker, connector) in _markerIndicators)
		{
			if (!IsConnectorConnected(connector))
			{
				marker.Enabled = false;
			}
		}

		_markerIndicators.Clear();
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

	/// <summary>True when one of the connector's own sanctioned open directions is <paramref name="direction"/>.</summary>
	private bool ConnectorOpensToward(Vector2I connector, Vector2I direction)
	{
		foreach (var openDirection in Map.GetConnectorDirections(connector.X, connector.Y))
		{
			if (openDirection == direction)
			{
				return true;
			}
		}

		return false;
	}

	private void ConnectRooms()
	{
		GD.Print("Connecting rooms...");

		CorridorConnector.ConnectRooms(Map);

		PlaceCorridors();
		PlaceWalls();
	}

	private void PlaceCorridors()
	{
		GD.Print("Placing corridors...");
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				var position = TileToWorld(x, 0, z);
				if ((Map.IsCorridor(x, z) || Map.IsConnector(x, z)) && FloorGridMap.GetCellItem(position) < 0)
				{
					int tileIndex = TileFactory.GetCorridorTileIndex();
					FloorGridMap.SetCellItem(position, tileIndex, 0);
				}
			}
		}
	}

	private void PlaceWalls()
	{
		GD.Print("Placing walls...");
		var cornerEdges = new System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges>();
		// A HashSet, not a List: an interior room/corridor edge is a wall-source tile on
		// both sides (unlike a void-facing edge, which only ever has one walkable side),
		// so both sides independently request the same wall at the same position and
		// orientation. WallStraightRequest is a record struct, so this dedupes for free.
		var straightWalls = new HashSet<WallStraightRequest>();
		var authoredWallBoxes = CollectWallFootprints();

		// Decision pass: one rule per tile edge (see NeedsWallToward) collects every
		// edge that must end up sealed.
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (IsWallSourceTile(x, z))
				{
					PlaceWallModulesForTile(x, z, cornerEdges, straightWalls, authoredWallBoxes);
				}
			}
		}

		// Fast path: a full-width wall on each edge whose midpoint cell is free -- the
		// overwhelmingly common case.
		foreach (var wall in straightWalls)
		{
			if (WallGridMap.GetCellItem(wall.Position) < 0)
			{
				WallGridMap.SetCellItem(wall.Position, TileFactory.GetWallTileIndex(), wall.Orientation);
			}
		}

		// Coverage pass: re-measure the actual mesh footprints now in the grid (authored
		// + just-generated) and patch any required edge still showing daylight. Coverage
		// is judged from real geometry rather than parallel bookkeeping, so authored
		// frame posts, corner pieces, and rotated perpendicular walls that merely occupy
		// anchor CELLS without covering the edge LINE cannot fool it -- that exact
		// confusion previously left an unconnected authored doorway open to the void
		// (silently: the old half-wall fallback gave up when its two fixed anchor cells
		// were taken). A required edge that cannot be sealed is now a loud error.
		var wallBoxes = CollectWallFootprints();
		foreach (var wall in straightWalls)
		{
			EnsureEdgeCovered(wall, wallBoxes);
		}

		PlaceWallCorners(cornerEdges);
	}

	/// <summary>
	/// Seals whatever parts of a required edge are still uncovered, placing full or half
	/// wall pieces anchored at any free cell along the edge row whose measured footprint
	/// actually covers the hole. Errors loudly when a hole cannot be sealed -- a visible
	/// gap in the dungeon must never be silent.
	/// </summary>
	private void EnsureEdgeCovered(WallStraightRequest wall, List<Aabb> wallBoxes)
	{
		bool horizontal = wall.Orientation == HorizontalWallOrientation;
		float lineCoord = horizontal ? wall.Position.Z : wall.Position.X;
		float spanMin = (horizontal ? wall.Position.X : wall.Position.Z) - TileSize / 2f;
		float spanMax = spanMin + TileSize;

		// Each successful fill covers a positive part of the hole, so a handful of
		// pieces always suffices for a TileSize-wide edge; the bound only guards
		// against a degenerate mesh library (e.g. zero-width wall meshes).
		for (int attempt = 0; attempt < TileSize; attempt++)
		{
			if (!WallFootprints.TryGetUncoveredSpan(wallBoxes, horizontal, lineCoord, spanMin, spanMax, out var gap))
			{
				return; // Fully sealed.
			}

			if (!TryFillEdgeGap(horizontal, lineCoord, gap, wallBoxes))
			{
				break;
			}
		}

		if (WallFootprints.TryGetUncoveredSpan(wallBoxes, horizontal, lineCoord, spanMin, spanMax, out var remaining))
		{
			GD.PrintErr($"Wall gap could not be sealed on the {(horizontal ? "horizontal" : "vertical")} edge at "
				+ $"{(horizontal ? "z" : "x")}={lineCoord}, span [{remaining.A:F1},{remaining.B:F1}] near {wall.Position}: "
				+ "no free cell on the edge row fits a covering wall piece.");
		}
	}

	/// <summary>
	/// Places the single candidate piece (full or half wall, any orientation valid for
	/// the edge axis, anchored at any free cell along the edge row) whose measured mesh
	/// footprint covers the most of <paramref name="gap"/>. Candidates are judged by
	/// their real rotated AABB footprint, never by which cell they anchor on.
	/// </summary>
	private bool TryFillEdgeGap(bool horizontal, float lineCoord, (float A, float B) gap, List<Aabb> wallBoxes)
	{
		MeshLibrary library = WallGridMap.MeshLibrary;
		if (library == null)
		{
			return false;
		}

		(int Item, int Orientation)[] candidates = horizontal
			? new[]
			{
				(TileFactory.GetWallTileIndex(), HorizontalWallOrientation),
				(TileFactory.GetWallHalfTileIndex(), HorizontalWallOrientation),
				(TileFactory.GetWallHalfTileIndex(), HorizontalWallWestHalfOrientation),
			}
			: new[]
			{
				(TileFactory.GetWallTileIndex(), VerticalWallOrientation),
				(TileFactory.GetWallHalfTileIndex(), VerticalWallOrientation),
				(TileFactory.GetWallHalfTileIndex(), VerticalWallSouthHalfOrientation),
			};

		float bestOverlap = WallCoverageEps;
		Vector3I bestCell = default;
		(int Item, int Orientation) bestCandidate = default;
		Aabb bestFootprint = default;

		int gapCenter = Mathf.RoundToInt((gap.A + gap.B) / 2f);
		for (int offset = -(int)TileSize / 2; offset <= (int)TileSize / 2; offset++)
		{
			var cell = horizontal
				? new Vector3I(gapCenter + offset, 0, Mathf.RoundToInt(lineCoord))
				: new Vector3I(Mathf.RoundToInt(lineCoord), 0, gapCenter + offset);
			if (WallGridMap.GetCellItem(cell) >= 0)
			{
				continue;
			}

			foreach (var candidate in candidates)
			{
				Mesh mesh = library.GetItemMesh(candidate.Item);
				if (mesh == null)
				{
					continue;
				}

				Aabb footprint = WallFootprints.FootprintWorld(mesh.GetAabb(), candidate.Orientation, cell);
				float perpMin = horizontal ? footprint.Position.Z : footprint.Position.X;
				float perpMax = horizontal ? footprint.End.Z : footprint.End.X;
				if (lineCoord < perpMin - WallCoverageEps || lineCoord > perpMax + WallCoverageEps)
				{
					continue; // Doesn't sit on the edge line.
				}

				float a = horizontal ? footprint.Position.X : footprint.Position.Z;
				float b = horizontal ? footprint.End.X : footprint.End.Z;
				float overlap = Mathf.Min(b, gap.B) - Mathf.Max(a, gap.A);
				if (overlap > bestOverlap)
				{
					bestOverlap = overlap;
					bestCell = cell;
					bestCandidate = candidate;
					bestFootprint = footprint;
				}
			}
		}

		if (bestOverlap <= WallCoverageEps)
		{
			return false;
		}

		WallGridMap.SetCellItem(bestCell, bestCandidate.Item, bestCandidate.Orientation);
		wallBoxes.Add(bestFootprint);
		return true;
	}

	private void PlaceWallModulesForTile(int x, int z, System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, HashSet<WallStraightRequest> straightWalls, List<Aabb> authoredWallBoxes)
	{
		int tileCenter = (int)TileSize / 2;
		var basePosition = TileToWorld(x, 0, z);
		int westX = basePosition.X - tileCenter;
		int eastX = basePosition.X + tileCenter;
		int northZ = basePosition.Z - tileCenter;
		int southZ = basePosition.Z + tileCenter;

		// Generate a wall on each edge that faces void, or that needs interior separation
		// (an unsanctioned room/corridor contact; see MapData.RequiresInteriorWall) --
		// unless the room template already authored a wall that fully covers that edge
		// (checked against real mesh footprints).
		bool north = NeedsWallToward(x, z, 0, -1) && !EdgeFullyAuthored(authoredWallBoxes, horizontal: true, northZ, westX, eastX);
		bool south = NeedsWallToward(x, z, 0, 1) && !EdgeFullyAuthored(authoredWallBoxes, horizontal: true, southZ, westX, eastX);
		bool west = NeedsWallToward(x, z, -1, 0) && !EdgeFullyAuthored(authoredWallBoxes, horizontal: false, westX, northZ, southZ);
		bool east = NeedsWallToward(x, z, 1, 0) && !EdgeFullyAuthored(authoredWallBoxes, horizontal: false, eastX, northZ, southZ);

		if (north)
		{
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(westX, northZ), extendsEast: true);
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(eastX, northZ), extendsEast: false);
			straightWalls.Add(new(basePosition + new Vector3I(0, 0, -tileCenter), HorizontalWallOrientation));
		}

		if (south)
		{
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(westX, southZ), extendsEast: true);
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(eastX, southZ), extendsEast: false);
			straightWalls.Add(new(basePosition + new Vector3I(0, 0, tileCenter), HorizontalWallOrientation));
		}

		if (west)
		{
			AddVerticalCornerEdge(cornerEdges, new Vector2I(westX, northZ), extendsSouth: true);
			AddVerticalCornerEdge(cornerEdges, new Vector2I(westX, southZ), extendsSouth: false);
			straightWalls.Add(new(basePosition + new Vector3I(-tileCenter, 0, 0), VerticalWallOrientation));
		}

		if (east)
		{
			AddVerticalCornerEdge(cornerEdges, new Vector2I(eastX, northZ), extendsSouth: true);
			AddVerticalCornerEdge(cornerEdges, new Vector2I(eastX, southZ), extendsSouth: false);
			straightWalls.Add(new(basePosition + new Vector3I(tileCenter, 0, 0), VerticalWallOrientation));
		}
	}

	private void AddHorizontalCornerEdge(System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, Vector2I position, bool extendsEast)
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

	private void AddVerticalCornerEdge(System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, Vector2I position, bool extendsSouth)
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

	private void PlaceWallCorners(System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges)
	{
		foreach (var (position, edges) in cornerEdges)
		{
			if (edges.HorizontalEast && edges.VerticalSouth)
			{
				PlaceWallCorner(position, NorthWestCornerOrientation);
			}
			else if (edges.HorizontalWest && edges.VerticalSouth)
			{
				PlaceWallCorner(position, NorthEastCornerOrientation);
			}
			else if (edges.HorizontalEast && edges.VerticalNorth)
			{
				PlaceWallCorner(position, SouthWestCornerOrientation);
			}
			else if (edges.HorizontalWest && edges.VerticalNorth)
			{
				PlaceWallCorner(position, SouthEastCornerOrientation);
			}
		}
	}

	private void PlaceWallCorner(Vector2I position, int orientation)
	{
		// A corner is pure polish at the join of two edges; the full-width straight walls
		// already seal both edges. So just drop a corner mesh on the corner cell when it is
		// free, and otherwise leave it. The previous code fell back to scattering half-walls
		// into neighbouring cells, which could occupy a straight wall's midpoint cell and
		// leave that edge with a gap.
		var cellPosition = new Vector3I(position.X, 0, position.Y);
		if (WallGridMap.GetCellItem(cellPosition) < 0)
		{
			WallGridMap.SetCellItem(cellPosition, TileFactory.GetWallCornerTileIndex(), orientation);
		}
	}

	private const float WallCoverageEps = WallFootprints.Eps;

	/// <summary>
	/// World-space XZ footprints of every wall currently in the grid. Called before
	/// generation (yielding the room-authored walls, to skip edges they already seal)
	/// and again after the fast placement pass (yielding authored + generated, as the
	/// baseline for the coverage-patching pass).
	/// </summary>
	private List<Aabb> CollectWallFootprints()
	{
		return WallFootprints.Collect(WallGridMap);
	}

	/// <summary>
	/// True when authored wall footprints fully cover a logical tile edge. The edge runs
	/// along X at z=<paramref name="lineCoord"/> when <paramref name="horizontal"/>, else
	/// along Z at x=<paramref name="lineCoord"/>; <paramref name="spanMin"/>..spanMax is its
	/// 4-unit extent on that axis.
	/// </summary>
	private static bool EdgeFullyAuthored(List<Aabb> authoredWallBoxes, bool horizontal, float lineCoord, float spanMin, float spanMax)
	{
		return !WallFootprints.TryGetUncoveredSpan(authoredWallBoxes, horizontal, lineCoord, spanMin, spanMax, out _);
	}

	private readonly record struct WallStraightRequest(Vector3I Position, int Orientation);

	private bool IsWallSourceTile(int x, int z)
	{
		return Map.IsWithinBounds(x, z)
			&& (Map.IsRoom(x, z) || Map.IsConnector(x, z) || Map.IsCorridor(x, z));
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

	private struct WallCornerEdges
	{
		public bool HorizontalWest;
		public bool HorizontalEast;
		public bool VerticalNorth;
		public bool VerticalSouth;
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
	/// The shared unlit black cap covering one tile footprint. Used both by the static
	/// occlusion grid and by the temporary overlay that fades a revealed area into view.
	/// Visible from both sides so the cap reads as black at any camera yaw.
	/// </summary>
	private PlaneMesh OcclusionCapMesh => _occlusionCapMesh ??= new PlaneMesh
	{
		Size = new Vector2(TileSize, TileSize),
		Material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.Black,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		},
	};

	/// <summary>
	/// Places the black occluder caps across the map (plus a margin band so edge
	/// rooms' outer walls stay hidden). When <paramref name="fog"/> is set every
	/// tile is covered so rooms start hidden and are revealed during play; without
	/// it only the void/border is covered and interior surfaces stay visible.
	/// </summary>
	private void PlaceOcclusion(bool fog)
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
				bool isVisibleInterior = Map.IsWithinBounds(x, z)
					&& (Map.IsRoom(x, z) || Map.IsConnector(x, z)
						|| Map.IsCorridor(x, z) || Map.IsChasm(x, z));
				if (fog || !isVisibleInterior)
				{
					OcclusionGridMap.SetCellItem(TileToWorld(x, 0, z), OcclusionItemId, 0);
				}
			}
		}
	}

	/// <summary>
	/// Reveals a room and everything reachable from it without crossing a door:
	/// the room footprint, its connected corridors, and any further rooms joined by
	/// door-free connectors. A doored connector blocks the cascade so the corridor
	/// beyond a closed door stays hidden, but the doorway tile itself (part of the
	/// room) is revealed; the closed door and the hidden corridor are the seal.
	/// When <paramref name="animate"/> is set the newly uncovered tiles fade out
	/// together; pass false to apply the reveal instantly (e.g. restoring a save).
	/// </summary>
	public void RevealRoom(int roomId, bool animate = true)
	{
		if (roomId < 0 || roomId >= _roomRegions.Count || OcclusionGridMap == null)
		{
			return;
		}

		// Batch the tiles uncovered by this call so they can fade out as one overlay.
		// Guard against re-entrancy so a single batch spans the whole cascade.
		bool ownsBatch = animate && _revealBatch == null;
		if (ownsBatch)
		{
			_revealBatch = new List<Vector2I>();
		}

		var roomQueue = new Queue<int>();
		var queuedRooms = new HashSet<int> { roomId };
		roomQueue.Enqueue(roomId);

		while (roomQueue.Count > 0)
		{
			var region = _roomRegions[roomQueue.Dequeue()];
			foreach (var tile in region.Tiles)
			{
				RevealTile(tile);
			}

			foreach (var connector in region.ConnectorTiles)
			{
				if (_dooredConnectors.Contains(connector))
				{
					continue; // The door gates the cascade.
				}

				FloodCorridors(connector, roomQueue, queuedRooms);
			}
		}

		UpdateDoorIndicators();

		if (ownsBatch)
		{
			SpawnRevealFade(_revealBatch);
			_revealBatch = null;
		}
	}

	/// <summary>
	/// Shows a door's x-ray indicator only once the area on at least one side of it
	/// has been revealed, so doors still buried in the fog don't leak their location.
	/// </summary>
	private void UpdateDoorIndicators()
	{
		foreach (var (door, connector) in _doorIndicators)
		{
			door.SetIndicatorVisible(HasRevealedNeighbor(connector));
		}
	}

	private bool HasRevealedNeighbor(Vector2I tile)
	{
		foreach (var offset in CardinalOffsets)
		{
			if (_revealedTiles.Contains(tile + offset))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Unseals the doored connector nearest the opened door and reveals through it,
	/// so opening a door flows the fog into the corridor and rooms beyond. Returns
	/// the unsealed connector tile (for persistence), or null if none matched.
	/// </summary>
	public Vector2I? OpenDoorAt(Vector3 worldPosition)
	{
		if (!TryFindNearestDoorConnector(worldPosition, out Vector2I connectorTile))
		{
			return null;
		}

		OpenConnector(connectorTile);
		return connectorTile;
	}

	/// <summary>
	/// Marks the connector guarded by a closed door as sealed again. Fog remains revealed;
	/// this only updates logical reachability for enemy decisions.
	/// </summary>
	public Vector2I? CloseDoorAt(Vector3 worldPosition)
	{
		if (!TryFindNearestDoorConnector(worldPosition, out Vector2I connectorTile))
		{
			return null;
		}

		_dooredConnectors.Add(connectorTile);
		UpdateDoorIndicators();
		return connectorTile;
	}

	private bool TryFindNearestDoorConnector(Vector3 worldPosition, out Vector2I connectorTile)
	{
		connectorTile = default;
		float bestDistance = float.MaxValue;
		bool found = false;
		foreach (var (door, connector) in _doorIndicators)
		{
			float distance = HorizontalDistance(worldPosition, door.GlobalPosition);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				connectorTile = connector;
				found = true;
			}
		}

		return found;
	}

	/// <summary>
	/// Restores a previously explored level: re-opens the doors that were opened and
	/// re-reveals the rooms that were entered. Combined with deterministic map
	/// generation, this reproduces the exact area the player had uncovered.
	/// </summary>
	public void RestoreReveal(IEnumerable<int> revealedRoomIds, IEnumerable<Vector2I> openedDoors)
	{
		// Unseal every opened door first so cascades can flow through all of them.
		foreach (var connector in openedDoors)
		{
			_dooredConnectors.Remove(connector);
		}

		foreach (var roomId in revealedRoomIds)
		{
			RevealRoom(roomId, animate: false);
		}

		// Reveal through doors opened from the corridor side (no room was entered).
		foreach (var connector in openedDoors)
		{
			if (_connectorToRoom.TryGetValue(connector, out int roomId))
			{
				RevealRoom(roomId, animate: false);
			}
		}
	}

	/// <summary>
	/// Clears gameplay fog across the generated level for runtime debugging.
	/// </summary>
	public void RevealAllFog()
	{
		if (OcclusionGridMap == null)
		{
			return;
		}

		OcclusionGridMap.Clear();
		PlaceOcclusion(false);

		foreach (var (door, _) in _doorIndicators)
		{
			door.SetIndicatorVisible(true);
		}
	}

	private void OpenConnector(Vector2I connector)
	{
		_dooredConnectors.Remove(connector);
		RevealRoom(_connectorToRoom[connector]);
	}

	private void FloodCorridors(Vector2I startConnector, Queue<int> roomQueue,
		HashSet<int> queuedRooms)
	{
		var queue = new Queue<Vector2I>();
		var visited = new HashSet<Vector2I>();

		foreach (var offset in CardinalOffsets)
		{
			// Gate the seed the same way the cascade gates arriving at a connector
			// (below): a corridor can pass alongside startConnector's walled,
			// non-sanctioned side (RequiresInteriorWall keeps that contact sealed), and
			// seeding from it would reveal that corridor tile through solid wall.
			if (!ConnectorOpensToward(startConnector, offset))
			{
				continue;
			}

			var seed = startConnector + offset;
			if (Map.IsWithinBounds(seed.X, seed.Y) && Map.IsCorridor(seed.X, seed.Y)
				&& visited.Add(seed))
			{
				queue.Enqueue(seed);
			}
		}

		while (queue.Count > 0)
		{
			var tile = queue.Dequeue();
			RevealTile(tile);
			foreach (var offset in CardinalOffsets)
			{
				var n = tile + offset;
				if (!Map.IsWithinBounds(n.X, n.Y))
				{
					continue;
				}

				if (Map.IsCorridor(n.X, n.Y))
				{
					if (visited.Add(n))
					{
						queue.Enqueue(n);
					}
				}
				else if (Map.IsConnector(n.X, n.Y))
				{
					// Reached another room. A doored connector stays sealed (black); an
					// open one cascades into that room -- but only through one of its own
					// sanctioned directions, so a corridor merely passing alongside a
					// connector's walled (non-open) side does not leak the reveal through
					// that wall (see MapData.RequiresInteriorWall).
					if (!_dooredConnectors.Contains(n)
						&& ConnectorOpensToward(n, -offset)
						&& _connectorToRoom.TryGetValue(n, out int otherRoom)
						&& queuedRooms.Add(otherRoom))
					{
						roomQueue.Enqueue(otherRoom);
					}
				}
			}
		}
	}

	private void RevealTile(Vector2I tile)
	{
		if (!_revealedTiles.Add(tile))
		{
			return;
		}

		// -1 clears the cell (GridMap.InvalidCellItem).
		OcclusionGridMap.SetCellItem(TileToWorld(tile.X, 0, tile.Y), -1);

		// Hand the just-cleared cap to the fade overlay so it eases out instead of popping.
		_revealBatch?.Add(tile);
	}

	/// <summary>
	/// Spawns a short-lived black overlay covering the just-revealed tiles and fades it
	/// out, so a newly explored area eases into view. The caps were already cleared from
	/// the static occlusion grid; this batched <see cref="MultiMeshInstance3D"/> stands in
	/// for them only for the fade, then frees itself. It carries its own alpha-blended
	/// material override (starting fully opaque, so there is no flash when it takes over
	/// from the grid), leaving the shared opaque cap material untouched.
	/// </summary>
	private void SpawnRevealFade(List<Vector2I> tiles)
	{
		if (tiles == null || tiles.Count == 0 || OcclusionGridMap == null)
		{
			return;
		}

		var multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = OcclusionCapMesh,
			InstanceCount = tiles.Count,
		};

		for (int i = 0; i < tiles.Count; i++)
		{
			Vector3 origin = TileToWorld(tiles[i].X, 0, tiles[i].Y);
			origin.Y += OcclusionCapHeight;
			multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, origin));
		}

		// A fresh alpha-blended material override drives the fade without mutating the
		// shared cap material that the static occlusion grid still relies on.
		var fadeMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.Black,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		var overlay = new MultiMeshInstance3D
		{
			Multimesh = multiMesh,
			MaterialOverride = fadeMaterial,
		};

		// Parent to the occlusion grid so the caps share its coordinate space and align
		// exactly with the cells they replace.
		OcclusionGridMap.AddChild(overlay);

		var tween = overlay.CreateTween();
		tween.TweenProperty(fadeMaterial, "albedo_color:a", 0.0f, FogRevealFadeSeconds)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		tween.Finished += overlay.QueueFree;
	}

	private void SetPlayerSpawnPoint()
	{
		// Find the player spawn point on the map
		// TODO: This is a hack to find the player spawn point
		PlayerSpawnPoint = FindChild("PlayerSpawnPoint", true, false) as PlayerSpawnPoint;
		if (PlayerSpawnPoint == null)
		{
			GD.PrintErr("PlayerSpawnPoint not found in the scene.");
		}
	}

	/// <summary>
	/// Scatters chests, traps, and loose ground items across the finished map. Runs after
	/// walls/corridors are final (so wall-adjacency and occupancy checks are accurate) and
	/// before <see cref="BakeNavigationMesh"/> (so chests -- solid, unlike traps/loose items
	/// -- are added as children of <see cref="NavigationRegion"/> in time to be baked in as
	/// navmesh obstacles, the same way rooms already are). Every content type shares one
	/// running list of placed points so chests/traps/items all keep clear of each other, not
	/// just their own kind.
	/// </summary>
	private void PlaceLoot()
	{
		var placedLootPoints = new List<Vector3>();
		PlaceChests(placedLootPoints);
		PlaceTraps(placedLootPoints);
		PlaceLooseItems(placedLootPoints);
	}

	private void PlaceChests(List<Vector3> placedLootPoints)
	{
		if (ChestScene == null || MaxChests <= 0)
		{
			return;
		}

		var candidates = GetChestCandidates();
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("chests", "no valid wall-adjacent procedural room tiles found");
			return;
		}

		int count = GD.RandRange(MinChests, MaxChests);
		var remaining = new List<(Vector3 Point, float RotationDegrees)>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = GD.RandRange(0, remaining.Count - 1);
			var candidate = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(candidate.Point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			var chest = ChestScene.Instantiate<Node3D>();
			// A chest is a solid StaticBody3D: parent it under NavigationRegion (like rooms
			// and the GridMap copies BakeNavigationMesh adds) so it becomes a navmesh
			// obstacle. Do NOT route this through SpawnPoint: it parents under Level instead
			// (missing the navmesh bake) and unconditionally applies an extra 180-degree
			// yaw on spawn, which would silently point the chest the wrong way.
			NavigationRegion.AddChild(chest);
			// Raised onto the floor's visual surface: a static prop doesn't settle via
			// physics the way characters do, so at y=0 its base sits buried inside the
			// floor mesh.
			chest.GlobalPosition = candidate.Point + Vector3.Up * GetFloorSurfaceHeight(candidate.Point);
			chest.RotationDegrees = new Vector3(0, candidate.RotationDegrees, 0);

			placedLootPoints.Add(candidate.Point);
			placed++;
		}

		if (placed < MinChests)
		{
			GD.PrintErr($"Placed only {placed}/{MinChests} minimum chests: ran out of valid spots.");
		}
	}

	private void PlaceTraps(List<Vector3> placedLootPoints)
	{
		if (TrapScene == null || MaxTraps <= 0)
		{
			return;
		}

		var candidates = GetOpenTileCandidates(proceduralRoomsOnly: true);
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("traps", "no valid open procedural room or corridor tiles found");
			return;
		}

		int count = GD.RandRange(MinTraps, MaxTraps);
		var remaining = new List<Vector3>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = GD.RandRange(0, remaining.Count - 1);
			var point = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			// Traps deliberately allow room/corridor chokepoint tiles (unlike chests) -- a
			// trap you're forced to cross is the point. No facing requirement either. The
			// floor plate mesh is square and its texture is grid-aligned, matching the
			// room's own floor tiles -- rotating it off-axis visibly breaks that seam
			// alignment (the plate's edges no longer line up with its floor neighbors),
			// which read as an oddly angled, "floating" tile rather than a natural part
			// of the floor. Leave it unrotated, like any other floor tile.
			var trap = TrapScene.Instantiate<Node3D>();
			NavigationRegion.AddChild(trap);
			trap.GlobalPosition = point;

			placedLootPoints.Add(point);
			placed++;
		}

		if (placed < MinTraps)
		{
			GD.PrintErr($"Placed only {placed}/{MinTraps} minimum traps: ran out of valid spots.");
		}
	}

	private void PlaceLooseItems(List<Vector3> placedLootPoints)
	{
		if (LooseItemScene == null || LooseItemLootTable == null || MaxLooseItems <= 0)
		{
			return;
		}

		var candidates = GetLooseItemCandidates();
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("loose items", "no valid procedural room tiles found");
			return;
		}

		// Loot *rolls* (what's inside) stay on the run's own seeded loot sequence, the same
		// one LootTableComponent.DropLoot draws from -- kept separate from the dungeon-layout
		// RNG so rebalancing loot tables never perturbs level layout, and vice versa.
		RandomNumberGenerator rng = GameSession.Instance?.CreateLootRng();
		if (rng == null)
		{
			rng = new RandomNumberGenerator();
			rng.Randomize();
		}

		uint depth = GameSession.Instance?.ActiveDungeonDepth ?? DungeonDepth;
		int count = GD.RandRange(MinLooseItems, MaxLooseItems);
		var remaining = new List<Vector3>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = GD.RandRange(0, remaining.Count - 1);
			var point = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			var entry = LooseItemLootTable.PickWeightedEntry(rng);
			if (entry == null)
			{
				break; // Table is empty; no point trying further candidates.
			}

			Item item = LootRoller.Roll(entry.Item, depth, rng);
			var lootableItem = LooseItemScene.Instantiate<LootableItem>();
			lootableItem.Item = item;
			lootableItem.Quantity = entry.Quantity;
			NavigationRegion.AddChild(lootableItem);
			lootableItem.GlobalPosition = point + Vector3.Up * GetFloorSurfaceHeight(point);

			placedLootPoints.Add(point);
			placed++;
		}

		if (placed < MinLooseItems)
		{
			GD.PrintErr($"Placed only {placed}/{MinLooseItems} minimum loose items: ran out of valid spots.");
		}
	}

	/// <summary>
	/// Every interior room tile with a wall on at least one cardinal side, paired with the
	/// Y-rotation that faces a chest away from that wall into the room -- excludes tiles
	/// adjacent to a connector for extra margin from doorway traffic. A tile with exactly one
	/// wall-adjacent side (a flat wall run) is preferred over a corner (two sides) for the
	/// cleanest single-direction facing; corners are only used as a fallback when no flat
	/// spot exists anywhere on the map.
	/// </summary>
	private List<(Vector3 Point, float RotationDegrees)> GetChestCandidates()
	{
		var flatCandidates = new List<(Vector3, float)>();
		var cornerCandidates = new List<(Vector3, float)>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (!Map.IsRoom(x, z) || !IsProceduralRoomTile(x, z)
					|| IsOccupiedSpawnTile(x, z) || IsAdjacentToConnector(x, z))
				{
					continue;
				}

				var wallDirections = new List<Vector2I>();
				foreach (var offset in CardinalOffsets)
				{
					int nx = x + offset.X, nz = z + offset.Y;
					// Wall placement has already sealed every void-facing and unsanctioned
					// interior edge by this point in GenerateMap, so a non-walkable
					// neighbor -- including the map border -- is solid wall geometry to back
					// a chest against. Chasm is the one exception: it's deliberately left
					// unwalled (pits are open), so a chasm-adjacent tile has nothing behind
					// it and must not count as a wall direction.
					if (Map.IsWithinBounds(nx, nz) && Map.IsChasm(nx, nz))
					{
						continue;
					}

					if (!Map.IsWithinBounds(nx, nz) || !Map.IsWalkable(nx, nz))
					{
						wallDirections.Add(offset);
					}
				}

				if (wallDirections.Count == 0)
				{
					continue;
				}

				var point = TileToWorld(x, 0, z);
				// GetYRotationDegrees assumes its prop is authored facing North (0deg) --
				// true for doors/markers, but chest.tscn's lid/opening faces the opposite
				// way at rotation 0. Rotating toward the wall direction itself (not away
				// from it) compensates for that -- equivalent to "away + 180" but without
				// a sum that can exceed 360 (RotationDegrees.Y doesn't normalize on a
				// direct assignment, so a raw 450 would stay 450, not wrap to 90).
				float rotation = DoorwayMarker.GetYRotationDegrees(DoorwayMarker.GetDirectionFlag(wallDirections[0]));
				(wallDirections.Count == 1 ? flatCandidates : cornerCandidates).Add((point, rotation));
			}
		}

		return flatCandidates.Count > 0 ? flatCandidates : cornerCandidates;
	}

	private List<Vector3> GetLooseItemCandidates()
	{
		var candidates = new List<Vector3>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (Map.IsRoom(x, z) && IsProceduralRoomTile(x, z) && !IsOccupiedSpawnTile(x, z))
				{
					candidates.Add(TileToWorld(x, 0, z));
				}
			}
		}

		return candidates;
	}

	/// <summary>
	/// True when tile (x,z) belongs to a procedurally built room, or to no tracked room
	/// region at all (e.g. a corridor tile, which is never part of any room). False only
	/// for a tile that belongs to an authored room -- see RoomRegion.IsProcedural.
	/// </summary>
	private bool IsProceduralRoomTile(int x, int z)
	{
		return !_tileToRoom.TryGetValue(new Vector2I(x, z), out int roomId) || _roomRegions[roomId].IsProcedural;
	}

	/// <summary>
	/// Generation-time loot only goes into procedural rooms (authored rooms carry their
	/// own hand-placed content), so a map whose standard rooms all came from authored
	/// scenes legitimately places nothing -- report that quietly. Zero candidates on a
	/// map that DOES contain procedural rooms means something real broke, so that stays
	/// a loud error.
	/// </summary>
	private void ReportNoLootCandidates(string lootKind, string reason)
	{
		bool anyProceduralRoom = false;
		foreach (var region in _roomRegions)
		{
			if (region.IsProcedural)
			{
				anyProceduralRoom = true;
				break;
			}
		}

		if (anyProceduralRoom)
		{
			GD.PrintErr($"Cannot place {lootKind}: {reason}.");
		}
		else
		{
			GD.Print($"Skipping {lootKind}: no procedural rooms in this map (authored rooms keep their hand-placed content).");
		}
	}

	/// <summary>
	/// The walkable top surface of the floor at a world position, in world units above
	/// y=0 -- what a prop must be raised by to stand ON the floor instead of having its
	/// base buried inside the floor mesh (whose visual surface sits above its y=0 anchor
	/// plane; ~0.05 for tile/wood floors, ~0.11 for dirt). Measured from the actual
	/// floor mesh's AABB at that tile, capped because decorated variants (e.g.
	/// floor_dirt_large_rocky's scattered rocks) inflate the AABB far above the actual
	/// walkable plane.
	/// </summary>
	private float GetFloorSurfaceHeight(Vector3 worldPoint)
	{
		const float MaxSurfaceHeight = 0.12f;

		MeshLibrary library = FloorGridMap.MeshLibrary;
		if (library == null)
		{
			return 0f;
		}

		// A tile's floor is anchored somewhere within its 4x4 cell block: at the tile
		// center for tile-sized meshes, at center±1 for half-tile meshes.
		var center = new Vector3I(Mathf.RoundToInt(worldPoint.X), 0, Mathf.RoundToInt(worldPoint.Z));
		float height = 0f;
		for (int dx = -(int)TileSize / 2; dx < (int)TileSize / 2; dx++)
		{
			for (int dz = -(int)TileSize / 2; dz < (int)TileSize / 2; dz++)
			{
				int item = FloorGridMap.GetCellItem(center + new Vector3I(dx, 0, dz));
				if (item < 0)
				{
					continue;
				}

				Mesh mesh = library.GetItemMesh(item);
				if (mesh == null)
				{
					continue;
				}

				Aabb aabb = mesh.GetAabb();
				height = Mathf.Max(height, Mathf.Min(aabb.Position.Y + aabb.Size.Y, MaxSurfaceHeight));
			}
		}

		return height;
	}

	private bool IsAdjacentToConnector(int x, int z)
	{
		foreach (var offset in CardinalOffsets)
		{
			int nx = x + offset.X, nz = z + offset.Y;
			if (Map.IsWithinBounds(nx, nz) && Map.IsConnector(nx, nz))
			{
				return true;
			}
		}

		return false;
	}

	private bool IsBlockedLootPoint(Vector3 point, List<Vector3> placedLootPoints, float spacing)
	{
		if (PlayerSpawnPoint != null && HorizontalDistance(point, PlayerSpawnPoint.GlobalPosition) < LootSpawnPlayerClearance)
		{
			return true;
		}

		foreach (var placed in placedLootPoints)
		{
			if (HorizontalDistance(point, placed) < spacing)
			{
				return true;
			}
		}

		return false;
	}

	private void GenerateEnemySpawnPoints()
	{
		uint mobCount = 3 + DungeonDepth % 5 + (GD.Randi() % 3);
		GD.Print($"Generating {mobCount} enemy spawn points...");

		if (NavigationRegion.NavigationMesh.GetVertices().Length == 0)
		{
			GD.PrintErr("Cannot generate enemy spawn points: navigation mesh has no vertices.");
			return;
		}

		var candidates = GetOpenTileCandidates(proceduralRoomsOnly: false);
		if (candidates.Count == 0)
		{
			GD.PrintErr("Cannot generate enemy spawn points: no valid map tiles found.");
			return;
		}

		var spawnPoints = new List<Vector3>();
		for (int i = 0; i < mobCount; i++)
		{
			if (!TryPickEnemySpawnPoint(candidates, spawnPoints, out var point))
			{
				GD.PrintErr("Could not find a valid enemy spawn point.");
				continue;
			}

			spawnPoints.Add(point);

			// Enemy scenes are loaded lazily by the factory to keep unused variants out of memory.
			var enemyScene = MobFactory.CreateEnemy(DungeonDepth);
			if (enemyScene == null)
			{
				continue;
			}

			var spawnPointNode = new SpawnPoint();
			spawnPointNode.SpawnOnStart = true;
			spawnPointNode.Scenes = [enemyScene];
			spawnPointNode.PersistentId = $"depth:{DungeonDepth}:monster:{i}";
			AddChild(spawnPointNode);

			spawnPointNode.GlobalPosition = point;
			spawnPointNode.Rotation = new Vector3(0, (float)(GD.Randf() * 2 * Math.PI), 0);

			EnemySpawnPoints.Add(spawnPointNode);
		}
	}

	/// <summary>
	/// Every open (room or corridor), unoccupied tile's world position -- shared by enemy
	/// spawning and trap placement, both of which are happy to land on a chokepoint.
	/// Corridor tiles are never part of any room, authored or not, so <paramref
	/// name="proceduralRoomsOnly"/> only ever excludes a room tile -- pass true for
	/// generation-time loot (an authored room brings its own hand-placed content the
	/// generic scan knows nothing about), false for enemy spawning, which has always
	/// used every room regardless of origin and should keep doing so.
	/// </summary>
	private List<Vector3> GetOpenTileCandidates(bool proceduralRoomsOnly)
	{
		var candidates = new List<Vector3>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				bool isOpenRoomTile = Map.IsRoom(x, z) && (!proceduralRoomsOnly || IsProceduralRoomTile(x, z));
				if ((isOpenRoomTile || Map.IsCorridor(x, z)) && !IsOccupiedSpawnTile(x, z))
				{
					candidates.Add(TileToWorld(x, 0, z));
				}
			}
		}

		return candidates;
	}

	private bool IsOccupiedSpawnTile(int x, int z)
	{
		return HasDecorationInTile(x, z) || HasPropInTile(x, z);
	}

	/// <summary>
	/// Picks a uniformly random free floor/corridor tile anywhere on the map, for effects
	/// like a teleport scroll that relocate the player rather than spawn near a point.
	/// Uses its own RNG rather than the run's seeded loot RNG, since a teleport's outcome
	/// should not consume or depend on the loot-roll sequence.
	/// </summary>
	public bool TryPickRandomFreePosition(out Vector3 position)
	{
		List<Vector3> candidates = GetOpenTileCandidates(proceduralRoomsOnly: false);
		if (candidates.Count == 0)
		{
			position = default;
			return false;
		}

		var rng = new RandomNumberGenerator();
		rng.Randomize();
		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int j = rng.RandiRange(0, i);
			(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}

		foreach (Vector3 candidate in candidates)
		{
			if (IsSpawnPositionFree(candidate))
			{
				position = candidate;
				return true;
			}
		}

		position = default;
		return false;
	}

	/// <summary>
	/// Finds a free world position close to <paramref name="origin"/> for a runtime spawn. Probes the
	/// origin first, then samples points on expanding rings out to <paramref name="maxRadius"/> world
	/// units, returning the nearest one that sits on a room/corridor floor tile whose column is clear
	/// of any solid collider (walls, props, stairs, transition blockers, etc.). Falls back to
	/// <paramref name="origin"/> when nothing nearby is free, so the item still drops next to the
	/// player rather than teleporting across the map.
	/// </summary>
	public Vector3 FindFreeSpawnPositionNear(Vector3 origin, float maxRadius = 6f)
	{
		if (Map == null || IsSpawnPositionFree(origin))
		{
			return origin;
		}

		const int samplesPerRing = 12;
		const float ringStep = 1.0f;
		for (float radius = ringStep; radius <= maxRadius; radius += ringStep)
		{
			for (int i = 0; i < samplesPerRing; i++)
			{
				float angle = Mathf.Tau * i / samplesPerRing;
				var candidate = new Vector3(
					origin.X + Mathf.Cos(angle) * radius,
					origin.Y,
					origin.Z + Mathf.Sin(angle) * radius);

				if (IsSpawnPositionFree(candidate))
				{
					return candidate;
				}
			}
		}

		return origin;
	}

	private bool IsSpawnPositionFree(Vector3 worldPosition)
	{
		if (!IsWalkableMapPosition(worldPosition))
		{
			return false;
		}

		return !IsColumnObstructed(worldPosition);
	}

	/// <summary>
	/// Finds a nearby floor/corridor point for effects that can land near props or decorations.
	/// Unlike item spawns, this deliberately does not require a clear item-sized collision column.
	/// </summary>
	public bool TryFindEffectLandingPositionNear(Vector3 origin, float maxRadius, out Vector3 landingPosition)
	{
		if (Map == null || IsEffectLandingPositionValid(origin))
		{
			landingPosition = origin;
			return true;
		}

		if (TryProjectEffectLandingPosition(origin, out landingPosition))
		{
			return true;
		}

		const int samplesPerRing = 16;
		const float ringStep = 0.5f;
		for (float radius = ringStep; radius <= maxRadius; radius += ringStep)
		{
			for (int i = 0; i < samplesPerRing; i++)
			{
				float angle = Mathf.Tau * i / samplesPerRing;
				var candidate = new Vector3(
					origin.X + Mathf.Cos(angle) * radius,
					origin.Y,
					origin.Z + Mathf.Sin(angle) * radius);

				if (IsEffectLandingPositionValid(candidate))
				{
					landingPosition = candidate;
					return true;
				}

				if (TryProjectEffectLandingPosition(candidate, out landingPosition))
				{
					return true;
				}
			}
		}

		landingPosition = default;
		return false;
	}

	public bool IsEffectLandingPositionValid(Vector3 worldPosition)
	{
		return Map != null && IsWalkableMapPosition(worldPosition);
	}

	private bool TryProjectEffectLandingPosition(Vector3 worldPosition, out Vector3 landingPosition)
	{
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			landingPosition = default;
			return false;
		}

		var query = PhysicsRayQueryParameters3D.Create(
			worldPosition + Vector3.Up * EffectLandingRayHeight,
			worldPosition + Vector3.Down * EffectLandingRayDepth,
			EffectLandingSurfaceMask);
		query.HitFromInside = false;
		var result = space.IntersectRay(query);
		if (result.Count == 0)
		{
			landingPosition = default;
			return false;
		}

		Vector3 normal = result["normal"].AsVector3();
		if (normal.Dot(Vector3.Up) < 0.35f)
		{
			landingPosition = default;
			return false;
		}

		Vector3 projectedPosition = result["position"].AsVector3();
		Node collider = result["collider"].As<Node>();
		if (!IsEffectLandingSurface(collider, projectedPosition))
		{
			landingPosition = default;
			return false;
		}

		landingPosition = projectedPosition;
		return true;
	}

	private bool IsEffectLandingSurface(Node collider, Vector3 worldPosition)
	{
		if (collider == FloorGridMap)
		{
			return IsWalkableMapPosition(worldPosition);
		}

		return collider?.IsInGroup("stairs") == true && IsWithinMapBounds(worldPosition);
	}

	private bool IsWalkableMapPosition(Vector3 worldPosition)
	{
		var tile = WorldToTile(worldPosition);
		if (!IsWithinMapBounds(tile))
		{
			return false;
		}

		return Map.IsRoom(tile.X, tile.Y) || Map.IsCorridor(tile.X, tile.Y);
	}

	private bool IsWithinMapBounds(Vector3 worldPosition)
	{
		return IsWithinMapBounds(WorldToTile(worldPosition));
	}

	private bool IsWithinMapBounds(Vector2I tile)
	{
		return Map != null && tile.X >= 0 && tile.Y >= 0 && tile.X < Map.Width && tile.Y < Map.Height;
	}

	/// <summary>
	/// Returns true when a solid collider occupies the column at <paramref name="worldPosition"/>.
	/// This is a physics overlap against the world, walls, and props layers, so it treats stairs,
	/// transition blockers, decorations, and props uniformly as "occupied" without enumerating each
	/// object category. The probe is item-sized and lifted off the floor so the flat floor collider
	/// is ignored and free space right beside an obstacle still qualifies.
	/// </summary>
	private bool IsColumnObstructed(Vector3 worldPosition)
	{
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			return false;
		}

		var shape = new BoxShape3D
		{
			Size = new Vector3(0.8f, 1.6f, 0.8f),
		};

		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = shape,
			Transform = new Transform3D(Basis.Identity, worldPosition + Vector3.Up * 1.0f),
			CollisionMask = SpawnObstructionMask,
			CollideWithBodies = true,
			CollideWithAreas = false,
		};

		return space.IntersectShape(query, 1).Count > 0;
	}

	private bool HasDecorationInTile(int x, int z)
	{
		var center = TileToWorld(x, 0, z);
		int halfTileSize = (int)TileSize / 2;
		for (int dx = -halfTileSize; dx < halfTileSize; dx++)
		{
			for (int dz = -halfTileSize; dz < halfTileSize; dz++)
			{
				var cell = new Vector3I(center.X + dx, center.Y, center.Z + dz);
				if (DecorationGridMap.GetCellItem(cell) >= 0)
				{
					return true;
				}
			}
		}

		return false;
	}

	private bool HasPropInTile(int x, int z)
	{
		var center = TileToWorld(x, 0, z);
		foreach (Node node in GetTree().GetNodesInGroup("prop"))
		{
			if (node is Node3D node3D && HorizontalDistance(center, node3D.GlobalPosition) < EnemySpawnPropClearance)
			{
				return true;
			}
		}

		return false;
	}

	private bool TryPickEnemySpawnPoint(IReadOnlyList<Vector3> candidates, List<Vector3> spawnPoints, out Vector3 point)
	{
		point = Vector3.Zero;
		var remainingCandidates = new List<Vector3>(candidates);
		while (remainingCandidates.Count > 0)
		{
			int index = (int)(GD.Randi() % (ulong)remainingCandidates.Count);
			var candidate = remainingCandidates[index];
			remainingCandidates.RemoveAt(index);

			if (IsBlockedEnemySpawnPoint(candidate, spawnPoints))
			{
				continue;
			}

			point = candidate;
			return true;
		}

		return false;
	}

	private bool IsBlockedEnemySpawnPoint(Vector3 point, List<Vector3> spawnPoints)
	{
		if (PlayerSpawnPoint != null && HorizontalDistance(point, PlayerSpawnPoint.GlobalPosition) < EnemySpawnPlayerClearance)
		{
			return true;
		}

		foreach (var spawnPoint in spawnPoints)
		{
			if (HorizontalDistance(point, spawnPoint) < EnemySpawnPointSpacing)
			{
				return true;
			}
		}

		foreach (Node node in FindChildren("*", "", true, false))
		{
			if (node is not Node3D node3D)
			{
				continue;
			}

			if ((node is PlayerSpawnPoint || node is LevelTransitionTrigger || node.IsInGroup("stairs"))
				&& HorizontalDistance(point, node3D.GlobalPosition) < EnemySpawnBlockedAreaClearance)
			{
				return true;
			}
		}

		return false;
	}

	private static float HorizontalDistance(Vector3 a, Vector3 b)
	{
		return new Vector2(a.X, a.Z).DistanceTo(new Vector2(b.X, b.Z));
	}

	public void GenerateMap(bool includeGameplay = true)
	{
		Reset();

		GD.Print("Generating map...");
		if (RoomLayout == null || CorridorConnector == null
			|| RoomFactory == null || MobFactory == null || TileFactory == null
			|| FloorGridMap == null || WallGridMap == null || DecorationGridMap == null)
		{
			// This is especially important to check in the editor
			return;
		}

		// Step 1: Generate random rooms
		GenerateRooms();

		// Step 2: Connect the rooms
		ConnectRooms();

		// Step 2b: Resolve doors against the routed corridors: keep doors at connected
		// doorways (and gate fog there), drop doors at doorways that got walled shut.
		FinalizeDoors();

		// Step 2b.2: Hide doorway-marker gizmos left pointing at a wall for the same reason.
		FinalizeMarkers();

		// Step 2c: Place the black occluder caps. In gameplay the whole map starts
		// covered (fog of war) and rooms are carved out as the player explores; in
		// the editor preview only the void is covered so the layout stays visible.
		PlaceOcclusion(includeGameplay);

		if (!includeGameplay)
		{
			GD.Print("Map preview generated.");
			return;
		}

		// Step 3: Find the player spawn point (moved ahead of the navmesh bake: loot
		// placement below needs it for clearance, and it's just a scene lookup with no
		// dependency on anything the bake computes).
		SetPlayerSpawnPoint();

		// Step 3b: Scatter chests/traps/loose items. Must run before BakeNavigationMesh so
		// chests are included as navmesh obstacles, and before GenerateEnemySpawnPoints so
		// enemy-spawn clearance (which already checks the "prop" group) naturally avoids them.
		PlaceLoot();

		// Step 4: Bake navigation mesh
		BakeNavigationMesh();

		// Step 5: Create enemy spawn points
		GenerateEnemySpawnPoints();

		GD.Print("Map generated.");
	}

	private void BakeNavigationMesh()
	{
		// Add grid maps back to the NavigationRegion and rebake the navigation mesh
		// TODO: Not sure why we cannot make the GridMaps children of the NavigationRegion directly
		// If we try, the thread seems to block indefinitely when making updates to the GridMaps
		Node floorGridMapCopy = FloorGridMap.Duplicate();
		Node wallGripMapCopy = WallGridMap.Duplicate();
		Node decorationGridMapCopy = DecorationGridMap.Duplicate();
		try
		{
			NavigationRegion.AddChild(floorGridMapCopy);
			NavigationRegion.AddChild(wallGripMapCopy);
			NavigationRegion.AddChild(decorationGridMapCopy);
			NavigationRegion.BakeNavigationMesh(false);
		}
		finally
		{
			floorGridMapCopy.QueueFree();
			wallGripMapCopy.QueueFree();
			decorationGridMapCopy.QueueFree();
		}
	}

	/// <summary>
	/// Shows or hides a translucent overlay of the baked navigation mesh. The overlay is built lazily
	/// from the current <see cref="NavigationRegion"/> mesh so it reflects the active level, and is
	/// rebuilt automatically if a regeneration discarded the previous instance.
	/// </summary>
	public void SetNavigationDebugVisible(bool visible)
	{
		if (NavigationRegion?.NavigationMesh == null)
		{
			return;
		}

		// Rebuild from scratch each time so door-link colours reflect current open/closed state
		// rather than whatever it was when the overlay was first shown.
		if (_navigationDebugMesh != null && IsInstanceValid(_navigationDebugMesh))
		{
			_navigationDebugMesh.QueueFree();
			_navigationDebugMesh = null;
		}

		if (visible)
		{
			_navigationDebugMesh = BuildNavigationDebugMesh();
		}
	}

	private MeshInstance3D BuildNavigationDebugMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		// Main baked region in blue.
		bool hasGeometry = AppendRegionTriangles(
			surfaceTool, NavigationRegion, new Color(0.1f, 0.6f, 1f, 0.35f));

		// Per-door links drawn as flat strips between their endpoints, in the same blue as the main
		// mesh, so each doorway bridge is visible (whether the door is open or closed).
		foreach (Node node in GetTree().GetNodesInGroup("door"))
		{
			if (node is not Door)
			{
				continue;
			}

			var doorLink = node.GetNodeOrNull<NavigationLink3D>("NavigationLink3D");
			if (doorLink != null)
			{
				hasGeometry |= AppendLinkStrip(
					surfaceTool, doorLink, new Color(0.1f, 0.6f, 1f, 0.35f));
			}
		}

		if (!hasGeometry)
		{
			return null;
		}

		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			VertexColorUseAsAlbedo = true,
		};

		var meshInstance = new MeshInstance3D
		{
			Name = "NavigationDebugMesh",
			Mesh = surfaceTool.Commit(),
			MaterialOverride = material,
			// Vertices are already baked in world space, so ignore the parent transform.
			TopLevel = true,
		};

		NavigationRegion.AddChild(meshInstance);
		return meshInstance;
	}

	/// <summary>
	/// Appends a navigation region's polygons to <paramref name="surfaceTool"/> in world space
	/// (lifted slightly to avoid z-fighting), tinted with <paramref name="color"/>. Returns false
	/// if the region has no navmesh geometry. Used by the debug overlay to draw the main mesh and
	/// each door's bridge patch together so their relative height and alignment are visible.
	/// </summary>
	private static bool AppendRegionTriangles(SurfaceTool surfaceTool, NavigationRegion3D region, Color color)
	{
		NavigationMesh navigationMesh = region?.NavigationMesh;
		if (navigationMesh == null)
		{
			return false;
		}

		Vector3[] vertices = navigationMesh.GetVertices();
		if (vertices.Length == 0)
		{
			return false;
		}

		Transform3D transform = region.GlobalTransform;
		transform.Origin += new Vector3(0, 0.05f, 0);

		for (int polygonIndex = 0; polygonIndex < navigationMesh.GetPolygonCount(); polygonIndex++)
		{
			int[] polygon = navigationMesh.GetPolygon(polygonIndex);
			// Fan-triangulate each convex navmesh polygon.
			for (int corner = 2; corner < polygon.Length; corner++)
			{
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[0]]);
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[corner - 1]]);
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[corner]]);
			}
		}

		return true;
	}

	/// <summary>
	/// Appends a flat quad strip following a navigation link from its start to end endpoint, tinted
	/// with <paramref name="color"/>. The strip's height is snapped to the baked navmesh (via the
	/// nearest navmesh point) so it sits co-planar with the main mesh overlay rather than at the
	/// link's authored floor-level endpoints. Returns false if the endpoints coincide.
	/// </summary>
	private static bool AppendLinkStrip(SurfaceTool surfaceTool, NavigationLink3D link, Color color)
	{
		Transform3D transform = link.GlobalTransform;
		Vector3 start = transform * link.StartPosition;
		Vector3 end = transform * link.EndPosition;

		// Lift to the navmesh surface (+ the same small z-fight offset the main mesh uses) so the
		// debug strip lines up with the blue overlay instead of hugging the floor.
		Rid navigationMap = link.GetWorld3D().NavigationMap;
		if (navigationMap.IsValid)
		{
			start.Y = NavigationServer3D.MapGetClosestPoint(navigationMap, start).Y;
			end.Y = NavigationServer3D.MapGetClosestPoint(navigationMap, end).Y;
		}
		start.Y += 0.05f;
		end.Y += 0.05f;

		Vector3 along = end - start;
		if (along.LengthSquared() <= 0.0001f)
		{
			return false;
		}

		// A half-metre-wide ribbon centred on the link line, lying flat on the floor plane.
		Vector3 side = along.Normalized().Cross(Vector3.Up);
		if (side.LengthSquared() <= 0.0001f)
		{
			side = Vector3.Right;
		}
		side = side.Normalized() * 0.25f;

		Vector3 a = start + side;
		Vector3 b = start - side;
		Vector3 c = end - side;
		Vector3 d = end + side;

		foreach (Vector3 vertex in new[] { a, b, c, a, c, d })
		{
			surfaceTool.SetColor(color);
			surfaceTool.AddVertex(vertex);
		}

		return true;
	}

	private void Reset()
	{
		GD.Print("Resetting map generator...");
		GD.Seed(Seed);

		_roomRegions.Clear();
		_tileToRoom.Clear();
		_revealedTiles.Clear();
		_connectorToRoom.Clear();
		_dooredConnectors.Clear();
		_doorIndicators.Clear();
		_markerIndicators.Clear();

		// Initialize the map with empty tiles and a walled border
		Map = new MapData((int)MapWidth, (int)MapDepth);
		Map.ResetToBorderedEmpty();

		FloorGridMap?.Clear();
		WallGridMap?.Clear();
		DecorationGridMap?.Clear();
		OcclusionGridMap?.Clear();

		PlayerSpawnPoint = null;
		if (EnemySpawnPoints != null)
		{
			foreach (var spawnPoint in EnemySpawnPoints)
			{
				spawnPoint.QueueFree();
			}
		}
		EnemySpawnPoints = new Array<SpawnPoint>();

		if (NavigationRegion != null)
		{
			// Empty the navigation region
			foreach (var node in NavigationRegion.GetChildren())
			{
				NavigationRegion.RemoveChild(node);
				node.QueueFree();
			}
			NavigationRegion.NavigationMesh.Clear();
		}

		RoomLayout?.Reset();
		CorridorConnector?.Reset();
		MobFactory?.Reset();
		RoomFactory?.Reset();
		TileFactory?.Reset();
	}

	/// <summary>
	/// Converts a world position to the master map tile that contains it. Inverse
	/// of <see cref="TileToWorld(int,int,int)"/>; tiles are centered on their cell.
	/// </summary>
	public Vector2I WorldToTile(Vector3 worldPosition)
	{
		var centerX = Map.Width / 2;
		var centerZ = Map.Height / 2;
		return new Vector2I(
			Mathf.RoundToInt(worldPosition.X / TileSize) + centerX,
			Mathf.RoundToInt(worldPosition.Z / TileSize) + centerZ);
	}

	/// <summary>
	/// Returns the id of the room that owns the given tile, or -1 if the tile is
	/// not part of any room (corridor, void, or out of bounds).
	/// </summary>
	public int GetRoomIdAt(Vector2I tile)
	{
		return _tileToRoom.TryGetValue(tile, out var id) ? id : -1;
	}

	private Vector3I TileToWorld(Vector3I tile)
	{
		return TileToWorld(tile.X, tile.Y, tile.Z);
	}

	private Vector3I TileToWorld(int x, int y, int z)
	{
		var centerX = (int)Map.Width / 2;
		var centerZ = (int)Map.Height / 2;
		return new Vector3I(
			(x - centerX) * (int)TileSize,
			y * (int)TileSize,
			(z - centerZ) * (int)TileSize);
	}

	/// <summary>
	/// Debug/tooling query: every connector tile's world position, whether it's an
	/// explicit doorway (guaranteed-connected) or an inferred edge (optional), and each
	/// open direction annotated with whether it actually leads to an open passage or is
	/// sealed by a generated wall. All-Godot-native return types so this is callable
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
					{ "worldPosition", (Vector3)TileToWorld(x, 0, z) },
					{ "isDoorway", Map.IsDoorway(x, z) },
					{ "directions", directionsInfo },
				});
			}
		}

		return result;
	}
}
