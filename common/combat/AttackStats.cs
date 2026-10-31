using Godot;

/// <summary>
/// The damage range and critical-hit chance of one hit, resolved per attack (see <see cref="PlayerStats.ResolveAttack"/>),
/// and the one rule that rolls it: an amount from the range, then a critical check that multiplies it. Melee contacts,
/// projectiles and spells all roll through it, so a seeded run draws in the same order wherever the hit lands.
/// </summary>
public readonly record struct AttackStats(float MinDamage, float MaxDamage, float CritChance)
{
	public const float CriticalMultiplier = 2;

	/// <summary>The mean non-critical hit, for effects that need one number, such as a ward's capacity.</summary>
	public float Average => (MinDamage + MaxDamage) / 2;

	/// <summary>The range multiplied by <paramref name="scale"/>; the critical chance stays.</summary>
	public AttackStats Scaled(float scale) => this with { MinDamage = MinDamage * scale, MaxDamage = MaxDamage * scale };

	/// <summary>Draws the amount from the range, then the critical check; the amount includes the critical multiplier.</summary>
	public (float Amount, bool Critical) Roll(RandomNumberGenerator random)
	{
		float amount = random.RandfRange(MinDamage, MaxDamage);
		bool critical = random.Randf() < CritChance;
		return (critical ? amount * CriticalMultiplier : amount, critical);
	}
}