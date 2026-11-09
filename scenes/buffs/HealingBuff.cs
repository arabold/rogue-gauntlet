using Godot;

[Tool]
[GlobalClass]
public partial class HealingBuff : PeriodicBuff
{
	[Export] public int HealthPointsPerTick = 0;

	public override void OnTick(IBuffTarget target)
	{
		if (HealthPointsPerTick > 0 && target?.Health != null)
		{
			target.Health.Heal(HealthPointsPerTick);
		}
	}
}
