using System.Linq;

/// <summary>
/// The trade's Sell tab: the hero's pack, as the Pack tab shows it. What the keeper would not buy right now (worn gear,
/// goods outside the keeper's trade) dims; the alternate prompt sells a whole stack.
/// </summary>
public partial class SellTab : TradeTab
{
	protected override string Verb => "Sell";
	protected override string Receipt => "Sold";
	protected override string Title => "Pack";

	protected override void Fill()
	{
		ShowPack();
		foreach (ItemSlotPanel panel in Grid.GetChildren().OfType<ItemSlotPanel>())
		{
			panel.Dimmed = panel.Slot != null && !Shop.QuoteSale(Player, panel.Slot, 1).IsAvailable;
		}
	}

	protected override (ShopQuote One, ShopQuote Stack) Quote(int index)
	{
		if (index >= Player.Inventory.Items.Count) return default;
		InventoryItemSlot entry = Player.Inventory.Items[index];
		return (Shop.QuoteSale(Player, entry, 1), entry.Quantity > 1 ? Shop.QuoteSale(Player, entry, entry.Quantity) : default);
	}

	protected override string Commit(int index, ShopQuote displayed) => Shop.Sell(Player, Player.Inventory.Items[index], displayed);
}