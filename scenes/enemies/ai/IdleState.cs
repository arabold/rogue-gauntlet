/// <summary>
/// Transient wake-up state. Scans once for a target and immediately routes to Chasing if one is
/// found, otherwise to Patrolling - it never stays Idle. Mirrors the original Idle branch.
/// </summary>
public sealed class IdleState : IEnemyState
{
	public EnemyBehaviorState Id => EnemyBehaviorState.Idle;

	public void Enter(EnemyContext ctx)
	{
	}

	public EnemyBehaviorState? Update(EnemyContext ctx, double delta)
	{
		ctx.Navigation.ResetStuckTracking();

		if (ctx.ShouldScanForTarget() && ctx.LookForNewTarget())
		{
			return EnemyBehaviorState.Chasing;
		}

		return EnemyBehaviorState.Patrolling;
	}

	public void Exit(EnemyContext ctx)
	{
	}
}
