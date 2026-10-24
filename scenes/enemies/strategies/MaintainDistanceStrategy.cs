using Godot;

/// <summary>Approach to regain a shot, back away when crowded and hold within an authored distance band.</summary>
[GlobalClass]
public partial class MaintainDistanceStrategy : EnemyMovementStrategy
{
	[Export] public float MinimumDistance { get; set; } = 5;
	[Export] public float PreferredDistance { get; set; } = 7;
	[Export] public float MaximumDistance { get; set; } = 10;

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		if (context.HorizontalTargetDistance < (context.TacticalSelector?.IsRetreating == true ? PreferredDistance : MinimumDistance)) return new(EnemyMovementGoal.Retreat, false, PreferredDistance);
		if (context.HorizontalTargetDistance > MaximumDistance || !context.HasLineOfSightToTarget()) return new(EnemyMovementGoal.Approach, false);
		return new(EnemyMovementGoal.Hold, true);
	}
}