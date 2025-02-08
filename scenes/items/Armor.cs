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

	public override void OnEquipped(Player player)
	{
		// Apply stats
		base.OnEquipped(player);
		var stats = player.Stats;
		stats.BaseArmor += ArmorPoints;
		stats.AccuracyModifier *= AccuracyModifier;
		stats.SpeedModifier *= SpeedModifier;
	}

	public override void OnUnequipped(Player player)
	{
		// Reset stats
		var stats = player.Stats;
		stats.BaseArmor -= ArmorPoints;
		stats.AccuracyModifier /= AccuracyModifier;
		stats.SpeedModifier /= SpeedModifier;
		base.OnUnequipped(player);
	}

	public void UpgradeLevel()
	{
		Level++;
		ArmorPoints += 1;
		RequiredStrength -= 1;
	}
}
