using Godot;
using System;
using System.Collections.Generic;

public partial class EnemyBehavior : Node, IDamageable
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
		Hit,
		Attacking,
		Dying
	}

	private static readonly Dictionary<BehaviorState, string> BehaviorAnimations = new()
	{
		{ BehaviorState.Sleeping, "Lie_Idle" },
		{ BehaviorState.Idle, "Idle" },
		{ BehaviorState.Guarding, "Walking_A" },
		{ BehaviorState.Patrolling, "Walking_A" },
		{ BehaviorState.Searching, "Walking_A" },
		{ BehaviorState.Chasing, "Walking_A" },
		{ BehaviorState.Fleeing, "Walking_A" },
		{ BehaviorState.Dead, "Death_A" }
	};

	private static readonly Dictionary<ActionState, string> ActionAnimations = new()
	{
		{ ActionState.None, null },
		{ ActionState.Hit, "Hit_A" },
		{ ActionState.Attacking, "1H_Melee_Attack_Chop" },
		{ ActionState.Dying, "Death_A" }
	};

	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private int _currentHitPoints;
	private int _maxHitPoints;
	// Reference to the Enemy's main Node
	private Node3D _owner;
	// Reference to the target Node, i.e. the player
	private Node3D _target;
	private MovementComponent _movementComponent;

	[Export] public float ChaseDistance = 10.0f; // Distance within which the enemy starts chasing the player
	[Export] public PackedScene HitEffect { get; set; }

	public BehaviorState CurrentBehavior { get; private set; } = BehaviorState.Idle;
	public ActionState CurrentAction { get; private set; } = ActionState.None;

	public override void _Ready()
	{
		base._Ready();

		_owner = GetParent<Node3D>(); // Assume the parent is the Enemy node
		_animationTree = GetNode<AnimationTree>("AnimationTree");
		if (_animationTree == null)
		{
			GD.PrintErr("AnimationTree not found!");
			GetParent().QueueFree();
			return;
		}

		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		UpdateAnimation();

		_maxHitPoints = ((Enemy)_owner).MaxHitPoints;
		_currentHitPoints = _maxHitPoints;

		_movementComponent = _owner.GetNode<MovementComponent>("MovementComponent");
	}

	public void SetSleeping() => SetBehavior(BehaviorState.Sleeping);
	public void SetIdle() => SetBehavior(BehaviorState.Idle);
	public void SetGuarding(Node3D target) => SetBehavior(BehaviorState.Guarding, target);
	public void SetPatrolling() => SetBehavior(BehaviorState.Patrolling);
	public void SetSearching(Node3D target) => SetBehavior(BehaviorState.Searching, target);
	public void SetChasing(Node3D target) => SetBehavior(BehaviorState.Chasing, target);
	public void SetFleeing(Node3D target) => SetBehavior(BehaviorState.Fleeing, target);
	public void SetDead() => SetBehavior(BehaviorState.Dead);

	public override void _PhysicsProcess(double delta)
	{
		if (_target == null)
		{
			return; // No target to chase
		}

		// Get the target's position
		Vector3 targetPosition = _target.GlobalTransform.Origin;

		// Move toward the target
		if (_movementComponent != null)
		{
			_movementComponent.MoveTo(targetPosition);
		}
	}

	private void UpdateAnimation()
	{
		string targetAnimation = CurrentAction != ActionState.None
			? ActionAnimations[CurrentAction]
			: BehaviorAnimations[CurrentBehavior];

		_animationStateMachine.Travel(targetAnimation);
	}

	public void SetBehavior(BehaviorState newBehavior, Node3D target = null)
	{
		if (CurrentBehavior == BehaviorState.Dead)
		{
			return;
		}

		SetTarget(target);
		if (CurrentBehavior != newBehavior)
		{
			GD.Print($"{_owner.Name} is now {newBehavior}");
			CurrentBehavior = newBehavior;
			if (CurrentAction == ActionState.None)
			{
				UpdateAnimation();
			}
		}
	}

	public void SetAction(ActionState newAction)
	{
		if (CurrentBehavior == BehaviorState.Dead)
		{
			return;
		}

		if (CurrentAction != newAction)
		{
			CurrentAction = newAction;
			UpdateAnimation();
		}
	}

	public void SetTarget(Node3D target)
	{
		_target = target;
	}

	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		// Prevent taking damage if already dead
		if (CurrentBehavior == BehaviorState.Dead)
		{
			return;
		}

		_currentHitPoints -= amount;

		SetAction(ActionState.Hit);
		SpawnHitEffect();

		// Push the enemy away from the attacker
		_movementComponent.Push(attackDirection, 5.0f);

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

	private void Die()
	{
		SetAction(ActionState.Dying);
		SetBehavior(BehaviorState.Dead);

		// Stop movement immediately
		((Enemy)GetParent()).Velocity = Vector3.Zero;

		// Wait for death animation to finish
		GetTree().CreateTimer(1.0f).Connect("timeout", Callable.From(() =>
		{
			GD.Print($"{GetParent().Name} is destroyed!");
			GetParent().QueueFree();
		}));
	}

	private void SpawnHitEffect()
	{
		if (HitEffect == null)
		{
			GD.PrintErr("HitEffectScene is not set!");
			return;
		}

		var hitEffect = HitEffect.Instantiate<GpuParticles3D>();
		hitEffect.GlobalTransform = _owner.GlobalTransform; // Position the effect at the enemy's location
		hitEffect.OneShot = true;

		// Add to the scene
		GetParent().GetParent().AddChild(hitEffect);
	}
}
