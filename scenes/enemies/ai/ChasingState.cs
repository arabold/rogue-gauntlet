/// <summary>
/// Active pursuit. The enemy keeps repathing to the target's live position for as long as the
/// navmesh can still reach it, drops to Searching once the target becomes unreachable, and melee
/// attacks when in range. Retention deliberately does NOT require line of sight: an alerted enemy
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
		// While crossing a doorway link the actor is briefly off the navmesh (the link spans the
		// doorway gap rather than filling it). Repathing or reachability-testing from an off-mesh
		// position snaps the path start to whichever doorway side is nearest and flips it as the
		// actor inches across, so the enemy oscillates in the doorway. Freeze those decisions until
		// it lands back on the mesh so it commits to the crossing; it keeps following its
		// already-computed path, which routes through the link.
		bool crossingDoorway = ctx.Navigation.IsCrossingDoorway();

		if (!crossingDoorway && !ctx.CanReachCurrentTarget())
		{
			return EnemyBehaviorState.Searching;
		}

		// Retention is reachability-based, not sight-based: as long as the navmesh can reach the
		// target we keep repathing to its live position, so the enemy pursues around corners and
		// through doors where it has no line of sight. Skipped while crossing a doorway link, where
		// the agent is briefly off-mesh and repathing would snap the path start across the gap.
		if (!crossingDoorway)
		{
			ctx.UpdateTargetPositionThrottled();
		}

		NavigateToTarget(ctx);

		if (IsNearTarget(ctx))
		{
			ctx.RequestMeleeAttack();
		}

		return null;
	}

	public void Exit(EnemyContext ctx)
	{
	}

	private static void NavigateToTarget(EnemyContext ctx)
	{
		if (ctx.Target == null)
		{
			ctx.Movement.Stop();
			return;
		}

		// The target is passed as the ignored collider so the enemy slides along walls but never
		// treats the player as a wall to orbit.
		ctx.Navigation.FollowPath(ctx.Target);
		if (ctx.Navigation.IsStuck)
		{
			ctx.Navigation.ResetStuckTracking();
			ctx.Navigation.SetDestination(ctx.LastKnownTargetPosition);
		}
	}

	private static bool IsNearTarget(EnemyContext ctx)
	{
		if (ctx.Target == null)
		{
			return false;
		}

		float distance = ctx.Actor.GlobalPosition.DistanceTo(ctx.Target.GlobalPosition);
		return distance < ctx.Profile.MeleeAttackRange;
	}
}
