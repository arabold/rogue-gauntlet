using Godot;
using System;

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
[Tool]
[GlobalClass]
public partial class StatModifier : Resource
{
	[Export] public StatType Stat { get; set; }
	[Export] public ModifierOp Op { get; set; } = ModifierOp.Flat;
	[Export] public float Value { get; set; }

	/// <summary>True when <see cref="Value"/> is a fraction shown as a percentage, e.g. 0.1 for +10%.</summary>
	public bool IsFraction => Op == ModifierOp.Percent || Stat == StatType.CritChance;

	/// <summary>The rounding step for rolled and scaled values: whole percents for fractions, else whole points.</summary>
	public float Unit => IsFraction ? 0.01f : 1f;

	/// <summary><paramref name="value"/> rounded half away from zero to <see cref="Unit"/>.</summary>
	public float Round(float value) => MathF.Round(value / Unit, MidpointRounding.AwayFromZero) * Unit;

	/// <summary>The signed value as players read it: "+3", "-15" or "+10%".</summary>
	public string FormatValue() => FormatValue(Value, IsFraction);

	public static string FormatValue(float value, bool isFraction)
	{
		string sign = value < 0 ? "" : "+";
		return isFraction ? $"{sign}{value * 100:0.#}%" : $"{sign}{value:0.##}";
	}
}
