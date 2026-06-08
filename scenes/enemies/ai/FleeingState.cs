/// <summary>
/// Backs away from the target's last known position. Fleeing intentionally just steers directly away
/// without wall-sliding or stuck recovery, and resets stuck tracking each frame so a later chase
/// starts clean. No state currently transitions into Fleeing on its own, but the behavior is kept so
/// a profile or future trigger can drive an enemy into it.
/// </summary>
public sealed class FleeingState : IEnemyState
{
	public EnemyBehaviorState Id => EnemyBehaviorState.Fleeing;

	public void Enter(EnemyContext ctx)
	{
	}

	public EnemyBehaviorState? Update(EnemyContext ctx, double delta)
	{
		ctx.UpdateTargetPositionThrottled();

		if (ctx.Target == null)
		{
			ctx.Movement.Stop();
		}
		else
		{
			ctx.Navigation.FollowPathAway();
		}

		ctx.Navigation.ResetStuckTracking();
		return null;
	}

	public void Exit(EnemyContext ctx)
	{
	}
}
