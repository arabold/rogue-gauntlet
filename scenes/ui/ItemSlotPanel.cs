using Godot;

[Tool]
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
		private set => _slot = value;
	}

	private bool _isEquipped = false;
	private InventoryItemSlot _slot = null;

	[Export] public Color CommonColor = new Color(0.8f, 0.8f, 0.0f, 0.1f);
	[Export] public Color UncommonColor = new Color(0.0f, 0.8f, 0.0f, 0.1f);
	[Export] public Color RareColor = new Color(0.0f, 0.0f, 0.8f, 0.1f);
	[Export] public Color LegendaryColor = new Color(0.8f, 0.0f, 0.8f, 0.1f);
	[Export] public Color UniqueColor = new Color(0.8f, 0.8f, 0.0f, 0.1f);
	[Export] public Color DefaultColor = new Color(0.8f, 0.8f, 0.8f, 0.1f);

	public override void _Ready()
	{
		var button = GetNode<Button>("%Button");
		button.ButtonUp += () => EmitSignalItemSelected(this);

		Update();
	}

	public override void _ExitTree()
	{
		if (_slot != null)
		{
			_slot.Changed -= Update;
		}
		base._ExitTree();
	}

	public void SetItem(InventoryItemSlot slot, bool isEquipped)
	{
		if (_slot != null)
		{
			_slot.Changed -= Update;
		}

		_slot = slot;
		_isEquipped = isEquipped;

		if (_slot != null)
		{
			_slot.Changed += Update;
		}

		Update();
	}

	private void Update()
	{
		var colorRect = GetNode<ColorRect>("%ColorRect");
		var preview = GetNode<Preview>("%Preview");
		var quantityLabel = GetNode<Label>("%QuantityLabel");
		var equippedBorder = GetNode<Panel>("%EquippedBorder");

		if (_slot != null && _slot.Item != null)
		{
			preview.SetScene(_slot.Item.Scene);
			quantityLabel.Text = _slot.Quantity > 1 ? _slot.Quantity.ToString() : "";
			equippedBorder.Visible = IsEquipped;

			if (_slot.Item is EquippableItem equippableItem)
			{
				colorRect.Color = equippableItem.Rarity switch
				{
					EquippableItemRarity.Common => CommonColor,
					EquippableItemRarity.Uncommon => UncommonColor,
					EquippableItemRarity.Rare => RareColor,
					EquippableItemRarity.Legendary => LegendaryColor,
					EquippableItemRarity.Unique => UniqueColor,
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
			quantityLabel.Text = "";
			equippedBorder.Visible = false;
			colorRect.Color = DefaultColor;
		}
	}
}
