using Godot;
using System;
using System.Collections.Generic;

public partial class Enemy : CharacterBody3D, IDamageable
{
	public enum BehaviorState
	{
		Sleeping,
		Idle,
		Walking
	}

	public enum ActionState
	{
		None,
		Hit,
		Attacking,
		Dying
	}

	private static readonly Dictionary<BehaviorState, string> BehaviorAnimations = new()
	{
		{ BehaviorState.Sleeping, "Lie_Idle" },
		{ BehaviorState.Idle, "Idle" },
		{ BehaviorState.Walking, "Walking_A" }
	};

	private static readonly Dictionary<ActionState, string> ActionAnimations = new()
	{
		{ ActionState.None, null },
		{ ActionState.Hit, "Hit_A" },
		{ ActionState.Attacking, "1H_Melee_Attack_Chop" },
		{ ActionState.Dying, "Death_A" }
	};

	// Minimum speed of the enemy in meters per second
	[Export] public int MinSpeed { get; set; } = 10;

	// Maximum speed of the enemy in meters per second
	[Export] public int MaxSpeed { get; set; } = 18;

	[Export] public float RotationSpeed { get; set; } = 10.0f;

	// Total health
	[Export] public int MaxHitPoints = 10;

	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;

	private Vector3 _previousVelocity = Vector3.Zero;
	private Vector3 _targetLookDirection = Vector3.Forward;
	private const float VELOCITY_CHANGE_THRESHOLD = 0.1f;
	private bool _isRotating = false;
	private int _currentHitPoints;

	private BehaviorState _currentBehavior = BehaviorState.Walking;
	private ActionState _currentAction = ActionState.None;

	private Node3D _pivot;

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

		_animationTree = GetNode<AnimationTree>("AnimationTree");
		if (_animationTree == null)
		{
			GD.PrintErr("AnimationTree not found!");
			QueueFree();
			return;
		}

		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		UpdateAnimation();
		_currentHitPoints = MaxHitPoints;
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
			// Create the target rotation basis (using negative direction like the Player class)
			var targetBasis = Basis.LookingAt(-_targetLookDirection);
			// Smoothly interpolate between current and target rotation
			_pivot.Basis = _pivot.Basis.Slerp(targetBasis, (float)delta * RotationSpeed);
		}

		// Update behavior based on velocity
		if (currentVelocity.Length() > 0.1f)
		{
			SetBehavior(BehaviorState.Walking);
		}
		else
		{
			SetBehavior(BehaviorState.Idle);
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
		_currentHitPoints -= amount;

		SetAction(ActionState.Hit);
		SpawnHitEffect();

		if (_currentHitPoints <= 0)
		{
			Die();
		}
		else
		{
			// Reset action state after a short delay
			GetTree().CreateTimer(0.3f).Connect("timeout", Callable.From(() => SetAction(ActionState.None)));
		}
	}

	private void SpawnHitEffect()
	{
		// Load the HitEffect scene
		var hitEffect = ResourceLoader.Load<PackedScene>("res://scenes/effects/hit_effect.tscn").Instantiate<GpuParticles3D>();
		hitEffect.GlobalTransform = GlobalTransform; // Position the effect at the enemy's location
		hitEffect.OneShot = true;

		// Add to the scene
		GetParent().AddChild(hitEffect);
	}

	private void Die()
	{
		SetAction(ActionState.Dying);

		// Stop nao movement immediately
		Velocity = Vector3.Zero;

		// Wait for death animation to finish
		GetTree().CreateTimer(1.0f).Connect("timeout", Callable.From(() =>
		{
			GD.Print($"{Name} is destroyed!");
			QueueFree();
		}));
	}

	private void UpdateAnimation()
	{
		string targetAnimation = _currentAction != ActionState.None
			? ActionAnimations[_currentAction]
			: BehaviorAnimations[_currentBehavior];

		_animationStateMachine.Travel(targetAnimation);
	}

	public void SetBehavior(BehaviorState newBehavior)
	{
		if (_currentBehavior != newBehavior)
		{
			_currentBehavior = newBehavior;
			if (_currentAction == ActionState.None)
			{
				UpdateAnimation();
			}
		}
	}

	public void SetAction(ActionState newAction)
	{
		if (_currentAction != newAction)
		{
			_currentAction = newAction;
			UpdateAnimation();
		}
	}
}
