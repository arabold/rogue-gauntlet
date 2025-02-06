using Godot;

[Tool]
public partial class ItemSlotPanel : PanelContainer
{
	[Signal] public delegate void ItemSelectedEventHandler(ItemSlotPanel itemSlotPanel);

	[Export] public Color CommonColor = new Color(0.8f, 0.8f, 0.0f, 0.1f);
	[Export] public Color UncommonColor = new Color(0.0f, 0.8f, 0.0f, 0.1f);
	[Export] public Color RareColor = new Color(0.0f, 0.0f, 0.8f, 0.1f);
	[Export] public Color LegendaryColor = new Color(0.8f, 0.0f, 0.8f, 0.1f);
	[Export] public Color UniqueColor = new Color(0.8f, 0.8f, 0.0f, 0.1f);
	[Export] public Color DefaultColor = new Color(0.8f, 0.8f, 0.8f, 0.1f);

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

		Update();
	}

	public override void _ExitTree()
	{
		if (Slot != null)
		{
			Slot.Changed -= Update;
		}
		base._ExitTree();
	}

	public void SetItem(InventoryItemSlot slot, bool isEquipped)
	{
		if (Slot != null)
		{
			Slot.Changed -= Update;
		}

		Slot = slot;
		IsEquipped = isEquipped;

		if (Slot != null)
		{
			Slot.Changed += Update;
		}

		Update();
	}

	private void Update()
	{
		var colorRect = GetNode<ColorRect>("%ColorRect");
		var preview = GetNode<Preview>("%Preview");
		var quantityLabel = GetNode<Label>("%QuantityLabel");
		var equippedBorder = GetNode<Panel>("%EquippedBorder");

		if (Slot != null && Slot.Item != null)
		{
			preview.SetScene(Slot.Item.Scene);
			quantityLabel.Text = Slot.Quantity > 1 ? Slot.Quantity.ToString() : "";
			equippedBorder.Visible = IsEquipped;

			if (Slot.Item is EquipableItem equipableItem)
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
			quantityLabel.Text = "";
			equippedBorder.Visible = false;
			colorRect.Color = DefaultColor;
		}
	}
}
