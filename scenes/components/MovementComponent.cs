using System;
using Godot;

public partial class MovementComponent : Node
{
	// Movement speed
	[Export] public float Speed { get; set; } = 3.0f;
	// Rotation speed
	[Export] public float RotationSpeed { get; set; } = 20.0f;
	// The NavigationAgent3D node that will be used for pathfinding
	[Export] public NavigationAgent3D NavigationAgent;
	// Gravity
	[Export] public float Gravity = 9.8f;
	// Maximum fall speed
	[Export] public float MaxFallSpeed = 50.0f;

	public bool IsMoving => _targetDirection != Vector3.Zero;
	public bool IsPushed => _pushStrength > 0.1f;
	public bool IsFalling => _velocity.Y < 0;

	private CharacterBody3D _parent;
	private Vector3 _velocity = Vector3.Zero;
	private Vector3 _targetDirection = Vector3.Zero;
	private Vector3 _targetPosition = Vector3.Zero;
	private Vector3 _pushDirection = Vector3.Zero;
	private float _pushStrength = 0.0f;

	public override void _Ready()
	{
		_parent = GetParent<CharacterBody3D>();

		// Make the parent look in a random direction
		Random random = new Random();
		float randomRotation = (float)(random.NextDouble() * 2 * Math.PI);
		_parent.RotateY(randomRotation);
	}

	public void NavigateTo(Vector3 targetPosition)
	{
		if (NavigationAgent == null)
		{
			GD.PrintErr("NavigationAgent3D not found");
			return;
		}

		if (_targetPosition != targetPosition)
		{
			_targetPosition = targetPosition;
			NavigationAgent.SetTargetPosition(_targetPosition);
		}
	}

	public void Push(Vector3 direction, float strength)
	{
		_pushDirection = direction.Normalized();
		_pushStrength = strength;
	}

	public void SetInputDirection(Vector3 inputDirection)
	{
		// Set target direction based on input direction
		_targetDirection = inputDirection.Normalized();
	}

	public Vector3 GetVelocity()
	{
		return _velocity;
	}

	public Vector3 GetDirection()
	{
		return _targetDirection;
	}

	public override void _PhysicsProcess(double delta)
	{
		// If we have a target position, navigate to it
		if (_targetPosition != Vector3.Zero)
		{
			if (_parent.GlobalTransform.Origin.DistanceTo(_targetPosition) < 1f)
			{
				// If we reached the final destination, stop moving
				_targetDirection = Vector3.Zero;
				_targetPosition = Vector3.Zero;
			}
			else
			{
				// Move to the next path position
				Vector3 destination = NavigationAgent.GetNextPathPosition();
				Vector3 localDestination = destination - _parent.GlobalPosition;
				_targetDirection = localDestination.Normalized();
			}
		}

		Vector3 velocity;
		if (_pushStrength > 0.1f)
		{
			// Pushed by an external force
			velocity = _pushDirection * _pushStrength;
			_pushStrength -= Speed * (float)delta; // Decrease push strength over time
		}
		else if (_targetDirection != Vector3.Zero)
		{
			// General movement
			velocity = _targetDirection * Speed;
		}
		else
		{
			// Stop moving if no input
			velocity = Vector3.Zero;
		}

		// Apply horizontal velocity
		_velocity.X = velocity.X;
		_velocity.Z = velocity.Z;

		if (_parent.IsOnFloor())
		{
			// Reset vertical velocity when on the floor
			_velocity.Y = 0;
		}
		else
		{
			// Apply gravity if not on the floor
			_velocity.Y -= Gravity * Gravity * (float)delta; // Gravity applied over time
			_velocity.Y = Mathf.Clamp(_velocity.Y, -MaxFallSpeed, MaxFallSpeed);
		}
	}

	public void Stop()
	{
		_targetDirection = Vector3.Zero;
		_targetPosition = Vector3.Zero;
	}
}
