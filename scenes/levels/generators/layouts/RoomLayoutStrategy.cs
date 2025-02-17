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
}
