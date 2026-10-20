using Godot;

/// <summary>
/// Handles inventory side effects for a player actor.
/// </summary>
public partial class PlayerInventoryController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public Inventory Inventory { get; set; }
	[Export] public ActionManager ActionManager { get; set; }
	[Export] public PlayerStatsController StatsController { get; set; }
	[Export] public Level Level { get; set; }
	[Export] public PackedScene LootableItemScene { get; set; }

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		Inventory ??= Player.Inventory;
		ActionManager ??= Player.ActionManager;
		StatsController ??= Player.StatsController;
		Level ??= this.GetAncestorOrNull<Level>();
		LootableItemScene ??= Player.LootableItemScene;

		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemEquipped += OnItemEquipped,
			inventory => inventory.ItemEquipped -= OnItemEquipped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemUnequipped += OnItemUnequipped,
			inventory => inventory.ItemUnequipped -= OnItemUnequipped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemDestroyed += OnItemDestroyed,
			inventory => inventory.ItemDestroyed -= OnItemDestroyed);
	}

	public void AutoEquipItems()
	{
		foreach (var item in Inventory.Items)
		{
			if (item.Item is EquipableItem)
			{
				Inventory.Equip(item);
			}
		}
	}

	public bool IsCommittingItemOperation { get; private set; }
	public string LastOperationMessage { get; private set; } = "";

	private ItemOperationResult Report(ItemFailure failure)
	{
		LastOperationMessage = failure switch
		{
			ItemFailure.None => "",
			ItemFailure.InsufficientSpace => "Backpack full. Make room or combine supplies.",
			ItemFailure.WorldUnavailable => "Cannot drop an item here.",
			ItemFailure.InvalidTarget => "That item cannot be used on this target.",
			_ => "That item is no longer available for this action.",
		};
		return new(failure);
	}

	public bool PickupItem(Item item, int quantity = 1) => Acquire(item, quantity, null).Success;
	public ItemOperationResult TryPickup(LootableItem source) => source == null
		? Report(ItemFailure.NotOwned) : Acquire(source.Item, source.Quantity, source);

	private ItemOperationResult Acquire(Item item, int quantity, LootableItem source)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (item == null || quantity <= 0 || source?.IsCollected == true) { return Report(ItemFailure.InvalidQuantity); }
		ItemOperationResult check = item.ShowInInventory ? Inventory.CanAdd(item, quantity) : ItemOperationResult.Ok;
		if (!check.Success) { return Report(check.Failure); }
		IsCommittingItemOperation = true;
		try
		{
			using var update = Inventory.DeferChanges();
			if (item.ShowInInventory)
			{
				check = Inventory.TryAdd(item, quantity);
				if (!check.Success) { return Report(check.Failure); }
			}
			source?.CommitCollected();
			item.OnPickup(Player, quantity);
			return Report(ItemFailure.None);
		}
		finally { IsCommittingItemOperation = false; }
	}

	public ItemOperationResult TryUse(InventoryItemSlot source, InventoryItemSlot target = null)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (!Inventory.CanRemove(source, 1).Success || source.Item is not ConsumableItem consumable)
		{ return Report(ItemFailure.NotOwned); }
		Scroll scroll = consumable as Scroll;
		if (scroll?.RequiresTarget == true && (target == source || !Inventory.Items.Contains(target)
			|| !scroll.Effect.IsValidTarget(Player, target))) { return Report(ItemFailure.InvalidTarget); }
		IsCommittingItemOperation = true;
		try
		{
			using var update = Inventory.DeferChanges();
			if (scroll?.RequiresTarget == true) { scroll.Effect.ApplyToTarget(Player, target); }
			else { consumable.PerformAction(Player); }
			Inventory.RemoveItem(source, 1);
			Inventory.NotifyConsumed(consumable);
			SignalBus.EmitItemConsumed(Player, consumable);
			return Report(ItemFailure.None);
		}
		finally { IsCommittingItemOperation = false; }
	}

	public ItemOperationResult TryDrop(InventoryItemSlot source, int quantity)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (!Inventory.CanRemove(source, quantity).Success) { return Report(ItemFailure.NotOwned); }
		if (source.Item.IsQuestItem) { return Report(ItemFailure.ProtectedItem); }
		Level ??= this.GetAncestorOrNull<Level>();
		if (Level == null || LootableItemScene == null || source.Item.Scene == null)
		{ return Report(ItemFailure.WorldUnavailable); }
		// Prepare off-tree first: failure cannot remove equipment or inventory contents.
		Node prepared = LootableItemScene.Instantiate();
		if (prepared is not LootableItem pickup) { prepared.Free(); return Report(ItemFailure.WorldUnavailable); }
		Item item = quantity == source.Quantity ? source.Item : ItemFactory.CopyDefinition(source.Item);
		pickup.Item = item;
		pickup.Quantity = quantity;
		pickup.WaitForPlayerExited = true;
		IsCommittingItemOperation = true;
		try
		{
			using var update = Inventory.DeferChanges();
			Inventory.RemoveItem(source, quantity);
			Level.AddWorldNode(pickup, Player.GlobalPosition);
			item.OnDropped(Player, quantity);
			Inventory.NotifyDropped(item, quantity);
			SignalBus.EmitItemDropped(Player, item, quantity);
			return Report(ItemFailure.None);
		}
		finally { IsCommittingItemOperation = false; }
	}

	/// <summary>Equipment callbacks run under the same save/transfer guard as consumables.</summary>
	public ItemOperationResult TryEquip(InventoryItemSlot source)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (!Inventory.Items.Contains(source) || source.Item is not EquipableItem) { return Report(ItemFailure.NotOwned); }
		IsCommittingItemOperation = true;
		try { using var update = Inventory.DeferChanges(); Inventory.Equip(source); return Report(ItemFailure.None); }
		finally { IsCommittingItemOperation = false; }
	}

	public ItemOperationResult TryUnequip(InventoryItemSlot source)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (!Inventory.Items.Contains(source)) { return Report(ItemFailure.NotOwned); }
		IsCommittingItemOperation = true;
		try { using var update = Inventory.DeferChanges(); Inventory.Unequip(source); return Report(ItemFailure.None); }
		finally { IsCommittingItemOperation = false; }
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		item.OnEquipped(Player);
		StatsController.SyncStats();
		SignalBus.EmitItemEquipped(Player, item);

		if (item is IPlayerAction action)
		{
			ActionManager.AssignFirstAvailableAction(action, item.Scene);
		}
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		item.OnUnequipped(Player);
		StatsController.SyncStats();
		SignalBus.EmitItemUnequipped(Player, item);

		if (item is IPlayerAction action)
		{
			ActionManager.ClearAction(action);
		}
	}

	private void OnItemDestroyed(Item item, int quantity)
	{
		item.OnDestroyed(Player, quantity);
		SignalBus.EmitItemDestroyed(Player, item, quantity);
	}
}