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

	/// <summary>Resolved room purpose, independent of the dungeon realm.</summary>
	public RoomPurpose Purpose { get; set; } = RoomPurpose.Chamber;

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

	/// <summary>Assembled with its content by a <see cref="RoomType"/>; the generic furnishing pass skips it.</summary>
	public bool IsFurnished { get; set; }

	/// <summary>Per-room furnishing policy supplied by a coded recipe; absent for generic/authored rooms.</summary>
	public RoomArrangementPool InteriorPool { get; set; }
	public RoomDressingProfile Dressing { get; set; }
	public Vector2I LayoutAxis { get; set; }

	/// <summary>Required furnishing anchors resolved after room rotation, in master-map coordinates.</summary>
	public List<RoomArrangementPlacement> FurnishingSlots { get; } = [];

	public RoomRegion(int id)
	{
		Id = id;
	}
}