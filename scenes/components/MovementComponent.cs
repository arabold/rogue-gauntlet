using System;
using Godot;

public partial class MovementComponent : Node
{
	/// <summary>
	/// Movement speed
	/// </summary>
	[Export] public float Speed { get; set; } = 3.0f;
	/// <summary>
	/// Rotation speed
	/// </summary>
	[Export] public float RotationSpeed { get; set; } = 20.0f;
	/// <summary>
	/// The NavigationAgent3D node that will be used to navigate the character.
	/// </summary>
	[Export] public NavigationAgent3D NavigationAgent;
	/// <summary>
	/// The StairsTriggers node that will be used to detect stairs.
	/// </summary>
	[Export] public StairsTriggers StairsTriggers;
	/// <summary>
	/// The gravity strength.
	/// </summary>
	[Export] public float Gravity = 9.8f;
	/// <summary>
	/// Maximum fall speed
	/// </summary>
	[Export] public float MaxFallSpeed = 50.0f;
	/// <summary>
	/// Stairs step height 
	/// </summary>
	[Export] public float StairsStepHeight = 0.5f;

	public bool IsMoving => _targetDirection != Vector3.Zero;
	public bool IsPushed => _pushStrength > 0.1f;
	public bool IsFalling => _velocity.Y < 0;

	public Vector3 Velocity => _velocity;
	public Vector3 TargetDirection => _targetDirection;
	public Vector3 LookAtDirection => _lookAtDirection;

	private CharacterBody3D _parent;
	private Vector3 _velocity = Vector3.Zero;
	private Vector3 _lookAtDirection = Vector3.Forward;
	private Vector3 _targetDirection = Vector3.Zero;
	private Vector3 _targetPosition = Vector3.Zero;
	private Vector3 _pushDirection = Vector3.Zero;
	private float _pushStrength = 0.0f;

	public override void _Ready()
	{
		_parent = GetParent<CharacterBody3D>();
		_lookAtDirection = _parent.GlobalTransform.Basis.Z;
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

	public void SetLookAtDirection(Vector3 lookAtDirection)
	{
		_lookAtDirection = lookAtDirection.Normalized();
	}

	public void Stop()
	{
		_targetDirection = Vector3.Zero;
		_targetPosition = Vector3.Zero;
	}

	public override void _PhysicsProcess(double delta)
	{
		NavigateToTarget();
		UpdateVelocity(delta);
		SmoothRotateToward(delta);
	}

	private void UpdateVelocity(double delta)
	{
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

			// Check if we're on stairs
			if (_velocity != Vector3.Zero && StairsTriggers?.stairs > 0)
			{
				_velocity.Y = StairsStepHeight * Speed;
			}
		}
		else
		{
			// Apply gravity if not on the floor
			_velocity.Y -= Gravity * Gravity * (float)delta; // Gravity applied over time
			_velocity.Y = Mathf.Clamp(_velocity.Y, -MaxFallSpeed, MaxFallSpeed);
		}
	}

	private void NavigateToTarget()
	{
		// If we have a target position, navigate to it
		if (_targetPosition != Vector3.Zero)
		{
			if (_parent.GlobalPosition.DistanceTo(_targetPosition) < 1f)
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
				_targetDirection = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
			}
		}
	}

	private void SmoothRotateToward(double delta)
	{
		if (_targetDirection != Vector3.Zero)
		{
			var lookAt = -new Vector3(_targetDirection.X, 0, _targetDirection.Z).Normalized();
			if (lookAt.IsEqualApprox(_lookAtDirection))
			{
				_lookAtDirection = lookAt;
			}
			else
			{
				// Handle the case where lookAt is exactly opposite to _lookAtDirection
				if (lookAt.Dot(_lookAtDirection) < -0.999f)
				{
					lookAt += new Vector3(0.001f, 0, 0).Normalized();
				}
				_lookAtDirection = _lookAtDirection.Slerp(
					lookAt, RotationSpeed * (float)delta
				).Normalized();
			}
		}
	}
}
