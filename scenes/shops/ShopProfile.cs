using Godot;
using System;
using System.Collections.Generic;

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

	/// <summary>Whether a drawn definition may go on the shelf: never a quest item, and cursed gear only for a merchant who sells it.</summary>
	internal bool CanStock(Item item) => !item.IsQuestItem
		&& (SellsKnownCursedGear || item is not EquipableItem { AlwaysCursed: true } and not EquipableItem { IsCursed: true });

	/// <summary>Authoring problems: missing pricing, stock without a pool or a unique ID, invalid pools, and curses a merchant would not sell.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		if (Pricing == null) problems.Add($"{ResourcePath}: assign a pricing profile.");
		if (!SellsKnownCursedGear && CreationPolicy?.CurseChance > 0) problems.Add($"{ResourcePath}: the creation policy rolls curses the merchant does not sell.");
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (ShopStockProfile stock in Stock)
		{
			if (stock?.Pool == null || string.IsNullOrWhiteSpace(stock.Id) || stock.Id.Contains(':') || !ids.Add(stock.Id) || stock.Quantity < 1)
			{
				problems.Add($"{ResourcePath}: every stock needs a pool, a unique ID without ':' and a positive quantity.");
				continue;
			}
			problems.AddRange(stock.Pool.Validate());
		}
		return problems.ToArray();
	}

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
