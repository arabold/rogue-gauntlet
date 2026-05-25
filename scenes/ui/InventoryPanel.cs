using Godot;
using System;

public partial class InventoryPanel : ScrollContainer
{
	[Export] public PackedScene InventoryItemScene;

	public GridContainer InventoryGrid;
	private Inventory _inventory;
	private Action _unsubscribeInventory = () => { };

	public override void _Ready()
	{
		InventoryGrid = GetNode<GridContainer>("%InventoryGrid");
		Update();
	}

	public void Initialize(Inventory inventory)
	{
		_unsubscribeInventory();
		_unsubscribeInventory = () => { };

		_inventory = inventory;
		if (_inventory != null)
		{
			_unsubscribeInventory = this.SubscribeUntilExit(
				_inventory,
				inventory =>
				{
					inventory.Changed += Update;
					inventory.ItemEquipped += OnItemEquipped;
					inventory.ItemUnequipped += OnItemUnequipped;
				},
				inventory =>
				{
					inventory.Changed -= Update;
					inventory.ItemEquipped -= OnItemEquipped;
					inventory.ItemUnequipped -= OnItemUnequipped;
				});
		}

		Update();
	}

	public override void _ExitTree()
	{
		_unsubscribeInventory();
		_unsubscribeInventory = () => { };
		_inventory = null;

		base._ExitTree();
	}

	private void Update()
	{
		// Clear existing items
		foreach (Node child in InventoryGrid.GetChildren())
		{
			child.QueueFree();
		}

		if (_inventory == null)
		{
			return;
		}

		// Add items
		foreach (InventoryItemSlot slot in _inventory.Items)
		{
			var itemSlotPanel = InventoryItemScene.Instantiate<ItemSlotPanel>();
			itemSlotPanel.SetItem(slot, _inventory.IsEquipped(slot));
			itemSlotPanel.ItemSelected += OnItemSelected;

			InventoryGrid.AddChild(itemSlotPanel);
		}
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		Update();
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		Update();
	}

	private void OnItemSelected(ItemSlotPanel itemSlotPanel)
	{
		if (_inventory == null)
		{
			return;
		}

		var contextMenu = GetNode<InventoryItemContextMenu>("%InventoryItemContextMenu");
		contextMenu.Initialize(_inventory, itemSlotPanel.Slot);

		var rect = itemSlotPanel.GetGlobalRect();
		contextMenu.Position =
			new Vector2I((int)rect.Position.X, (int)rect.Position.Y)
				+ new Vector2I((int)rect.Size.X, 0);

		// contextMenu.PopupExclusive(this);
		contextMenu.Popup();
	}
}
