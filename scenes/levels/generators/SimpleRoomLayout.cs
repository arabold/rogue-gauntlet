
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

/// <summary>
/// Arranges rooms in a simple layout with an entrance and exit room.
/// </summary>
public partial class SimpleRoomLayout : IRoomLayoutStrategy
{
	public List<RoomPlacement> GenerateRooms(MapData map, IRoomFactory factory, int maxRooms, int retries, Random random)
	{
		List<RoomPlacement> rooms = new List<RoomPlacement>();

		// Place entrance and exit rooms
		var entranceScene = factory.CreateLevelEntrance(random);
		var entranceRoom = entranceScene.Instantiate<Room>();
		entranceRoom.Initialize();
		var entrancePlacement = TryPlaceRoom(map, entranceRoom, int.MaxValue, random);

		var exitScene = factory.CreateLevelExit(random);
		var exitRoom = exitScene.Instantiate<Room>();
		exitRoom.Initialize();
		var exitPlacement = TryPlaceRoom(map, exitRoom, int.MaxValue, random);

		rooms.Add(new RoomPlacement(entranceRoom, entrancePlacement.Value));
		PlaceRoom(map, entranceRoom, entrancePlacement.Value);
		rooms.Add(new RoomPlacement(exitRoom, exitPlacement.Value));
		PlaceRoom(map, exitRoom, exitPlacement.Value);

		for (int i = 0; i < maxRooms; i++)
		{
			var scenePath = factory.CrateStandardRoom(random);
			var scene = scenePath;
			var room = scene.Instantiate<Room>();
			room.Initialize();

			var placement = TryPlaceRoom(map, room, retries, random);
			if (placement != null)
			{
				// Add the room to the map
				rooms.Add(new RoomPlacement(room, placement.Value));
				PlaceRoom(map, room, placement.Value);
			}
			else
			{
				// Unload the room scene
				room.QueueFree();
			}
		}

		return rooms;
	}

	private void PlaceRoom(MapData map, Room room, Vector3I placement)
	{
		foreach (var tile in room.BaseMap.GetUsedCells())
		{
			var tileIndex = room.BaseMap.GetCellItem(tile);
			var baseX = placement.X + tile.X;
			var baseZ = placement.Z + tile.Z;
			switch (tileIndex)
			{
				case 0:
					map.SetTile(baseX, baseZ, MapTile.Room);
					break;
				case 1:
					map.SetTile(baseX, baseZ, MapTile.Hallway);
					break;
				case 2:
					map.SetTile(baseX, baseZ, MapTile.Wall);
					break;
			}
		}
	}

	private Vector3I? TryPlaceRoom(MapData map, Room room, int retries, Random random)
	{
		bool overlaps;
		Vector3I placement;
		do
		{
			// Place the room at a random position on the map
			int roomX = random.Next(2 - room.Bounds.Position.X, map.Width - room.Bounds.Size.X - 2);
			int roomZ = random.Next(2 - room.Bounds.Position.Y, map.Height - room.Bounds.Size.Y - 2);
			placement = new Vector3I(roomX, 0, roomZ);

			// Check if the room placement overlaps with any existing rooms
			overlaps = IsRoomOverlapping(map, room, placement);
		}
		while (overlaps && retries-- >= 0);

		if (!overlaps)
		{
			GD.Print($"Placing room at {placement}");
			return placement;
		}
		else
		{
			// We didn't place the room, so free the instance
			return null;
		}
	}

	public bool IsRoomOverlapping(MapData map, Room room, Vector3I placement)
	{
		bool overlaps = false;
		foreach (var cell in room.BaseMap.GetUsedCells())
		{
			var baseX = placement.X + cell.X;
			var baseZ = placement.Z + cell.Z;
			// Check all nine tiles around the room
			var adjacentTiles = new[]
			{
				new Vector2I(baseX - 1, baseZ - 1),
				new Vector2I(baseX, baseZ - 1),
				new Vector2I(baseX + 1, baseZ - 1),
				new Vector2I(baseX - 1, baseZ),
				new Vector2I(baseX, baseZ),
				new Vector2I(baseX + 1, baseZ),
				new Vector2I(baseX - 1, baseZ + 1),
				new Vector2I(baseX, baseZ + 1),
				new Vector2I(baseX + 1, baseZ + 1),
			};
			if (adjacentTiles.Any(tile => map.IsWithinBounds(tile.X, tile.Y) && !map.IsEmpty(tile.X, tile.Y)))
			{
				GD.Print($"Room overlaps with existing room at ({baseX}, 0, {baseZ})");
				overlaps = true;
				break;
			}
		}
		return overlaps;
	}

}
