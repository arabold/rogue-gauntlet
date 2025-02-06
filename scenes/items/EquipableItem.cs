using Godot;
using Godot.Collections;
using System;


/// <summary>
/// Flags for equipment slots.
/// </summary>
[Flags]
public enum ValidSlots
{
	Head = EquipmentSlot.Head,
	Chest = EquipmentSlot.Chest,
	Hands = EquipmentSlot.Hands,
	Legs = EquipmentSlot.Legs,
	Feet = EquipmentSlot.Feet,
	Neck = EquipmentSlot.Neck,
	LeftRing = EquipmentSlot.LeftRing,
	RightRing = EquipmentSlot.RightRing,
	WeaponHand = EquipmentSlot.WeaponHand,
	ShieldHand = EquipmentSlot.ShieldHand,
	Arrows = EquipmentSlot.Arrows,
}

public enum EquipableItemRarity
{
	Common = 0,
	Uncommon = 1,
	Rare = 2,
	Legendary = 3,
	Unique = 4,
}

[GlobalClass]
public partial class EquipableItem : BuffedItem
{
	/// <summary>
	/// Type of this item
	/// </summary>
	[Export] public ValidSlots ValidSlots { get; set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Rarity of this item
	/// </summary>
	[Export]
	public EquipableItemRarity Rarity { get; set => SetValue(ref field, value); } = EquipableItemRarity.Common;

	public virtual void OnEquipped(Player player)
	{
		ApplyBuff(player);
	}

	public virtual void OnUnequipped(Player player)
	{
		RemoveBuff(player);
	}
}
