using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Level-local access generation, guaranteed sources and restoration of seeded access deviations.</summary>
public partial class MapGenerator
{
	[ExportGroup("Access")]
	[Export] public LevelAccessProfile AccessProfile { get; set; }
	private readonly List<Vector3> _accessPickupPoints = [];
	private readonly HashSet<int> _secretRoomIds = [];
	private readonly List<(Door Door, Vector2I Host, Vector2I Connector, Aabb Threshold)> _generatedSecrets = [];
	internal IReadOnlyList<(Door Door, Vector2I Host, Vector2I Connector, Aabb Threshold)> GeneratedSecrets => _generatedSecrets;
	/// <summary>Reserve the future threshold so furnishing cannot attach to its concealed wall or block the approach.</summary>
	private IEnumerable<Aabb> SecretThresholdBounds() => _generatedSecrets.Select(s => s.Threshold);
	private void ResetAccess()
	{
		_accessPickupPoints.Clear(); _secretRoomIds.Clear(); _generatedSecrets.Clear();
	}
	/// <summary>Adds optional pocket rooms at variable corridor/procedural-wall segments, never authored-room sockets.</summary>
	private void CreateSecretBranches()
	{
		if (AccessProfile == null || AccessProfile.MaximumSecretRooms == 0) return;
		using var rng = GenerationRandom.Create(Seed, "access-secrets", GenerationAttempt.ToString());
		int secretBudget = AccessProfile.RollSecretRoomBudget(rng);
		if (secretBudget == 0) return;
		var requiredTiles = Floor.Rooms.SelectMany(r => r.FurnishingSlots).SelectMany(s => s.Occupied).ToArray();
		var existingContent = RoomArrangement.GetStaticPropBounds(NavigationRegion, GlobalTransform.AffineInverse() * NavigationRegion.GlobalTransform)
			.Concat(RoomDressingProfile.GetBounds(Floor.Rooms.SelectMany(r => r.FurnishingSlots))).ToArray();
		var candidates = new List<(Vector2I Host, Vector2I Direction)>();
		var pocketTiles = new HashSet<Vector2I>();
		for (int x = 1; x < Map.Width - 1; x++)
		for (int z = 1; z < Map.Height - 1; z++)
			if (Map.IsCorridor(x, z) || Map.IsRoom(x, z) && Floor.IsProceduralRoomTile(x, z)) foreach (var d in MapData.CardinalOffsets) candidates.Add((new(x, z), d));
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
			if (tiles.Any(t => MapData.CardinalOffsets.Any(d => !tiles.Contains(t + d) && Map.IsWithinBounds(t.X + d.X, t.Y + d.Y)
				&& Map.IsWalkable(t.X + d.X, t.Y + d.Y) && !Map.IsCorridor(t.X + d.X, t.Y + d.Y) && !(t == connector && t + d == host)))) continue;
			var wallCell = Map.TileToWorld(host) + new Vector3I(direction.X * 2, 0, direction.Y * 2);
			var spanSize = direction.X == 0 ? new Vector3(4, 4, 2) : new Vector3(2, 4, 4);
			var threshold = new Aabb((Vector3)wallCell - new Vector3(spanSize.X * 0.5f, 0, spanSize.Z * 0.5f), spanSize);
			if (existingContent.Any(box => ConnectedArrangementStrategy.Overlaps(box, threshold, 1))) continue;
			int wallItem = WallGridMap.GetCellItem(wallCell);
			var wallMesh = wallItem < 0 ? null : WallGridMap.MeshLibrary.GetItemMesh(wallItem);
			// Only full straight spans qualify; corner and partial pieces remain ordinary geometry.
			if (wallMesh == null || Math.Max(wallMesh.GetAabb().Size.X, wallMesh.GetAabb().Size.Z) < 3.8f) continue;
			var region = new RoomRegion(Floor.Rooms.Count) { IsProcedural = true };
			// Floor variation is cosmetic: a per-pocket decoration stream keeps tile-palette changes from moving pockets.
			using var floorRng = GenerationRandom.Create(Seed, GenerationRandom.Decoration, $"secret-floor:{GenerationAttempt}:{connector.X}:{connector.Y}");
			foreach (var tile in tiles.OrderBy(t => t.X).ThenBy(t => t.Y))
			{
				Map.SetTile(tile.X, tile.Y, MapTile.Room); region.Tiles.Add(tile);
				Floor.AssignTile(tile, region.Id);
				FloorGridMap.SetCellItem(Map.TileToWorld(tile), TileFactory.GetCorridorTileIndex(floorRng));
			}
			Map.SetConnector(connector.X, connector.Y, [-direction]);
			Floor.UnassignTile(connector); region.ConnectorTiles.Add(connector);
			Floor.AddRoom(region); _secretRoomIds.Add(region.Id);
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
			var entry = Map.WorldToTile(Floor.Arrival.GlobalPosition);
			var blocked = _generatedSecrets.Select(s => s.Connector)
				.Concat(Floor.Doors.Where(d => d.Door.Lock?.IsLocked == true).Select(d => d.Connector)).ToHashSet();
			var targets = new List<(RoomRegion Region, List<Vector2I> Entrances, DoorLockDefinition Definition)>();
			// The gatekeeper's room was resolved before access; locking it would hide a required encounter.
			int requiredEncounterRoom = Floor.GatekeeperSite is Transform3D site ? Floor.RoomIdAt(Map.WorldToTile(site.Origin)) : -1;
			var candidates = Floor.Rooms.Where(r => r.Id != requiredEncounterRoom && !_secretRoomIds.Contains(r.Id) && !r.Tiles.Contains(entry)
				&& (r.IsProcedural || Floor.Doors.Any(d => r.ConnectorTiles.Contains(d.Connector) && d.Door.LockEligible))).ToList();
			// Protect progression anchors, stairs and existing explicit-lock rooms from arbitrary assignment.
			var protectedTiles = lockBudget == 0 ? [] : NavigationRegion.FindChildren("*", "", true, false).OfType<Node3D>()
				.Where(n => n is IProgressionAnchor or Door { Lock.Definition: not null } || n.IsInGroup("stairs"))
				.Select(n => Map.WorldToTile(n.GlobalPosition)).ToHashSet();
			while (candidates.Count > 0 && targets.Count < lockBudget)
			{
				int index = rng.RandiRange(0, candidates.Count - 1); var region = candidates[index]; candidates.RemoveAt(index);
				var entrances = region.ConnectorTiles.Where(IsConnectorConnected).Distinct().ToList();
				if (entrances.Count == 0 || targets.Sum(t => t.Entrances.Count) + entrances.Count > AccessProfile.MaximumLockedDoors || entrances.Count > 3 || entrances.Any(c => Map.GetConnectorDirections(c.X, c.Y)
					.Count(d => Map.IsWithinBounds(c.X + d.X, c.Y + d.Y) && Map.IsCorridor(c.X + d.X, c.Y + d.Y)) != 1)) continue;
				if (!region.IsProcedural && entrances.Any(c => !Floor.Doors.Any(d => d.Connector == c && d.Door.LockEligible))) continue;
				if (region.Tiles.Any(protectedTiles.Contains)) continue;
				var trial = blocked.Concat(entrances).ToHashSet();
				var reached = AccessSolutionValidator.Reachable(Map, entry, trial);
				if (region.Tiles.Any(t => reached.Contains(t))) continue; // an unprotected inferred opening/bypass
				// Every other ordinary room must retain its route. Prior selected optional rooms stay blocked.
				if (Floor.Rooms.Any(r => r != region && !_secretRoomIds.Contains(r.Id) && !targets.Any(t => t.Region == r)
					&& r.Tiles.Any(t => Map.IsRoom(t.X, t.Y)) && !r.Tiles.Any(reached.Contains))) continue;
				blocked = trial;
				targets.Add((region, entrances, AccessProfile.Locks[rng.RandiRange(0, AccessProfile.Locks.Length - 1)]));
			}
			var accessible = targets.Count == 0 ? [] : AccessSolutionValidator.Reachable(Map, entry, blocked);
			var points = targets.Count == 0 ? [] : GetLooseItemCandidates().Where(p => accessible.Contains(Map.WorldToTile(p)) && !Floor.IsColumnObstructed(p)).ToList();
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
					var matches = Floor.Doors.Where(d => d.Connector == connector).ToList();
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
			var pair = Floor.Doors.FirstOrDefault(d => d.Door == door);
			if (door.Interior == null) door.ConnectionId = pair.Door != null ? $"connector:{pair.Connector.X}:{pair.Connector.Y}:{ordinal++}"
				: "authored:" + NavigationRegion.GetPathTo(door);
			Floor.RecordDoorBaseline(door);
		}
	}
	/// <summary>Places a door on the connector's edge toward <paramref name="opening"/> and registers it with the floor; while closed it seals sight.</summary>
	private Door CreateConnectorDoor(Vector2I connector, Vector2I opening)
	{
		var door = AccessProfile.DoorScene.Instantiate<Door>();
		if (door.Lock == null || door.Secret == null)
		{
			door.Free();
			throw new InvalidOperationException("The access door scene must wire its Lock and Secret components.");
		}
		NavigationRegion.AddChild(door);
		door.Position = (Vector3)Map.TileToWorld(connector) + new Vector3(opening.X * 2, 0, opening.Y * 2);
		door.RotationDegrees = new Vector3(0, opening.X == 0 ? 0 : 90, 0);
		Floor.AddDoor(door, connector);
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
	private bool IsSecretConnector(Vector2I tile) => _generatedSecrets.Any(s => s.Connector == tile);
}