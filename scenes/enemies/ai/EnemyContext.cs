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
/// <see cref="RequestMeleeAttack"/> so states never poke <c>CurrentAction</c> directly. Detection
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

	/// <summary>
	/// Starts a melee attack through the host's timed-action layer. Wired by
	/// <see cref="EnemyBehaviorComponent"/> so states request attacks without reaching into the
	/// action gate themselves.
	/// </summary>
	public Action RequestMeleeAttack { get; }

	/// <summary>The player currently being chased, or null when the enemy has no target.</summary>
	public Node3D Target { get; set; }

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
		Action requestMeleeAttack)
	{
		Actor = actor;
		Movement = movement;
		Perception = perception;
		Navigation = navigation;
		Profile = profile;
		RequestMeleeAttack = requestMeleeAttack;
		HomePosition = actor.GlobalPosition;
	}

	/// <summary>
	/// Advances the scan/repath/reachability throttle timers. The host calls this every physics
	/// frame, before any action or behavior logic, so the cadence is independent of which state runs.
	/// </summary>
	public void TickThrottles(double delta)
	{
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

	/// <summary>Uniform random float in [min, max).</summary>
	public static float RandomRange(float min, float max)
	{
		return min + ((float)GD.Randf() * (max - min));
	}
}
