using Godot;

public partial class ItemSlotPanel : PanelContainer
{
	[Signal] public delegate void ItemSelectedEventHandler(ItemSlotPanel itemSlotPanel);

	public bool IsEquipped
	{
		get => _isEquipped;
		set
		{
			_isEquipped = value;
			if (IsNodeReady())
			{
				Update();
			}
		}
	}

	public InventoryItemSlot Slot
	{
		get => _slot;
		set
		{
			if (_slot != null)
			{
				_slot.ItemChanged -= Update;
			}
			_slot = value;
			if (_slot != null)
			{
				_slot.ItemChanged += Update;
			}
			if (IsNodeReady())
			{
				Update();
			}
		}
	}

	private bool _isEquipped = false;
	private InventoryItemSlot _slot = null;

	~ItemSlotPanel()
	{
		if (_slot != null)
		{
			_slot.ItemChanged -= Update;
		}
	}

	override public void _Ready()
	{
		var button = GetNode<Button>("%Button");
		button.ButtonUp += () => EmitSignal(SignalName.ItemSelected, this);

		Update();
	}

	private void Update()
	{
		var preview = GetNode<Preview>("%Preview");
		var quantityLabel = GetNode<Label>("%QuantityLabel");
		var equippedBorder = GetNode<Panel>("%EquippedBorder");

		if (_slot != null && _slot.Item != null)
		{
			preview.SetScene(_slot.Item.Scene);

			quantityLabel.Text = _slot.Quantity > 1 ? _slot.Quantity.ToString() : "";
			equippedBorder.Visible = IsEquipped;
		}
	}
}
