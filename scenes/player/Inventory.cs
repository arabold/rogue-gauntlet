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
	[Signal] public delegate void ItemEquippedEventHandler(EquipableItem item, EquipmentSlot slot);
	[Signal] public delegate void ItemUnequippedEventHandler(EquipableItem item, EquipmentSlot slot);
	[Signal] public delegate void ItemConsumedEventHandler(ConsumableItem item);
	[Signal] public delegate void ItemDroppedEventHandler(Item item, int quantity);
	[Signal] public delegate void ItemDestroyedEventHandler(Item item, int quantity);

	public Dictionary<EquipmentSlot, InventoryItemSlot> EquippedItems { get; private set; } = new Dictionary<EquipmentSlot, InventoryItemSlot>{
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

	public bool IsEquipped(InventoryItemSlot itemSlot)
	{
		foreach (var equipSlot in EquippedItems.Keys)
		{
			if (EquippedItems[equipSlot] == itemSlot)
			{
				return true;
			}
		}
		return false;
	}

	public void Equip(InventoryItemSlot itemSlot)
	{
		var item = itemSlot.Item as EquipableItem;
		var validSlots = item.ValidSlots;

		if (validSlots.Count == 0)
		{
			GD.PrintErr($"{item.Name} cannot be equipped");
			return;
		}

		var isTwoHanded = (item is Weapon weapon) ? weapon.IsTwoHanded : false;
		if (isTwoHanded)
		{
			Unequip(EquipmentSlot.WeaponHand);
			Unequip(EquipmentSlot.ShieldHand);
			EquippedItems[EquipmentSlot.WeaponHand] = itemSlot;
			EmitSignalItemEquipped(item, EquipmentSlot.WeaponHand);
		}
		else
		{
			EquipmentSlot equipSlot = validSlots[0];
			foreach (var slot in validSlots)
			{
				if (EquippedItems[slot] == null)
				{
					equipSlot = slot;
					break;
				}
			}

			Unequip(equipSlot);

			// Special handling when unequipping a two-handed weapon
			if (equipSlot == EquipmentSlot.ShieldHand
				&& EquippedItems[EquipmentSlot.WeaponHand]?.Item is Weapon equippedWeapon
				&& equippedWeapon.IsTwoHanded)
			{
				Unequip(EquipmentSlot.WeaponHand);
			}

			EquippedItems[equipSlot] = itemSlot;
			EmitSignalItemEquipped(item, equipSlot);
		}
	}

	public void Unequip(InventoryItemSlot itemSlot)
	{
		foreach (var equipSlot in EquippedItems.Keys)
		{
			if (EquippedItems[equipSlot] == itemSlot)
			{
				Unequip(equipSlot);
			}
		}
	}

	public void Unequip(EquipmentSlot equipSlot)
	{
		var invSlot = EquippedItems[equipSlot];
		if (invSlot == null)
		{
			return;
		}

		var item = invSlot.Item as EquipableItem;
		GD.Print($"{item.Name} unequipped from {equipSlot}");
		EquippedItems[equipSlot] = null;
		EmitSignalItemUnequipped(item, equipSlot);
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

		// Unequip the item first
		Unequip(itemSlot);

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

		// Unequip the item first
		Unequip(itemSlot);

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
}
