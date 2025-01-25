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

		// At this point the player is already spawned
		SignalBus.Instance.PlayerSpawned += OnPlayerSpawned;
		if (GameManager.Instance.Player != null)
		{
			OnPlayerSpawned(GameManager.Instance.Player);
		}
	}

	private void OnPlayerSpawned(Player player)
	{
		GD.Print($"{player.Name} spawned. Setting camera target...");
		_pcam.Call("set_follow_target", player);
		_pcam.Set("follow_mode", 5); // FRAMED = 5
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionReleased("inventory"))
		{
			var inventoryDialog = GetNode<InventoryDialog>("%InventoryDialog");
			inventoryDialog.Open(GameManager.Instance.Player.Inventory);
		}

		base._UnhandledInput(@event);
	}
}
