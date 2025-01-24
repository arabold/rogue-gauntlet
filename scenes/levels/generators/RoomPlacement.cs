using Godot;

/// <summary>
/// Placement of a room in the map.
/// </summary>
public class RoomPlacement
{
	public Room Room;
	public Vector2I Position;

	public RoomPlacement(Room room, Vector2I position)
	{
		Room = room;
		Position = position;
	}
}
