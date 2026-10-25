using Godot;
using System.Collections.Generic;
using System.Linq;

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

	private readonly HashSet<InventoryItemSlot> _reservedSlots = new();
	private ulong _itemUseGeneration = 0;

	public bool IsItemUseChannelActive => _reservedSlots.Count > 0;

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
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.EquippedItemChanged += OnEquippedItemChanged,
			inventory => inventory.EquippedItemChanged -= OnEquippedItemChanged);
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
		RefreshEffectiveGuard();
	}

	public void RefreshEffectiveGuard()
	{
		if (Player?.GuardComponent == null) return;

		GuardDefinition effectiveGuard = null;
		if (Inventory?.EquippedItems != null)
		{
			Inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var weaponSlot);
			Inventory.EquippedItems.TryGetValue(EquipmentSlot.ShieldHand, out var shieldSlot);

			Weapon weapon = weaponSlot?.Item as Weapon;
			EquipableItem shield = shieldSlot?.Item as EquipableItem;

			bool isTwoHanded = weapon != null && weapon.IsTwoHanded;

			if (!isTwoHanded && shield?.Guard != null)
			{
				effectiveGuard = shield.Guard;
			}
			else if (weapon?.Guard != null)
			{
				effectiveGuard = weapon.Guard;
			}
			else if (weapon != null && weapon is not RangedWeapon)
			{
				effectiveGuard = Player?.Capabilities?.InnateGuard;
			}
			else if (weapon is RangedWeapon && shield == null)
			{
				// Ranged weapons without a shield cannot guard/parry
				Player.GuardComponent.ApplyGuardDefinition(null);
				return;
			}
		}

		effectiveGuard ??= Player?.Capabilities?.InnateGuard;
		Player.GuardComponent.ApplyGuardDefinition(effectiveGuard);
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
			ItemFailure.Cursed => "You can't remove it. It is cursed.",
			_ => "That item is no longer available for this action.",
		};
		return new(failure);
	}

	/// <summary>
	/// Reports an operation's result. A cursed item blocking the player's own action teaches them it is cursed; checks
	/// inside <see cref="Inventory"/> reveal nothing by themselves.
	/// </summary>
	private ItemOperationResult Report(ItemOperationResult result)
	{
		if (result.Failure == ItemFailure.Cursed)
		{
			(result.Subject as EquipableItem)?.RevealCurseStatus();
		}

		return Report(result.Failure);
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
			Player?.QuickItemController?.OnItemAcquired(item, quantity);
			return Report(ItemFailure.None);
		}
		finally { IsCommittingItemOperation = false; }
	}

	public ItemOperationResult TryBeginItemUse(InventoryItemSlot source, InventoryItemSlot target = null)
	{
		if (IsCommittingItemOperation || _reservedSlots.Contains(source)) { return Report(ItemFailure.Busy); }
		if (!Inventory.CanRemove(source, 1).Success || source.Item is not ConsumableItem consumable)
		{ return Report(ItemFailure.NotOwned); }
		Scroll scroll = consumable as Scroll;
		if (scroll?.RequiresTarget == true && (target == source || !Inventory.Items.Contains(target)
			|| !scroll.Effect.IsValidTarget(Player, target))) { return Report(ItemFailure.InvalidTarget); }

		if (Player?.QuickItemController?.IsVarietyOnCooldown(consumable.DefinitionPath) == true)
		{
			return Report(ItemFailure.Busy);
		}

		ActorActionLease lease = ActorActionLease.None;
		if (Player?.ActionGate != null)
		{
			var gateResult = Player.ActionGate.TryAcquire(
				ActorActionKind.ItemUse,
				ActorActionPhase.Channeling,
				reason => CancelItemUse(reason),
				out lease);
			if (gateResult != ActorActionRequestResult.Success)
			{
				return Report(ItemFailure.Busy);
			}
		}

		_reservedSlots.Add(source);
		if (consumable.CooldownDuration > 0)
		{
			Player?.QuickItemController?.StartVarietyCooldown(consumable.DefinitionPath, consumable.CooldownDuration);
		}

		ulong generation = ++_itemUseGeneration;
		double duration = consumable.Delay + consumable.PerformDuration;

		if (duration <= 0)
		{
			_reservedSlots.Remove(source);
			if (Player?.ActionGate != null) Player.ActionGate.Release(lease);
			return TryCommitItemUse(source, consumable, target);
		}

		ExecuteItemUseChannel(generation, source, consumable, target, lease, duration);
		return Report(ItemFailure.None);
	}

	private async void ExecuteItemUseChannel(
		ulong generation,
		InventoryItemSlot source,
		ConsumableItem consumable,
		InventoryItemSlot target,
		ActorActionLease lease,
		double duration)
	{
		await ToSignal(GetTree().CreateTimer(duration, false, true), SceneTreeTimer.SignalName.Timeout);

		if (generation != _itemUseGeneration || Player == null || Player.IsDead)
		{
			_reservedSlots.Remove(source);
			if (Player?.ActionGate != null) Player.ActionGate.Release(lease);
			return;
		}

		_reservedSlots.Remove(source);
		if (Player?.ActionGate != null) Player.ActionGate.Release(lease);

		if (Inventory.CanRemove(source, 1).Success)
		{
			TryCommitItemUse(source, consumable, target);
		}
	}

	public void CancelItemUse(ActionInterruptReason reason)
	{
		_itemUseGeneration++;
		_reservedSlots.Clear();
	}

	private ItemOperationResult TryCommitItemUse(InventoryItemSlot source, ConsumableItem consumable, InventoryItemSlot target)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		Scroll scroll = consumable as Scroll;
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

	public ItemOperationResult TryUse(InventoryItemSlot source, InventoryItemSlot target = null)
	{
		return TryBeginItemUse(source, target);
	}

	public ItemOperationResult TryDrop(InventoryItemSlot source, int quantity)
	{
		if (IsCommittingItemOperation || _reservedSlots.Contains(source)) { return Report(ItemFailure.Busy); }
		ItemOperationResult removable = Inventory.CanRemove(source, quantity);
		if (removable.Failure == ItemFailure.Cursed) { return Report(removable); }
		if (!removable.Success) { return Report(ItemFailure.NotOwned); }
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
	public ItemOperationResult TryEquip(InventoryItemSlot source, EquipmentSlot? targetSlot = null)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (!Inventory.Items.Contains(source) || source.Item is not EquipableItem) { return Report(ItemFailure.NotOwned); }
		IsCommittingItemOperation = true;
		try { using var update = Inventory.DeferChanges(); return Report(targetSlot.HasValue
			? Inventory.Equip(source, targetSlot.Value) : Inventory.Equip(source)); }
		finally { IsCommittingItemOperation = false; }
	}

	public ItemOperationResult TryUnequip(InventoryItemSlot source)
	{
		if (IsCommittingItemOperation) { return Report(ItemFailure.Busy); }
		if (!Inventory.Items.Contains(source)) { return Report(ItemFailure.NotOwned); }
		IsCommittingItemOperation = true;
		try { using var update = Inventory.DeferChanges(); return Report(Inventory.Unequip(source)); }
		finally { IsCommittingItemOperation = false; }
	}

	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		if (slot is EquipmentSlot.WeaponHand or EquipmentSlot.ShieldHand)
			Player.GuardComponent?.Cancel(ActionInterruptReason.EquipChanged);
		item.OnEquipped(Player);
		StatsController.SyncStats();
		SignalBus.EmitItemEquipped(Player, item);

		if (item is Weapon weapon && slot == EquipmentSlot.WeaponHand)
		{
			ActionManager.AssignPrimary(weapon, weapon.Scene);
			ActionManager.AssignSecondary(new MartialTechniqueAction(weapon), weapon.Scene);
		}
		RefreshEffectiveGuard();
	}

	/// <summary>A worn item changed in place (enchanted, uncursed): only its stats need re-registering.</summary>
	private void OnEquippedItemChanged(EquipableItem item)
	{
		item.RefreshStats(Player);
		StatsController.SyncStats();
	}

	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		if (slot is EquipmentSlot.WeaponHand or EquipmentSlot.ShieldHand)
			Player.GuardComponent?.Cancel(ActionInterruptReason.EquipChanged);
		item.OnUnequipped(Player);
		StatsController.SyncStats();
		SignalBus.EmitItemUnequipped(Player, item);

		if (item is Weapon && slot == EquipmentSlot.WeaponHand)
		{
			ActionManager.AssignPrimary(UnarmedAttackAction.Instance, null);
			ActionManager.AssignSecondary(null, null);
		}
		RefreshEffectiveGuard();
	}

	private void OnItemDestroyed(Item item, int quantity)
	{
		item.OnDestroyed(Player, quantity);
		SignalBus.EmitItemDestroyed(Player, item, quantity);
	}
}