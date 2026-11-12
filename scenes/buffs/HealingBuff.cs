using Godot;

/// <summary>
/// Restores a share of the target's max health each tick, so a potion is worth the same at every level. The shares
/// of the healing potions come from the balance model (<c>scripts/balance/params.py</c>) through <c>simulate.py derive</c>.
/// </summary>
[Tool]
[GlobalClass]
public partial class HealingBuff : PeriodicBuff
{
	[Export(PropertyHint.Range, "0,1,0.005")] public float MaxHealthFractionPerTick { get; set; }

	/// <summary>Regeneration only helps a hurt target.</summary>
	public override bool IsUsefulOn(IBuffTarget target) => base.IsUsefulOn(target)
		&& (MaxHealthFractionPerTick <= 0 || target.Health?.Fraction < 1);

	public override void OnTick(IBuffTarget target)
	{
		if (MaxHealthFractionPerTick > 0 && target?.Health != null)
		{
			target.Health.Heal(MaxHealthFractionPerTick * target.Health.MaxHealth);
		}
	}
}
