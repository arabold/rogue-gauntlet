using Godot;

/// <summary>
/// A class's rules for how its attributes pay off and grow. Attributes scale shared curves by percent around
/// <see cref="AttributeBaseline"/>, so a class's strengths keep their proportion at every level and with every piece of
/// gear. The player references one via <see cref="PlayerStats.Profile"/>; a null profile means no attribute
/// derivation (the editor/test fallback), so secondary stats equal their base values. The values come from the
/// balance model (<c>scripts/balance/params.py</c>) through <c>simulate.py derive</c>.
/// </summary>
[GlobalClass]
public partial class StatProfile : Resource
{
	/// <summary>The attribute value at which an attribute neither adds nor removes anything.</summary>
	public const float AttributeBaseline = 10;

	[ExportGroup("Payoff")]
	/// <summary>Share of max health gained per point of Vitality above <see cref="AttributeBaseline"/> (lost below it).</summary>
	[Export] public float HealthPercentPerVitality { get; set; } = 0.04f;
	/// <summary>Share of weapon and unarmed damage gained per point of Strength above <see cref="AttributeBaseline"/>.</summary>
	[Export] public float PhysicalDamagePercentPerStrength { get; set; } = 0.03f;
	/// <summary>Share of a wand's or staff's missile and spell damage gained per point of Intelligence above <see cref="AttributeBaseline"/>.</summary>
	[Export] public float MagicDamagePercentPerIntelligence { get; set; } = 0.03f;
	/// <summary>Max mana gained per point of Intelligence.</summary>
	[Export] public float ManaPerIntelligence { get; set; } = 5f;
	/// <summary>Critical-hit chance (0-1) gained per point of Dexterity.</summary>
	[Export] public float CritChancePerDexterity { get; set; } = 0.01f;

	[ExportGroup("Growth per level")]
	/// <summary>Attribute points gained per level; fractions accumulate, so 0.25 adds a point every four levels.</summary>
	[Export] public float StrengthPerLevel { get; set; } = 0.25f;
	[Export] public float DexterityPerLevel { get; set; } = 0.25f;
	[Export] public float VitalityPerLevel { get; set; } = 0.25f;
	[Export] public float IntelligencePerLevel { get; set; } = 0.25f;
}
