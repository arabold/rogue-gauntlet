using Godot;
using System;
using System.Collections.Generic;

/// <summary>Paused trade screen. Selection is a preview, and a separate action confirms the displayed quote.</summary>
public partial class ShopMenu : Control
{
	[ExportGroup("Header")]
	[Export] public Label MerchantName { get; set; }
	[Export] public Label Gold { get; set; }
	[Export] public Label PackCount { get; set; }
	[Export] public Button BuyTab { get; set; }
	[Export] public Button SellTab { get; set; }

	[ExportGroup("Items")]
	[Export] public GridContainer Grid { get; set; }
	[Export] public PackedScene SlotScene { get; set; }
	[Export] public Preview ItemPreview { get; set; }
	[Export] public ItemDetailsView Details { get; set; }
	[Export] public Label ItemName { get; set; }
	[Export] public Label Status { get; set; }

	[ExportGroup("Actions")]
	[Export] public Button TradeOne { get; set; }
	[Export] public Button SellStack { get; set; }
	[Export] public Button Back { get; set; }
	[Export] public StringName CancelAction { get; set; } = "ui_cancel";
	[Export] public StringName AlternateCancelAction { get; set; } = "pause";
	[Export] public StringName PreviousTabAction { get; set; } = "menu_previous_tab";
	[Export] public StringName NextTabAction { get; set; } = "menu_next_tab";

	private readonly List<ItemSlotPanel> _slots = [];
	private ShopComponent _shop;
	private Player _player;
	private bool _selling;
	private int _selected;
	private bool _refreshing;
	private ShopQuote _one;
	private ShopQuote _stack;

	public bool IsOpen => _shop != null;

	public override void _Ready()
	{
		BuyTab.Pressed += () => ShowTab(false);
		SellTab.Pressed += () => ShowTab(true);
		TradeOne.Pressed += () => Confirm(_one);
		SellStack.Pressed += () => Confirm(_stack);
		Back.Pressed += Close;
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.TradeRequested += Open,
			bus => bus.TradeRequested -= Open);
		this.SubscribeUntilExit(SignalBus.Instance,
			bus => bus.ItemIdentified += OnItemIdentified,
			bus => bus.ItemIdentified -= OnItemIdentified);
	}

	public override void _ExitTree()
	{
		if (IsOpen) GetTree().Paused = false;
	}

	public override void _UnhandledInput(InputEvent input)
	{
		if (!IsOpen || input.IsEcho()) return;
		if (input.IsActionPressed(CancelAction) || input.IsActionPressed(AlternateCancelAction)) Close();
		else if (input.IsActionPressed(PreviousTabAction) || input.IsActionPressed(NextTabAction)) ShowTab(!_selling);
		else return;
		GetViewport().SetInputAsHandled();
	}

	private void Open(ShopComponent shop, Player player)
	{
		if (GetTree().Paused || LoadingScreen.Instance?.IsCovering == true || !shop.CanTrade(player)) return;
		_shop = shop;
		_player = player;
		MerchantName.Text = shop.Profile.DisplayName;
		Visible = true;
		GetTree().Paused = true;
		SignalBus.EmitGamePaused(true);
		ShowTab(false);
	}

	private void Close()
	{
		if (!IsOpen) return;
		_shop = null;
		_player = null;
		_one = _stack = default;
		foreach (var slot in _slots) slot.SetItem(null, false);
		ItemPreview.ShowItem(null);
		Details.SetItem(null);
		GetViewport().GuiReleaseFocus();
		Visible = false;
		GetTree().Paused = false;
		SignalBus.EmitGamePaused(false);
	}

	private void ShowTab(bool selling)
	{
		_selling = selling;
		_selected = 0;
		BuyTab.SetPressedNoSignal(!selling);
		SellTab.SetPressedNoSignal(selling);
		Refresh();
		FocusSelection();
	}

	private void FocusSelection()
	{
		if (_slots.Count > 0 && _slots[_selected].Visible && _slots[_selected].Selectable)
			_slots[_selected].FocusSlot();
		else
			(_selling ? SellTab : BuyTab).GrabFocus();
	}

	private void Refresh(string notice = "")
	{
		_refreshing = true;
		var inventory = _player.Inventory;
		Gold.Text = $"{_player.Stats.Gold} gold";
		PackCount.Text = $"Pack  {inventory.Items.Count} / {inventory.Capacity}";
		int itemsCount = _selling ? inventory.Items.Count : _shop.Offers.Count;
		int displayCount = _selling ? inventory.Capacity : Math.Max(Grid.Columns, itemsCount);

		while (_slots.Count < displayCount)
		{
			int index = _slots.Count;
			var slot = SlotScene.Instantiate<ItemSlotPanel>();
			_slots.Add(slot);
			Grid.AddChild(slot);
			slot.ItemHighlighted += _ => Select(index);
			slot.ItemSelected += _ =>
			{
				Select(index);
				if (!TradeOne.Disabled) TradeOne.GrabFocus();
				else if (SellStack.Visible && !SellStack.Disabled) SellStack.GrabFocus();
			};
		}

		for (int i = 0; i < _slots.Count; i++)
		{
			var slot = _slots[i];
			var entry = EntryAt(i);
			slot.Visible = i < displayCount;
			slot.Selectable = i < itemsCount;
			var offer = OfferAt(i);
			bool outOfStock = offer != null && _shop.IsSold(offer);
			if (offer != null && !outOfStock)
				entry = slot.Slot?.Item == offer.Item ? slot.Slot : new InventoryItemSlot { Item = offer.Item };
			bool equipped = _selling && entry != null && inventory.IsEquipped(entry);
			if (slot.Slot != entry || slot.IsEquipped != equipped)
				slot.SetItem(entry, equipped);
			slot.Dimmed = outOfStock;
		}
		_selected = Math.Clamp(_selected, 0, Math.Max(0, itemsCount - 1));
		_refreshing = false;
		ShowSelection(notice);
	}

	private void Select(int index)
	{
		if (_refreshing) return;
		_selected = index;
		ShowSelection();
	}

	private ShopOffer OfferAt(int index) => !_selling && index < _shop.Offers.Count ? _shop.Offers[index] : null;
	private InventoryItemSlot EntryAt(int index) => _selling && index < _player.Inventory.Items.Count ? _player.Inventory.Items[index] : null;

	private void ShowSelection(string notice = "")
	{
		var offer = OfferAt(_selected);
		var entry = EntryAt(_selected);
		_one = _selling ? _shop.QuoteSale(_player, entry, 1) : _shop.QuotePurchase(_player, offer);
		bool showStack = entry?.Quantity > 1;
		_stack = showStack ? _shop.QuoteSale(_player, entry, entry.Quantity) : default;
		bool sold = offer != null && _shop.IsSold(offer);
		Item item = sold ? null : offer?.Item ?? entry?.Item;
		bool fresh = !_selling && item is FoodItem && _shop.Profile.CreationPolicy?.RollFoodContamination != true;
		string displayName = ItemIdentity.ResolveDisplayName(item);
		ItemName.Text = sold ? "Sold out" : fresh ? $"Fresh {displayName}" : displayName;
		ItemName.Modulate = ItemQualityPalette.Color(item);
		ItemPreview.ShowItem(item);
		Details.SetItem(item, _player);
		TradeOne.Disabled = !_one.IsAvailable;
		TradeOne.Text = ActionLabel(_selling ? "Sell one" : "Buy", _one.Gold);
		SellStack.Visible = showStack;
		SellStack.Disabled = !_stack.IsAvailable;
		SellStack.Text = ActionLabel($"Sell stack ({entry?.Quantity})", _stack.Gold);
		if (notice.Length == 0 && !sold)
		{
			if (item == null) notice = _selling ? "Your pack is empty" : "No stock";
			else if (!_one.IsAvailable) notice = _one.Reason;
		}
		Status.Text = notice;
	}

	private static string ActionLabel(string action, int price) => price > 0 ? $"{action} · {price} gold" : action;

	private void Confirm(ShopQuote displayed)
	{
		string reason = _selling
			? _shop.Sell(_player, EntryAt(_selected), displayed)
			: _shop.Buy(_player, OfferAt(_selected), displayed);
		Refresh(reason.Length == 0 ? (_selling ? "Sold" : "Purchased") : reason);
		if (reason.Length == 0) FocusSelection();
	}

	private void OnItemIdentified(string typeId)
	{
		if (IsOpen) ShowSelection();
	}
}
