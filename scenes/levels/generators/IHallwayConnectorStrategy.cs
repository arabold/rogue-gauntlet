using System;

public interface IHallwayConnectorStrategy
{
	// Connects the rooms in the base map with hallways
	public void ConnectRooms(MapData mapData, Random random);
}
