using Godot;

/// <summary>
/// Approach until the primary (first offensive) action can start, then hold for it. Attacks stay allowed while
/// approaching, so a secondary gap-closer such as a rush fires on the way in. An approach that is already in reach holds
/// and turns instead (see <see cref="EnemyContext.ExecuteTactics"/>).
/// </summary>
[Tool]
[GlobalClass]
public partial class CloseDistanceStrategy : EnemyMovementStrategy
{
	public override EnemyMovementIntent Evaluate(EnemyContext context) =>
		new(context.Actions.CanEngage ? EnemyMovementGoal.Hold : EnemyMovementGoal.Approach, true);

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat) => RequirePrimary(context, combat);
}
