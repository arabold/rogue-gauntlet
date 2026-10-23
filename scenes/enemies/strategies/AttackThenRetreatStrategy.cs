using Godot;

/// <summary>Retreat after normal attack recovery, wait briefly, then approach for another attack.</summary>
[GlobalClass]
public partial class AttackThenRetreatStrategy : EnemyMovementStrategy
{
	[Export] public float RetreatDistance { get; set; } = 4;
	[Export] public float MaximumRetreatDuration { get; set; } = 1.2f;
	[Export] public float ReengagementDelay { get; set; } = .4f;

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		var phase = context.Tactics.EvaluatePostAttack(context.Delta, context.HorizontalTargetDistance,
			RetreatDistance, MaximumRetreatDuration, ReengagementDelay);
		if (phase == EnemyRetreatPhase.Retreat) return new(EnemyMovementGoal.Retreat, false, RetreatDistance);
		if (phase == EnemyRetreatPhase.Wait) return new(EnemyMovementGoal.Hold, false);
		return context.Profile.AttackStrategy.CanAttack(context)
			? new(EnemyMovementGoal.Hold, true) : new(EnemyMovementGoal.Approach, false);
	}
}