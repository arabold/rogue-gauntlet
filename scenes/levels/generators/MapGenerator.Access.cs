using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Level-local access generation, guaranteed sources and restoration of seeded access deviations.</summary>
public partial class MapGenerator
{
	private readonly List<Vector3> _accessPickupPoints = [];
	/// <summary>The rooms generated locks closed this attempt, except the exit's: each gets a reward chest.</summary>
	private readonly List<RoomRegion> _lockedRewardRooms = [];
	private void ResetAccess()
	{
		_accessPickupPoints.Clear();
		_lockedRewardRooms.Clear();
	}
	/// <summary>
	/// Locks optional rooms (and exit when enabled) whose every doorway has a door its room set there, as long as
	/// every lock has reachable keys and required progression routes remain solvable.
	/// </summary>
	private void PlaceAccess()
	{
		var accessProfile = CurrentProfile?.Access;
		if (accessProfile != null) PlaceGeneratedLocks(accessProfile);

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

	private sealed record PendingLockTarget(RoomRegion Region, List<Vector2I> Entrances, DoorLockDefinition Definition, List<Vector3> KeyPoints);

	/// <summary>Reserves reachable keys before assigning any generated locks (flat or chained).</summary>
	private void PlaceGeneratedLocks(FloorAccessProfile accessProfile)
	{
		using var rng = GenerationRandom.Create(Seed, "access-locks", GenerationAttempt.ToString());
		int lockBudget = accessProfile.RollLockedRoomBudget(rng);
		if (lockBudget == 0 || accessProfile.Locks.Length == 0) return;

		var entry = Map.WorldToTile(Floor.Arrival.GlobalPosition);
		var initialBlocked = Floor.Doors.Where(d => d.Door.Lock?.IsLocked == true || !d.Door.IsDiscovered)
			.Select(d => d.Connector).ToHashSet();
		var initiallyReachable = AccessSolutionValidator.Reachable(Map, entry, initialBlocked);

		int requiredEncounterRoom = Floor.GatekeeperSite is Transform3D site ? Floor.RoomIdAt(Map.WorldToTile(site.Origin)) : -1;
		var nodes = LiveNodes<Node3D>().ToList();
		bool canLockExit = accessProfile.AllowLockingExit;
		int exitRoomId = Floor.Descent != null ? RoomRegionFor(Floor.Descent) : -1;
		int arrivalRoomId = Floor.Arrival != null ? RoomRegionFor(Floor.Arrival) : -1;
		bool IsExemptExitNode(Node3D n) => canLockExit
			&& exitRoomId >= 0
			&& exitRoomId != arrivalRoomId
			&& n is not GatekeeperSpawnMarker
			&& n is not Door
			&& RoomRegionFor(n) == exitRoomId;
		var protectedRooms = nodes
			.Where(n => n is IProgressionAnchor && !IsExemptExitNode(n))
			.Select(RoomRegionFor)
			.Where(id => id >= 0)
			.ToHashSet();
		var protectedTiles = nodes
			.Where(n => (n is IProgressionAnchor or Door { Lock.Definition: not null } || n.IsInGroup("stairs"))
				&& !IsExemptExitNode(n))
			.Select(n => Map.WorldToTile(n.GlobalPosition))
			.ToHashSet();

		var candidates = Floor.Rooms.Where(r =>
			r.Id != requiredEncounterRoom
			&& r.SlotId != "boss"
			&& !r.ReserveAccess
			&& !r.MustTraverse
			&& !protectedRooms.Contains(r.Id)
			&& r.Tiles.Any(initiallyReachable.Contains)
			&& !r.Tiles.Contains(entry)).ToList();

		if (candidates.Count == 0) return;

		var doorByConnector = new Dictionary<Vector2I, Door>(Floor.Doors.Count);
		foreach (var (door, connector) in Floor.Doors)
		{
			doorByConnector[connector] = door;
		}
		var rawKeySpots = Floor.OpenTileCandidates();
		float deadEndWeight = CurrentProfile?.DeadEndLootWeight ?? 1f;
		var targets = accessProfile.AllowLockChains
			? SelectChainedLockTargets(accessProfile, lockBudget, exitRoomId, entry, initialBlocked, initiallyReachable, protectedTiles, doorByConnector, candidates, rawKeySpots, deadEndWeight, rng)
			: SelectFlatLockTargets(accessProfile, lockBudget, entry, initialBlocked, initiallyReachable, protectedTiles, doorByConnector, candidates, rawKeySpots, deadEndWeight, rng);

		int sourceIndex = 0;
		foreach (var target in targets)
		{
			if (target.Region.Id != exitRoomId) _lockedRewardRooms.Add(target.Region);
			for (int i = 0; i < target.Entrances.Count; i++)
			{
				var door = doorByConnector[target.Entrances[i]];
				door.Lock.Initialize(target.Definition);
				door.RefreshAccessPresentation();
				SpawnAccessItem(target.Definition.RequiredKey, target.KeyPoints[i], $"key:{sourceIndex++}");
			}
		}
	}

	private bool CanLockRegion(
		RoomRegion region,
		List<Vector2I> entrances,
		int usedLockedDoors,
		FloorAccessProfile accessProfile,
		HashSet<Vector2I> protectedTiles,
		HashSet<Vector2I> baseBlocked,
		HashSet<Vector2I> initiallyReachable,
		HashSet<RoomRegion> selectedRegions,
		Dictionary<Vector2I, Door> doorByConnector,
		Vector2I entry,
		out HashSet<Vector2I> trialBlocked,
		out HashSet<Vector2I> trialReached)
	{
		trialBlocked = null;
		trialReached = null;
		if (entrances.Count == 0
			|| usedLockedDoors + entrances.Count > accessProfile.MaximumLockedDoors
			|| entrances.Count > 3
			|| !entrances.All(c => doorByConnector.TryGetValue(c, out var door)
				&& door.Lock != null
				&& door.Lock.Definition == null
				&& door.Secret?.IsSecret != true)
			|| region.Tiles.Any(protectedTiles.Contains))
		{
			return false;
		}

		trialBlocked = [.. baseBlocked, .. entrances];
		var reached = trialReached = AccessSolutionValidator.Reachable(Map, entry, trialBlocked);
		if (region.Tiles.Any(reached.Contains)) return false;

		return !Floor.Rooms.Any(r =>
			r != region
			&& !selectedRegions.Contains(r)
			&& r.Tiles.Any(initiallyReachable.Contains)
			&& r.Tiles.Any(t => Map.IsRoom(t.X, t.Y))
			&& !r.Tiles.Any(reached.Contains));
	}

	private List<PendingLockTarget> SelectFlatLockTargets(
		FloorAccessProfile accessProfile,
		int lockBudget,
		Vector2I entry,
		HashSet<Vector2I> initialBlocked,
		HashSet<Vector2I> initiallyReachable,
		HashSet<Vector2I> protectedTiles,
		Dictionary<Vector2I, Door> doorByConnector,
		List<RoomRegion> candidates,
		List<Vector3> rawKeySpots,
		float deadEndWeight,
		RandomNumberGenerator rng)
	{
		// Keys are placed as each room is chosen: a room whose keys do not fit is never treated as locked, so it keeps
		// its place in every later room's route check and nothing ends up gated behind an unplanned lock.
		var blocked = new HashSet<Vector2I>(initialBlocked);
		var committed = new List<PendingLockTarget>();
		var committedRegions = new HashSet<RoomRegion>();
		var reservedKeys = new List<Vector3>();
		int usedDoors = 0;
		while (candidates.Count > 0 && committed.Count < lockBudget)
		{
			int index = rng.RandiRange(0, candidates.Count - 1);
			var region = candidates[index];
			candidates.RemoveAt(index);
			var entrances = region.ConnectorTiles;
			if (!CanLockRegion(region, entrances, usedDoors, accessProfile, protectedTiles, blocked, initiallyReachable,
				committedRegions, doorByConnector, entry, out var trial, out var trialReached))
			{
				continue;
			}

			// Every key already placed must stay reachable with this room locked as well.
			var physicalReach = ReachableWithoutConnectors(trial);
			if (!reservedKeys.All(key => trialReached.Contains(Map.WorldToTile(key)) && IsPhysicallyReachable(key, physicalReach)))
			{
				continue;
			}

			var definition = accessProfile.Locks[rng.RandiRange(0, accessProfile.Locks.Length - 1)];
			if (!TryPickKeyPositions(rawKeySpots, trialReached, physicalReach, reservedKeys, entrances.Count, deadEndWeight, null, rng, out var picked))
			{
				continue;
			}

			blocked = trial;
			usedDoors += entrances.Count;
			reservedKeys.AddRange(picked);
			committed.Add(new PendingLockTarget(region, entrances, definition, picked));
			committedRegions.Add(region);
		}
		return committed;
	}

	private List<PendingLockTarget> SelectChainedLockTargets(
		FloorAccessProfile accessProfile,
		int lockBudget,
		int exitRoomId,
		Vector2I entry,
		HashSet<Vector2I> initialBlocked,
		HashSet<Vector2I> initiallyReachable,
		HashSet<Vector2I> protectedTiles,
		Dictionary<Vector2I, Door> doorByConnector,
		List<RoomRegion> candidates,
		List<Vector3> rawKeySpots,
		float deadEndWeight,
		RandomNumberGenerator rng)
	{
		const float ChainInteriorBoost = 3f;
		var blocked = new HashSet<Vector2I>(initialBlocked);
		var committed = new List<PendingLockTarget>();
		var committedRegions = new HashSet<RoomRegion>();
		var earlierLockedTiles = new HashSet<Vector2I>();
		var reservedKeys = new List<Vector3>();
		var usedKeyPaths = new HashSet<string>();
		int usedDoors = 0;

		while (candidates.Count > 0 && committed.Count < lockBudget)
		{
			int index = rng.RandiRange(0, candidates.Count - 1);
			var region = candidates[index];
			candidates.RemoveAt(index);
			var entrances = region.ConnectorTiles;
			if (!CanLockRegion(region, entrances, usedDoors, accessProfile, protectedTiles, blocked, initiallyReachable,
				committedRegions, doorByConnector, entry, out var trialBlocked, out var openWithoutLocks))
			{
				continue;
			}

			// Do not lock a room that encloses an already-committed key without keeping the earlier chain solvable.
			if (reservedKeys.Any(k => region.Tiles.Contains(Map.WorldToTile(k)))) continue;

			var (preLockReachable, closedAtFixedPoint) = SolvePrefixWithLockedRoom(entry, initialBlocked, committed, entrances);
			if (!committed.All(c => c.KeyPoints.All(k => preLockReachable.Contains(Map.WorldToTile(k))))) continue;

			var unusedDefs = accessProfile.Locks
				.Where(l => !usedKeyPaths.Contains(l.RequiredKey.DefinitionPath))
				.ToList();

			DoorLockDefinition definition;
			bool allowInsideEarlierLocks;
			if (unusedDefs.Count > 0)
			{
				definition = unusedDefs[rng.RandiRange(0, unusedDefs.Count - 1)];
				allowInsideEarlierLocks = true;
			}
			else
			{
				// Reuse a key color only when every prior key of that color stays freely reachable without unlocking anything.
				var reusableDefs = accessProfile.Locks.Where(l => !committed.Any(c =>
					c.Definition.RequiredKey.DefinitionPath == l.RequiredKey.DefinitionPath
					&& c.KeyPoints.Any(k => !openWithoutLocks.Contains(Map.WorldToTile(k))))).ToList();
				if (reusableDefs.Count == 0) continue;
				definition = reusableDefs[rng.RandiRange(0, reusableDefs.Count - 1)];
				allowInsideEarlierLocks = false;
			}

			var eligibleTiles = allowInsideEarlierLocks ? preLockReachable : openWithoutLocks;
			var physicalReach = ReachableWithoutConnectors(allowInsideEarlierLocks ? closedAtFixedPoint : trialBlocked);
			var bonusTiles = allowInsideEarlierLocks && earlierLockedTiles.Count > 0 ? earlierLockedTiles : null;
			if (!TryPickKeyPositions(rawKeySpots, eligibleTiles, physicalReach, reservedKeys, entrances.Count, deadEndWeight, bonusTiles, rng, out var picked, ChainInteriorBoost))
			{
				continue;
			}

			committed.Add(new PendingLockTarget(region, entrances, definition, picked));
			committedRegions.Add(region);
			blocked = trialBlocked;
			usedDoors += entrances.Count;
			reservedKeys.AddRange(picked);
			usedKeyPaths.Add(definition.RequiredKey.DefinitionPath);
			if (region.Id != exitRoomId)
			{
				foreach (var tile in region.Tiles) earlierLockedTiles.Add(tile);
			}
		}

		return committed;
	}

	private (HashSet<Vector2I> Reachable, HashSet<Vector2I> StillClosed) SolvePrefixWithLockedRoom(
		Vector2I entry,
		HashSet<Vector2I> initialBlocked,
		IReadOnlyList<PendingLockTarget> priorTargets,
		IReadOnlyList<Vector2I> keptClosedEntrances)
	{
		var simBlocked = new HashSet<Vector2I>(initialBlocked);
		foreach (var t in priorTargets)
		foreach (var e in t.Entrances) simBlocked.Add(e);
		foreach (var e in keptClosedEntrances) simBlocked.Add(e);

		var unlocked = new HashSet<int>();
		HashSet<Vector2I> reached;
		while (true)
		{
			reached = AccessSolutionValidator.Reachable(Map, entry, simBlocked);
			bool progressed = false;
			for (int i = 0; i < priorTargets.Count; i++)
			{
				if (unlocked.Contains(i)) continue;
				if (priorTargets[i].KeyPoints.All(k => reached.Contains(Map.WorldToTile(k))))
				{
					unlocked.Add(i);
					foreach (var e in priorTargets[i].Entrances) simBlocked.Remove(e);
					progressed = true;
				}
			}
			if (!progressed) break;
		}
		return (reached, simBlocked);
	}

	/// <summary>Whether <paramref name="point"/> lies on walkability <paramref name="physicalReach"/> marks reachable; true without a grid.</summary>
	private bool IsPhysicallyReachable(Vector3 point, bool[] physicalReach)
	{
		var grid = Floor.Walkability;
		if (physicalReach == null || grid == null) return true;
		var cell = grid.CellAt(new Vector2(point.X, point.Z));
		return grid.Contains(cell) && physicalReach[cell.Y * grid.Size.X + cell.X];
	}

	private bool TryPickKeyPositions(
		List<Vector3> rawCandidates,
		HashSet<Vector2I> accessible,
		bool[] physicalReach,
		List<Vector3> alreadyTaken,
		int count,
		float deadEndWeight,
		HashSet<Vector2I> bonusTiles,
		RandomNumberGenerator rng,
		out List<Vector3> picked,
		float bonusMultiplier = 1f)
	{
		picked = new List<Vector3>(count);
		var taken = new List<Vector3>(_accessPickupPoints.Count + alreadyTaken.Count + count);
		taken.AddRange(_accessPickupPoints);
		taken.AddRange(alreadyTaken);

		var eligible = new List<(Vector3 Point, float Weight)>();
		foreach (var p in rawCandidates)
		{
			var tile = Map.WorldToTile(p);
			if (!accessible.Contains(tile) || !IsPhysicallyReachable(p, physicalReach) || IsBlockedLootPoint(p, taken, LootSpawnSpacing)) continue;
			float w = Map.OpenNeighborCount(tile.X, tile.Y) == 1 ? deadEndWeight : 1f;
			if (bonusTiles != null && bonusTiles.Contains(tile)) w *= bonusMultiplier;
			eligible.Add((p, w));
		}

		while (picked.Count < count && eligible.Count > 0)
		{
			int idx = PickWeightedCandidateIndex(eligible, item => item.Weight, rng);
			var candidate = eligible[idx].Point;
			eligible.RemoveAt(idx);
			if (IsBlockedLootPoint(candidate, picked, LootSpawnSpacing) || Floor.IsColumnObstructed(candidate)) continue;
			picked.Add(candidate);
		}
		return picked.Count == count;
	}

	private IEnumerable<T> LiveNodes<T>() where T : Node => FloorContent.FindChildren("*", "", true, false).OfType<T>().Where(n => !n.IsQueuedForDeletion());
	private void SpawnAccessItem(Item definition, Vector3 point, string identity)
	{
		if (LooseItemScene == null) throw new InvalidOperationException("Access generation requires a pickup scene.");
		string source = $"depth:{DungeonDepth}:access:{identity}";
		SpawnPickup(ItemFactory.Create(definition, instanceId: source + ":0"), 1, source, point);
		_accessPickupPoints.Add(point);
	}
}