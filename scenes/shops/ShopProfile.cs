using Godot;
using System;
using System.Collections.Generic;

[Flags]
public enum ShopItemCategory
{
	None = 0, Weapon = 1, Armor = 2, Potion = 4, Scroll = 8, Food = 16, Tool = 32, Jewelry = 64,
}

/// <summary>
/// Reusable merchant rules. Each placed keeper rolls and owns their own shelf. Merchants are reputable: they sell
/// neither cursed gear nor tainted food, and buy no gear known to be cursed.
/// </summary>
[GlobalClass]
public partial class ShopProfile : Resource
{
	[Export] public string DisplayName { get; set; } = "Merchant";
	[Export] public ShopStockProfile[] Stock { get; set; } = [];
	[Export] public ItemCreationPolicy CreationPolicy { get; set; }
	[Export] public ShopPricingProfile Pricing { get; set; }
	[Export] public ShopItemCategory Buys { get; set; }

	/// <summary>Whether a drawn definition may go on the shelf: never a quest item or gear that is always cursed.</summary>
	internal static bool CanStock(Item item) => !item.IsQuestItem && item is not EquipableItem { AlwaysCursed: true };

	/// <summary>Authoring problems: missing pricing, stock without a pool or a unique ID, invalid pools, and a creation policy that rolls curses or tainted food.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		if (Pricing == null) problems.Add($"{ResourcePath}: assign a pricing profile.");
		if (CreationPolicy == null || CreationPolicy.CurseChance > 0 || CreationPolicy.RollFoodContamination)
			problems.Add($"{ResourcePath}: assign a creation policy that rolls neither curses nor tainted food.");
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
		&& item is not EquipableItem { CurseStatusKnown: true, IsCursed: true };

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
