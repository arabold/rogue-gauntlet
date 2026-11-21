using System.Linq;

/// <summary>
/// The trade's Buy tab: the keeper's shelf, one niche per offer. A sold offer stays on the shelf, dimmed, so the hero
/// sees what the keeper had.
/// </summary>
public partial class BuyTab : TradeTab
{
	protected override string Verb => "Buy";
	protected override string Receipt => "Bought";
	protected override string Title => Shop.Profile.DisplayName;

	protected override void Fill()
	{
		SyncSlots(Grid, Shop.Offers.Count);
		for (int i = 0; i < Shop.Offers.Count; i++)
		{
			Item offer = Shop.Offers[i];
			var panel = Grid.GetChild<ItemSlotPanel>(i);
			if (panel.Slot?.Item != offer)
			{
				panel.SetItem(new InventoryItemSlot { Item = offer }, false);
			}
			panel.Selectable = true;
			panel.Dimmed = Shop.IsSold(offer);
		}
		CountLabel.Text = $"{Shop.Offers.Count(offer => !Shop.IsSold(offer))} / {Shop.Offers.Count}";
	}

	protected override (ShopQuote One, ShopQuote Stack) Quote(int index) => (Shop.QuotePurchase(Player, Shop.Offers[index]), default);

	protected override string Commit(int index, ShopQuote displayed) => Shop.Buy(Player, Shop.Offers[index], displayed);

	/// <summary>The keeper sells only sound food, which the hero could not otherwise tell from tainted.</summary>
	protected override string Qualify(Item item, string name) =>
		item is FoodItem && Shop.Profile.CreationPolicy?.RollFoodContamination != true ? $"Fresh {name}" : name;
}