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

	/// <summary>Logical floor role copied from a <see cref="RoomGroupMember"/> or layout slot (e.g., "entrance", "boss", "exit").</summary>
	public string SlotId { get; set; } = "";

	/// <summary>Topological guarantees carried from layout/group authoring (e.g., MustTraverse).</summary>
	public RoomObligations Obligations { get; set; } = RoomObligations.None;

	/// <summary>When true, excludes this region's doors from random floor lock/secret assignment.</summary>
	public bool ReserveAccess { get; set; } = false;

	/// <summary>Placed cluster index connecting all members of one <see cref="RoomCluster"/>; -1 if unassigned.</summary>
	public int ClusterId { get; set; } = -1;

	public RoomRegion(int id)
	{
		Id = id;
	}
}