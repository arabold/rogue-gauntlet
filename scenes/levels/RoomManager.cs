using System.Linq;
using Godot;

/// <summary>
/// Tracks which room the player currently occupies and emits RoomEntered when it
/// changes. Detection is a per-frame tile lookup against the generated map, which
/// is exact for any room shape and needs no per-room collision shapes.
/// </summary>
public partial class RoomManager : Node
{
	private MapGenerator _mapGenerator;
	private Player _player;
	private int _currentRoomId = -1;

	public void Initialize(MapGenerator mapGenerator)
	{
		_mapGenerator = mapGenerator;
	}

	public override void _Ready()
	{
		// The player may already be spawned by the time this manager is created, so
		// resolve it both ways: the group covers the already-spawned case and the
		// signal covers respawns.
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.PlayerSpawned += OnPlayerSpawned,
			bus => bus.PlayerSpawned -= OnPlayerSpawned);
		_player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
	}

	private void OnPlayerSpawned(Player player)
	{
		_player = player;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null || _mapGenerator?.Map == null)
		{
			return;
		}

		var tile = _mapGenerator.WorldToTile(_player.GlobalPosition);
		int roomId = _mapGenerator.GetRoomIdAt(tile);
		if (roomId == _currentRoomId)
		{
			return;
		}

		_currentRoomId = roomId;

		if (roomId != -1)
		{
			SignalBus.EmitRoomEntered(roomId);
		}
	}
}
