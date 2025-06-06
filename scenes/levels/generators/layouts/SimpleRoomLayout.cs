using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Arranges rooms in a simple layout with an entrance and exit room.
/// </summary>
[Tool]
[GlobalClass]
public partial class SimpleRoomLayout : RoomLayoutStrategy
{   /// <summary>
	/// The maximum number of times to retry placing a room before giving up.
	/// Increasing this value may help to generate more complex maps at the
	/// expense of performance.
	/// </summary>
	[Export] public int Retries = 3;

	public override List<RoomPlacement> GenerateRooms(MapData map, RoomFactory factory, uint maxRooms)
	{
		List<RoomPlacement> rooms = new();

		// Place entrance and exit rooms
		var entranceScene = factory.CreateEntrance();
		var entranceRoom = entranceScene.Instantiate<Room>();
		GD.Print($"Instantiated room {entranceRoom}...");
		entranceRoom.BakeTileMap();
		var entrancePlacement = TryPlaceRoom(map, entranceRoom.Map, 99);
		rooms.Add(new RoomPlacement(entranceRoom, entrancePlacement.Value));
		PlaceRoom(map, entranceRoom.Map, entrancePlacement.Value);

		var exitScene = factory.CreateExit();
		var exitRoom = exitScene.Instantiate<Room>();
		GD.Print($"Instantiated room {exitRoom}...");
		exitRoom.BakeTileMap();
		var exitPlacement = TryPlaceRoom(map, exitRoom.Map, 99);
		rooms.Add(new RoomPlacement(exitRoom, exitPlacement.Value));
		PlaceRoom(map, exitRoom.Map, exitPlacement.Value);

		var countSpecialRooms = GD.RandRange(1, 1 + (int)Mathf.Floor(maxRooms / 3));
		for (int i = 0; i < maxRooms; i++)
		{
			var retries = Retries;
			do
			{
				var scenePath = (i < countSpecialRooms)
					? factory.CreateSpecialRoom()
					: factory.CreateStandardRoom();
				var scene = scenePath;
				var room = scene.Instantiate<Room>();
				GD.Print($"Instantiated room {scenePath}...");
				room.BakeTileMap();

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

	private void PlaceRoom(MapData map, MapData roomMap, Vector2I placement)
	{
		GD.Print($"Placing room at {placement}");
		for (var x = 0; x < roomMap.Width; x++)
		{
			for (var y = 0; y < roomMap.Height; y++)
			{
				var mapX = placement.X + x;
				var mapZ = placement.Y + y;

				map.SetTile(mapX, mapZ, roomMap.Tiles[x, y]);
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
