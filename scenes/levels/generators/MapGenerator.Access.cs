using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Level-local access generation, guaranteed sources and restoration of seeded access deviations.</summary>
public partial class MapGenerator
{
	[Export] public LevelAccessProfile AccessProfile { get; set; }
	private readonly List<Vector3> _accessPickupPoints = [];
	private readonly HashSet<int> _secretRoomIds = [];
	private readonly Dictionary<string, DoorAccessSaveData> _accessBaseline = [];
	private readonly List<(Door Door, Vector2I Host, Vector2I Connector, Aabb Threshold)> _generatedSecrets = [];
	internal IReadOnlyList<(Door Door, Vector2I Host, Vector2I Connector, Aabb Threshold)> GeneratedSecrets => _generatedSecrets;
	/// <summary>Reserve the future threshold so furnishing cannot attach to its concealed wall or block the approach.</summary>
	private IEnumerable<Aabb> SecretThresholdBounds() => _generatedSecrets.Select(s => s.Threshold);
	private void ResetAccess()
	{
		_accessPickupPoints.Clear(); _secretRoomIds.Clear(); _accessBaseline.Clear(); _generatedSecrets.Clear();
	}
	/// <summary>Adds optional pocket rooms at variable corridor/procedural-wall segments, never authored-room sockets.</summary>
	private void CreateSecretBranches()
	{
		if (AccessProfile == null || AccessProfile.MaximumSecretRooms == 0) return;
		using var rng = GenerationRandom.Create(Seed, "access-secrets", GenerationAttempt.ToString());
		int secretBudget = AccessProfile.RollSecretRoomBudget(rng);
		if (secretBudget == 0) return;
		var requiredTiles = _roomRegions.SelectMany(r => r.FurnishingSlots).SelectMany(s => s.Occupied).ToArray();
		var existingContent = RoomArrangement.GetStaticPropBounds(NavigationRegion, GlobalTransform.AffineInverse() * NavigationRegion.GlobalTransform)
			.Concat(RoomDressingProfile.GetBounds(_roomRegions.SelectMany(r => r.FurnishingSlots))).ToArray();
		var candidates = new List<(Vector2I Host, Vector2I Direction)>();
		var pocketTiles = new HashSet<Vector2I>();
		for (int x = 1; x < Map.Width - 1; x++)
		for (int z = 1; z < Map.Height - 1; z++)
			if (Map.IsCorridor(x, z) || Map.IsRoom(x, z) && IsProceduralRoomTile(x, z)) foreach (var d in CardinalOffsets) candidates.Add((new(x, z), d));
		int count = 0;
		while (candidates.Count > 0 && count < secretBudget)
		{
			int index = rng.RandiRange(0, candidates.Count - 1);
			var (host, direction) = candidates[index]; candidates.RemoveAt(index);
			// Preserve required furnishing anchors and their wall/clearance dependencies, not just mesh overlap.
			if (requiredTiles.Any(t => Math.Abs(t.X - host.X) <= 1 && Math.Abs(t.Y - host.Y) <= 1)) continue;
			var tangent = new Vector2I(-direction.Y, direction.X);
			var tiles = new HashSet<Vector2I>();
			for (int depth = 2; depth <= 4; depth++) for (int side = -1; side <= 1; side++)
				tiles.Add(host + direction * depth + tangent * side);
			var connector = host + direction;
			tiles.Add(connector);
			if (tiles.Any(t => !Map.IsWithinBounds(t.X, t.Y) || Map.IsOnBoundary(t.X, t.Y) || !Map.IsEmpty(t.X, t.Y))) continue;
			// Existing corridors may run along the outer wall; ordinary room/corridor walls seal every side except the chosen connector.
			if (tiles.Any(t => CardinalOffsets.Any(d => !tiles.Contains(t + d) && Map.IsWithinBounds(t.X + d.X, t.Y + d.Y)
				&& Map.IsWalkable(t.X + d.X, t.Y + d.Y) && !Map.IsCorridor(t.X + d.X, t.Y + d.Y) && !(t == connector && t + d == host)))) continue;
			var wallCell = TileToWorld(host.X, 0, host.Y) + new Vector3I(direction.X * 2, 0, direction.Y * 2);
			var spanSize = direction.X == 0 ? new Vector3(4, 4, 2) : new Vector3(2, 4, 4);
			var threshold = new Aabb((Vector3)wallCell - new Vector3(spanSize.X * 0.5f, 0, spanSize.Z * 0.5f), spanSize);
			if (existingContent.Any(box => ConnectedArrangementStrategy.Overlaps(box, threshold, 1))) continue;
			int wallItem = WallGridMap.GetCellItem(wallCell);
			var wallMesh = wallItem < 0 ? null : WallGridMap.MeshLibrary.GetItemMesh(wallItem);
			// Only full straight spans qualify; corner and partial pieces remain ordinary geometry.
			if (wallMesh == null || Math.Max(wallMesh.GetAabb().Size.X, wallMesh.GetAabb().Size.Z) < 3.8f) continue;
			var region = new RoomRegion(_roomRegions.Count) { IsProcedural = true };
			// Floor variation is cosmetic: a per-pocket decoration stream keeps tile-palette changes from moving pockets.
			using var floorRng = GenerationRandom.Create(Seed, GenerationRandom.Decoration, $"secret-floor:{GenerationAttempt}:{connector.X}:{connector.Y}");
			foreach (var tile in tiles.OrderBy(t => t.X).ThenBy(t => t.Y))
			{
				Map.SetTile(tile.X, tile.Y, MapTile.Room); region.Tiles.Add(tile);
				_tileToRoom[tile] = region.Id;
				FloorGridMap.SetCellItem(TileToWorld(tile.X, 0, tile.Y), TileFactory.GetCorridorTileIndex(floorRng));
			}
			Map.SetConnector(connector.X, connector.Y, [-direction]);
			_tileToRoom.Remove(connector); region.ConnectorTiles.Add(connector); _connectorToRoom[connector] = region.Id;
			_roomRegions.Add(region); _secretRoomIds.Add(region.Id);
			var door = CreateConnectorDoor(connector, -direction); // Sits on wallCell, the host/pocket edge.
			door.Secret.Initialize(WallGridMap, wallCell, new Vector3(-direction.X, 0, -direction.Y), AccessProfile.Detection,
				GenerationRandom.DeriveSeed(Seed, "secret-discovery", $"{connector.X}:{connector.Y}"));
			_generatedSecrets.Add((door, host, connector, threshold)); count++;
			pocketTiles.UnionWith(tiles);
		}
		// Every other edge is already sealed, so only the pockets need walls.
		if (count > 0) PlaceWallsAround(pocketTiles.OrderBy(t => t.X).ThenBy(t => t.Y));
	}
	/// <summary>Assign optional regions only if all their entrances can be secured without gating progression.</summary>
	private void PlaceAccess()
	{
		if (AccessProfile != null)
		{
			using var rng = GenerationRandom.Create(Seed, "access-locks", GenerationAttempt.ToString());
			int lockBudget = AccessProfile.RollLockedRoomBudget(rng);
			var entry = WorldToTile(PlayerSpawnPoint.GlobalPosition);
			var blocked = _generatedSecrets.Select(s => s.Connector)
				.Concat(_doorIndicators.Where(d => d.Door.Lock?.IsLocked == true).Select(d => d.Connector)).ToHashSet();
			var targets = new List<(RoomRegion Region, List<Vector2I> Entrances, DoorLockDefinition Definition)>();
			int requiredEncounterRoom = -1;
			if (lockBudget > 0 && MobFactory is RealmEncounterFactory realm && realm.IsGatekeeperFloor(DungeonDepth) && realm.GatekeeperFit != null
				&& TryFindLargeEncounterPosition(Vector3.Zero, realm.GatekeeperFit.MinimumClearance, realm.GatekeeperFit.PreferredClearance, out var bossPoint))
				requiredEncounterRoom = GetRoomIdAt(WorldToTile(bossPoint));
			var candidates = _roomRegions.Where(r => r.Id != requiredEncounterRoom && !_secretRoomIds.Contains(r.Id) && !r.Tiles.Contains(entry)
				&& (r.IsProcedural || _doorIndicators.Any(d => r.ConnectorTiles.Contains(d.Connector) && d.Door.LockEligible))).ToList();
			// Protect progression anchors, stairs and existing explicit-lock rooms from arbitrary assignment.
			var protectedTiles = lockBudget == 0 ? [] : NavigationRegion.FindChildren("*", "", true, false).OfType<Node3D>()
				.Where(n => n is IProgressionAnchor or Door { Lock.Definition: not null } || n.IsInGroup("stairs"))
				.Select(n => WorldToTile(n.GlobalPosition)).ToHashSet();
			while (candidates.Count > 0 && targets.Count < lockBudget)
			{
				int index = rng.RandiRange(0, candidates.Count - 1); var region = candidates[index]; candidates.RemoveAt(index);
				var entrances = region.ConnectorTiles.Where(IsConnectorConnected).Distinct().ToList();
				if (entrances.Count == 0 || targets.Sum(t => t.Entrances.Count) + entrances.Count > AccessProfile.MaximumLockedDoors || entrances.Count > 3 || entrances.Any(c => Map.GetConnectorDirections(c.X, c.Y)
					.Count(d => Map.IsWithinBounds(c.X + d.X, c.Y + d.Y) && Map.IsCorridor(c.X + d.X, c.Y + d.Y)) != 1)) continue;
				if (!region.IsProcedural && entrances.Any(c => !_doorIndicators.Any(d => d.Connector == c && d.Door.LockEligible))) continue;
				if (region.Tiles.Any(protectedTiles.Contains)) continue;
				var trial = blocked.Concat(entrances).ToHashSet();
				var reached = AccessSolutionValidator.Reachable(Map, entry, trial);
				if (region.Tiles.Any(t => reached.Contains(t))) continue; // an unprotected inferred opening/bypass
				// Every other ordinary room must retain its route. Prior selected optional rooms stay blocked.
				if (_roomRegions.Any(r => r != region && !_secretRoomIds.Contains(r.Id) && !targets.Any(t => t.Region == r)
					&& r.Tiles.Any(t => Map.IsRoom(t.X, t.Y)) && !r.Tiles.Any(reached.Contains))) continue;
				blocked = trial;
				targets.Add((region, entrances, AccessProfile.Locks[rng.RandiRange(0, AccessProfile.Locks.Length - 1)]));
			}
			var accessible = targets.Count == 0 ? [] : AccessSolutionValidator.Reachable(Map, entry, blocked);
			var points = targets.Count == 0 ? [] : GetLooseItemCandidates().Where(p => accessible.Contains(WorldToTile(p)) && !IsColumnObstructed(p)).ToList();
			int sourceIndex = 0;
			foreach (var (region, entrances, definition) in targets)
			{
				// Guarantee all costs first, before changing any door. No partial pair on placement failure.
				var reserved = new List<Vector3>();
				foreach (var c in entrances)
				{
					var taken = _accessPickupPoints.Concat(reserved).ToList();
					var available = points.Where(p => !IsBlockedLootPoint(p, taken, LootSpawnSpacing)).ToList();
					if (available.Count == 0) break;
					reserved.Add(available[rng.RandiRange(0, available.Count - 1)]);
				}
				if (reserved.Count != entrances.Count) continue;
				for (int i = 0; i < entrances.Count; i++)
				{
					var connector = entrances[i];
					var matches = _doorIndicators.Where(d => d.Connector == connector).ToList();
					if (matches.Count > 1) throw new InvalidOperationException("Multiple physical doors share a generated lock threshold.");
					Door door = matches.Count == 1 ? matches[0].Door : CreateConnectorDoor(connector, Map.GetConnectorDirections(connector.X, connector.Y)
						.First(d => Map.IsWithinBounds(connector.X + d.X, connector.Y + d.Y) && Map.IsCorridor(connector.X + d.X, connector.Y + d.Y)));
					door.Lock.Initialize(definition); door.RefreshAccessPresentation();
					SpawnAccessItem(definition.RequiredKey, reserved[i], $"key:{sourceIndex++}");
				}
			}
		}
		int ordinal = 0;
		foreach (var door in LiveNodes<Door>())
		{
			var pair = _doorIndicators.FirstOrDefault(d => d.Door == door);
			door.ConnectionId = pair.Door != null ? $"connector:{pair.Connector.X}:{pair.Connector.Y}:{ordinal++}"
				: "authored:" + NavigationRegion.GetPathTo(door);
			_accessBaseline.Add(door.ConnectionId, CaptureDoor(door));
		}
	}
	/// <summary>Optional access must not strand teleport arrivals or hide required encounter placement.</summary>
	internal HashSet<Vector2I> FreelyAccessibleTiles()
	{
		if (Map == null || PlayerSpawnPoint == null) return null;
		var blocked = _doorIndicators.Where(d => d.Door.Lock?.IsLocked == true || !d.Door.IsDiscovered).Select(d => d.Connector).ToHashSet();
		return AccessSolutionValidator.Reachable(Map, WorldToTile(PlayerSpawnPoint.GlobalPosition), blocked);
	}
	/// <summary>Places a door on the connector's edge toward <paramref name="opening"/> and registers it as a fog/reachability seal.</summary>
	private Door CreateConnectorDoor(Vector2I connector, Vector2I opening)
	{
		var door = AccessProfile.DoorScene.Instantiate<Door>();
		if (door.Lock == null || door.Secret == null)
		{
			door.Free();
			throw new InvalidOperationException("The access door scene must wire its Lock and Secret components.");
		}
		NavigationRegion.AddChild(door);
		door.Position = (Vector3)TileToWorld(connector.X, 0, connector.Y) + new Vector3(opening.X * 2, 0, opening.Y * 2);
		door.RotationDegrees = new Vector3(0, opening.X == 0 ? 0 : 90, 0);
		_doorIndicators.Add((door, connector)); _dooredConnectors.Add(connector);
		return door;
	}
	private IEnumerable<T> LiveNodes<T>() where T : Node => NavigationRegion.FindChildren("*", "", true, false).OfType<T>().Where(n => !n.IsQueuedForDeletion());
	private void SpawnAccessItem(Item definition, Vector3 point, string identity, int quantity = 1)
	{
		if (LooseItemScene == null) throw new InvalidOperationException("Access generation requires a pickup scene.");
		string source = $"depth:{DungeonDepth}:access:{identity}";
		SpawnPickup(ItemFactory.Create(definition, instanceId: source + ":0"), quantity, source, point);
		_accessPickupPoints.Add(point);
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
	private bool IsSecretConnector(Vector2I tile) => _generatedSecrets.Any(s => s.Connector == tile);
	private static DoorAccessSaveData CaptureDoor(Door door) => new()
	{
		ConnectionId = door.ConnectionId, IsDiscovered = door.IsDiscovered, IsLocked = door.Lock?.IsLocked ?? false, IsOpen = door.IsOpen
	};
	/// <summary>This floor's deviations from its seeded baseline; the inverse of <see cref="RestoreExploration"/>.</summary>
	public RevealedLevelSaveData CaptureExploration() => new()
	{
		DungeonDepth = DungeonDepth,
		Doors = LiveNodes<Door>().Where(d => _accessBaseline.ContainsKey(d.ConnectionId)).Select(CaptureDoor)
			.Where(d => { var b = _accessBaseline[d.ConnectionId]; return d.IsDiscovered != b.IsDiscovered || d.IsLocked != b.IsLocked || d.IsOpen != b.IsOpen; }).ToList(),
		ExploredTiles = _revealedTiles.OrderBy(t => t.X).ThenBy(t => t.Y).Select(t => new Vector2ISaveData { X = t.X, Y = t.Y }).ToList(),
		DiscoveredTrapIds = LiveNodes<FloorTrap>().Where(t => t.IsDiscovered).Select(t => t.PersistentId).OrderBy(id => id, StringComparer.Ordinal).ToList(),
	};
	public void RestoreExploration(RevealedLevelSaveData saved)
	{
		if (saved == null) return;
		if (saved.DungeonDepth != DungeonDepth) throw new InvalidOperationException("Wrong exploration floor.");
		var doors = LiveNodes<Door>().ToDictionary(d => d.ConnectionId);
		var traps = LiveNodes<FloorTrap>().ToDictionary(t => t.PersistentId);
		if (saved.DiscoveredTrapIds.Distinct().Count() != saved.DiscoveredTrapIds.Count || saved.DiscoveredTrapIds.Any(id => !traps.ContainsKey(id)))
			throw new InvalidOperationException("Unknown or duplicate saved trap discovery.");
		var ids = new HashSet<string>();
		foreach (var state in saved.Doors)
		{
			state.Validate();
			if (!ids.Add(state.ConnectionId) || !doors.TryGetValue(state.ConnectionId, out var door)
				|| state.IsLocked && door.Lock?.Definition == null || !state.IsDiscovered && door.Secret?.IsSecret != true)
				throw new InvalidOperationException("Unknown, duplicate or incompatible saved door.");
		}
		if (saved.ExploredTiles.Any(t => !Map.IsWithinBounds(t.X, t.Y))) throw new InvalidOperationException("Invalid explored tile.");
		foreach (var state in saved.Doors) doors[state.ConnectionId].RestoreAccess(state);
		foreach (var id in saved.DiscoveredTrapIds) traps[id].RestoreDiscovered(true);
		foreach (var (door, connector) in _doorIndicators)
			if (door.IsOpen) _dooredConnectors.Remove(connector); else _dooredConnectors.Add(connector);
		foreach (var tile in saved.ExploredTiles) RevealTile(new Vector2I(tile.X, tile.Y));
		UpdateDoorIndicators();
	}
}