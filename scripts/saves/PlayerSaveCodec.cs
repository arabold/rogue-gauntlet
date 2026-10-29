using System;
using System.Linq;
using Godot;

/// <summary>
/// Captures and restores a live player: stats, inventory, quick slots and combat timers, and optionally its transform.
/// <see cref="Apply"/> takes a record the save service already validated and preflights it again, so a mismatch throws
/// before the player changes.
/// </summary>
public static class PlayerSaveCodec
{
	/// <param name="includeTransform">False for level travel, where the next floor's arrival places the player.</param>
	public static PlayerSaveData Capture(Player player, bool includeTransform)
	{
		// A committed teleport must not be lost if saving midway through its presentation.
		player.WorldEffects?.CompleteSurfaceTransitions();
		var data = new PlayerSaveData
		{
			ApplyTransform = includeTransform,
			Stats = player.Stats.CreateSaveData(),
			Inventory = InventorySaveCodec.Capture(player.Inventory),
			Actions = CaptureActions(player),
		};
		if (includeTransform)
		{
			data.Position = CaptureVector3(player.GlobalPosition);
			data.Rotation = CaptureVector3(player.Rotation);
		}

		return data;
	}

	public static void Apply(Player player, PlayerSaveData data)
	{
		if (data?.Stats == null || data.ApplyTransform && (data.Position == null || data.Rotation == null))
		{
			throw new InvalidOperationException("Incomplete saved player; a new run is required.");
		}
		Inventory restored = InventorySaveCodec.Restore(data.Inventory);
		data.Actions?.ValidateCombatTimers();

		player.BuffController.ClearBuffs();
		ApplyInventory(player, restored, data.Inventory);
		ApplyActions(player, data.Actions);
		player.Stats.ApplySaveData(data.Stats);
		player.StatsController.SyncStats();
		if (data.ApplyTransform)
		{
			player.GlobalPosition = ToVector3(data.Position);
			player.Rotation = ToVector3(data.Rotation);
			player.ResetPhysicsInterpolation();
		}
	}

	internal static PlayerActionSaveData CaptureActions(Player player)
	{
		var data = new PlayerActionSaveData();
		if (player.QuickItemController != null)
		{
			for (int i = 0; i < PlayerQuickItemController.SlotCount; i++)
			{
				var binding = player.QuickItemController.GetBinding(i);
				data.QuickSlots.Add(new QuickItemBindingSaveData
				{
					SlotIndex = i,
					Kind = binding.Kind.ToString(),
					DefinitionPath = binding.DefinitionPath,
					InstanceId = binding.InstanceId
				});
			}

			foreach (var kvp in player.QuickItemController.GetVarietyCooldowns())
			{
				data.VarietyCooldowns[kvp.Key] = kvp.Value;
			}
		}

		data.DodgeChargesRemaining = player.DodgeComponent?.ChargesRemaining ?? 0;
		data.DodgeRechargeRemaining = player.DodgeComponent?.RechargeRemaining ?? 0f;
		data.DodgeIntervalRemaining = player.DodgeComponent?.IntervalRemaining ?? 0f;
		data.ParryRearmRemaining = player.GuardComponent?.ParryRearmRemaining ?? 0f;
		data.RecoveryLockoutRemaining = Mathf.Max(player.ActionGate?.StaggerRemaining ?? 0,
			Mathf.Max(player.AttackController?.RecoveryRemaining ?? 0,
			Mathf.Max(player.DodgeComponent?.RecoveryRemaining ?? 0, player.GuardComponent?.RecoveryRemaining ?? 0)));

		return data;
	}

	internal static void ApplyActions(Player player, PlayerActionSaveData data)
	{
		if (player.QuickItemController == null || data == null) return;
		data.ValidateCombatTimers();
		player.QuickItemController.IsRestoringSave = true;
		try
		{
			if (data.QuickSlots != null)
			{
				foreach (var slotData in data.QuickSlots)
				{
					if (slotData.SlotIndex < 0 || slotData.SlotIndex >= PlayerQuickItemController.SlotCount) continue;
					if (Enum.TryParse<QuickItemBindingKind>(slotData.Kind, out var kind))
					{
						switch (kind)
						{
							case QuickItemBindingKind.Variety:
								player.QuickItemController.RestoreSlot(slotData.SlotIndex, QuickItemBinding.ForVariety(slotData.DefinitionPath));
								break;
							case QuickItemBindingKind.Instance:
								player.QuickItemController.RestoreSlot(slotData.SlotIndex, QuickItemBinding.ForInstance(slotData.InstanceId, slotData.DefinitionPath));
								break;
							default:
								player.QuickItemController.RestoreSlot(slotData.SlotIndex, QuickItemBinding.Unassigned());
								break;
						}
					}
				}
			}

			if (data.VarietyCooldowns != null)
			{
				player.QuickItemController.RestoreVarietyCooldowns(data.VarietyCooldowns);
			}

			player.QuickItemController.RefreshCounts();
		}
		finally
		{
			player.QuickItemController.IsRestoringSave = false;
		}

		player.ActionGate?.CancelCurrent(ActionInterruptReason.Invalidated);
		player.AttackController?.CancelAttack();
		player.MovementComponent?.ResetTransientMotion();
		player.DodgeComponent?.RestoreCharges(data.DodgeChargesRemaining, data.DodgeRechargeRemaining, data.DodgeIntervalRemaining);
		player.GuardComponent?.RestoreCooldown(data.ParryRearmRemaining);
		player.InputController?.RequireFreshDefenseInput();
		if (data.RecoveryLockoutRemaining > 0) player.ActionGate?.TryStagger(data.RecoveryLockoutRemaining);
	}

	private static void ApplyInventory(Player player, Inventory restored, InventorySaveData saveData)
	{
		Inventory inventory = player.Inventory;
		player.ActionManager.ClearActions();
		inventory.ReleaseAll();
		inventory.Items.Clear();
		inventory.Capacity = restored.Capacity;
		foreach (InventoryItemSlot entry in restored.Items) { inventory.Items.Add(entry); }
		foreach (EquippedItemSaveData equipment in saveData.EquippedItems)
		{
			var entry = inventory.Items.First(i => i.Item.InstanceId == equipment.ItemInstanceId);
			inventory.Equip(entry, Enum.Parse<EquipmentSlot>(equipment.Slot));
		}
		inventory.EmitChanged();
	}

	private static Vector3SaveData CaptureVector3(Vector3 vector) => new() { X = vector.X, Y = vector.Y, Z = vector.Z };

	private static Vector3 ToVector3(Vector3SaveData vector) => new(vector.X, vector.Y, vector.Z);
}