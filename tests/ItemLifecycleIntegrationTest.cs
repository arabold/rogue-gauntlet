namespace RogueGauntlet.Tests;

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Exercises production scene transfers and floor reconstruction through the real player controller.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ItemLifecycleIntegrationTest
{
	[TestCase]
	public async Task PickupUseDropAndFloorReconstructionConserveItems()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(10);
		var level = runner.Scene().GetNode<Level>("Level");
		Player player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();
		var inventory = player.Inventory;
		var controller = player.InventoryController;
		var initial = level.ItemPersistence.Capture();
		AssertInt(initial.AddedItems.Count + initial.RemovedItemIds.Count + initial.Overrides.Count).IsEqual(0);

		// Failed pickup leaves its world object and quantity intact; a later retry transfers it once.
		var item = ItemFactory.Create(GD.Load<Item>("res://scenes/items/potions/healing_potion_small.tres"));
		var pickup = GD.Load<PackedScene>("res://scenes/items/lootable_item.tscn").Instantiate<LootableItem>();
		pickup.Item = item; pickup.Quantity = 2; pickup.WaitForPlayerExited = true;
		level.AddWorldNode(pickup, player.GlobalPosition + new Vector3(20, 0, 0));
		inventory.Capacity = inventory.Items.Count;
		// Starting supplies may have a compatible stack: fill it before capacity validation.
		foreach (var entry in inventory.Items.Where(e => e.Item.DefinitionPath == item.DefinitionPath)) { entry.Quantity = entry.Item.MaxStackSize; }
		AssertBool(controller.TryPickup(pickup).Success).IsFalse();
		AssertBool(pickup.IsCollected).IsFalse();
		inventory.Capacity = 20;
		AssertBool(controller.TryPickup(pickup).Success).IsTrue();
		AssertBool(controller.TryPickup(pickup).Success).IsFalse();
		var carried = inventory.Items.First(e => e.Item.InstanceId == item.InstanceId);
		int consumed = 0;
		inventory.ItemConsumed += _ => consumed++;
		AssertBool(controller.TryUse(carried).Success).IsTrue();
		AssertInt(carried.Quantity).IsEqual(1);
		AssertInt(consumed).IsEqual(1);
		AssertBool(controller.TryDrop(carried, 1).Success).IsTrue();
		AssertBool(inventory.Items.Contains(carried)).IsFalse();
		AssertBool(controller.TryUse(carried).Success).IsFalse();
		AssertInt(consumed).IsEqual(1);

		// Food applies its recovery and contamination exactly once through the same use path.
		var apple = (FoodItem)ItemFactory.Create(GD.Load<Item>("res://scenes/items/food/food/food_apple_red.tres"));
		apple.RestorePoison(true);
		AssertBool(controller.PickupItem(apple)).IsTrue();
		var foodSlot = inventory.Items.First(e => e.Item == apple);
		bool callbackWasGuarded = false;
		inventory.ItemConsumed += _ => callbackWasGuarded = !controller.TryUse(foodSlot).Success;
		AssertBool(controller.TryUse(foodSlot).Success).IsTrue();
		AssertBool(callbackWasGuarded).IsTrue();
		AssertInt(player.BuffController.ActiveBuffs.Count(b => b.Buff is PoisonBuff)).IsEqual(1);
		AssertBool(controller.TryUse(foodSlot).Success).IsFalse();
		AssertInt(player.BuffController.ActiveBuffs.Count(b => b.Buff is PoisonBuff)).IsEqual(1);

		// Missing world preparation fails before touching ownership.
		var scene = controller.LootableItemScene;
		controller.LootableItemScene = null;
		var protectedEntry = inventory.Items[0];
		AssertBool(controller.TryDrop(protectedEntry, 1).Success).IsFalse();
		AssertBool(inventory.Items.Contains(protectedEntry)).IsTrue();
		controller.LootableItemScene = scene;

		// Chest activation must stay claimed and the exact surviving drop must reappear.
		var chest = level.FindChildren("*", "", true, false).OfType<Chest>().FirstOrDefault();
		AssertObject(chest).IsNotNull();
		chest.GetNode<InteractiveComponent>("InteractiveComponent").Interact(player);
		string chestSource = chest.GetNode<LootTableComponent>("LootTableComponent").ResolvedSourceId;
		var delta = level.ItemPersistence.Capture();
		AssertBool(delta.AddedItems.Any(e => e.Item.InstanceId == item.InstanceId)).IsTrue();
		delta = JsonSerializer.Deserialize<LevelItemDeltaSaveData>(JsonSerializer.Serialize(delta));
		level.Generate();
		level.ItemPersistence.Apply(delta);
		var restoredChest = level.FindChildren("*", "", true, false).OfType<Chest>().First(c =>
			c.GetNode<LootTableComponent>("LootTableComponent").ResolvedSourceId == chestSource);
		AssertBool(restoredChest.IsOpen).IsTrue();
		var restored = level.ItemPersistence.Capture();
		AssertString(JsonSerializer.Serialize(restored)).IsEqual(JsonSerializer.Serialize(delta));
		var ground = level.FindChildren("*", "", true, false).OfType<LootableItem>()
			.Where(p => !p.IsQueuedForDeletion() && p.Item.InstanceId == item.InstanceId).ToArray();
		AssertInt(ground.Length).IsEqual(1);
		AssertInt(ground[0].Quantity).IsEqual(1);
		restoredChest.GetNode<LootTableComponent>("LootTableComponent").DropLoot();
		AssertString(JsonSerializer.Serialize(level.ItemPersistence.Capture())).IsEqual(JsonSerializer.Serialize(restored));
		await runner.SimulateFrames(1);
	}
}