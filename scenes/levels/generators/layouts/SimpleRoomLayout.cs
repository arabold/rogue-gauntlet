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
	[Export] public int Retries = 3;

	public override List<RoomPlacement> GenerateRooms(MapData map, RoomFactory factory, uint maxRooms)
	{
		List<RoomPlacement> rooms = new();

		// Factories load content lazily. If a scene path is broken, skip generation
		// gracefully so content mistakes show as editor/runtime errors instead of crashes.
		var entranceRoom = PrepareConnectableRoom(factory.CreateEntrance());
		if (entranceRoom == null)
		{
			return rooms;
		}
		var entrancePlacement = TryPlaceRoom(map, entranceRoom.Map, 99);
		if (entrancePlacement == null)
		{
			GD.PrintErr("SimpleRoomLayout: Failed to place the entrance room; map is too crowded.");
			entranceRoom.QueueFree();
			return rooms;
		}
		rooms.Add(new RoomPlacement(entranceRoom, entrancePlacement.Value));
		StampRoom(map, entranceRoom.Map, entrancePlacement.Value);

		var exitRoom = PrepareConnectableRoom(factory.CreateExit());
		if (exitRoom == null)
		{
			return rooms;
		}
		var exitPlacement = TryPlaceRoom(map, exitRoom.Map, 99);
		if (exitPlacement == null)
		{
			GD.PrintErr("SimpleRoomLayout: Failed to place the exit room; map is too crowded.");
			exitRoom.QueueFree();
			return rooms;
		}
		rooms.Add(new RoomPlacement(exitRoom, exitPlacement.Value));
		StampRoom(map, exitRoom.Map, exitPlacement.Value);

		var countSpecialRooms = PickSpecialRoomCount(maxRooms);
		for (int i = 0; i < maxRooms; i++)
		{
			var retries = Retries;
			do
			{
				var room = PrepareConnectableRoom((i < countSpecialRooms)
					? factory.CreateSpecialRoom()
					: factory.CreateStandardRoom());
				if (room == null)
				{
					continue;
				}

				var placement = TryPlaceRoom(map, room.Map, retries);
				if (placement != null)
				{
					// Add the room to the map
					rooms.Add(new RoomPlacement(room, placement.Value));
					StampRoom(map, room.Map, placement.Value);
					break; // Room placed successfully
				}
				else
				{
					// Unload the room scene
					room.QueueFree();
				}
			} while (retries-- > 0);
		}

		return rooms;
	}

	private Vector2I? TryPlaceRoom(MapData map, MapData roomMap, int retries)
	{
		bool overlaps;
		Vector2I placement;
		do
		{
			// Place the room at a  position on the map
			int roomX = GD.RandRange(2, map.Width - roomMap.Width - 1);
			int roomZ = GD.RandRange(2, map.Height - roomMap.Height - 1);
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
