using Godot;
using System;

[GlobalClass]
public partial class Armor : EquipableItem
{
	/// <summary>
	/// Armor level modifier (-1, +0, +1, etc.); affects armor stats.
	/// </summary>
	[Export] public int Level { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Strength required to wear this armor effectively.
	/// </summary>
	[Export] public int RequiredStrength { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Armor bonus for this item (absolute value). Stacks up with other items' armor bonus.
	/// </summary>
	[Export] public int ArmorPoints { get; set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Accuracy modifier for this armor
	/// </summary>
	[Export] public float AccuracyModifier { get; set => SetValue(ref field, value); } = 1.0f;
	/// <summary>
	/// Speed modifier for this armor
	/// </summary>
	[Export] public float SpeedModifier { get; set => SetValue(ref field, value); } = 1.0f;

	protected override System.Collections.Generic.IEnumerable<StatModifier> BuildStatModifiers()
	{
		foreach (StatModifier modifier in base.BuildStatModifiers())
		{
			yield return modifier;
		}

		yield return new StatModifier { Stat = StatType.Armor, Op = ModifierOp.Flat, Value = ArmorPoints };
		// AccuracyModifier/SpeedModifier are authored as multipliers (1.0 = no change), so a
		// value of 0.9 becomes a -10% percent modifier.
		yield return new StatModifier { Stat = StatType.Accuracy, Op = ModifierOp.Percent, Value = AccuracyModifier - 1f };
		yield return new StatModifier { Stat = StatType.Speed, Op = ModifierOp.Percent, Value = SpeedModifier - 1f };
	}

	public void UpgradeLevel()
	{
		Level++;
		ArmorPoints += 1;
		RequiredStrength -= 1;
	}
}
