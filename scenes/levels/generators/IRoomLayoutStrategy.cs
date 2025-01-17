using System;
using System.Collections.Generic;

public interface IRoomLayoutStrategy
{
	/// <summary>
	/// Populates the map with room data (e.g., which tiles are rooms)
	/// </summary>
	public List<RoomPlacement> GenerateRooms(MapData mapData, IRoomFactory factory, int maxRooms, int retries, Random random);
}
