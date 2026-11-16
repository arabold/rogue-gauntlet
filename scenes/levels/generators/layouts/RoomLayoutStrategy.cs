using System.Collections.Generic;
using Godot;

[Tool]
[GlobalClass]
public abstract partial class RoomLayoutStrategy : Resource
{
	/// <summary>
	/// Populates the map with room data (e.g., which tiles are rooms)
	/// </summary>
	public abstract List<RoomPlacement> GenerateRooms(MapData map, AssembledRoomFactory factory, uint maxRooms, RandomNumberGenerator rng);

	/// <summary>Keeps <paramref name="room"/> at <paramref name="placement"/> and stamps its tiles into the map.</summary>
	protected static void AddPlacement(MapData map, List<RoomPlacement> rooms, Room room, Vector2I placement)
	{
		rooms.Add(new RoomPlacement(room, placement));
		StampRoom(map, room.Map, placement);
	}

	/// <summary>Creates a standard room biased against repeating types already recorded in <paramref name="uses"/>.</summary>
	protected static Room CreateBalancedRoom(AssembledRoomFactory factory, RandomNumberGenerator rng, Dictionary<RoomType, int> uses) =>
		factory.CreateStandardRoom(rng, type => uses.GetValueOrDefault(type));

	/// <summary>Records a placed room's type in <paramref name="uses"/> for balanced room selection.</summary>
	protected static void RecordRoomUse(Dictionary<RoomType, int> uses, Room room)
	{
		if (room?.Type != null)
		{
			uses[room.Type] = uses.GetValueOrDefault(room.Type) + 1;
		}
	}

	/// <summary>
	/// Stamps a room's local tile map into the master map at the given placement. Local
	/// Empty cells (e.g. an L-shaped/notched footprint's cut corner) are skipped rather
	/// than written as Empty: MapData.Intersects only checks the new room's non-empty
	/// cells for overlap, so an Empty notch can legally coincide with a different,
	/// already-placed room's tiles -- writing Empty there would silently erase them.
	/// Leaving the cell untouched means "this room makes no claim here", not "this space
	/// is void".
	/// </summary>
	protected static void StampRoom(MapData map, MapData roomMap, Vector2I placement)
	{
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
				else if (!roomMap.IsEmpty(x, y))
				{
					map.SetTile(mapX, mapZ, roomMap.Tiles[x, y]);
				}
			}
		}
	}

}