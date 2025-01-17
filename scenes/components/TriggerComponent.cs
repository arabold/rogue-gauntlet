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
		AreaEntered += OnAreaEntered;
	}

	private void OnAreaEntered(Node3D body)
	{
		GD.Print($"{body.Name} enterd the trigger area");
		EmitSignal(SignalName.Triggered, body);
	}
}
