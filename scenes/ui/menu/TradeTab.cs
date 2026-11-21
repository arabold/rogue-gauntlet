using Godot;
using System.Collections.Generic;

/// <summary>
/// A run-menu tab of a keeper's trade: the items on one side of the counter, the selected one with its price. Selecting
/// an item quotes it; its prompt confirms that quote, and the shop refuses the trade if the price changed in between,
/// so the hero never pays a price they did not see. The menu opens these tabs only for a keeper
/// (<see cref="RunMenu.OpenTrade"/>), which sets <see cref="Shop"/> first.
/// </summary>
public abstract partial class TradeTab : ItemGridTab
{
	[Export] public Label TitleLabel { get; set; }
	/// <summary>The selection's quote: its price, and why it cannot be traded when it cannot.</summary>
	[Export] public Label PriceLabel { get; set; }

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
	/// <summary>Fills the grid and its count.</summary>
	protected abstract void Fill();
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
		One = Stack = default;
		base.Close();
	}

	/// <summary>Confirms the quote the pressed prompt showed, or says why the selection cannot be traded.</summary>
	public override void Invoke(StringName action)
	{
		ShopQuote displayed = action == Accept ? One : action == Alternate ? Stack : default;
		if (Player == null || Selected == null || displayed == default)
		{
			return;
		}

		string reason = displayed.IsAvailable ? Commit(Selected.GetIndex(), displayed) : displayed.Reason;
		EmitSignalNotified(reason.Length > 0 ? reason : Receipt, false);
		// A trade that went through redraws through the pack and gold it changed; a refused one needs a fresh quote.
		if (reason.Length > 0)
		{
			Refresh();
		}
	}

	protected override void Select(ItemSlotPanel panel)
	{
		base.Select(panel);
		Describe();
	}

	protected override void Describe()
	{
		(One, Stack) = Selected != null ? Quote(Selected.GetIndex()) : default;
		base.Describe();
		PriceLabel.Text = One.Describe();
		EmitSignalPromptsChanged();
	}

	private void Refresh()
	{
		if (Player == null || Shop == null)
		{
			return;
		}

		Fill();
		TitleLabel.Text = Title;
		KeepSelection();
		Describe();
	}
}