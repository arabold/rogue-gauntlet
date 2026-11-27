using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Generation-time loot: chests, traps and loose items scattered over the rooms, and the pickup helper access keys share.</summary>
public partial class MapGenerator
{
	/// <summary>Chest prop placed against a room wall, facing into the room. Unset skips chest placement.</summary>
	[ExportGroup("Loot")]
	[Export] public PackedScene ChestScene { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinChests { get; set; } = 1;
	[Export(PropertyHint.Range, "0,10")] public int MaxChests { get; set; } = 3;
	/// <summary>
	/// The chest every generated locked room holds, never a mimic, so a key always pays. Unset leaves locked rooms to
	/// the ordinary scatter.
	/// </summary>
	[Export] public PackedScene LockedRewardScene { get; set; }
	/// <summary>
	/// The chest every room reached only through secret doors holds, never a mimic, so finding a secret always pays.
	/// Unset leaves secret rooms to the ordinary scatter.
	/// </summary>
	[Export] public PackedScene SecretRewardScene { get; set; }

	/// <summary>The trap a trap slot places: a concealed <see cref="PressurePlate"/> scene, the spike plate by default,
	/// placeable on any open room or corridor tile. Unset skips trap placement.</summary>
	[Export] public PackedScene TrapScene { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinTraps { get; set; } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxTraps { get; set; } = 3;
	/// <summary>Plate traps a trap slot may place instead of <see cref="TrapScene"/>, by weight, from their first depth.</summary>
	[Export] public TriggeredTrapProfile[] TriggeredTraps { get; set; } = [];
	/// <summary>Chance that a trap slot tries a plate trap, where one may place; slots without a fitting lane keep the spike plate.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float TriggeredTrapChance { get; set; } = .35f;

	/// <summary>Pickup scene for loose ground items and generated access keys. Unset skips loose-item placement.</summary>
	[Export] public PackedScene LooseItemScene { get; set; }
	/// <summary>What each loose item rolls from. Unset skips loose-item placement.</summary>
	[Export] public LootDropProfile LooseItems { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinLooseItems { get; set; } = 3;
	[Export(PropertyHint.Range, "0,10")] public int MaxLooseItems { get; set; } = 5;
	/// <summary>What each gold pile lying on the floor rolls from, like Rogue's gold in every other room. Unset skips them.</summary>
	[Export] public LootDropProfile GoldPiles { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinGoldPiles { get; set; } = 2;
	[Export(PropertyHint.Range, "0,10")] public int MaxGoldPiles { get; set; } = 3;

	// Smaller than the enemy-spawn equivalents: loot close to the entrance is fine, and a
	// tight level shouldn't run out of valid spots just to keep loot far from the player.
	// Internal (not private) so LootPlacementTest can assert against the real thresholds.
	internal const float LootSpawnPlayerClearance = 10f;
	/// <summary>Keeps loot off the landing in front of every staircase, whose path must stay open.</summary>
	private const float StairLandingClearance = 4f;
	internal const float LootSpawnSpacing = 6f;

	/// <summary>
	/// Scatters chests, traps, gold and loose ground items across the finished map. Runs after
	/// walls/corridors are final (so wall-adjacency and occupancy checks are accurate) and
	/// before <see cref="BuildNavigation"/> (so chests -- solid, unlike traps/loose items
	/// -- are children of <see cref="FloorContent"/> in time to count as navigation solids,
	/// the same way rooms already do). Every content type shares one
	/// running list of placed points so chests/traps/items all keep clear of each other, not
	/// just their own kind. Access pickups and keepers, placed earlier, seed that list, the run's scheduled
	/// fountains claim their spots first and the locked and secret rooms' reward chests claim theirs next.
	/// </summary>
	private void PlaceLoot(List<Vector3> placedLootPoints)
	{
		var doorClearances = DoorClearances();
		PlaceFountains(placedLootPoints, doorClearances);
		PlaceRewardChests(placedLootPoints, doorClearances);
		PlaceChests(placedLootPoints, doorClearances);
		PlaceTraps(placedLootPoints, doorClearances);
		PlaceScatter(placedLootPoints, LooseItems, MinLooseItems, MaxLooseItems, "loose");
		PlaceScatter(placedLootPoints, GoldPiles, MinGoldPiles, MaxGoldPiles, "gold");
	}

	/// <summary>
	/// One reward chest in every generated locked room and every room reached only through secret doors: a key or a
	/// found secret always pays. Placed before the ordinary chests so the room's own wall spots are still free.
	/// </summary>
	private void PlaceRewardChests(List<Vector3> placedLootPoints, IReadOnlyList<Aabb> doorClearances)
	{
		var secretConnectors = Floor.Doors.Where(d => d.Door.Secret?.IsSecret == true).Select(d => d.Connector).ToHashSet();
		var rewards = new List<(RoomRegion Room, PackedScene Scene, string Kind)>();
		if (LockedRewardScene != null) rewards.AddRange(_lockedRewardRooms.Select(room => (room, LockedRewardScene, "locked")));
		if (SecretRewardScene != null)
		{
			rewards.AddRange(Floor.Rooms
				.Where(room => room.ConnectorTiles.Count > 0 && room.ConnectorTiles.All(secretConnectors.Contains))
				.Select(room => (room, SecretRewardScene, "secret")));
		}

		foreach (var (room, scene, kind) in rewards)
		{
			string id = $"depth:{DungeonDepth}:{kind}-reward:{room.Id}";
			var chest = scene.Instantiate<Node3D>();
			if (TryPlaceRoomChest(room, chest, id, placedLootPoints, doorClearances)) continue;
			// A room too crowded for a chest still pays: its reward lies on the floor instead.
			var profile = chest.GetNodeOrNull<LootTableComponent>("LootTableComponent")?.Profile;
			chest.Free();
			if (!TryScatterRoomReward(room, profile, id, placedLootPoints))
			{
				GD.PrintErr($"No spot for the {kind} room {room.Id}'s reward.");
			}
		}
	}

	/// <summary>Spacing a reward keeps from other loot: its room is closed off, so it may stand closer than the scatter.</summary>
	private const float RewardSpacing = 2.5f;

	/// <summary>
	/// Stands <paramref name="chest"/> inside <paramref name="room"/>: against a wall facing into it where one is free,
	/// otherwise on any free tile facing the room's middle. Keeps the chest on failure so the caller can free it.
	/// </summary>
	private bool TryPlaceRoomChest(RoomRegion room, Node3D chest, string persistentId, List<Vector3> placedLootPoints,
		IReadOnlyList<Aabb> doorClearances)
	{
		var tiles = room.Tiles.ToHashSet();
		var middle = room.Tiles.Aggregate(Vector3.Zero, (sum, tile) => sum + Map.TileToWorld(tile.X, tile.Y)) / room.Tiles.Count;
		var (primary, corners) = ChestSpots();
		var spots = primary.Concat(corners).Where(spot => tiles.Contains(spot.Tile)).Select(spot => (spot.Point, spot.RotationDegrees)).ToList();
		spots.AddRange(GetSolidPropTiles()
			.Where(spot => tiles.Contains(spot.Tile))
			.Select(spot => (spot.Point, Mathf.RadToDeg(Mathf.Atan2(middle.X - spot.Point.X, middle.Z - spot.Point.Z)))));
		foreach (var (point, rotation) in spots)
		{
			// The room's door already keeps the arrival out, so its reward may stand anywhere inside.
			if (IsBlockedLootPoint(point, placedLootPoints, RewardSpacing, arrivalClearance: 0f)) continue;
			Vector3 frontPoint = point + new Basis(Vector3.Up, Mathf.DegToRad(rotation)).Z * 1.5f;
			if (!Floor.IsReachable(frontPoint) || Floor.IsColumnObstructed(frontPoint)) continue;
			if (!TryPlaceSolidProp(chest, point, rotation, persistentId, doorClearances)) continue;
			placedLootPoints.Add(point);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Lays a reward's loot inside <paramref name="room"/>, as one seeded reward source: on an open tile, or in a furnished
	/// room on any clear spot the hero can stand on.
	/// </summary>
	private bool TryScatterRoomReward(RoomRegion room, LootDropProfile profile, string sourceId, List<Vector3> placedLootPoints)
	{
		if (LooseItemScene == null || profile?.Table == null) return false;
		var tiles = room.Tiles.ToHashSet();
		float quarter = Room.TileSize * 0.25f;
		var clearSpots = room.Tiles
			.SelectMany(tile => new[] { Vector3.Zero, new(quarter, 0, quarter), new(-quarter, 0, quarter), new(quarter, 0, -quarter), new(-quarter, 0, -quarter) }
				.Select(offset => Map.TileToWorld(tile.X, tile.Y) + offset))
			.Where(point => tiles.Contains(Map.WorldToTile(point)) && Floor.IsReachable(point) && !Floor.IsColumnObstructed(point));
		foreach (var point in Floor.OpenTileCandidates().Where(point => tiles.Contains(Map.WorldToTile(point))).Concat(clearSpots))
		{
			if (IsBlockedLootPoint(point, placedLootPoints, RewardSpacing, arrivalClearance: 0f)) continue;
			using var rng = CreateSourceLootRng(sourceId);
			var outcome = LootTableComponent.GenerateOutcome(profile, DungeonDepth, rng, sourceId);
			for (int i = 0; i < outcome.Count; i++)
			{
				// Fanned out around the spot as a chest throws them, so the pieces do not lie on top of each other.
				var heading = Vector3.Forward.Rotated(Vector3.Up, Mathf.Tau * i / outcome.Count);
				var landing = outcome.Count == 1 ? point : Floor.FindDropLanding(point, heading, 1.2f) with { Y = point.Y };
				SpawnPickup(outcome[i].Item, outcome[i].Quantity, sourceId, landing);
			}
			placedLootPoints.Add(point);
			return true;
		}
		return false;
	}

	/// <summary>Every interior door's swing area in generator space: solid props keep clear of them.</summary>
	private IReadOnlyList<Aabb> DoorClearances() =>
		InteriorDoorComponent.CollectClearances(FloorContent, GlobalTransform.AffineInverse() * FloorContent.GlobalTransform);

	/// <summary>A solid prop (chest, fountain) must sit on reachable space, keep door swings clear and create no
	/// inaccessible pocket. On success its collision joins the floor's walkability.</summary>
	private bool PreservesRoomPassage(Node3D prop, Vector3 point, float rotation, IReadOnlyList<Aabb> doorClearances)
	{
		if (!Floor.IsReachable(point)) return false;
		var proposed = GlobalTransform.AffineInverse() * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(rotation)),
			point + Vector3.Up * GetFloorSurfaceHeight(point));
		var solids = CollisionOutlines.FromStaticBodies(prop, proposed).ToList();
		return !solids.Any(solid => doorClearances.Any(clearance => clearance.Intersects(solid.Bounds))) && TryBlockReachably(solids);
	}

	/// <summary>
	/// Attempts to place an already-instantiated solid prop <paramref name="prop"/> at <paramref name="point"/> when it
	/// preserves floor passage. Caller keeps ownership of <paramref name="prop"/> on false so probe loops can reuse it.
	/// </summary>
	private bool TryPlaceSolidProp(Node3D prop, Vector3 point, float rotation, string persistentId, IReadOnlyList<Aabb> doorClearances)
	{
		if (!PreservesRoomPassage(prop, point, rotation, doorClearances))
		{
			return false;
		}

		prop.SetMeta("persistent_id", persistentId);
		FloorContent.AddChild(prop);
		// Raised onto the floor's visual surface: a static prop doesn't settle via physics the way characters do, so at
		// y=0 its base sits buried inside the floor mesh.
		prop.GlobalPosition = point + Vector3.Up * GetFloorSurfaceHeight(point);
		prop.RotationDegrees = new Vector3(0, rotation, 0);
		return true;
	}

	private void PlaceChests(List<Vector3> placedLootPoints, IReadOnlyList<Aabb> doorClearances)
	{
		if (ChestScene == null || MaxChests <= 0)
		{
			return;
		}

		var candidates = GetChestCandidates();
		if (candidates.Count == 0)
		{
			GD.PrintErr("Cannot place chests: no valid wall-adjacent room or corridor tiles found.");
			return;
		}

		float deadEndWeight = CurrentProfile?.DeadEndLootWeight ?? 1f;
		int count = _random.Items.RandiRange(MinChests, MaxChests);
		var remaining = new List<(Vector3 Point, float RotationDegrees, float Weight)>(candidates.Count);
		foreach (var (tile, point, rot) in candidates)
		{
			float weight = Map.OpenNeighborCount(tile.X, tile.Y) == 1 ? deadEndWeight : 1f;
			remaining.Add((point, rot, weight));
		}

		int placed = 0;
		Node3D probe = null;
		try
		{
			while (placed < count && remaining.Count > 0)
			{
				int index = PickWeightedCandidateIndex(remaining, r => r.Weight, _random.Items);
				var candidate = remaining[index];
				remaining.RemoveAt(index);
				if (IsBlockedLootPoint(candidate.Point, placedLootPoints, LootSpawnSpacing))
				{
					continue;
				}
				Vector3 frontPoint = candidate.Point + new Basis(Vector3.Up, Mathf.DegToRad(candidate.RotationDegrees)).Z * 1.5f;
				if (!Floor.IsReachable(frontPoint) || Floor.IsColumnObstructed(frontPoint))
				{
					continue;
				}

				probe ??= ChestScene.Instantiate<Node3D>();
				string id = $"depth:{DungeonDepth}:chest:{placed}";
				if (IsMimic(id))
				{
					// A mimic takes the chest's spot and identity: it waits there as a monster, and killing it persists.
					if (!PreservesRoomPassage(probe, candidate.Point, candidate.RotationDegrees, doorClearances)) continue;
					AddEnemySpawnPoint(Floor.Encounter.Definition.MimicScene, id,
						new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(candidate.RotationDegrees)), candidate.Point));
				}
				else
				{
					if (!TryPlaceSolidProp(probe, candidate.Point, candidate.RotationDegrees, id, doorClearances)) continue;
					probe = null;
				}
				placedLootPoints.Add(candidate.Point);
				placed++;
			}
		}
		finally
		{
			if (GodotObject.IsInstanceValid(probe)) probe.Free();
		}

		if (placed < MinChests)
		{
			GD.PrintErr($"Placed only {placed}/{MinChests} minimum chests: ran out of valid spots.");
		}
	}

	/// <summary>Whether the chest with <paramref name="persistentId"/> is a mimic: the realm's chance, on the chest's own stream.</summary>
	private bool IsMimic(string persistentId)
	{
		var realm = Floor.Encounter?.Definition;
		if (realm?.MimicScene == null || realm.MimicChance <= 0) return false;
		using var rng = GenerationRandom.Create(Seed, GenerationRandom.Monsters, persistentId + ":mimic");
		return rng.Randf() < realm.MimicChance;
	}

	/// <summary>
	/// Fills the floor's trap slots: each may become a plate trap (<see cref="TryPlacePlateTrap"/>), and otherwise lays the
	/// spike plate on a random open room or corridor tile.
	/// </summary>
	private void PlaceTraps(List<Vector3> placedLootPoints, IReadOnlyList<Aabb> doorClearances)
	{
		float scale = CurrentProfile?.TrapDensityScale ?? 1f;
		int minTraps = Mathf.RoundToInt(MinTraps * scale);
		int maxTraps = Math.Max(minTraps, Mathf.RoundToInt(MaxTraps * scale));
		if (TrapScene == null || maxTraps <= 0)
		{
			return;
		}

		var candidates = Floor.OpenTileCandidates();
		if (candidates.Count == 0)
		{
			GD.PrintErr("Cannot place traps: no valid open room or corridor tiles found.");
			return;
		}

		int count = _random.Items.RandiRange(minTraps, maxTraps);
		var remaining = candidates.Where(point => !HoldsInlay(Map.WorldToTile(point))).ToList();
		var search = PlateTrapSearchFor(doorClearances);
		int placed = 0;
		while (placed < count)
		{
			string id = $"depth:{DungeonDepth}:trap:{placed}";
			if (!TryPlacePlateTrap(id, placedLootPoints, search) && !TryPlaceSpikeTrap(id, remaining, placedLootPoints))
			{
				break;
			}
			placed++;
		}

		if (placed < minTraps)
		{
			GD.PrintErr($"Placed only {placed}/{minTraps} minimum traps: ran out of valid spots.");
		}
	}

	/// <summary>
	/// Lays the spike plate on the first of the <paramref name="remaining"/> open tiles, taken at random, that keeps
	/// clear of other loot; false once none does.
	/// </summary>
	private bool TryPlaceSpikeTrap(string id, List<Vector3> remaining, List<Vector3> placedLootPoints)
	{
		while (remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
			var point = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing)) continue;

			// Spike plates deliberately allow room/corridor chokepoint tiles (unlike chests): a trap you're forced to
			// cross is the point. The plate is square and grid-aligned like the room's own floor tiles, so it stays
			// unrotated: turned off-axis its edges no longer line up with its neighbours and it reads as a floating tile.
			LayPlate(TrapScene, id, point, placedLootPoints);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Whether a floor inlay (a drain grate, a gutter) lies in <paramref name="tile"/>: a trap there would sit on the grate
	/// instead of the floor. Inlays anchor at most a quarter tile off the tile's middle, or on its edge.
	/// </summary>
	private bool HoldsInlay(Vector2I tile)
	{
		if (_inlayItems.Count == 0) return false;
		Vector3I center = Map.TileToWorld(tile);
		int half = Room.TileSize / 2;
		for (int dx = -half; dx <= half; dx++)
		for (int dz = -half; dz <= half; dz++)
			if (_inlayItems.Contains(FloorGridMap.GetCellItem(center + new Vector3I(dx, 0, dz)))) return true;
		return false;
	}

	/// <summary>Lays a concealed plate from <paramref name="scene"/> on <paramref name="point"/>, identified as trap slot
	/// <paramref name="id"/>, and claims the spot from other loot.</summary>
	private PressurePlate LayPlate(PackedScene scene, string id, Vector3 point, List<Vector3> placedLootPoints)
	{
		var plate = scene.Instantiate<PressurePlate>();
		FloorContent.AddChild(plate);
		plate.GlobalPosition = point;
		plate.Initialize(id, Seed, FloorGridMap, FloorGridMap.LocalToMap(FloorGridMap.ToLocal(point)));
		placedLootPoints.Add(point);
		return plate;
	}

	/// <summary>
	/// Scatters <paramref name="min"/> to <paramref name="max"/> pickups rolled from <paramref name="profile"/> over open
	/// floor, each spot its own <paramref name="kind"/> reward source: the loose items and the gold piles.
	/// </summary>
	private void PlaceScatter(List<Vector3> placedLootPoints, LootDropProfile profile, int min, int max, string kind)
	{
		if (LooseItemScene == null || profile?.Table == null || max <= 0)
		{
			return;
		}

		var candidates = Floor.OpenTileCandidates();
		if (candidates.Count == 0)
		{
			GD.PrintErr($"Cannot place {kind} pickups: no valid floor tiles found.");
			return;
		}

		float deadEndWeight = CurrentProfile?.DeadEndLootWeight ?? 1f;
		uint depth = DungeonDepth;
		int count = _random.Items.RandiRange(min, max);
		var remaining = new List<(Vector3 Point, float Weight)>(candidates.Count);
		foreach (var point in candidates)
		{
			var tile = Map.WorldToTile(point);
			float weight = Map.OpenNeighborCount(tile.X, tile.Y) == 1 ? deadEndWeight : 1f;
			remaining.Add((point, weight));
		}

		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = PickWeightedCandidateIndex(remaining, r => r.Weight, _random.Items);
			var point = remaining[index].Point;
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			// Each spot is a reward source: its own stream, chance, roll count and instance ids. A spot whose roll yields
			// nothing still counts, so chance thins the scatter instead of moving it.
			string sourceId = $"depth:{DungeonDepth}:{kind}:{placed}";
			using var rng = CreateSourceLootRng(sourceId);
			var outcome = LootTableComponent.GenerateOutcome(profile, depth, rng, sourceId);
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

		if (placed < min)
		{
			GD.PrintErr($"Placed only {placed}/{min} minimum {kind} pickups: ran out of valid spots.");
		}
	}

	private static int PickWeightedCandidateIndex<T>(IReadOnlyList<T> items, Func<T, float> getWeight, RandomNumberGenerator rng)
	{
		float total = 0f;
		bool anyNonUnit = false;
		for (int i = 0; i < items.Count; i++)
		{
			float w = getWeight(items[i]);
			if (!Mathf.IsEqualApprox(w, 1f)) anyNonUnit = true;
			if (w > 0f) total += w;
		}
		if (!anyNonUnit || total <= 0f) return rng.RandiRange(0, items.Count - 1);

		float roll = rng.Randf() * total;
		float cumulative = 0f;
		int lastPositive = items.Count - 1;
		for (int i = 0; i < items.Count; i++)
		{
			float w = getWeight(items[i]);
			if (w <= 0f) continue;
			lastPositive = i;
			cumulative += w;
			if (roll < cumulative) return i;
		}
		return lastPositive;
	}

	/// <summary>
	/// Every unoccupied tile a solid prop may stand on (rooms, and optionally corridors), away from connector tiles
	/// for margin from doorway traffic, paired with a 4-bit mask (indexed by <see cref="MapData.CardinalOffsets"/>)
	/// of cardinal directions in which a wall backs it.
	/// </summary>
	private List<(Vector2I Tile, Vector3 Point, byte WallMask)> GetSolidPropTiles(bool includeCorridors = false)
	{
		Floor.RefreshPropTiles();
		var tiles = new List<(Vector2I, Vector3, byte)>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				bool eligible = Map.IsRoom(x, z) || (includeCorridors && Map.IsCorridor(x, z));
				if (!eligible || Floor.IsOccupiedNow(x, z) || IsAdjacentToConnector(x, z))
				{
					continue;
				}

				byte wallMask = 0;
				for (int i = 0; i < MapData.CardinalOffsets.Count; i++)
				{
					var offset = MapData.CardinalOffsets[i];
					int nx = x + offset.X, nz = z + offset.Y;
					// Chasms are deliberately left unwalled, so a chasm-adjacent tile has nothing solid behind it.
					if (Map.IsWithinBounds(nx, nz) && Map.IsChasm(nx, nz))
					{
						continue;
					}

					if (Map.HasWallToward(x, z, offset))
					{
						wallMask |= (byte)(1 << i);
					}
				}

				tiles.Add((new Vector2I(x, z), Map.TileToWorld(x, z), wallMask));
			}
		}

		return tiles;
	}

	/// <summary>
	/// Every wall-backed solid prop tile (including corridor dead ends and corners/junctions, skipping straight 1-tile
	/// corridor bottlenecks), paired with the Y-rotation facing away from the backing wall: the wall-backed spots, or the
	/// corner spots on a floor without any.
	/// </summary>
	private List<(Vector2I Tile, Vector3 Point, float RotationDegrees)> GetChestCandidates()
	{
		var (primary, corners) = ChestSpots();
		return primary.Count > 0 ? primary : corners;
	}

	/// <summary>
	/// Chest spots split into the preferred ones (one backing wall, or a corridor dead end) and corners and junctions.
	/// </summary>
	private (List<(Vector2I Tile, Vector3 Point, float RotationDegrees)> Primary, List<(Vector2I Tile, Vector3 Point, float RotationDegrees)> Corners) ChestSpots()
	{
		var primary = new List<(Vector2I, Vector3, float)>();
		var corners = new List<(Vector2I, Vector3, float)>();
		foreach (var (tile, point, wallMask) in GetSolidPropTiles(includeCorridors: true))
		{
			if (wallMask == 0)
			{
				continue;
			}

			int wallCount = System.Numerics.BitOperations.PopCount(wallMask);
			bool isCorridor = Map.IsCorridor(tile.X, tile.Y);
			int openNeighbors = Map.OpenNeighborCount(tile.X, tile.Y);

			Vector2I firstWall = default;
			Vector2I wallDir = default;
			bool twoOpposite = false;
			int seenWalls = 0;
			for (int i = 0; i < MapData.CardinalOffsets.Count; i++)
			{
				if ((wallMask & (1 << i)) == 0) continue;
				var dir = MapData.CardinalOffsets[i];
				if (seenWalls == 0)
				{
					firstWall = dir;
					wallDir = dir;
				}
				else if (seenWalls == 1 && dir == -firstWall && wallCount == 2)
				{
					twoOpposite = true;
				}
				seenWalls++;
			}
			if (isCorridor && openNeighbors == 2 && twoOpposite)
			{
				// Straight 1-tile corridor chokepoint: keep clear for passage.
				continue;
			}

			bool isDeadEnd = openNeighbors == 1;
			for (int i = 0; i < MapData.CardinalOffsets.Count; i++)
			{
				if ((wallMask & (1 << i)) == 0) continue;
				var w = MapData.CardinalOffsets[i];
				var front = tile - w;
				if (!Map.HasWallToward(tile.X, tile.Y, -w) && Map.IsWalkable(front.X, front.Y))
				{
					wallDir = w;
					break;
				}
			}

			// Rotating toward the wall direction faces chest.tscn out into the open tile.
			float rotation = MapData.FacingYaw(wallDir);
			(wallCount == 1 || (isCorridor && isDeadEnd) ? primary : corners).Add((tile, point, rotation));
		}

		return (primary, corners);
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

	private bool IsBlockedLootPoint(Vector3 point, List<Vector3> placedLootPoints, float spacing,
		float arrivalClearance = LootSpawnPlayerClearance)
	{
		if (Floor.Arrival != null && point.HorizontalDistanceTo(Floor.Arrival.GlobalPosition) < arrivalClearance)
		{
			return true;
		}

		for (int i = 0; i < Floor.StairLandings.Count; i++)
		{
			if (point.HorizontalDistanceTo(Floor.StairLandings[i]) < StairLandingClearance)
			{
				return true;
			}
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
		FloorContent.AddChild(pickup);
		pickup.GlobalPosition = point + Vector3.Up * GetFloorSurfaceHeight(point);
	}
}