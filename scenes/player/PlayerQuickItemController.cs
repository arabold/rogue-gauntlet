using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages the four item-only quick slots, pickup auto-assignment, variety counting, and usage.
/// </summary>
[GlobalClass]
public partial class PlayerQuickItemController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public Inventory Inventory { get; set; }
	[Export] public ActionManager ActionManager { get; set; }
	[Export] public PlayerInventoryController InventoryController { get; set; }

	public const int SlotCount = 4;
	public bool IsRestoringSave { get; set; } = false;

	private readonly QuickItemBinding[] _bindings = new QuickItemBinding[SlotCount];
	private readonly Dictionary<string, float> _varietyCooldowns = new();
	private readonly Dictionary<string, float> _varietyTotalCooldowns = new();
	private readonly List<string> _expiredVarietyKeys = new();
	private readonly List<string> _activeKeys = new();

	public PlayerQuickItemController()
	{
		for (int i = 0; i < SlotCount; i++)
		{
			_bindings[i] = QuickItemBinding.Unassigned();
		}
	}

	public override void _Ready()
	{
		Player ??= GetOwner<Player>() ?? GetParent<Player>();
		if (Player != null)
		{
			Inventory ??= Player.Inventory;
			ActionManager ??= Player.ActionManager;
			InventoryController ??= Player.InventoryController;
		}

		if (Inventory != null)
		{
			this.SubscribeUntilExit(
				Inventory,
				inv => inv.Changed += RefreshCounts,
				inv => inv.Changed -= RefreshCounts);
		}

		RefreshCounts();
	}

	public QuickItemBinding GetBinding(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount) return QuickItemBinding.Unassigned();
		return _bindings[slotIndex].Clone();
	}

	public bool TryAssignVariety(int slotIndex, string definitionPath)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount || string.IsNullOrEmpty(definitionPath)) return false;

		// Reject duplicate variety bindings
		for (int i = 0; i < SlotCount; i++)
		{
			if (i != slotIndex && _bindings[i].Kind == QuickItemBindingKind.Variety && _bindings[i].DefinitionPath == definitionPath)
			{
				return false;
			}
		}

		_bindings[slotIndex] = QuickItemBinding.ForVariety(definitionPath);
		RefreshSlot(slotIndex);
		return true;
	}

	public bool TryAssignInstance(int slotIndex, string instanceId, string definitionPath)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount || string.IsNullOrEmpty(instanceId)) return false;

		_bindings[slotIndex] = QuickItemBinding.ForInstance(instanceId, definitionPath);
		RefreshSlot(slotIndex);
		return true;
	}

	public void ClearSlot(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount) return;
		_bindings[slotIndex] = QuickItemBinding.Unassigned();
		RefreshSlot(slotIndex);
	}

	public void SwapSlots(int fromIndex, int toIndex)
	{
		if (fromIndex < 0 || fromIndex >= SlotCount || toIndex < 0 || toIndex >= SlotCount || fromIndex == toIndex) return;
		var temp = _bindings[fromIndex];
		_bindings[fromIndex] = _bindings[toIndex];
		_bindings[toIndex] = temp;
		RefreshSlot(fromIndex);
		RefreshSlot(toIndex);
	}

	public void RestoreSlot(int slotIndex, QuickItemBinding binding)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount || binding == null) return;
		_bindings[slotIndex] = binding.Clone();
	}

	public int GetSlotCount(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount || Inventory == null) return 0;
		var binding = _bindings[slotIndex];
		if (!binding.IsAssigned) return 0;

		if (binding.Kind == QuickItemBindingKind.Variety)
		{
			int count = 0;
			for (int i = 0; i < Inventory.Items.Count; i++)
			{
				var entry = Inventory.Items[i];
				if (entry?.Item?.DefinitionPath == binding.DefinitionPath)
				{
					count += entry.Quantity;
				}
			}
			return count;
		}
		if (binding.Kind == QuickItemBindingKind.Instance)
		{
			for (int i = 0; i < Inventory.Items.Count; i++)
			{
				var entry = Inventory.Items[i];
				if (entry?.Item?.InstanceId == binding.InstanceId)
				{
					return entry.Quantity;
				}
			}
		}
		return 0;
	}

	public bool IsSlotDepleted(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount) return false;
		return _bindings[slotIndex].IsAssigned && GetSlotCount(slotIndex) == 0;
	}

	public void OnItemAcquired(Item item, int quantity)
	{
		if (IsRestoringSave) return;
		if (item is not ConsumableItem consumable || !consumable.AllowQuickUse) return;

		// Check if variety or instance is already represented
		for (int i = 0; i < SlotCount; i++)
		{
			bool matches = consumable.CanStack
				? (_bindings[i].Kind == QuickItemBindingKind.Variety && _bindings[i].DefinitionPath == consumable.DefinitionPath)
				: (_bindings[i].Kind == QuickItemBindingKind.Instance && _bindings[i].InstanceId == consumable.InstanceId);
			if (matches)
			{
				RefreshSlot(i);
				return;
			}
		}

		// Find lowest unassigned slot (depleted stackable slots remain reserved and are not overwritten)
		for (int i = 0; i < SlotCount; i++)
		{
			if (_bindings[i].Kind == QuickItemBindingKind.Unassigned)
			{
				_bindings[i] = consumable.CanStack
					? QuickItemBinding.ForVariety(consumable.DefinitionPath)
					: QuickItemBinding.ForInstance(consumable.InstanceId, consumable.DefinitionPath);
				RefreshSlot(i);
				return;
			}
		}
	}

	private InventoryItemSlot FindSlot(QuickItemBinding binding)
	{
		if (Inventory == null || !binding.IsAssigned) return null;
		if (binding.Kind == QuickItemBindingKind.Instance)
		{
			for (int i = 0; i < Inventory.Items.Count; i++)
			{
				var entry = Inventory.Items[i];
				if (entry?.Item?.InstanceId == binding.InstanceId) return entry;
			}
		}
		for (int i = 0; i < Inventory.Items.Count; i++)
		{
			var entry = Inventory.Items[i];
			if (entry?.Item?.DefinitionPath == binding.DefinitionPath) return entry;
		}
		return null;
	}

	public ItemOperationResult TryUseSlot(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount)
		{
			return new(ItemFailure.NotOwned);
		}

		var binding = _bindings[slotIndex];
		if (!binding.IsAssigned || GetSlotCount(slotIndex) <= 0)
		{
			return new(ItemFailure.NotOwned);
		}

		if (IsVarietyOnCooldown(binding.DefinitionPath))
		{
			return new(ItemFailure.Busy);
		}

		var targetSlot = FindSlot(binding);
		if (targetSlot == null)
		{
			return new(ItemFailure.NotOwned);
		}

		InventoryController ??= Player?.InventoryController;
		if (InventoryController == null)
		{
			return new(ItemFailure.NotOwned);
		}

		return InventoryController.TryBeginItemUse(targetSlot);
	}

	public bool IsVarietyOnCooldown(string definitionPath)
	{
		if (string.IsNullOrEmpty(definitionPath)) return false;
		return _varietyCooldowns.TryGetValue(definitionPath, out float remaining) && remaining > 0;
	}

	public void StartVarietyCooldown(string definitionPath, float duration)
	{
		if (string.IsNullOrEmpty(definitionPath) || duration <= 0) return;
		_varietyCooldowns[definitionPath] = duration;
		_varietyTotalCooldowns[definitionPath] = duration;
	}

	public IReadOnlyDictionary<string, float> GetVarietyCooldowns() => _varietyCooldowns;

	public void RestoreVarietyCooldowns(Dictionary<string, float> cooldowns)
	{
		_varietyCooldowns.Clear();
		_varietyTotalCooldowns.Clear();
		if (cooldowns == null) return;
		foreach (var kvp in cooldowns)
		{
			if (kvp.Value > 0)
			{
				_varietyCooldowns[kvp.Key] = kvp.Value;
				_varietyTotalCooldowns[kvp.Key] = kvp.Value;
			}
		}
	}

	public void RefreshCounts()
	{
		for (int i = 0; i < SlotCount; i++)
		{
			RefreshSlot(i);
		}
	}

	public void RefreshSlot(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= SlotCount) return;
		ActionManager ??= Player?.ActionManager;

		var binding = _bindings[slotIndex];
		if (!binding.IsAssigned)
		{
			ActionManager?.AssignAction(slotIndex, null, null);
			var slot = ActionManager?.GetActionSlot(slotIndex);
			if (slot != null) slot.SetQuantity(0);
			return;
		}

		Item itemDef = FindSlot(binding)?.Item;
		if (itemDef == null && !string.IsNullOrEmpty(binding.DefinitionPath))
		{
			if (ResourceLoader.Exists(binding.DefinitionPath))
			{
				itemDef = GD.Load<Item>(binding.DefinitionPath);
			}
		}

		int count = GetSlotCount(slotIndex);

		// Non-stackable item instances clear completely on depletion/consumption,
		// freeing the slot for new pickups. Stackable varieties remain as depleted ghost slots.
		if (Inventory != null && (binding.Kind == QuickItemBindingKind.Instance || (itemDef != null && !itemDef.CanStack)) && count <= 0)
		{
			_bindings[slotIndex] = QuickItemBinding.Unassigned();
			ActionManager?.AssignAction(slotIndex, null, null);
			var slot = ActionManager?.GetActionSlot(slotIndex);
			if (slot != null) slot.SetQuantity(0);
			return;
		}

		ActionManager?.AssignAction(slotIndex, itemDef as IPlayerAction, itemDef?.Scene);
		var actionSlot = ActionManager?.GetActionSlot(slotIndex);
		if (actionSlot != null)
		{
			actionSlot.SetQuantity(count);
		}
	}

	public override void _Process(double delta)
	{
		if (_varietyCooldowns.Count == 0) return;

		_expiredVarietyKeys.Clear();
		_activeKeys.Clear();
		foreach (var key in _varietyCooldowns.Keys)
		{
			_activeKeys.Add(key);
		}

		for (int k = 0; k < _activeKeys.Count; k++)
		{
			string key = _activeKeys[k];
			float remaining = _varietyCooldowns[key] - (float)delta;
			if (remaining <= 0)
			{
				_expiredVarietyKeys.Add(key);
			}
			else
			{
				_varietyCooldowns[key] = remaining;
				float total = _varietyTotalCooldowns[key];
				for (int i = 0; i < SlotCount; i++)
				{
					if (_bindings[i].DefinitionPath == key)
					{
						SignalBus.EmitCooldownUpdated(i, remaining, total);
					}
				}
			}
		}

		for (int k = 0; k < _expiredVarietyKeys.Count; k++)
		{
			string key = _expiredVarietyKeys[k];
			_varietyCooldowns.Remove(key);
			_varietyTotalCooldowns.Remove(key);
			for (int i = 0; i < SlotCount; i++)
			{
				if (_bindings[i].DefinitionPath == key)
				{
					SignalBus.EmitCooldownUpdated(i, 0, 0);
				}
			}
		}
	}
}