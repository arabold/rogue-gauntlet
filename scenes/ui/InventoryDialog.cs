using Godot;
using System;

public partial class InventoryDialog : PanelContainer
{
	[Export] public PackedScene InventoryItemScene;

	public Button CloseButton;
	public GridContainer InventoryGrid;

	private Inventory _inventory;

	public override void _Ready()
	{
		CloseButton = GetNode<Button>("%CloseButton");
		CloseButton.Pressed += OnCloseButtonPressed;

		InventoryGrid = GetNode<GridContainer>("%InventoryGrid");

		Update();
	}

	public void Open(Inventory inventory)
	{
		_inventory = inventory;
		_inventory.InventoryUpdated += Update;
		Update();
		Show();
	}

	public void Close()
	{
		_inventory.InventoryUpdated -= Update;
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
			itemSlotPanel.SetItem(slot);
			InventoryGrid.AddChild(itemSlotPanel);
		}
	}
}
