using Godot;

public partial class MovementComponent : Node
{
	[Export] public float Speed { get; set; } = 3.0f; // Movement speed
	[Export] public float RotationSpeed { get; set; } = 20.0f; // Increased rotation speed

	private Node3D _parent; // Parent node
	private Vector3 _targetPosition = Vector3.Zero;

	public override void _Ready()
	{
		_parent = GetParent<Node3D>();
	}

	public void MoveTo(Vector3 targetPosition)
	{
		_targetPosition = targetPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_targetPosition != Vector3.Zero)
		{
			Vector3 direction = (_targetPosition - _parent.GlobalTransform.Origin).Normalized();
			Vector3 moveVector = direction * Speed * (float)delta;

			// Automatically orient the parent towards the target position
			_parent.LookAt(_targetPosition, Vector3.Up);

			// Move the enemy towards the target
			_parent.GlobalTransform = new Transform3D(_parent.GlobalTransform.Basis, _parent.GlobalTransform.Origin + moveVector);

			// Check if the enemy has reached the target position
			if (_parent.GlobalTransform.Origin.DistanceTo(_targetPosition) < 0.1f)
			{
				_targetPosition = Vector3.Zero;
			}
		}
	}

	public void Stop()
	{
		_targetPosition = Vector3.Zero;
	}
}
