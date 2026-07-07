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
	/// door resolution and the fog-reveal cascade both assume at least one corridor
	/// tile between two rooms.
	/// </summary>
	[Export(PropertyHint.Range, "1,4")] public int RoomGap { get; set; } = 1;

	/// <summary>How many different room scenes to try for a slot before skipping it.</summary>
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

	public override List<RoomPlacement> GenerateRooms(MapData map, RoomFactory factory, uint maxRooms)
	{
		List<RoomPlacement> rooms = new();

		// The entrance anchors the cluster at the map center so it can grow in every direction.
		var entranceRoom = PrepareConnectableRoom(factory.CreateEntrance());
		if (entranceRoom == null)
		{
			return rooms;
		}

		var entrancePlacement = new Vector2I(
			(map.Width - entranceRoom.Map.Width) / 2,
			(map.Height - entranceRoom.Map.Height) / 2);
		if (!FitsWithinPlayableBounds(map, entranceRoom.Map, entrancePlacement)
			|| map.Intersects(entranceRoom.Map, entrancePlacement))
		{
			GD.PrintErr("PackedRoomLayout: map is too small for the entrance room.");
			entranceRoom.QueueFree();
			return rooms;
		}
		AddPlacement(map, rooms, entranceRoom, entrancePlacement);

		var entranceCenter = PlacementCenter(rooms[0]);
		// Standard rooms placed successfully in this pass, tracked separately from
		// `rooms` so the exit-placement rollback below can free space by dropping filler
		// content only -- never the entrance or a special room.
		var removableRooms = new List<RoomPlacement>();
		var countSpecialRooms = PickSpecialRoomCount(maxRooms);
		for (int i = 0; i < maxRooms; i++)
		{
			bool isSpecial = i < countSpecialRooms;
			int countBefore = rooms.Count;
			var clusterCenter = ComputeClusterCenter(rooms);
			TryAddPackedRoom(map, rooms,
				() => isSpecial ? factory.CreateSpecialRoom() : factory.CreateStandardRoom(),
				(placement, roomMap, connectors) => ScoreCompactPlacement(map, clusterCenter, placement, roomMap, connectors));
			if (!isSpecial && rooms.Count > countBefore)
			{
				removableRooms.Add(rooms[^1]);
			}
		}

		PlaceExit(map, rooms, factory, entranceCenter, removableRooms);
		return rooms;
	}

	/// <summary>
	/// Places the exit packed against the cluster, preferring the candidate farthest from
	/// the entrance. On very full maps there may be no space left; in that case standard
	/// (filler) rooms are removed, newest first, until the exit fits -- never the
	/// entrance or a special room -- because a level without an exit is a dead end for
	/// the run.
	/// </summary>
	private void PlaceExit(
		MapData map, List<RoomPlacement> rooms, RoomFactory factory, Vector2 entranceCenter,
		List<RoomPlacement> removableRooms)
	{
		while (true)
		{
			bool placed = TryAddPackedRoom(map, rooms, factory.CreateExit,
				(placement, roomMap, connectors) =>
					(RectCenter(placement, roomMap) - entranceCenter).Length()
					+ CountAlignedConnectors(map, placement, connectors) * DoorwayAlignmentBonus);
			if (placed)
			{
				return;
			}

			if (removableRooms.Count == 0)
			{
				GD.PrintErr("PackedRoomLayout: could not place an exit room.");
				return;
			}

			// Free up space: drop the most recently placed standard (filler) room and
			// rebuild the map.
			var removed = removableRooms[^1];
			removableRooms.RemoveAt(removableRooms.Count - 1);
			rooms.Remove(removed);
			removed.Room.QueueFree();
			GD.Print($"PackedRoomLayout: removed a room at {removed.Position} to make space for the exit.");
			RestampAll(map, rooms);
		}
	}

	private bool TryAddPackedRoom(
		MapData map, List<RoomPlacement> rooms, Func<Room> createRoom,
		Func<Vector2I, MapData, List<(Vector2I Tile, Vector2I Direction)>, float> score)
	{
		for (int attempt = 0; attempt < Retries; attempt++)
		{
			var room = PrepareConnectableRoom(createRoom());
			if (room == null)
			{
				continue;
			}

			if (TryFindBestPlacementAcrossRotations(map, rooms, room, score, out var placement))
			{
				AddPlacement(map, rooms, room, placement);
				return true;
			}

			// No valid position for this room shape at any rotation; a differently-sized
			// room may still fit.
			room.QueueFree();
		}

		return false;
	}

	/// <summary>
	/// Tries all 4 quarter-turn orientations of the same room instance -- not 4
	/// independently created rooms, which for a procedurally built room would each be a
	/// different random shape rather than the one being evaluated -- and keeps whichever
	/// (position, rotation) pair scores best. Rotating never hurts: "unrotated" is one
	/// of the 4 options considered, so this can only ever match or beat placing the
	/// room without rotation.
	/// </summary>
	private bool TryFindBestPlacementAcrossRotations(
		MapData map, List<RoomPlacement> rooms, Room room,
		Func<Vector2I, MapData, List<(Vector2I Tile, Vector2I Direction)>, float> score,
		out Vector2I bestPlacement)
	{
		bestPlacement = default;
		int bestRotationSteps = 0;
		float bestScore = float.NegativeInfinity;
		bool found = false;

		for (int steps = 0; steps < 4; steps++)
		{
			// A rotated orientation is re-baked from scratch (Room.Rotate), and content
			// whose walls/props extend past its own floor bounds can occasionally fail
			// doorway-marker validation at some angles even though the unrotated room
			// validated fine -- skip any orientation that came out unreachable rather
			// than ever placing an unconnectable room. Logged so a room that's
			// unusable at most rotations (a content authoring issue, not a placement
			// one) is discoverable instead of just being picked less often.
			if (!IsConnectable(room))
			{
				GD.PrintErr($"PackedRoomLayout: {room.Name} has no connector at {steps * 90} degrees of rotation, skipping this orientation.");
			}
			else if (TryFindBestPlacement(map, rooms, room.Map, score, out var placement, out float placementScore)
				&& placementScore > bestScore)
			{
				bestScore = placementScore;
				bestPlacement = placement;
				bestRotationSteps = steps;
				found = true;
			}

			// Always rotate, even on the last iteration: 4 quarter turns bring the room
			// back to its original orientation, so it ends up exactly where the winning
			// rotation below expects to rotate it from.
			room.Rotate(1);
		}

		if (found && bestRotationSteps != 0)
		{
			room.Rotate(bestRotationSteps);

			// BakeTileMap is a pure function of the room's current geometry, so
			// re-baking the exact same rotated geometry a second time cannot legally
			// produce a different result -- but this guards the invariant explicitly
			// rather than silently trusting it, since placing an unconnectable room
			// would be a much worse failure than the placement attempt just failing.
			if (!IsConnectable(room))
			{
				GD.PrintErr($"PackedRoomLayout: {room.Name} lost its connector re-baking the winning rotation; treating this attempt as failed.");
				return false;
			}
		}

		return found;
	}

	private bool TryFindBestPlacement(
		MapData map, List<RoomPlacement> rooms, MapData roomMap,
		Func<Vector2I, MapData, List<(Vector2I Tile, Vector2I Direction)>, float> score,
		out Vector2I placement, out float placementScore)
	{
		var connectors = GetRoomConnectors(roomMap);
		var scored = new List<(Vector2I Position, float Score)>();
		foreach (var candidate in CollectCandidates(map, rooms, roomMap))
		{
			if (map.Intersects(roomMap, candidate))
			{
				continue;
			}

			scored.Add((candidate, score(candidate, roomMap, connectors)));
		}

		if (scored.Count == 0)
		{
			placement = default;
			placementScore = float.NegativeInfinity;
			return false;
		}

		// Pick randomly among the near-best candidates for variety. The candidate list is
		// insertion-ordered (not hash-ordered), so the same seed yields the same layout.
		float best = scored.Max(c => c.Score);
		var top = scored.Where(c => c.Score >= best - Mathf.Max(ScoreTolerance, 0f)).ToList();
		placement = top[GD.RandRange(0, top.Count - 1)].Position;
		placementScore = best;
		return true;
	}

	/// <summary>
	/// Enumerates placements that put the new room's bounding rectangle exactly
	/// <see cref="RoomGap"/> tiles away from an already-placed room, sliding along all
	/// four sides. Deduplicated but insertion-ordered, to keep generation deterministic.
	/// </summary>
	private List<Vector2I> CollectCandidates(MapData map, List<RoomPlacement> rooms, MapData roomMap)
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
			var size = new Vector2I(placed.Room.Map.Width, placed.Room.Map.Height);

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

	/// <summary>
	/// True when the room's footprint stays strictly inside the border wall ring.
	/// Intersects would reject border contact anyway, but out-of-bounds stamping must
	/// never be attempted, and Intersects alone cannot catch that on an empty map (there
	/// is nothing yet to collide with).
	/// </summary>
	private static bool FitsWithinPlayableBounds(MapData map, MapData roomMap, Vector2I placement)
	{
		return placement.X >= 1 && placement.Y >= 1
			&& placement.X + roomMap.Width <= map.Width - 1
			&& placement.Y + roomMap.Height <= map.Height - 1;
	}

	private static Vector2 ComputeClusterCenter(List<RoomPlacement> rooms)
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
		return CountAlignedConnectors(map, placement, connectors) * DoorwayAlignmentBonus
			- (RectCenter(placement, roomMap) - clusterCenter).Length();
	}

	/// <summary>
	/// Counts the new room's connectors that would directly face a placed room's
	/// connector across empty gap tiles — those become doorway-to-doorway corridors of
	/// minimal length, the hallmark of a tightly packed layout.
	/// </summary>
	private int CountAlignedConnectors(
		MapData map, Vector2I placement, List<(Vector2I Tile, Vector2I Direction)> connectors)
	{
		int aligned = 0;
		foreach (var (tile, direction) in connectors)
		{
			var probe = placement + tile;
			for (int step = 0; step <= RoomGap; step++)
			{
				probe += direction;
				if (!map.IsWithinBounds(probe.X, probe.Y))
				{
					break;
				}
				if (map.IsEmpty(probe.X, probe.Y))
				{
					continue;
				}
				if (map.IsConnector(probe.X, probe.Y)
					&& map.GetConnectorDirections(probe.X, probe.Y).Contains(-direction))
				{
					aligned++;
				}
				break;
			}
		}

		return aligned;
	}

	private static List<(Vector2I Tile, Vector2I Direction)> GetRoomConnectors(MapData roomMap)
	{
		var connectors = new List<(Vector2I, Vector2I)>();
		for (int x = 0; x < roomMap.Width; x++)
		{
			for (int z = 0; z < roomMap.Height; z++)
			{
				if (!roomMap.IsConnector(x, z))
				{
					continue;
				}
				foreach (var direction in roomMap.GetConnectorDirections(x, z))
				{
					connectors.Add((new Vector2I(x, z), direction));
				}
			}
		}

		return connectors;
	}

	private static void AddPlacement(MapData map, List<RoomPlacement> rooms, Room room, Vector2I placement)
	{
		rooms.Add(new RoomPlacement(room, placement));
		StampRoom(map, room.Map, placement);
	}

	/// <summary>
	/// Resets the map to its initial state (empty interior, wall border) and re-stamps
	/// the remaining rooms. Used when a placed room is rolled back for the exit.
	/// </summary>
	private static void RestampAll(MapData map, List<RoomPlacement> rooms)
	{
		map.ResetToBorderedEmpty();
		foreach (var placed in rooms)
		{
			StampRoom(map, placed.Room.Map, placed.Position);
		}
	}

	private static Vector2 PlacementCenter(RoomPlacement placement)
	{
		return RectCenter(placement.Position, placement.Room.Map);
	}

	private static Vector2 RectCenter(Vector2I placement, MapData roomMap)
	{
		return new Vector2(placement.X + roomMap.Width / 2f, placement.Y + roomMap.Height / 2f);
	}
}
