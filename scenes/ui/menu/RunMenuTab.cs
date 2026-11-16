using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// One tab of the <see cref="RunMenu"/>. The menu opens the tab for the hero and shows its <see cref="Prompts"/> twice:
/// as the key row, whose keys act at once, and as a popup on a chosen slot, which the accept key and a click both
/// open, so it is always plain what choosing an item will do. Either way the action reaches <see cref="Invoke"/>.
/// What every tab of items shares lives here: following the pack, the grid of slots, wearing, changing hands, dropping.
/// </summary>
public abstract partial class RunMenuTab : Control
{
	/// <summary>The selection changed what the tab offers; the menu redraws the key row.</summary>
	[Signal] public delegate void PromptsChangedEventHandler();
	/// <summary>A line for the menu's banner. Held lines stay until an empty one clears them; others fade on their own.</summary>
	[Signal] public delegate void NotifiedEventHandler(string message, bool held);
	/// <summary>A slot was chosen: the menu offers the tab's <see cref="Prompts"/> as a popup on <paramref name="anchor"/>.</summary>
	[Signal] public delegate void ActionsRequestedEventHandler(Control anchor);
	/// <summary>The tab is done with the menu, e.g. Resume, or a potion the hero now drinks in the world.</summary>
	[Signal] public delegate void CloseRequestedEventHandler();

	/// <summary>The slot a tab of items fills its grids with.</summary>
	[Export] public PackedScene SlotScene { get; set; }

	protected static readonly StringName Accept = "ui_accept", Alternate = "menu_alternate", Drop = "menu_drop";

	protected Player Player { get; private set; }
	/// <summary>The slot the tab's prompts are about: the one last pointed at or moved onto.</summary>
	protected ItemSlotPanel Selected { get; set; }
	/// <summary>
	/// Whether choosing the selection asks what to do with it. False where there is one thing to do, such as an empty
	/// slot or a scroll's target, which is then done at once.
	/// </summary>
	public virtual bool AsksWhatToDo => Selected?.Slot != null;

	/// <summary>What the cancel key does here: it closes the menu unless the tab is in the middle of something.</summary>
	public virtual string CancelLabel => "Close";
	public virtual IEnumerable<MenuPrompt> Prompts => [];

	private readonly List<Action> _unwatch = [];

	public virtual void Open(Player player)
	{
		Player = player;
		Selected = null;
	}

	public virtual void Close()
	{
		foreach (Action unwatch in _unwatch)
		{
			unwatch();
		}
		_unwatch.Clear();
		Player = null;
	}

	/// <summary>Puts the focus where the tab starts.</summary>
	public abstract void FocusDefault();
	public virtual void Invoke(StringName action) { }
	/// <summary>The selection was chosen, by the accept key or a click: asks what to do with it, or does the one thing there is.</summary>
	public void Activate()
	{
		if (AsksWhatToDo)
		{
			EmitSignalActionsRequested(Selected);
		}
		else
		{
			Invoke(Accept);
		}
	}
	/// <summary>Backs out of whatever the tab is in the middle of. False when there was nothing, so the menu closes.</summary>
	public virtual bool Cancel() => false;

	/// <summary>
	/// Runs <paramref name="refresh"/> whenever <paramref name="observed"/> changes, until the tab closes. The pack
	/// reports a swap once it is whole, so the tab never draws half of one.
	/// </summary>
	protected void Watch(Resource observed, Action refresh) =>
		_unwatch.Add(this.SubscribeUntilExit(observed, resource => resource.Changed += refresh, resource => resource.Changed -= refresh));

	/// <summary>
	/// Wires a slot into the tab: pointing at it or moving the focus onto it selects it, and the accept key or a click
	/// chooses it.
	/// </summary>
	protected void Connect(ItemSlotPanel panel)
	{
		panel.ItemHighlighted += Select;
		// A click reaches the slot before the pointer's focus is certain to have settled on it.
		panel.ItemSelected += chosen =>
		{
			Select(chosen);
			Activate();
		};
	}

	protected virtual void Select(ItemSlotPanel panel) => Selected = panel;

	/// <summary>
	/// Gives <paramref name="grid"/> exactly <paramref name="count"/> slots. The slots stay and take their items in
	/// order, so the selection keeps its place when the list changes. True if the selected slot was among those
	/// removed; the selection is then cleared.
	/// </summary>
	protected bool SyncSlots(GridContainer grid, int count)
	{
		bool lostSelection = false;
		for (int i = grid.GetChildCount() - 1; i >= count; i--)
		{
			Node extra = grid.GetChild(i);
			lostSelection |= extra == Selected;
			grid.RemoveChild(extra);
			extra.QueueFree();
		}
		while (grid.GetChildCount() < count)
		{
			var panel = SlotScene.Instantiate<ItemSlotPanel>();
			grid.AddChild(panel);
			Connect(panel);
		}

		if (lostSelection)
		{
			Selected = null;
		}
		return lostSelection;
	}

	/// <summary>Writes <paramref name="item"/>'s name as the player knows it, in its quality's color; nothing for no item.</summary>
	protected static void ShowName(Label label, Item item)
	{
		label.Text = item != null ? ItemIdentity.ResolveDisplayName(item) : "";
		label.SelfModulate = ItemQualityPalette.Color(item);
		label.GetAncestorOrNull<AutoScrollViewport>()?.ResetScroll();
	}

	/// <summary>Whether either hand can hold <paramref name="item"/>, so the player has to say which.</summary>
	protected static bool GoesInEitherHand(EquipableItem item) =>
		Inventory.Fits(item, EquipmentSlot.WeaponHand) && Inventory.Fits(item, EquipmentSlot.ShieldHand);

	/// <summary>
	/// What wearing offers for <paramref name="entry"/>: Equip, or Unequip once it is worn. What either hand can hold
	/// names the hands instead and always offers the other one, so a worn weapon can change hands without coming off.
	/// <paramref name="intoChosenSlot"/> says the slot is already decided, as when filling a chosen worn slot.
	/// </summary>
	protected IEnumerable<MenuPrompt> WearPrompts(InventoryItemSlot entry, EquipableItem item, bool intoChosenSlot = false)
	{
		bool eitherHand = GoesInEitherHand(item);
		yield return new MenuPrompt(Player.Inventory.IsEquipped(entry) ? "Unequip" : intoChosenSlot || !eitherHand ? "Equip" : "Main Hand", Accept);
		if (eitherHand)
		{
			yield return new MenuPrompt(OtherHand(entry) == EquipmentSlot.WeaponHand ? "Main Hand" : "Off Hand", Alternate);
		}
	}

	/// <summary>Worn things come off first, and quest items stay.</summary>
	protected bool CanDrop(InventoryItemSlot entry) => !Player.Inventory.IsEquipped(entry) && !entry.Item.IsQuestItem;

	/// <summary>Does what every tab of items offers alike: the other hand and drop. False for any other action.</summary>
	protected bool InvokeCommon(StringName action, InventoryItemSlot entry)
	{
		if (action == Alternate && entry.Item is EquipableItem item && GoesInEitherHand(item))
		{
			Report(Player.InventoryController.TryEquip(entry, OtherHand(entry)));
		}
		else if (action == Drop && CanDrop(entry))
		{
			Report(Player.InventoryController.TryDrop(entry, entry.Quantity));
		}
		else
		{
			return false;
		}
		return true;
	}

	/// <summary>The hand the alternate action puts a hand item in: the off hand, or the main hand for what the off hand holds.</summary>
	private EquipmentSlot OtherHand(InventoryItemSlot entry) =>
		Player.Inventory.EquippedItems[EquipmentSlot.ShieldHand] == entry ? EquipmentSlot.WeaponHand : EquipmentSlot.ShieldHand;

	/// <summary>Tells the player why an item operation did not happen.</summary>
	protected void Report(ItemOperationResult? result)
	{
		if (result?.Success != true)
		{
			EmitSignalNotified(Player?.InventoryController.LastOperationMessage ?? "", false);
		}
	}

	/// <summary>
	/// Wears <paramref name="entry"/>, or takes it off when it is worn. A hand item goes to <paramref name="preferred"/>
	/// when it fits there, else to the main hand when it can, else wherever it belongs.
	/// </summary>
	protected void ToggleEquip(InventoryItemSlot entry, EquipmentSlot? preferred = null)
	{
		if (entry?.Item is not EquipableItem item)
		{
			return;
		}

		if (Player.Inventory.IsEquipped(entry))
		{
			Report(Player.InventoryController.TryUnequip(entry));
			return;
		}

		EquipmentSlot? target = preferred is { } slot && Inventory.Fits(item, slot) ? slot
			: Inventory.Fits(item, EquipmentSlot.WeaponHand) ? EquipmentSlot.WeaponHand : null;
		Report(Player.InventoryController.TryEquip(entry, target));
	}
}