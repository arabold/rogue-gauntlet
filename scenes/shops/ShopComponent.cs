using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record ShopOffer(Item Item)
{
	public string Id => Item.InstanceId;
}

/// <summary>A single unit or stack quoted to the hero. An empty reason means the trade can proceed.</summary>
public readonly record struct ShopQuote(Item Item, int Quantity, int Gold, string Reason)
{
	public bool IsAvailable => Item != null && string.IsNullOrEmpty(Reason);
}

/// <summary>
/// Owns one keeper's shelf and commits transfers between the shelf, the hero's pack and their gold. Generation stands
/// the keeper and names the shop; the level then stocks the shelf from its seed, and item persistence replays what the
/// hero bought.
/// </summary>
[GlobalClass]
public partial class ShopComponent : Node
{
	[Export] public ShopProfile Profile { get; set; }
	[Export] public InteractiveComponent Interaction { get; set; }

	private readonly List<ShopOffer> _offers = [];
	private readonly HashSet<string> _purchased = new(StringComparer.Ordinal);
	private Level _level;

	/// <summary>The floor-unique save key generation gives the shop; it also seeds the shelf.</summary>
	public string Id { get; internal set; } = "";
	public IReadOnlyList<ShopOffer> Offers => _offers;
	public bool IsCommitting { get; private set; }

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready() => Interaction.Interacted += OnInteracted;

	private void OnInteracted(Player player)
	{
		if (CanTrade(player)) SignalBus.EmitTradeOffered(this, player);
	}

	/// <summary>Rolls the shelf from the level seed, one stream per stock category, so adding gear never pushes out supplies.</summary>
	internal void Stock()
	{
		if (_level == null || Interaction?.Definition == null || Profile?.Pricing == null)
			throw new InvalidOperationException($"Shop {Id} needs a level, an interaction definition and a pricing profile.");
		_offers.Clear();
		_purchased.Clear();
		ulong seed = GenerationRandom.DeriveSeed(_level.Seed, "shop.stock", Id);
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (ShopStockProfile stock in Profile.Stock)
		{
			if (stock?.Pool == null || string.IsNullOrWhiteSpace(stock.Id) || stock.Id.Contains(':')
				|| !ids.Add(stock.Id) || stock.Quantity < 1)
				throw new InvalidOperationException($"Shop {Id} needs unique stock IDs, pools and positive quantities.");
			RollStock(_level.Depth, seed, stock);
		}
	}

	/// <summary>Marks the saved offers sold; every one must be on the shelf, once.</summary>
	internal void RestorePurchases(IReadOnlyList<string> purchased)
	{
		foreach (string id in purchased)
		{
			if (!_offers.Any(offer => offer.Id == id) || !_purchased.Add(id))
				throw new InvalidOperationException($"Unknown or duplicate offer {id} in shop {Id}.");
		}
	}

	private void RollStock(uint depth, ulong seed, ShopStockProfile stock)
	{
		var candidates = stock.Pool.Items.Where(entry => LootTableItem.CanRollAt(entry, depth, stock.Pool)
			&& !entry.Item.IsQuestItem && (Profile.SellsKnownCursedGear
				|| entry.Item is not EquipableItem { AlwaysCursed: true } and not EquipableItem { IsCursed: true }))
			.ToList();
		if (candidates.Count == 0) return;

		using var rng = GenerationRandom.Create(seed, "category", stock.Id);
		var selected = new List<Item>();
		for (int i = 0; i < stock.Quantity && candidates.Count > 0; i++)
		{
			var entry = candidates[(int)rng.RandWeighted(candidates.Select(candidate => candidate.Weight).ToArray())];
			selected.Add(entry.Item);
			if (stock.DistinctItems) candidates.RemoveAll(candidate => candidate.Item == entry.Item);
		}
		// A more heavily weighted potion type has the same chance to appear in either bottle slot.
		GenerationRandom.Shuffle(selected, rng);
		for (int i = 0; i < selected.Count; i++)
		{
			string id = $"shop:{depth}:{Id}:{stock.Id}:{i}";
			var item = ItemFactory.Create(selected[i], Profile.CreationPolicy, depth, rng, id);
			if (item is EquipableItem gear)
			{
				if (gear.IsCursed && !Profile.SellsKnownCursedGear)
					throw new InvalidOperationException($"Shop {Id} rolled cursed stock. Disable curses in its creation policy.");
				gear.RevealAttributes();
				gear.RevealCurseStatus();
			}
			_offers.Add(new ShopOffer(item));
		}
	}

	public bool IsSold(ShopOffer offer) => _purchased.Contains(offer.Id);
	internal List<string> CapturePurchases() => _purchased.Order(StringComparer.Ordinal).ToList();

	public bool CanTrade(Player player) =>
		_level != null && IsInsideTree() && IsInstanceValid(player) && player.IsInsideTree() && !player.IsDead
		&& player.GetAncestorOrNull<Level>() == _level && Interaction.IsInteractive
		&& player.GlobalPosition.HorizontalDistanceTo(Interaction.GlobalPosition) <= Interaction.Definition.InteractionRange
		&& Math.Abs(player.GlobalPosition.Y - Interaction.GlobalPosition.Y) <= 2.5f;

	public ShopQuote QuotePurchase(Player player, ShopOffer offer)
	{
		if (!CanTrade(player) || !_offers.Contains(offer)) return new(null, 0, 0, "Trade unavailable");
		if (IsSold(offer)) return new(null, 0, 0, "Sold out");
		int price = Profile.Pricing.RetailPrice(offer.Item, _level.Run.Identification);
		string reason = price > player.Stats.Gold ? "Not enough gold"
			: !player.Inventory.CanAdd(offer.Item).Success ? "Pack is full" : "";
		return new(offer.Item, 1, price, reason);
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

	public string Buy(Player player, ShopOffer offer, ShopQuote displayed)
	{
		if (IsCommitting) return "A trade is in progress";
		var quote = QuotePurchase(player, offer);
		if (!quote.IsAvailable) return quote.Reason;
		if (quote != displayed) return "Price changed. Confirm the new price.";

		IsCommitting = true;
		try
		{
			using var update = player.Inventory.DeferChanges();
			if (!player.Inventory.TryAdd(quote.Item).Success) return "Pack is full";
			_purchased.Add(offer.Id);
			player.Stats.PayGold(quote.Gold);
			return "";
		}
		finally { IsCommitting = false; }
	}

	public string Sell(Player player, InventoryItemSlot entry, ShopQuote displayed)
	{
		if (IsCommitting) return "A trade is in progress";
		var quote = QuoteSale(player, entry, displayed.Quantity);
		if (!quote.IsAvailable) return quote.Reason;
		if (quote != displayed) return "Price changed. Confirm the new price.";

		IsCommitting = true;
		try
		{
			using var update = player.Inventory.DeferChanges();
			player.Inventory.RemoveItem(entry, quote.Quantity);
			player.Stats.AddGold(quote.Gold);
			return "";
		}
		finally { IsCommitting = false; }
	}
}
