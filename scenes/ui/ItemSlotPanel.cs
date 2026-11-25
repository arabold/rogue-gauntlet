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
	/// <summary>The slot was chosen: by the accept key while it holds the focus, or by a click with either mouse button.</summary>
	[Signal] public delegate void ItemSelectedEventHandler(ItemSlotPanel itemSlotPanel);
	/// <summary>The slot took the focus, by keys or by the pointer moving onto it; its item is the one to describe.</summary>
	[Signal] public delegate void ItemHighlightedEventHandler(ItemSlotPanel itemSlotPanel);

	/// <summary>Opacity of the <see cref="ItemQualityPalette"/> tint behind Magic and Rare equipment.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float QualityTintAlpha { get; set; } = 0.25f;
	[Export] public Color DefaultColor { get; set; } = new(0.8f, 0.8f, 0.8f, 0.1f);
	/// <summary>
	/// What an empty slot shows: the kit's icon for the kind of item it holds. Unset, an empty slot stays bare. Applied at
	/// runtime only: in the editor the slot's script-typed nodes are placeholders (see <see cref="_Ready"/>).
	/// </summary>
	[Export] public Texture2D EmptyIcon { get; set { field = value; if (IsNodeReady() && !Engine.IsEditorHint()) { Update(); } } }
	/// <summary>Fades the slot back without disabling it, e.g. gear that does not fit the slot being filled.</summary>
	public bool Dimmed { get; set { field = value; ApplyModulate(); } }

	private static readonly StringName Empty = "SlotPanel", Worn = "FilledSlotPanel";
	private Button _button;
	private TextureRect _emptyIcon;
	/// <summary>The selection glow, drawn behind the item so the model stands on it rather than under it.</summary>
	private Panel _glow;
	private SlotTargetingState _targeting;
	private ColorRect _colorRect;
	private ItemIcon _icon;
	private Preview _preview;
	private Label _quantityLabel;
	private Action _unsubscribeSlot = () => { };
	private Action _unsubscribeItem = () => { };
	/// <summary>The item whose own <c>Changed</c> relabels this slot, e.g. when its attributes or curse are revealed.</summary>
	private Item _observedItem;
	private bool _isPointedAt;

	public bool IsEquipped { get; private set; }
	public InventoryItemSlot Slot { get; private set; }

	public override void _Ready()
	{
		// Tool only for the authored layout; slot content, its script-typed nodes and bus events are runtime concerns.
		if (Engine.IsEditorHint()) return;
		CacheNodes();
		_button.Pressed += () => EmitSignalItemSelected(this);
		// The right button means the same as the left: both ask what to do with the item.
		_button.GuiInput += @event =>
		{
			if (!_button.Disabled && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
			{
				EmitSignalItemSelected(this);
			}
		};
		// Pointing at a slot focuses it, so pointer and keys always agree on which item is being looked at. The
		// focused item comes alive; every other slot stays a still, render-once icon.
		_button.MouseEntered += () =>
		{
			SetPointedAt(true);
			if (!_button.Disabled && _button.FocusMode != FocusModeEnum.None) { _button.GrabFocus(); }
		};
		_button.MouseExited += () => SetPointedAt(false);
		_button.FocusEntered += () => { OnFocusChanged(); EmitSignalItemHighlighted(this); };
		_button.FocusExited += OnFocusChanged;

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
		_button ??= GetNode<Button>("%Button");
		_emptyIcon ??= GetNode<TextureRect>("%EmptyIcon");
		_glow ??= GetNode<Panel>("%Glow");
		_colorRect ??= GetNode<ColorRect>("%ColorRect");
		_icon ??= GetNode<ItemIcon>("%Icon");
		_preview ??= _icon.Preview;
		_quantityLabel ??= GetNode<Label>("%QuantityLabel");
	}

	/// <summary>Gives the slot the keyboard focus, if it can take it.</summary>
	public void FocusSlot()
	{
		CacheNodes();
		if (!_button.Disabled && _button.FocusMode != FocusModeEnum.None) { _button.GrabFocus(); }
	}

	/// <summary>Whether the slot can be focused and pressed; an empty pack niche cannot.</summary>
	public bool Selectable
	{
		get;
		set
		{
			field = value;
			CacheNodes();
			_button.FocusMode = value ? FocusModeEnum.All : FocusModeEnum.None;
			_button.MouseFilter = value ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
		}
	} = true;

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

	// Tracked from the signals: the button only clears IsHovered() after MouseExited has fired.
	private void SetPointedAt(bool pointedAt)
	{
		_isPointedAt = pointedAt;
		UpdatePreviewAnimation();
	}

	private void OnFocusChanged()
	{
		_glow.Visible = _button.HasFocus();
		UpdatePreviewAnimation();
	}

	private void UpdatePreviewAnimation() => _preview.Animated = _isPointedAt || _button.HasFocus();

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
		_targeting = state;
		_button.Disabled = state == SlotTargetingState.Ineligible;
		ApplyModulate();
	}

	private void ApplyModulate() =>
		Modulate = _targeting == SlotTargetingState.Ineligible || Dimmed ? new Color(1f, 1f, 1f, 0.35f) : Colors.White;

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
			_preview.ShowItem(Slot.Item);
			_icon.Filled = true;
			_quantityLabel.Text = Slot.Item.CanStack ? Slot.Quantity.ToString() : "";
			// A worn item's niche is lit gold.
			ThemeTypeVariation = IsEquipped ? Worn : Empty;
			_emptyIcon.Visible = false;

			// The quality is never secret, so unidentified items tint too; only Magic and Rare stand out.
			_colorRect.Color = Slot.Item is EquipableItem { Quality: >= ItemQuality.Magic } equipment
				? ItemQualityPalette.Color(equipment.Quality) with { A = QualityTintAlpha }
				: DefaultColor;
		}
		else
		{
			_preview.ShowItem(null);
			_icon.Filled = false;
			_quantityLabel.Text = "";
			ThemeTypeVariation = Empty;
			_emptyIcon.Texture = EmptyIcon;
			_emptyIcon.Visible = EmptyIcon != null;
			_colorRect.Color = DefaultColor;
		}
	}
}
