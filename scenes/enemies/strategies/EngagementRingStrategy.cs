using Godot;

/// <summary>
/// Close distance while the target has a free attack slot for this monster; otherwise wait spread around it at
/// <see cref="RingDistance"/>, so crowds surround the player instead of all striking at once. The ring starts behind the
/// player's facing, so waiting monsters flank.
/// </summary>
[Tool]
[GlobalClass]
public partial class EngagementRingStrategy : CloseDistanceStrategy
{
	/// <summary>Waiting distance from the target; keep it outside the primary action's range.</summary>
	[Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")] public float RingDistance { get; set; } = 3.5f;

	/// <summary>Degrees behind the target that waiting monsters spread over; 360 surrounds it.</summary>
	[Export(PropertyHint.Range, "0,360,5,suffix:\u00b0")] public float Arc { get; set; } = 360;

	/// <summary>Most degrees a waiting monster swings around the target per ring point, so it circles instead of crossing.</summary>
	[Export(PropertyHint.Range, "10,180,5,suffix:\u00b0")] public float OrbitStep { get; set; } = 60;

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		if (!context.Actions.CanTakePrimarySlot) return new(EnemyMovementGoal.Encircle, false, RingDistance, Arc, OrbitStep);
		return base.Evaluate(context);
	}

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		base.Validate(context, combat);
		context.Check(MonsterValidationContext.IsPositive(RingDistance) && combat.IsOutsidePrimaryReach(RingDistance),
			"MovementStrategy: the engagement ring must lie outside the primary action's range.");
	}
}
