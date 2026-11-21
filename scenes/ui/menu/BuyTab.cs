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
	protected override string Count => $"{Shop.Offers.Count(offer => !Shop.IsSold(offer))} / {Shop.Offers.Count}";
	protected override int Niches => Shop.Offers.Count;

	protected override void Fill(ItemSlotPanel panel, int index)
	{
		ShopOffer offer = Shop.Offers[index];
		if (panel.Slot?.Item != offer.Item)
		{
			panel.SetItem(new InventoryItemSlot { Item = offer.Item }, false);
		}
		panel.Selectable = true;
		panel.Dimmed = Shop.IsSold(offer);
	}

	protected override (ShopQuote One, ShopQuote Stack) Quote(int index) =>
		index < Shop.Offers.Count ? (Shop.QuotePurchase(Player, Shop.Offers[index]), default) : default;

	protected override string Commit(int index, ShopQuote displayed) => Shop.Buy(Player, Shop.Offers[index], displayed);

	/// <summary>The keeper sells only sound food, which the hero could not otherwise tell from tainted.</summary>
	protected override string Qualify(Item item, string name) =>
		item is FoodItem && Shop.Profile.CreationPolicy?.RollFoodContamination != true ? $"Fresh {name}" : name;
}