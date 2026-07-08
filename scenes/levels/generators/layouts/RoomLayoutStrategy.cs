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
	public abstract List<RoomPlacement> GenerateRooms(MapData map, RoomFactory factory, uint maxRooms);
	public virtual void Reset() { }

	/// <summary>
	/// Bakes a factory-created room's tile map. Returns null (freeing the instance)
	/// when the room is missing or has no usable connector and could therefore never
	/// be reached by a corridor.
	/// </summary>
	protected static Room PrepareConnectableRoom(Room room)
	{
		if (room == null)
		{
			return null;
		}

		GD.Print($"Preparing room {room.Name}...");
		room.BakeTileMap();
		if (!IsConnectable(room))
		{
			room.QueueFree();
			return null;
		}

		return room;
	}

	/// <summary>
	/// True when the room's currently-baked Map has at least one usable connector.
	/// Exposed so a subclass can re-check this after mutating an already-prepared room
	/// (e.g. PackedRoomLayout re-baking a rotated orientation) instead of only at the
	/// initial bake.
	/// </summary>
	protected static bool IsConnectable(Room room)
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
					map.SetConnector(mapX, mapZ, roomMap.GetConnectorDirections(x, y), roomMap.IsDoorway(x, y));
				}
				else if (!roomMap.IsEmpty(x, y))
				{
					map.SetTile(mapX, mapZ, roomMap.Tiles[x, y]);
				}
			}
		}
	}

	/// <summary>Rolls how many of a level's rooms should be special rooms rather than standard ones.</summary>
	protected static int PickSpecialRoomCount(uint maxRooms)
	{
		return GD.RandRange(1, 1 + (int)Mathf.Floor(maxRooms / 3));
	}
}
