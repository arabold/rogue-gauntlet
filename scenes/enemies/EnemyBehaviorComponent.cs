using Godot;
using System.Linq;

/// <summary>
/// High-level behavior states available to enemy AI controllers.
/// </summary>
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

/// <summary>
/// Timed enemy actions that temporarily interrupt normal behavior decisions.
/// </summary>
public enum EnemyAction
{
	None,
	Spawning,
	StandingUp,
	Hit,
	MeeleAttack,
	RangedAttack,
	Dying,
}

/// <summary>
/// Coordinates enemy target acquisition, roaming, navigation, and attacks.
/// </summary>
/// <remarks>
/// Monster-specific tuning lives in <see cref="EnemyBehaviorProfile"/> resources so new enemy
/// scenes can vary behavior without duplicating this controller.
/// </remarks>
public partial class EnemyBehaviorComponent : Node
{
	/// <summary>
	/// Character body moved and rotated by this behavior controller.
	/// </summary>
	[Export] public CharacterBody3D Actor { get; set; }

	/// <summary>
	/// Movement component used to apply navigation directions.
	/// </summary>
	[Export] public MovementComponent MovementComponent { get; set; }

	/// <summary>
	/// Health component observed for death transitions.
	/// </summary>
	[Export] public HealthComponent HealthComponent { get; set; }

	/// <summary>
	/// Authored behavior tuning for this enemy type.
	/// </summary>
	[Export] public EnemyBehaviorProfile Profile { get; set; }

	public EnemyBehaviorState CurrentBehavior { get; private set; } = EnemyBehaviorState.Idle;
	public EnemyAction CurrentAction { get; private set; } = EnemyAction.Spawning;

	// We can use these properties to automatically transition between animation states
	public bool IsSleeping => CurrentBehavior == EnemyBehaviorState.Sleeping;
	public bool IsMoving => CurrentAction == EnemyAction.None && MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsDead => CurrentAction == EnemyAction.Dying || CurrentBehavior == EnemyBehaviorState.Dead;
	public bool IsAttacking => CurrentAction == EnemyAction.MeeleAttack || CurrentAction == EnemyAction.RangedAttack;
	public bool IsMeleeAttack => CurrentAction == EnemyAction.MeeleAttack;
	public bool IsRangedAttack => CurrentAction == EnemyAction.RangedAttack;
	public bool IsHit => MovementComponent.IsPushed;
	public bool IsSpawning => CurrentAction == EnemyAction.Spawning;

	/// <summary>
	/// The target node that the enemy is chasing
	/// </summary>
	public Node3D Target { get; private set; } = null;

	private RayCast3D _sightRay;
	private NavigationAgent3D _navigationAgent;
	private EnemyBehaviorProfile _profile;
	private float _remainingActionTime = 0;
	private Vector3 _lastKnownTargetPosition;
	private Vector3 _homePosition;
	private Vector3 _roamTargetPosition;
	private bool _hasRoamTarget;
	private float _remainingRoamPauseTime;
	private AttackController _attackController;

	public override void _Ready()
	{
		base._Ready();

		_sightRay = GetNode<RayCast3D>("SightRay");
		_navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");
		_profile = Profile ?? new EnemyBehaviorProfile();
		CurrentBehavior = _profile.InitialBehavior;
		CurrentAction = _profile.InitialAction;
		_homePosition = Actor.GlobalPosition;
		_remainingRoamPauseTime = RandomRange(0, _profile.RoamPauseMax);

		_attackController = Actor.GetNodeOrNull<AttackController>("AttackController");
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} has no AttackController child; melee attacks will not deal damage.");
		}
		else
		{
			_attackController.DebugDrawEnabled = true;
		}

		// Ensure to properly initialize the enemy's state with the current selection
		GD.Print($"{GetParent().Name} is initialized with {CurrentBehavior} and {CurrentAction}");
		_remainingActionTime = _profile.GetActionDuration(CurrentAction);

		if (HealthComponent != null)
		{
			this.SubscribeUntilExit(
				HealthComponent,
				healthComponent => healthComponent.Died += OnDie,
				healthComponent => healthComponent.Died -= OnDie);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (CurrentAction != EnemyAction.None)
		{
			MovementComponent.Stop();

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
				else
				{
					SetBehavior(EnemyBehaviorState.Patrolling);
				}
			}
			else if (CurrentBehavior == EnemyBehaviorState.Patrolling)
			{
				if (LookForNewTarget())
				{
					SetBehavior(EnemyBehaviorState.Chasing);
				}
				else
				{
					Roam(delta);
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

				if (IsNearTarget())
				{
					// Attack the target
					SetAction(EnemyAction.MeeleAttack);
					TriggerMeleeAttack();
				}
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

		Vector3 forward = Actor.GlobalTransform.Basis.Z;
		float angle = Mathf.RadToDeg(Mathf.Acos(forward.Normalized().Dot(direction)));
		if (angle > _profile.DetectionAngle)
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
			var players = GetTree().GetNodesInGroup("player").OfType<Player>();
			foreach (var player in players)
			{
				if (player.IsDead) continue;

				var distance = Actor.GlobalPosition.DistanceTo(player.GlobalPosition);
				if (distance > 0 && distance <= _profile.DetectionRange)
				{
					if ((distance < _profile.DetectionRange * _profile.CloseDetectionRangeMultiplier) || TestLineOfSight(player))
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
		return distance < _profile.MeleeAttackRange;
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

	private void Roam(double delta)
	{
		if (_remainingRoamPauseTime > 0)
		{
			_remainingRoamPauseTime -= (float)delta;
			MovementComponent.Stop();
			return;
		}

		if (!_hasRoamTarget)
		{
			if (!TrySetRoamTarget())
			{
				StartRoamPause();
				MovementComponent.Stop();
				return;
			}
		}

		if (_navigationAgent.IsNavigationFinished()
			|| Actor.GlobalPosition.DistanceTo(_roamTargetPosition) <= _profile.RoamTargetDistance)
		{
			_hasRoamTarget = false;
			StartRoamPause();
			MovementComponent.Stop();
			return;
		}

		NavigateAlongPath();
	}

	private bool TrySetRoamTarget()
	{
		for (int i = 0; i < _profile.RoamTargetAttempts; i++)
		{
			float angle = RandomRange(0, Mathf.Pi * 2.0f);
			float distance = RandomRange(_profile.RoamRadius * 0.25f, _profile.RoamRadius);
			Vector3 offset = new(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
			Vector3 targetPosition = NavigationServer3D.MapGetClosestPoint(
				Actor.GetWorld3D().NavigationMap,
				_homePosition + offset);

			if (targetPosition.DistanceTo(_homePosition) > _profile.RoamRadius
				|| targetPosition.DistanceTo(Actor.GlobalPosition) <= _profile.RoamTargetDistance)
			{
				continue;
			}

			_roamTargetPosition = targetPosition;
			_hasRoamTarget = true;
			_navigationAgent.SetTargetPosition(_roamTargetPosition);
			return true;
		}

		return false;
	}

	private void StartRoamPause()
	{
		_remainingRoamPauseTime = RandomRange(_profile.RoamPauseMin, _profile.RoamPauseMax);
	}

	private static float RandomRange(float min, float max)
	{
		return min + ((float)GD.Randf() * (max - min));
	}

	/// <summary>
	/// Navigate to the last known target position (i.e. chase the target)
	/// </summary>
	private void NavigateToTarget()
	{
		if (Target == null)
		{
			MovementComponent.Stop();
			return;
		}

		NavigateAlongPath();
	}

	private void NavigateAlongPath()
	{
		if (_navigationAgent.IsNavigationFinished())
		{
			MovementComponent.Stop();
			return;
		}

		// Move to the next path position
		Vector3 destination = _navigationAgent.GetNextPathPosition();
		Vector3 localDestination = destination - Actor.GlobalPosition;
		var direction = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
		if (direction == Vector3.Zero)
		{
			MovementComponent.Stop();
			return;
		}

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
			MovementComponent.Stop();
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
			if (newBehavior != EnemyBehaviorState.Patrolling)
			{
				_hasRoamTarget = false;
			}
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
			_remainingActionTime = _profile.GetActionDuration(newAction);
			if (newAction != EnemyAction.None)
			{
				MovementComponent.Stop();
			}

			// Cancel attack hitbox if transitioning out of attack, getting hit, or dying
			if (newAction == EnemyAction.None || newAction == EnemyAction.Hit || newAction == EnemyAction.Dying)
			{
				_attackController?.CancelAttack();
			}
		}
	}

	private void TriggerMeleeAttack()
	{
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} cannot start melee attack without AttackController.");
			return;
		}

		var def = _profile.MeleeAttackDefinition ?? CreateDefaultMeleeAttackDefinition();

		uint targetMask = 4; // Targets player (HurtBoxComponent is on Layer 3 / Mask 4)

		_attackController.StartAttack(
			def,
			_profile.MeleeAttackMinDamage,
			_profile.MeleeAttackMaxDamage,
			_profile.MeleeAttackAccuracy,
			_profile.MeleeAttackCritChance,
			targetMask
		);
	}

	private AttackDefinition CreateDefaultMeleeAttackDefinition()
	{
		var def = new AttackDefinition();
		def.AnimationId = "melee_attack";

		float duration = _profile.GetActionDuration(EnemyAction.MeeleAttack);
		def.HitWindowStart = 0.3f * duration;
		def.HitWindowEnd = 0.7f * duration;

		def.AttachHitBoxToWeapon = false;
		def.HitBoxSize = new Vector3(1.5f, 2.2f, 2.2f);
		def.HitBoxOffset = new Vector3(0.0f, 1.0f, 1.1f);
		return def;
	}

	public void OnDie()
	{
		SetAction(EnemyAction.Dying);
		SetBehavior(EnemyBehaviorState.Dead);
		MovementComponent.Stop();
	}
}
