using Godot;

/// <summary>
/// Retreat after normal attack recovery, wait briefly, then approach for another attack. Neither the retreat nor the
/// wait allows attacking.
/// </summary>
[Tool]
[GlobalClass]
public partial class AttackThenRetreatStrategy : EnemyMovementStrategy
{
	[Export] public float RetreatDistance { get; set; } = 4;
	[Export] public float MaximumRetreatDuration { get; set; } = 1.2f;
	[Export] public float ReengagementDelay { get; set; } = .4f;
	public override void AttackAccepted(EnemyContext context, AttackIdentity identity) => context.Tactics.TrackAttack(identity);

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		var phase = context.Tactics.EvaluatePostAttack(context.Delta, context.HorizontalTargetDistance,
			RetreatDistance, MaximumRetreatDuration, ReengagementDelay);
		if (phase == EnemyRetreatPhase.Retreat) return new(EnemyMovementGoal.Retreat, false, RetreatDistance);
		if (phase == EnemyRetreatPhase.Wait) return new(EnemyMovementGoal.Hold, false);
		return new(context.Actions.CanEngage ? EnemyMovementGoal.Hold : EnemyMovementGoal.Approach, true);
	}

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		RequirePrimary(context, combat);
		context.Check(float.IsFinite(RetreatDistance) && RetreatDistance > 0 && float.IsFinite(MaximumRetreatDuration)
			&& MaximumRetreatDuration > 0 && float.IsFinite(ReengagementDelay) && ReengagementDelay >= 0,
			"MovementStrategy: invalid retreat distance, duration or delay.");
	}
}
