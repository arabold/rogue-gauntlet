using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Generation-time loot: chests, traps and loose items scattered over procedural rooms, and the pickup helper access keys share.</summary>
public partial class MapGenerator
{
	/// <summary>Chest prop placed against a room wall, facing into the room. Unset skips chest placement.</summary>
	[ExportGroup("Loot")]
	[Export] public PackedScene ChestScene { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinChests { get; set; } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxChests { get; set; } = 2;

	/// <summary>Floor trap prop, placeable on any open room/corridor tile. Unset skips trap placement.</summary>
	[Export] public PackedScene TrapScene { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinTraps { get; set; } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxTraps { get; set; } = 3;

	/// <summary>Pickup scene for loose ground items and generated access keys. Unset skips loose-item placement.</summary>
	[Export] public PackedScene LooseItemScene { get; set; }
	/// <summary>What each loose item rolls from. Unset skips loose-item placement.</summary>
	[Export] public LootDropProfile LooseItems { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinLooseItems { get; set; } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxLooseItems { get; set; } = 4;

	// Smaller than the enemy-spawn equivalents: loot close to the entrance is fine, and a
	// tight level shouldn't run out of valid spots just to keep loot far from the player.
	// Internal (not private) so LootPlacementTest can assert against the real thresholds.
	internal const float LootSpawnPlayerClearance = 10f;
	internal const float LootSpawnSpacing = 6f;

	/// <summary>
	/// Scatters chests, traps, and loose ground items across the finished map. Runs after
	/// walls/corridors are final (so wall-adjacency and occupancy checks are accurate) and
	/// before <see cref="BakeNavigationMesh"/> (so chests -- solid, unlike traps/loose items
	/// -- are added as children of <see cref="NavigationRegion"/> in time to be baked in as
	/// navmesh obstacles, the same way rooms already are). Every content type shares one
	/// running list of placed points so chests/traps/items all keep clear of each other, not
	/// just their own kind. Access pickups and fountains, placed earlier, seed that list.
	/// </summary>
	private void PlaceLoot()
	{
		var placedLootPoints = new List<Vector3>([.. _accessPickupPoints, .. _fountainPoints]);
		PlaceChests(placedLootPoints);
		PlaceTraps(placedLootPoints);
		PlaceLooseItems(placedLootPoints);
	}

	/// <summary>Later solid props (chests, fountains) must not undo the furnishing pass's connected-free-space invariant.</summary>
	private bool PreservesRoomPassage(Node3D prop, Vector3 point, float rotation)
	{
		var tile = Map.WorldToTile(point);
		var room = Floor.Rooms.FirstOrDefault(region => region.IsProcedural && region.Tiles.Contains(tile));
		if (room == null) return false;
		var pool = room.InteriorPool ?? ArrangementPool;
		if (pool == null) return true;
		var obstacles = RoomArrangement.GetGridBounds(DecorationGridMap, DecorationGridMap.Transform)
			.Concat(RoomArrangement.GetGridBounds(WallGridMap, WallGridMap.Transform))
			.Concat(RoomArrangement.GetStaticPropBounds(NavigationRegion, GlobalTransform.AffineInverse() * NavigationRegion.GlobalTransform)).ToArray();
		var doors = room.ConnectorTiles.Where(connector => Map.GetConnectorDirections(connector.X, connector.Y)
			.Any(direction => { var next = connector + direction; return Map.IsWithinBounds(next.X, next.Y) && Map.IsCorridor(next.X, next.Y); }));
		var passage = new RoomPassageValidator(Map, room.Tiles.Where(t => Map.IsWalkable(t.X, t.Y)).ToHashSet(), doors,
			t => Map.TileToWorld(t), obstacles, pool.PassageRadius);
		var proposed = GlobalTransform.AffineInverse() * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(rotation)),
			point + Vector3.Up * GetFloorSurfaceHeight(point));
		return passage.IsConnected(RoomArrangement.GetStaticPropBounds(prop, proposed));
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

		int count = _random.Items.RandiRange(MinChests, MaxChests);
		var remaining = new List<(Vector3 Point, float RotationDegrees)>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
			var candidate = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(candidate.Point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			var chest = ChestScene.Instantiate<Node3D>();
			if (!PreservesRoomPassage(chest, candidate.Point, candidate.RotationDegrees))
			{
				chest.Free();
				continue;
			}
			chest.SetMeta("persistent_id", $"depth:{DungeonDepth}:chest:{placed}");
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

		var candidates = Floor.OpenTileCandidates(proceduralRoomsOnly: true);
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("traps", "no valid open procedural room or corridor tiles found");
			return;
		}

		int count = _random.Items.RandiRange(MinTraps, MaxTraps);
		var remaining = new List<Vector3>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
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
			if (trap is FloorTrap floorTrap)
			{
				floorTrap.Initialize($"depth:{DungeonDepth}:trap:{placed}", Seed, FloorGridMap, FloorGridMap.LocalToMap(FloorGridMap.ToLocal(point)));
			}

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
		if (LooseItemScene == null || LooseItems?.Table == null || MaxLooseItems <= 0)
		{
			return;
		}

		var candidates = GetLooseItemCandidates();
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("loose items", "no valid procedural room tiles found");
			return;
		}

		uint depth = DungeonDepth;
		int count = _random.Items.RandiRange(MinLooseItems, MaxLooseItems);
		var remaining = new List<Vector3>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
			var point = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			// Each spot is a reward source: its own stream, chance, roll count and instance ids. A spot whose roll yields
			// nothing still counts, so chance thins the scatter instead of moving it.
			string sourceId = $"depth:{DungeonDepth}:loose:{placed}";
			using var rng = CreateSourceLootRng(sourceId);
			var outcome = LootTableComponent.GenerateOutcome(LooseItems, depth, rng, sourceId);
			foreach (var record in outcome)
			{
				SpawnPickup(record.Item, record.Quantity, sourceId, point);
			}

			if (outcome.Count > 0)
			{
				placedLootPoints.Add(point);
			}
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
				if (!Map.IsRoom(x, z) || !Floor.IsProceduralRoomTile(x, z)
					|| Floor.IsOccupied(x, z) || IsAdjacentToConnector(x, z))
				{
					continue;
				}

				var wallDirections = new List<Vector2I>();
				foreach (var offset in MapData.CardinalOffsets)
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

				var point = Map.TileToWorld(x, z);
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
				if (Map.IsRoom(x, z) && Floor.IsProceduralRoomTile(x, z) && !Floor.IsOccupied(x, z))
				{
					candidates.Add(Map.TileToWorld(x, z));
				}
			}
		}

		return candidates;
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
		if (Floor.Rooms.Any(region => region.IsProcedural))
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
		for (int dx = -Room.TileSize / 2; dx < Room.TileSize / 2; dx++)
		{
			for (int dz = -Room.TileSize / 2; dz < Room.TileSize / 2; dz++)
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
		foreach (var offset in MapData.CardinalOffsets)
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
		if (Floor.Arrival != null && point.HorizontalDistanceTo(Floor.Arrival.GlobalPosition) < LootSpawnPlayerClearance)
		{
			return true;
		}

		// Every loot kind is a prop, and props occupy the tiles around them: none may claim a gatekeeper footprint tile.
		if (Floor.IsInGatekeeperArena(point))
		{
			return true;
		}

		foreach (var placed in placedLootPoints)
		{
			if (point.HorizontalDistanceTo(placed) < spacing)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Places a seeded pickup on the floor under <paramref name="point"/>. The persistent id ties it to
	/// item persistence, so collecting it survives regeneration and save/load.</summary>
	private void SpawnPickup(Item item, int quantity, string sourceId, Vector3 point)
	{
		var pickup = LooseItemScene.Instantiate<LootableItem>();
		pickup.Item = item;
		pickup.Quantity = quantity;
		pickup.SetMeta("persistent_id", sourceId);
		NavigationRegion.AddChild(pickup);
		pickup.GlobalPosition = point + Vector3.Up * GetFloorSurfaceHeight(point);
	}
}