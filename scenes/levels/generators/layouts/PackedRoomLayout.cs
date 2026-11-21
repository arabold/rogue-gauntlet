using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Arranges rooms as a tightly packed cluster. The entrance anchors the cluster at the
/// map center; every following room is placed on a candidate position hugging the
/// already-placed rooms at the minimum legal spacing, scored for compactness (distance
/// to the cluster center) plus a bonus for lining doorways up face to face across the
/// gap. The exit is placed last, packed against the cluster but as far from the
/// entrance as possible; if the map filled up before the exit could be placed, standard
/// rooms are removed until it fits.
/// <br/>
/// Corridors are not routed here — tight placement simply leaves the corridor connector
/// short gaps to work with, which is what produces the compact look.
/// </summary>
[Tool]
[GlobalClass]
public partial class PackedRoomLayout : RoomLayoutStrategy
{
	/// <summary>
	/// Empty tiles kept between the bounding rectangles of neighboring rooms. 1 is the
	/// minimum the generator supports: rooms must never sit directly adjacent because
	/// every doorway opens onto a corridor tile and the fog-reveal cascade assumes at
	/// least one corridor tile between two rooms.
	/// </summary>
	[Export(PropertyHint.Range, "1,4")] public int RoomGap { get; set; } = 1;

	/// <summary>How many different rooms to try for a slot before skipping it.</summary>
	[Export] public int Retries { get; set; } = 3;

	/// <summary>
	/// Score bonus per pair of connectors that end up facing each other across the gap,
	/// measured in tiles: a candidate this many tiles farther from the cluster center
	/// still wins when it aligns one extra doorway pair.
	/// </summary>
	[Export] public float DoorwayAlignmentBonus { get; set; } = 3f;

	/// <summary>
	/// The placement is picked randomly among the candidates within this score distance
	/// of the best one, trading a little compactness for layout variety.
	/// </summary>
	[Export(PropertyHint.Range, "0,10")] public float ScoreTolerance { get; set; } = 1f;

	public override FloorPlan GenerateLayout(MapData map, FloorBuildContext context)
	{
		var plan = new FloorPlan();
		var rng = context.Rng;
		var pool = context.Pool;
		var progressionOptions = context.Profile?.CreateRoomOptions(pool.RealmId, isProgressionRoom: true);
		var roomOptions = context.Profile?.CreateRoomOptions(pool.RealmId);

		// The entrance anchors the cluster at the map center so it can grow in every direction.
		var entranceRoom = pool.CreateEntrance(rng, progressionOptions);
		entranceRoom.Rotate(rng.RandiRange(0, 3));
		var entrancePlacement = new Vector2I(
			(map.Width - entranceRoom.Map.Width) / 2,
			(map.Height - entranceRoom.Map.Height) / 2);
		if (!FitsWithinPlayableBounds(map, entranceRoom.Map, entrancePlacement)
			|| map.Intersects(entranceRoom.Map, entrancePlacement))
		{
			GD.PrintErr("PackedRoomLayout: map is too small for the entrance room.");
			entranceRoom.Free();
			return plan;
		}
		AddPlacement(map, plan, entranceRoom, entrancePlacement);

		var uses = new Dictionary<RoomDefinition, int>();
		var entranceCenter = PlacementCenter(plan.Placements[0]);
		for (int i = 0; i < context.MaxRooms; i++)
		{
			var clusterCenter = ComputeClusterCenter(plan.Placements);
			if (TryAddPackedRoom(map, plan,
				() => CreateBalancedRoom(context, uses, roomOptions),
				(placement, roomMap, connectors) => ScoreCompactPlacement(map, clusterCenter, placement, roomMap, connectors), rng))
			{
				RecordRoomUse(uses, plan.Placements[^1].Cluster);
			}
		}

		PlaceExit(map, plan, context, entranceCenter, progressionOptions);
		return plan;
	}

	/// <summary>
	/// Places the exit packed against the cluster, preferring the candidate farthest from
	/// the entrance. On very full maps there may be no space left; in that case standard
	/// (filler) rooms are removed, newest first, until the exit fits -- never the
	/// entrance -- because a level without an exit is a dead end for
	/// the run.
	/// </summary>
	private void PlaceExit(
		MapData map, FloorPlan plan, FloorBuildContext context, Vector2 entranceCenter,
		RoomAssemblyOptions progressionOptions)
	{
		while (true)
		{
			bool placed = TryAddPackedRoom(map, plan, () => CreateGoal(context, progressionOptions),
				(placement, roomMap, connectors) =>
					(RectCenter(placement, roomMap) - entranceCenter).Length()
					+ CountAlignedConnectors(map, placement, connectors, RoomGap) * DoorwayAlignmentBonus, context.Rng);
			if (placed)
			{
				return;
			}

			if (plan.Placements.Count == 1)
			{
				GD.PrintErr("PackedRoomLayout: could not place an exit room.");
				return;
			}

			// Free up space: drop the most recently placed standard (filler) room and
			// rebuild the map.
			var removed = plan.Placements[^1];
			plan.Placements.RemoveAt(plan.Placements.Count - 1);
			removed.Cluster.Free();
			GD.Print($"PackedRoomLayout: removed a room at {removed.Position} to make space for the exit.");
			RestampAll(map, plan);
		}
	}

	private bool TryAddPackedRoom(
		MapData map, FloorPlan plan, Func<RoomCluster> createRoom,
		Func<Vector2I, MapData, List<(Vector2I Tile, Vector2I Direction)>, float> score, RandomNumberGenerator rng)
	{
		for (int attempt = 0; attempt < Retries; attempt++)
		{
			var cluster = createRoom();
			if (TryFindBestPlacementAcrossRotations(
				map,
				cluster,
				roomMap => CollectCandidates(map, plan.Placements, roomMap),
				score,
				ScoreTolerance,
				RoomGap,
				rng,
				out var placement))
			{
				AddPlacement(map, plan, cluster, placement);
				return true;
			}

			// No valid position for this cluster shape at any rotation; a differently-sized
			// room may still fit.
			cluster.Free();
		}

		return false;
	}

	/// <summary>
	/// Enumerates placements that put the new room's bounding rectangle exactly
	/// <see cref="RoomGap"/> tiles away from an already-placed room, sliding along all
	/// four sides. Deduplicated but insertion-ordered, to keep generation deterministic.
	/// </summary>
	private List<Vector2I> CollectCandidates(MapData map, List<(RoomCluster Cluster, Vector2I Position)> rooms, MapData roomMap)
	{
		var seen = new HashSet<Vector2I>();
		var candidates = new List<Vector2I>();

		void Add(int x, int z)
		{
			var candidate = new Vector2I(x, z);
			if (!FitsWithinPlayableBounds(map, roomMap, candidate))
			{
				return;
			}
			if (seen.Add(candidate))
			{
				candidates.Add(candidate);
			}
		}

		foreach (var placed in rooms)
		{
			var anchor = placed.Position;
			var size = new Vector2I(placed.Cluster.Map.Width, placed.Cluster.Map.Height);

			int east = anchor.X + size.X + RoomGap;
			int west = anchor.X - roomMap.Width - RoomGap;
			int south = anchor.Y + size.Y + RoomGap;
			int north = anchor.Y - roomMap.Height - RoomGap;

			// Slide along the facing side; any amount of face-to-face overlap is a candidate.
			for (int z = anchor.Y - roomMap.Height + 1; z <= anchor.Y + size.Y - 1; z++)
			{
				Add(east, z);
				Add(west, z);
			}
			for (int x = anchor.X - roomMap.Width + 1; x <= anchor.X + size.X - 1; x++)
			{
				Add(x, north);
				Add(x, south);
			}
		}

		return candidates;
	}

	private static Vector2 ComputeClusterCenter(List<(RoomCluster Cluster, Vector2I Position)> rooms)
	{
		var center = Vector2.Zero;
		foreach (var placed in rooms)
		{
			center += PlacementCenter(placed);
		}

		return center / rooms.Count;
	}

	/// <summary>
	/// Compactness score: negative distance from the cluster center, plus a bonus for
	/// every connector pair that ends up directly facing another room's connector.
	/// </summary>
	private float ScoreCompactPlacement(
		MapData map, Vector2 clusterCenter, Vector2I placement, MapData roomMap,
		List<(Vector2I Tile, Vector2I Direction)> connectors)
	{
		return CountAlignedConnectors(map, placement, connectors, RoomGap) * DoorwayAlignmentBonus
			- (RectCenter(placement, roomMap) - clusterCenter).Length();
	}
}