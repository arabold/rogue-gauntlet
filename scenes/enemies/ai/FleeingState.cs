/// <summary>Explicit flee-state entry uses the authored bounded health-retreat rule and normal navigation safety.</summary>
public sealed class FleeingState : IEnemyState
{
	public EnemyBehaviorState Id => EnemyBehaviorState.Fleeing;
	public void Enter(EnemyContext context) { }
	public EnemyBehaviorState? Update(EnemyContext context, double delta)
	{
		if (context.Target == null || context.Profile.RetreatCondition == null) return EnemyBehaviorState.Idle;
		var intent = context.EvaluateMovement();
		if (intent.Goal != EnemyMovementGoal.Retreat) return EnemyBehaviorState.Chasing;
		context.FollowRetreat(intent.DesiredDistance);
		return null;
	}
	public void Exit(EnemyContext context) { }
}