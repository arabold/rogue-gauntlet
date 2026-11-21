using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>A single unit or stack quoted to the hero. An empty reason means the trade can proceed.</summary>
public readonly record struct ShopQuote(Item Item, int Quantity, int Gold, string Reason)
{
	public bool IsAvailable => Item != null && string.IsNullOrEmpty(Reason);

	/// <summary>The quote as the hero reads it: its price, and why it cannot go through when it cannot.</summary>
	public string Describe() => string.Join(" · ", new[] { Gold > 0 ? $"{Gold} gold" : "", Reason ?? "" }.Where(part => part.Length > 0));
}

/// <summary>
/// Owns one keeper's shelf and quotes and commits trades between the shelf, the hero's pack and their gold. Generation
/// stands the keeper and names the shop; the level then stocks the shelf from its seed. A sold offer is one of the
/// run's cleared entities, so revisits and saves keep the shelf depleted.
/// </summary>
[GlobalClass]
public partial class ShopComponent : Node
{
	[Export] public ShopProfile Profile { get; set; }
	[Export] public InteractiveComponent Interaction { get; set; }

	private const string PriceChanged = "Price changed. Confirm the new price.";

	private readonly List<Item> _offers = [];
	private Level _level;

	/// <summary>The floor-unique ID generation gives the shop; it names the shelf's reward streams and offers.</summary>
	public string Id { get; internal set; } = "";
	/// <summary>The shelf: one item per offer, sold or not.</summary>
	public IReadOnlyList<Item> Offers => _offers;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready() => Interaction.Interacted += OnInteracted;

	private void OnInteracted(Player player)
	{
		if (CanTrade(player)) SignalBus.EmitTradeOffered(this, player);
	}

	/// <summary>
	/// Rolls the shelf, one reward stream per stock category, so adding gear never pushes out supplies. Generation
	/// validated the profile (<see cref="ShopProfile.Validate"/>) when it stood the keeper.
	/// </summary>
	internal void Stock()
	{
		_offers.Clear();
		uint depth = _level.Depth;
		foreach (ShopStockProfile stock in Profile.Stock)
		{
			string source = $"depth:{depth}:shop:{Id}:{stock.Id}";
			using var rng = _level.CreateSourceLootRng(source);
			var drawn = stock.Pool.Draw(depth, stock.Quantity, stock.DistinctItems, rng, Profile.CanStock);
			for (int i = 0; i < drawn.Count; i++)
			{
				var item = ItemFactory.Create(drawn[i], Profile.CreationPolicy, depth, rng, $"{source}:{i}");
				(item as EquipableItem)?.RevealAll();
				_offers.Add(item);
			}
		}
	}

	public bool IsSold(Item offer) => _level.Run.IsCleared(offer.InstanceId);

	/// <summary>
	/// Whether <paramref name="player"/> may trade here. Reach is the interaction's business: the trade opens only once
	/// it commits, and the menu then pauses the game until the trade ends.
	/// </summary>
	public bool CanTrade(Player player) =>
		_level != null && IsInsideTree() && IsInstanceValid(player) && player.IsInsideTree() && !player.IsDead
		&& player.GetAncestorOrNull<Level>() == _level && Interaction.IsInteractive;

	public ShopQuote QuotePurchase(Player player, Item offer)
	{
		if (!CanTrade(player) || !_offers.Contains(offer)) return new(null, 0, 0, "Trade unavailable");
		if (IsSold(offer)) return new(null, 0, 0, "Sold out");
		int price = Profile.Pricing.RetailPrice(offer, _level.Run.Identification);
		string reason = price > player.Stats.Gold ? "Not enough gold"
			: !player.Inventory.CanAdd(offer).Success ? "Pack is full" : "";
		return new(offer, 1, price, reason);
	}

	public ShopQuote QuoteSale(Player player, InventoryItemSlot entry, int quantity)
	{
		if (!CanTrade(player) || !player.Inventory.Items.Contains(entry)) return new(null, 0, 0, "Trade unavailable");
		Item item = entry.Item;
		if (player.Inventory.IsEquipped(entry)) return new(item, quantity, 0, "Unequip this item first");
		if (!Profile.AcceptsForSale(item))
			return new(item, quantity, 0, "The merchant won't buy this");
		if (!player.Inventory.CanRemove(entry, quantity).Success) return new(item, quantity, 0, "Quantity unavailable");
		long price = (long)Profile.Pricing.SalePrice(item, _level.Run.Identification) * quantity;
		if (price == 0) return new(item, quantity, 0, "This item has no resale value");
		if (price > int.MaxValue - (long)player.Stats.Gold) return new(item, quantity, 0, "Can't carry more gold");
		return new(item, quantity, (int)price, "");
	}

	/// <summary>Buys <paramref name="offer"/> at the <paramref name="displayed"/> quote; empty on success, else why not.</summary>
	public string Buy(Player player, Item offer, ShopQuote displayed)
	{
		var quote = QuotePurchase(player, offer);
		if (!quote.IsAvailable) return quote.Reason;
		if (quote != displayed) return PriceChanged;
		return Report(player, player.InventoryController.TryAcquire(offer, 1, () =>
		{
			_level.Run.MarkCleared(offer.InstanceId);
			player.Stats.PayGold(quote.Gold);
		}));
	}

	/// <summary>Sells the <paramref name="displayed"/> quantity of <paramref name="entry"/> at that quote; empty on success, else why not.</summary>
	public string Sell(Player player, InventoryItemSlot entry, ShopQuote displayed)
	{
		var quote = QuoteSale(player, entry, displayed.Quantity);
		if (!quote.IsAvailable) return quote.Reason;
		if (quote != displayed) return PriceChanged;
		return Report(player, player.InventoryController.TryHandOver(entry, quote.Quantity, () => player.Stats.AddGold(quote.Gold)));
	}

	private static string Report(Player player, ItemOperationResult result) =>
		result.Success ? "" : player.InventoryController.LastOperationMessage;
}