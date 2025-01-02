using Godot;
using System;
using System.Collections.Generic;

public partial class Enemy : CharacterBody3D, IDamageable
{
	// Minimum speed of the enemy in meters per second
	[Export] public int MinSpeed { get; set; } = 10;

	// Maximum speed of the enemy in meters per second
	[Export] public int MaxSpeed { get; set; } = 18;

	[Export] public float RotationSpeed { get; set; } = 10.0f;

	// Total health
	[Export] public int MaxHitPoints = 10;

	private Vector3 _previousVelocity = Vector3.Zero;
	private Vector3 _targetLookDirection = Vector3.Forward;
	private const float VELOCITY_CHANGE_THRESHOLD = 0.1f;
	private bool _isRotating = false;
	private int _currentHitPoints;

	private Node3D _pivot;
	private EnemyBehavior _enemyBehavior;

	public override void _Ready()
	{
		base._Ready();

		_pivot = GetNode<Node3D>("Pivot");
		if (_pivot == null)
		{
			GD.PrintErr("Pivot node not found! Make sure to add a Node3D named 'Pivot' as a child of the Enemy.");
			QueueFree();
			return;
		}

		// Rotate the pivot 180 degrees to correct initial orientation
		_pivot.RotateY(Mathf.Pi);

		_enemyBehavior = GetNode<EnemyBehavior>("EnemyBehavior");
		if (_enemyBehavior == null)
		{
			GD.PrintErr("EnemyBehavior node not found!");
			QueueFree();
			return;
		}

		_currentHitPoints = MaxHitPoints;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Prevent any logic if the enemy is dead
		if (_enemyBehavior.CurrentBehavior == EnemyBehavior.BehaviorState.Dead)
		{
			return;
		}

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
			// Create the target rotation basis (using negative direction like the Player class)
			var targetBasis = Basis.LookingAt(-_targetLookDirection);
			// Smoothly interpolate between current and target rotation
			_pivot.Basis = _pivot.Basis.Slerp(targetBasis, (float)delta * RotationSpeed);
		}

		// Update behavior based on velocity
		if (currentVelocity.Length() > 0.1f)
		{
			// Set behavior to one of the walking states based on your AI logic
			_enemyBehavior.SetBehavior(EnemyBehavior.BehaviorState.Patrolling); // Example: Set to Patrolling
		}
		else
		{
			_enemyBehavior.SetBehavior(EnemyBehavior.BehaviorState.Idle);
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
		Position = startPosition;
		var direction = (playerPosition - startPosition).Normalized();

		// Calculate random speed and velocity
		int randomSpeed = GD.RandRange(MinSpeed, MaxSpeed);
		Velocity = direction * randomSpeed;
	}

	private void OnVisibilityNotifierScreenExited()
	{
		QueueFree();
	}

	public void TakeDamage(int amount)
	{
		_enemyBehavior.TakeDamage(amount);
	}
}
