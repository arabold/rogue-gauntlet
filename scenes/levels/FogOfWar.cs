using Godot;

/// <summary>
/// Drives the fog reveal: carves the occluder caps away when the player enters a
/// room (and the door-free rooms/corridors it connects to), and again when a door
/// is opened so the area beyond it comes into view.
/// </summary>
public partial class FogOfWar : Node
{
	private MapGenerator _mapGenerator;

	public void Initialize(MapGenerator mapGenerator)
	{
		_mapGenerator = mapGenerator;

		// Re-apply the reveal the player had already uncovered on this depth.
		var session = GameSession.Instance;
		if (session != null)
		{
			_mapGenerator.RestoreReveal(session.GetRevealedRoomIds(), session.GetOpenedDoors());
		}
	}

	public override void _Ready()
	{
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.RoomEntered += OnRoomEntered,
			bus => bus.RoomEntered -= OnRoomEntered);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.DoorOpened += OnDoorOpened,
			bus => bus.DoorOpened -= OnDoorOpened);
	}

	private void OnRoomEntered(int roomId)
	{
		_mapGenerator?.RevealRoom(roomId);
		GameSession.Instance?.MarkRoomRevealed(roomId);
	}

	private void OnDoorOpened(Node3D door)
	{
		Vector2I? connector = _mapGenerator?.OpenDoorAt(door.GlobalPosition);
		if (connector.HasValue)
		{
			GameSession.Instance?.MarkDoorOpened(connector.Value);
		}
	}
}
