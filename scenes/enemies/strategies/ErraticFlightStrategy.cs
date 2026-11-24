using Godot;

/// <summary>
/// A flyer's flutter around its <see cref="Pattern"/>: wherever the pattern would hold, the monster hovers instead,
/// darting to a fresh point around the target every <see cref="BurstInterval"/>; and a pattern's approach within
/// <see cref="ErraticRange"/> and sight closes in darts of <see cref="BurstLength"/>, swung off the straight line, down to
/// its striking distance. It
/// holds at its striking distance while it may attack and at its current distance while it may not. Retreats, and
/// approaches from farther away, follow the pattern unchanged. The giant bat flies this way around bite and dart.
/// </summary>
[Tool]
[GlobalClass]
public partial class ErraticFlightStrategy : EnemyMovementStrategy
{
	/// <summary>The positioning the flutter wraps, such as bite and dart.</summary>
	[Export] public EnemyMovementStrategy Pattern { get; set; }

	/// <summary>Most degrees each dart swings around the target, either way.</summary>
	[Export(PropertyHint.Range, "0,180,5,suffix:°")] public float Swing { get; set; } = 50;

	/// <summary>Seconds between darts, varied by ±30%; a dart that arrives sooner picks the next at once.</summary>
	[Export(PropertyHint.Range, "0.1,3,0.05,suffix:s")] public float BurstInterval { get; set; } = .45f;

	/// <summary>How much closer each dart of an approach brings the monster.</summary>
	[Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")] public float BurstLength { get; set; } = 2.5f;

	/// <summary>Farther than this, or out of sight, an approach follows the floor's chase field instead of darting.</summary>
	[Export(PropertyHint.Range, "1,30,0.5,suffix:m")] public float ErraticRange { get; set; } = 8;

	public override void AttackAccepted(EnemyContext context, AttackIdentity identity) => Pattern.AttackAccepted(context, identity);

	public override EnemyMovementIntent? Preempt(EnemyContext context) => Pattern.Preempt(context);

	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		var intent = Pattern.Evaluate(context);
		float distance = context.HorizontalTargetDistance;
		float reach = context.CombatProfile.PrimaryOffense.EffectiveMaximumRange;
		if (intent.Goal == EnemyMovementGoal.Hold)
			return Hover(intent.AllowAttack, intent.AllowAttack ? Mathf.Clamp(distance, reach * .6f, reach * .85f) : Mathf.Max(distance, reach));
		// Within reach too: an approach there would stop the body while its attack readies.
		if (intent.Goal == EnemyMovementGoal.Approach && distance <= ErraticRange && context.HasLineOfSightToTarget())
			return Hover(intent.AllowAttack, Mathf.Max(reach * .85f, distance - BurstLength));
		return intent;
	}

	// Snapped, so the decision only changes when the distance does by a step, not every frame.
	private EnemyMovementIntent Hover(bool allowAttack, float distance) =>
		new(EnemyMovementGoal.Hover, allowAttack, Mathf.Snapped(distance, .25f), OrbitStep: Swing, Burst: BurstInterval);

	public override void Validate(MonsterValidationContext context, EnemyCombatProfile combat)
	{
		context.Check(Pattern != null && Pattern is not ErraticFlightStrategy, "MovementStrategy: give the erratic flight a pattern to flutter around.");
		if (Pattern == null) return;
		RequirePrimary(context, combat);
		Pattern.Validate(context, combat);
	}
}
