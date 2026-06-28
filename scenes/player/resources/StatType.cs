/// <summary>
/// Identifies a single player stat a <see cref="StatModifier"/> can target. Covers the
/// primary attributes that form the basis for character builds and the secondary
/// combat/utility stats they feed into; <see cref="PlayerStats"/> resolves each
/// independently through the modifier pipeline.
/// </summary>
public enum StatType
{
	// Primary attributes
	Strength,
	Dexterity,
	Vitality,
	Intelligence,

	// Secondary / derived stats
	MaxHealth,
	Speed,
	Accuracy,
	MinDamage,
	MaxDamage,
	CritChance,
	Armor,
	Evasion,
}
