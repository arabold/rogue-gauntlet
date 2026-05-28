using System.Collections.Generic;
using Godot;

/// <summary>
/// Runtime footprint of a placed room in master map tile coordinates. Built
/// during generation so systems can answer "which room owns this tile?" and
/// reveal a room's exact shape (including non-rectangular rooms) plus the
/// corridors leaving it.
/// </summary>
public class RoomRegion
{
	public int Id { get; }

	/// <summary>All tiles that reveal with the room: floor, connectors and chasm pits.</summary>
	public List<Vector2I> Tiles { get; } = new();

	/// <summary>Connector tiles where corridors leave the room.</summary>
	public List<Vector2I> ConnectorTiles { get; } = new();

	public RoomRegion(int id)
	{
		Id = id;
	}
}
