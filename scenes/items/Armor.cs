using Godot;
using System;

/// <summary>
/// How heavy a body armor or helmet is. Heavier pieces carry more armor; a heavy body piece slows its wearer and a light
/// one quickens them (<see cref="Armor.SpeedModifier"/>). The balance model sizes both from the weight; the game has no
/// passive evasion, so footwork is movement speed.
/// </summary>
public enum ArmorWeight
{
	Light,
	Medium,
	Heavy,
}

[Tool]
[GlobalClass]
public partial class Armor : EquipableItem
{
	/// <summary>The weight class the balance model sizes this body armor or helmet for; shields ignore it.</summary>
	[Export] public ArmorWeight Weight { get; set => SetValue(ref field, value); } = ArmorWeight.Medium;
	/// <summary>
	/// Body armor's or a helmet's model on the hero, in its bone's rest frame (origin at the chest or head bone, +Z
	/// forward). <see cref="Item.Scene"/> stays the pickup and inventory model. Unassigned, nothing shows on the body
	/// and the class model's own hat or cape stays visible.
	/// </summary>
	[Export] public PackedScene WornScene { get; set => SetValue(ref field, value); }
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