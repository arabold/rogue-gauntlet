/// <summary>
/// What one cast is worth, captured when the cast is accepted: the spell level (the casting weapon's tier) and the
/// caster's weapon hit. Spell definitions author damage as scales of <see cref="Hit"/>, so the weapon's damage, quality,
/// affixes and crit raise the spell as they raise its missiles. Later stat or equipment changes never alter a released
/// spell.
/// </summary>
public readonly record struct SpellPower(int Level, AttackStats Hit);

/// <summary>The level cap every spell definition shares; a tier-5 weapon casts level 5.</summary>
public static class SpellLevels
{
	public const int Max = 5;
}