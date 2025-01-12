using Godot;

/// <summary>
/// A component that triggers events when the player enters or exits its area.
/// </summary>
public partial class TriggerComponent : Area3D
{
	/// <summary>
	/// The radius around the object in which the trigger will activate.
	/// </summary>
	[Export] public float Radius { get; set; } = 1.5f;

	[Signal]
	public delegate void OnPlayerEnteredEventHandler(Player player);
	[Signal]
	public delegate void OnPlayerExitedEventHandler(Player player);

	private CollisionShape3D _collisionShape;

	public override void _Ready()
	{
		_collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
		((SphereShape3D)_collisionShape.Shape).Radius = Radius;

		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	private void OnBodyEntered(Node body)
	{
		if (body is Player player)
		{
			GD.Print($"{player.Name} entered the trigger area");
			EmitSignal(SignalName.OnPlayerEntered, player);
		}
	}

	private void OnBodyExited(Node body)
	{
		if (body is Player player)
		{
			GD.Print($"{player.Name} left the trigger area");
			EmitSignal(SignalName.OnPlayerExited, player);
		}
	}
}
