using Godot;

/// <summary>Approach to regain a shot, back away when crowded and hold within an authored distance band.</summary>
[Tool]
[GlobalClass]
public partial class MaintainDistanceStrategy : EnemyMovementStrategy
{
	[Export] public float MinimumDistance { get; set; } = 5;
	[Export] public float PreferredDistance { get; set; } = 7;
	[Export] public float MaximumDistance { get; set; } = 10;

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		if (context.HorizontalTargetDistance < (context.TacticalSelector?.IsRetreating == true ? PreferredDistance : MinimumDistance)) return new(EnemyMovementGoal.Retreat, false, PreferredDistance);
		// Closing in may still shoot when in sight; each action's own range and sight decide whether it can.
		if (!context.HasLineOfSightToTarget()) return new(EnemyMovementGoal.Approach, false);
		if (context.HorizontalTargetDistance > MaximumDistance) return new(EnemyMovementGoal.Approach, true);
		return new(EnemyMovementGoal.Hold, true);
	}

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		var primary = combat.PrimaryOffense;
		context.Check(MonsterValidationContext.IsNonnegative(MinimumDistance) && float.IsFinite(PreferredDistance)
			&& float.IsFinite(MaximumDistance) && MinimumDistance < PreferredDistance && PreferredDistance < MaximumDistance
			&& primary != null && MaximumDistance <= primary.EffectiveMaximumRange,
			"MovementStrategy: require minimum < preferred < maximum <= the primary action's range.");
	}
}
