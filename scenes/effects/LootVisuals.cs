using Godot;

/// <summary>
/// Maps an item to the color used for its loot beam / glow. Centralized so the whole
/// palette is tunable in one place. Equipables are colored by rarity (ARPG ladder);
/// currency and everything else fall back to type/neutral colors.
/// </summary>
public static class LootVisuals
{
	// Rarity ladder — see EquipableItemRarity.
	public static readonly Color Common = new("dfe4ea");
	public static readonly Color Uncommon = new("4caf50");
	public static readonly Color Rare = new("3b82f6");
	public static readonly Color Legendary = new("ff9d2e");
	public static readonly Color Unique = new("a855f7");

	// Type colors for non-equipables.
	public static readonly Color GoldColor = new("ffcf40");
	public static readonly Color Neutral = Common;

	/// <summary>
	/// The beam/glow color for a piece of loot, based on its rarity (equipables) or
	/// type (currency, everything else).
	/// </summary>
	public static Color ResolveColor(Item item)
	{
		if (item is EquipableItem equipable)
		{
			return RarityColor(equipable.Rarity);
		}

		if (item is Gold)
		{
			return GoldColor;
		}

		return Neutral;
	}

	public static Color RarityColor(EquipableItemRarity rarity) => rarity switch
	{
		EquipableItemRarity.Uncommon => Uncommon,
		EquipableItemRarity.Rare => Rare,
		EquipableItemRarity.Legendary => Legendary,
		EquipableItemRarity.Unique => Unique,
		_ => Common,
	};
}
