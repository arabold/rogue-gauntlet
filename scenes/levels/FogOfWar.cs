using System.Linq;
using Godot;

/// <summary>Updates persistent discovery from the player's position. Room entry is independent of exploration.</summary>
public partial class FogOfWar : Node
{
	private MapGenerator _mapGenerator;
	private Player _player;
	private double _untilUpdate;
	private Vector3 _lastPosition = Vector3.Inf;
	private float _lastRadius;
	private bool _refresh = true;

	/// <summary>Saved exploration is restored by Level before this floor-local driver starts.</summary>
	public void Initialize(MapGenerator mapGenerator) => _mapGenerator = mapGenerator;

	public override void _Ready()
	{
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.PlayerSpawned += OnPlayerSpawned, bus => bus.PlayerSpawned -= OnPlayerSpawned);
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.DoorOpened += OnDoorOpened, bus => bus.DoorOpened -= OnDoorOpened);
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.DoorClosed += OnDoorClosed, bus => bus.DoorClosed -= OnDoorClosed);
		_player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
	}

	private void OnPlayerSpawned(Player player) { _player = player; _untilUpdate = 0; _refresh = true; }
	private void OnDoorOpened(Node3D door) { _mapGenerator?.OpenDoorAt(door.GlobalPosition); _untilUpdate = 0; _refresh = true; }
	private void OnDoorClosed(Node3D door) { _mapGenerator?.CloseDoorAt(door.GlobalPosition); _untilUpdate = 0; _refresh = true; }

	public override void _PhysicsProcess(double delta)
	{
		_untilUpdate -= delta;
		if (_untilUpdate > 0 || !IsInstanceValid(_player) || _mapGenerator?.Map == null) return;
		_untilUpdate = 0.1;
		if (!_refresh && _lastRadius == _mapGenerator.SightRadius && _lastPosition.DistanceSquaredTo(_player.GlobalPosition) < 0.0225f) return;
		_refresh = false;
		_lastPosition = _player.GlobalPosition;
		_lastRadius = _mapGenerator.SightRadius;
		_mapGenerator.RevealFrom(_lastPosition);
	}
}