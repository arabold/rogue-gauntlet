using Godot;
using Godot.Collections;
using System;

public enum EquipmentSlot
{
	Head = 0,
	Chest = 1,
	Hands = 2,
	Legs = 3,
	Feet = 4,
	Neck = 5,
	LeftRing = 6,
	RightRing = 7,
	WeaponHand = 8,
	ShieldHand = 9,
	Arrows = 10,
}

[GlobalClass]
public partial class Inventory : Resource
{
	[Signal] public delegate void ItemEquippedEventHandler(EquipmentSlot slot, EquippableItem item);
	[Signal] public delegate void ItemUnequippedEventHandler(EquipmentSlot slot, EquippableItem item);
	[Signal] public delegate void ItemConsumedEventHandler(ConsumableItem item);
	[Signal] public delegate void ItemDroppedEventHandler(Item item, int quantity);
	[Signal] public delegate void ItemDestroyedEventHandler(Item item, int quantity);

	public Dictionary<EquipmentSlot, EquippableItem> EquippedItems { get; private set; } = new Dictionary<EquipmentSlot, EquippableItem>{
		{ EquipmentSlot.Head, null },
		{ EquipmentSlot.Chest, null },
		{ EquipmentSlot.Hands, null },
		{ EquipmentSlot.Legs, null },
		{ EquipmentSlot.Feet, null },
		{ EquipmentSlot.Neck, null },
		{ EquipmentSlot.LeftRing, null },
		{ EquipmentSlot.RightRing, null },
		{ EquipmentSlot.WeaponHand, null },
		{ EquipmentSlot.ShieldHand, null },
	};

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

		if (quantity <= 0)
		{
			return;
		}

		// Find an existing stackable item
		foreach (var slot in Items)
		{
			if (slot.Item == item && slot.IsStackable)
			{
				slot.Quantity += quantity;
				GD.Print($"Stacked {item.Name} ({slot.Quantity}) in inventory");
				EmitChanged();
				return;
			}
		}

		// Add a new item
		var newSlot = new InventoryItemSlot { Item = item, Quantity = quantity };
		Items.Add(newSlot);
		GD.Print($"{quantity}x {item.Name} added to inventory");
		EmitChanged();
	}

	public void RemoveItem(InventoryItemSlot itemSlot, int quantity)
	{
		quantity = Math.Clamp(quantity, 0, itemSlot.Quantity);
		itemSlot.Quantity -= quantity;
		if (itemSlot.Quantity <= 0)
		{
			Items.Remove(itemSlot);
		}
		GD.Print($"{quantity}x {itemSlot.Item.Name} removed from inventory");
		EmitChanged();
		return;
	}

	public EquipmentSlot? GetEquipmentSlot(Item item)
	{
		if (item == null)
		{
			return null;
		}

		foreach (var (slot, equippedItem) in EquippedItems)
		{
			if (equippedItem == item)
			{
				return slot;
			}
		}

		return null;
	}

	public bool IsEquipped(InventoryItemSlot slot)
	{
		var item = slot.Item;
		return GetEquipmentSlot(item) != null;
	}

	public void Equip(InventoryItemSlot itemSlot)
	{
		var item = itemSlot.Item as EquippableItem;
		var equipSlot = getDefaultEquipmentSlotForItem(item);
		if (item.Type == EquippableItemType.Weapon)
		{
			var isTwoHanded = (item is Weapon weapon) ? weapon.IsTwoHanded : false;
			if (isTwoHanded)
			{
				// Also unequip the other hand if it's occupied
				Unequip(EquipmentSlot.ShieldHand);
			}
		}
		else if (item.Type == EquippableItemType.Shield)
		{
			// Also unequip the weapon if it's two-handed
			if (EquippedItems[EquipmentSlot.WeaponHand] is Weapon weapon && weapon.IsTwoHanded)
			{
				Unequip(EquipmentSlot.WeaponHand);
			}
		}
		else if (item.Type == EquippableItemType.Ring)
		{
			// Check if the left ring slot is occupied
			if (EquippedItems[EquipmentSlot.LeftRing] == null)
			{
				equipSlot = EquipmentSlot.LeftRing;
			}
			// Check if the right ring slot is occupied
			else if (EquippedItems[EquipmentSlot.RightRing] == null)
			{
				equipSlot = EquipmentSlot.RightRing;
			}
		}

		Unequip(equipSlot);

		GD.Print($"{item.Name} equipped to {equipSlot}");
		EquippedItems[equipSlot] = item;
		EmitSignalItemEquipped(equipSlot, item);
	}

	public void Unequip(InventoryItemSlot itemSlot)
	{
		var equipSlot = GetEquipmentSlot(itemSlot.Item);
		if (equipSlot != null)
		{
			Unequip((EquipmentSlot)equipSlot);
		}
		else
		{
			GD.PrintErr($"{itemSlot.Item.Name} is not equipped");
		}
	}

	public void Unequip(EquipmentSlot itemSlot)
	{
		var item = EquippedItems[itemSlot];
		if (item != null)
		{
			GD.Print($"{item.Name} unequipped from {itemSlot}");
			EquippedItems[itemSlot] = null;
			EmitSignalItemUnequipped(itemSlot, item);
		}
	}

	public void Consume(InventoryItemSlot itemSlot)
	{
		GD.Print($"Consuming {itemSlot.Item.Name} from inventory...");
		EmitSignalItemConsumed(itemSlot.Item as ConsumableItem);
		RemoveItem(itemSlot, 1);
	}

	public void DropItem(InventoryItemSlot itemSlot)
	{
		var item = itemSlot.Item;
		if (item.IsQuestItem)
		{
			GD.PrintErr($"{itemSlot.Item.Name} is a quest item and cannot be dropped");
			return;
		}

		var equipSlot = GetEquipmentSlot(item);
		if (equipSlot != null)
		{
			// Unequip the item first
			Unequip((EquipmentSlot)equipSlot);
		}

		var quantity = itemSlot.Quantity;
		GD.Print($"Dropping {quantity}x {itemSlot.Item.Name} from inventory...");
		EmitSignalItemDropped(itemSlot.Item, quantity);
		RemoveItem(itemSlot, quantity);
	}

	public void DestroyItem(InventoryItemSlot itemSlot)
	{
		var item = itemSlot.Item;
		if (item.IsQuestItem)
		{
			GD.PrintErr($"{itemSlot.Item.Name} is a quest item and cannot be destroyed");
			return;
		}

		var equipSlot = GetEquipmentSlot(item);
		if (equipSlot != null)
		{
			// Unequip the item first
			Unequip((EquipmentSlot)equipSlot);
		}

		var quantity = itemSlot.Quantity;
		GD.Print($"Destroying {quantity}x {itemSlot.Item.Name} from inventory...");
		EmitSignalItemDestroyed(itemSlot.Item, quantity);
		RemoveItem(itemSlot, quantity);
	}

	public void SplitItem(InventoryItemSlot itemSlot, int newQuantity)
	{
		if (IsFull)
		{
			GD.Print("Inventory is full");
			return;
		}

		newQuantity = Math.Clamp(newQuantity, 0, itemSlot.Quantity - 1);
		if (newQuantity > 0)
		{
			GD.Print($"Splitting {itemSlot.Quantity}x {itemSlot.Item.Name} in inventory...");
			RemoveItem(itemSlot, newQuantity);
			AddItem(itemSlot.Item, newQuantity);
		}
	}

	private EquipmentSlot getDefaultEquipmentSlotForItem(EquippableItem item)
	{
		if (item.Type == EquippableItemType.Helmet)
		{
			return EquipmentSlot.Head;
		}
		else if (item.Type == EquippableItemType.Armor)
		{
			return EquipmentSlot.Chest;
		}
		else if (item.Type == EquippableItemType.Gloves)
		{
			return EquipmentSlot.Hands;
		}
		else if (item.Type == EquippableItemType.Boots)
		{
			return EquipmentSlot.Feet;
		}
		else if (item.Type == EquippableItemType.Amulet)
		{
			return EquipmentSlot.Neck;
		}
		else if (item.Type == EquippableItemType.Pants)
		{
			return EquipmentSlot.Legs;
		}
		else if (item.Type == EquippableItemType.Weapon)
		{
			return EquipmentSlot.WeaponHand;
		}
		else if (item.Type == EquippableItemType.Shield)
		{
			return EquipmentSlot.ShieldHand;
		}
		else
		{
			return EquipmentSlot.WeaponHand;
		}
	}
}
