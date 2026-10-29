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

	/// <summary>Opacity of the <see cref="ItemQualityPalette"/> tint behind Magic and Rare equipment.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float QualityTintAlpha { get; set; } = 0.25f;
	[Export] public Color DefaultColor { get; set; } = new(0.8f, 0.8f, 0.8f, 0.1f);

	private ItemSlotButton _button;
	private ColorRect _colorRect;
	private Preview _preview;
	private Label _quantityLabel;
	private Panel _equippedBorder;
	private Action _unsubscribeSlot = () => { };
	private Action _unsubscribeItem = () => { };
	/// <summary>The item whose own <c>Changed</c> relabels this slot, e.g. when its attributes or curse are revealed.</summary>
	private Item _observedItem;
	/// <summary>
	/// Whether the button should stay disabled for a reason other than targeting-mode
	/// ineligibility (currently always false — no such condition exists yet). Composed with
	/// targeting ineligibility in <see cref="SetTargetingState"/> rather than that method
	/// unconditionally overwriting <c>Disabled</c>, so a future base-disabled reason can't be
	/// silently re-enabled by leaving targeting mode.
	/// </summary>
	private bool _baseDisabled;

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
		// Tool only for the authored layout; slot content, its script-typed nodes and bus events are runtime concerns.
		if (Engine.IsEditorHint()) return;
		CacheNodes();
		_button.ButtonUp += () => EmitSignalItemSelected(this);

		// Relabel live when a hidden type is discovered elsewhere this run, so every stack of
		// the identified type reveals its true name at once.
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.ItemIdentified += OnItemIdentified,
			signalBus => signalBus.ItemIdentified -= OnItemIdentified);

		Update();
	}

	private void CacheNodes()
	{
		_button ??= GetNode<ItemSlotButton>("%Button");
		_colorRect ??= GetNode<ColorRect>("%ColorRect");
		_preview ??= GetNode<Preview>("%Preview");
		_quantityLabel ??= GetNode<Label>("%QuantityLabel");
		_equippedBorder ??= GetNode<Panel>("%EquippedBorder");
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
		// Cheap to refresh unconditionally; only the matching type's name changes (its model stays the same).
		Update();
	}

	/// <summary>
	/// Applies the inventory's targeting-mode visuals. Ineligible slots dim and disable
	/// their button so the eligibility signal is unambiguous at a glance; eligible slots
	/// (and no active targeting) look and behave normally.
	/// </summary>
	public void SetTargetingState(SlotTargetingState state)
	{
		CacheNodes();
		_button.Disabled = _baseDisabled || state == SlotTargetingState.Ineligible;
		Modulate = state == SlotTargetingState.Ineligible ? new Color(1f, 1f, 1f, 0.35f) : Colors.White;
	}

	/// <summary>Follows the shown item's own <c>Changed</c>, so per-item reveals and in-place changes relabel the slot.</summary>
	private void ObserveItem(Item item)
	{
		if (item == _observedItem) { return; }
		_unsubscribeItem();
		_observedItem = item;
		_unsubscribeItem = this.SubscribeUntilExit(item, observed => observed.Changed += Update, observed => observed.Changed -= Update);
	}

	private void Update()
	{
		ObserveItem(Slot?.Item);
		CacheNodes();

		if (Slot != null && Slot.Item != null)
		{
			_preview.Scene = ItemIdentity.ResolveScene(Slot.Item);
			// Plain text is the fallback; the rich hover tooltip is built by ItemSlotButton.
			_button.Item = Slot.Item;
			_button.TooltipText = ItemIdentity.ResolveDisplayName(Slot.Item);
			_quantityLabel.Text = Slot.Item.CanStack ? Slot.Quantity.ToString() : "";
			_equippedBorder.Visible = IsEquipped;

			// The quality is never secret, so unidentified items tint too; only Magic and Rare stand out.
			_colorRect.Color = Slot.Item is EquipableItem { Quality: >= ItemQuality.Magic } equipment
				? ItemQualityPalette.Color(equipment.Quality) with { A = QualityTintAlpha }
				: DefaultColor;
		}
		else
		{
			_preview.Scene = null;
			_button.Item = null;
			_button.TooltipText = "";
			_quantityLabel.Text = "";
			_equippedBorder.Visible = false;
			_colorRect.Color = DefaultColor;
		}
	}
}
