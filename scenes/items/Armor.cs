using Godot;
using System;

[Tool]
[GlobalClass]
public partial class Armor : EquipableItem
{
	/// <summary>
	/// Strength required to wear this armor effectively.
	/// </summary>
	[Export] public int RequiredStrength { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Authored armor bonus at quality scale 1. Stacks up with other items' armor bonus; the wearer gets
	/// <see cref="ScaledArmorPoints"/>.
	/// </summary>
	[Export] public int ArmorPoints { get; set => SetValue(ref field, value); } = 0;
	/// <summary>Armor bonus after quality scaling, rounded to whole points.</summary>
	public int ScaledArmorPoints => (int)ScaleWhole(ArmorPoints);
	/// <summary>
	/// Speed modifier for this armor. Quality does not scale it.
	/// </summary>
	[Export] public float SpeedModifier { get; set => SetValue(ref field, value); } = 1.0f;

	protected override System.Collections.Generic.IEnumerable<StatModifier> BuildIntrinsicModifiers()
	{
		yield return new StatModifier { Stat = StatType.Armor, Op = ModifierOp.Flat, Value = ScaledArmorPoints };
		// SpeedModifier are authored as multipliers (1.0 = no change), so a
		// value of 0.9 becomes a -10% percent modifier.
		yield return new StatModifier { Stat = StatType.Speed, Op = ModifierOp.Percent, Value = SpeedModifier - 1f };
	}
}