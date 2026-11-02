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
	private void ResetAccess() => _accessPickupPoints.Clear();
	/// <summary>
	/// Locks optional rooms whose every doorway has a door its room set there, as long as that never gates
	/// progression. Access never adds a door to a room.
	/// </summary>
	private void PlaceAccess()
	{
		if (AccessProfile != null) PlaceGeneratedLocks();

		int ordinal = 0;
		foreach (var door in LiveNodes<Door>())
		{
			if (door.Interior == null)
			{
				var connector = Floor.Doors.First(d => d.Door == door).Connector;
				door.ConnectionId = $"connector:{connector.X}:{connector.Y}:{ordinal++}";
			}
			Floor.RecordDoorBaseline(door);
		}
	}
	/// <summary>Reserves reachable keys before assigning any generated locks.</summary>
	private void PlaceGeneratedLocks()
	{
		using var rng = GenerationRandom.Create(Seed, "access-locks", GenerationAttempt.ToString());
		int lockBudget = AccessProfile.RollLockedRoomBudget(rng);
		if (lockBudget == 0) return;
		var entry = Map.WorldToTile(Floor.Arrival.GlobalPosition);
		var blocked = Floor.Doors.Where(d => d.Door.Lock?.IsLocked == true || !d.Door.IsDiscovered)
			.Select(d => d.Connector).ToHashSet();
		var initiallyReachable = AccessSolutionValidator.Reachable(Map, entry, blocked);
		var targets = new List<(RoomRegion Region, List<Vector2I> Entrances, DoorLockDefinition Definition)>();
		// The gatekeeper's room was resolved before access; locking it would hide a required encounter.
		int requiredEncounterRoom = Floor.GatekeeperSite is Transform3D site ? Floor.RoomIdAt(Map.WorldToTile(site.Origin)) : -1;
		var protectedRooms = NavigationRegion.FindChildren("*", "", true, false).OfType<Node3D>()
			.Where(n => n is IProgressionAnchor).Select(RoomRegionFor).Where(id => id >= 0).ToHashSet();
		var candidates = Floor.Rooms.Where(r => r.Id != requiredEncounterRoom && !protectedRooms.Contains(r.Id)
			&& r.Tiles.Any(initiallyReachable.Contains) && !r.Tiles.Contains(entry)).ToList();
		// Protect progression anchors, stairs and existing explicit-lock rooms from arbitrary assignment.
		var protectedTiles = NavigationRegion.FindChildren("*", "", true, false).OfType<Node3D>()
			.Where(n => n is IProgressionAnchor or Door { Lock.Definition: not null } || n.IsInGroup("stairs"))
			.Select(n => Map.WorldToTile(n.GlobalPosition)).ToHashSet();
		while (candidates.Count > 0 && targets.Count < lockBudget)
		{
			int index = rng.RandiRange(0, candidates.Count - 1); var region = candidates[index]; candidates.RemoveAt(index);
			// Every doorway is connected (generation throws otherwise) and opens one way only.
			var entrances = region.ConnectorTiles;
			if (entrances.Count == 0 || targets.Sum(t => t.Entrances.Count) + entrances.Count > AccessProfile.MaximumLockedDoors || entrances.Count > 3
				|| !entrances.All(c => Floor.Doors.Any(d => d.Connector == c && d.Door.Lock != null))) continue;
			if (region.Tiles.Any(protectedTiles.Contains)) continue;
			var trial = blocked.Concat(entrances).ToHashSet();
			var reached = AccessSolutionValidator.Reachable(Map, entry, trial);
			if (region.Tiles.Any(t => reached.Contains(t))) continue; // an unprotected bypass
			// Every other ordinary room must retain its route. Prior selected optional rooms stay blocked.
			if (Floor.Rooms.Any(r => r != region && r.Tiles.Any(initiallyReachable.Contains) && !targets.Any(t => t.Region == r)
				&& r.Tiles.Any(t => Map.IsRoom(t.X, t.Y)) && !r.Tiles.Any(reached.Contains))) continue;
			blocked = trial;
			targets.Add((region, entrances, AccessProfile.Locks[rng.RandiRange(0, AccessProfile.Locks.Length - 1)]));
		}
		if (targets.Count == 0) return;
		var accessible = AccessSolutionValidator.Reachable(Map, entry, blocked);
		var physicalReach = ReachableWithoutConnectors(blocked);
		var grid = Floor.Walkability;
		bool PhysicallyReachable(Vector3 point)
		{
			if (physicalReach == null || grid == null) return true;
			var cell = grid.CellAt(new Vector2(point.X, point.Z));
			return grid.Contains(cell) && physicalReach[cell.Y * grid.Size.X + cell.X];
		}
		var points = GetLooseItemCandidates().Where(p => accessible.Contains(Map.WorldToTile(p))
			&& PhysicallyReachable(p) && !Floor.IsColumnObstructed(p)).ToList();
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
				var door = Floor.Doors.Single(d => d.Connector == entrances[i]).Door;
				door.Lock.Initialize(definition); door.RefreshAccessPresentation();
				SpawnAccessItem(definition.RequiredKey, reserved[i], $"key:{sourceIndex++}");
			}
		}
	}

	private IEnumerable<T> LiveNodes<T>() where T : Node => NavigationRegion.FindChildren("*", "", true, false).OfType<T>().Where(n => !n.IsQueuedForDeletion());
	private void SpawnAccessItem(Item definition, Vector3 point, string identity, int quantity = 1)
	{
		if (LooseItemScene == null) throw new InvalidOperationException("Access generation requires a pickup scene.");
		string source = $"depth:{DungeonDepth}:access:{identity}";
		SpawnPickup(ItemFactory.Create(definition, instanceId: source + ":0"), quantity, source, point);
		_accessPickupPoints.Add(point);
	}
}