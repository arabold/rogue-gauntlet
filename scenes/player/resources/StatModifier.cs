using Godot;

/// <summary>How a <see cref="StatModifier"/> combines with a stat's resolved base value.</summary>
public enum ModifierOp
{
	/// <summary>Added to the base before any percentage scaling.</summary>
	Flat,
	/// <summary>A fraction (0.6 = +60%) summed with other percent modifiers, applied after flats.</summary>
	Percent,
}

/// <summary>
/// A single composable change to one <see cref="StatType"/>: the atomic unit shared by
/// buffs and item affixes. <see cref="PlayerStats"/> resolves a stat as
/// (base + sum of flat modifiers) * (1 + sum of percent modifiers).
/// </summary>
[GlobalClass]
public partial class StatModifier : Resource
{
	[Export] public StatType Stat { get; set; }
	[Export] public ModifierOp Op { get; set; } = ModifierOp.Flat;
	[Export] public float Value { get; set; }
}
