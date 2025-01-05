using Godot;

public partial class InputComponent : Node
{
	[Signal] public delegate void DirectionChangedEventHandler(Vector3 direction);
	[Signal] public delegate void ActionEventHandler(int action);

	public Vector3 InputDirection { get; set; } = Vector3.Zero;

	public bool IsMoving()
	{
		return InputDirection != Vector3.Zero;
	}

	public bool IsActionPressed(int action)
	{
		return Input.IsActionPressed($"action_{action}");
	}

	public override void _Process(double delta)
	{
		// Get the camera's forward and right vectors
		Camera3D camera = GetViewport().GetCamera3D();
		Vector3 cameraForward = camera.GlobalTransform.Basis.Z;
		Vector3 cameraRight = camera.GlobalTransform.Basis.X;
		var direction = Vector3.Zero;

		// We check for each move input and update the direction accordingly.
		if (Input.IsActionPressed("move_right"))
		{
			direction += cameraRight;
		}
		if (Input.IsActionPressed("move_left"))
		{
			direction -= cameraRight;
		}
		if (Input.IsActionPressed("move_up"))
		{
			direction -= cameraForward;
		}
		if (Input.IsActionPressed("move_down"))
		{
			direction += cameraForward;
		}

		if (direction != Vector3.Zero)
		{
			direction.Y = 0;
			direction = direction.Normalized();
		}

		InputDirection = direction;
		EmitSignal(SignalName.DirectionChanged, direction);
	}
}
