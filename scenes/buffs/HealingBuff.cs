using Godot;

/// <summary>
/// Restores a fixed amount of health each tick, so a potion matters less as the hero's health grows and the next size
/// takes over. The healing potions' and food's points come from the balance model (<c>scripts/balance/params.py</c>)
/// through <c>simulate.py derive</c>.
/// </summary>
[Tool]
[GlobalClass]
public partial class HealingBuff : PeriodicBuff
{
	[Export(PropertyHint.Range, "0,100,0.01,or_greater")] public float HealthPointsPerTick { get; set; }

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
