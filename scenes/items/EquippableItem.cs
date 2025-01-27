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
public partial class EquippableItem : Item
{
	[Export] public EquippableItemType Type = EquippableItemType.Weapon;
	/// <summary>
	/// Minimum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public int DamageMin = 0;
	/// <summary>
	/// Maximum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public int DamageMax = 0;
	/// <summary>
	/// Armor bonus for this item (absolute value). Stacks up with other items' armor bonus.
	/// </summary>
	[Export] public int Armor = 0;
	/// <summary>
	/// Accuracy modifier for this weapon
	/// </summary>
	[Export] public float AccuracyModifier = 1.0f;
	/// <summary>
	/// Speed modifier for this weapon
	/// </summary>
	[Export] public float SpeedModifier = 1.0f;
	/// <summary>
	/// Reach modifier for this weapon (melee weapons only)
	/// </summary>
	[Export] public float ReachModifier = 1.0f;
	/// <summary>
	/// Rarity of this item
	/// </summary>
	[Export] public EquippableItemRarity Rarity = EquippableItemRarity.Common;

	// public void Equip(Player player)
	// {
	// 	GD.Print($"{Name} is equipped");
	// 	SignalBus.EmitItemEquipped(this);
	// }

	// public void Unequip(Player player)
	// {
	// 	GD.Print($"{Name} is unequipped");
	// 	SignalBus.EmitItemUnequipped(this);
	// }
}
