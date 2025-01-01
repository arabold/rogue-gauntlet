using Godot;
using System;

public partial class Mob : CharacterBody3D
{
	// Minimum speed of the mob in meters per second
	[Export]
	public int MinSpeed { get; set; } = 10;

	// Maximum speed of the mob in meters per second
	[Export]
	public int MaxSpeed { get; set; } = 18;

	[Export]
	public float RotationSpeed { get; set; } = 10.0f;

	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;

	private Vector3 _previousVelocity = Vector3.Zero;
	private Vector3 _targetLookDirection = Vector3.Forward;
	private const float VELOCITY_CHANGE_THRESHOLD = 0.1f;
	private bool _isRotating = false;

	public override void _Ready()
	{
		base._Ready();

		_animationTree = GetNode<AnimationTree>("AnimationTree");
		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		_animationStateMachine.Start("Walking_A");
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 currentVelocity = new Vector3(Velocity.X, 0, Velocity.Z);

		// Check if velocity changed enough to warrant rotation update
		if (!IsVelocitySimilar(_previousVelocity, currentVelocity))
		{
			if (currentVelocity.Length() > 0.1f)
			{
				_isRotating = true;
				_targetLookDirection = currentVelocity.Normalized();
			}
		}
		else if (_isRotating && currentVelocity.Length() < 0.1f)
		{
			_isRotating = false;
		}

		// Apply rotation if needed
		if (_isRotating)
		{
			var currentRotation = Quaternion.FromEuler(Rotation);
			var targetRotation = GetLookAtQuaternion(_targetLookDirection);

			// Ensure both quaternions are normalized
			currentRotation = currentRotation.Normalized();
			targetRotation = targetRotation.Normalized();

			Rotation = currentRotation.Slerp(targetRotation, (float)delta * RotationSpeed).GetEuler();
		}

		_previousVelocity = currentVelocity;
		MoveAndSlide();
	}

	private bool IsVelocitySimilar(Vector3 a, Vector3 b)
	{
		return (a - b).LengthSquared() < VELOCITY_CHANGE_THRESHOLD * VELOCITY_CHANGE_THRESHOLD;
	}

	private Quaternion GetLookAtQuaternion(Vector3 direction)
	{
		// Create a basis that looks in the target direction
		var lookAtBasis = Basis.LookingAt(direction, Vector3.Up);
		// Convert to quaternion and normalize
		return lookAtBasis.GetRotationQuaternion().Normalized();
	}

	public void Initialize(Vector3 startPosition, Vector3 playerPosition)
	{
		// We position the mob by placing it at startPosition
		// and rotate it towards playerPosition, so it looks at the player.
		LookAtFromPosition(startPosition, playerPosition, Vector3.Up);
		// Rotate this mob randomly within range of -45 and +45 degrees,
		// so that it doesn't move directly towards the player.
		RotateY((float)GD.RandRange(-Mathf.Pi / 4.0, Mathf.Pi / 4.0));

		// We calculate a random speed (integer).
		int randomSpeed = GD.RandRange(MinSpeed, MaxSpeed);
		// We calculate a forward velocity that represents the speed.
		Velocity = Vector3.Forward * randomSpeed;
		// We then rotate the velocity vector based on the mob's Y rotation
		// in order to move in the direction the mob is looking.
		Velocity = Velocity.Rotated(Vector3.Up, Rotation.Y);
	}

	private void OnVisibilityNotifierScreenExited()
	{
		QueueFree();
	}
}
