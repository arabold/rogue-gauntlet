using Godot;

public enum EquippableItemType
{
	Helmet = 0,
	Armor = 1,
	Gloves = 2,
	Boots = 3,
	Weapon = 4,
	Shield = 5,
	Pants = 6,
	Amulet = 7,
	Ring = 8,
}

public enum EquippableItemRarity
{
	Common = 0,
	Uncommon = 1,
	Rare = 2,
	Legendary = 3,
	Unique = 4,
}

[GlobalClass]
public partial class EquippableItem : BuffedItem
{
	/// <summary>
	/// Type of this item
	/// </summary>
	[Export] public EquippableItemType Type { get; set => SetValue(ref field, value); } = EquippableItemType.Weapon;
	/// <summary>
	/// Rarity of this item
	/// </summary>
	[Export]
	public EquippableItemRarity Rarity { get; set => SetValue(ref field, value); } = EquippableItemRarity.Common;

	public virtual void OnEquipped(Player player)
	{
		ApplyBuff(player);
	}

	public virtual void OnUnequipped(Player player)
	{
		RemoveBuff(player);
	}
}
