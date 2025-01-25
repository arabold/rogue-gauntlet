using Godot;
using System;

public partial class InventoryDialog : PanelContainer
{
	[Export] public PackedScene InventoryItemScene;

	public Button CloseButton;
	public GridContainer InventoryGrid;

	public override void _Ready()
	{
		CloseButton = GetNode<Button>("%CloseButton");
		CloseButton.Pressed += OnCloseButtonPressed;

		InventoryGrid = GetNode<GridContainer>("%InventoryGrid");
	}

	public void Open(Inventory inventory)
	{
		GD.Print($"{inventory.Items.Count} items in inventory");

		// Clear existing items
		foreach (Node child in InventoryGrid.GetChildren())
		{
			child.QueueFree();
		}
		// Add new items
		foreach (InventoryItemSlot slot in inventory.Items)
		{
			var itemSlotPanel = InventoryItemScene.Instantiate<ItemSlotPanel>();
			itemSlotPanel.SetItem(slot);
			InventoryGrid.AddChild(itemSlotPanel);
		}

		Show();
	}

	private void OnCloseButtonPressed()
	{
		Hide();
	}
}
