using Godot;

/// <summary>
/// An area that reports bodies entering it (<see cref="Triggered"/>) and leaving it (<see cref="Released"/>); its
/// collision mask decides which bodies count.
/// </summary>
[GlobalClass]
public partial class TriggerComponent : Area3D
{
	[Signal]
	public delegate void TriggeredEventHandler(Node3D body);
	[Signal]
	public delegate void ReleasedEventHandler(Node3D body);

	/// <summary>
	/// Waits for body exit event before triggering.
	/// This is useful when a player just dropped an item and is still within
	/// the trigger zone. In that case we want to wait until the player leaves
	/// before listening for new trigger events.
	/// </summary>
	[Export] public bool WaitForBodyExited { get; set; }

	public override void _Ready()
	{
		// AreaEntered += OnAreaEntered;
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (!WaitForBodyExited)
		{
			GD.Print($"{body.Name} entered the trigger area");
			EmitSignalTriggered(body);
		}
	}

	private void OnBodyExited(Node3D body)
	{
		WaitForBodyExited = false;
		EmitSignalReleased(body);
	}
}
