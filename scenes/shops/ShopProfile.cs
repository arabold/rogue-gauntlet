using Godot;
using System;

[Flags]
public enum ShopItemCategory
{
	None = 0, Weapon = 1, Armor = 2, Potion = 4, Scroll = 8, Food = 16, Tool = 32, Jewelry = 64,
}

/// <summary>Reusable merchant rules. Each placed keeper rolls and owns their own shelf.</summary>
[GlobalClass]
public partial class ShopProfile : Resource
{
	[Export] public string DisplayName { get; set; } = "Merchant";
	[Export] public ShopStockProfile[] Stock { get; set; } = [];
	[Export] public ItemCreationPolicy CreationPolicy { get; set; }
	[Export] public ShopPricingProfile Pricing { get; set; }
	[Export] public ShopItemCategory Buys { get; set; }
	[Export] public bool BuysKnownCursedGear { get; set; }
	[Export] public bool SellsKnownCursedGear { get; set; }

	/// <summary>Whether the merchant buys this item given its visible traits.</summary>
	internal bool AcceptsForSale(Item item) => !item.IsQuestItem
		&& (Buys & Category(item)) != ShopItemCategory.None
		&& (BuysKnownCursedGear || item is not EquipableItem { CurseStatusKnown: true, IsCursed: true });

	private static ShopItemCategory Category(Item item) => item switch
	{
		Weapon => ShopItemCategory.Weapon,
		Armor => ShopItemCategory.Armor,
		Scroll => ShopItemCategory.Scroll,
		FoodItem => ShopItemCategory.Food,
		ConsumableItem { HasIdentity: true } => ShopItemCategory.Potion,
		LockpickItem => ShopItemCategory.Tool,
		IdentifiableEquipableItem => ShopItemCategory.Jewelry,
		_ => ShopItemCategory.None,
	};
}
