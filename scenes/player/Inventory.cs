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
	[Signal] public delegate void InventoryUpdatedEventHandler();
	[Signal] public delegate void ItemEquippedEventHandler(EquipmentSlot slot, EquippableItem item);
	[Signal] public delegate void ItemUnequippedEventHandler(EquipmentSlot slot, EquippableItem item);

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

		// Find an existing stackable item
		foreach (var slot in Items)
		{
			if (slot.Item == item && slot.IsStackable)
			{
				slot.Quantity += quantity;
				GD.Print($"Stacked {item.Name} ({slot.Quantity}) in inventory");
				EmitSignal(SignalName.InventoryUpdated);
				return;
			}
		}

		// Add a new item
		var newSlot = new InventoryItemSlot { Item = item, Quantity = quantity };
		Items.Add(newSlot);
		GD.Print($"{item.Name} added to inventory");
		EmitSignal(SignalName.InventoryUpdated);
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
				EmitSignal(SignalName.InventoryUpdated);
				return;
			}
		}
		GD.Print($"{item.Name} not found in inventory");
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

	public bool IsEquipped(Item item)
	{
		return GetEquipmentSlot(item) != null;
	}

	public void Equip(EquippableItem item)
	{
		var slot = getSlotForItem(item);
		if (item.Type == EquippableItemType.Weapon)
		{
			var isTwoHanded = (item is Weapon weapon) ? weapon.IsTwoHanded : false;
			if (isTwoHanded)
			{
				// Also unequip the other hand if it's occupied
				Unequip(EquipmentSlot.ShieldHand);
			}
		}
		else if (item.Type == EquippableItemType.Ring)
		{
			// Check if the left ring slot is occupied
			if (EquippedItems[EquipmentSlot.LeftRing] == null)
			{
				slot = EquipmentSlot.LeftRing;
			}
			// Check if the right ring slot is occupied
			else if (EquippedItems[EquipmentSlot.RightRing] == null)
			{
				slot = EquipmentSlot.RightRing;
			}
		}

		Unequip(slot);

		GD.Print($"{item.Name} equipped to {slot}");
		EquippedItems[slot] = item;
		EmitSignal(SignalName.ItemEquipped, (int)slot, item);

		SignalBus.EmitItemEquipped(item);
	}

	public void Unequip(EquippableItem item)
	{
		var slot = GetEquipmentSlot(item);
		if (slot != null)
		{
			Unequip((EquipmentSlot)slot);
		}
		else
		{
			GD.PrintErr($"{item.Name} is not equipped");
		}
	}

	public void Unequip(EquipmentSlot slot)
	{
		var item = EquippedItems[slot];
		if (item != null)
		{
			GD.Print($"{item.Name} unequipped from {slot}");
			EquippedItems[slot] = null;
			EmitSignal(SignalName.ItemUnequipped, (int)slot, item);

			SignalBus.EmitItemUnequipped(item);
		}
	}

	private EquipmentSlot getSlotForItem(EquippableItem item)
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
