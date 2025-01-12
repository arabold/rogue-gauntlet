using Godot;
using System;

public partial class InteractionArea : Area3D
{
	private Player _player;

	[Signal]
	public delegate void InteractiveEnteredEventHandler(Node node);
	[Signal]
	public delegate void InteractiveExitedEventHandler(Node node);

	public override void _Ready()
	{
		_player = GetParent<Player>();

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
		BodyEntered += OnAreaEntered;
		BodyExited += OnAreaExited;
	}

	private void OnAreaEntered(Node body)
	{
		GD.Print($"{body.Name} entered the detection area");
		if (body is IInteractive interactive)
		{
			interactive.OnPlayerNearby(_player);
			EmitSignal(SignalName.InteractiveEntered, body);
		}
	}

	private void OnAreaExited(Node body)
	{
		GD.Print($"{body.Name} left the detection area");
		if (body is IInteractive interactive)
		{
			interactive.OnPlayerLeft(_player);
			EmitSignal(SignalName.InteractiveExited, body);
		}
	}
}
