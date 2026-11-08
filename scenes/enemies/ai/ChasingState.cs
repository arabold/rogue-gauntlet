/// <summary>
/// Active pursuit. The enemy follows the target's shared chase field for as long as it can still
/// reach the target, drops to Searching once the target becomes unreachable (a door closed between
/// them, say), and attacks when in range. Retention deliberately does NOT require line of sight: an alerted enemy
/// follows the target around corners and through other doors even when it cannot see the player.
/// </summary>
/// <remarks>
/// Line of sight gates <em>acquisition</em> only (see <see cref="EnemyContext.LookForNewTarget"/> and
/// <see cref="PerceptionComponent"/>); it must not be re-introduced as a chase give-up condition, or
/// an enemy taking the long way around - which loses sight of the player en route - would abandon the
/// chase exactly when it is pursuing correctly. See docs/level-design.md "Chase retention" and
/// "Enemy Door Awareness" for the authoritative rules.
/// </remarks>
public sealed class ChasingState : IEnemyState
{
	public EnemyBehaviorState Id => EnemyBehaviorState.Chasing;

	public void Enter(EnemyContext ctx)
	{
	}

	public EnemyBehaviorState? Update(EnemyContext ctx, double delta)
	{
		if (!ctx.CanReachCurrentTarget())
		{
			return EnemyBehaviorState.Searching;
		}

		ctx.ExecuteTactics();
		return null;
	}

	public void Exit(EnemyContext ctx)
	{
	}

}