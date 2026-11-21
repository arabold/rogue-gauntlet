using Godot;

/// <summary>
/// The trade's Sell tab: the hero's pack, one niche per place in it. What the keeper would not buy right now (worn gear,
/// goods outside the keeper's trade) dims; the alternate prompt sells a whole stack.
/// </summary>
public partial class SellTab : TradeTab
{
	protected override string Verb => "Sell";
	protected override string Receipt => "Sold";
	protected override string Title => "Pack";
	protected override string Count => $"{Player.Inventory.Items.Count} / {Player.Inventory.Capacity}";
	protected override int Niches => Mathf.Max(Player.Inventory.Capacity, Player.Inventory.Items.Count);

	protected override void Fill(ItemSlotPanel panel, int index)
	{
		Inventory inventory = Player.Inventory;
		InventoryItemSlot entry = index < inventory.Items.Count ? inventory.Items[index] : null;
		panel.SetItem(entry, entry != null && inventory.IsEquipped(entry));
		panel.Selectable = entry != null;
		panel.Dimmed = entry != null && !Shop.QuoteSale(Player, entry, 1).IsAvailable;
	}

	protected override (ShopQuote One, ShopQuote Stack) Quote(int index)
	{
		if (index >= Player.Inventory.Items.Count) return default;
		InventoryItemSlot entry = Player.Inventory.Items[index];
		return (Shop.QuoteSale(Player, entry, 1), entry.Quantity > 1 ? Shop.QuoteSale(Player, entry, entry.Quantity) : default);
	}

	protected override string Commit(int index, ShopQuote displayed) => Shop.Sell(Player, Player.Inventory.Items[index], displayed);
}