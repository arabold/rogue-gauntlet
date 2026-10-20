using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Preflights the whole inventory before callers replace a live player's state.</summary>
public static class InventorySaveCodec
{
	public static InventorySaveData Capture(Inventory inventory)
	{
		var data = new InventorySaveData { Capacity = inventory.Capacity };
		foreach (var entry in inventory.Items) { data.Items.Add(ItemSaveCodec.Capture(entry.Item, entry.Quantity)); }
		foreach (var pair in inventory.EquippedItems.Where(p => p.Value != null))
		{
			data.EquippedItems.Add(new EquippedItemSaveData { Slot = pair.Key.ToString(), ItemInstanceId = pair.Value.Item.InstanceId });
		}
		return data;
	}

	public static Inventory Restore(InventorySaveData data)
	{
		if (data == null || data.Capacity <= 0 || data.Items.Count > data.Capacity)
		{ throw new InvalidOperationException("Invalid saved inventory capacity."); }
		var inventory = new Inventory { Capacity = data.Capacity };
		var ids = new HashSet<string>();
		foreach (var record in data.Items)
		{
			Item item = ItemSaveCodec.Restore(record);
			if (!ids.Add(item.InstanceId)) { throw new InvalidOperationException("Duplicate saved item identity."); }
			inventory.Items.Add(new InventoryItemSlot { Item = item, Quantity = record.Quantity });
		}
		var slots = new HashSet<EquipmentSlot>();
		var equippedIds = new HashSet<string>();
		foreach (var record in data.EquippedItems)
		{
			var entry = inventory.Items.FirstOrDefault(e => e.Item.InstanceId == record.ItemInstanceId);
			if (!Enum.TryParse(record.Slot, out EquipmentSlot slot) || !inventory.EquippedItems.ContainsKey(slot)
				|| !slots.Add(slot) || !equippedIds.Add(record.ItemInstanceId) || entry?.Item is not EquipableItem equipment || ((int)equipment.ValidSlots & (int)slot) == 0)
			{ throw new InvalidOperationException("Invalid saved equipment reference."); }
		}
		var weapon = data.EquippedItems.FirstOrDefault(e => e.Slot == nameof(EquipmentSlot.WeaponHand));
		if (weapon != null && inventory.Items.First(e => e.Item.InstanceId == weapon.ItemInstanceId).Item is Weapon { IsTwoHanded: true }
			&& slots.Contains(EquipmentSlot.ShieldHand)) { throw new InvalidOperationException("Conflicting saved hand equipment."); }
		return inventory;
	}
}