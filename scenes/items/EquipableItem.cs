using Godot;
using Godot.Collections;

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
	[Export] public Array<EquipmentSlot> ValidSlots { get; set => SetValue(ref field, value); } = new Array<EquipmentSlot>();
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
