using Godot;

/// <summary>
/// Active pursuit. The enemy keeps repathing to the target's live position for as long as the
/// navmesh can still reach it, drops to Searching once the target becomes unreachable, and attacks
/// when in range. Retention deliberately does NOT require line of sight: an alerted enemy follows
/// the target around corners and through other doors even when it cannot see the player.
/// </summary>
/// <remarks>
/// Ranged attackers (<see cref="EnemyBehaviorProfile.RangedAttackDefinition"/> set) behave
/// differently from melee: once within <see cref="EnemyBehaviorProfile.RangedAttackRange"/> AND
/// with a clear line of sight, they stop closing in, face the target, and fire - they do not walk
/// into melee range first. Line of sight IS required here (unlike chase retention above) because it
/// gates the decision to stop and shoot, not whether the chase continues; without a clear shot the
/// enemy keeps closing distance like a melee attacker so it does not stand still forever pointed at
/// a wall.
/// </remarks>
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

		bool isRanged = ctx.Profile.RangedAttackDefinition != null;
		if (isRanged && HasClearShot(ctx))
		{
			FaceTarget(ctx);
			ctx.RequestRangedAttack();
			return null;
		}

		// Close the distance: melee attackers always, ranged attackers while they have no clear
		// shot yet (out of range or blocked) so they do not idle at range forever waiting for a
		// line that never opens up.
		NavigateToTarget(ctx);

		if (!isRanged && IsNearTarget(ctx))
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

	private static bool HasClearShot(EnemyContext ctx)
	{
		if (ctx.Target == null)
		{
			return false;
		}

		float distance = ctx.Actor.GlobalPosition.DistanceTo(ctx.Target.GlobalPosition);
		return distance <= ctx.Profile.RangedAttackRange && ctx.Perception.CanSee(ctx.Target);
	}

	/// <summary>
	/// Snaps the body to face the target before firing. The action layer holds the body still for
	/// the rest of the attack (see <see cref="EnemyBehaviorComponent"/>), so this is the only chance
	/// to aim; movement is left at zero (<see cref="MovementComponent.Stop"/> was already applied via
	/// <see cref="EnemyBehaviorComponent.SetAction"/>), which keeps <see cref="MovementComponent"/>'s
	/// own turn-toward-movement smoothing from overriding the snap next frame.
	/// </summary>
	private static void FaceTarget(EnemyContext ctx)
	{
		Vector3 toTarget = ctx.Target.GlobalPosition - ctx.Actor.GlobalPosition;
		toTarget.Y = 0;
		if (toTarget.LengthSquared() > 0.0001f)
		{
			ctx.Movement.SetLookAtDirection(toTarget.Normalized());
		}
	}
}
