using Godot;

/// <summary>Optional bounded low-health override, with hysteresis to prevent repeated retreat at low health.</summary>
[GlobalClass]
public partial class EnemyRetreatCondition : Resource
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float EnterHealthFraction { get; set; } = .25f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float ExitHealthFraction { get; set; } = .4f;
	[Export] public float DesiredDistance { get; set; } = 5;
	[Export] public float MaximumDuration { get; set; } = 2;
}