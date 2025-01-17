using System.ComponentModel.DataAnnotations;
using Godot;

public partial class Main : Node
{
	// TODO: There's no PhantomCamera3D in C# API
	private Node3D _pcam;

	public override void _Ready()
	{
		GD.Print("Main scene is ready");
		_pcam = GetNode<Node3D>("PhantomCamera3D");

		// Typically, the level scene is already loaded and the player is spawned
		// at the point when the main scene is ready.
		OnPlayerSpawned(GameManager.Instance.Player);
		SignalBus.Instance.PlayerSpawned += OnPlayerSpawned;

		GD.Print("Main scene done");
	}

	private void OnPlayerSpawned(Player player)
	{
		if (player == null)
		{
			GD.PrintErr("Player is null");
			return;
		}
		GD.Print($"{player.Name} spawned. Setting camera target...");
		_pcam.Call("set_follow_target", player);
		_pcam.Set("follow_mode", 5); // FRAMED = 5
	}
}
