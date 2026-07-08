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

	/// <summary>
	/// True when the room was built by <see cref="ProceduralRoomBuilder"/> rather than
	/// loaded from an authored scene. Generation-time loot (chests/traps/loose items)
	/// only considers procedural rooms -- an authored room brings its own hand-placed
	/// content and layout, which the generic candidate scan knows nothing about.
	/// </summary>
	public bool IsProcedural { get; set; }

	public RoomRegion(int id)
	{
		Id = id;
	}
}
