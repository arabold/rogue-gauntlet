using Godot;

/// <summary>
/// Close distance while the target has a free attack slot for this monster; otherwise wait spread
/// around it at <see cref="RingDistance"/>, so crowds surround the player instead of all striking at once.
/// </summary>
[Tool]
[GlobalClass]
public partial class EngagementRingStrategy : CloseDistanceStrategy
{
	/// <summary>Waiting distance from the target; keep it outside the primary action's range.</summary>
	[Export] public float RingDistance { get; set; } = 3.5f;

	/// <summary>Start the ring behind the target's facing, so waiting monsters flank.</summary>
	[Export] public bool PreferRear { get; set; }

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		if (!context.Actions.CanTakePrimarySlot) return new(EnemyMovementGoal.Encircle, false, RingDistance, PreferRear);
		return base.Evaluate(context);
	}

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		base.Validate(context, combat);
		var primary = combat.PrimaryOffense;
		context.Check(float.IsFinite(RingDistance) && RingDistance > 0 && (primary == null || RingDistance > primary.EffectiveMaximumRange),
			"MovementStrategy: the engagement ring must lie outside the primary action's range.");
	}
}
