using Godot;

/// <summary>
/// A run-menu tab of items: a grid of niches on the left, the selected item large on the right with its name, stack
/// count and details. The Pack and a trade's Buy and Sell tabs share it.
/// </summary>
public abstract partial class ItemGridTab : RunMenuTab
{
	[Export] public GridContainer Grid { get; set; }
	[Export] public Label CountLabel { get; set; }

	[ExportGroup("Selected item")]
	[Export] public Preview ItemPreview { get; set; }
	[Export] public Label NameLabel { get; set; }
	[Export] public Label QuantityLabel { get; set; }
	[Export] public ItemDetailsView Details { get; set; }

	public override void Close()
	{
		// The large preview renders live while it shows something; nobody is looking now.
		ItemPreview.Animated = false;
		ItemPreview.ShowItem(null);
		base.Close();
	}

	public override void FocusDefault()
	{
		if (Grid.GetChildCount() > 0 && Grid.GetChild<ItemSlotPanel>(0).Slot != null)
		{
			Grid.GetChild<ItemSlotPanel>(0).FocusSlot();
		}
	}

	/// <summary>Shows the pack in the grid: one niche per place in it, worn items lit; an empty niche cannot be selected.</summary>
	protected void ShowPack()
	{
		Inventory inventory = Player.Inventory;
		int niches = Mathf.Max(inventory.Capacity, inventory.Items.Count);
		SyncSlots(Grid, niches);
		for (int i = 0; i < niches; i++)
		{
			InventoryItemSlot entry = i < inventory.Items.Count ? inventory.Items[i] : null;
			var panel = Grid.GetChild<ItemSlotPanel>(i);
			panel.SetItem(entry, entry != null && inventory.IsEquipped(entry));
			panel.Selectable = entry != null;
		}
		CountLabel.Text = $"{inventory.Items.Count} / {inventory.Capacity}";
	}

	/// <summary>The selected niche emptied (its item was the last in the grid's order): falls back to the last filled one.</summary>
	protected void KeepSelection()
	{
		if (Selected is not { Slot: null })
		{
			return;
		}

		Selected = null;
		for (int i = Grid.GetChildCount() - 1; i >= 0; i--)
		{
			var panel = Grid.GetChild<ItemSlotPanel>(i);
			if (panel.Slot != null)
			{
				panel.FocusSlot();
				return;
			}
		}
	}

	/// <summary>Shows the selected item large: its model, its name in its quality's color, its stack count and details.</summary>
	protected virtual void Describe()
	{
		InventoryItemSlot entry = Selected?.Slot;
		Item item = entry?.Item;
		ItemPreview.ShowItem(item);
		ItemPreview.Animated = item != null;
		ShowName(NameLabel, item);
		QuantityLabel.Text = item is { CanStack: true } ? entry.Quantity.ToString() : "";
		Details.SetItem(item, Player);
	}
}