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
	public abstract List<RoomPlacement> GenerateRooms(Random random, MapData map, RoomFactoryStrategy factory, int maxRooms, int retries);
}
