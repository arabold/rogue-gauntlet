using Godot;
using System;

public partial class InventoryDialog : PanelContainer
{
	[Export] public PackedScene InventoryItemScene;

	public Button CloseButton;
	public GridContainer InventoryGrid;
	public EquipmentPanel EquipmentPanel;

	private Inventory _inventory;

	public override void _Ready()
	{
		CloseButton = GetNode<Button>("%CloseButton");
		CloseButton.Pressed += OnCloseButtonPressed;

		InventoryGrid = GetNode<GridContainer>("%InventoryGrid");
		EquipmentPanel = GetNode<EquipmentPanel>("%EquipmentPanel");

		Update();
	}

	public void Open(Inventory inventory)
	{
		_inventory = inventory;
		_inventory.InventoryUpdated += Update;
		_inventory.ItemEquipped += UpdateEquipped;
		_inventory.ItemUnequipped += UpdateUnequipped;
		Update();
		Show();
	}

	public void Close()
	{
		_inventory.InventoryUpdated -= Update;
		_inventory.ItemEquipped -= UpdateEquipped;
		_inventory.ItemUnequipped -= UpdateUnequipped;
		_inventory = null;
		Hide();
		Update();
	}

	private void OnCloseButtonPressed()
	{
		Close();
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
			itemSlotPanel.Slot = slot;
			itemSlotPanel.IsEquipped = _inventory.GetEquipmentSlot(slot.Item) != null;
			itemSlotPanel.ItemSelected += OnItemSelected;

			InventoryGrid.AddChild(itemSlotPanel);
		}
	}

	private void UpdateEquipped(EquipmentSlot slot, EquippableItem item)
	{
		switch (slot)
		{
			case EquipmentSlot.Neck:
				EquipmentPanel.AmuletPanel.SetItem(item);
				break;
			case EquipmentSlot.Head:
				EquipmentPanel.HelmetPanel.SetItem(item);
				break;
			case EquipmentSlot.Arrows:
				EquipmentPanel.ArrowsPanel.SetItem(item);
				break;
			case EquipmentSlot.WeaponHand:
				EquipmentPanel.WeaponPanel.SetItem(item);
				break;
			case EquipmentSlot.Chest:
				EquipmentPanel.ArmorPanel.SetItem(item);
				break;
			case EquipmentSlot.ShieldHand:
				EquipmentPanel.ShieldPanel.SetItem(item);
				break;
			case EquipmentSlot.RightRing:
				EquipmentPanel.RightRingPanel.SetItem(item);
				break;
			case EquipmentSlot.Hands:
				EquipmentPanel.GlovesPanel.SetItem(item);
				break;
			case EquipmentSlot.LeftRing:
				EquipmentPanel.LeftRingPanel.SetItem(item);
				break;
			case EquipmentSlot.Legs:
				EquipmentPanel.LegsPanel.SetItem(item);
				break;
			case EquipmentSlot.Feet:
				EquipmentPanel.BootsPanel.SetItem(item);
				break;
		}

		Update();
	}

	private void UpdateUnequipped(EquipmentSlot slot, EquippableItem item)
	{
		UpdateEquipped(slot, null);
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
