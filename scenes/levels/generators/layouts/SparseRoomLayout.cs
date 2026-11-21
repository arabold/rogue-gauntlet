using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Places Entrance, a geometrically distant Goal, and a sparse budget of dedicated rooms separated by
/// wide gaps so maze corridor generators can fill the remaining floor space.
/// </summary>
[Tool]
[GlobalClass]
public partial class SparseRoomLayout : RoomLayoutStrategy
{
	[Export(PropertyHint.Range, "0,10")] public int MinDedicatedRooms { get; set; } = 1;
	[Export(PropertyHint.Range, "0,12")] public int MaxDedicatedRooms { get; set; } = 3;
	[Export(PropertyHint.Range, "1,6")] public int MinRoomGap { get; set; } = 2;
	[Export(PropertyHint.Range, "0,40")] public int MinExitDistance { get; set; } = 8;
	[Export(PropertyHint.Range, "1,20")] public int Retries { get; set; } = 8;

	public override FloorPlan GenerateLayout(MapData map, FloorBuildContext context)
	{
		int attempts = Math.Max(1, Retries);
		for (int attempt = 0; attempt < attempts; attempt++)
		{
			if (TryBuildPlan(map, context, out var plan))
			{
				return plan;
			}
		}
		return new FloorPlan();
	}

	private bool TryBuildPlan(MapData map, FloorBuildContext context, out FloorPlan plan)
	{
		map.ResetToBorderedEmpty();
		plan = new FloorPlan();
		bool keepPlan = false;
		try
		{
			var rng = context.Rng;
			var progOptions = CreateRoomOptions(context, isProgressionRoom: true);
			var stdOptions = CreateRoomOptions(context);
			var fitOptions = context.Encounter?.GatekeeperFit != null
				? CreateRoomOptions(context, requiredFit: context.Encounter.GatekeeperFit)
				: null;

			var entrance = context.Pool.CreateEntrance(rng, progOptions);
			if (!TryPlaceEntrance(map, plan, entrance, rng, out var entrancePos))
			{
				entrance.Free();
				return false;
			}
			var entranceCenter = RectCenter(entrancePos, entrance.Map);

			var goal = CreateGoal(context, progOptions);
			if (!TryPlaceGoal(map, plan, goal, entranceCenter, rng))
			{
				goal.Free();
				return false;
			}

			int maxExtra = Math.Max(0, (int)context.MaxRooms - plan.Placements.Count);
			int minExtra = Math.Clamp(MinDedicatedRooms, 0, maxExtra);
			int maxTarget = Math.Clamp(Math.Max(MinDedicatedRooms, MaxDedicatedRooms), minExtra, maxExtra);
			int targetDedicated = rng.RandiRange(minExtra, maxTarget);

			var uses = new Dictionary<RoomDefinition, int>();
			var failedFitDefs = fitOptions != null ? new HashSet<RoomDefinition>() : null;
			var fitCandidates = fitOptions != null ? new List<RoomDefinition>() : null;
			int placedCount = 0;
			int maxFailures = targetDedicated + 4;
			int failures = 0;
			while (placedCount < targetDedicated && failures < maxFailures)
			{
				RoomCluster room = null;
				if (placedCount == 0 && fitOptions != null)
				{
					fitCandidates.Clear();
					fitCandidates.AddRange(context.Pool.Available(context.Pool.Rooms).Where(def => !failedFitDefs.Contains(def)));
					if (fitCandidates.Count > 0)
					{
						var picked = Weighted.PickBalanced(fitCandidates, d => d.Weight, d => uses.GetValueOrDefault(d), rng);
						try
						{
							room = context.Pool.CreateFrom(picked, rng, fitOptions);
						}
						catch (InvalidOperationException)
						{
							room?.Free();
							room = null;
							failedFitDefs.Add(picked);
							continue;
						}
					}
				}
				room ??= CreateBalancedRoom(context, uses, stdOptions);

				if (TryPlaceDedicated(map, plan, room, rng))
				{
					RecordRoomUse(uses, room);
					placedCount++;
				}
				else
				{
					room.Free();
					failures++;
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

	private bool TryPlaceEntrance(MapData map, FloorPlan plan, RoomCluster entrance, RandomNumberGenerator rng, out Vector2I placement)
	{
		var anchor = PickCornerAnchor(map, rng);
		return TryPlaceWithFallbacks(
			map,
			plan,
			entrance,
			(pos, rm, conns) => ScoreEntrance(map, pos, rm, conns, anchor),
			scoreTolerance: 1.5f,
			rng,
			out placement,
			primaryGap: 1,
			overrideMaxMargin: Math.Clamp(MinRoomGap, 1, 2));
	}

	private bool TryPlaceGoal(MapData map, FloorPlan plan, RoomCluster goal, Vector2 entranceCenter, RandomNumberGenerator rng)
	{
		int baseGap = Math.Max(1, MinRoomGap);
		return TryPlaceWithFallbacks(
			map,
			plan,
			goal,
			(pos, rm, conns) => ScoreGoal(map, pos, rm, conns, entranceCenter),
			scoreTolerance: 1.5f,
			rng,
			out _,
			primaryGap: baseGap,
			fallbackGap: baseGap > 1 ? 1 : -1);
	}

	private bool TryPlaceDedicated(MapData map, FloorPlan plan, RoomCluster room, RandomNumberGenerator rng)
	{
		var existingCenters = new List<Vector2>(plan.Placements.Count);
		foreach (var p in plan.Placements)
		{
			existingCenters.Add(PlacementCenter(p));
		}

		return TryPlaceWithFallbacks(
			map,
			plan,
			room,
			(pos, rm, conns) => ScoreDedicated(map, pos, rm, conns, existingCenters),
			scoreTolerance: 2f,
			rng,
			out _,
			primaryGap: Math.Max(1, MinRoomGap));
	}

	private static bool TryPlaceWithFallbacks(
		MapData map,
		FloorPlan plan,
		RoomCluster room,
		Func<Vector2I, MapData, List<(Vector2I Tile, Vector2I Direction)>, float> score,
		float scoreTolerance,
		RandomNumberGenerator rng,
		out Vector2I placement,
		int primaryGap,
		int fallbackGap = -1,
		int? overrideMaxMargin = null)
	{
		for (int pass = 0; pass < 2; pass++)
		{
			int gap = pass == 0 ? primaryGap : fallbackGap;
			if (gap <= 0) break;
			int highMargin = Math.Max(1, overrideMaxMargin ?? Math.Min(gap, 2));
			for (int margin = highMargin; margin >= 1; margin = margin > 1 ? 1 : 0)
			{
				int m = margin;
				if (TryFindBestPlacementAcrossRotations(
					map,
					room,
					rm => AllPlayableOrigins(map, rm, m),
					score,
					scoreTolerance,
					buffer: gap,
					rng,
					out placement))
				{
					AddPlacement(map, plan, room, placement);
					return true;
				}
			}
			if (fallbackGap == primaryGap) break;
		}

		placement = default;
		return false;
	}

	private static float ScoreEntrance(
		MapData map,
		Vector2I pos,
		MapData rm,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		Vector2 cornerTarget)
	{
		if (!AllExternalConnectorsPlayable(map, pos, connectors)) return float.NegativeInfinity;
		return ScoreCornerEntrance(map, pos, rm, connectors, cornerTarget, inwardWeight: 2f);
	}

	private float ScoreGoal(
		MapData map,
		Vector2I pos,
		MapData rm,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		Vector2 entranceCenter)
	{
		if (!AllExternalConnectorsPlayable(map, pos, connectors)) return float.NegativeInfinity;
		float dist = RectCenter(pos, rm).DistanceTo(entranceCenter);
		float penalty = dist < MinExitDistance ? -1000f : 0f;
		return dist + penalty;
	}

	private static float ScoreDedicated(
		MapData map,
		Vector2I pos,
		MapData rm,
		List<(Vector2I Tile, Vector2I Direction)> connectors,
		List<Vector2> existingCenters)
	{
		if (!AllExternalConnectorsPlayable(map, pos, connectors)) return float.NegativeInfinity;
		var center = RectCenter(pos, rm);
		float minDist = float.MaxValue;
		foreach (var existing in existingCenters)
		{
			minDist = Mathf.Min(minDist, center.DistanceTo(existing));
		}
		return minDist;
	}
}
