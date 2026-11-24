using Godot;

/// <summary>
/// Runs from the target while <see cref="Condition"/> holds, and follows its <see cref="Pattern"/> otherwise: the
/// monster backs away until <see cref="Distance"/> lies between them, then waits there, and never attacks meanwhile.
/// The retreat preempts (<see cref="EnemyMovementStrategy.Preempt"/>), so it outranks the profile's low-health retreat
/// and flank. A thief carrying loot runs off this way (<see cref="CarryingLootCondition"/>, the nymph).
/// </summary>
[Tool]
[GlobalClass]
public partial class RetreatWhileStrategy : EnemyMovementStrategy
{
	/// <summary>
	/// While this holds the monster retreats. It is asked every decision with the current target; a condition that keeps
	/// memory advances only while an action lists it too.
	/// </summary>
	[Export] public MonsterActionCondition Condition { get; set; }

	/// <summary>How far from the target the monster runs, and waits once there.</summary>
	[Export(PropertyHint.Range, "1,40,0.5,suffix:m")] public float Distance { get; set; } = 12;

	/// <summary>The positioning the monster follows while the condition does not hold.</summary>
	[Export] public EnemyMovementStrategy Pattern { get; set; }

	public override EnemyMovementIntent? Preempt(EnemyContext context) =>
		Condition.IsMet(context, context.Target) ? context.RetreatOrHold(Distance) : Pattern.Preempt(context);

	public override EnemyMovementIntent Evaluate(EnemyContext context) => Preempt(context) ?? Pattern.Evaluate(context);

	public override void AttackAccepted(EnemyContext context, AttackIdentity identity) => Pattern.AttackAccepted(context, identity);

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		context.Check(MonsterValidationContext.IsPositive(Distance), "MovementStrategy: give the retreat a positive Distance.");
		if (context.Check(Condition != null, "MovementStrategy: give the retreat a Condition to retreat while."))
			using (context.Scope("RetreatWhile Condition")) Condition.Validate(context);
		if (context.Check(Pattern != null, "MovementStrategy: give the retreat a Pattern to follow otherwise."))
			Pattern.Validate(context, combat);
	}
}
