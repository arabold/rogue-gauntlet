using Godot;

public partial class CameraManager : Node
{
	[Export] public PlayerSpawner PlayerSpawner { get; set; }
	[Export] public Node PCam { get; set; }

	public override void _Ready()
	{
		PlayerSpawner.Connect(
			PlayerSpawner.SignalName.PlayerSpawned,
			Callable.From<Player>(OnPlayerSpawned)
		);
	}

	private void OnPlayerSpawned(Player player)
	{
		GD.Print($"{player.Name} spawned. Setting camera target...");
		PCam.Call("set_follow_target", player);
		PCam.Set("follow_mode", 5); // FRAMED = 5
	}
}
