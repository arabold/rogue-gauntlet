using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Inventory : Resource
{
	/// <summary>
	/// The maximum number of items that can be stored in the inventory.
	/// </summary>
	[Export] public int Capacity = 20;
	/// <summary>
	/// The list of items in the inventory.
	/// </summary>
	[Export] public Array<InventoryItemSlot> Items = new Array<InventoryItemSlot>();

	public bool IsFull => Items.Count >= Capacity;

	public void AddItem(Item item, int quantity = 1)
	{
		if (IsFull)
		{
			GD.Print("Inventory is full");
			return;
		}

		// Find an existing stackable item
		foreach (var slot in Items)
		{
			if (slot.Item == item && slot.IsStackable)
			{
				slot.Quantity += quantity;
				GD.Print($"Stacked {item.Name} ({slot.Quantity}) in inventory");
				return;
			}
		}

		// Add a new item
		var newSlot = new InventoryItemSlot { Item = item, Quantity = quantity };
		Items.Add(newSlot);
		GD.Print($"{item.Name} added to inventory");
	}

	public void RemoveItem(Item item)
	{
		foreach (var slot in Items)
		{
			if (slot.Item == item)
			{
				slot.Quantity--;
				if (slot.Quantity <= 0)
				{
					Items.Remove(slot);
				}
				GD.Print($"{item.Name} removed from inventory");
				return;
			}
		}
		GD.Print($"{item.Name} not found in inventory");
	}
}
