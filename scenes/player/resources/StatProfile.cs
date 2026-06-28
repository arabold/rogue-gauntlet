using Godot;

/// <summary>
/// Designer-authored rules that turn primary attributes into secondary stats. This is the
/// seam classes and races plug into later: each supplies its own profile to reshape how a
/// build's attributes pay off. The player references one via <see cref="PlayerStats.Profile"/>.
/// A null profile means no attribute derivation (the editor/test fallback), so secondary
/// stats equal their base values.
/// </summary>
[GlobalClass]
public partial class StatProfile : Resource
{
	/// <summary>Max health gained per point of Vitality.</summary>
	[Export] public float HealthPerVitality { get; set; } = 5f;
	/// <summary>Min and max damage gained per point of Strength.</summary>
	[Export] public float DamagePerStrength { get; set; } = 0.1f;
	/// <summary>Min and max damage gained per point of Intelligence (for casters; 0 by default).</summary>
	[Export] public float DamagePerIntelligence { get; set; } = 0f;
	/// <summary>Critical-hit chance (0-1) gained per point of Dexterity.</summary>
	[Export] public float CritChancePerDexterity { get; set; } = 0.005f;
	/// <summary>Evasion gained per point of Dexterity.</summary>
	[Export] public float EvasionPerDexterity { get; set; } = 0.2f;
}
