using Godot;

/// <summary>
/// Central source of the text colors used to convey item rarity in the UI, so the hover
/// tooltip, context menu, and any future surface stay consistent. These are readable
/// foreground colors; the low-alpha slot background tints live on <see cref="ItemSlotPanel"/>.
/// </summary>
public static class RarityPalette
{
	private static readonly Color Default = new(0.85f, 0.85f, 0.85f);

	public static Color TextColor(EquipableItemRarity rarity) => rarity switch
	{
		EquipableItemRarity.Common => new Color(0.85f, 0.85f, 0.85f),
		EquipableItemRarity.Uncommon => new Color(0.40f, 0.85f, 0.40f),
		EquipableItemRarity.Rare => new Color(0.40f, 0.60f, 1.00f),
		EquipableItemRarity.Legendary => new Color(0.80f, 0.40f, 0.90f),
		EquipableItemRarity.Unique => new Color(1.00f, 0.70f, 0.20f),
		_ => Default,
	};

	/// <summary>Color for an item that has no rarity (consumables, quest items, …).</summary>
	public static Color TextColor(Item item) =>
		item is EquipableItem equipable ? TextColor(equipable.Rarity) : Default;
}
