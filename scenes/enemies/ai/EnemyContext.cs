using Godot;
using System;

/// <summary>
/// Shared blackboard and service references handed to every <see cref="IEnemyState"/>. It owns the
/// data that has to outlive a single state - the current target, the last position it was seen at,
/// and the roam anchor - plus the throttle timers and the small query helpers states use to talk to
/// the perception and navigation components.
/// </summary>
/// <remarks>
/// States read and mutate this context; the host <see cref="EnemyBehaviorComponent"/> ticks the
/// throttle timers each physics frame and bridges the timed-action layer through
/// <see cref="RequestAttack"/> so states never poke <c>CurrentAction</c> directly. Detection
/// and locomotion themselves live in the perception/navigation components - this only coordinates
/// them; see docs/level-design.md for the authoritative behavior description.
/// </remarks>
public sealed class EnemyContext
{
	// How often a target-less enemy re-scans its surroundings for a player to chase.
	private const float TargetScanInterval = 0.2f;
	// How often a chase repaths to the target's live position (throttled so we don't repath every
	// frame); also gated by TargetPositionRefreshDistance below.
	private const float TargetPathRefreshInterval = 0.12f;
	// How often the (relatively expensive) navmesh reachability query for the current target runs.
	private const float TargetReachabilityCheckInterval = 0.35f;
	// Minimum distance the target must have moved before an off-schedule repath is worthwhile.
	private const float TargetPositionRefreshDistance = 0.35f;

	public CharacterBody3D Actor { get; }
	public MovementComponent Movement { get; }
	public PerceptionComponent Perception { get; }
	public NavigationComponent Navigation { get; }
	public EnemyBehaviorProfile Profile { get; }
	public EnemyTacticsState Tactics { get; } = new();
	public EnemyTacticalSelector TacticalSelector { get; }
	private EnemyTacticalDecision? _lastDecision;
	public string TacticalDecisionReason => _lastDecision?.Reason ?? "inactive";
	public double Delta { get; private set; }
	private float _retreatRefresh;
	private bool _retreatDestinationValid;
	private float _ringRefresh;
	private Vector3 _ringPoint;
	public float HorizontalTargetDistance
	{
		get
		{
			if (Target == null) return float.PositiveInfinity;
			var offset = Target.GlobalPosition - Actor.GlobalPosition;
			offset.Y = 0;
			return offset.Length();
		}
	}

	public Action RequestAttack { get; }

	/// <summary>Degrees between the actor's facing and the horizontal direction to its target; 0 without a target.</summary>
	public float AngleToTarget
	{
		get
		{
			if (Target == null) return 0;
			Vector3 toTarget = Target.GlobalPosition - Actor.GlobalPosition;
			toTarget.Y = 0;
			Vector3 facing = Movement.LookAtDirection;
			facing.Y = 0;
			if (toTarget.LengthSquared() < .0001f || facing.LengthSquared() < .0001f) return 0;
			return Mathf.RadToDeg(facing.AngleTo(toTarget));
		}
	}

	/// <summary>Attack slots of the current target; null when the target lets every attacker strike.</summary>
	public AttackSlotsComponent TargetSlots => (Target as IAttackSlotHost)?.AttackSlots;

	/// <summary>Whether this actor may start its attack now as far as the target's slots are concerned.</summary>
	public bool CanTakeAttackSlot()
	{
		var policy = Profile.AttackStrategy;
		return policy == null || !policy.UsesAttackSlot || TargetSlots?.CanReserve(Actor, policy.SlotKind) != false;
	}

	/// <summary>The player currently being chased, or null when the enemy has no target.</summary>
	private Node3D _target;
	public Node3D Target
	{
		get => _target;
		set { if (_target != value) { Tactics.ClearTarget(); TacticalSelector?.ClearTarget(); _lastDecision = null; _retreatDestinationValid = false; _retreatRefresh = 0; } _target = value; }
	}

	/// <summary>
	/// Where the target was last known to be. Written while chasing (in sight) and read by Searching
	/// as the place to investigate after contact is lost.
	/// </summary>
	public Vector3 LastKnownTargetPosition { get; set; }

	/// <summary>The position the enemy roams around while patrolling (its spawn point).</summary>
	public Vector3 HomePosition { get; }

	private Cooldown _scanCooldown;
	private Cooldown _pathRefreshCooldown;
	private Cooldown _reachabilityCooldown;
	private bool _lastReachable = true;

	public EnemyContext(
		CharacterBody3D actor,
		MovementComponent movement,
		PerceptionComponent perception,
		NavigationComponent navigation,
		EnemyBehaviorProfile profile,
		Action requestAttack)
	{
		Actor = actor;
		Movement = movement;
		Perception = perception;
		Navigation = navigation;
		Profile = profile;
		TacticalSelector = profile.TacticalStrategy?.CreateRuntime();
		RequestAttack = requestAttack;
		HomePosition = actor.GlobalPosition;
	}

	/// <summary>
	/// True when the enemy currently has an unobstructed line of sight to its target. Reuses the
	/// perception line-of-sight test so ranged enemies do not fire through walls.
	/// </summary>
	public bool HasLineOfSightToTarget()
	{
		return Target != null && GodotObject.IsInstanceValid(Target) && Perception.CanSee(Target);
	}

	/// <summary>
	/// Advances the scan/repath/reachability throttle timers. The host calls this every physics
	/// frame, before any action or behavior logic, so the cadence is independent of which state runs.
	/// </summary>
	public void TickThrottles(double delta)
	{
		Delta = delta;
		TacticalSelector?.Tick(delta);
		_retreatRefresh -= (float)delta;
		_ringRefresh -= (float)delta;
		_scanCooldown.Tick(delta);
		_pathRefreshCooldown.Tick(delta);
		_reachabilityCooldown.Tick(delta);
	}

	/// <summary>True when the enemy has no target and the scan throttle has elapsed.</summary>
	public bool ShouldScanForTarget()
	{
		return Target == null && _scanCooldown.IsReady;
	}

	/// <summary>
	/// Scans for a detectable, reachable player and acquires it. Returns true when a new target was
	/// acquired this call. Detection (vision cone, hearing radius, line-of-sight) lives in
	/// <see cref="PerceptionComponent"/>; this only supplies the navmesh-reachability gate and
	/// records the sighting. See docs/level-design.md "Enemy Detection (sight &amp; hearing)".
	/// </summary>
	public bool LookForNewTarget()
	{
		_scanCooldown.Start(TargetScanInterval);
		if (Target != null)
		{
			return false;
		}

		Player player = Perception.FindVisibleTarget(CanReachTarget);
		if (player == null)
		{
			return false;
		}

		GameDebug.Ai($"{Actor.Name} has spotted {player.Name}");
		SetTarget(player);
		return true;
	}

	/// <summary>Sets the chase target and points navigation at its current position.</summary>
	public void SetTarget(Node3D target)
	{
		if (Target != target)
		{
			GameDebug.Ai($"{Actor.Name} is now targeting {target.Name}");
			Target = target;
		}
		UpdateTargetPosition();
	}

	/// <summary>True when the navmesh can path to <paramref name="target"/> (respects door state).</summary>
	public bool CanReachTarget(Node3D target)
	{
		return target != null && Navigation.IsReachable(target.GlobalPosition);
	}

	/// <summary>
	/// Throttled reachability check for the current <see cref="Target"/>, caching the last answer so
	/// the navmesh query runs at most once per <see cref="TargetReachabilityCheckInterval"/>.
	/// </summary>
	public bool CanReachCurrentTarget()
	{
		if (!_reachabilityCooldown.IsReady)
		{
			return _lastReachable;
		}

		_reachabilityCooldown.Start(TargetReachabilityCheckInterval);
		_lastReachable = CanReachTarget(Target);
		return _lastReachable;
	}

	/// <summary>
	/// Resets the cached reachability answer to "reachable". Called when a chase ends so the next
	/// chase starts from a clean assumption rather than a stale "unreachable" result.
	/// </summary>
	public void ResetReachabilityCache()
	{
		_lastReachable = true;
	}

	/// <summary>Records the target's current position as last-known and repaths to it.</summary>
	public bool UpdateTargetPosition()
	{
		if (Target == null)
		{
			return false;
		}
		LastKnownTargetPosition = Target.GlobalPosition;
		Navigation.SetDestination(LastKnownTargetPosition);
		return true;
	}

	/// <summary>
	/// Refreshes the chase destination to the target's live position, throttled so we only repath
	/// once per <see cref="TargetPathRefreshInterval"/> unless the target moved further than
	/// <see cref="TargetPositionRefreshDistance"/>.
	/// </summary>
	public bool UpdateTargetPositionThrottled()
	{
		if (Target == null)
		{
			return false;
		}

		if (!_pathRefreshCooldown.IsReady
			&& LastKnownTargetPosition.DistanceTo(Target.GlobalPosition) < TargetPositionRefreshDistance)
		{
			return true;
		}

		_pathRefreshCooldown.Start(TargetPathRefreshInterval);
		return UpdateTargetPosition();
	}

	/// <summary>Low-health movement temporarily overrides ordinary tactics after action commitment permits it.</summary>
	public EnemyMovementIntent EvaluateMovement()
	{
		var retreat = Profile.RetreatCondition;
		if (retreat != null)
		{
			var health = (Actor as EnemyBase)?.HealthComponent;
			float fraction = health != null && health.MaxHealth > 0 ? health.CurrentHealth / health.MaxHealth : 1;
			if (Tactics.EvaluateLowHealth(Delta, fraction, Target != null, retreat.EnterHealthFraction,
				retreat.ExitHealthFraction, retreat.MaximumDuration))
				return HorizontalTargetDistance < retreat.DesiredDistance
					? new(EnemyMovementGoal.Retreat, false, retreat.DesiredDistance) : new(EnemyMovementGoal.Hold, false);
		}
		return Profile.MovementStrategy.Evaluate(this);
	}

	/// <summary>Resolve tactical priorities and navigation failure before submitting an ordinary attack request.</summary>
	public void ExecuteTactics(EnemyMovementIntent? preference = null)
	{
		var decision = TacticalSelector.Select(preference ?? EvaluateMovement(), HorizontalTargetDistance, Profile.AttackStrategy.CanAttack(this));
		if (decision.Intent.Goal == EnemyMovementGoal.Retreat && !FollowRetreat(decision.Intent.DesiredDistance))
			decision = TacticalSelector.RetreatBlocked(Profile.AttackStrategy.CanAttack(this));
		if (decision.Intent.Goal != EnemyMovementGoal.Retreat) EndRetreatMovement();
		if (decision.Intent.Goal == EnemyMovementGoal.Approach)
		{
			// Retreat replaces the agent destination independently of the chase refresh cache.
			if (_lastDecision?.Intent.Goal == EnemyMovementGoal.Retreat || decision.Reason == "retreat_blocked")
				UpdateTargetPosition();
			else UpdateTargetPositionThrottled();
			FollowTargetPath();
		}
		else if (decision.Intent.Goal == EnemyMovementGoal.Hold)
		{
			// Holding keeps turning toward the target, limited by the actor's movement turn rate.
			Navigation.Stop();
			if (Target != null) Movement.FaceDirection(Target.GlobalPosition - Actor.GlobalPosition);
		}
		else if (decision.Intent.Goal == EnemyMovementGoal.Encircle) FollowEngagementRing(decision.Intent.DesiredDistance, decision.Intent.PreferRear);
		if (_lastDecision != decision)
		{
			_lastDecision = decision;
			if (CombatTrace.Enabled) CombatTrace.Write(Actor, "ai.tactical_decision", ("goal", decision.Intent.Goal.ToString()),
				("reason", decision.Reason), ("allow_attack", decision.Intent.AllowAttack));
		}
		if (decision.Intent.AllowAttack) RequestAttack();
	}

	/// <summary>Follow the existing chase path, including committed door crossings, with shared stuck recovery.</summary>
	public void FollowTargetPath()
	{
		if (Target == null) { Movement.Stop(); return; }
		Navigation.FollowPath(Target);
		if (Navigation.IsStuck)
		{
			Navigation.ResetStuckTracking();
			Navigation.SetDestination(LastKnownTargetPosition);
		}
	}

	public void EndRetreatMovement() { _retreatRefresh = 0; _retreatDestinationValid = false; }

	/// <summary>
	/// Wait for an attack slot at this actor's spread ring position, facing the target. The ring point
	/// is refreshed on a throttle and snapped to the navmesh when one is near.
	/// </summary>
	public void FollowEngagementRing(float distance, bool preferRear)
	{
		var slots = TargetSlots;
		if (Target == null || slots == null || Profile.AttackStrategy == null) { Navigation.Stop(); return; }
		if (_ringRefresh <= 0)
		{
			_ringRefresh = .3f;
			Vector3 ring = slots.GetRingPoint(Actor, Profile.AttackStrategy.SlotKind, distance, preferRear);
			Vector3 snapped = Navigation.GetClosestNavPoint(ring);
			_ringPoint = snapped.DistanceTo(ring) < distance ? snapped : ring;
			Navigation.SetDestination(_ringPoint);
		}
		Vector3 offset = _ringPoint - Actor.GlobalPosition;
		offset.Y = 0;
		if (offset.Length() <= .4f) Navigation.Stop();
		else Navigation.FollowPath(Target);
		Movement.FaceDirection(Target.GlobalPosition - Actor.GlobalPosition);
	}

	/// <summary>Bound and throttle retreat destination checks. Failed movement permits an eligible attack.</summary>
	public bool FollowRetreat(float desiredDistance)
	{
		if (Target == null) return false;
		if (_retreatRefresh <= 0)
		{
			_retreatRefresh = .2f;
			_retreatDestinationValid = Navigation.TrySetRetreatDestination(Target.GlobalPosition,
				Mathf.Max(.6f, desiredDistance - HorizontalTargetDistance));
		}
		if (!_retreatDestinationValid || Navigation.IsStuck) { Navigation.Stop(); return false; }
		Navigation.FollowPath(Target);
		Movement.FaceDirection(Target.GlobalPosition - Actor.GlobalPosition);
		return true;
	}

	/// <summary>Uniform random float in [min, max).</summary>
	public static float RandomRange(float min, float max)
	{
		return min + ((float)GD.Randf() * (max - min));
	}
}