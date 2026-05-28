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

		// Factories return lazily-loaded scenes. If a path is broken, skip generation
		// gracefully so content mistakes show as editor/runtime errors instead of crashes.
		var entranceScene = factory.CreateEntrance();
		if (entranceScene == null)
		{
			return rooms;
		}

		var entranceRoom = entranceScene.Instantiate<Room>();
		GD.Print($"Instantiated room {entranceRoom}...");
		entranceRoom.BakeTileMap();
		if (!ValidateConnectableRoom(entranceRoom))
		{
			entranceRoom.QueueFree();
			return rooms;
		}
		var entrancePlacement = TryPlaceRoom(map, entranceRoom.Map, 99);
		rooms.Add(new RoomPlacement(entranceRoom, entrancePlacement.Value));
		PlaceRoom(map, entranceRoom.Map, entrancePlacement.Value);

		var exitScene = factory.CreateExit();
		if (exitScene == null)
		{
			return rooms;
		}

		var exitRoom = exitScene.Instantiate<Room>();
		GD.Print($"Instantiated room {exitRoom}...");
		exitRoom.BakeTileMap();
		if (!ValidateConnectableRoom(exitRoom))
		{
			exitRoom.QueueFree();
			return rooms;
		}
		var exitPlacement = TryPlaceRoom(map, exitRoom.Map, 99);
		rooms.Add(new RoomPlacement(exitRoom, exitPlacement.Value));
		PlaceRoom(map, exitRoom.Map, exitPlacement.Value);

		var countSpecialRooms = GD.RandRange(1, 1 + (int)Mathf.Floor(maxRooms / 3));
		for (int i = 0; i < maxRooms; i++)
		{
			var retries = Retries;
			do
			{
				var roomScene = (i < countSpecialRooms)
					? factory.CreateSpecialRoom()
					: factory.CreateStandardRoom();
				if (roomScene == null)
				{
					continue;
				}

				var room = roomScene.Instantiate<Room>();
				GD.Print($"Instantiated room {roomScene}...");
				room.BakeTileMap();
				if (!ValidateConnectableRoom(room))
				{
					room.QueueFree();
					continue;
				}

				var placement = TryPlaceRoom(map, room.Map, retries);
				if (placement != null)
				{
					// Add the room to the map
					rooms.Add(new RoomPlacement(room, placement.Value));
					PlaceRoom(map, room.Map, placement.Value);
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

	private bool ValidateConnectableRoom(Room room)
	{
		if (room.Map == null)
		{
			GD.PrintErr($"Skipping room {room.Name}: no generated map data.");
			return false;
		}

		for (var x = 0; x < room.Map.Width; x++)
		{
			for (var z = 0; z < room.Map.Height; z++)
			{
				if (room.Map.IsConnector(x, z) && room.Map.GetConnectorDirections(x, z).Count > 0)
				{
					return true;
				}
			}
		}

		GD.PrintErr($"Skipping room {room.Name}: no connector tiles. Add at least one wall-free edge tile to make it reachable.");
		return false;
	}

	private void PlaceRoom(MapData map, MapData roomMap, Vector2I placement)
	{
		GD.Print($"Placing room at {placement}");
		for (var x = 0; x < roomMap.Width; x++)
		{
			for (var y = 0; y < roomMap.Height; y++)
			{
				var mapX = placement.X + x;
				var mapZ = placement.Y + y;

				if (roomMap.IsConnector(x, y))
				{
					map.SetConnector(mapX, mapZ, roomMap.GetConnectorDirections(x, y));
				}
				else
				{
					map.SetTile(mapX, mapZ, roomMap.Tiles[x, y]);
				}
			}
		}
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
