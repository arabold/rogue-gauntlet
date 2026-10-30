using Godot;

/// <summary>
/// What one cast is worth, captured when the cast is accepted: the spell level (the casting weapon's tier) and the
/// caster's weapon hit. Spell definitions author damage as scales of <see cref="Hit"/>, so the weapon's damage, quality,
/// affixes and crit raise the spell as they raise its missiles. Later stat or equipment changes never alter a released
/// spell.
/// </summary>
public readonly record struct SpellPower(int Level, AttackStats Hit);

/// <summary>The level cap every spell definition shares (a tier-5 weapon casts level 5) and its tooltip wording.</summary>
public static class SpellLevels
{
	public const int Max = 5;

	/// <summary>A damage range as tooltip text, "6–14 damage", or "9 damage" when both ends round alike.</summary>
	public static string Damage(AttackStats hit)
	{
		int min = Points(hit.MinDamage), max = Points(hit.MaxDamage);
		return min == max ? $"{min} damage" : $"{min}–{max} damage";
	}

	/// <summary>Whole points as damage numbers show them: never 0 for an amount that does damage.</summary>
	public static int Points(float amount) => Mathf.Max(1, Mathf.RoundToInt(amount));
}