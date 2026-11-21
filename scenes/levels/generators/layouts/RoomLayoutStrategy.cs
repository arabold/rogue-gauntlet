using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

[Tool]
[GlobalClass]
public abstract partial class RoomLayoutStrategy : Resource
{
	/// <summary>
	/// Places clusters onto <paramref name="map"/> and returns the resulting <see cref="FloorPlan"/>.
	/// </summary>
	public abstract FloorPlan GenerateLayout(MapData map, FloorBuildContext context);

	/// <summary>Keeps <paramref name="cluster"/> at <paramref name="placement"/>, stamps it, and marks internal bridges protected.</summary>
	protected static void AddPlacement(MapData map, FloorPlan plan, RoomCluster cluster, Vector2I placement)
	{
		plan.Placements.Add((cluster, placement));
		map.Stamp(cluster.Map, placement);
		RecordProtectedCorridors(plan, cluster.Map, placement);
	}

	protected static void RecordProtectedCorridors(FloorPlan plan, MapData clusterMap, Vector2I placement)
	{
		for (int x = 0; x < clusterMap.Width; x++)
		{
			for (int z = 0; z < clusterMap.Height; z++)
			{
				if (clusterMap.IsCorridor(x, z))
				{
					plan.ProtectedCorridors.Add(new Vector2I(placement.X + x, placement.Y + z));
				}
			}
		}
	}

	protected static RoomCluster CreateGoal(FloorBuildContext context, RoomAssemblyOptions options) =>
		context.Profile?.GoalOverride != null
			? context.Pool.CreateFrom(context.Profile.GoalOverride, context.Rng, options)
			: context.Pool.CreateExit(context.Rng, options);

	/// <summary>Creates a standard room or group biased against repeating definitions already recorded in <paramref name="uses"/>.</summary>
	protected static RoomCluster CreateBalancedRoom(FloorBuildContext context, Dictionary<RoomDefinition, int> uses, RoomAssemblyOptions options = null) =>
		context.Pool.CreateStandardRoom(
			context.Rng,
			def => uses.GetValueOrDefault(def),
			options ?? context.Profile?.CreateRoomOptions(context.Pool.RealmId));

	/// <summary>Records a placed cluster's definition in <paramref name="uses"/> for balanced room selection.</summary>
	protected static void RecordRoomUse(Dictionary<RoomDefinition, int> uses, RoomCluster cluster)
	{
		var def = cluster?.Definition ?? cluster?.PrimaryRoom?.Type;
		if (def != null)
		{
			uses[def] = uses.GetValueOrDefault(def) + 1;
		}
	}

	/// <summary>Enumerates all placements inside <paramref name="map"/> keeping <paramref name="margin"/> tiles from edges.</summary>
	protected static IEnumerable<Vector2I> AllPlayableOrigins(MapData map, MapData roomMap, int margin = 1)
	{
		int maxX = map.Width - roomMap.Width - margin;
		int maxY = map.Height - roomMap.Height - margin;
		for (int x = margin; x <= maxX; x++)
		{
			for (int y = margin; y <= maxY; y++)
			{
				yield return new Vector2I(x, y);
			}
		}
	}

	/// <summary>The floor tiles just outside each external doorway of <paramref name="clusterMap"/> placed at <paramref name="placement"/>.</summary>
	protected static List<Vector2I> ExternalExitsAt(Vector2I placement, MapData clusterMap)
	{
		var ext = RoomCluster.GetExternalConnectors(clusterMap);
		var exits = new List<Vector2I>(ext.Count);
		foreach (var (tile, dir) in ext)
		{
			exits.Add(placement + tile + dir);
		}
		return exits;
	}

	/// <summary>True when every external connector opens onto a playable interior tile (not the outer border wall ring).</summary>
	protected static bool AllExternalConnectorsPlayable(
		MapData map,
		Vector2I placement,
		IReadOnlyList<(Vector2I Tile, Vector2I Direction)> connectors)
	{
		foreach (var (tile, direction) in connectors)
		{
			int ox = placement.X + tile.X + direction.X;
			int oy = placement.Y + tile.Y + direction.Y;
			if (ox < 1 || ox >= map.Width - 1 || oy < 1 || oy >= map.Height - 1)
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>True when <paramref name="roomMap"/> at <paramref name="placement"/> stays strictly inside the outer border wall ring.</summary>
	protected static bool FitsWithinPlayableBounds(MapData map, MapData roomMap, Vector2I placement, int margin = 1) =>
		placement.X >= margin && placement.Y >= margin
		&& placement.X + roomMap.Width <= map.Width - margin
		&& placement.Y + roomMap.Height <= map.Height - margin;

	/// <summary>Resets <paramref name="map"/> and re-stamps every placement and protected bridge tile remaining in <paramref name="plan"/>.</summary>
	protected static void RestampAll(MapData map, FloorPlan plan)
	{
		map.ResetToBorderedEmpty();
		plan.ProtectedCorridors.Clear();
		foreach (var (cluster, position) in plan.Placements)
		{
			map.Stamp(cluster.Map, position);
			RecordProtectedCorridors(plan, cluster.Map, position);
		}
	}

	protected static Vector2 PlacementCenter((RoomCluster Cluster, Vector2I Position) placement) =>
		RectCenter(placement.Position, placement.Cluster.Map);

	protected static Vector2 RectCenter(Vector2I placement, MapData roomMap) =>
		new(placement.X + roomMap.Width / 2f, placement.Y + roomMap.Height / 2f);

	protected static int CountAlignedConnectors(
		MapData map,
		Vector2I placement,
		IReadOnlyList<(Vector2I Tile, Vector2I Direction)> connectors,
		int roomGap,
		Func<Vector2I, Vector2I, bool> matchConnector = null)
	{
		int aligned = 0;
		foreach (var (tile, direction) in connectors)
		{
			var probe = placement + tile;
			for (int step = 0; step <= roomGap; step++)
			{
				probe += direction;
				if (!map.IsWithinBounds(probe.X, probe.Y)) break;
				if (map.IsEmpty(probe.X, probe.Y)) continue;
				if (map.IsConnectorOpenToward(probe.X, probe.Y, -direction)
					&& (matchConnector == null || matchConnector(probe, -direction)))
				{
					aligned++;
				}
				break;
			}
		}
		return aligned;
	}

	/// <summary>
	/// Evaluates all 4 quarter-turn orientations on pure <see cref="MapData"/> before rotating the winning cluster once
	/// and choosing randomly among candidates within <paramref name="scoreTolerance"/> of the best score.
	/// </summary>
	protected static bool TryFindBestPlacementAcrossRotations(
		MapData map,
		RoomCluster cluster,
		Func<MapData, IEnumerable<Vector2I>> collectCandidates,
		Func<Vector2I, MapData, List<(Vector2I Tile, Vector2I Direction)>, float> score,
		float scoreTolerance,
		int buffer,
		RandomNumberGenerator rng,
		out Vector2I bestPlacement)
	{
		var dilated = BuildDilatedMask(map, buffer);
		var scored = new List<(int Steps, Vector2I Position, float Score)>();
		var rotatedMap = cluster.Map;
		for (int steps = 0; steps < 4; steps++)
		{
			var connectors = RoomCluster.GetExternalConnectors(rotatedMap);
			foreach (var candidate in collectCandidates(rotatedMap))
			{
				if (!FitsWithinPlayableBounds(map, rotatedMap, candidate) || IntersectsDilated(rotatedMap, candidate, dilated))
				{
					continue;
				}
				float candidateScore = score(candidate, rotatedMap, connectors);
				if (float.IsFinite(candidateScore))
				{
					scored.Add((steps, candidate, candidateScore));
				}
			}
			if (steps < 3) rotatedMap = rotatedMap.Turned();
		}

		if (scored.Count == 0)
		{
			bestPlacement = default;
			return false;
		}

		float best = float.NegativeInfinity;
		foreach (var candidate in scored)
		{
			if (candidate.Score > best) best = candidate.Score;
		}
		float cutoff = best - Mathf.Max(scoreTolerance, 0f);
		int topCount = 0;
		for (int i = 0; i < scored.Count; i++)
		{
			if (scored[i].Score >= cutoff)
			{
				scored[topCount++] = scored[i];
			}
		}
		var chosen = scored[rng.RandiRange(0, topCount - 1)];
		cluster.Rotate(chosen.Steps);
		bestPlacement = chosen.Position;
		return true;
	}

	protected static RoomAssemblyOptions CreateRoomOptions(
		FloorBuildContext context,
		bool isProgressionRoom = false,
		LargeActorFitProfile requiredFit = null,
		int? minDoorways = null,
		int? maxDoorways = null) =>
		context.Profile?.CreateRoomOptions(context.Pool.RealmId, isProgressionRoom, requiredFit, minDoorways, maxDoorways)
			?? new RoomAssemblyOptions
			{
				Realm = context.Pool.RealmId,
				MinDoorways = minDoorways,
				MaxDoorways = maxDoorways,
				RequiredFit = requiredFit,
			};

	protected static Vector2 PickCornerAnchor(MapData map, RandomNumberGenerator rng)
	{
		int corner = rng.RandiRange(0, 3);
		return new(
			(corner & 1) == 0 ? 2 : map.Width - 3,
			(corner & 2) == 0 ? 2 : map.Height - 3);
	}

	protected static float ScoreCornerEntrance(
		MapData map,
		Vector2I pos,
		MapData rm,
		IReadOnlyList<(Vector2I Tile, Vector2I Direction)> connectors,
		Vector2 cornerTarget,
		float inwardWeight)
	{
		var mapCenter = new Vector2(map.Width * 0.5f, map.Height * 0.5f);
		var center = RectCenter(pos, rm);
		float cornerScore = -center.DistanceTo(cornerTarget);
		var toCenter = (mapCenter - center).Normalized();
		float inward = 0f;
		foreach (var (_, dir) in connectors)
		{
			inward += toCenter.Dot(new Vector2(dir.X, dir.Y));
		}
		return cornerScore + inward * inwardWeight;
	}

	private static bool[,] BuildDilatedMask(MapData map, int buffer)
	{
		int w = map.Width;
		int h = map.Height;
		int radius = Mathf.Max(0, buffer);
		var horiz = new bool[w, h];
		for (int y = 0; y < h; y++)
		{
			int count = 0;
			int initEnd = Math.Min(w - 1, radius);
			for (int x = 0; x <= initEnd; x++)
			{
				if (!map.IsEmpty(x, y)) count++;
			}
			for (int x = 0; x < w; x++)
			{
				horiz[x, y] = count > 0;
				int enter = x + radius + 1;
				if (enter < w && !map.IsEmpty(enter, y)) count++;
				int leave = x - radius;
				if (leave >= 0 && !map.IsEmpty(leave, y)) count--;
			}
		}

		var dilated = new bool[w, h];
		for (int x = 0; x < w; x++)
		{
			int count = 0;
			int initEnd = Math.Min(h - 1, radius);
			for (int y = 0; y <= initEnd; y++)
			{
				if (horiz[x, y]) count++;
			}
			for (int y = 0; y < h; y++)
			{
				dilated[x, y] = count > 0;
				int enter = y + radius + 1;
				if (enter < h && horiz[x, enter]) count++;
				int leave = y - radius;
				if (leave >= 0 && horiz[x, leave]) count--;
			}
		}
		return dilated;
	}

	private static bool IntersectsDilated(MapData roomMap, Vector2I placement, bool[,] dilated)
	{
		int w = dilated.GetLength(0);
		int h = dilated.GetLength(1);
		for (int rx = 0; rx < roomMap.Width; rx++)
		{
			for (int ry = 0; ry < roomMap.Height; ry++)
			{
				if (roomMap.IsEmpty(rx, ry)) continue;
				int tx = placement.X + rx;
				int ty = placement.Y + ry;
				if ((uint)tx >= (uint)w || (uint)ty >= (uint)h || dilated[tx, ty])
				{
					return true;
				}
			}
		}
		return false;
	}
}