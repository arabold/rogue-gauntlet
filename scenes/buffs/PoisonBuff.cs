using Godot;

/// <summary>
/// Takes whole points of health each tick, past armor. Poison potions' and spoiled food's points come from the balance
/// model (<c>scripts/balance/params.py</c>) through <c>simulate.py derive</c>; healing potions cure the poisoned condition.
/// </summary>
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
			if (CombatTrace.Enabled)
				CombatTrace.Write(target.Health, "poison.tick", ("buff", Name), ("amount", DamagePointsPerTick),
					("health", target.Health.CurrentHealth));
		}
	}
}
