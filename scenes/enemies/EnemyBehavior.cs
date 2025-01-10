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

	private static readonly Dictionary<EnemyAction, float> ActionDurations = new()
	{
		{ EnemyAction.None, 0 },
		{ EnemyAction.Spawning, 3.5f },
		{ EnemyAction.StandingUp, 2.33f },
		{ EnemyAction.Hit, 0.66f },
		{ EnemyAction.Attacking, 1.0f },
		{ EnemyAction.Dying, 2.0f },
	};

	private Node3D _parent;
	private MovementComponent _movementComponent;
	private RayCast3D _sightRay;
	private float _remainingActionTime = 0;
	private Vector3 _lastKnownTargetPosition = Vector3.Zero;

	[Export] public EnemyBehaviorState CurrentBehavior { get; private set; } = EnemyBehaviorState.Idle;
	[Export] public EnemyAction CurrentAction { get; private set; } = EnemyAction.Spawning;
	[Export] public Node3D Target { get; private set; } = null;
	[Export] public float DetectionRange { get; set; } = 20.0f;
	[Export] public float DetectionAngle { get; set; } = 45.0f;

	// We can use these properties to automatically transition between animation states
	public bool IsSleeping => CurrentBehavior == EnemyBehaviorState.Sleeping;
	public bool IsMoving => _movementComponent.IsMoving;
	public bool IsFalling => _movementComponent.IsFalling;
	public bool IsDead => CurrentAction == EnemyAction.Dying || CurrentBehavior == EnemyBehaviorState.Dead;
	public bool IsAttacking => CurrentAction == EnemyAction.Attacking;
	public bool IsHit => CurrentAction == EnemyAction.Hit || _movementComponent.IsPushed;
	public bool IsSpawning => CurrentAction == EnemyAction.Spawning;

	public override void _Ready()
	{
		base._Ready();

		_parent = GetParent<Node3D>(); // Assume the parent is the Enemy node
		_movementComponent = _parent.GetNode<MovementComponent>("MovementComponent");
		_sightRay = GetNode<RayCast3D>("SightRay");

		// Ensure to properly initialize the enemy's state with the current selection
		GD.Print($"{_parent.Name} is initialized with {CurrentBehavior} and {CurrentAction}");
		_remainingActionTime = ActionDurations[CurrentAction];
	}

	public override void _Process(double delta)
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
		var space = _sightRay.GetWorld3D().DirectSpaceState;
		float distance = _parent.GlobalTransform.Origin.DistanceTo(node.GlobalTransform.Origin);
		if (distance > DetectionRange)
		{
			return false;
		}

		Vector3 direction = (node.GlobalTransform.Origin - _parent.GlobalTransform.Origin).Normalized();
		Vector3 endPoint = _parent.GlobalTransform.Origin + direction * Mathf.Min(distance, DetectionRange);

		Vector3 forward = -_parent.GlobalTransform.Basis.Z;
		float angle = Mathf.RadToDeg(Mathf.Acos(forward.Normalized().Dot(direction)));
		if (angle > DetectionAngle)
		{
			return false;
		}

		var query = PhysicsRayQueryParameters3D.Create(
			_parent.GlobalTransform.Origin,
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
				var distance = _parent.GlobalTransform.Origin.DistanceTo(player.GlobalTransform.Origin);
				if (distance > 0 && distance < DetectionRange && !player.IsDead)
				{
					// Target is very close, engage right away
					if (distance < DetectionRange / 2)
					{
						UpdateTargetPosition();
						SetTarget(player);
						return true;
					}
					// Check if the player is in line of sight
					else if (TestLineOfSight(player))
					{
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

		float distance = _parent.GlobalTransform.Origin.DistanceTo(Target.GlobalTransform.Origin);
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
		_lastKnownTargetPosition = Target.GlobalTransform.Origin;
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
		if (_movementComponent != null)
		{
			_movementComponent.NavigateTo(_lastKnownTargetPosition);
		}
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
		if (_movementComponent != null)
		{
			_movementComponent.NavigateTo(-_lastKnownTargetPosition);
		}
	}

	public void SetBehavior(EnemyBehaviorState newBehavior)
	{
		if (CurrentBehavior != newBehavior)
		{
			GD.Print($"{_parent.Name} is now {newBehavior}");
			CurrentBehavior = newBehavior;
		}
	}

	public void SetTarget(Node3D target)
	{
		if (Target != target)
		{
			GD.Print($"{_parent.Name} is now targeting {target.Name}");
			Target = target;
		}
		if (Target != null)
		{
			// Udpate the last known target position
			_lastKnownTargetPosition = Target.GlobalTransform.Origin;
		}
	}

	public void SetAction(EnemyAction newAction)
	{
		if (CurrentAction != newAction)
		{
			GD.Print($"{_parent.Name} is performing {newAction}");
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
	}
}
