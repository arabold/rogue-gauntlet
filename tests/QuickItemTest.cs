namespace RogueGauntlet.Tests;

using System.Linq;
using System.Text.Json;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class QuickItemTest
{
	private partial class TestWeapon : Weapon
	{
		public TestWeapon(string name, string attackStyle)
		{
			Name = name;
			AttackStyle = attackStyle;
		}
	}

	[TestCase]
	public void AllFourSlotsCanBeAssignedWhileWeaponActionsRemainIndependent()
	{
		var actionManager = new ActionManager();
		actionManager._Ready();

		var axe = new TestWeapon("Battle Axe", "axe.cleave");
		actionManager.AssignPrimary(axe, null);
		actionManager.AssignSecondary(new MartialTechniqueAction(axe), null);

		AssertInt(actionManager.ActionSlotCount).IsEqual(4);
		AssertObject(actionManager.PrimaryAttackSlot.AssignedAction).IsEqual(axe);
		AssertObject(((MartialTechniqueAction)actionManager.SecondaryTechniqueSlot.AssignedAction).Weapon).IsEqual(axe);

		var potion = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres");
		for (int i = 0; i < 4; i++)
		{
			actionManager.AssignAction(i, potion, null);
			AssertObject(actionManager.GetAction(i)).IsEqual(potion);
		}

		// Primary and secondary are unchanged
		AssertObject(actionManager.PrimaryAttackSlot.AssignedAction).IsEqual(axe);
		AssertObject(((MartialTechniqueAction)actionManager.SecondaryTechniqueSlot.AssignedAction).Weapon).IsEqual(axe);
	}

	[TestCase]
	public void UnarmedFallbackIsAssignedByDefault()
	{
		var actionManager = new ActionManager();
		actionManager._Ready();

		AssertObject(actionManager.PrimaryAttackSlot.AssignedAction).IsEqual(UnarmedAttackAction.Instance);
		AssertObject(actionManager.SecondaryTechniqueSlot.AssignedAction).IsNull();
	}

	[TestCase]
	public void PickupAutoAssignsLowestUnassignedSlot()
	{
		var inventory = new Inventory { Capacity = 10 };
		var quick = new PlayerQuickItemController { Inventory = inventory };

		var potion1 = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres");
		inventory.TryAdd(potion1, 1);
		var carried1 = inventory.Items.First(e => e.Item.DefinitionPath == potion1.DefinitionPath).Item;
		quick.OnItemAcquired(carried1, 1);

		AssertInt((int)quick.GetBinding(0).Kind).IsEqual((int)QuickItemBindingKind.Variety);
		AssertString(quick.GetBinding(0).DefinitionPath).IsEqual(potion1.ResourcePath);
		AssertInt(quick.GetSlotCount(0)).IsEqual(1);
		AssertInt((int)quick.GetBinding(1).Kind).IsEqual((int)QuickItemBindingKind.Unassigned);

		var potion2 = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_strength_small.tres");
		inventory.TryAdd(potion2, 1);
		var carried2 = inventory.Items.First(e => e.Item.DefinitionPath == potion2.DefinitionPath).Item;
		quick.OnItemAcquired(carried2, 1);

		AssertInt((int)quick.GetBinding(1).Kind).IsEqual((int)QuickItemBindingKind.Variety);
		AssertString(quick.GetBinding(1).DefinitionPath).IsEqual(potion2.ResourcePath);
		AssertInt(quick.GetSlotCount(1)).IsEqual(1);
	}

	[TestCase]
	public void StackableItemReplenishesCountWithoutTakingDuplicateSlot()
	{
		var inventory = new Inventory { Capacity = 10 };
		var quick = new PlayerQuickItemController { Inventory = inventory };

		var potion = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres");
		inventory.TryAdd(potion, 1);
		var carried = inventory.Items.First(e => e.Item.DefinitionPath == potion.DefinitionPath).Item;
		quick.OnItemAcquired(carried, 1);

		AssertInt(quick.GetSlotCount(0)).IsEqual(1);

		// Add more of same stackable item
		inventory.TryAdd(potion, 3);
		quick.OnItemAcquired(carried, 3);

		AssertInt(quick.GetSlotCount(0)).IsEqual(4);
		AssertInt((int)quick.GetBinding(1).Kind).IsEqual((int)QuickItemBindingKind.Unassigned);
	}

	[TestCase]
	public void FullQuickBarDoesNotOverwriteAndLeavesBindingsIntact()
	{
		var inventory = new Inventory { Capacity = 10 };
		var quick = new PlayerQuickItemController { Inventory = inventory };

		string[] paths = new[]
		{
			"res://scenes/items/potions/potion_healing_small.tres",
			"res://scenes/items/potions/potion_healing_medium.tres",
			"res://scenes/items/potions/potion_healing_large.tres",
			"res://scenes/items/potions/potion_strength_small.tres"
		};

		for (int i = 0; i < 4; i++)
		{
			var p = TestFactories.Load<ConsumableItem>(paths[i]);
			inventory.TryAdd(p, 1);
			var carried = inventory.Items.First(e => e.Item.DefinitionPath == p.ResourcePath).Item;
			quick.OnItemAcquired(carried, 1);
		}

		// All 4 slots are assigned
		for (int i = 0; i < 4; i++)
		{
			AssertBool(quick.GetBinding(i).IsAssigned).IsTrue();
		}

		// 5th item
		var extra = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_dexterity_small.tres");
		inventory.TryAdd(extra, 1);
		var carriedExtra = inventory.Items.First(e => e.Item.DefinitionPath == extra.ResourcePath).Item;
		quick.OnItemAcquired(carriedExtra, 1);

		// Slot 0 is NOT overwritten
		AssertString(quick.GetBinding(0).DefinitionPath).IsEqual(paths[0]);
	}

	[TestCase]
	public void DepletedBindingRemainsReservedAndIsNotOverwrittenByNewItem()
	{
		var inventory = new Inventory { Capacity = 10 };
		var quick = new PlayerQuickItemController { Inventory = inventory };

		var potion = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres");
		inventory.TryAdd(potion, 1);
		var carried = inventory.Items.First(e => e.Item.DefinitionPath == potion.DefinitionPath).Item;
		quick.OnItemAcquired(carried, 1);

		// Consume/remove the potion so count reaches 0
		inventory.RemoveItem(inventory.Items[0], 1);
		AssertInt(quick.GetSlotCount(0)).IsEqual(0);
		AssertBool(quick.IsSlotDepleted(0)).IsTrue();
		AssertBool(quick.GetBinding(0).IsAssigned).IsTrue();

		// Pickup a new item; it must NOT overwrite slot 0, but instead go to slot 1
		var manaPotion = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_strength_small.tres");
		inventory.TryAdd(manaPotion, 1);
		var carriedMana = inventory.Items.First(e => e.Item.DefinitionPath == manaPotion.DefinitionPath).Item;
		quick.OnItemAcquired(carriedMana, 1);

		AssertString(quick.GetBinding(0).DefinitionPath).IsEqual(potion.ResourcePath);
		AssertInt(quick.GetSlotCount(0)).IsEqual(0);
		AssertString(quick.GetBinding(1).DefinitionPath).IsEqual(manaPotion.ResourcePath);
		AssertInt(quick.GetSlotCount(1)).IsEqual(1);

		// Replenishing depleted variety restores count in slot 0
		inventory.TryAdd(potion, 2);
		quick.OnItemAcquired(carried, 2);
		AssertInt(quick.GetSlotCount(0)).IsEqual(2);
		AssertBool(quick.IsSlotDepleted(0)).IsFalse();
	}

	[TestCase]
	public void NonStackableFoodBindsInstanceId()
	{
		var inventory = new Inventory { Capacity = 10 };
		var quick = new PlayerQuickItemController { Inventory = inventory };

		var appleDef = TestFactories.Load<Item>("res://scenes/items/food/food/food_apple_red.tres");
		var apple1 = ItemFactory.Create(appleDef, instanceId: "instance_apple_1");
		inventory.TryAdd(apple1, 1);
		quick.OnItemAcquired(apple1, 1);

		AssertInt((int)quick.GetBinding(0).Kind).IsEqual((int)QuickItemBindingKind.Instance);
		AssertString(quick.GetBinding(0).InstanceId).IsEqual(apple1.InstanceId);
		AssertInt(quick.GetSlotCount(0)).IsEqual(1);

		// A second apple binds slot 1 as its own instance
		var apple2 = ItemFactory.Create(appleDef, instanceId: "instance_apple_2");
		inventory.TryAdd(apple2, 1);
		quick.OnItemAcquired(apple2, 1);

		AssertInt((int)quick.GetBinding(1).Kind).IsEqual((int)QuickItemBindingKind.Instance);
		AssertString(quick.GetBinding(1).InstanceId).IsEqual(apple2.InstanceId);
		AssertBool(quick.GetBinding(0).InstanceId != quick.GetBinding(1).InstanceId).IsTrue();

		// Removing apple1 depletes slot 0 even though apple2 remains in inventory
		inventory.RemoveItem(inventory.Items.First(e => e.Item.InstanceId == apple1.InstanceId), 1);
		AssertInt(quick.GetSlotCount(0)).IsEqual(0);
		AssertInt(quick.GetSlotCount(1)).IsEqual(1);

		// On refresh, non-stackable instance clears to Unassigned so the slot can be reused
		quick.RefreshSlot(0);
		AssertInt((int)quick.GetBinding(0).Kind).IsEqual((int)QuickItemBindingKind.Unassigned);

		// A newly acquired item claims slot 0
		var apple3 = ItemFactory.Create(appleDef, instanceId: "instance_apple_3");
		inventory.TryAdd(apple3, 1);
		quick.OnItemAcquired(apple3, 1);
		AssertInt((int)quick.GetBinding(0).Kind).IsEqual((int)QuickItemBindingKind.Instance);
		AssertString(quick.GetBinding(0).InstanceId).IsEqual(apple3.InstanceId);
	}

	[TestCase]
	public void StackableItemsDisplayCountIncludingZeroAndOne()
	{
		var potion = TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres");
		var food = TestFactories.Load<Item>("res://scenes/items/food/food/food_apple_red.tres");

		AssertBool(potion.CanStack).IsTrue();
		AssertBool(food.CanStack).IsFalse();

		var scene = TestFactories.Load<PackedScene>("res://scenes/ui/action_container.tscn");
		var container = scene.Instantiate<ActionContainer>();
		container.Slot = 0;
		TestFactories.AddToSceneTree(container);
		var label = container.GetNode<Label>("%QuantityLabel");

		var actionSlot = new ActionSlot();
		actionSlot.AssignAction(0, potion, null);
		actionSlot.SetQuantity(0);

		SignalBus.EmitPlayerActionSlotChanged(0, actionSlot);
		AssertString(label.Text).IsEqual("0");

		actionSlot.SetQuantity(1);
		SignalBus.EmitPlayerActionSlotChanged(0, actionSlot);
		AssertString(label.Text).IsEqual("1");

		actionSlot.SetQuantity(5);
		SignalBus.EmitPlayerActionSlotChanged(0, actionSlot);
		AssertString(label.Text).IsEqual("5");

		// Non-stackable food never displays count
		actionSlot.AssignAction(0, food as IPlayerAction, null);
		actionSlot.SetQuantity(1);
		SignalBus.EmitPlayerActionSlotChanged(0, actionSlot);
		AssertString(label.Text).IsEqual("");

		// Check inventory item slot panel displays 1 for stackables and empty for non-stackables
		var itemSlotPanelScene = TestFactories.Load<PackedScene>("res://scenes/ui/item_slot_panel.tscn");
		var panel = TestFactories.AddToSceneTree(itemSlotPanelScene.Instantiate<ItemSlotPanel>());
		var panelQtyLabel = panel.GetNode<Label>("%QuantityLabel");

		var inventorySlot = new InventoryItemSlot { Item = potion, Quantity = 1 };
		panel.SetItem(inventorySlot, false);
		AssertString(panelQtyLabel.Text).IsEqual("1");

		inventorySlot.Quantity = 4;
		AssertString(panelQtyLabel.Text).IsEqual("4");

		var foodSlot = new InventoryItemSlot { Item = food, Quantity = 1 };
		panel.SetItem(foodSlot, false);
		AssertString(panelQtyLabel.Text).IsEqual("");
	}

	[TestCase]
	public void ManualSlotOperationsAndDuplicateRejection()
	{
		var quick = new PlayerQuickItemController();

		AssertBool(quick.TryAssignVariety(0, "res://scenes/items/potions/potion_healing_small.tres")).IsTrue();
		// Reassigning same variety to slot 1 is rejected
		AssertBool(quick.TryAssignVariety(1, "res://scenes/items/potions/potion_healing_small.tres")).IsFalse();
		AssertBool(quick.GetBinding(1).IsAssigned).IsFalse();

		// Swap slots
		quick.SwapSlots(0, 2);
		AssertBool(quick.GetBinding(0).IsAssigned).IsFalse();
		AssertString(quick.GetBinding(2).DefinitionPath).IsEqual("res://scenes/items/potions/potion_healing_small.tres");

		// Clear slot
		quick.ClearSlot(2);
		AssertBool(quick.GetBinding(2).IsAssigned).IsFalse();
	}

	[TestCase]
	public void VarietyCooldownPersistsAcrossSlotChanges()
	{
		var quick = new PlayerQuickItemController();
		quick.TryAssignVariety(0, "res://scenes/items/potions/potion_healing_small.tres");

		quick.StartVarietyCooldown("res://scenes/items/potions/potion_healing_small.tres", 5.0f);
		AssertBool(quick.IsVarietyOnCooldown("res://scenes/items/potions/potion_healing_small.tres")).IsTrue();

		// Move or clear slot: cooldown remains running on the variety
		quick.ClearSlot(0);
		AssertBool(quick.IsVarietyOnCooldown("res://scenes/items/potions/potion_healing_small.tres")).IsTrue();

		// Reassigning to another slot still sees running cooldown
		quick.TryAssignVariety(3, "res://scenes/items/potions/potion_healing_small.tres");
		AssertBool(quick.IsVarietyOnCooldown("res://scenes/items/potions/potion_healing_small.tres")).IsTrue();
	}

	[TestCase]
	public void SaveRestoreRoundTripPreservesBindingsAndCooldowns()
	{
		var quick = new PlayerQuickItemController();
		quick.TryAssignVariety(0, "res://scenes/items/potions/potion_healing_small.tres");
		quick.TryAssignInstance(1, "instance_123", "res://scenes/items/food/food/food_apple_red.tres");
		quick.StartVarietyCooldown("res://scenes/items/potions/potion_healing_small.tres", 3.5f);

		var save = new PlayerActionSaveData();
		for (int i = 0; i < 4; i++)
		{
			var b = quick.GetBinding(i);
			save.QuickSlots.Add(new QuickItemBindingSaveData
			{
				SlotIndex = i,
				Kind = b.Kind.ToString(),
				DefinitionPath = b.DefinitionPath,
				InstanceId = b.InstanceId
			});
		}
		foreach (var kvp in quick.GetVarietyCooldowns())
		{
			save.VarietyCooldowns[kvp.Key] = kvp.Value;
		}

		// Serialize to JSON and back
		string json = JsonSerializer.Serialize(save);
		var restoredSave = JsonSerializer.Deserialize<PlayerActionSaveData>(json);

		var restoredQuick = new PlayerQuickItemController();
		restoredQuick.IsRestoringSave = true;
		foreach (var s in restoredSave.QuickSlots)
		{
			if (s.Kind == "Variety") restoredQuick.RestoreSlot(s.SlotIndex, QuickItemBinding.ForVariety(s.DefinitionPath));
			else if (s.Kind == "Instance") restoredQuick.RestoreSlot(s.SlotIndex, QuickItemBinding.ForInstance(s.InstanceId, s.DefinitionPath));
		}
		restoredQuick.RestoreVarietyCooldowns(restoredSave.VarietyCooldowns);
		restoredQuick.IsRestoringSave = false;

		AssertInt((int)restoredQuick.GetBinding(0).Kind).IsEqual((int)QuickItemBindingKind.Variety);
		AssertString(restoredQuick.GetBinding(0).DefinitionPath).IsEqual("res://scenes/items/potions/potion_healing_small.tres");
		AssertInt((int)restoredQuick.GetBinding(1).Kind).IsEqual((int)QuickItemBindingKind.Instance);
		AssertString(restoredQuick.GetBinding(1).InstanceId).IsEqual("instance_123");
		AssertInt((int)restoredQuick.GetBinding(2).Kind).IsEqual((int)QuickItemBindingKind.Unassigned);
		AssertBool(restoredQuick.IsVarietyOnCooldown("res://scenes/items/potions/potion_healing_small.tres")).IsTrue();
	}

	private class TestAttackAction : ICombatAttackAction
	{
		public float Delay => 0f;
		public float PerformDuration => 0.2f;
		public float CooldownDuration => 0.5f;
		public string CooldownKey => "test_key";
		public bool TryPrepare(Player player, out PreparedAttack attack)
		{
			attack = new PreparedAttack(null, null, 0.2);
			return true;
		}
		public bool StartPrepared(Player player, PreparedAttack attack, ActorActionLease lease = default) => true;
		public void PerformAction(Player player) { }
	}

	[TestCase]
	public void PrimaryAndSecondaryAttacksNeverCoolDownQuickSlots()
	{
		var player = new Player();
		var gate = new ActorActionGateComponent();
		player.AddChild(gate);
		var actionManager = new ActionManager();
		player.AddChild(actionManager);
		actionManager._Ready();

		AssertInt(actionManager.PrimaryAttackSlot.SlotIndex).IsEqual(ActionManager.PrimarySlotIndex);
		AssertInt(actionManager.SecondaryTechniqueSlot.SlotIndex).IsEqual(ActionManager.SecondarySlotIndex);

		var cooledDownSlots = new System.Collections.Generic.List<int>();
		void OnCooldown(int slot, float remaining, float total)
		{
			cooledDownSlots.Add(slot);
		}
		SignalBus.Instance.CooldownUpdated += OnCooldown;

		var attack = new TestAttackAction();
		actionManager.AssignPrimary(attack, null);
		actionManager.TryPerformPrimary();

		var technique = new TestAttackAction();
		actionManager.AssignSecondary(technique, null);
		actionManager.TryPerformSecondary();

		SignalBus.Instance.CooldownUpdated -= OnCooldown;

		// Cooldown updates must ONLY be emitted for PrimarySlotIndex (-1) and SecondarySlotIndex (-2), NEVER for quick slots 0..3
		AssertBool(cooledDownSlots.Contains(ActionManager.PrimarySlotIndex)).IsTrue();
		AssertBool(cooledDownSlots.Contains(ActionManager.SecondarySlotIndex)).IsTrue();
		AssertBool(cooledDownSlots.Contains(0)).IsFalse();
		AssertBool(cooledDownSlots.Contains(1)).IsFalse();
		AssertBool(cooledDownSlots.Contains(2)).IsFalse();
		AssertBool(cooledDownSlots.Contains(3)).IsFalse();

		player.Free();
	}
}