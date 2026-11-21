using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A house plan: one hallway runs the length of the floor with the entrance at one end and the goal at the other, side
/// halls branch off it toward the map edge, and rooms line both sides of every hall, each opening onto it. The halls are
/// laid as corridor tiles; the corridor connector then joins any doorway that does not open onto a hall.
/// </summary>
[Tool]
[GlobalClass]
public partial class HallwayRoomLayout : RoomLayoutStrategy
{
	/// <summary>Side halls branching off the main hallway, each lined with rooms of its own.</summary>
	[Export(PropertyHint.Range, "0,4")] public int Branches { get; set; } = 2;
	/// <summary>Most doorways a side room gets. With 1 every room opens only onto a hall; more let some rooms add a back corridor.</summary>
	[Export(PropertyHint.Range, "1,4")] public int MaxSideDoorways { get; set; } = 2;
	/// <summary>Rooms that may fail to fit in a row before the layout stops adding rooms.</summary>
	[Export(PropertyHint.Range, "1,16")] public int Retries { get; set; } = 8;
	/// <summary>Spots within this score of the best one are picked at random, trading a little tightness for variety.</summary>
	[Export(PropertyHint.Range, "0,10")] public float ScoreTolerance { get; set; } = 1f;

	/// <summary>Score cost of a doorway that does not open onto a hall and needs a corridor of its own.</summary>
	private const float BackDoorCost = 6f;
	/// <summary>Shortest hall worth laying, main or side: a shorter one has no room for rooms along it.</summary>
	private const int MinimumHallLength = 4;

	public override FloorPlan GenerateLayout(MapData map, FloorBuildContext context)
	{
		var plan = new FloorPlan();
		bool keepPlan = false;
		try
		{
			var rng = context.Rng;
			bool horizontal = map.Width != map.Height ? map.Width > map.Height : rng.Randf() < .5f;
			int length = horizontal ? map.Width : map.Height;
			int across = horizontal ? map.Height : map.Width;
			int line = across / 2 + rng.RandiRange(-across / 8, across / 8);

			// Hall tiles in walking order: the main hallway from one end, then each side hall from its junction outward.
			var halls = new Dictionary<Vector2I, int>();
			for (int along = 1; along < length - 1; along++) halls[At(horizontal, along, line)] = along;

			var progressionOptions = CreateRoomOptions(context, isProgressionRoom: true);
			var entrance = context.Pool.CreateEntrance(rng, progressionOptions);
			if (!Attach(map, plan, entrance, halls, (order, _) => -order, rng, out int start))
			{
				entrance.Free();
				return plan;
			}
			var goal = CreateGoal(context, progressionOptions);
			if (!Attach(map, plan, goal, halls, (order, _) => order, rng, out int end) || end <= start + MinimumHallLength)
			{
				goal.Free();
				return new FloorPlan();
			}

			// The main hallway runs from the entrance's doorway to the goal's, so the walk between them passes every room.
			foreach (var tile in halls.Where(pair => pair.Value < start || pair.Value > end).Select(pair => pair.Key).ToList())
				halls.Remove(tile);
			var branches = AddBranches(map, halls, horizontal, line, start, end, rng);

			var uses = new Dictionary<RoomDefinition, int>();
			var sideOptions = CreateRoomOptions(context, maxDoorways: MaxSideDoorways);
			var sideRooms = context.Pool.Rooms.Where(def => def != null && (def is not RoomType type || type.MinDoorways <= MaxSideDoorways)).ToList();
			for (int placed = 0, failures = 0; sideRooms.Count > 0 && placed < context.MaxRooms && failures < Retries;)
			{
				RoomCluster room;
				try
				{
					room = context.Pool.CreateFrom(Weighted.PickBalanced(sideRooms, def => def.Weight, def => uses.GetValueOrDefault(def), rng), rng, sideOptions);
				}
				catch (InvalidOperationException)
				{
					failures++;
					continue;
				}
				if (Attach(map, plan, room, halls, (order, backDoors) => -order - backDoors * BackDoorCost, rng, out _))
				{
					RecordRoomUse(uses, room);
					placed++;
					failures = 0;
				}
				else
				{
					room.Free();
					failures++;
				}
			}

			TrimBranches(plan, halls, branches);
			foreach (var tile in halls.Keys)
				if (map.IsEmpty(tile.X, tile.Y)) map.SetTile(tile.X, tile.Y, MapTile.Corridor);
			keepPlan = true;
			return plan;
		}
		finally
		{
			if (!keepPlan) plan.FreeUnplaced();
		}
	}

	private static Vector2I At(bool horizontal, int along, int across) => horizontal ? new(along, across) : new(across, along);

	/// <summary>
	/// Places <paramref name="cluster"/> with at least one doorway opening onto a hall tile and none of its tiles on a hall,
	/// at the best <paramref name="score"/> of the earliest hall tile it opens onto and its doorways that open elsewhere.
	/// </summary>
	private bool Attach(MapData map, FloorPlan plan, RoomCluster cluster, Dictionary<Vector2I, int> halls,
		Func<int, int, float> score, RandomNumberGenerator rng, out int order)
	{
		if (!TryFindBestPlacementAcrossRotations(map, cluster,
			roomMap => Candidates(roomMap, halls),
			(placement, roomMap, connectors) => Score(map, placement, roomMap, connectors, halls, score),
			ScoreTolerance, buffer: 1, rng, out var position))
		{
			order = -1;
			return false;
		}
		order = ExternalExitsAt(position, cluster.Map).Where(halls.ContainsKey).Min(exit => halls[exit]);
		AddPlacement(map, plan, cluster, position);
		return true;
	}

	/// <summary>Every placement that puts one of the room's doorways right beside a hall tile, facing it.</summary>
	private static IEnumerable<Vector2I> Candidates(MapData roomMap, Dictionary<Vector2I, int> halls)
	{
		var seen = new HashSet<Vector2I>();
		foreach (var (tile, direction) in RoomCluster.GetExternalConnectors(roomMap))
		foreach (var hall in halls.Keys)
		{
			var placement = hall - direction - tile;
			if (seen.Add(placement)) yield return placement;
		}
	}

	private static float Score(MapData map, Vector2I placement, MapData roomMap,
		IReadOnlyList<(Vector2I Tile, Vector2I Direction)> connectors, Dictionary<Vector2I, int> halls, Func<int, int, float> score)
	{
		if (!AllExternalConnectorsPlayable(map, placement, connectors)) return float.NegativeInfinity;
		for (int x = 0; x < roomMap.Width; x++)
		for (int z = 0; z < roomMap.Height; z++)
			if (!roomMap.IsEmpty(x, z) && halls.ContainsKey(placement + new Vector2I(x, z))) return float.NegativeInfinity;

		int earliest = int.MaxValue, backDoors = 0;
		foreach (var (tile, direction) in connectors)
		{
			var exit = placement + tile + direction;
			if (halls.TryGetValue(exit, out int order)) earliest = Math.Min(earliest, order);
			else if (!map.IsEmpty(exit.X, exit.Y)) return float.NegativeInfinity;
			else backDoors++;
		}
		return earliest == int.MaxValue ? float.NegativeInfinity : score(earliest, backDoors);
	}

	/// <summary>
	/// Side halls from evenly spaced points of the main hallway toward the map edge, alternating sides, each stopping a
	/// tile short of the border or of a placed room. A hall too short for rooms is dropped.
	/// </summary>
	private List<List<Vector2I>> AddBranches(MapData map, Dictionary<Vector2I, int> halls, bool horizontal, int line,
		int start, int end, RandomNumberGenerator rng)
	{
		var branches = new List<List<Vector2I>>();
		int side = rng.Randf() < .5f ? 1 : -1;
		for (int index = 0; index < Branches; index++, side = -side)
		{
			int along = start + (end - start) * (index + 1) / (Branches + 1);
			int junction = halls[At(horizontal, along, line)];
			var branch = new List<Vector2I>();
			for (int step = 1; ; step++)
			{
				var tile = At(horizontal, along, line + side * step);
				var next = At(horizontal, along, line + side * (step + 1));
				if (!map.IsWithinBounds(next.X, next.Y) || map.IsOnBoundary(next.X, next.Y) || !map.IsEmpty(tile.X, tile.Y)
					|| !map.IsEmpty(next.X, next.Y)) break;
				branch.Add(tile);
			}
			if (branch.Count < MinimumHallLength) continue;
			for (int step = 0; step < branch.Count; step++) halls[branch[step]] = junction + step + 1;
			branches.Add(branch);
		}
		return branches;
	}

	/// <summary>Cuts each side hall back to its farthest doorway, so none ends in a bare stub, and drops one no room opens onto.</summary>
	private static void TrimBranches(FloorPlan plan, Dictionary<Vector2I, int> halls, List<List<Vector2I>> branches)
	{
		var exits = plan.Placements.SelectMany(placement => ExternalExitsAt(placement.Position, placement.Cluster.Map)).ToHashSet();
		foreach (var branch in branches)
		{
			int keep = branch.FindLastIndex(exits.Contains) + 1;
			for (int step = keep; step < branch.Count; step++) halls.Remove(branch[step]);
		}
	}
}