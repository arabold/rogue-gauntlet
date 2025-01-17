using Godot;

/// <summary>
/// Placement of a room in the map.
/// </summary>
public class RoomPlacement
{
	public Room Room;
	public Vector3I Position;

	public RoomPlacement(Room room, Vector3I position)
	{
		Room = room;
		Position = position;
	}
}
