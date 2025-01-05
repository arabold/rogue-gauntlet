using Godot;
using System;
using System.Collections.Generic;

public partial class EnemyBehavior : Node
{
	public enum BehaviorState
	{
		Sleeping,
		Idle,
		Guarding,
		Patrolling,
		Searching,
		Chasing,
		Fleeing,
		Dead
	}

	public enum ActionState
	{
		None,
		Spawning,
		StandingUp,
		Hit,
		Attacking,
		Dying,
	}

	private static readonly Dictionary<ActionState, float> ActionDurations = new()
	{
		{ ActionState.None, 0 },
		{ ActionState.Spawning, 3.5f },
		{ ActionState.StandingUp, 2.33f },
		{ ActionState.Hit, 0.66f },
		{ ActionState.Attacking, 1.0f },
		{ ActionState.Dying, 2.0f },
	};

	private Node3D _parent;
	private Node3D _target;
	private MovementComponent _movementComponent;
	private float _remainingActionTime = 0;

	[Export] public BehaviorState CurrentBehavior { get; private set; } = BehaviorState.Idle;
	[Export] public ActionState CurrentAction { get; private set; } = ActionState.Spawning;

	// We can use these properties to automatically transition between animation states
	public bool IsSleeping => CurrentAction == ActionState.None && CurrentBehavior == BehaviorState.Sleeping;
	public bool IsMoving => CurrentAction == ActionState.None && _movementComponent.IsMoving;
	public bool IsDead => CurrentAction == ActionState.None && CurrentBehavior == BehaviorState.Dead;
	public bool IsAttacking => CurrentAction == ActionState.Attacking;
	public bool IsHit => CurrentAction == ActionState.Hit;
	public bool IsDying => CurrentAction == ActionState.Dying;
	public bool IsSpawning => CurrentAction == ActionState.Spawning;

	public void SetSleeping() => SetBehavior(BehaviorState.Sleeping);
	public void SetIdle() => SetBehavior(BehaviorState.Idle);
	public void SetGuarding(Node3D target) => SetBehavior(BehaviorState.Guarding, target);
	public void SetPatrolling() => SetBehavior(BehaviorState.Patrolling);
	public void SetSearching(Node3D target) => SetBehavior(BehaviorState.Searching, target);
	public void SetChasing(Node3D target) => SetBehavior(BehaviorState.Chasing, target);
	public void SetFleeing(Node3D target) => SetBehavior(BehaviorState.Fleeing, target);
	public void SetDead() => SetBehavior(BehaviorState.Dead);

	public override void _Ready()
	{
		base._Ready();

		_parent = GetParent<Node3D>(); // Assume the parent is the Enemy node
		_movementComponent = _parent.GetNode<MovementComponent>("MovementComponent");

		// Ensure to properly initialize the enemy's state with the current selection
		GD.Print($"{_parent.Name} is initialized with {CurrentBehavior} and {CurrentAction}");
		_remainingActionTime = ActionDurations[CurrentAction];
	}

	public override void _Process(double delta)
	{
		if (CurrentAction != ActionState.None)
		{
			// Update the remaining action time
			_remainingActionTime -= (float)delta;
			if (_remainingActionTime <= 0)
			{
				SetAction(ActionState.None);
			}
		}
		else
		{
			// Main behavior logic goes here...
			if (CurrentBehavior == BehaviorState.Chasing)
			{
				ChaseTarget();
			}
			else if (CurrentBehavior == BehaviorState.Fleeing)
			{
				FleeFromTarget();
			}
		}
	}

	private void ChaseTarget()
	{
		if (_target == null)
		{
			return;
		}

		// Get the target's position
		Vector3 targetPosition = _target.GlobalTransform.Origin;

		// Move toward the target
		if (_movementComponent != null)
		{
			_movementComponent.NavigateTo(targetPosition);
		}
	}

	private void FleeFromTarget()
	{
		if (_target == null)
		{
			return;
		}

		// Get the target's position
		Vector3 targetPosition = _target.GlobalTransform.Origin;

		// Move away from the target
		if (_movementComponent != null)
		{
			_movementComponent.NavigateTo(-targetPosition);
		}
	}

	private void SetBehavior(BehaviorState newBehavior, Node3D target = null)
	{
		if (CurrentBehavior == BehaviorState.Dead)
		{
			return;
		}

		_target = target;
		if (CurrentBehavior != newBehavior)
		{
			GD.Print($"{_parent.Name} is now {newBehavior}");
			CurrentBehavior = newBehavior;
		}
	}

	private void SetAction(ActionState newAction)
	{
		if (CurrentBehavior == BehaviorState.Dead)
		{
			return;
		}

		if (CurrentAction != newAction)
		{
			GD.Print($"{_parent.Name} is performing {newAction}");
			CurrentAction = newAction;
			_remainingActionTime = ActionDurations[newAction];
		}
	}

	public void Hit()
	{
		SetAction(EnemyBehavior.ActionState.Hit);
	}

	public void Die()
	{
		SetAction(ActionState.Dying);
		SetBehavior(BehaviorState.Dead);

		// Stop movement immediately
		_movementComponent.Stop();

		// Wait for death animation to finish
		GetTree().CreateTimer(2.0f).Connect("timeout", Callable.From(() =>
		{
			GD.Print($"{_parent.Name} is destroyed!");
			_parent.QueueFree();
		}));
	}
}
