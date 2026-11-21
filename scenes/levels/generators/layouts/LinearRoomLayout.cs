using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Builds a linear progression spine (<c>Entrance -> [Approach...] -> Goal</c>) with optional 1-doorway
/// dead-end sidearms, emitting exact <see cref="FloorPlan.ApprovedEdges"/> and marking intermediate
/// approach rooms (and the boss slot) as <see cref="RoomRegion.MustTraverse"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class LinearRoomLayout : RoomLayoutStrategy
{
	[Export(PropertyHint.Range, "0,8")] public int MinApproachRooms { get; set; } = 1;
	[Export(PropertyHint.Range, "0,8")] public int MaxApproachRooms { get; set; } = 3;
	[Export(PropertyHint.Range, "0,6")] public int MinSidearms { get; set; } = 0;
	[Export(PropertyHint.Range, "0,6")] public int MaxSidearms { get; set; } = 2;
	[Export(PropertyHint.Range, "1,64")] public int MaxBacktracks { get; set; } = 24;
	[Export(PropertyHint.Range, "1,4")] public int RoomGap { get; set; } = 1;
	[Export(PropertyHint.Range, "1,16")] public int Retries { get; set; } = 6;

	/// <summary>
	/// A placed node; <see cref="LinkedExit"/> is its own exit toward its parent and <see cref="ParentExitClaimed"/> the
	/// parent's exit paired with it, together the approved edge's endpoints.
	/// </summary>
	private sealed record PlacedSpineNode(
		RoomCluster Cluster,
		Vector2I Position,
		List<Vector2I> AllExits,
		HashSet<Vector2I> UsedExits,
		Vector2I? LinkedExit,
		Vector2I? ParentExitClaimed);

	public override FloorPlan GenerateLayout(MapData map, FloorBuildContext context)
	{
		int attempts = Math.Max(1, Retries);
		for (int attempt = 0; attempt < attempts; attempt++)
		{
			if (TryBuildAttempt(map, context, attempt, attempts, out var plan))
			{
				return plan;
			}
		}
		return new FloorPlan();
	}

	private bool TryBuildAttempt(MapData map, FloorBuildContext context, int attempt, int totalAttempts, out FloorPlan plan)
	{
		map.ResetToBorderedEmpty();
		plan = new FloorPlan();
		bool keepPlan = false;
		try
		{
			var rng = context.Rng;
			var uses = new Dictionary<RoomDefinition, int>();
			int maxRooms = Math.Max(2, (int)context.MaxRooms);
			int goalRoomSlots = context.Profile?.GoalOverride?.EstimatedRoomCount ?? 1;
			int maxExtraSlots = Math.Max(0, maxRooms - 1 - goalRoomSlots);

			int minApp = Math.Clamp(MinApproachRooms, 0, maxExtraSlots);
			int maxApp = Math.Clamp(Math.Max(MinApproachRooms, MaxApproachRooms), minApp, maxExtraSlots);
			int approachCount = attempt >= totalAttempts - 1
				? minApp
				: attempt >= totalAttempts / 2
					? rng.RandiRange(minApp, Math.Min(maxApp, Math.Max(minApp, 2)))
					: rng.RandiRange(minApp, maxApp);

			int remainingAfterSpine = Math.Max(0, maxExtraSlots - approachCount);
			int minSide = Math.Clamp(MinSidearms, 0, remainingAfterSpine);
			int maxSide = Math.Clamp(Math.Max(MinSidearms, MaxSidearms), minSide, remainingAfterSpine);
			int sidearmTarget = attempt >= totalAttempts / 2
				? minSide
				: rng.RandiRange(minSide, maxSide);

			// Assign at most 1 sidearm per non-goal spine node (preferring intermediate approach rooms).
			int spineNodeCount = 2 + approachCount; // 0: Entrance, 1..approachCount: Approach, spineNodeCount-1: Goal
			var sidearmsPerSpineSlot = new int[spineNodeCount - 1];
			var eligibleParents = new List<int>();
			for (int s = 1; s < spineNodeCount - 1; s++) eligibleParents.Add(s);
			eligibleParents.Add(0);
			int assignedSidearms = 0;
			while (assignedSidearms < sidearmTarget && eligibleParents.Count > 0)
			{
				// Bias toward intermediate approach rooms first, reserving index 0 (Entrance) for last.
				int pickLimit = eligibleParents.Count > 1 ? eligibleParents.Count - 1 : 1;
				int idx = rng.RandiRange(0, pickLimit - 1);
				int parentSlot = eligibleParents[idx];
				eligibleParents.RemoveAt(idx);
				sidearmsPerSpineSlot[parentSlot] = 1;
				assignedSidearms++;
			}

			var stack = new List<PlacedSpineNode>(spineNodeCount);
			var triedPositionsPerDepth = new Dictionary<int, HashSet<(Vector2I Pos, int Hash)>>();
			for (int d = 0; d < spineNodeCount; d++) triedPositionsPerDepth[d] = [];

			var entranceCorner = PickCornerAnchor(map, rng);
			var reservedExits = new HashSet<Vector2I>();
			var prevFreeExits = new List<Vector2I>();
			var prevFreeSet = new HashSet<Vector2I>();
			var seenCandidateExits = new HashSet<Vector2I>();
			var candidateExits = new List<Vector2I>();

			int backtracks = 0;
			int assemblySalt = 0;
			int depth = 0;

			while (depth < spineNodeCount)
			{
				using var stepRng = GenerationRandom.Create(rng.Randi(), $"linear:{attempt}:{depth}", (assemblySalt++).ToString());

				if (!TryAssembleSpineNode(context, depth, spineNodeCount, sidearmsPerSpineSlot, uses, stepRng, out var cluster))
				{
					if (depth == 0 || backtracks >= MaxBacktracks)
					{
						return false;
					}
					backtracks++;
					PopSpineNode(map, plan, stack);
					depth--;
					continue;
				}

				bool isGoal = depth == spineNodeCount - 1;
				if (depth > 0 && !isGoal)
				{
					cluster.MarkMustTraverse();
				}

				CollectReservedExits(stack, reservedExits);
				var prevNode = depth > 0 ? stack[depth - 1] : null;
				prevFreeExits.Clear();
				prevFreeSet.Clear();
				if (prevNode != null)
				{
					CollectFreeExits(prevNode, prevFreeExits);
					foreach (var exit in prevFreeExits) prevFreeSet.Add(exit);
				}
				var entranceCenter = depth > 0 ? PlacementCenter((stack[0].Cluster, stack[0].Position)) : Vector2.Zero;
				var tried = triedPositionsPerDepth[depth];

				Vector2I placement;
				bool placed;
				if (depth == 0)
				{
					placed = TryFindBestPlacementAcrossRotations(
						map,
						cluster,
						rm => AllPlayableOrigins(map, rm, margin: 1),
						(pos, rm, conns) => ScoreEntrance(map, pos, rm, conns, entranceCorner, tried, seenCandidateExits, candidateExits),
						scoreTolerance: 2.5f,
						buffer: Math.Max(1, RoomGap),
						stepRng,
						out placement);
				}
				else
				{
					placed = TryFindBestPlacementAcrossRotations(
						map,
						cluster,
						rm => AllPlayableOrigins(map, rm, margin: 1),
						(pos, rm, conns) => ScoreLinkedNode(map, pos, rm, conns, prevFreeSet, reservedExits, entranceCenter, isGoal, tried, seenCandidateExits, candidateExits),
						scoreTolerance: isGoal ? 1.5f : 3f,
						buffer: Math.Max(1, RoomGap),
						stepRng,
						out placement);
				}

				if (!placed)
				{
					cluster.Free();
					if (depth == 0 || backtracks >= MaxBacktracks)
					{
						return false;
					}
					backtracks++;
					triedPositionsPerDepth[depth].Clear();
					PopSpineNode(map, plan, stack);
					depth--;
					continue;
				}

				var clusterExits = ExternalExitsAt(placement, cluster.Map);
				var usedExits = new HashSet<Vector2I>();
				Vector2I? linkedExitClaimed = null;
				Vector2I? parentExitClaimed = null;
				if (prevNode != null)
				{
					var (linkedExit, parentExit) = PickClosestExitPair(clusterExits, prevFreeExits);
					usedExits.Add(linkedExit);
					prevNode.UsedExits.Add(parentExit);
					linkedExitClaimed = linkedExit;
					parentExitClaimed = parentExit;
				}

				tried.Add((placement, cluster.Map.Width * 31 + cluster.Map.Height * 97 + clusterExits.Count * 397));
				AddPlacement(map, plan, cluster, placement);
				RecordRoomUse(uses, cluster);
				stack.Add(new PlacedSpineNode(cluster, placement, clusterExits, usedExits, linkedExitClaimed, parentExitClaimed));
				depth++;
			}

			// Record sequential ApprovedEdges along the placed spine by ClusterId, between the exits each link paired.
			for (int i = 1; i < stack.Count; i++)
			{
				plan.ApprovedEdges.Add(new ApprovedEdge(i - 1, i, stack[i].ParentExitClaimed.Value, stack[i].LinkedExit.Value));
			}

			// Place any promised sidearms off non-goal spine slots.
			for (int slot = 0; slot < sidearmsPerSpineSlot.Length; slot++)
			{
				if (sidearmsPerSpineSlot[slot] == 0) continue;
				if (!TryPlaceSidearm(
					map, plan, context, slot, stack[slot], stack, uses,
					reservedExits, prevFreeExits, prevFreeSet, seenCandidateExits, candidateExits, rng))
				{
					return false;
				}
			}

			keepPlan = true;
			return true;
		}
		finally
		{
			if (!keepPlan)
			{
				plan.FreeUnplaced();
			}
		}
	}

	private static bool TryAssembleSpineNode(
		FloorBuildContext context,
		int depth,
		int spineNodeCount,
		int[] sidearmsPerSpineSlot,
		Dictionary<RoomDefinition, int> uses,
		RandomNumberGenerator stepRng,
		out RoomCluster cluster)
	{
		cluster = null;
		bool isGoal = depth == spineNodeCount - 1;

		for (int t = 0; t < 4; t++)
		{
			try
			{
				if (depth == 0)
				{
					int doors = 1 + sidearmsPerSpineSlot[0];
					var opts = CreateRoomOptions(context, isProgressionRoom: true, minDoorways: doors, maxDoorways: doors);
					cluster = context.Pool.CreateEntrance(stepRng, opts);
					return true;
				}
				if (isGoal)
				{
					var opts = CreateRoomOptions(
						context,
						isProgressionRoom: true,
						requiredFit: context.Encounter?.GatekeeperFit,
						minDoorways: 1,
						maxDoorways: 1);
					cluster = CreateGoal(context, opts);
					return true;
				}
				else
				{
					int doors = 2 + sidearmsPerSpineSlot[depth];
					var opts = CreateRoomOptions(context, minDoorways: doors, maxDoorways: doors);
					cluster = CreateBalancedRoom(context, uses, opts);
					return true;
				}
			}
			catch (InvalidOperationException)
			{
				cluster?.Free();
				cluster = null;
			}
		}

		return false;
	}

	private bool TryPlaceSidearm(
		MapData map,
		FloorPlan plan,
		FloorBuildContext context,
		int parentClusterId,
		PlacedSpineNode parent,
		List<PlacedSpineNode> allNodes,
		Dictionary<RoomDefinition, int> uses,
		HashSet<Vector2I> reservedExits,
		List<Vector2I> parentFreeExits,
		HashSet<Vector2I> parentFreeSet,
		HashSet<Vector2I> seenCandidateExits,
		List<Vector2I> candidateExits,
		RandomNumberGenerator rng)
	{
		var validDefs = context.Pool.Rooms
			.Where(def => def != null && def.SupportsRealm(context.Pool.RealmId) && def is RoomType rt && rt.MinDoorways <= 1)
			.ToList();
		if (validDefs.Count == 0) return false;

		var opts = CreateRoomOptions(context, minDoorways: 1, maxDoorways: 1);

		parentFreeExits.Clear();
		parentFreeSet.Clear();
		CollectFreeExits(parent, parentFreeExits);
		if (parentFreeExits.Count == 0) return false;
		foreach (var exit in parentFreeExits) parentFreeSet.Add(exit);
		CollectReservedExits(allNodes, reservedExits);

		for (int attempt = 0; attempt < 4; attempt++)
		{
			var def = Weighted.PickBalanced(validDefs, d => d.Weight, d => uses.GetValueOrDefault(d), rng);
			RoomCluster sidearm = null;
			try
			{
				sidearm = context.Pool.CreateFrom(def, rng, opts);
			}
			catch (InvalidOperationException)
			{
				sidearm?.Free();
				continue;
			}

			if (TryFindBestPlacementAcrossRotations(
				map,
				sidearm,
				rm => AllPlayableOrigins(map, rm, margin: 1),
				(pos, rm, conns) => ScoreSidearm(map, pos, rm, conns, parentFreeSet, reservedExits, seenCandidateExits, candidateExits),
				scoreTolerance: 2f,
				buffer: Math.Max(1, RoomGap),
				rng,
				out var placement))
			{
				var sidearmExits = ExternalExitsAt(placement, sidearm.Map);
				var (linkedExit, parentExit) = PickClosestExitPair(sidearmExits, parentFreeExits);
				parent.UsedExits.Add(parentExit);
				var usedExits = new HashSet<Vector2I> { linkedExit };

				int sidearmClusterId = plan.Placements.Count;
				AddPlacement(map, plan, sidearm, placement);
				RecordRoomUse(uses, sidearm);
				allNodes.Add(new PlacedSpineNode(sidearm, placement, sidearmExits, usedExits, linkedExit, parentExit));
				plan.ApprovedEdges.Add(new ApprovedEdge(parentClusterId, sidearmClusterId, parentExit, linkedExit));
				return true;
			}

			sidearm.Free();
		}

		return false;
	}

	private static void PopSpineNode(MapData map, FloorPlan plan, List<PlacedSpineNode> stack)
	{
		int last = stack.Count - 1;
		var popped = stack[last];
		stack.RemoveAt(last);
		if (popped.ParentExitClaimed.HasValue && stack.Count > 0)
		{
			stack[^1].UsedExits.Remove(popped.ParentExitClaimed.Value);
		}

		if (plan.Placements.Count > 0)
		{
			plan.Placements.RemoveAt(plan.Placements.Count - 1);
		}
		popped.Cluster.Free();
		RestampAll(map, plan);
	}

	private static void CollectReservedExits(List<PlacedSpineNode> nodes, HashSet<Vector2I> reserved)
	{
		reserved.Clear();
		foreach (var node in nodes)
		{
			foreach (var exit in node.AllExits) reserved.Add(exit);
		}
	}

	private static void CollectFreeExits(PlacedSpineNode node, List<Vector2I> free)
	{
		foreach (var exit in node.AllExits)
		{
			if (!node.UsedExits.Contains(exit))
			{
				free.Add(exit);
			}
		}
	}

	private static (Vector2I Exit, Vector2I Target) PickClosestExitPair(List<Vector2I> exits, List<Vector2I> targets)
	{
		if (exits.Count == 0) return default;
		if (targets.Count == 0) return (exits[0], default);
		var bestPair = (exits[0], targets[0]);
		int bestDist = int.MaxValue;
		foreach (var e in exits)
		{
			foreach (var t in targets)
			{
				int d = Math.Abs(e.X - t.X) + Math.Abs(e.Y - t.Y);
				if (d < bestDist)
				{
					bestDist = d;
					bestPair = (e, t);
				}
			}
		}
		return bestPair;
	}

	private static bool ValidateExitsDistinctAndOpen(
		MapData map,
		Vector2I pos,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		HashSet<Vector2I> allowedSharedExits,
		HashSet<Vector2I> allExistingExits,
		HashSet<Vector2I> seenBuffer,
		List<Vector2I> exitsBuffer)
	{
		if (!AllExternalConnectorsPlayable(map, pos, connectors)) return false;

		exitsBuffer.Clear();
		seenBuffer.Clear();
		foreach (var (tile, dir) in connectors)
		{
			var exit = pos + tile + dir;
			if (!seenBuffer.Add(exit)) return false;
			if (!map.IsEmpty(exit.X, exit.Y)) return false;
			if (allExistingExits != null && allExistingExits.Contains(exit) && (allowedSharedExits == null || !allowedSharedExits.Contains(exit)))
			{
				return false;
			}
			exitsBuffer.Add(exit);
		}
		return true;
	}

	private static (int Aligned, int MinDist) ScoreExitProximity(List<Vector2I> exits, HashSet<Vector2I> targetSet)
	{
		int minDist = int.MaxValue;
		int aligned = 0;
		foreach (var e in exits)
		{
			if (targetSet.Contains(e)) aligned++;
			foreach (var target in targetSet)
			{
				int d = Math.Abs(e.X - target.X) + Math.Abs(e.Y - target.Y);
				if (d < minDist) minDist = d;
			}
		}
		return (aligned, minDist);
	}

	private static float ScoreEntrance(
		MapData map,
		Vector2I pos,
		MapData rm,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		Vector2 corner,
		HashSet<(Vector2I Pos, int Hash)> tried,
		HashSet<Vector2I> seenBuffer,
		List<Vector2I> exitsBuffer)
	{
		if (!ValidateExitsDistinctAndOpen(map, pos, connectors, null, null, seenBuffer, exitsBuffer))
		{
			return float.NegativeInfinity;
		}
		if (tried.Contains((pos, rm.Width * 31 + rm.Height * 97 + exitsBuffer.Count * 397)))
		{
			return float.NegativeInfinity;
		}

		return ScoreCornerEntrance(map, pos, rm, connectors, corner, inwardWeight: 3f);
	}

	private float ScoreLinkedNode(
		MapData map,
		Vector2I pos,
		MapData rm,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		HashSet<Vector2I> prevFreeSet,
		HashSet<Vector2I> reservedExits,
		Vector2 entranceCenter,
		bool isGoal,
		HashSet<(Vector2I Pos, int Hash)> tried,
		HashSet<Vector2I> seenBuffer,
		List<Vector2I> exitsBuffer)
	{
		if (!ValidateExitsDistinctAndOpen(map, pos, connectors, prevFreeSet, reservedExits, seenBuffer, exitsBuffer))
		{
			return float.NegativeInfinity;
		}
		if (tried.Contains((pos, rm.Width * 31 + rm.Height * 97 + exitsBuffer.Count * 397)))
		{
			return float.NegativeInfinity;
		}

		var (aligned, minExitDist) = ScoreExitProximity(exitsBuffer, prevFreeSet);
		aligned += CountAlignedConnectors(map, pos, connectors, RoomGap, (oppConn, oppDir) => prevFreeSet.Contains(oppConn + oppDir));

		// Keep spare outgoing connectors (on multi-doorway approach nodes) out of cramped 1-tile wall seams when possible.
		float spareExitOpenness = 0f;
		if (!isGoal && exitsBuffer.Count > 1)
		{
			foreach (var e in exitsBuffer)
			{
				if (e.X <= 1 || e.X >= map.Width - 2 || e.Y <= 1 || e.Y >= map.Height - 2)
				{
					spareExitOpenness -= 2f;
				}
			}
		}

		float distFromEntrance = RectCenter(pos, rm).DistanceTo(entranceCenter);
		return aligned * 400f - minExitDist * 4f + distFromEntrance * (isGoal ? 0.5f : 0.25f) + spareExitOpenness;
	}

	private static float ScoreSidearm(
		MapData map,
		Vector2I pos,
		MapData rm,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		HashSet<Vector2I> parentFreeSet,
		HashSet<Vector2I> reservedExits,
		HashSet<Vector2I> seenBuffer,
		List<Vector2I> exitsBuffer)
	{
		if (!ValidateExitsDistinctAndOpen(map, pos, connectors, parentFreeSet, reservedExits, seenBuffer, exitsBuffer))
		{
			return float.NegativeInfinity;
		}

		var (aligned, minExitDist) = ScoreExitProximity(exitsBuffer, parentFreeSet);
		return aligned * 400f - minExitDist * 4f;
	}
}
