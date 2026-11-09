using Godot;

[Tool]
[GlobalClass]
public partial class HealingBuff : PeriodicBuff
{
	[Export] public int HealthPointsPerTick = 0;

	/// <summary>Regeneration only helps a hurt target.</summary>
	public override bool IsUsefulOn(IBuffTarget target) => base.IsUsefulOn(target)
		&& (HealthPointsPerTick <= 0 || target.Health?.Fraction < 1);

	public override void OnTick(IBuffTarget target)
	{
		if (HealthPointsPerTick > 0 && target?.Health != null)
		{
			target.Health.Heal(HealthPointsPerTick);
		}
	}
}
