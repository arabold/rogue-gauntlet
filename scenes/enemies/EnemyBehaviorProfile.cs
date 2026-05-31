using Godot;

/// <summary>
/// Defines designer-authored AI tuning for an enemy type.
/// </summary>
/// <remarks>
/// Create one profile per monster variant so scenes can share behavior code while changing
/// detection, roaming, attack ranges, and action timing through resources.
/// </remarks>
[GlobalClass]
public partial class EnemyBehaviorProfile : Resource
{
	/// <summary>
	/// Behavior state the enemy enters when it is initialized.
	/// </summary>
	[Export] public EnemyBehaviorState InitialBehavior { get; set; } = EnemyBehaviorState.Idle;

	/// <summary>
	/// Action the enemy performs when it is initialized.
	/// </summary>
	[Export] public EnemyAction InitialAction { get; set; } = EnemyAction.Spawning;

	/// <summary>
	/// Maximum distance at which the enemy can detect a player.
	/// </summary>
	[Export] public float DetectionRange { get; set; } = 20.0f;

	/// <summary>
	/// Field-of-view angle, in degrees, used for line-of-sight detection.
	/// </summary>
	[Export] public float DetectionAngle { get; set; } = 45.0f;

	/// <summary>
	/// Fraction of detection range where the enemy wakes up from proximity alone.
	/// </summary>
	[Export] public float CloseDetectionRangeMultiplier { get; set; } = 0.5f;

	/// <summary>
	/// Distance from the target at which the enemy can start a melee attack.
	/// </summary>
	[Export] public float MeleeAttackRange { get; set; } = 2.0f;

	/// <summary>
	/// Accuracy used by this enemy's melee attack.
	/// </summary>
	[Export] public float MeleeAttackAccuracy { get; set; } = 0.8f;

	/// <summary>
	/// Minimum damage dealt by this enemy's melee attack.
	/// </summary>
	[Export] public float MeleeAttackMinDamage { get; set; } = 1.0f;

	/// <summary>
	/// Maximum damage dealt by this enemy's melee attack.
	/// </summary>
	[Export] public float MeleeAttackMaxDamage { get; set; } = 5.0f;

	/// <summary>
	/// Critical hit chance used by this enemy's melee attack.
	/// </summary>
	[Export] public float MeleeAttackCritChance { get; set; } = 0.0f;

	/// <summary>
	/// Optional authored attack definition for enemy melee hit shape and timing overrides.
	/// </summary>
	[Export] public AttackDefinition MeleeAttackDefinition { get; set; }

	/// <summary>
	/// Maximum distance from its spawn point that the enemy can pick roam destinations.
	/// </summary>
	[Export] public float RoamRadius { get; set; } = 8.0f;

	/// <summary>
	/// Minimum time the enemy waits between roam destinations.
	/// </summary>
	[Export] public float RoamPauseMin { get; set; } = 1.0f;

	/// <summary>
	/// Maximum time the enemy waits between roam destinations.
	/// </summary>
	[Export] public float RoamPauseMax { get; set; } = 3.0f;

	/// <summary>
	/// Distance at which a roam destination is considered reached.
	/// </summary>
	[Export] public float RoamTargetDistance { get; set; } = 0.75f;

	/// <summary>
	/// Number of random navmesh points to try before pausing and retrying later.
	/// </summary>
	[Export] public int RoamTargetAttempts { get; set; } = 8;

	/// <summary>
	/// Time spent in the spawn action before normal behavior resumes.
	/// </summary>
	[Export] public float SpawningActionDuration { get; set; } = 3.5f;

	/// <summary>
	/// Time spent in the stand-up action before normal behavior resumes.
	/// </summary>
	[Export] public float StandingUpActionDuration { get; set; } = 2.33f;

	/// <summary>
	/// Time spent in the hit reaction action before normal behavior resumes.
	/// </summary>
	[Export] public float HitActionDuration { get; set; } = 0.35f;

	/// <summary>
	/// Time spent in the melee attack action before normal behavior resumes.
	/// </summary>
	[Export] public float MeleeAttackActionDuration { get; set; } = 1.0f;

	/// <summary>
	/// Time spent in the ranged attack action before normal behavior resumes.
	/// </summary>
	[Export] public float RangedAttackActionDuration { get; set; } = 1.0f;

	/// <summary>
	/// Time spent in the dying action before the enemy is fully dead.
	/// </summary>
	[Export] public float DyingActionDuration { get; set; } = 2.0f;

	/// <summary>
	/// Returns the configured duration for an action.
	/// </summary>
	public float GetActionDuration(EnemyAction action)
	{
		return action switch
		{
			EnemyAction.Spawning => SpawningActionDuration,
			EnemyAction.StandingUp => StandingUpActionDuration,
			EnemyAction.Hit => HitActionDuration,
			EnemyAction.MeeleAttack => MeleeAttackActionDuration,
			EnemyAction.RangedAttack => RangedAttackActionDuration,
			EnemyAction.Dying => DyingActionDuration,
			_ => 0,
		};
	}
}
