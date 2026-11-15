using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The run menu's Pack tab: everything the hero carries as a grid of niches, the four quick slots beneath it, and the
/// selected item large on the right. Its prompts use the item, wear it, give it a quick slot or drop it. A scroll that
/// needs a target (identify, enchant) turns the grid into a choice: what it cannot act on dims, the banner says what
/// it asks for, and choosing an item is the answer.
/// </summary>
public partial class PackTab : RunMenuTab
{
	[Export] public GridContainer Grid { get; set; }
	[Export] public Label CountLabel { get; set; }
	/// <summary>Holds the quick slots in order; clicking one gives it the selected item.</summary>
	[Export] public Container QuickRow { get; set; }

	[ExportGroup("Selected item")]
	[Export] public Preview ItemPreview { get; set; }
	[Export] public Label NameLabel { get; set; }
	[Export] public Label QuantityLabel { get; set; }
	[Export] public ItemDetailsView Details { get; set; }

	private static readonly StringName[] QuickActions = ["quick_item_1", "quick_item_2", "quick_item_3", "quick_item_4"];

	/// <summary>The scroll waiting for the player to choose what it acts on; null outside such a choice.</summary>
	private InventoryItemSlot _reading;

	public override string CancelLabel => _reading != null ? "Cancel" : "Close";
	/// <summary>While a scroll asks for its target, choosing an item is the answer.</summary>
	public override bool AsksWhatToDo => base.AsksWhatToDo && _reading == null;

	public override void _Ready()
	{
		ActionContainer[] quickSlots = [.. QuickRow.GetChildren().OfType<ActionContainer>()];
		for (int i = 0; i < Mathf.Min(quickSlots.Length, QuickActions.Length); i++)
		{
			StringName action = QuickActions[i];
			quickSlots[i].GuiInput += @event =>
			{
				if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				{
					Invoke(action);
				}
			};
		}
	}

	public override void Open(Player player)
	{
		base.Open(player);
		Watch(player.Inventory, Refresh);
		Refresh();
	}

	public override void Close()
	{
		Cancel();
		// The large preview renders live while it shows something; nobody is looking now.
		ItemPreview.Animated = false;
		ItemPreview.ShowItem(null);
		base.Close();
	}

	public override void FocusDefault()
	{
		if (Player?.Inventory.Items.Count > 0)
		{
			Grid.GetChild<ItemSlotPanel>(0).FocusSlot();
		}
	}

	public override IEnumerable<MenuPrompt> Prompts
	{
		get
		{
			if (Player == null || Selected?.Slot is not { Item: { } item } entry)
			{
				yield break;
			}

			if (_reading != null)
			{
				if (IsTarget(entry))
				{
					yield return new MenuPrompt("Choose", Accept);
				}
				yield break;
			}

			switch (item)
			{
				case ConsumableItem consumable:
					yield return new MenuPrompt(item is Scroll ? "Read" : item is FoodItem ? "Eat" : "Drink", Accept);
					if (consumable.AllowQuickUse)
					{
						yield return new MenuPrompt("Quick Slot", QuickActions);
					}
					break;
				case EquipableItem equipable:
					foreach (MenuPrompt prompt in WearPrompts(entry, equipable))
					{
						yield return prompt;
					}
					break;
			}
			if (CanDrop(entry))
			{
				yield return new MenuPrompt("Drop", Drop);
			}
		}
	}

	public override void Invoke(StringName action)
	{
		if (Player == null || Selected?.Slot is not { Item: { } item } entry)
		{
			return;
		}

		if (_reading != null)
		{
			if (action == Accept && IsTarget(entry))
			{
				InventoryItemSlot scroll = _reading;
				EndTargeting();
				Use(scroll, entry);
			}
			return;
		}

		int quickSlot = Array.IndexOf(QuickActions, action);
		if (quickSlot >= 0)
		{
			Player.QuickItemController.ToggleAssignment(quickSlot, item as ConsumableItem);
		}
		else if (action != Accept)
		{
			InvokeCommon(action, entry);
		}
		else if (item is ConsumableItem)
		{
			Use(entry);
		}
		else
		{
			ToggleEquip(entry);
		}
	}

	public override bool Cancel()
	{
		if (_reading == null)
		{
			return false;
		}

		EndTargeting();
		return true;
	}

	private void Use(InventoryItemSlot entry, InventoryItemSlot target = null)
	{
		if (target == null && entry.Item is Scroll { RequiresTarget: true })
		{
			ChooseTarget(entry);
			return;
		}

		PlayerInventoryController controller = Player.InventoryController;
		ItemOperationResult result = controller.TryUse(entry, target);
		Report(result);
		// Drinking takes a moment the paused game cannot play: back to the world, where the hero does it.
		if (result.Success && controller.IsItemUseChannelActive)
		{
			EmitSignalCloseRequested();
		}
	}

	/// <summary>Whether the scroll being read can act on <paramref name="slot"/>.</summary>
	private bool IsTarget(InventoryItemSlot slot) =>
		slot != _reading && ((Scroll)_reading.Item).Effect.IsValidTarget(Player, slot);

	/// <summary>Has the player pick what a targeted scroll acts on.</summary>
	private void ChooseTarget(InventoryItemSlot scrollSlot)
	{
		_reading = scrollSlot;
		if (!Player.Inventory.Items.Any(IsTarget))
		{
			_reading = null;
			EmitSignalNotified("Nothing suitable. The scroll stays rolled up.", false);
			return;
		}

		// Reading far enough to pick a target reveals the scroll itself, even if the player then backs out.
		var scroll = (Scroll)scrollSlot.Item;
		Player.InventoryController.BeginTargeting(scroll);
		EmitSignalNotified(scroll.Effect.TargetPrompt, true);
		ApplyTargeting();
		Grid.GetChildren().OfType<ItemSlotPanel>().FirstOrDefault(panel => panel.Slot != null && IsTarget(panel.Slot))?.FocusSlot();
		EmitSignalPromptsChanged();
	}

	/// <summary>Leaves the choice of a target, made or not.</summary>
	private void EndTargeting()
	{
		_reading = null;
		EmitSignalNotified("", false);
		ApplyTargeting();
		EmitSignalPromptsChanged();
	}

	/// <summary>While a scroll asks for its target, dims and disables what it cannot act on.</summary>
	private void ApplyTargeting()
	{
		foreach (ItemSlotPanel panel in Grid.GetChildren().OfType<ItemSlotPanel>())
		{
			panel.SetTargetingState(_reading == null || panel.Slot == null ? SlotTargetingState.None
				: IsTarget(panel.Slot) ? SlotTargetingState.Eligible : SlotTargetingState.Ineligible);
		}
	}

	protected override void Select(ItemSlotPanel panel)
	{
		base.Select(panel);
		Describe();
		EmitSignalPromptsChanged();
	}

	private void Refresh()
	{
		if (Player == null)
		{
			return;
		}

		// One niche per place in the pack; an emptied one cannot be selected.
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
		ApplyTargeting();

		// The selected niche emptied (its item was the last in the pack's order): fall back to the new last item.
		if (Selected is { Slot: null })
		{
			Selected = null;
			if (inventory.Items.Count > 0)
			{
				Grid.GetChild<ItemSlotPanel>(inventory.Items.Count - 1).FocusSlot();
			}
		}
		Describe();
		EmitSignalPromptsChanged();
	}

	private void Describe()
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