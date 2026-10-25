using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// Equipment slots for equipable items.
/// 
/// We use a bitwise enum to allow items to be equipped in multiple slots.
/// However, for most use cases we will only use a single slot.
/// </summary>
public enum EquipmentSlot
{
	Head = 1 << 0,
	Chest = 1 << 1,
	Hands = 1 << 2,
	Legs = 1 << 3,
	Feet = 1 << 4,
	Neck = 1 << 5,
	LeftRing = 1 << 6,
	RightRing = 1 << 7,
	WeaponHand = 1 << 8,
	ShieldHand = 1 << 9,
	Arrows = 1 << 10,
}

[GlobalClass]
public partial class Inventory : Resource
{
	[Signal] public delegate void ItemEquippedEventHandler(EquipableItem item, EquipmentSlot slot);
	[Signal] public delegate void ItemUnequippedEventHandler(EquipableItem item, EquipmentSlot slot);
	/// <summary>A worn item changed in place, so listeners re-register its stats; it stays equipped throughout.</summary>
	[Signal] public delegate void EquippedItemChangedEventHandler(EquipableItem item);
	[Signal] public delegate void ItemConsumedEventHandler(ConsumableItem item);
	[Signal] public delegate void ItemDroppedEventHandler(Item item, int quantity);
	[Signal] public delegate void ItemDestroyedEventHandler(Item item, int quantity);

	public Dictionary<EquipmentSlot, InventoryItemSlot> EquippedItems { get; private set; } = new(){
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
		{ EquipmentSlot.Arrows, null },
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

	private int _updateDepth;
	private bool _changed;

	/// <summary>Holds UI notifications until a coordinated transfer has committed both owners.</summary>
	public IDisposable DeferChanges()
	{
		_updateDepth++;
		return new UpdateScope(this);
	}

	private sealed class UpdateScope(Inventory inventory) : IDisposable
	{
		public void Dispose()
		{
			if (--inventory._updateDepth == 0 && inventory._changed)
			{
				inventory._changed = false;
				inventory.EmitChanged();
			}
		}
	}

	private void NotifyChanged()
	{
		if (_updateDepth > 0) { _changed = true; } else { EmitChanged(); }
	}

	public Inventory CreateRuntimeCopy()
	{
		var inventory = new Inventory { Capacity = Capacity };
		foreach (var entry in Items)
		{
			foreach (var copy in ItemFactory.CreateBatch(entry.Item, entry.Quantity))
			{
				inventory.Items.Add(new InventoryItemSlot { Item = copy.Item, Quantity = copy.Quantity });
			}
		}
		return inventory;
	}

	public ItemOperationResult CanAdd(Item item, int quantity = 1)
	{
		if (item == null || quantity <= 0 || (!item.CanStack && quantity != 1))
		{ return new(ItemFailure.InvalidQuantity); }
		if (Items.Any(e => e.Item == item || e.Item.InstanceId == item.InstanceId && item.IsRuntimeInstance))
		{ return new(ItemFailure.NotOwned); }
		long remaining = quantity;
		if (item.CanStack)
		{
			foreach (var entry in Items.Where(e => CanMerge(e.Item, item)))
			{ remaining -= Math.Max(0, item.MaxStackSize - entry.Quantity); }
		}
		int cap = item.CanStack ? item.MaxStackSize : 1;
		long slots = Math.Max(0, (remaining + cap - 1) / cap);
		return slots <= Math.Max(0, Capacity - Items.Count) ? ItemOperationResult.Ok : new(ItemFailure.InsufficientSpace);
	}

	private static bool CanMerge(Item a, Item b) => a.CanStack && b.CanStack
		&& !string.IsNullOrEmpty(a.DefinitionPath) && a.DefinitionPath == b.DefinitionPath;

	public ItemOperationResult TryAdd(Item item, int quantity = 1)
	{
		ItemOperationResult check = CanAdd(item, quantity);
		if (!check.Success) { return check; }
		if (!item.IsRuntimeInstance) { item = ItemFactory.Create(item); }
		int remaining = quantity;
		if (item.CanStack)
		{
			foreach (var entry in Items.Where(e => CanMerge(e.Item, item)))
			{
				int count = Math.Min(remaining, Math.Max(0, item.MaxStackSize - entry.Quantity));
				entry.Quantity += count;
				remaining -= count;
				if (remaining == 0) { break; }
			}
		}
		bool first = true;
		while (remaining > 0)
		{
			int count = Math.Min(remaining, item.CanStack ? item.MaxStackSize : 1);
			Items.Add(new InventoryItemSlot { Item = first ? item : ItemFactory.CopyDefinition(item), Quantity = count });
			first = false;
			remaining -= count;
		}
		NotifyChanged();
		return ItemOperationResult.Ok;
	}

	public void AddItem(Item item, int quantity = 1) => TryAdd(item, quantity);

	/// <summary>Whether a quantity can leave the backpack; a worn cursed item cannot (<see cref="ItemFailure.Cursed"/>).</summary>
	public ItemOperationResult CanRemove(InventoryItemSlot entry, int quantity)
	{
		if (entry == null || !Items.Contains(entry)) { return new(ItemFailure.NotOwned); }
		if (quantity <= 0 || quantity > entry.Quantity) { return new(ItemFailure.InvalidQuantity); }
		return quantity == entry.Quantity ? CanVacate(SlotsOf(entry)) : ItemOperationResult.Ok;
	}

	public void RemoveItem(InventoryItemSlot entry, int quantity)
	{
		if (!CanRemove(entry, quantity).Success) { return; }
		if (quantity == entry.Quantity) { ReleaseEntry(entry); Items.Remove(entry); }
		else { entry.Quantity -= quantity; }
		NotifyChanged();
	}

	public ItemOperationResult TrySplit(InventoryItemSlot entry, int quantity)
	{
		if (!CanRemove(entry, quantity).Success) { return new(ItemFailure.NotOwned); }
		if (!entry.Item.CanStack || quantity >= entry.Quantity) { return new(ItemFailure.InvalidQuantity); }
		if (IsFull) { return new(ItemFailure.InsufficientSpace); }
		entry.Quantity -= quantity;
		Items.Add(new InventoryItemSlot { Item = ItemFactory.CopyDefinition(entry.Item), Quantity = quantity });
		NotifyChanged();
		return ItemOperationResult.Ok;
	}

	public ItemOperationResult TryMerge(InventoryItemSlot source, InventoryItemSlot target)
	{
		if (source == target || !Items.Contains(source) || !Items.Contains(target)) { return new(ItemFailure.NotOwned); }
		if (!CanMerge(source.Item, target.Item) || source.Quantity > target.Item.MaxStackSize - target.Quantity)
		{ return new(ItemFailure.IncompatibleStack); }
		target.Quantity += source.Quantity;
		Items.Remove(source);
		NotifyChanged();
		return ItemOperationResult.Ok;
	}

	public bool IsEquipped(InventoryItemSlot itemSlot) => SlotsOf(itemSlot).Any();

	/// <summary>
	/// Equips into the first free valid slot, else the first valid one; two-handed weapons always take the weapon hand.
	/// The choice ignores curses, so a hidden cursed occupant blocks the swap and reveals itself instead of being skipped.
	/// </summary>
	public ItemOperationResult Equip(InventoryItemSlot itemSlot)
	{
		if (itemSlot == null || !Items.Contains(itemSlot) || itemSlot.Item is not EquipableItem item) { return new(ItemFailure.NotOwned); }
		var validSlots = (int)item.ValidSlots;

		if (validSlots == 0)
		{
			GD.PrintErr($"{item.Name} cannot be equipped");
			return new(ItemFailure.InvalidTarget);
		}

		if (item is Weapon { IsTwoHanded: true })
		{
			return Equip(itemSlot, EquipmentSlot.WeaponHand);
		}

		EquipmentSlot[] candidates = item is PortableLightItem
			? new[] { EquipmentSlot.ShieldHand, EquipmentSlot.WeaponHand }.Where(slot => (validSlots & (int)slot) != 0).ToArray()
			: Enum.GetValues<EquipmentSlot>().Where(slot => (validSlots & (int)slot) != 0).ToArray();
		return Equip(itemSlot, candidates.FirstOrDefault(slot => EquippedItems[slot] == null, candidates[0]));
	}

	/// <summary>
	/// Equips into <paramref name="equipSlot"/>, first removing whatever that displaces: the slot's occupant, the item's
	/// own current slot, and the other hand for two-handed combinations. Fails without any change when a displaced item
	/// is cursed, naming it as the result's subject.
	/// </summary>
	public ItemOperationResult Equip(InventoryItemSlot itemSlot, EquipmentSlot equipSlot)
	{
		if (itemSlot == null || !Items.Contains(itemSlot) || itemSlot.Item is not EquipableItem item) { return new(ItemFailure.NotOwned); }
		var validSlots = (int)item.ValidSlots;
		if ((validSlots & (int)equipSlot) == 0)
		{
			GD.PrintErr($"{item.Name} cannot be equipped in {equipSlot}");
			return new(ItemFailure.InvalidTarget);
		}

		if (item is PortableLightItem light && (light.HeldScene == null || light.GripFor(equipSlot) == null))
		{
			GD.PushError($"{item.Name}: assign HeldScene and the selected hand's grip before equipping.");
			return new(ItemFailure.InvalidTarget);
		}

		var isTwoHanded = item is Weapon weapon && weapon.IsTwoHanded;
		if (isTwoHanded && equipSlot != EquipmentSlot.WeaponHand)
		{
			GD.PrintErr($"{item.Name} must be equipped in {EquipmentSlot.WeaponHand}");
			return new(ItemFailure.InvalidTarget);
		}

		if (EquippedItems[equipSlot] == itemSlot)
		{
			return ItemOperationResult.Ok;
		}

		var displaced = SlotsOf(itemSlot).Append(equipSlot).ToList();
		if (isTwoHanded)
		{
			displaced.Add(EquipmentSlot.ShieldHand);
		}
		else if (equipSlot == EquipmentSlot.ShieldHand
			&& EquippedItems[EquipmentSlot.WeaponHand]?.Item is Weapon equippedWeapon
			&& equippedWeapon.IsTwoHanded)
		{
			displaced.Add(EquipmentSlot.WeaponHand);
		}

		ItemOperationResult vacate = CanVacate(displaced);
		if (!vacate.Success)
		{
			return vacate;
		}

		using var update = DeferChanges();
		foreach (EquipmentSlot slot in displaced)
		{
			Release(slot);
		}

		Place(itemSlot, equipSlot);
		return ItemOperationResult.Ok;
	}

	/// <summary>Takes an entry off wherever it is worn; fails without change when it is cursed.</summary>
	public ItemOperationResult Unequip(InventoryItemSlot itemSlot)
	{
		ItemOperationResult vacate = CanVacate(SlotsOf(itemSlot));
		if (vacate.Success)
		{
			ReleaseEntry(itemSlot);
		}

		return vacate;
	}

	/// <summary>Empties an equipment slot; fails without change when its occupant is cursed.</summary>
	public ItemOperationResult Unequip(EquipmentSlot equipSlot)
	{
		ItemOperationResult vacate = CanVacate([equipSlot]);
		if (vacate.Success)
		{
			Release(equipSlot);
		}

		return vacate;
	}

	/// <summary>The equipment slots <paramref name="entry"/> is worn in.</summary>
	private System.Collections.Generic.IEnumerable<EquipmentSlot> SlotsOf(InventoryItemSlot entry) =>
		EquippedItems.Where(pair => pair.Value == entry).Select(pair => pair.Key);

	/// <summary>The sticky-curse rule: emptying these slots fails, naming the first cursed occupant, or succeeds.</summary>
	private ItemOperationResult CanVacate(System.Collections.Generic.IEnumerable<EquipmentSlot> slots) =>
		slots.Select(slot => EquippedItems[slot]?.Item).OfType<EquipableItem>().FirstOrDefault(item => item.IsCursed) is { } cursed
			? new(ItemFailure.Cursed, cursed) : ItemOperationResult.Ok;

	/// <summary>
	/// Changes an owned equipment item in place, curses aside, as Enchant and Remove Curse do. The item keeps its
	/// instance and its slots; when worn, <see cref="EquippedItemChanged"/> lets listeners re-register its stats. Item
	/// views relabel from the item's own <c>Changed</c>.
	/// </summary>
	public void ChangeItem(InventoryItemSlot entry, Action<EquipableItem> change)
	{
		if (entry?.Item is not EquipableItem { IsRuntimeInstance: true } item || !Items.Contains(entry)) { return; }
		change(item);
		if (IsEquipped(entry))
		{
			EmitSignalEquippedItemChanged(item);
		}
	}

	/// <summary>Empties every equipment slot, curses aside. Only for replacing the whole state on restore.</summary>
	internal void ReleaseAll()
	{
		using var update = DeferChanges();
		foreach (EquipmentSlot slot in EquippedItems.Keys.ToArray())
		{
			Release(slot);
		}
	}

	private void ReleaseEntry(InventoryItemSlot entry)
	{
		foreach (EquipmentSlot slot in SlotsOf(entry).ToArray())
		{
			Release(slot);
		}
	}

	private void Place(InventoryItemSlot entry, EquipmentSlot slot)
	{
		EquippedItems[slot] = entry;
		EmitSignalItemEquipped((EquipableItem)entry.Item, slot);
		NotifyChanged();
	}

	private void Release(EquipmentSlot equipSlot)
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
		NotifyChanged();
	}

	internal void NotifyConsumed(ConsumableItem item) => EmitSignalItemConsumed(item);
	internal void NotifyDropped(Item item, int quantity) => EmitSignalItemDropped(item, quantity);

	public void DestroyItem(InventoryItemSlot entry)
	{
		if (entry == null || entry.Item?.IsQuestItem != false || !CanRemove(entry, entry.Quantity).Success) { return; }
		using var update = DeferChanges();
		int count = entry.Quantity;
		RemoveItem(entry, count);
		EmitSignalItemDestroyed(entry.Item, count);
	}

	public void SplitItem(InventoryItemSlot entry, int quantity) => TrySplit(entry, quantity);
}