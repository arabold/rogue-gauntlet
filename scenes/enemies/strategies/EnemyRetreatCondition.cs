using Godot;

/// <summary>Optional bounded low-health override, with hysteresis to prevent repeated retreat at low health.</summary>
[Tool]
[GlobalClass]
public partial class EnemyRetreatCondition : Resource
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float EnterHealthFraction { get; set; } = .25f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float ExitHealthFraction { get; set; } = .4f;
	[Export] public float DesiredDistance { get; set; } = 5;
	[Export] public float MaximumDuration { get; set; } = 2;

	public void Validate(MonsterValidationContext context) =>
		context.Check(Probability(EnterHealthFraction) && Probability(ExitHealthFraction) && EnterHealthFraction < ExitHealthFraction
			&& float.IsFinite(DesiredDistance) && DesiredDistance > 0 && float.IsFinite(MaximumDuration) && MaximumDuration > 0,
			"RetreatCondition: require ordered health thresholds and positive distance/duration.");

	private static bool Probability(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
