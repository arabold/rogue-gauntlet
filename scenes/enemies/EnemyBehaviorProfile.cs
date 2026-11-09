using Godot;

/// <summary>
/// Defines designer-authored AI tuning for an enemy type.
/// </summary>
/// <remarks>
/// Create one profile per monster variant so scenes can share behavior code while changing
/// detection, roaming, combat ranges, and action timing through resources.
/// </remarks>
[GlobalClass]
public partial class EnemyBehaviorProfile : Resource
{
	[ExportGroup("Combat")]
	[Export] public EnemyCombatProfile Combat { get; set; }
	[Export] public Godot.Collections.Array<BossPhaseProfile> Phases { get; set; } = [];
	[ExportGroup("Perception and lifecycle")]
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
	/// Fraction of detection range within which the enemy detects the player regardless of
	/// facing ("hearing"). Still requires an unobstructed line of sight, so it only triggers
	/// when they share a room rather than through walls.
	/// </summary>
	[Export] public float CloseDetectionRangeMultiplier { get; set; } = 0.5f;

	/// <summary>
	/// Keep the monster in the room it spawns in: it walks anywhere inside, but holds at the room's edge rather than
	/// follow a target out. Bosses guarding an arena use it; turning it off lets them roam like any other monster.
	/// </summary>
	[Export] public bool StayInHomeRoom { get; set; }

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
	/// Number of random roam points to try before pausing and retrying later.
	/// </summary>
	[Export] public int RoamTargetAttempts { get; set; } = 8;

	/// <summary>
	/// How long the enemy investigates a target's last known position after losing it
	/// before giving up and returning to patrol.
	/// </summary>
	[Export] public float SearchDuration { get; set; } = 4.0f;

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
			EnemyAction.Dying => DyingActionDuration,
			_ => 0,
		};
	}

	/// <summary>Returns errors for phase counts, thresholds and complete phase configurations.</summary>
	public string[] ValidateCombat()
	{
		var errors = new System.Collections.Generic.List<string>();
		if (Combat == null) errors.Add("base combat profile is missing");
		else errors.AddRange(Combat.Validate());
		if (Phases == null || Phases.Count > 2) errors.Add("boss behavior supports at most two health phase transitions");
		else
		{
			float previous = 1;
			foreach (var phase in Phases)
			{
				if (phase == null || !float.IsFinite(phase.EnterHealthFraction) || phase.EnterHealthFraction <= 0
					|| phase.EnterHealthFraction >= previous || phase.EnterHealthFraction >= 1 || phase.Combat == null)
					errors.Add("boss phase thresholds must be strictly descending and each phase must have a combat profile");
				else
				{
					previous = phase.EnterHealthFraction;
					errors.AddRange(phase.Combat.Validate());
				}
			}
		}
		return errors.ToArray();
	}
}
