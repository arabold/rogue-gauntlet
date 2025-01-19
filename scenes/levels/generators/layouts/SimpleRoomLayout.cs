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
	public override List<RoomPlacement> GenerateRooms(Random random, MapData map, RoomFactoryStrategy factory, int maxRooms, int retries)
	{
		List<RoomPlacement> rooms = new List<RoomPlacement>();

		// Place entrance and exit rooms
		var entranceScene = factory.CreateEntrance(random);
		var entranceRoom = entranceScene.Instantiate<Room>();
		entranceRoom.Initialize();
		var entrancePlacement = TryPlaceRoom(random, map, entranceRoom.Map, 99);
		rooms.Add(new RoomPlacement(entranceRoom, entrancePlacement.Value));
		PlaceRoom(map, entranceRoom.Map, entrancePlacement.Value);

		var exitScene = factory.CreateExit(random);
		var exitRoom = exitScene.Instantiate<Room>();
		exitRoom.Initialize();
		var exitPlacement = TryPlaceRoom(random, map, exitRoom.Map, 99);
		rooms.Add(new RoomPlacement(exitRoom, exitPlacement.Value));
		PlaceRoom(map, exitRoom.Map, exitPlacement.Value);

		for (int i = 0; i < maxRooms; i++)
		{
			var scenePath = factory.CreateStandardRoom(random);
			var scene = scenePath;
			var room = scene.Instantiate<Room>();
			room.Initialize();

			var placement = TryPlaceRoom(random, map, room.Map, retries);
			if (placement != null)
			{
				// Add the room to the map
				rooms.Add(new RoomPlacement(room, placement.Value));
				PlaceRoom(map, room.Map, placement.Value);
			}
			else
			{
				// Unload the room scene
				room.QueueFree();
			}
		}

		return rooms;
	}

	private void PlaceRoom(MapData map, MapData roomMap, Vector3I placement)
	{
		GD.Print($"Placing room at {placement}");
		for (var x = 0; x < roomMap.Width; x++)
		{
			for (var z = 0; z < roomMap.Height; z++)
			{
				var mapX = placement.X + x;
				var mapZ = placement.Z + z;

				map.SetTile(mapX, mapZ, roomMap.Tiles[x, z]);
			}
		}
	}

	private Vector3I? TryPlaceRoom(Random random, MapData map, MapData roomMap, int retries)
	{
		bool overlaps;
		Vector3I placement;
		do
		{
			// Place the room at a random position on the map
			int roomX = random.Next(2, map.Width - roomMap.Width - 2);
			int roomZ = random.Next(2, map.Height - roomMap.Height - 2);
			placement = new Vector3I(roomX, 0, roomZ);

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
