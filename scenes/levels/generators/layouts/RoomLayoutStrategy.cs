using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

[Tool]
[GlobalClass]
public abstract partial class RoomLayoutStrategy : Resource
{
	/// <summary>
	/// Populates the map with room data (e.g., which tiles are rooms)
	/// </summary>
	public abstract List<RoomPlacement> GenerateRooms(MapData map, RoomFactory factory, uint maxRooms, RandomNumberGenerator rng);
	public virtual void Reset() { }

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
				else if (!roomMap.IsEmpty(x, y))
				{
					map.SetTile(mapX, mapZ, roomMap.Tiles[x, y]);
				}
			}
		}
	}

	/// <summary>Rolls how many of a level's rooms should be special rooms rather than standard ones.</summary>
	protected static int PickSpecialRoomCount(uint maxRooms, RandomNumberGenerator rng)
	{
		return rng.RandiRange(1, 1 + (int)Mathf.Floor(maxRooms / 3));
	}
}