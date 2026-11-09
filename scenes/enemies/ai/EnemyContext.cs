using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Shared blackboard and service references handed to every <see cref="IEnemyState"/>. It owns the
/// data that has to outlive a single state - the current target, the last position it was seen at,
/// and the roam anchor - plus the throttle timers and the small query helpers states use to talk to
/// the perception and navigation components.
/// </summary>
/// <remarks>
/// States read and mutate this context; the host <see cref="EnemyBehaviorComponent"/> ticks the
/// throttle timers each physics frame and bridges the timed-action layer through
/// <see cref="RequestAction"/> so states never poke <c>CurrentAction</c> directly. Detection
/// and locomotion themselves live in the perception/navigation components - this only coordinates
/// them; see docs/level-design.md for the authoritative behavior description.
/// </remarks>
public sealed class EnemyContext
{
	// How often a target-less enemy re-scans its surroundings for a player to chase.
	private const float TargetScanInterval = 0.2f;
	// How often the reachability of the current target is re-checked against its chase field.
	private const float TargetReachabilityCheckInterval = 0.35f;
	// How often a monster waiting for an attack slot recomputes its ring point.
	private const float RingRefreshInterval = 0.3f;

	public CharacterBody3D Actor { get; }
	/// <summary>The actor as a monster, for executors that need its components; null for bare test actors.</summary>
	public EnemyBase Enemy => Actor as EnemyBase;
	public MovementComponent Movement { get; }
	public PerceptionComponent Perception { get; }
	public NavigationComponent Navigation { get; }
	public EnemyBehaviorProfile Profile { get; }
	public EnemyCombatProfile CombatProfile => Profile.Combat;
	public EnemyTacticsState Tactics { get; } = new();
	public EnemyTacticalSelector TacticalSelector { get; }
	/// <summary>Cooldowns, condition memory and the per-frame evaluation of this actor's actions.</summary>
	public MonsterActionRuntime Actions { get; }
	private EnemyTacticalDecision? _lastDecision;
	public string TacticalDecisionReason => _lastDecision?.Reason ?? "inactive";
	public double Delta { get; private set; }
	private Cooldown _ringRefresh;
	private Vector3 _ringPoint;
	private bool _hasRingPoint;
	private ulong _sightFrame = ulong.MaxValue;
	private Node3D _sightTarget;
	private bool _sightResult;
	private ulong _queryFrame = ulong.MaxValue;
	private readonly Dictionary<Node3D, bool> _sight = [];
	private readonly List<Node3D> _allies = [];
	private readonly List<(float Fraction, bool IsSelf, string Path, Node3D Ally)> _allyKeys = [];
	private bool _alliesCollected;
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

	/// <summary>Bridge to the host's action layer, so states never poke <c>CurrentAction</c> directly.</summary>
	public Action<MonsterActionFilter> RequestAction { get; }

	/// <summary>Degrees between the actor's facing and the horizontal direction to its target; 0 without a target.</summary>
	public float AngleToTarget => Target == null ? 0 : Movement.LookAtDirection.HorizontalAngleTo(Target.GlobalPosition - Actor.GlobalPosition);

	/// <summary>Attack slots of the current target; null when the target lets every attacker strike.</summary>
	public AttackSlotsComponent TargetSlots => (Target as IAttackSlotHost)?.AttackSlots;

	/// <summary>The actor's latched health phase; 0 without a <see cref="HealthPhaseComponent"/>.</summary>
	public int Phase => Enemy?.HealthPhases?.CurrentPhase ?? 0;

	/// <summary>Whether this actor can reserve a turn of <paramref name="kind"/> against its target now.</summary>
	public bool CanTakeSlot(AttackSlotKind kind) => TargetSlots?.CanReserve(Actor, kind) != false;

	/// <summary>
	/// Reserve a turn of <paramref name="kind"/>; a null kind or a target without slots needs none. The caller releases
	/// the returned slots (without yielding) if its start fails afterwards.
	/// </summary>
	public bool TryReserveSlot(AttackSlotKind? kind, out AttackSlotsComponent slots)
	{
		slots = kind is { } slotKind ? TargetSlots : null;
		if (slots == null) return true;
		if (slots.TryReserve(Actor, kind.Value)) return true;
		slots = null;
		return false;
	}

	/// <summary>Tell stateful tactics an attack was committed; rejected requests never reach them.</summary>
	public void NotifyAttackAccepted(AttackIdentity identity)
	{
		TacticalSelector?.AttackAccepted(identity);
		CombatProfile?.MovementStrategy?.AttackAccepted(this, identity);
	}

	/// <summary>The player currently being chased, or null when the enemy has no target.</summary>
	private Node3D _target;
	public Node3D Target
	{
		get => _target;
		set
		{
			if (_target != value)
			{
				Tactics.ClearTarget();
				TacticalSelector?.ClearTarget();
				Actions.ClearTargetMemory();
				_lastDecision = null;
				_hasRingPoint = false;
			}
			_target = value;
		}
	}

	/// <summary>
	/// Fellow living monsters (this actor included), most injured first; on equal health others come before the actor,
	/// then scene path keeps the order deterministic. Collected once per physics frame.
	/// </summary>
	public IReadOnlyList<Node3D> AlliesByInjury
	{
		get
		{
			BeginQueryFrame();
			if (_alliesCollected) return _allies;
			_alliesCollected = true;
			_allyKeys.Clear();
			foreach (Node node in Actor.GetTree().GetNodesInGroup("enemy"))
				if (node is Node3D ally && ally.IsInsideTree() && ally is IBuffTarget { Health: { IsDead: false, MaxHealth: > 0 } health })
					_allyKeys.Add((health.CurrentHealth / health.MaxHealth, ReferenceEquals(ally, Actor), ally.GetPath().ToString(), ally));
			_allyKeys.Sort((left, right) =>
			{
				int byHealth = left.Fraction.CompareTo(right.Fraction);
				if (byHealth != 0) return byHealth;
				int bySelf = left.IsSelf.CompareTo(right.IsSelf);
				return bySelf != 0 ? bySelf : string.CompareOrdinal(left.Path, right.Path);
			});
			foreach (var key in _allyKeys) _allies.Add(key.Ally);
			return _allies;
		}
	}

	/// <summary>Unobstructed sight to any node, raycast at most once per node per physics frame.</summary>
	public bool CanSee(Node3D target)
	{
		if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree()) return false;
		if (ReferenceEquals(target, Target)) return HasLineOfSightToTarget();
		BeginQueryFrame();
		if (_sight.TryGetValue(target, out bool visible)) return visible;
		visible = WorldSight.IsClear(Actor.GetWorld3D(), Actor.GlobalPosition + Vector3.Up, target.GlobalPosition + Vector3.Up);
		_sight[target] = visible;
		return visible;
	}

	private void BeginQueryFrame()
	{
		ulong frame = Engine.GetPhysicsFrames();
		if (frame == _queryFrame) return;
		_queryFrame = frame;
		_sight.Clear();
		_allies.Clear();
		_alliesCollected = false;
	}

	/// <summary>
	/// Where the target was last known to be. Written while chasing (in sight) and read by Searching
	/// as the place to investigate after contact is lost.
	/// </summary>
	public Vector3 LastKnownTargetPosition { get; set; }

	/// <summary>The position the enemy roams around while patrolling (its spawn point).</summary>
	public Vector3 HomePosition { get; }

	private Cooldown _scanCooldown;
	private Cooldown _reachabilityCooldown;
	private bool _lastReachable = true;

	public EnemyContext(
		CharacterBody3D actor,
		MovementComponent movement,
		PerceptionComponent perception,
		NavigationComponent navigation,
		EnemyBehaviorProfile profile,
		Action<MonsterActionFilter> requestAction)
	{
		Actor = actor;
		Movement = movement;
		Perception = perception;
		Navigation = navigation;
		Profile = profile;
		TacticalSelector = profile.Combat?.TacticalStrategy?.CreateRuntime();
		Actions = new MonsterActionRuntime(this);
		RequestAction = requestAction;
		HomePosition = actor.GlobalPosition;
	}

	/// <summary>Reuses one perception ray result per physics frame for reach, attack and positioning checks.</summary>
	public bool HasLineOfSightToTarget()
	{
		if (Target == null || !GodotObject.IsInstanceValid(Target)) return false;
		ulong frame = Engine.GetPhysicsFrames();
		if (frame != _sightFrame || !ReferenceEquals(Target, _sightTarget))
		{
			_sightFrame = frame;
			_sightTarget = Target;
			_sightResult = Perception.CanSee(Target);
		}
		return _sightResult;
	}

	/// <summary>
	/// Advances the scan/repath/reachability throttle timers. The host calls this every physics
	/// frame, before any action or behavior logic, so the cadence is independent of which state runs.
	/// </summary>
	public void TickThrottles(double delta)
	{
		Delta = delta;
		TacticalSelector?.Tick(delta);
		Actions.Tick(delta);
		_ringRefresh.Tick(delta);
		_scanCooldown.Tick(delta);
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
	/// <see cref="PerceptionComponent"/>; this only supplies the reachability gate and
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

	/// <summary>Sets the chase target and records where it was seen.</summary>
	public void SetTarget(Node3D target)
	{
		if (Target != target)
		{
			GameDebug.Ai($"{Actor.Name} is now targeting {target.Name}");
			Target = target;
		}
		RecordTargetPosition();
	}

	/// <summary>True when this body can walk to <paramref name="target"/> (door-aware, size-aware).</summary>
	public bool CanReachTarget(Node3D target)
	{
		return target != null && Navigation.CanReach(target);
	}

	/// <summary>
	/// Throttled reachability check for the current <see cref="Target"/>, caching the last answer so
	/// the field query runs at most once per <see cref="TargetReachabilityCheckInterval"/>.
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

	/// <summary>Records the target's current position as last known, for a search after contact is lost.</summary>
	public void RecordTargetPosition()
	{
		if (Target != null) LastKnownTargetPosition = Target.GlobalPosition;
	}

	/// <summary>Low-health movement temporarily overrides ordinary tactics after action commitment permits it.</summary>
	public EnemyMovementIntent EvaluateMovement()
	{
		var retreat = CombatProfile.RetreatCondition;
		if (retreat != null)
		{
			var health = (Actor as EnemyBase)?.HealthComponent;
			float fraction = health != null && health.MaxHealth > 0 ? health.CurrentHealth / health.MaxHealth : 1;
			if (Tactics.EvaluateLowHealth(Delta, fraction, Target != null, retreat.EnterHealthFraction,
				retreat.ExitHealthFraction, retreat.MaximumDuration))
				return HorizontalTargetDistance < retreat.DesiredDistance
					? new(EnemyMovementGoal.Retreat, false, retreat.DesiredDistance) : new(EnemyMovementGoal.Hold, false);
		}
		return CombatProfile.MovementStrategy.Evaluate(this);
	}

	/// <summary>
	/// Resolve tactical priorities and navigation, then request an action. Offensive actions are requested only when the
	/// decision allows attacking, so retreating, fleeing and encircling monsters never attack; support actions always may.
	/// </summary>
	public void ExecuteTactics(EnemyMovementIntent? preference = null)
	{
		var decision = TacticalSelector.Select(preference ?? EvaluateMovement(), HorizontalTargetDistance, Actions.CanActNow);
		if (decision.Intent.Goal == EnemyMovementGoal.Retreat && !FollowRetreat())
			decision = TacticalSelector.RetreatBlocked(Actions.CanActNow);
		// An approach that is already in reach (but not yet facing or eligible) holds and turns instead.
		bool approaching = decision.Intent.Goal == EnemyMovementGoal.Approach && !Actions.IsPrimaryInReach;
		if (approaching)
		{
			RecordTargetPosition();
			FollowTargetPath();
		}
		else if (decision.Intent.Goal is EnemyMovementGoal.Hold or EnemyMovementGoal.Approach) Navigation.Stop();
		else if (decision.Intent.Goal == EnemyMovementGoal.Encircle) FollowEngagementRing(decision.Intent.DesiredDistance, decision.Intent.PreferRear);
		if (decision.Intent.Goal != EnemyMovementGoal.Encircle) _hasRingPoint = false;
		// Every goal but an approach faces the target, limited by the actor's movement turn rate; approaching faces its path.
		if (!approaching && Target != null) Movement.FaceDirection(Target.GlobalPosition - Actor.GlobalPosition);
		if (_lastDecision != decision)
		{
			_lastDecision = decision;
			if (CombatTrace.Enabled) CombatTrace.Write(Actor, "ai.tactical_decision", ("goal", decision.Intent.Goal.ToString()),
				("reason", decision.Reason), ("allow_attack", decision.Intent.AllowAttack));
		}
		RequestAction(decision.Intent.AllowAttack ? MonsterActionFilter.All : MonsterActionFilter.SupportOnly);
	}

	/// <summary>Chase the target down its shared field. A stall only restarts progress tracking: the field updates as the target moves.</summary>
	private void FollowTargetPath()
	{
		if (Target == null) { Navigation.Stop(); return; }
		Navigation.Chase(Target);
		if (Navigation.IsStuck) Navigation.ResetStuckTracking();
	}

	/// <summary>
	/// Wait for an attack slot at this actor's spread ring position, facing the target. The ring point is refreshed on
	/// a throttle and moved to the nearest spot this body fits; without one the actor holds where it is.
	/// </summary>
	private void FollowEngagementRing(float distance, bool preferRear)
	{
		var slots = TargetSlots;
		if (Target == null || slots == null || Actions.PrimaryOffense?.ReservedSlot is not { } kind) { Navigation.Stop(); return; }
		if (_ringRefresh.IsReady || !_hasRingPoint)
		{
			_ringRefresh.Start(RingRefreshInterval);
			Vector3 ring = slots.GetRingPoint(Actor, kind, distance, preferRear);
			_hasRingPoint = Navigation.TryFindTraversable(ring, distance, out _ringPoint);
		}
		if (!_hasRingPoint || !Navigation.MoveTo(_ringPoint) || Navigation.IsStuck) Navigation.Stop();
	}

	/// <summary>Flee the target down its shared field. False when cornered or stalled, which permits an eligible attack.</summary>
	private bool FollowRetreat()
	{
		if (Target == null || !Navigation.Flee(Target)) return false;
		if (!Navigation.IsStuck) return true;
		Navigation.Stop();
		return false;
	}

	/// <summary>Uniform random float in [min, max).</summary>
	public static float RandomRange(float min, float max)
	{
		return min + ((float)GD.Randf() * (max - min));
	}
}
