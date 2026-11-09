using Godot;

[Tool]
[GlobalClass]
public partial class PoisonBuff : PeriodicBuff
{
	[Export] public int DamagePointsPerTick = 0;

	public override bool IsValid => base.IsValid && DamagePointsPerTick > 0;

	public override void OnTick(IBuffTarget target)
	{
		if (DamagePointsPerTick > 0 && target?.Health != null)
		{
			target.Health.TakeDamage(DamagePointsPerTick);
		}
	}
}
