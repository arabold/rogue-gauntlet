using Godot;

/// <summary>
/// Default state when no target is known: the enemy wanders around its spawn point and keeps
/// scanning for a player. The roam target, roam pause, and "have a roam target" flag are state-local
/// and persist across re-entry (the single instance is reused for the enemy's lifetime), matching
/// the original per-component roam fields.
/// </summary>
public sealed class PatrollingState : IEnemyState
{
	public EnemyBehaviorState Id => EnemyBehaviorState.Patrolling;

	private bool _hasRoamTarget;
	private Vector3 _roamTargetPosition;
	private Cooldown _roamPauseCooldown;

	public PatrollingState(EnemyBehaviorProfile profile)
	{
		// Stagger the first roam so freshly spawned enemies don't all step off at once
		// (matches the original _Ready initialization).
		_roamPauseCooldown.Start(EnemyContext.RandomRange(0, profile.RoamPauseMax));
	}

	public void Enter(EnemyContext ctx)
	{
	}

	/// <summary>
	/// Leaving patrol abandons any in-progress roam target, so a later return to patrol picks a fresh
	/// destination instead of resuming a stale one. (The original cleared this whenever the behavior
	/// became anything other than Patrolling.)
	/// </summary>
	public void Exit(EnemyContext ctx)
	{
		_hasRoamTarget = false;
	}

	public EnemyBehaviorState? Update(EnemyContext ctx, double delta)
	{
		if (ctx.ShouldScanForTarget() && ctx.LookForNewTarget())
		{
			return EnemyBehaviorState.Chasing;
		}

		Roam(ctx, delta);
		return null;
	}

	private void Roam(EnemyContext ctx, double delta)
	{
		if (!_roamPauseCooldown.IsReady)
		{
			_roamPauseCooldown.Tick(delta);
			ctx.Movement.Stop();
			return;
		}

		if (!_hasRoamTarget)
		{
			if (!TrySetRoamTarget(ctx))
			{
				StartRoamPause(ctx);
				ctx.Movement.Stop();
				return;
			}
		}

		if (ctx.Navigation.IsNavigationFinished()
			|| ctx.Actor.GlobalPosition.DistanceTo(_roamTargetPosition) <= ctx.Profile.RoamTargetDistance)
		{
			_hasRoamTarget = false;
			StartRoamPause(ctx);
			ctx.Movement.Stop();
			return;
		}

		ctx.Navigation.FollowPath();
		if (ctx.Navigation.IsStuck)
		{
			// Stuck mid-roam: drop this destination and pause before trying a new one.
			GameDebug.Ai($"{ctx.Actor.Name} appears stuck while patrolling; abandoning roam target.");
			ctx.Navigation.ResetStuckTracking();
			_hasRoamTarget = false;
			StartRoamPause(ctx);
			ctx.Movement.Stop();
		}
	}

	private bool TrySetRoamTarget(EnemyContext ctx)
	{
		for (int i = 0; i < ctx.Profile.RoamTargetAttempts; i++)
		{
			float angle = EnemyContext.RandomRange(0, Mathf.Pi * 2.0f);
			float distance = EnemyContext.RandomRange(ctx.Profile.RoamRadius * 0.25f, ctx.Profile.RoamRadius);
			Vector3 offset = new(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
			Vector3 targetPosition = ctx.Navigation.GetClosestNavPoint(ctx.HomePosition + offset);

			if (targetPosition.DistanceTo(ctx.HomePosition) > ctx.Profile.RoamRadius
				|| targetPosition.DistanceTo(ctx.Actor.GlobalPosition) <= ctx.Profile.RoamTargetDistance)
			{
				continue;
			}

			_roamTargetPosition = targetPosition;
			_hasRoamTarget = true;
			ctx.Navigation.SetDestination(_roamTargetPosition);
			return true;
		}

		return false;
	}

	private void StartRoamPause(EnemyContext ctx)
	{
		_roamPauseCooldown.Start(EnemyContext.RandomRange(ctx.Profile.RoamPauseMin, ctx.Profile.RoamPauseMax));
	}
}
