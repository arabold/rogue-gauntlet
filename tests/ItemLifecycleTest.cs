namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Ownership, stack limits, hidden state, and exact current-version item restoration.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ItemLifecycleTest
{
	private const string Apple = "res://scenes/items/food/food/food_apple_red.tres";
	private const string Potion = "res://scenes/items/potions/potion_healing_small.tres";
	private const string Ring = "res://scenes/items/jewelry/ring_strength.tres";

	[TestCase]
	public void FixedCreationDoesNotRollAndFoodPiecesKeepIndependentState()
	{
		var definition = GD.Load<FoodItem>(Apple);
		definition.PoisonChance = 1;
		using var rng = GenerationRandom.Create(42, "test");
		var policy = GD.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
		var poisoned = (FoodItem)ItemFactory.Create(definition, policy, 1, rng);
		var wholesome = (FoodItem)ItemFactory.Create(definition);
		AssertBool(poisoned.IsPoisoned).IsTrue();
		AssertBool(wholesome.IsPoisoned || definition.IsPoisoned).IsFalse();
		AssertBool(poisoned.CanStack).IsFalse();
		var copied = (FoodItem)ItemFactory.CopyDefinition(poisoned);
		AssertBool(copied.IsPoisoned).IsTrue();
		AssertBool(copied.InstanceId != poisoned.InstanceId).IsTrue();
		var restored = (FoodItem)ItemSaveCodec.Restore(ItemSaveCodec.Capture(poisoned, 1));
		AssertBool(restored.IsPoisoned).IsTrue();
		AssertString(restored.InstanceId).IsEqual(poisoned.InstanceId);
		AssertBool(ItemFactory.CreateBatch(definition, 3).Select(e => e.Item.InstanceId).Distinct().Count() == 3).IsTrue();
		definition.PoisonChance = 0.15f;
	}

	[TestCase]
	public void FullBackpackAcceptsMergeButRejectsOverflowWithoutChangingOwnership()
	{
		var definition = GD.Load<Item>(Potion);
		var inventory = new Inventory { Capacity = 1 };
		AssertBool(inventory.TryAdd(ItemFactory.Create(definition), 18).Success).IsTrue();
		AssertBool(inventory.TryAdd(ItemFactory.Create(definition), 2).Success).IsTrue();
		AssertInt(inventory.Items[0].Quantity).IsEqual(20);
		AssertBool(inventory.TryAdd(ItemFactory.Create(definition), 1).Success).IsFalse();
		AssertInt(inventory.Items[0].Quantity).IsEqual(20);
		AssertBool(inventory.TrySplit(inventory.Items[0], 10).Success).IsFalse();
		inventory.Capacity = 2;
		AssertBool(inventory.TrySplit(inventory.Items[0], 10).Success).IsTrue();
		AssertBool(inventory.Items[0].Item.InstanceId != inventory.Items[1].Item.InstanceId).IsTrue();
		AssertBool(inventory.TryMerge(inventory.Items[1], inventory.Items[0]).Success).IsTrue();
		AssertInt(inventory.Items.Count).IsEqual(1);
		AssertInt(inventory.Items[0].Quantity).IsEqual(20);
	}

	[TestCase]
	public void DifferentVarietiesCannotMergeAndNonstackingBatchesExpand()
	{
		var inventory = new Inventory { Capacity = 3 };
		inventory.TryAdd(ItemFactory.Create(GD.Load<Item>(Potion)), 1);
		inventory.TryAdd(ItemFactory.Create(GD.Load<Item>("res://scenes/items/potions/potion_poison_small.tres")), 1);
		AssertBool(inventory.TryMerge(inventory.Items[0], inventory.Items[1]).Success).IsFalse();
		AssertInt(inventory.Items.Count).IsEqual(2);
		var batch = ItemFactory.CreateBatch(GD.Load<Item>(Apple), 3);
		AssertInt(batch.Count).IsEqual(3);
		AssertBool(batch.All(entry => entry.Quantity == 1)).IsTrue();
		var stacks = ItemFactory.CreateBatch(GD.Load<Item>(Potion), 41);
		AssertInt(stacks.Count).IsEqual(3);
		AssertInt(stacks.Sum(entry => entry.Quantity)).IsEqual(41);
	}

	[TestCase]
	public void EquipmentCopiesAndInventoryCodecPreserveRollsWithoutSharingModifiers()
	{
		var definition = GD.Load<EquipableItem>(Ring);
		var item = (EquipableItem)ItemFactory.Create(definition);
		item.Rarity = EquipableItemRarity.Rare;
		item.Affixes.Add(new RolledAffix { NameFragment = "Test", Modifiers = [new StatModifier { Value = 7 }] });
		var copy = (EquipableItem)ItemFactory.CopyDefinition(item);
		copy.Affixes[0].Modifiers[0].Value = 99;
		AssertFloat(item.Affixes[0].Modifiers[0].Value).IsEqual(7);
		AssertInt(definition.Affixes.Count).IsEqual(0);
		var inventory = new Inventory { Capacity = 23 };
		inventory.TryAdd(item);
		inventory.Equip(inventory.Items[0]);
		var data = InventorySaveCodec.Capture(inventory);
		data = JsonSerializer.Deserialize<InventorySaveData>(JsonSerializer.Serialize(data));
		var restored = InventorySaveCodec.Restore(data);
		AssertInt(restored.Capacity).IsEqual(23);
		var restoredItem = (EquipableItem)restored.Items[0].Item;
		AssertString(restoredItem.InstanceId).IsEqual(item.InstanceId);
		AssertInt((int)restoredItem.Rarity).IsEqual((int)item.Rarity);
		AssertFloat(restoredItem.Affixes[0].Modifiers[0].Value).IsEqual(7);
		AssertString(data.EquippedItems[0].ItemInstanceId).IsEqual(item.InstanceId);
	}

	[TestCase]
	public void CodecRejectsDuplicateIdentityInvalidQuantityAndMissingFoodState()
	{
		var item = ItemFactory.Create(GD.Load<Item>(Potion));
		var record = ItemSaveCodec.Capture(item, 1);
		var data = new InventorySaveData { Items = [record, record] };
		bool rejected = false;
		try { InventorySaveCodec.Restore(data); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		record.Quantity = 21;
		rejected = false;
		try { ItemSaveCodec.Restore(record); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		var food = ItemSaveCodec.Capture(ItemFactory.Create(GD.Load<Item>(Apple)), 1);
		food.Food = null;
		rejected = false;
		try { ItemSaveCodec.Restore(food); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
	}
}