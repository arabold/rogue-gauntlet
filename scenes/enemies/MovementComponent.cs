using System;
using Godot;

public partial class MovementComponent : Node
{
	[Export] public float Speed { get; set; } = 3.0f; // Movement speed
	[Export] public float RotationSpeed { get; set; } = 20.0f; // Increased rotation speed

	public bool IsMoving => _targetPosition != Vector3.Zero;

	private CharacterBody3D _parent; // Parent node
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

	public void MoveTo(Vector3 targetPosition)
	{
		_targetPosition = targetPosition;
	}

	public void Push(Vector3 direction, float strength)
	{
		_pushDirection = direction.Normalized();
		_pushStrength = strength;
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Vector3.Zero;

		if (_pushStrength > 0.0f)
		{
			velocity = _pushDirection * _pushStrength;
			_pushStrength -= Speed * (float)delta; // Decrease push strength over time
		}
		else if (_targetPosition != Vector3.Zero)
		{
			Vector3 direction = (_targetPosition - _parent.GlobalTransform.Origin).Normalized();
			velocity = direction * Speed;

			// Automatically orient the parent towards the target position
			_parent.LookAt(_targetPosition, Vector3.Up);

			// Check if the enemy has reached the target position
			if (_parent.GlobalTransform.Origin.DistanceTo(_targetPosition) < 0.1f)
			{
				_targetPosition = Vector3.Zero;
				velocity = Vector3.Zero;
			}
		}

		_parent.Velocity = velocity;
		_parent.MoveAndSlide();
	}

	public void Stop()
	{
		_targetPosition = Vector3.Zero;
		_parent.Velocity = Vector3.Zero;
	}
}
