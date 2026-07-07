using Godot;
using System;

/// <summary>Whether a slot is a valid pick while the inventory is in targeting mode.</summary>
public enum SlotTargetingState
{
	None,
	Eligible,
	Ineligible,
}

[Tool]
public partial class ItemSlotPanel : PanelContainer
{
	[Signal] public delegate void ItemSelectedEventHandler(ItemSlotPanel itemSlotPanel);

	[Export] public Color CommonColor = new(0.8f, 0.8f, 0.0f, 0.1f);
	[Export] public Color UncommonColor = new(0.0f, 0.8f, 0.0f, 0.1f);
	[Export] public Color RareColor = new(0.0f, 0.0f, 0.8f, 0.1f);
	[Export] public Color LegendaryColor = new(0.8f, 0.0f, 0.8f, 0.1f);
	[Export] public Color UniqueColor = new(0.8f, 0.8f, 0.0f, 0.1f);
	[Export] public Color DefaultColor = new(0.8f, 0.8f, 0.8f, 0.1f);

	private Action _unsubscribeSlot = () => { };

	public bool IsEquipped
	{
		get;
		private set
		{
			field = value;
			if (IsNodeReady()) { Update(); }
		}
	}

	public InventoryItemSlot Slot
	{
		get;
		private set
		{
			field = value;
			if (IsNodeReady()) { Update(); }
		}
	}

	public override void _Ready()
	{
		var button = GetNode<Button>("%Button");
		button.ButtonUp += () => EmitSignalItemSelected(this);

		// Relabel live when a hidden identity is discovered elsewhere this run, so
		// every stack of the identified type reveals its true name at once.
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.ItemIdentified += OnItemIdentified,
			signalBus => signalBus.ItemIdentified -= OnItemIdentified);

		Update();
	}

	public override void _ExitTree()
	{
		_unsubscribeSlot();
		_unsubscribeSlot = () => { };
		base._ExitTree();
	}

	public void SetItem(InventoryItemSlot slot, bool isEquipped)
	{
		_unsubscribeSlot();
		_unsubscribeSlot = () => { };

		Slot = slot;
		IsEquipped = isEquipped;

		_unsubscribeSlot = this.SubscribeUntilExit(
			Slot,
			slot => slot.Changed += Update,
			slot => slot.Changed -= Update);

		Update();
	}

	private void OnItemIdentified(string typeId)
	{
		// Cheap to refresh unconditionally; only the matching type changes name/tint.
		Update();
	}

	/// <summary>
	/// Applies the inventory's targeting-mode visuals. Ineligible slots dim and disable
	/// their button so the eligibility signal is unambiguous at a glance; eligible slots
	/// (and no active targeting) look and behave normally.
	/// </summary>
	public void SetTargetingState(SlotTargetingState state)
	{
		var button = GetNode<ItemSlotButton>("%Button");
		button.Disabled = state == SlotTargetingState.Ineligible;
		Modulate = state == SlotTargetingState.Ineligible ? new Color(1f, 1f, 1f, 0.35f) : Colors.White;
	}

	private void Update()
	{
		var button = GetNode<ItemSlotButton>("%Button");
		var colorRect = GetNode<ColorRect>("%ColorRect");
		var preview = GetNode<Preview>("%Preview");
		var quantityLabel = GetNode<Label>("%QuantityLabel");
		var equippedBorder = GetNode<Panel>("%EquippedBorder");

		if (Slot != null && Slot.Item != null)
		{
			preview.SetScene(Slot.Item.Scene, ItemIdentity.ResolveTint(Slot.Item));
			// Plain text is the fallback; the rich hover tooltip is built by ItemSlotButton.
			button.Item = Slot.Item;
			button.TooltipText = ItemIdentity.ResolveDisplayName(Slot.Item);
			quantityLabel.Text = Slot.Quantity > 1 ? Slot.Quantity.ToString() : "";
			equippedBorder.Visible = IsEquipped;

			if (Slot.Item is EquipableItem equipableItem && ItemIdentity.IsIdentified(Slot.Item))
			{
				colorRect.Color = equipableItem.Rarity switch
				{
					EquipableItemRarity.Common => CommonColor,
					EquipableItemRarity.Uncommon => UncommonColor,
					EquipableItemRarity.Rare => RareColor,
					EquipableItemRarity.Legendary => LegendaryColor,
					EquipableItemRarity.Unique => UniqueColor,
					_ => DefaultColor,
				};
			}
			else
			{
				colorRect.Color = DefaultColor;
			}
		}
		else
		{
			preview.SetScene(null);
			button.Item = null;
			button.TooltipText = "";
			quantityLabel.Text = "";
			equippedBorder.Visible = false;
			colorRect.Color = DefaultColor;
		}
	}
}
