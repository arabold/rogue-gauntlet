using Godot;

public partial class ItemSlotPanel : PanelContainer
{
	public void SetItem(InventoryItemSlot slot)
	{
		slot.ItemChanged += () => UpdateItem(slot);
		UpdateItem(slot);
	}

	private void UpdateItem(InventoryItemSlot slot)
	{
		var preview = GetNode<Preview>("%Preview");
		var quantityLabel = GetNode<Label>("%QuantityLabel");

		preview.SetScene(slot.Item.Scene);

		quantityLabel.Text = slot.Quantity > 1 ? slot.Quantity.ToString() : "";
	}
}
