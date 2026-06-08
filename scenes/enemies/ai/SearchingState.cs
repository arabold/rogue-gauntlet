/// <summary>
/// Entered when a chase loses contact. The enemy walks to the spot where the target was last seen
/// and investigates for a while before giving up and returning to patrol, so a player who breaks
/// line of sight (or closes a door) is pursued to their last position instead of being forgotten
/// instantly. Re-acquiring the target along the way resumes the chase.
/// </summary>
public sealed class SearchingState : IEnemyState
{
	private Cooldown _searchCooldown;

	public EnemyBehaviorState Id => EnemyBehaviorState.Searching;

	/// <summary>
	/// Drops the target and heads for its last known position. The destination is whatever the chase
	/// recorded in <see cref="EnemyContext.LastKnownTargetPosition"/> before contact was lost.
	/// </summary>
	public void Enter(EnemyContext ctx)
	{
		GameDebug.Ai($"{ctx.Actor.Name} lost contact; searching last known position for {ctx.Profile.SearchDuration}s.");
		ctx.Movement.Stop();
		ctx.Target = null;
		ctx.ResetReachabilityCache();
		_searchCooldown.Start(ctx.Profile.SearchDuration);
		ctx.Navigation.SetDestination(ctx.LastKnownTargetPosition);
	}

	public EnemyBehaviorState? Update(EnemyContext ctx, double delta)
	{
		bool searchExpired = _searchCooldown.Tick(delta);

		if (ctx.ShouldScanForTarget() && ctx.LookForNewTarget())
		{
			return EnemyBehaviorState.Chasing;
		}

		if (searchExpired || ctx.Navigation.IsNavigationFinished())
		{
			// Reached the last known position or gave up; resume normal patrol.
			return EnemyBehaviorState.Patrolling;
		}

		ctx.Navigation.FollowPath();
		if (ctx.Navigation.IsStuck)
		{
			ctx.Navigation.ResetStuckTracking();
			ctx.Navigation.SetDestination(ctx.LastKnownTargetPosition);
		}

		return null;
	}

	public void Exit(EnemyContext ctx)
	{
	}
}
