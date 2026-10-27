using Godot;

/// <summary>
/// Close distance while the target has a free attack slot for this monster; otherwise wait spread
/// around it at <see cref="RingDistance"/>, so crowds surround the player instead of all striking at once.
/// </summary>
[GlobalClass]
public partial class EngagementRingStrategy : EnemyMovementStrategy
{
	/// <summary>Waiting distance from the target; keep it outside the attack's engagement distance.</summary>
	[Export] public float RingDistance { get; set; } = 3.5f;

	/// <summary>Start the ring behind the target's facing, so waiting monsters flank.</summary>
	[Export] public bool PreferRear { get; set; }

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		if (!context.CanTakeAttackSlot()) return new(EnemyMovementGoal.Encircle, false, RingDistance, PreferRear);
		var attack = context.Profile.AttackStrategy;
		if (attack?.CanAttack(context) == true) return new(EnemyMovementGoal.Hold, true);
		return attack?.IsInReach(context) == true ? new(EnemyMovementGoal.Hold, false) : new(EnemyMovementGoal.Approach, false);
	}
}