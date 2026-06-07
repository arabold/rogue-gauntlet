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
	private const float StuckCheckInterval = 0.25f;
	private const float StuckMinimumProgress = 0.2f;
	private const float StuckRecoverySeconds = 1.0f;

	// How far the desired heading must point into a wall normal before we slide along that wall.
	// Filters floor/ceiling contacts and grazing touches so straight movement is untouched.
	private const float WallSlideEngageDot = -0.05f;
	// Minimum squared length of the wall-projected heading we trust. Below this the actor is
	// wedged in a concave corner with no clear tangent, so we keep the original heading and let
	// stuck-recovery repath instead of snapping to an unstable sliver at full speed.
	private const float WallSlideMinLengthSquared = 0.04f;
	private const float TargetScanInterval = 0.2f;
	private const float TargetPathRefreshInterval = 0.12f;
	private const float TargetReachabilityCheckInterval = 0.35f;
	private const float TargetPositionRefreshDistance = 0.35f;

	// Horizontal distance from the baked navmesh beyond which the actor is treated as crossing a
	// doorway link. agent_radius keeps a navigating body flush with the mesh everywhere else, so
	// only the gap a door's NavigationLink3D spans pushes it this far off-mesh.
	private const float DoorwayCrossingOffMeshDistance = 0.6f;

	// How close a navigation path must end to the target's projected navmesh point for the
	// target to count as reachable. A blocked target yields a path that stops short of this.
	private const float ReachabilityThreshold = 1.5f;

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
	private Vector3 _lastStuckCheckPosition;
	private float _stuckCheckTimer;
	private float _stuckTime;
	private float _remainingSearchTime;
	private float _targetScanTimer;
	private float _targetPathRefreshTimer;
	private float _targetReachabilityTimer;
	private bool _lastTargetReachable = true;

	public override void _Ready()
	{
		base._Ready();

		_sightRay = GetNode<RayCast3D>("SightRay");
		_navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");
		_profile = Profile ?? new EnemyBehaviorProfile();
		CurrentBehavior = _profile.InitialBehavior;
		CurrentAction = _profile.InitialAction;
		_homePosition = Actor.GlobalPosition;
		_lastStuckCheckPosition = Actor.GlobalPosition;
		_remainingRoamPauseTime = RandomRange(0, _profile.RoamPauseMax);

		_attackController = Actor.GetNodeOrNull<AttackController>("AttackController");
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} has no AttackController child; melee attacks will not deal damage.");
		}
		// Ensure to properly initialize the enemy's state with the current selection
		GameDebug.Ai($"{GetParent().Name} is initialized with {CurrentBehavior} and {CurrentAction}");
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
		_targetScanTimer -= (float)delta;
		_targetPathRefreshTimer -= (float)delta;
		_targetReachabilityTimer -= (float)delta;

		if (CurrentAction != EnemyAction.None)
		{
			MovementComponent.Stop();
			ResetStuckTracking();

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
				ResetStuckTracking();

				// Check if the target is within range and start chasing
				if (ShouldScanForTarget() && LookForNewTarget())
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
				if (ShouldScanForTarget() && LookForNewTarget())
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
				_remainingSearchTime -= (float)delta;

				if (ShouldScanForTarget() && LookForNewTarget())
				{
					SetBehavior(EnemyBehaviorState.Chasing);
				}
				else if (_remainingSearchTime <= 0 || _navigationAgent.IsNavigationFinished())
				{
					// Reached the last known position or gave up; resume normal patrol.
					SetBehavior(EnemyBehaviorState.Patrolling);
				}
				else
				{
					// Walk to the last known target position set when the chase was lost.
					NavigateAlongPath();
				}
			}
			else if (CurrentBehavior == EnemyBehaviorState.Chasing)
			{
				// While crossing a doorway link the actor is briefly off the navmesh (the link spans
				// the doorway gap rather than filling it). Repathing or reachability-testing from an
				// off-mesh position snaps the path start to whichever doorway side is nearest and
				// flips it as the actor inches across, so the enemy oscillates in the doorway. Freeze
				// those decisions until it lands back on the mesh so it commits to the crossing; it
				// keeps following its already-computed path, which routes through the link.
				bool crossingDoorway = IsCrossingDoorway();

				if (!crossingDoorway && !CanReachCurrentTarget())
				{
					StartSearching();
					return;
				}

				if (!crossingDoorway)
				{
					UpdateTargetPositionThrottled();
				}

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
				UpdateTargetPositionThrottled();
				NavigateAwayFromTarget();
				ResetStuckTracking();
			}
		}
	}

	private bool TestLineOfSight(Node3D node)
	{
		Vector3 endPoint = node.GlobalPosition;
		Vector3 direction = (endPoint - Actor.GlobalPosition).Normalized();

		Vector3 forward = -Actor.GlobalTransform.Basis.Z;
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
		_targetScanTimer = TargetScanInterval;
		if (Target == null)
		{
			var players = GetTree().GetNodesInGroup("player").OfType<Player>();
			foreach (var player in players)
			{
				if (player.IsDead) continue;

				var distance = Actor.GlobalPosition.DistanceTo(player.GlobalPosition);
				if (distance > 0 && distance <= _profile.DetectionRange)
				{
					bool isClose = distance < _profile.DetectionRange * _profile.CloseDetectionRangeMultiplier;
					if (CanReachTarget(player) && (isClose || TestLineOfSight(player)))
					{
						GameDebug.Ai($"{Actor.Name} has spotted {player.Name}");
						UpdateTargetPosition();
						SetTarget(player);
						return true;
					}
				}
			}
		}
		return false;
	}

	private bool ShouldScanForTarget()
	{
		return Target == null && _targetScanTimer <= 0.0f;
	}

	private bool CanReachCurrentTarget()
	{
		if (_targetReachabilityTimer > 0.0f)
		{
			return _lastTargetReachable;
		}

		_targetReachabilityTimer = TargetReachabilityCheckInterval;
		_lastTargetReachable = CanReachTarget(Target);
		return _lastTargetReachable;
	}

	private bool CanReachTarget(Node3D target)
	{
		return target != null && IsReachableByNavmesh(target.GlobalPosition);
	}

	/// <summary>
	/// Tests reachability against the baked navigation mesh, which already reflects door
	/// state (each open door enables a <see cref="NavigationLink3D"/> across its doorway, closed
	/// doors leave the doorway severed). A blocked target yields a path that stops short of it, so
	/// we compare the path's end against the target's projected navmesh point. This query is
	/// side-effect free and does not disturb the agent's current chase or roam path.
	/// </summary>
	private bool IsReachableByNavmesh(Vector3 targetPosition)
	{
		Rid navigationMap = Actor.GetWorld3D().NavigationMap;
		if (!navigationMap.IsValid)
		{
			return true;
		}

		Vector3[] path = NavigationServer3D.MapGetPath(
			navigationMap, Actor.GlobalPosition, targetPosition, true);
		if (path.Length == 0)
		{
			return false;
		}

		Vector3 mappedTarget = NavigationServer3D.MapGetClosestPoint(navigationMap, targetPosition);
		return path[^1].DistanceTo(mappedTarget) <= ReachabilityThreshold;
	}

	/// <summary>
	/// True while the actor is mid-doorway, crossing the gap a door's <see cref="NavigationLink3D"/>
	/// spans. Detected by horizontal distance to the nearest navmesh point: agent_radius keeps a
	/// navigating body flush with the mesh everywhere else, so only a doorway link pushes it past
	/// <see cref="DoorwayCrossingOffMeshDistance"/>. Chasing uses this to commit to the crossing.
	/// </summary>
	private bool IsCrossingDoorway()
	{
		Rid navigationMap = Actor.GetWorld3D().NavigationMap;
		if (!navigationMap.IsValid)
		{
			return false;
		}

		Vector3 closest = NavigationServer3D.MapGetClosestPoint(navigationMap, Actor.GlobalPosition);
		return HorizontalDistance(Actor.GlobalPosition, closest) > DoorwayCrossingOffMeshDistance;
	}

	/// <summary>
	/// Drops the current target and investigates its last known position for a while before
	/// returning to patrol, so a player that breaks contact (or closes a door) is pursued to
	/// where they were last seen instead of being forgotten instantly.
	/// </summary>
	private void StartSearching()
	{
		MovementComponent.Stop();
		Target = null;
		_lastTargetReachable = true;
		_remainingSearchTime = _profile.SearchDuration;
		_navigationAgent.SetTargetPosition(_lastKnownTargetPosition);
		SetBehavior(EnemyBehaviorState.Searching);
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

	private bool UpdateTargetPositionThrottled()
	{
		if (Target == null)
		{
			return false;
		}

		if (_targetPathRefreshTimer > 0.0f
			&& _lastKnownTargetPosition.DistanceTo(Target.GlobalPosition) < TargetPositionRefreshDistance)
		{
			return true;
		}

		_targetPathRefreshTimer = TargetPathRefreshInterval;
		return UpdateTargetPosition();
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
			ResetStuckTracking();
			return;
		}

		// Move to the next path position
		Vector3 destination = _navigationAgent.GetNextPathPosition();
		Vector3 localDestination = destination - Actor.GlobalPosition;
		var direction = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
		if (direction == Vector3.Zero)
		{
			MovementComponent.Stop();
			ResetStuckTracking();
			return;
		}

		direction = SteerAlongWalls(direction);
		MovementComponent.SetInputDirection(direction);
		UpdateStuckTracking();
	}

	/// <summary>
	/// Deflects a desired horizontal heading along any wall the actor is pressing into so it
	/// follows the wall at full speed toward its path waypoint instead of grinding to a crawl at
	/// corners. Uses the previous physics frame's slide collisions (movement runs after this
	/// component). Returns the heading unchanged when nothing is blocking it or when the actor is
	/// wedged in a concave corner with no usable tangent.
	/// </summary>
	private Vector3 SteerAlongWalls(Vector3 desired)
	{
		Vector3 deflected = desired;
		int collisionCount = Actor.GetSlideCollisionCount();
		for (int i = 0; i < collisionCount; i++)
		{
			KinematicCollision3D collision = Actor.GetSlideCollision(i);

			// Never slide along the chase target itself, or the enemy would orbit the player.
			if (collision.GetCollider() == Target)
			{
				continue;
			}

			Vector3 normal = collision.GetNormal();
			normal.Y = 0;
			if (normal.LengthSquared() < 0.0001f)
			{
				// Floor or ceiling contact, not a wall.
				continue;
			}
			normal = normal.Normalized();

			float into = deflected.Dot(normal);
			if (into < WallSlideEngageDot)
			{
				// Remove the component pushing into the wall, leaving the tangent along it.
				deflected -= normal * into;
			}
		}

		if (deflected.LengthSquared() < WallSlideMinLengthSquared)
		{
			return desired;
		}

		return deflected;
	}

	private void UpdateStuckTracking()
	{
		_stuckCheckTimer += (float)GetPhysicsProcessDeltaTime();
		if (_stuckCheckTimer < StuckCheckInterval)
		{
			return;
		}

		float progress = HorizontalDistance(Actor.GlobalPosition, _lastStuckCheckPosition);
		_stuckTime = progress < StuckMinimumProgress ? _stuckTime + _stuckCheckTimer : 0;
		_lastStuckCheckPosition = Actor.GlobalPosition;
		_stuckCheckTimer = 0;

		if (_stuckTime >= StuckRecoverySeconds)
		{
			RecoverFromStuckPath();
		}
	}

	private void RecoverFromStuckPath()
	{
		GameDebug.Ai($"{Actor.Name} appears stuck while {CurrentBehavior}; refreshing path.");
		ResetStuckTracking();

		if (CurrentBehavior == EnemyBehaviorState.Patrolling)
		{
			_hasRoamTarget = false;
			StartRoamPause();
			MovementComponent.Stop();
			return;
		}

		if (CurrentBehavior == EnemyBehaviorState.Searching || CurrentBehavior == EnemyBehaviorState.Chasing)
		{
			_navigationAgent.SetTargetPosition(_lastKnownTargetPosition);
		}
	}

	private void ResetStuckTracking()
	{
		_lastStuckCheckPosition = Actor.GlobalPosition;
		_stuckCheckTimer = 0;
		_stuckTime = 0;
	}

	private static float HorizontalDistance(Vector3 a, Vector3 b)
	{
		return new Vector2(a.X, a.Z).DistanceTo(new Vector2(b.X, b.Z));
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
			GameDebug.Ai($"{Actor.Name} is now {newBehavior}");
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
			GameDebug.Ai($"{Actor.Name} is now targeting {target.Name}");
			Target = target;
		}
		UpdateTargetPosition();
	}

	public void SetAction(EnemyAction newAction)
	{
		if (CurrentAction != newAction)
		{
			GameDebug.Ai($"{Actor.Name} is performing {newAction}");
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
		def.HitBoxOffset = new Vector3(0.0f, 1.0f, -1.1f);
		return def;
	}

	public void OnDie()
	{
		SetAction(EnemyAction.Dying);
		SetBehavior(EnemyBehaviorState.Dead);
		MovementComponent.Stop();
	}
}
