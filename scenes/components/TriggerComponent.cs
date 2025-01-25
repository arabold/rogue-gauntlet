using Godot;

/// <summary>
/// A component that triggers events when the player enters or exits its area.
/// </summary>
public partial class TriggerComponent : Area3D
{
	[Signal]
	public delegate void TriggeredEventHandler(Node3D body);

	public override void _Ready()
	{
		// AreaEntered += OnAreaEntered;
		BodyEntered += OnBodyEntered;
	}

	// private void OnAreaEntered(Node3D area)
	// {
	// 	GD.Print($"{area.Name} entered the trigger area");
	// 	EmitSignal(SignalName.Triggered, area);
	// }

	private void OnBodyEntered(Node3D body)
	{
		GD.Print($"{body.Name} entered the trigger area");
		EmitSignal(SignalName.Triggered, body);
	}
}
