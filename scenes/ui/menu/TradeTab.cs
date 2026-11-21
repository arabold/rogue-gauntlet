using Godot;
using System.Collections.Generic;

/// <summary>
/// A run-menu tab of a keeper's trade: the items on one side of the counter as a grid, the selected one large on the
/// right with its price. Selecting an item quotes it; its prompt confirms that quote, and the shop refuses the trade if
/// the price changed in between, so the hero never pays a price they did not see. The menu opens these tabs only for a
/// keeper (<see cref="RunMenu.OpenTrade"/>), which sets <see cref="Shop"/> first.
/// </summary>
public abstract partial class TradeTab : RunMenuTab
{
	[Export] public GridContainer Grid { get; set; }
	[Export] public Label TitleLabel { get; set; }
	[Export] public Label CountLabel { get; set; }

	[ExportGroup("Selected item")]
	[Export] public Preview ItemPreview { get; set; }
	[Export] public Label NameLabel { get; set; }
	[Export] public Label QuantityLabel { get; set; }
	/// <summary>The selection's quote: its price, and why it cannot be traded when it cannot.</summary>
	[Export] public Label PriceLabel { get; set; }
	[Export] public ItemDetailsView Details { get; set; }

	/// <summary>The keeper whose shelf the tab trades with, for as long as the menu is open on the trade.</summary>
	public ShopComponent Shop { get; set; }

	/// <summary>The quotes the prompts show: one unit, and the whole stack where there is one.</summary>
	protected ShopQuote One { get; private set; }
	protected ShopQuote Stack { get; private set; }

	public override bool AsksWhatToDo => One.IsAvailable || Stack.IsAvailable;

	/// <summary>"Buy" or "Sell": the verb of the tab's prompts.</summary>
	protected abstract string Verb { get; }
	/// <summary>The banner's line once a trade went through.</summary>
	protected abstract string Receipt { get; }
	protected abstract string Title { get; }
	protected abstract string Count { get; }
	/// <summary>How many niches the grid shows.</summary>
	protected abstract int Niches { get; }
	/// <summary>Shows niche <paramref name="index"/>'s item in <paramref name="panel"/>.</summary>
	protected abstract void Fill(ItemSlotPanel panel, int index);
	/// <summary>The quotes for the item in niche <paramref name="index"/>; default for an empty niche.</summary>
	protected abstract (ShopQuote One, ShopQuote Stack) Quote(int index);
	/// <summary>Trades the item in niche <paramref name="index"/> at the <paramref name="displayed"/> quote; empty on success, else why not.</summary>
	protected abstract string Commit(int index, ShopQuote displayed);

	public override IEnumerable<MenuPrompt> Prompts
	{
		get
		{
			if (One.IsAvailable) yield return new MenuPrompt($"{Verb} · {One.Gold} gold", Accept);
			if (Stack.IsAvailable) yield return new MenuPrompt($"{Verb} {Stack.Quantity} · {Stack.Gold} gold", Alternate);
		}
	}

	public override void Open(Player player)
	{
		base.Open(player);
		// Gold decides what the hero can afford, and every trade changes the pack.
		Watch(player.Inventory, Refresh);
		Watch(player.Stats, Refresh);
		Refresh();
	}

	public override void Close()
	{
		// The large preview renders live while it shows something; nobody is looking now.
		ItemPreview.Animated = false;
		ItemPreview.ShowItem(null);
		One = Stack = default;
		base.Close();
	}

	public override void FocusDefault()
	{
		if (Grid.GetChildCount() > 0 && Grid.GetChild<ItemSlotPanel>(0).Selectable)
		{
			Grid.GetChild<ItemSlotPanel>(0).FocusSlot();
		}
	}

	/// <summary>Confirms the quote the pressed prompt showed, or says why the selection cannot be traded.</summary>
	public override void Invoke(StringName action)
	{
		ShopQuote displayed = action == Accept ? One : action == Alternate ? Stack : default;
		if (Player == null || Selected?.Slot == null || string.IsNullOrEmpty(displayed.Reason) && displayed.Item == null)
		{
			return;
		}

		string reason = displayed.IsAvailable ? Commit(Selected.GetIndex(), displayed) : displayed.Reason;
		EmitSignalNotified(reason.Length > 0 ? reason : Receipt, false);
		Refresh();
	}

	protected override void Select(ItemSlotPanel panel)
	{
		base.Select(panel);
		Describe();
	}

	/// <summary>Adds what the shelf promises to the item's name; the shelf's own tab tells fresh food apart.</summary>
	protected virtual string Qualify(Item item, string name) => name;

	private void Refresh()
	{
		if (Player == null || Shop == null)
		{
			return;
		}

		int niches = Niches;
		SyncSlots(Grid, niches);
		for (int i = 0; i < niches; i++)
		{
			Fill(Grid.GetChild<ItemSlotPanel>(i), i);
		}
		TitleLabel.Text = Title;
		CountLabel.Text = Count;

		// The selected niche emptied (its item was the last in the pack's order): fall back to the new last item.
		if (Selected is { Slot: null })
		{
			Selected = null;
			for (int i = niches - 1; i >= 0; i--)
			{
				if (Grid.GetChild<ItemSlotPanel>(i).Slot == null) continue;
				Grid.GetChild<ItemSlotPanel>(i).FocusSlot();
				break;
			}
		}
		Describe();
	}

	private void Describe()
	{
		(One, Stack) = Selected != null && Shop != null ? Quote(Selected.GetIndex()) : default;
		InventoryItemSlot entry = Selected?.Slot;
		Item item = entry?.Item;
		ItemPreview.ShowItem(item);
		ItemPreview.Animated = item != null;
		ShowName(NameLabel, item);
		NameLabel.Text = item != null ? Qualify(item, NameLabel.Text) : "";
		QuantityLabel.Text = entry?.Quantity > 1 ? entry.Quantity.ToString() : "";
		PriceLabel.Text = One.Item == null || One.Gold == 0 ? One.Reason ?? ""
			: One.IsAvailable ? $"{One.Gold} gold" : $"{One.Gold} gold · {One.Reason}";
		Details.SetItem(item, Player);
		EmitSignalPromptsChanged();
	}
}