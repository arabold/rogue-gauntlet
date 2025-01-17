using Godot;
using Godot.Collections;

public enum EnemyBehaviorState
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

public enum EnemyAction
{
	None,
	Spawning,
	StandingUp,
	Hit,
	Attacking,
	Dying,
}

public partial class EnemyBehavior : Node
{
	// FIXME: Remove this hardcoded dictionary and use a data-driven approach
	private static readonly Dictionary<EnemyAction, float> ActionDurations = new()
	{
		{ EnemyAction.None, 0 },
		{ EnemyAction.Spawning, 3.5f },
		{ EnemyAction.StandingUp, 2.33f },
		{ EnemyAction.Hit, 0.66f },
		{ EnemyAction.Attacking, 1.0f },
		{ EnemyAction.Dying, 2.0f },
	};

	[Export] public CharacterBody3D Actor { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public HealthComponent HealthComponent { get; set; }
	[Export] public EnemyBehaviorState CurrentBehavior { get; private set; } = EnemyBehaviorState.Idle;
	[Export] public EnemyAction CurrentAction { get; private set; } = EnemyAction.Spawning;
	[Export] public float DetectionRange { get; set; } = 20.0f;
	[Export] public float DetectionAngle { get; set; } = 45.0f;

	// We can use these properties to automatically transition between animation states
	public bool IsSleeping => CurrentBehavior == EnemyBehaviorState.Sleeping;
	public bool IsMoving => MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsDead => CurrentAction == EnemyAction.Dying || CurrentBehavior == EnemyBehaviorState.Dead;
	public bool IsAttacking => CurrentAction == EnemyAction.Attacking;
	public bool IsHit => CurrentAction == EnemyAction.Hit || MovementComponent.IsPushed;
	public bool IsSpawning => CurrentAction == EnemyAction.Spawning;

	/// <summary>
	/// The target node that the enemy is chasing
	/// </summary>
	public Node3D Target { get; private set; } = null;

	private RayCast3D _sightRay;
	private NavigationAgent3D _navigationAgent;
	private float _remainingActionTime = 0;
	private Vector3 _lastKnownTargetPosition;

	public override void _Ready()
	{
		base._Ready();

		_sightRay = GetNode<RayCast3D>("SightRay");
		_navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");

		// Ensure to properly initialize the enemy's state with the current selection
		GD.Print($"{GetParent().Name} is initialized with {CurrentBehavior} and {CurrentAction}");
		_remainingActionTime = ActionDurations[CurrentAction];

		if (HealthComponent != null)
		{
			HealthComponent.Died += OnDie;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (CurrentAction != EnemyAction.None)
		{
			// Update the remaining action time
			_remainingActionTime -= (float)delta;
			if (_remainingActionTime <= 0)
			{
				SetAction(EnemyAction.None);
			}
		}
		else
		{
			// Main behavior logic goes here...
			if (CurrentBehavior == EnemyBehaviorState.Idle)
			{
				// Check if the target is within range and start chasing
				if (LookForNewTarget())
				{
					SetBehavior(EnemyBehaviorState.Chasing);
				}
			}
			else if (CurrentBehavior == EnemyBehaviorState.Searching)
			{
				if (LookForNewTarget())
				{
					SetBehavior(EnemyBehaviorState.Chasing);
				}
				else
				{
					// Go to last known position
					NavigateToTarget();
				}
			}
			else if (CurrentBehavior == EnemyBehaviorState.Chasing)
			{
				UpdateTargetPosition();
				NavigateToTarget();
			}
			else if (CurrentBehavior == EnemyBehaviorState.Fleeing)
			{
				UpdateTargetPosition();
				NavigateAwayFromTarget();
			}
		}
	}

	private bool TestLineOfSight(Node3D node)
	{
		Vector3 endPoint = node.GlobalPosition;
		Vector3 direction = (endPoint - Actor.GlobalPosition).Normalized();

		Vector3 forward = -Actor.GlobalTransform.Basis.Z;
		float angle = Mathf.RadToDeg(Mathf.Acos(forward.Normalized().Dot(direction)));
		if (angle > DetectionAngle)
		{
			return false;
		}

		var space = _sightRay.GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(
			Actor.GlobalPosition,
			endPoint,
			_sightRay.CollisionMask);
		var result = space.IntersectRay(query);
		if (result.Count == 0)
		{
			return false;
		}
		if (result["collider"].Obj == node)
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// Look for a new target in the scene
	/// </summary>
	private bool LookForNewTarget()
	{
		if (Target == null)
		{
			var players = GameManager.Instance.PlayersInScene;
			foreach (var player in players)
			{
				if (player.IsDead) continue;

				var distance = Actor.GlobalPosition.DistanceTo(player.GlobalPosition);
				if (distance > 0 && distance <= DetectionRange)
				{
					if ((distance < DetectionRange / 2) || TestLineOfSight(player))
					{
						GD.Print($"{Actor.Name} has spotted {player.Name}");
						UpdateTargetPosition();
						SetTarget(player);
						return true;
					}
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Check if the enemy is near the target
	/// </summary>
	private bool IsNearTarget()
	{
		if (Target == null)
		{
			return false;
		}

		float distance = Actor.GlobalPosition.DistanceTo(Target.GlobalPosition);
		return distance < 1.0f;
	}

	/// <summary>
	/// Update the last known target position
	/// </summary>
	private bool UpdateTargetPosition()
	{
		if (Target == null)
		{
			return false;
		}
		_lastKnownTargetPosition = Target.GlobalPosition;
		_navigationAgent.SetTargetPosition(_lastKnownTargetPosition);
		return true;
	}

	/// <summary>
	/// Navigate to the last known target position (i.e. chase the target)
	/// </summary>
	private void NavigateToTarget()
	{
		if (Target == null)
		{
			return;
		}

		// Move to the next path position
		Vector3 destination = _navigationAgent.GetNextPathPosition();
		Vector3 localDestination = destination - Actor.GlobalPosition;
		var direction = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
		MovementComponent.SetInputDirection(direction);
		// _targetDirection = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
	}

	/// <summary>
	/// Navigate away from the last known target position (i.e. flee from the target)
	/// </summary>
	private void NavigateAwayFromTarget()
	{
		if (Target == null)
		{
			return;
		}

		Vector3 destination = _navigationAgent.GetNextPathPosition();
		Vector3 localDestination = destination - Actor.GlobalPosition;
		var direction = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
		MovementComponent.SetInputDirection(-direction);
	}

	public void SetBehavior(EnemyBehaviorState newBehavior)
	{
		if (CurrentBehavior != newBehavior)
		{
			GD.Print($"{Actor.Name} is now {newBehavior}");
			CurrentBehavior = newBehavior;
		}
	}

	public void SetTarget(Node3D target)
	{
		if (Target != target)
		{
			GD.Print($"{Actor.Name} is now targeting {target.Name}");
			Target = target;
		}
		UpdateTargetPosition();
	}

	public void SetAction(EnemyAction newAction)
	{
		if (CurrentAction != newAction)
		{
			GD.Print($"{Actor.Name} is performing {newAction}");
			CurrentAction = newAction;
			_remainingActionTime = ActionDurations[newAction];
		}
	}

	public void OnHit()
	{
		SetAction(EnemyAction.Hit);
	}

	public void OnDie()
	{
		SetAction(EnemyAction.Dying);
		SetBehavior(EnemyBehaviorState.Dead);
		MovementComponent.Stop();
	}
}
