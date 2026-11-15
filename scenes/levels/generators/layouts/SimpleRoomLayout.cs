using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Arranges rooms in a simple layout with an entrance and exit room.
/// </summary>
[Tool]
[GlobalClass]
public partial class SimpleRoomLayout : RoomLayoutStrategy
{
	/// <summary>
	/// The maximum number of times to retry placing a room before giving up.
	/// Increasing this value may help to generate more complex maps at the
	/// expense of performance.
	/// </summary>
	[Export] public int Retries { get; set; } = 3;

	public override List<RoomPlacement> GenerateRooms(MapData map, AssembledRoomFactory factory, uint maxRooms, RandomNumberGenerator rng)
	{
		List<RoomPlacement> rooms = new();

		var entranceRoom = factory.CreateEntrance(rng);
		var entrancePlacement = TryPlaceRoom(map, entranceRoom.Map, 99, rng);
		if (entrancePlacement == null)
		{
			GD.PrintErr("SimpleRoomLayout: Failed to place the entrance room; map is too crowded.");
			entranceRoom.Free();
			return rooms;
		}
		AddPlacement(map, rooms, entranceRoom, entrancePlacement.Value);

		var exitRoom = factory.CreateExit(rng);
		var exitPlacement = TryPlaceRoom(map, exitRoom.Map, 99, rng);
		if (exitPlacement == null)
		{
			GD.PrintErr("SimpleRoomLayout: Failed to place the exit room; map is too crowded.");
			exitRoom.Free();
			return rooms;
		}
		AddPlacement(map, rooms, exitRoom, exitPlacement.Value);

		for (int i = 0; i < maxRooms; i++)
		{
			var retries = Retries;
			do
			{
				var room = factory.CreateStandardRoom(rng);
				var placement = TryPlaceRoom(map, room.Map, retries, rng);
				if (placement != null)
				{
					AddPlacement(map, rooms, room, placement.Value);
					break; // Room placed successfully
				}
				else
				{
					// Rooms never enter the tree before placement, so a misfit is freed at once.
					room.Free();
				}
			} while (retries-- > 0);
		}

		return rooms;
	}

	private Vector2I? TryPlaceRoom(MapData map, MapData roomMap, int retries, RandomNumberGenerator rng)
	{
		bool overlaps;
		Vector2I placement;
		do
		{
			// Place the room at a  position on the map
			int roomX = rng.RandiRange(2, map.Width - roomMap.Width - 1);
			int roomZ = rng.RandiRange(2, map.Height - roomMap.Height - 1);
			placement = new Vector2I(roomX, roomZ);

			// Check if the room placement overlaps with any existing rooms
			overlaps = map.Intersects(roomMap, placement);
		}
		while (overlaps && retries-- >= 0);

		if (!overlaps)
		{
			return placement;
		}
		return null;
	}
}