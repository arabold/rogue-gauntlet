using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A floor plan built around halls: corridor tiles the layout lays itself. Each plan places its core (the entrance, the
/// goal and anything they gather around) and lays its halls, side halls branch off toward the map edge, and rooms line
/// both sides of every hall, each opening onto it. The corridor connector then joins any doorway that does not open
/// onto a hall.
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class HallRoomLayout : RoomLayoutStrategy
{
	/// <summary>Side halls branching off the core's halls, each lined with rooms of its own.</summary>
	[Export(PropertyHint.Range, "0,4")] public int Branches { get; set; } = 2;
	/// <summary>Most doorways a side room gets. With 1 every room opens only onto a hall; more let some rooms add a back corridor.</summary>
	[Export(PropertyHint.Range, "1,4")] public int MaxSideDoorways { get; set; } = 2;
	/// <summary>Weighted rooms that line the halls, in place of the realm's ordinary rooms; each still needs to suit the realm.</summary>
	[Export] public RoomDefinition[] SideRooms { get; set; } = [];
	/// <summary>Rooms that may fail to fit in a row before the layout stops adding rooms.</summary>
	[Export(PropertyHint.Range, "1,16")] public int Retries { get; set; } = 8;
	/// <summary>Spots within this score of the best one are picked at random, trading a little tightness for variety.</summary>
	[Export(PropertyHint.Range, "0,10")] public float ScoreTolerance { get; set; } = 1f;

	/// <summary>Score cost of a doorway that does not open onto a hall and needs a corridor of its own.</summary>
	private const float BackDoorCost = 6f;
	/// <summary>Shortest hall worth laying, main or side: a shorter one has no room for rooms along it.</summary>
	protected const int MinimumHallLength = 4;

	/// <summary>A point a side hall may grow from: a hall tile, the outward direction and the hall tile's walking order.</summary>
	protected readonly record struct BranchPoint(Vector2I From, Vector2I Direction, int Order);

	/// <summary>
	/// Places the plan's core rooms, the entrance and goal among them, and lays their halls into <paramref name="halls"/>
	/// (tile to walking order; rooms fill in that order). Returns where side halls may branch, best first, or null when the
	/// core does not fit, so the attempt is retried. A cluster that fails to attach is the caller's to free.
	/// </summary>
	protected abstract List<BranchPoint> LayCore(MapData map, FloorPlan plan, FloorBuildContext context, Dictionary<Vector2I, int> halls);

	public override FloorPlan GenerateLayout(MapData map, FloorBuildContext context)
	{
		var plan = new FloorPlan();
		bool keepPlan = false;
		try
		{
			var halls = new Dictionary<Vector2I, int>();
			var branchPoints = LayCore(map, plan, context, halls);
			if (branchPoints == null) return new FloorPlan();
			var branches = branchPoints.Take(Branches).Select(point => AddBranch(map, halls, point)).Where(branch => branch != null).ToList();
			AddSideRooms(map, plan, context, halls);
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

	/// <summary>Lines the halls with rooms until the room budget is spent or <see cref="Retries"/> rooms in a row do not fit.</summary>
	private void AddSideRooms(MapData map, FloorPlan plan, FloorBuildContext context, Dictionary<Vector2I, int> halls)
	{
		var rng = context.Rng;
		var uses = new Dictionary<RoomDefinition, int>();
		var options = CreateRoomOptions(context, maxDoorways: MaxSideDoorways);
		var candidates = (SideRooms.Length > 0 ? SideRooms : context.Pool.Rooms)
			.Where(def => def != null && def.SupportsRealm(context.Pool.RealmId) && (def is not RoomType type || type.MinDoorways <= MaxSideDoorways))
			.ToList();
		// An authoring error must surface, not read as a room that did not fit.
		if (SideRooms.SelectMany(def => def?.Validate() ?? []).ToArray() is { Length: > 0 } errors)
			throw new InvalidOperationException(string.Join("\n", errors));
		for (int placed = 0, failures = 0; candidates.Count > 0 && placed < context.MaxRooms && failures < Retries;)
		{
			RoomCluster room;
			try
			{
				room = context.Pool.CreateFrom(Weighted.PickBalanced(candidates, def => def.Weight, def => uses.GetValueOrDefault(def), rng), rng, options);
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
	}

	/// <summary>
	/// Places <paramref name="cluster"/> with at least one doorway opening onto a hall tile and none of its tiles on a hall,
	/// at the best <paramref name="score"/> of the earliest hall tile it opens onto and its doorways that open elsewhere.
	/// </summary>
	protected bool Attach(MapData map, FloorPlan plan, RoomCluster cluster, Dictionary<Vector2I, int> halls,
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
	/// Grows a side hall straight out from <paramref name="point"/>, stopping a tile short of the border or of a placed room.
	/// Null when it would be too short for rooms.
	/// </summary>
	private static List<Vector2I> AddBranch(MapData map, Dictionary<Vector2I, int> halls, BranchPoint point)
	{
		var branch = new List<Vector2I>();
		for (var tile = point.From + point.Direction; ; tile += point.Direction)
		{
			var next = tile + point.Direction;
			if (!map.IsWithinBounds(next.X, next.Y) || map.IsOnBoundary(next.X, next.Y) || halls.ContainsKey(tile)
				|| !map.IsEmpty(tile.X, tile.Y) || !map.IsEmpty(next.X, next.Y)) break;
			branch.Add(tile);
		}
		if (branch.Count < MinimumHallLength) return null;
		for (int step = 0; step < branch.Count; step++) halls[branch[step]] = point.Order + step + 1;
		return branch;
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