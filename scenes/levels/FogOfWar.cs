using Godot;

/// <summary>
/// Drives the fog reveal: carves the occluder caps away when the player enters a
/// room (and the door-free rooms/corridors it connects to), and again when a door
/// is opened so the area beyond it comes into view.
/// </summary>
public partial class FogOfWar : Node
{
	private MapGenerator _mapGenerator;

	/// <summary>Binds the reveal target. Saved exploration is restored by <see cref="Level"/> together with the
	/// other floor deltas, so a rejected save is handled in one place.</summary>
	public void Initialize(MapGenerator mapGenerator)
	{
		_mapGenerator = mapGenerator;
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
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.DoorClosed += OnDoorClosed,
			bus => bus.DoorClosed -= OnDoorClosed);
	}

	private void OnRoomEntered(int roomId)
	{
		_mapGenerator?.RevealRoom(roomId);
	}

	private void OnDoorOpened(Node3D door)
	{
		_mapGenerator?.OpenDoorAt(door.GlobalPosition);
	}

	private void OnDoorClosed(Node3D door)
	{
		_mapGenerator?.CloseDoorAt(door.GlobalPosition);
	}
}