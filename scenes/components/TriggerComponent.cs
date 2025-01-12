using Godot;

/// <summary>
/// A component that triggers events when the player enters or exits its area.
/// </summary>
public partial class TriggerComponent : Area3D
{
	[Signal]
	public delegate void PlayerEnteredEventHandler(Player player);
	[Signal]
	public delegate void PlayerExitedEventHandler(Player player);

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	private void OnBodyEntered(Node body)
	{
		if (body is Player player)
		{
			GD.Print($"{player.Name} entered the trigger area");
			EmitSignal(SignalName.PlayerEntered, player);
		}
	}

	private void OnBodyExited(Node body)
	{
		if (body is Player player)
		{
			GD.Print($"{player.Name} left the trigger area");
			EmitSignal(SignalName.PlayerExited, player);
		}
	}
}
